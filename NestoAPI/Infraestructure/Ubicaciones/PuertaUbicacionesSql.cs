using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Ubicaciones
{
    /// <summary>
    /// NestoAPI#594 (corte 1): la puerta única de Ubicaciones sobre SQL parametrizado. No abre transacción: va dentro de la
    /// del servicio que la llama (mismo NVEntities). Las filas que se van a tocar se leen con UPDLOCK para que nadie las
    /// cambie entre la lectura y la escritura. Usuario de auditoría explícito (el default de la columna sería la cuenta de
    /// máquina de la API). Cada cambio pasa por <see cref="Registrar"/>.
    /// </summary>
    public class PuertaUbicacionesSql : IPuertaUbicaciones
    {
        private readonly NVEntities db;
        private readonly IRegistroMovimientosUbicacion registro;
        private readonly Func<DateTime> ahora;

        public PuertaUbicacionesSql(NVEntities db, IRegistroMovimientosUbicacion registro = null, Func<DateTime> ahora = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            // Corte 2: a la tabla UbicacionesMovimientos, en la misma transacción (detrás del interruptor Ubicaciones:RegistrarMovimientos)
            this.registro = registro ?? new RegistroMovimientosUbicacionSql(db);
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        // ---------------------------------------------------------------- SQL

        internal const string SQL_FIFO = @"
SELECT CAST(ISNULL(FIFO, 1) AS bit) FROM Almacenes WHERE Empresa = @p0 AND [Número] = @p1";

        // prdUbicarReposicion: ubicaciones where almacen=@almacen and numero=@producto and (empresa=@empresa or empresa=@espejo)
        //                      and (estado=0 or estado=2)
        internal const string SQL_LIBRES_PARA_RESERVA = @"
SELECT [NºOrden] AS NumeroOrden, RTRIM(Empresa) AS Empresa, RTRIM([Almacén]) AS Almacen, RTRIM([Número]) AS Producto,
       RTRIM(Pasillo) AS Pasillo, RTRIM(Fila) AS Fila, RTRIM(Columna) AS Columna, Cantidad, ISNULL(Estado, 0) AS Estado,
       FechaCreación AS FechaCreacion
FROM Ubicaciones WITH (UPDLOCK, HOLDLOCK)
WHERE [Almacén] = @p2 AND [Número] = @p3 AND (Empresa = @p0 OR Empresa = @p1) AND Estado IN (0, 2) AND Cantidad > 0";

        // prdUbicarReposicion, «vamos a dar toda esta ubicación»: update ubicaciones set NºOrdenRepo=@nºOrdenRepo, estado=4
        internal const string SQL_RESERVAR_FILA_ENTERA = @"
UPDATE Ubicaciones SET Estado = 4, [NºOrdenRepo] = @p1, Usuario = @p2
WHERE [NºOrden] = @p0 AND Estado IN (0, 2) AND Cantidad = @p3";

        // prdUbicarReposicion, «vamos a dar menos de la ubicación»: update … set cantidad=cantidad-@CantidadPendiente
        internal const string SQL_RESTAR_DE_LIBRE = @"
UPDATE Ubicaciones SET Cantidad = Cantidad - @p1, Usuario = @p2
WHERE [NºOrden] = @p0 AND Estado IN (0, 2) AND Cantidad > @p1";

        // … e insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,estado,nºordenrepo) select … from
        // ubicaciones where nºorden=@nºordenUbicacion. SCOPE_IDENTITY y no OUTPUT: la tabla tiene triggers.
        internal const string SQL_INSERTAR_RESERVA = @"
INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Pasillo, Fila, Columna, Estado, [NºOrdenRepo], Usuario)
SELECT Empresa, [Almacén], [Número], @p1, Pasillo, Fila, Columna, 4, @p2, @p3 FROM Ubicaciones WHERE [NºOrden] = @p0;
SELECT CAST(SCOPE_IDENTITY() AS int);";

        internal const string COLUMNAS_FILA = @"
SELECT [NºOrden] AS NumeroOrden, RTRIM(Empresa) AS Empresa, RTRIM([Almacén]) AS Almacen, RTRIM([Número]) AS Producto,
       RTRIM(Pasillo) AS Pasillo, RTRIM(Fila) AS Fila, RTRIM(Columna) AS Columna, Cantidad, ISNULL(Estado, 0) AS Estado,
       FechaCreación AS FechaCreacion, [NºOrdenRepo] AS NumeroOrdenRepo
FROM Ubicaciones WITH (UPDLOCK)";

        // La reserva de una línea de ENTRADA (NºOrdenRepo = su [Nº Orden] en PreExtrProducto)
        internal const string SQL_RESERVAS_DE_LA_LINEA = COLUMNAS_FILA + @"
WHERE [NºOrdenRepo] = @p0 AND Estado = 4";

        internal const string SQL_RESERVAS_DE_LAS_LINEAS = COLUMNAS_FILA + @"
WHERE Estado = 4 AND [NºOrdenRepo] IN ({0})";

        // Nesto viejo al terminar: update ubicaciones set cantidad=-1*u.cantidad, estado=-4, nºtraspasorepo=N from ubicaciones u
        //   inner join preextrproducto p on u.nºordenrepo=p.[nº orden] where u.estado=4 and p.diario=… and p.almacen=destino
        internal const string SQL_SALIDA_DE_REPOSICION = @"
UPDATE Ubicaciones SET Cantidad = -Cantidad, Estado = -4, [NºTraspasoRepo] = @p0, Usuario = @p1
WHERE Estado = 4 AND [NºOrdenRepo] IN ({0})";

        // NestoAPI#651: al cerrar sin contabilizar (Algete), la reserva sigue viva (4, positiva) y se enlaza al traspaso
        internal const string SQL_ENLAZAR_RESERVAS_AL_TRASPASO = @"
UPDATE Ubicaciones SET [NºTraspasoRepo] = @p0, Usuario = @p1
WHERE Estado = 4 AND [NºOrdenRepo] IN ({0})";

        // NestoAPI#651: las reservas enlazadas a un traspaso cuya salida aún no se ha contabilizado
        internal const string SQL_RESERVAS_DEL_TRASPASO = COLUMNAS_FILA + @"
WHERE [NºTraspasoRepo] = @p0 AND Estado = 4 AND [Almacén] = @p1";

        // NestoAPI#651: al contabilizar la salida (Ariadna, terminar la recogida), las reservas del traspaso salen (4 → -4)
        internal const string SQL_SALIDA_DE_REPOSICION_DEL_TRASPASO = @"
UPDATE Ubicaciones SET Cantidad = -Cantidad, Estado = -4, Usuario = @p2
WHERE [NºTraspasoRepo] = @p0 AND Estado = 4 AND [Almacén] = @p1";

        // Lo reservado en los huecos del origen para un traspaso aún no recogido: en 4 (NestoAPI#651) o, si se cerró antes
        // de #651, ya en -4
        internal const string SQL_SALIDAS_DEL_TRASPASO = COLUMNAS_FILA + @"
WHERE [NºTraspasoRepo] = @p0 AND Estado IN (4, -4) AND [Almacén] = @p1";

        // prdDeshacerUbicacionReposicion: si en el mismo hueco hay una fila libre (estado 0, sin pedido ni albarán), se le suma…
        internal const string SQL_SUMAR_A_LIBRE_DEL_HUECO = @"
UPDATE TOP (1) Ubicaciones SET Cantidad = Cantidad + @p1, Usuario = @p8
WHERE Empresa = @p2 AND [Almacén] = @p3 AND [Número] = @p4 AND Pasillo = @p5 AND Fila = @p6 AND Columna = @p7
  AND Estado = 0 AND PedidoVta IS NULL AND [AlbaránVta] IS NULL AND [NºOrden] <> @p0";

        // … y se borra la reserva
        internal const string SQL_BORRAR_FILA = @"
DELETE FROM Ubicaciones WHERE [NºOrden] = @p0";

        // … si no, la propia reserva vuelve a libre (0) o, sin pasillo, a pendiente de ubicar (2). A diferencia del
        // procedimiento, se limpian también NºOrdenRepo y NºTraspasoRepo: ya no es de ninguna reposición.
        internal const string SQL_DEVOLVER_FILA_A_LIBRE = @"
UPDATE Ubicaciones SET Cantidad = @p1, Estado = CASE WHEN Pasillo IS NULL THEN 2 ELSE 0 END,
       PedidoVta = NULL, [AlbaránVta] = NULL, [NºOrdenVta] = NULL, [NºOrdenRepo] = NULL, [NºTraspasoRepo] = NULL, Usuario = @p2
WHERE [NºOrden] = @p0";

        // NestoAPI#634: lo libre y sin reservar del producto en el almacén, y si está en el hueco del que salió el traspaso
        internal const string SQL_FILAS_PARA_DIFERENCIA = @"
SELECT u.[NºOrden] AS NumeroOrden, RTRIM(u.Empresa) AS Empresa, RTRIM(u.[Almacén]) AS Almacen, RTRIM(u.[Número]) AS Producto,
       RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Fila) AS Fila, RTRIM(u.Columna) AS Columna, u.Cantidad, ISNULL(u.Estado, 0) AS Estado,
       u.FechaCreación AS FechaCreacion,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM Ubicaciones s
                              WHERE s.[NºTraspasoRepo] = @p4 AND s.Estado IN (4, -4) AND s.[Almacén] = u.[Almacén] AND s.[Número] = u.[Número]
                                AND s.Pasillo = u.Pasillo AND s.Fila = u.Fila AND s.Columna = u.Columna) THEN 1 ELSE 0 END AS bit) AS DelTraspaso
