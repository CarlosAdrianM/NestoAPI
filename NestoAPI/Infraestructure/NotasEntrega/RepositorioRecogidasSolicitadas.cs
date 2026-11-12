using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#855: dbo.RecogidasSolicitadas por SQL, fuera del EDMX (como NotasEntregaCabecera, #757: si estuviera en el EDMX y el
    /// script no se hubiera lanzado, tumbaría el contexto). El pedido de la nota, sus líneas y la ficha del cliente, por EF.
    /// </summary>
    public class RepositorioRecogidasSolicitadas : IRepositorioRecogidasSolicitadas
    {
        public const string TABLA = "RecogidasSolicitadas";
        /// <summary>Las columnas que añade el script además de las de la issue (si falta alguna, el script no está entero).</summary>
        internal static readonly string[] COLUMNAS_SCRIPT = { "QuienRecoge", "AvisoSinRespuesta", "UsuarioAnula", "FechaAnulacion", "MotivoAnulacion" };
        internal const string TABLA_MOVIMIENTOS_FURGONETA = "RepartoBultosMovimientos";

        internal const string SQL_COLUMNAS = "Id, RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(Contacto) AS Contacto, Texto, Bultos, " +
            "RTRIM(FacturaOriginal) AS FacturaOriginal, RTRIM(QuienRecoge) AS QuienRecoge, RTRIM(Vendedor) AS Vendedor, RTRIM(UsuarioSolicita) AS UsuarioSolicita, " +
            "FechaSolicitud, RTRIM(Estado) AS Estado, PedidoNota, RTRIM(UsuarioResuelve) AS UsuarioResuelve, FechaResolucion, MotivoRechazo, " +
            "RTRIM(UsuarioAnula) AS UsuarioAnula, FechaAnulacion, MotivoAnulacion, AvisoSinRespuesta";

        internal const string SQL_INSERTAR = "INSERT INTO dbo." + TABLA + " (Empresa, Cliente, Contacto, Texto, Bultos, FacturaOriginal, QuienRecoge, " +
            "Vendedor, UsuarioSolicita, FechaSolicitud, Estado) OUTPUT INSERTED.Id VALUES (@Empresa, @Cliente, @Contacto, @Texto, @Bultos, @Factura, " +
            "@QuienRecoge, @Vendedor, @Usuario, @Fecha, '" + EstadosRecogidaSolicitada.SOLICITADA + "')";

        internal const string SQL_CONFIRMAR = "UPDATE dbo." + TABLA + " SET Estado = '" + EstadosRecogidaSolicitada.CONFIRMADA + "', PedidoNota = @Pedido, " +
            "Bultos = @Bultos, Texto = @Texto, UsuarioResuelve = @Usuario, FechaResolucion = GETDATE() " +
            "WHERE Id = @Id AND Estado = '" + EstadosRecogidaSolicitada.SOLICITADA + "'";

        internal const string SQL_RECHAZAR = "UPDATE dbo." + TABLA + " SET Estado = '" + EstadosRecogidaSolicitada.RECHAZADA + "', MotivoRechazo = @Motivo, " +
            "UsuarioResuelve = @Usuario, FechaResolucion = GETDATE() WHERE Id = @Id AND Estado = '" + EstadosRecogidaSolicitada.SOLICITADA + "'";

        internal const string SQL_ANULAR = "UPDATE dbo." + TABLA + " SET Estado = '" + EstadosRecogidaSolicitada.ANULADA + "', MotivoAnulacion = @Motivo, " +
            "UsuarioAnula = @Usuario, FechaAnulacion = GETDATE() WHERE Id = @Id AND Estado = @Esperado";

        internal const string SQL_MARCAR_AVISO = "UPDATE dbo." + TABLA + " SET AvisoSinRespuesta = GETDATE() " +
            "WHERE Id = @Id AND AvisoSinRespuesta IS NULL AND Estado = '" + EstadosRecogidaSolicitada.SOLICITADA + "'";

        /// <summary>
        /// La nota sigue sin empezar: sin líneas con picking o procesadas, sin ningún envío y sin abono. Con UPDLOCK para que nadie la
        /// meta en un picking mientras se borra.
        /// </summary>
        internal const string SQL_NOTA_EMPEZADA = @"
SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (UPDLOCK, HOLDLOCK)
                         WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND (ISNULL(l.Picking, 0) > 0 OR l.Estado <> 1))
              OR EXISTS (SELECT 1 FROM dbo.EnviosAgencia e WITH (UPDLOCK, HOLDLOCK) WHERE e.Pedido = @p1)
              OR EXISTS (SELECT 1 FROM dbo.NotasEntregaCabecera n WHERE n.Empresa = @p0 AND n.Pedido = @p1 AND n.PedidoAbono IS NOT NULL)
            THEN 1 ELSE 0 END";

        internal const string SQL_SEGUIMIENTO = @"
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.CabPedidoVta c WITH (NOLOCK) WHERE c.Empresa = @p0 AND c.[Número] = @p1) THEN 1 ELSE 0 END AS bit) AS Existe,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK)
                              WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND (ISNULL(l.Picking, 0) > 0 OR l.Estado <> 1)) THEN 1 ELSE 0 END AS bit) AS Empezada,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.EnviosAgencia e WITH (NOLOCK) WHERE e.Pedido = @p1 AND e.Estado >= 0) THEN 1 ELSE 0 END AS bit) AS TieneEnvio,
       (SELECT TOP 1 e.Estado FROM dbo.EnviosAgencia e WITH (NOLOCK) WHERE e.Pedido = @p1 AND e.Agencia = @p2 AND e.Estado >= 0 ORDER BY e.Numero DESC) AS EstadoParada,
       (SELECT TOP 1 n.PedidoAbono FROM dbo.NotasEntregaCabecera n WITH (NOLOCK) WHERE n.Empresa = @p0 AND n.Pedido = @p1) AS PedidoAbono,
       {0} AS BultosRecogidos";

        internal const string SQL_BULTOS_RECOGIDOS = "CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo." + TABLA_MOVIMIENTOS_FURGONETA + " m WITH (NOLOCK) " +
            "WHERE m.Pedido = @p1 AND m.TipoCodigo = 'NR' AND m.Tipo IN ('Recogido', 'Descargado')) THEN 1 ELSE 0 END AS bit)";

        private readonly NVEntities db;
        private readonly Func<GestorPedidosVenta> crearGestor;

        public RepositorioRecogidasSolicitadas(NVEntities db) : this(db, null)
        {
        }

        internal RepositorioRecogidasSolicitadas(NVEntities db, Func<GestorPedidosVenta> crearGestor)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.crearGestor = crearGestor ?? (() => new GestorPedidosVenta(new ServicioPedidosVenta()));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.Existen(db, TABLA, COLUMNAS_SCRIPT);

        public async Task<FilaRecogidaSolicitada> Leer(int id)
            => (await db.Database.SqlQuery<FilaRecogidaSolicitada>($"SELECT {SQL_COLUMNAS} FROM dbo.{TABLA} WHERE Id = @p0", id)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public Task<List<FilaRecogidaSolicitada>> LeerDelCliente(string empresa, string cliente, string contacto)
            => contacto == null
                ? db.Database.SqlQuery<FilaRecogidaSolicitada>(
                    $"SELECT TOP 50 {SQL_COLUMNAS} FROM dbo.{TABLA} WITH (NOLOCK) WHERE Empresa = @p0 AND Cliente = @p1 ORDER BY FechaSolicitud DESC, Id DESC",
                    empresa, cliente).ToListAsync()
                : db.Database.SqlQuery<FilaRecogidaSolicitada>(
                    $"SELECT TOP 50 {SQL_COLUMNAS} FROM dbo.{TABLA} WITH (NOLOCK) WHERE Empresa = @p0 AND Cliente = @p1 AND Contacto = @p2 " +
                    "ORDER BY FechaSolicitud DESC, Id DESC", empresa, cliente, contacto).ToListAsync();

        public Task<List<FilaRecogidaSolicitada>> LeerPendientes(string empresa)
            => db.Database.SqlQuery<FilaRecogidaSolicitada>(
                $"SELECT {SQL_COLUMNAS} FROM dbo.{TABLA} WITH (NOLOCK) WHERE Empresa = @p0 AND Estado = '{EstadosRecogidaSolicitada.SOLICITADA}' " +
                "ORDER BY FechaSolicitud, Id", empresa).ToListAsync();

        public Task<List<FilaRecogidaSolicitada>> LeerSinAvisar()
            => db.Database.SqlQuery<FilaRecogidaSolicitada>(
                $"SELECT {SQL_COLUMNAS} FROM dbo.{TABLA} WITH (NOLOCK) WHERE Estado = '{EstadosRecogidaSolicitada.SOLICITADA}' AND AvisoSinRespuesta IS NULL " +
                "ORDER BY FechaSolicitud, Id").ToListAsync();

        public Task<List<int>> LeerAbiertas(string empresa, string cliente, string contacto)
            => db.Database.SqlQuery<int>(
                $"SELECT Id FROM dbo.{TABLA} WHERE Empresa = @p0 AND Cliente = @p1 AND Contacto = @p2 " +
                $"AND Estado = '{EstadosRecogidaSolicitada.SOLICITADA}' ORDER BY Id", empresa, cliente, contacto).ToListAsync();

        public Task<FichaNotaCobro> LeerFicha(string empresa, string cliente, string contacto)
            => new RepositorioNotasCobro(db).LeerFicha(empresa, cliente, contacto);

        public async Task<Dictionary<string, FichaNotaCobro>> LeerFichas(string empresa, IReadOnlyCollection<string> clientes)
        {
            List<string> lista = (clientes ?? new string[0]).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
            var resultado = new Dictionary<string, FichaNotaCobro>();
            if (lista.Count == 0)
            {
                return resultado;
            }
            List<Cliente> fichas = await db.Clientes.AsNoTracking()
                .Where(c => c.Empresa == empresa && lista.Contains(c.Nº_Cliente))
                .ToListAsync().ConfigureAwait(false);
            foreach (Cliente c in fichas)
            {
                resultado[ServicioRecogidasSolicitadas.Clave(c.Nº_Cliente, c.Contacto)] = new FichaNotaCobro
                {
                    Nombre = c.Nombre?.Trim(),
                    Direccion = c.Dirección?.Trim(),
                    CodPostal = c.CodPostal?.Trim(),
                    Poblacion = c.Población?.Trim(),
                    Provincia = c.Provincia?.Trim(),
                    Telefonos = c.Teléfono?.Trim(),
                    Vendedor = c.Vendedor?.Trim(),
                    Ruta = c.Ruta?.Trim(),
                    Iva = c.IVA?.Trim(),
                    ContactoCobro = c.ContactoCobro?.Trim()
                };
            }
            return resultado;
        }

        public Task<string> LeerClienteFactura(string empresa, string factura) => new RepositorioNotasEntregaCabecera(db).LeerClienteFactura(empresa, factura);

        public async Task<int> Insertar(FilaRecogidaSolicitada fila)
        {
            List<int> ids = await db.Database.SqlQuery<int>(SQL_INSERTAR,
                Parametro("@Empresa", SqlDbType.Char, fila.Empresa),
                Parametro("@Cliente", SqlDbType.Char, fila.Cliente),
                Parametro("@Contacto", SqlDbType.Char, fila.Contacto),
                Parametro("@Texto", SqlDbType.NVarChar, fila.Texto),
                Parametro("@Bultos", SqlDbType.SmallInt, fila.Bultos),
                Parametro("@Factura", SqlDbType.VarChar, fila.FacturaOriginal),
                Parametro("@QuienRecoge", SqlDbType.VarChar, fila.QuienRecoge),
                Parametro("@Vendedor", SqlDbType.Char, fila.Vendedor),
                Parametro("@Usuario", SqlDbType.VarChar, fila.UsuarioSolicita),
                Parametro("@Fecha", SqlDbType.DateTime, fila.FechaSolicitud)).ToListAsync().ConfigureAwait(false);
            return ids.First();
        }

        public async Task<int?> Confirmar(FilaRecogidaSolicitada fila, short bultos, string texto, IReadOnlyList<string> lineas, FichaNotaCobro ficha, string usuario)
        {
            string empresa = fila.Empresa;
            DateTime hoy = DateTime.Today;
            bool recogeVendedor = fila.QuienRecoge == QuienRecogeRecogida.VENDEDOR;
            PlazoPago plazo = await db.PlazosPago.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Empresa == empresa && p.Número == Constantes.PlazosPago.CONTADO).ConfigureAwait(false);
            if (plazo == null)
            {
                throw new InvalidOperationException($"No existen los plazos de pago {Constantes.PlazosPago.CONTADO} en la empresa {empresa}.");
            }
            string usuarioPedido = ServicioRecogidasSolicitadas.Recortar(usuario ?? "NestoAPI", ServicioRecogidasSolicitadas.LONGITUD_USUARIO_PEDIDO);

            int numero = db.TomarSiguienteNumeroPedido();
            CabPedidoVta cabecera = ConstruirCabecera(fila, numero, texto, ficha, hoy, usuarioPedido);
            List<LinPedidoVta> lineasPedido;
            try
            {
                GestorPedidosVenta gestor = crearGestor();
                lineasPedido = new List<LinPedidoVta>();
                foreach (string linea in lineas ?? new List<string>())
                {
                    LinPedidoVta lin = gestor.CrearLineaVta(LineaTexto(linea, hoy, usuarioPedido), numero, empresa, cabecera.IVA, plazo,
                        fila.Cliente, fila.Contacto, cabecera.Ruta, cabecera.Vendedor, usuarioPedido);
                    if (recogeVendedor)
                    {
                        // No entra en el picking ni en Facturar rutas: no hay parada (la recoge el vendedor).
                        lin.Fecha_Entrega = CreadorNotaEntregaPendiente.FECHA_ENTREGA_SIN_DETERMINAR;
                    }
                    lineasPedido.Add(lin);
                }
            }
            catch
            {
                DevolverNumero(numero);
                throw;
            }

            var modificacion = new Modificacion
            {
                Tabla = ServicioRecogidasSolicitadas.TABLA_MODIFICACIONES,
                Anterior = $"Recogida solicitada {fila.Id}: {EstadosRecogidaSolicitada.SOLICITADA}",
                Nuevo = ServicioRecogidasSolicitadas.Recortar($"Recogida solicitada {fila.Id} confirmada: nota de recogida {numero} para " +
                    $"{fila.Cliente}/{fila.Contacto}, {bultos} bulto(s), la recoge {(recogeVendedor ? "el vendedor" : "el reparto")}, pedida por " +
                    $"{fila.UsuarioSolicita} (NestoAPI#855, usuario {usuario})", 4000),
                Usuario = ServicioRecogidasSolicitadas.Recortar(usuario, 50)
            };

            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    _ = db.CabPedidoVtas.Add(cabecera);
                    GestorComisiones.CrearVendedorPedidoGrupoProducto(db, cabecera, new PedidoVentaDTO { Usuario = cabecera.Usuario });
                    _ = db.LinPedidoVtas.AddRange(lineasPedido);
                    _ = db.Modificaciones.Add(modificacion);
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);

                    _ = await db.Database.ExecuteSqlCommandAsync(RepositorioNotasEntregaCabecera.SQL_GUARDAR,
                        Parametro("@Empresa", SqlDbType.Char, empresa),
                        Parametro("@Pedido", SqlDbType.Int, numero),
                        Parametro("@Tipo", SqlDbType.VarChar, TiposNotaEntrega.RECOGIDA),
                        Parametro("@Bultos", SqlDbType.SmallInt, bultos),
                        Parametro("@Factura", SqlDbType.VarChar, fila.FacturaOriginal),
                        Parametro("@Usuario", SqlDbType.VarChar, ServicioRecogidasSolicitadas.Recortar(usuario ?? "NestoAPI", 50))).ConfigureAwait(false);

                    int filas = await db.Database.ExecuteSqlCommandAsync(SQL_CONFIRMAR,
                        Parametro("@Pedido", SqlDbType.Int, numero),
                        Parametro("@Bultos", SqlDbType.SmallInt, bultos),
                        Parametro("@Texto", SqlDbType.NVarChar, texto),
                        Parametro("@Usuario", SqlDbType.VarChar, ServicioRecogidasSolicitadas.Recortar(usuario, 50)),
                        Parametro("@Id", SqlDbType.Int, fila.Id)).ConfigureAwait(false);
                    if (filas != 1)
                    {
                        transaccion.RollbackSeguro();
                        Soltar(cabecera, lineasPedido, modificacion);
                        DevolverNumero(numero);
                        return null;
                    }
                    transaccion.Commit();
                    return numero;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    Soltar(cabecera, lineasPedido, modificacion);
                    DevolverNumero(numero);
                    throw;
                }
            }
        }

        /// <summary>
        /// La cabecera de la nota (pura): como la de cobro (#786) pero de RECOGIDA: cliente/contacto, NotaEntrega = 1, serie por defecto,
        /// Periodo NRM, al contado con vencimiento hoy y efectivo (no se factura), IVA, vendedor, ruta y contacto de cobro de la ficha, y
        /// en Comentarios lo que hay que recoger y quién lo pidió.
        /// </summary>
        internal static CabPedidoVta ConstruirCabecera(FilaRecogidaSolicitada fila, int numero, string texto, FichaNotaCobro ficha, DateTime hoy, string usuario)
            => new CabPedidoVta
            {
                Empresa = fila.Empresa,
                Número = numero,
                Nº_Cliente = fila.Cliente,
                Contacto = fila.Contacto,
                Fecha = hoy.Date,
                Forma_Pago = Constantes.FormasPago.EFECTIVO,
                PlazosPago = Constantes.PlazosPago.CONTADO,
                Primer_Vencimiento = hoy.Date,
                IVA = ficha.Iva,
                Vendedor = string.IsNullOrWhiteSpace(ficha.Vendedor) ? fila.Vendedor : ficha.Vendedor,
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Ruta = ficha.Ruta,
                Serie = Constantes.Series.SERIE_POR_DEFECTO,
                Origen = fila.Empresa,
                ContactoCobro = string.IsNullOrWhiteSpace(ficha.ContactoCobro) ? fila.Contacto : ficha.ContactoCobro,
                MantenerJunto = false,
                ServirJunto = true,
                ModoServicio = Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                NotaEntrega = true,
                Comentarios = ServicioRecogidasSolicitadas.ComentarioNota(fila, texto),
                Usuario = usuario
            };

        /// <summary>Una línea de texto de la nota, como las que manda Nesto: en curso, sin precio, en Algete.</summary>
        internal static LineaPedidoVentaDTO LineaTexto(string texto, DateTime hoy, string usuario)
            => new LineaPedidoVentaDTO
            {
                tipoLinea = (byte)Constantes.TiposLineaVenta.TEXTO,
                estado = Constantes.EstadosLineaVenta.EN_CURSO,
                texto = texto,
                Cantidad = 0,
                PrecioUnitario = 0m,
                fechaEntrega = hoy.Date,
                almacen = Constantes.Almacenes.ALGETE,
                delegacion = Constantes.Empresas.DELEGACION_POR_DEFECTO,
                formaVenta = Constantes.Empresas.FORMA_VENTA_POR_DEFECTO,
                AplicarDescuento = false,
                vistoBueno = true,
                usuario = usuario
            };

        public async Task<bool> Rechazar(int id, string motivo, string usuario)
            => await db.Database.ExecuteSqlCommandAsync(SQL_RECHAZAR,
                Parametro("@Motivo", SqlDbType.NVarChar, motivo),
                Parametro("@Usuario", SqlDbType.VarChar, ServicioRecogidasSolicitadas.Recortar(usuario, 50)),
                Parametro("@Id", SqlDbType.Int, id)).ConfigureAwait(false) == 1;

        public async Task<bool> Anular(int id, string estadoEsperado, string motivo, string usuario)
            => await db.Database.ExecuteSqlCommandAsync(SQL_ANULAR,
                Parametro("@Motivo", SqlDbType.NVarChar, motivo),
                Parametro("@Usuario", SqlDbType.VarChar, ServicioRecogidasSolicitadas.Recortar(usuario, 50)),
                Parametro("@Id", SqlDbType.Int, id),
                Parametro("@Esperado", SqlDbType.VarChar, estadoEsperado)).ConfigureAwait(false) == 1;

        public async Task<bool> AnularBorrandoNota(int id, string empresa, int pedido, string motivo, string usuario)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                CabPedidoVta cabecera = null;
                try
                {
                    int empezada = (await db.Database.SqlQuery<int>(SQL_NOTA_EMPEZADA, empresa, pedido).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                    if (empezada != 0)
                    {
                        transaccion.RollbackSeguro();
                        return false;
                    }
                    int filas = await db.Database.ExecuteSqlCommandAsync(SQL_ANULAR,
                        Parametro("@Motivo", SqlDbType.NVarChar, motivo),
                        Parametro("@Usuario", SqlDbType.VarChar, ServicioRecogidasSolicitadas.Recortar(usuario, 50)),
                        Parametro("@Id", SqlDbType.Int, id),
                        Parametro("@Esperado", SqlDbType.VarChar, EstadosRecogidaSolicitada.CONFIRMADA)).ConfigureAwait(false);
                    if (filas != 1)
                    {
                        transaccion.RollbackSeguro();
                        return false;
                    }
                    // Como DELETE api/PedidosVenta (Borrar pedido de Nesto): las líneas las borra la BD con la cabecera. Es una nota de
                    // solo texto: no hay stock que devolver a la tienda ni cheques ni cupones que soltar. Su fila de NotasEntregaCabecera
                    // se queda (la tabla no tiene DELETE y el número de pedido no se vuelve a dar).
                    cabecera = await db.CabPedidoVtas.FirstOrDefaultAsync(c => c.Empresa == empresa && c.Número == pedido).ConfigureAwait(false);
                    if (cabecera != null)
                    {
                        _ = db.CabPedidoVtas.Remove(cabecera);
                    }
                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = ServicioRecogidasSolicitadas.TABLA_MODIFICACIONES,
                        Anterior = $"Recogida solicitada {id}: {EstadosRecogidaSolicitada.CONFIRMADA}, nota de recogida {pedido}",
                        Nuevo = ServicioRecogidasSolicitadas.Recortar($"Recogida solicitada {id} anulada y nota de recogida {pedido} borrada (sin empezar)" +
                            (string.IsNullOrWhiteSpace(motivo) ? string.Empty : $": {motivo}") + $" (NestoAPI#855, usuario {usuario})", 4000),
                        Usuario = ServicioRecogidasSolicitadas.Recortar(usuario, 50)
                    });
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return true;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    if (cabecera != null)
                    {
                        db.Entry(cabecera).State = EntityState.Detached;
                    }
                    throw;
                }
            }
        }

        public async Task<SeguimientoNotaRecogida> LeerSeguimiento(string empresa, int pedido)
        {
            string bultos = ColumnasOpcionales.ExisteTabla(db, TABLA_MOVIMIENTOS_FURGONETA) ? SQL_BULTOS_RECOGIDOS : "CAST(0 AS bit)";
            string sql = string.Format(SQL_SEGUIMIENTO, bultos);
            if (!ColumnasOpcionales.ExisteTabla(db, RepositorioNotasEntregaCabecera.TABLA))
            {
                sql = sql.Replace("(SELECT TOP 1 n.PedidoAbono FROM dbo.NotasEntregaCabecera n WITH (NOLOCK) WHERE n.Empresa = @p0 AND n.Pedido = @p1)", "CAST(NULL AS int)");
            }
            return (await db.Database.SqlQuery<SeguimientoNotaRecogida>(sql, empresa, pedido, Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
        }

        public async Task<bool> MarcarAvisoSinRespuesta(int id)
            => await db.Database.ExecuteSqlCommandAsync(SQL_MARCAR_AVISO, Parametro("@Id", SqlDbType.Int, id)).ConfigureAwait(false) == 1;

        public async Task<bool> EsRecogidaDelVendedor(string empresa, int pedidoNota)
        {
            if (!ScriptAplicado())
            {
                return false;
            }
            List<int> filas = await db.Database.SqlQuery<int>(
                $"SELECT TOP 1 Id FROM dbo.{TABLA} WITH (NOLOCK) WHERE Empresa = @p0 AND PedidoNota = @p1 AND QuienRecoge = '{QuienRecogeRecogida.VENDEDOR}' " +
                $"AND Estado = '{EstadosRecogidaSolicitada.CONFIRMADA}'", empresa, pedidoNota).ToListAsync().ConfigureAwait(false);
            return filas.Count > 0;
        }

        private void Soltar(CabPedidoVta cabecera, List<LinPedidoVta> lineas, Modificacion modificacion)
        {
            try
            {
                db.Entry(cabecera).State = EntityState.Detached;
                foreach (LinPedidoVta linea in lineas ?? new List<LinPedidoVta>())
                {
                    db.Entry(linea).State = EntityState.Detached;
                }
                db.Entry(modificacion).State = EntityState.Detached;
            }
            catch
            {
                // solo es limpiar el contexto
            }
        }

        private void DevolverNumero(int numero)
        {
            try
            {
                db.DevolverNumeroPedido(numero);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recogidas solicitadas #855] No se ha podido devolver el número de pedido {numero}: {ex.Message}", ex));
            }
        }

        private static SqlParameter Parametro(string nombre, SqlDbType tipo, object valor)
            => new SqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value };
    }
}
