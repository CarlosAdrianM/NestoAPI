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
            this.registro = registro ?? new RegistroMovimientosUbicacionDebug();
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

        // Lo que salió de los huecos del origen con un traspaso aún no recogido
        internal const string SQL_SALIDAS_DEL_TRASPASO = COLUMNAS_FILA + @"
WHERE [NºTraspasoRepo] = @p0 AND Estado = -4 AND [Almacén] = @p1";

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

        // ---------------------------------------------------------------- Registro

        /// <summary>El punto ÚNICO por donde pasa cada cambio de Ubicaciones (corte 2: la tabla de movimientos).</summary>
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

        public async Task<ResumenUbicaciones> AnularSalidaDeReposicion(string empresa, string almacenOrigen, int numeroTraspaso, string usuario)
        {
            List<FilaUbicacionReposicion> salidas = await db.Database.SqlQuery<FilaUbicacionReposicion>(SQL_SALIDAS_DEL_TRASPASO,
                new SqlParameter("@p0", SqlDbType.Int) { Value = numeroTraspaso }, Char("@p1", almacenOrigen, 3)).ToListAsync().ConfigureAwait(false);
            return await DevolverAlHueco(salidas, TipoMovimientoUbicacion.AnularSalidaDeReposicion,
                DocumentoUbicacion.De(TipoDocumentoUbicacion.Traspaso, numeroTraspaso), usuario).ConfigureAwait(false);
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
}