FROM Ubicaciones u WITH (UPDLOCK, HOLDLOCK)
WHERE u.[Almacén] = @p2 AND u.[Número] = @p3 AND (u.Empresa = @p0 OR u.Empresa = @p1) AND u.Estado IN (0, 2) AND u.Cantidad > 0
  AND u.PedidoVta IS NULL AND u.[AlbaránVta] IS NULL AND u.[NºOrdenVta] IS NULL AND u.[NºOrdenRepo] IS NULL";

        // Se coge la fila entera: sobra
        internal const string SQL_QUITAR_FILA_ENTERA = @"
DELETE FROM Ubicaciones WHERE [NºOrden] = @p0 AND Cantidad = @p1 AND Estado IN (0, 2)";

        // La misma fila que pone prdExtrProducto con un diario que no es el de entrada de reposiciones
        internal const string SQL_ENTRADA_PENDIENTE_DE_UBICAR = @"
INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Estado, Usuario) VALUES (@p0, @p1, @p2, @p3, 2, @p4);
SELECT CAST(SCOPE_IDENTITY() AS int);";

        // ---- Ariadna#12: cambiar de hueco la reserva de una reposición (CambiarHuecoReservaReposicion) ----

        // Lo reservado para el traspaso en el hueco donde no estaba (la más nueva primero)
        internal const string SQL_RESERVAS_DEL_TRASPASO_EN_EL_HUECO = COLUMNAS_FILA + @"
WHERE [NºTraspasoRepo] = @p0 AND Estado = 4 AND [Almacén] = @p1 AND [Número] = @p2 AND Cantidad > 0
  AND RTRIM(Pasillo) + RTRIM(Fila) + RTRIM(Columna) = @p3
ORDER BY [NºOrden] DESC";

        // Lo libre del hueco de donde se va a coger: lo mismo que se ofrece como alternativa (estado 0, sin pedido, albarán ni
        // reserva), bloqueado hasta el final de la transacción. La más antigua primero, como al reservar con FIFO.
        internal const string SQL_LIBRES_DEL_HUECO = @"
SELECT [NºOrden] AS NumeroOrden, RTRIM(Empresa) AS Empresa, RTRIM([Almacén]) AS Almacen, RTRIM([Número]) AS Producto,
       RTRIM(Pasillo) AS Pasillo, RTRIM(Fila) AS Fila, RTRIM(Columna) AS Columna, Cantidad, ISNULL(Estado, 0) AS Estado,
       FechaCreación AS FechaCreacion
FROM Ubicaciones WITH (UPDLOCK, HOLDLOCK)
WHERE Empresa = @p0 AND [Almacén] = @p1 AND [Número] = @p2 AND Estado = 0 AND Cantidad > 0
  AND PedidoVta IS NULL AND [AlbaránVta] IS NULL AND [NºOrdenVta] IS NULL AND [NºOrdenRepo] IS NULL
  AND RTRIM(Pasillo) + RTRIM(Fila) + RTRIM(Columna) = @p3
ORDER BY FechaCreación, [NºOrden]";

        // En el destino: se resta de la libre y se inserta la reserva (4) en su mismo hueco, con la línea de entrada
        // (NºOrdenRepo) y el traspaso de la reserva de origen; si la libre se queda a cero, sobra. Devuelve el NºOrden de la
        // reserva nueva, o NULL si la libre ya no tenía esas unidades (entonces no se ha tocado nada). SCOPE_IDENTITY y no
        // OUTPUT: la tabla tiene triggers.
        internal const string SQL_RESERVA_DE_LA_LIBRE_PARA_EL_TRASPASO = @"
DECLARE @nueva int = NULL;
IF EXISTS (SELECT 1 FROM Ubicaciones WHERE [NºOrden] = @p0 AND Estado = 0 AND Cantidad >= @p1)
BEGIN
    UPDATE Ubicaciones SET Cantidad = Cantidad - @p1, Usuario = @p4 WHERE [NºOrden] = @p0;
    INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Pasillo, Fila, Columna, Estado, [NºOrdenRepo], [NºTraspasoRepo], Usuario)
    SELECT Empresa, [Almacén], [Número], @p1, Pasillo, Fila, Columna, 4, @p2, @p3, @p4 FROM Ubicaciones WHERE [NºOrden] = @p0;
    SET @nueva = CAST(SCOPE_IDENTITY() AS int);
    DELETE FROM Ubicaciones WHERE [NºOrden] = @p0 AND Cantidad = 0 AND Estado = 0;
END
SELECT @nueva;";

        // En el origen: lo que no estaba sale de la reserva y queda «pendiente de ubicar» (2, sin hueco), como una falta
        // (EscriturasSalida.SQL_REGISTRO_REPOSICION_A_PENDIENTE_DE_UBICAR). 1 si se ha hecho; 0 si la reserva ya no tenía esas unidades.
        internal const string SQL_RESERVA_REPOSICION_A_PENDIENTE_DE_UBICAR = @"
IF EXISTS (SELECT 1 FROM Ubicaciones WHERE [NºOrden] = @p0 AND Estado = 4 AND Cantidad >= @p1)
BEGIN
    INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Estado, Usuario)
    SELECT Empresa, [Almacén], [Número], @p1, 2, @p2 FROM Ubicaciones WHERE [NºOrden] = @p0;
    UPDATE Ubicaciones SET Cantidad = Cantidad - @p1, Usuario = @p2 WHERE [NºOrden] = @p0;
    DELETE FROM Ubicaciones WHERE [NºOrden] = @p0 AND Cantidad = 0 AND Estado = 4;
    SELECT 1;
END
ELSE
    SELECT 0;";

        // ---------------------------------------------------------------- Registro

        /// <summary>El punto ÚNICO por donde pasa cada cambio de Ubicaciones. Corte 2: va a UbicacionesMovimientos (<see cref="RegistroMovimientosUbicacionSql"/>).</summary>
        public void Registrar(MovimientoUbicacion movimiento)
        {
            if (movimiento == null)
            {
                return;
            }
            if (movimiento.Momento == default)
            {
                movimiento.Momento = ahora();
            }
            registro.Registrar(movimiento);
        }

        // ---------------------------------------------------------------- Reposiciones

        public async Task<ResumenUbicaciones> ReservarParaReposicion(string empresa, string espejo, string almacenOrigen,
            IReadOnlyList<LineaReservaReposicion> lineas, string usuario)
        {
            var resumen = new ResumenUbicaciones();
            if (lineas == null || lineas.Count == 0)
            {
                return resumen;
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            bool fifo = (await db.Database.SqlQuery<bool?>(SQL_FIFO, Char("@p0", empresa, 3), Char("@p1", almacenOrigen, 3))
                .FirstOrDefaultAsync().ConfigureAwait(false)) ?? true;

            foreach (IGrouping<string, LineaReservaReposicion> producto in lineas.Where(l => l != null && l.Cantidad > 0)
                .GroupBy(l => l.Producto?.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                List<FilaUbicacionLibre> libres = await db.Database.SqlQuery<FilaUbicacionLibre>(SQL_LIBRES_PARA_RESERVA,
                    Char("@p0", empresa, 3), Char("@p1", espejo ?? empresa, 3), Char("@p2", almacenOrigen, 3), Char("@p3", producto.Key, 15))
                    .ToListAsync().ConfigureAwait(false);

                foreach (LineaReservaReposicion linea in producto)
                {
                    var reserva = new ReservaLineaReposicion { NumeroOrdenEntrada = linea.NumeroOrdenEntrada, Producto = producto.Key, Pedida = linea.Cantidad };
                    List<TomaDeHueco> tomas = PlanificadorReservaHuecos.Repartir(libres, linea.Cantidad, fifo);
                    foreach (TomaDeHueco toma in tomas)
                    {
                        int fila = await Tomar(toma, linea.NumeroOrdenEntrada, quien).ConfigureAwait(false);
                        resumen.FilasTocadas += toma.Entera ? 1 : 2;
                        reserva.Piezas.Add(new PiezaReservada { NumeroOrdenUbicacion = fila, Hueco = toma.Libre.Hueco, Cantidad = toma.Cantidad });
                        Apuntar(resumen, new MovimientoUbicacion
                        {
                            Tipo = TipoMovimientoUbicacion.ReservarParaReposicion,
                            Empresa = toma.Libre.Empresa, Almacen = almacenOrigen, Producto = producto.Key, NumeroOrdenUbicacion = fila,
                            HuecoOrigen = toma.Libre.Hueco, HuecoDestino = toma.Libre.Hueco, Cantidad = toma.Cantidad,
                            EstadoAnterior = toma.Libre.Estado, EstadoNuevo = EstadosUbicacion.RESERVA_REPOSICION,
                            Documento = DocumentoUbicacion.De(TipoDocumentoUbicacion.LineaReposicion, linea.NumeroOrdenEntrada), Usuario = quien
                        });
                    }
                    libres = PlanificadorReservaHuecos.Descontar(libres, tomas);
                    resumen.Reservas.Add(reserva);
                }
            }
            return resumen;
        }

        public async Task<ResumenUbicaciones> LiberarReservaReposicion(string empresa, int numeroOrdenEntrada, string usuario)
        {
            List<FilaUbicacionReposicion> reservas = await db.Database.SqlQuery<FilaUbicacionReposicion>(SQL_RESERVAS_DE_LA_LINEA,
                new SqlParameter("@p0", SqlDbType.Int) { Value = numeroOrdenEntrada }).ToListAsync().ConfigureAwait(false);
            return await DevolverAlHueco(reservas, TipoMovimientoUbicacion.LiberarReservaReposicion,
                DocumentoUbicacion.De(TipoDocumentoUbicacion.LineaReposicion, numeroOrdenEntrada), usuario).ConfigureAwait(false);
        }

        public async Task<ResumenUbicaciones> SalidaDeReposicion(string empresa, IReadOnlyList<int> numerosOrdenEntrada, int numeroTraspaso, string usuario)
        {
            var resumen = new ResumenUbicaciones();
            List<int> lineas = (numerosOrdenEntrada ?? new List<int>()).Distinct().ToList();
            if (lineas.Count == 0)
            {
                return resumen;
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            List<SqlParameter> enLista = ListaDeLineas(lineas, out string lista);
            List<FilaUbicacionReposicion> reservas = await db.Database.SqlQuery<FilaUbicacionReposicion>(
                string.Format(SQL_RESERVAS_DE_LAS_LINEAS, lista), enLista.ToArray()).ToListAsync().ConfigureAwait(false);
            if (reservas.Count == 0)
            {
                return resumen;
            }

            var parametros = new List<SqlParameter>
            {
                new SqlParameter("@p0", SqlDbType.Int) { Value = numeroTraspaso },
                new SqlParameter("@p1", SqlDbType.VarChar, 30) { Value = quien }
            };
            parametros.AddRange(ListaDeLineas(lineas, out _));
            int cambiadas = await db.Database.ExecuteSqlCommandAsync(string.Format(SQL_SALIDA_DE_REPOSICION, lista), parametros.ToArray()).ConfigureAwait(false);
            if (cambiadas != reservas.Count)
            {
                throw new NestoBusinessException($"Al sacar de los huecos el traspaso {numeroTraspaso} se han cambiado {cambiadas} reservas y había {reservas.Count}. " +
                    "No se ha hecho nada.");
            }
            resumen.FilasTocadas = cambiadas;
            foreach (FilaUbicacionReposicion reserva in reservas)
            {
                Apuntar(resumen, new MovimientoUbicacion
                {
                    Tipo = TipoMovimientoUbicacion.SalidaDeReposicion,
                    Empresa = reserva.Empresa, Almacen = reserva.Almacen, Producto = reserva.Producto, NumeroOrdenUbicacion = reserva.NumeroOrden,
                    HuecoOrigen = reserva.Hueco, Cantidad = reserva.Cantidad,
                    EstadoAnterior = EstadosUbicacion.RESERVA_REPOSICION, EstadoNuevo = EstadosUbicacion.SALIDA_REPOSICION,
                    Documento = DocumentoUbicacion.De(TipoDocumentoUbicacion.Traspaso, numeroTraspaso), Usuario = quien
                });
            }
            return resumen;
        }

        public async Task<ResumenUbicaciones> EnlazarReservasAlTraspaso(string empresa, IReadOnlyList<int> numerosOrdenEntrada, int numeroTraspaso, string usuario)
        {
            var resumen = new ResumenUbicaciones();
            List<int> lineas = (numerosOrdenEntrada ?? new List<int>()).Distinct().ToList();
            if (lineas.Count == 0)
            {
                return resumen;
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            List<SqlParameter> enLista = ListaDeLineas(lineas, out string lista);
            List<FilaUbicacionReposicion> reservas = await db.Database.SqlQuery<FilaUbicacionReposicion>(
                string.Format(SQL_RESERVAS_DE_LAS_LINEAS, lista), enLista.ToArray()).ToListAsync().ConfigureAwait(false);
            if (reservas.Count == 0)
            {
                return resumen;
            }

            var parametros = new List<SqlParameter>
            {
                new SqlParameter("@p0", SqlDbType.Int) { Value = numeroTraspaso },
                new SqlParameter("@p1", SqlDbType.VarChar, 30) { Value = quien }
            };
            parametros.AddRange(ListaDeLineas(lineas, out _));
            int enlazadas = await db.Database.ExecuteSqlCommandAsync(string.Format(SQL_ENLAZAR_RESERVAS_AL_TRASPASO, lista), parametros.ToArray()).ConfigureAwait(false);
            if (enlazadas != reservas.Count)
            {
                throw new NestoBusinessException($"Al enlazar las reservas al traspaso {numeroTraspaso} se han cambiado {enlazadas} y había {reservas.Count}. " +
                    "No se ha hecho nada.");
            }
            resumen.FilasTocadas = enlazadas;
            foreach (FilaUbicacionReposicion reserva in reservas)
            {
                Apuntar(resumen, new MovimientoUbicacion
                {
                    Tipo = TipoMovimientoUbicacion.EnlazarReservaAlTraspaso,
                    Empresa = reserva.Empresa, Almacen = reserva.Almacen, Producto = reserva.Producto, NumeroOrdenUbicacion = reserva.NumeroOrden,
                    HuecoOrigen = reserva.Hueco, HuecoDestino = reserva.Hueco, Cantidad = reserva.Cantidad,
                    EstadoAnterior = EstadosUbicacion.RESERVA_REPOSICION, EstadoNuevo = EstadosUbicacion.RESERVA_REPOSICION,
                    Documento = DocumentoUbicacion.De(TipoDocumentoUbicacion.Traspaso, numeroTraspaso), Usuario = quien
                });
            }
            return resumen;
        }

        public async Task<ResumenUbicaciones> SalidaDeReposicionDelTraspaso(string empresa, string almacenOrigen, int numeroTraspaso, string usuario)
        {
            var resumen = new ResumenUbicaciones();
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            List<FilaUbicacionReposicion> reservas = await db.Database.SqlQuery<FilaUbicacionReposicion>(SQL_RESERVAS_DEL_TRASPASO,
                new SqlParameter("@p0", SqlDbType.Int) { Value = numeroTraspaso }, Char("@p1", almacenOrigen, 3)).ToListAsync().ConfigureAwait(false);
            if (reservas.Count == 0)
            {
                // Cerrado antes de NestoAPI#651 (ya en -4), sin nada reservado o de un almacén sin control de ubicaciones
                return resumen;
            }
            int cambiadas = await db.Database.ExecuteSqlCommandAsync(SQL_SALIDA_DE_REPOSICION_DEL_TRASPASO,
                new SqlParameter("@p0", SqlDbType.Int) { Value = numeroTraspaso }, Char("@p1", almacenOrigen, 3),
                new SqlParameter("@p2", SqlDbType.VarChar, 30) { Value = quien }).ConfigureAwait(false);
            if (cambiadas != reservas.Count)
            {
                throw new NestoBusinessException($"Al sacar de los huecos el traspaso {numeroTraspaso} se han cambiado {cambiadas} reservas y había {reservas.Count}. " +
                    "No se ha hecho nada.");
            }
            resumen.FilasTocadas = cambiadas;
            foreach (FilaUbicacionReposicion reserva in reservas)
            {
                Apuntar(resumen, new MovimientoUbicacion
                {
                    Tipo = TipoMovimientoUbicacion.SalidaDeReposicion,
                    Empresa = reserva.Empresa, Almacen = reserva.Almacen, Producto = reserva.Producto, NumeroOrdenUbicacion = reserva.NumeroOrden,
                    HuecoOrigen = reserva.Hueco, Cantidad = reserva.Cantidad,
                    EstadoAnterior = EstadosUbicacion.RESERVA_REPOSICION, EstadoNuevo = EstadosUbicacion.SALIDA_REPOSICION,
                    Documento = DocumentoUbicacion.De(TipoDocumentoUbicacion.Traspaso, numeroTraspaso), Usuario = quien
                });
            }
            return resumen;
        }

        public async Task<ResumenUbicaciones> AnularSalidaDeReposicion(string empresa, string almacenOrigen, int numeroTraspaso, string usuario)
        {
            List<FilaUbicacionReposicion> salidas = await db.Database.SqlQuery<FilaUbicacionReposicion>(SQL_SALIDAS_DEL_TRASPASO,
                new SqlParameter("@p0", SqlDbType.Int) { Value = numeroTraspaso }, Char("@p1", almacenOrigen, 3)).ToListAsync().ConfigureAwait(false);
            return await DevolverAlHueco(salidas, TipoMovimientoUbicacion.AnularSalidaDeReposicion,
                DocumentoUbicacion.De(TipoDocumentoUbicacion.Traspaso, numeroTraspaso), usuario).ConfigureAwait(false);
        }

        public async Task<ResumenUbicaciones> CambiarHuecoReservaReposicion(string empresa, string almacen, int numeroTraspaso, string producto,
            HuecoUbicacion origen, HuecoUbicacion destino, int cantidad, string usuario)
        {
            if (origen?.TieneHueco != true || destino?.TieneHueco != true || origen.Codigo == destino.Codigo || cantidad <= 0)
            {
                throw new NestoBusinessException("Para cambiar de hueco hacen falta dos huecos distintos y una cantidad mayor que cero.");
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            string articulo = producto?.Trim();

            List<FilaUbicacionReposicion> reservas = await db.Database.SqlQuery<FilaUbicacionReposicion>(SQL_RESERVAS_DEL_TRASPASO_EN_EL_HUECO,
                new SqlParameter("@p0", SqlDbType.Int) { Value = numeroTraspaso }, Char("@p1", almacen, 3), Char("@p2", articulo, 15),
                new SqlParameter("@p3", SqlDbType.VarChar, 9) { Value = origen.Codigo }).ToListAsync().ConfigureAwait(false);
            List<FilaUbicacionLibre> libres = await db.Database.SqlQuery<FilaUbicacionLibre>(SQL_LIBRES_DEL_HUECO,
                Char("@p0", empresa, 3), Char("@p1", almacen, 3), Char("@p2", articulo, 15),
                new SqlParameter("@p3", SqlDbType.VarChar, 9) { Value = destino.Codigo }).ToListAsync().ConfigureAwait(false);

            List<TrasladoDeReserva> traslados = PlanificadorCambioHuecoReposicion.Planear(reservas, libres, cantidad, out string motivo);
            if (traslados == null)
            {
                throw new NestoBusinessException($"{motivo} No se ha cambiado nada.") { StatusCode = System.Net.HttpStatusCode.Conflict, IsWarning = true };
            }

            var resumen = new ResumenUbicaciones();
            foreach (TrasladoDeReserva traslado in traslados)
            {
                // Primero la reserva nueva en el destino…
                int? nueva = await db.Database.SqlQuery<int?>(SQL_RESERVA_DE_LA_LIBRE_PARA_EL_TRASPASO,
                    Orden(traslado.Libre.NumeroOrden), new SqlParameter("@p1", SqlDbType.Int) { Value = traslado.Cantidad },
                    new SqlParameter("@p2", SqlDbType.Int) { Value = (object)traslado.Reserva.NumeroOrdenRepo ?? DBNull.Value },
                    new SqlParameter("@p3", SqlDbType.Int) { Value = numeroTraspaso },
                    new SqlParameter("@p4", SqlDbType.VarChar, 30) { Value = quien }).FirstOrDefaultAsync().ConfigureAwait(false);
                if (nueva == null)
                {
                    throw HaCambiado(destino, articulo);
                }
                // … y lo que no estaba en el origen, a «pendiente de ubicar»
                int hecho = await db.Database.SqlQuery<int>(SQL_RESERVA_REPOSICION_A_PENDIENTE_DE_UBICAR,
                    Orden(traslado.Reserva.NumeroOrden), new SqlParameter("@p1", SqlDbType.Int) { Value = traslado.Cantidad },
                    new SqlParameter("@p2", SqlDbType.VarChar, 30) { Value = quien }).FirstOrDefaultAsync().ConfigureAwait(false);
                if (hecho != 1)
                {
                    throw HaCambiado(origen, articulo);
                }
                resumen.FilasTocadas += 4;
                DocumentoUbicacion traspaso = DocumentoUbicacion.De(TipoDocumentoUbicacion.Traspaso, numeroTraspaso);
                Apuntar(resumen, new MovimientoUbicacion
                {
                    Tipo = TipoMovimientoUbicacion.CambiarDeHueco,
                    Empresa = traslado.Libre.Empresa, Almacen = almacen, Producto = articulo, NumeroOrdenUbicacion = nueva,
                    HuecoOrigen = origen, HuecoDestino = destino, Cantidad = traslado.Cantidad,
                    EstadoAnterior = EstadosUbicacion.LIBRE, EstadoNuevo = EstadosUbicacion.RESERVA_REPOSICION,
                    Documento = traspaso, Usuario = quien
                });
                Apuntar(resumen, new MovimientoUbicacion
                {
                    Tipo = TipoMovimientoUbicacion.Falta,
                    Empresa = traslado.Reserva.Empresa, Almacen = almacen, Producto = articulo, NumeroOrdenUbicacion = traslado.Reserva.NumeroOrden,
                    HuecoOrigen = origen, Cantidad = traslado.Cantidad,
                    EstadoAnterior = EstadosUbicacion.RESERVA_REPOSICION, EstadoNuevo = EstadosUbicacion.PENDIENTE_DE_UBICAR,
                    Documento = traspaso, Usuario = quien
                });
            }
            return resumen;
        }

        private static NestoBusinessException HaCambiado(HuecoUbicacion hueco, string producto)
        {
            return new NestoBusinessException($"El hueco {hueco} de {producto} ha cambiado mientras se cambiaba de hueco: vuelva a intentarlo. No se ha hecho nada.")
            {
                StatusCode = System.Net.HttpStatusCode.Conflict
            };
        }

        // ---------------------------------------------------------------- Diferencias de reposición (NestoAPI#634)

        public async Task<ResumenUbicaciones> EntradaPorDiferenciaReposicion(string empresa, string almacen, string producto, int cantidad,
            int numeroTraspaso, string usuario)
        {
            var resumen = new ResumenUbicaciones();
            if (cantidad <= 0)
            {
                return resumen;
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            int? nueva = await db.Database.SqlQuery<int?>(SQL_ENTRADA_PENDIENTE_DE_UBICAR,
                Char("@p0", empresa, 3), Char("@p1", almacen, 3), Char("@p2", producto, 15),
                new SqlParameter("@p3", SqlDbType.Int) { Value = cantidad },
                new SqlParameter("@p4", SqlDbType.VarChar, 30) { Value = quien }).FirstOrDefaultAsync().ConfigureAwait(false);
            resumen.FilasTocadas = 1;
            Apuntar(resumen, new MovimientoUbicacion
            {
                Tipo = TipoMovimientoUbicacion.DiferenciaDeReposicion,
                Empresa = empresa, Almacen = almacen, Producto = producto?.Trim(), NumeroOrdenUbicacion = nueva,
                Cantidad = cantidad, EstadoNuevo = EstadosUbicacion.PENDIENTE_DE_UBICAR,
                Documento = DocumentoUbicacion.De(TipoDocumentoUbicacion.Traspaso, numeroTraspaso), Usuario = quien
            });
            return resumen;
        }

        public async Task<ResumenUbicaciones> SalidaPorDiferenciaReposicion(string empresa, string almacen, string producto, int cantidad,
            int numeroTraspaso, bool primeroPendienteDeUbicar, string usuario)
        {
            var resumen = new ResumenUbicaciones();
            if (cantidad <= 0)
            {
                return resumen;
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            List<FilaUbicacionDiferencia> filas = await db.Database.SqlQuery<FilaUbicacionDiferencia>(SQL_FILAS_PARA_DIFERENCIA,
                Char("@p0", empresa, 3), Char("@p1", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO, 3), Char("@p2", almacen, 3),
                Char("@p3", producto, 15), new SqlParameter("@p4", SqlDbType.Int) { Value = numeroTraspaso })
                .ToListAsync().ConfigureAwait(false);

            foreach (TomaDeHueco toma in PlanificadorDiferenciaUbicaciones.Repartir(filas, cantidad, primeroPendienteDeUbicar))
            {
                int tocadas = toma.Entera
                    ? await db.Database.ExecuteSqlCommandAsync(SQL_QUITAR_FILA_ENTERA, Orden(toma.Libre.NumeroOrden),
                        new SqlParameter("@p1", SqlDbType.Int) { Value = toma.Cantidad }).ConfigureAwait(false)
                    : await db.Database.ExecuteSqlCommandAsync(SQL_RESTAR_DE_LIBRE, Orden(toma.Libre.NumeroOrden),
                        new SqlParameter("@p1", SqlDbType.Int) { Value = toma.Cantidad },
                        new SqlParameter("@p2", SqlDbType.VarChar, 30) { Value = quien }).ConfigureAwait(false);
                Comprobar(tocadas, toma);
                resumen.FilasTocadas += 1;
                Apuntar(resumen, new MovimientoUbicacion
                {
                    Tipo = TipoMovimientoUbicacion.DiferenciaDeReposicion,
                    Empresa = toma.Libre.Empresa, Almacen = almacen, Producto = producto?.Trim(), NumeroOrdenUbicacion = toma.Libre.NumeroOrden,
                    HuecoOrigen = toma.Libre.Hueco, Cantidad = toma.Cantidad, EstadoAnterior = toma.Libre.Estado,
                    Documento = DocumentoUbicacion.De(TipoDocumentoUbicacion.Traspaso, numeroTraspaso), Usuario = quien
                });
            }
            return resumen;
        }

        // ---------------------------------------------------------------- Aún no pasan por la puerta (corte 3 de #594)

        public Task<ResumenUbicaciones> EntradaDeCompra(string empresa, string almacen, string producto, int cantidad, DocumentoUbicacion documento, string usuario) =>
            throw Pendiente(nameof(EntradaDeCompra), "prdCrearAlbaránCmp y prdExtrProducto (estado 2 al contabilizar la compra)");

        public Task<ResumenUbicaciones> Ubicar(string empresa, string almacen, string producto, int cantidad, HuecoUbicacion destino, string usuario) =>
            throw Pendiente(nameof(Ubicar), "prdUbicar (llamado desde PreparacionAlmacen/UbicacionesAlmacen) y Models/Picking/GestorUbicaciones");

        public Task<ResumenUbicaciones> ReservarParaPicking(string empresa, string almacen, int numeroOrdenLineaVenta, string producto, int cantidad, string usuario) =>
            throw Pendiente(nameof(ReservarParaPicking), "prdUbicacionPicking");

        public Task<ResumenUbicaciones> SoltarReservaPicking(string empresa, int numeroOrdenLineaVenta, int cantidad, string usuario) =>
            throw Pendiente(nameof(SoltarReservaPicking), "prdDeshacerUbicacionPicking, prdCambiarCantidadPicking y PreparacionAlmacen/EscriturasSalida");

        public Task<ResumenUbicaciones> SalidaDePicking(string empresa, int numeroOrdenLineaVenta, DocumentoUbicacion albaran, string usuario) =>
            throw Pendiente(nameof(SalidaDePicking), "prdCrearAlbaránVta, prdAgruparAlbaranesVta, prdAgruparOfertasPedido (y prdDeshacerExtractoProducto al deshacer)");

        public Task<ResumenUbicaciones> EntradaDeReposicion(string empresa, string almacenDestino, string producto, int cantidad, int numeroTraspaso, string usuario) =>
            throw Pendiente(nameof(EntradaDeReposicion), "PreparacionAlmacen/CierreReposiciones y prdExtrProducto");

        public Task<ResumenUbicaciones> MontarKit(string empresa, string almacen, string kit, int cantidad, string usuario) =>
            throw Pendiente(nameof(MontarKit), "prdUbicarReposicion tipo 2 (estado 5) y Kits/UbicacionService");

        public Task<ResumenUbicaciones> DesmontarKit(string empresa, string almacen, string kit, int cantidad, string usuario) =>
            throw Pendiente(nameof(DesmontarKit), "Kits/UbicacionService");

        public Task<ResumenUbicaciones> RegularizarPorInventario(string empresa, string almacen, int numeroTraspasoInventario, string usuario) =>
            throw Pendiente(nameof(RegularizarPorInventario), "prdContabilizarInventario y prdDeshacerInventario");

        public Task<ResumenUbicaciones> CambiarDeHueco(string empresa, string almacen, string producto, HuecoUbicacion origen, HuecoUbicacion destino, int cantidad, string usuario) =>
            throw Pendiente(nameof(CambiarDeHueco), "PreparacionAlmacen/CambioHuecoPicking (Ariadna#12) y Models/Picking/GestorUbicaciones");

        public Task<ResumenUbicaciones> Falta(string empresa, int numeroOrdenUbicacion, int cantidad, string usuario) =>
            throw Pendiente(nameof(Falta), "PreparacionAlmacen/EscriturasSalida (faltas de picking y de reposición en Ariadna)");

        // ---------------------------------------------------------------- privados

        private static NotImplementedException Pendiente(string operacion, string quienLoHaceHoy)
        {
            return new NotImplementedException($"NestoAPI#594: {operacion} todavía no pasa por la puerta única de Ubicaciones; hoy lo hace {quienLoHaceHoy}.");
        }

        /// <summary>Coge de una fila libre: entera (la fila pasa a reserva) o una parte (se resta y se inserta la reserva). Devuelve el NºOrden de la reserva.</summary>
        private async Task<int> Tomar(TomaDeHueco toma, int numeroOrdenEntrada, string quien)
        {
            var orden = new SqlParameter("@p0", SqlDbType.Int) { Value = toma.Libre.NumeroOrden };
            if (toma.Entera)
            {
                int reservadas = await db.Database.ExecuteSqlCommandAsync(SQL_RESERVAR_FILA_ENTERA, orden,
                    new SqlParameter("@p1", SqlDbType.Int) { Value = numeroOrdenEntrada },
                    new SqlParameter("@p2", SqlDbType.VarChar, 30) { Value = quien },
                    new SqlParameter("@p3", SqlDbType.Int) { Value = toma.Cantidad }).ConfigureAwait(false);
                Comprobar(reservadas, toma);
                return toma.Libre.NumeroOrden;
            }
            int restadas = await db.Database.ExecuteSqlCommandAsync(SQL_RESTAR_DE_LIBRE, orden,
                new SqlParameter("@p1", SqlDbType.Int) { Value = toma.Cantidad },
                new SqlParameter("@p2", SqlDbType.VarChar, 30) { Value = quien }).ConfigureAwait(false);
            Comprobar(restadas, toma);
            int? nueva = await db.Database.SqlQuery<int?>(SQL_INSERTAR_RESERVA,
                new SqlParameter("@p0", SqlDbType.Int) { Value = toma.Libre.NumeroOrden },
                new SqlParameter("@p1", SqlDbType.Int) { Value = toma.Cantidad },
                new SqlParameter("@p2", SqlDbType.Int) { Value = numeroOrdenEntrada },
                new SqlParameter("@p3", SqlDbType.VarChar, 30) { Value = quien }).FirstOrDefaultAsync().ConfigureAwait(false);
            if (nueva == null)
            {
                throw new NestoBusinessException($"No se ha podido reservar {toma.Cantidad} de {toma.Libre.Producto}: no se ha creado la reserva. No se ha hecho nada.");
            }
            return nueva.Value;
        }

        private static void Comprobar(int filas, TomaDeHueco toma)
        {
            if (filas != 1)
            {
                throw new NestoBusinessException($"El hueco {toma.Libre.Hueco?.ToString() ?? "pendiente de ubicar"} de {toma.Libre.Producto} ha cambiado " +
                    "mientras se reservaba: vuelve a intentarlo. No se ha hecho nada.") { StatusCode = System.Net.HttpStatusCode.Conflict };
            }
        }

        /// <summary>prdDeshacerUbicacionReposicion para cada fila: a la fila libre del mismo hueco si la hay; si no, la propia fila vuelve a 0/2.</summary>
        private async Task<ResumenUbicaciones> DevolverAlHueco(List<FilaUbicacionReposicion> filas, TipoMovimientoUbicacion tipo, DocumentoUbicacion documento, string usuario)
        {
            var resumen = new ResumenUbicaciones();
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            foreach (FilaUbicacionReposicion fila in filas ?? new List<FilaUbicacionReposicion>())
            {
                int cantidad = Math.Abs(fila.Cantidad);
                bool sumada = false;
                if (fila.Hueco.TieneHueco)
                {
                    sumada = await db.Database.ExecuteSqlCommandAsync(SQL_SUMAR_A_LIBRE_DEL_HUECO, Orden(fila.NumeroOrden),
                        new SqlParameter("@p1", SqlDbType.Int) { Value = cantidad },
                        Char("@p2", fila.Empresa, 3), Char("@p3", fila.Almacen, 3), Char("@p4", fila.Producto, 15),
                        Char("@p5", fila.Pasillo, 3), Char("@p6", fila.Fila, 3), Char("@p7", fila.Columna, 3),
                        new SqlParameter("@p8", SqlDbType.VarChar, 30) { Value = quien }).ConfigureAwait(false) == 1;
                }
                if (sumada)
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_BORRAR_FILA, Orden(fila.NumeroOrden)).ConfigureAwait(false);
                    resumen.FilasTocadas += 2;
                }
                else
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_DEVOLVER_FILA_A_LIBRE, Orden(fila.NumeroOrden),
                        new SqlParameter("@p1", SqlDbType.Int) { Value = cantidad },
                        new SqlParameter("@p2", SqlDbType.VarChar, 30) { Value = quien }).ConfigureAwait(false);
                    resumen.FilasTocadas += 1;
                }
                Apuntar(resumen, new MovimientoUbicacion
                {
                    Tipo = tipo, Empresa = fila.Empresa, Almacen = fila.Almacen, Producto = fila.Producto, NumeroOrdenUbicacion = fila.NumeroOrden,
                    HuecoDestino = fila.Hueco, Cantidad = cantidad, EstadoAnterior = fila.Estado,
                    EstadoNuevo = string.IsNullOrWhiteSpace(fila.Pasillo) ? EstadosUbicacion.PENDIENTE_DE_UBICAR : EstadosUbicacion.LIBRE,
                    Documento = documento, Usuario = quien
                });
            }
            return resumen;
        }

        private void Apuntar(ResumenUbicaciones resumen, MovimientoUbicacion movimiento)
        {
            movimiento.Momento = ahora();
            resumen.Movimientos.Add(movimiento);
            Registrar(movimiento);
        }

        private static List<SqlParameter> ListaDeLineas(List<int> lineas, out string lista)
        {
            var parametros = lineas.Select((l, i) => new SqlParameter("@l" + i, SqlDbType.Int) { Value = l }).ToList();
            lista = string.Join(", ", parametros.Select(p => p.ParameterName));
            return parametros;
        }

        private static SqlParameter Orden(int numeroOrden)
        {
            return new SqlParameter("@p0", SqlDbType.Int) { Value = numeroOrden };
        }

        private static SqlParameter Char(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor ?? DBNull.Value };
        }
    }

    /// <summary>Una fila de Ubicaciones de una reposición (reserva 4 o salida -4).</summary>
    public class FilaUbicacionReposicion : FilaUbicacionLibre
    {
        public int? NumeroOrdenRepo { get; set; }
    }

    /// <summary>Ariadna#12: unidades de una reserva de reposición que pasan a cogerse de una fila libre de otro hueco.</summary>
    public class TrasladoDeReserva
    {
        public FilaUbicacionReposicion Reserva { get; set; }
        public FilaUbicacionLibre Libre { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// Ariadna#12: qué reserva de reposición pasa a qué fila libre del otro hueco. Puro: sin base de datos. A diferencia del
    /// picking (que cambia lo que haya libre), aquí es todo o nada: si en el origen no queda reservada toda la cantidad o en
    /// el destino no hay toda libre, no se planea nada y se dice por qué. Las reservas, en el orden en que vienen; las
    /// libres, también (las lee la puerta de la más antigua a la más nueva).
    /// </summary>
    public static class PlanificadorCambioHuecoReposicion
    {
        /// <returns>Los traslados, o null si no se puede (con <paramref name="motivo"/>).</returns>
        public static List<TrasladoDeReserva> Planear(IEnumerable<FilaUbicacionReposicion> reservas, IEnumerable<FilaUbicacionLibre> libres,
            int cantidad, out string motivo)
        {
            List<FilaUbicacionReposicion> deLaReposicion = (reservas ?? Enumerable.Empty<FilaUbicacionReposicion>())
                .Where(r => r != null && r.Cantidad > 0 && r.Estado == EstadosUbicacion.RESERVA_REPOSICION).ToList();
            List<FilaUbicacionLibre> sinReservar = (libres ?? Enumerable.Empty<FilaUbicacionLibre>())
                .Where(l => l != null && l.Cantidad > 0 && l.Estado == EstadosUbicacion.LIBRE).ToList();
            int reservado = deLaReposicion.Sum(r => r.Cantidad);
            int libre = sinReservar.Sum(l => l.Cantidad);
            motivo = null;
            if (cantidad <= 0)
            {
                motivo = "La cantidad tiene que ser mayor que cero.";
                return null;
            }
            if (reservado == 0)
            {
                motivo = "La reposición ya no tiene nada reservado de ese producto en ese hueco (¿se ha cambiado ya o se ha terminado?).";
                return null;
            }
            if (reservado < cantidad)
            {
                motivo = $"En ese hueco la reposición solo tiene reservadas {reservado}, no {cantidad}.";
                return null;
            }
            if (libre < cantidad)
            {
                motivo = libre == 0
                    ? "En el hueco de donde se va a coger ya no hay nada libre de ese producto."
                    : $"En el hueco de donde se va a coger solo hay {libre} libres, no {cantidad}.";
                return null;
            }

            var traslados = new List<TrasladoDeReserva>();
            var quedaLibre = sinReservar.ToDictionary(l => l, l => l.Cantidad);
            int falta = cantidad;
            foreach (FilaUbicacionReposicion reserva in deLaReposicion)
            {
                int deEstaReserva = Math.Min(falta, reserva.Cantidad);
                foreach (FilaUbicacionLibre fila in sinReservar)
                {
                    if (deEstaReserva == 0)
                    {
                        break;
                    }
                    int trozo = Math.Min(deEstaReserva, quedaLibre[fila]);
                    if (trozo == 0)
                    {
                        continue;
                    }
                    traslados.Add(new TrasladoDeReserva { Reserva = reserva, Libre = fila, Cantidad = trozo });
                    quedaLibre[fila] -= trozo;
                    deEstaReserva -= trozo;
                    falta -= trozo;
                }
                if (falta == 0)
                {
                    break;
                }
            }
            return traslados;
        }
    }
}
