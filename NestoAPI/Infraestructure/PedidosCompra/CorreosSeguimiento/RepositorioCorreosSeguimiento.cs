using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857: una fila de dbo.ComprasCorreosSeguimiento.</summary>
    public class CorreoSeguimientoFila
    {
        public int Id { get; set; }
        public string MensajeId { get; set; }
        public DateTime Recibido { get; set; }
        public string RemitenteDireccion { get; set; }
        public string RemitenteDominio { get; set; }
        public string Asunto { get; set; }
        public string Agencia { get; set; }
        public string Codigo { get; set; }
        public string Url { get; set; }
        /// <summary>JSON con todos los seguimientos del correo (Codigo/Url es el primero).</summary>
        public string Seguimientos { get; set; }
        public string Extracto { get; set; }
        public string ProveedorPropuesto { get; set; }
        /// <summary>JSON con los <see cref="CandidatoPedidoCompra"/>.</summary>
        public string PedidosCandidatos { get; set; }
        public DateTime FechaCaptura { get; set; }
        public string EmpresaReal { get; set; }
        public int? PedidoReal { get; set; }
        public DateTime? FechaAlbaranReal { get; set; }
        /// <summary>Adjuntos de verdad (sin los incrustados de la firma); null = sin apuntar (sin las columnas de Issue857b o no se pudo).</summary>
        public int? NumAdjuntos { get; set; }
        /// <summary>JSON [{Nombre, Tipo, Tamano}] de los 10 primeros (<see cref="ResumenAdjuntos"/>).</summary>
        public string Adjuntos { get; set; }
        /// <summary>
        /// La entrega que anuncia el correo (<see cref="ExtractorEntregaEstimada"/>) y desde cuándo se podría servir
        /// (<see cref="ReglaDisponibleCompra"/>). Null = no lo dice, correo de antes o sin las columnas de Issue857c.
        /// </summary>
        public DateTime? EntregaEstimadaDia { get; set; }
        public TimeSpan? EntregaEstimadaDesde { get; set; }
        public TimeSpan? EntregaEstimadaHasta { get; set; }
        public DateTime? DisponibleEstimado { get; set; }
        /// <summary>Envío nuestro de salida en copia (<see cref="EnvioNuestroDeSalida"/>): sin asignación. Null = sin apuntar.</summary>
        public bool? EsSalidaNuestra { get; set; }
    }

    public interface IRepositorioCorreosSeguimiento
    {
        /// <summary>¿Se ha lanzado Scripts/Issue857_ComprasCorreosSeguimiento.sql? Sin las tablas no se hace nada.</summary>
        bool TablasExisten();
        /// <summary>¿Tiene ComprasCorreosSeguimiento las columnas NumAdjuntos y Adjuntos (Scripts/Issue857b)? Con caché.</summary>
        bool AdjuntosDisponibles();
        /// <summary>
        /// Histórico: los pedidos que estaban en el aire en algún momento desde <paramref name="desde"/> (los de ahora y los recibidos
        /// desde entonces), con su fecha y la del recibido entero, para quedarse con los de cada correo (<see cref="AsignadorPedidosCompra.EnAireEl"/>).
        /// </summary>
        Task<List<PedidoCompraEnAire>> LeerPedidosDesde(string empresa, DateTime desde);
        /// <summary>Medición del histórico, por lotes y por clave: los pendientes de medir recibidos desde <paramref name="desde"/> con Id mayor que <paramref name="despuesDeId"/>.</summary>
        Task<List<CorreoSeguimientoFila>> LeerPendientesDeMedirPorId(DateTime desde, int despuesDeId, int cuantos);
        Task<string> LeerEstado(string buzon);
        Task GuardarEstado(string buzon, string enlace, string ultimoError);
        /// <summary>Inserta si no está ya ese MensajeId. True si la ha insertado.</summary>
        Task<bool> Insertar(CorreoSeguimientoFila fila);
        Task<List<RemitenteMarcado>> LeerRemitentes();
        /// <summary>Alta (o reactivación / cambio de agencia si ya estaba ese remitente). Devuelve el Id.</summary>
        Task<int> GuardarRemitente(string agencia, string remitente, string usuario);
        /// <summary>Baja lógica (Activo = 0, con quién y cuándo). False si no existe.</summary>
        Task<bool> DesactivarRemitente(int id, string usuario);
        Task<List<PedidoCompraEnAire>> LeerPedidosEnAire(string empresa);
        Task<List<CorreoSeguimientoFila>> LeerPendientesDeMedir(DateTime desde);
        Task<List<AlbaranPedidoCompra>> LeerAlbaranes(IEnumerable<CandidatoPedidoCompra> pedidos);
        Task GuardarReal(int id, AlbaranPedidoCompra albaran);
        Task<List<CorreoSeguimientoFila>> LeerCorreos(DateTime desde);
    }

    public class RepositorioCorreosSeguimiento : IRepositorioCorreosSeguimiento
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.ComprasCorreosSeguimiento', 'U') IS NULL " +
            "OR OBJECT_ID('dbo.AgenciasEntradaRemitentes', 'U') IS NULL OR OBJECT_ID('dbo.ComprasCorreosSeguimientoEstado', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_LEER_ESTADO = "SELECT EnlaceDelta FROM dbo.ComprasCorreosSeguimientoEstado WHERE Buzon = @buzon";

        internal const string SQL_GUARDAR_ESTADO = @"
UPDATE dbo.ComprasCorreosSeguimientoEstado SET EnlaceDelta = @enlace, UltimoError = @error, FechaModificacion = GETDATE() WHERE Buzon = @buzon;
IF @@ROWCOUNT = 0
    INSERT INTO dbo.ComprasCorreosSeguimientoEstado (Buzon, EnlaceDelta, UltimoError, FechaModificacion) VALUES (@buzon, @enlace, @error, GETDATE());";

        // {0} = las columnas opcionales que haya; {1} = sus valores
        internal const string SQL_INSERTAR_BASE = @"
INSERT INTO dbo.ComprasCorreosSeguimiento (MensajeId, Recibido, RemitenteDireccion, RemitenteDominio, Asunto, Agencia, Codigo, Url, Seguimientos,
    Extracto, ProveedorPropuesto, PedidosCandidatos, FechaCaptura{0})
SELECT @mensajeId, @recibido, @remitente, @dominio, @asunto, @agencia, @codigo, @url, @seguimientos, @extracto, @proveedor, @candidatos, GETDATE(){1}
WHERE NOT EXISTS (SELECT 1 FROM dbo.ComprasCorreosSeguimiento WITH (UPDLOCK, HOLDLOCK) WHERE MensajeId = @mensajeId)";

        /// <summary>El INSERT con las columnas opcionales que existan (adjuntos de Issue857b, entrega estimada de Issue857c).</summary>
        internal static string SqlInsertar(bool conAdjuntos, bool conEntrega) => string.Format(SQL_INSERTAR_BASE,
            (conAdjuntos ? ", " + COLUMNAS_ADJUNTOS : string.Empty) + (conEntrega ? ", " + COLUMNAS_ENTREGA : string.Empty),
            (conAdjuntos ? ", @numAdjuntos, @adjuntos" : string.Empty)
                + (conEntrega ? ", @entregaDia, @entregaDesde, @entregaHasta, @disponible, @esSalida" : string.Empty));

        internal static readonly string SQL_INSERTAR = SqlInsertar(false, false);

        internal const string SQL_EXISTEN_ADJUNTOS = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'NumAdjuntos') IS NULL " +
            "OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'Adjuntos') IS NULL THEN 0 ELSE 1 END AS bit)";

        // Con las columnas de Scripts/Issue857b_ComprasCorreosHistoricoYAdjuntos.sql
        internal static readonly string SQL_INSERTAR_CON_ADJUNTOS = SqlInsertar(true, false);

        internal const string COLUMNAS_ADJUNTOS = "NumAdjuntos, Adjuntos";
        internal const string COLUMNAS_ADJUNTOS_NULAS = "CAST(NULL AS int) AS NumAdjuntos, CAST(NULL AS nvarchar(2000)) AS Adjuntos";

        // Scripts/Issue857c_ComprasCorreosEntregaEstimada.sql (las cinco van juntas)
        internal const string SQL_EXISTEN_ENTREGA = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'EntregaEstimadaDia') IS NULL " +
            "OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'EntregaEstimadaDesde') IS NULL OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'EntregaEstimadaHasta') IS NULL " +
            "OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'DisponibleEstimado') IS NULL OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'EsSalidaNuestra') IS NULL " +
            "THEN 0 ELSE 1 END AS bit)";
        internal const string COLUMNAS_ENTREGA = "EntregaEstimadaDia, EntregaEstimadaDesde, EntregaEstimadaHasta, DisponibleEstimado, EsSalidaNuestra";
        internal const string COLUMNAS_ENTREGA_NULAS = "CAST(NULL AS date) AS EntregaEstimadaDia, CAST(NULL AS time(0)) AS EntregaEstimadaDesde, " +
            "CAST(NULL AS time(0)) AS EntregaEstimadaHasta, CAST(NULL AS date) AS DisponibleEstimado, CAST(NULL AS bit) AS EsSalidaNuestra";

        /// <summary>Tras la comprobación de las columnas, cuánto vale lo sabido (así, lanzado el script, se nota sin reiniciar).</summary>
        internal static readonly TimeSpan CACHE_ADJUNTOS = TimeSpan.FromMinutes(10);
        private static readonly ColumnasOpcionales columnasAdjuntos = new ColumnasOpcionales(SQL_EXISTEN_ADJUNTOS);
        private static readonly ColumnasOpcionales columnasEntrega = new ColumnasOpcionales(SQL_EXISTEN_ENTREGA);

        /// <summary>Si existen unas columnas opcionales, con caché de <see cref="CACHE_ADJUNTOS"/>. Ante cualquier fallo, que no.</summary>
        private sealed class ColumnasOpcionales
        {
            private readonly string sql;
            private readonly object bloqueo = new object();
            private bool? existen;
            private DateTime comprobadasUtc;

            public ColumnasOpcionales(string sql)
            {
                this.sql = sql;
            }

            public bool Existen(Func<NVEntities> crearContexto)
            {
                lock (bloqueo)
                {
                    if (existen.HasValue && DateTime.UtcNow - comprobadasUtc < CACHE_ADJUNTOS)
                    {
                        return existen.Value;
                    }
                }
                bool resultado;
                try
                {
                    using (NVEntities db = crearContexto())
                    {
                        resultado = db.Database.SqlQuery<bool>(sql).FirstOrDefault();
                    }
                }
                catch (Exception)
                {
                    resultado = false;
                }
                lock (bloqueo)
                {
                    existen = resultado;
                    comprobadasUtc = DateTime.UtcNow;
                }
                return resultado;
            }
        }

        internal const string SQL_REMITENTES = "SELECT Id, RTRIM(Agencia) AS Agencia, RTRIM(Remitente) AS Remitente, Activo, Usuario, FechaModificacion " +
            "FROM dbo.AgenciasEntradaRemitentes WITH (NOLOCK) ORDER BY Agencia, Remitente";

        internal const string SQL_GUARDAR_REMITENTE = @"SET NOCOUNT ON;
DECLARE @id int = (SELECT TOP 1 Id FROM dbo.AgenciasEntradaRemitentes WHERE Remitente = @remitente);
IF @id IS NULL
BEGIN
    INSERT INTO dbo.AgenciasEntradaRemitentes (Agencia, Remitente, Activo, Usuario, FechaModificacion) VALUES (@agencia, @remitente, 1, @usuario, GETDATE());
    SET @id = CAST(SCOPE_IDENTITY() AS int);
END
ELSE
    UPDATE dbo.AgenciasEntradaRemitentes SET Agencia = @agencia, Activo = 1, Usuario = @usuario, FechaModificacion = GETDATE() WHERE Id = @id;
SELECT @id;";

        internal const string SQL_DESACTIVAR_REMITENTE = "UPDATE dbo.AgenciasEntradaRemitentes SET Activo = 0, Usuario = @usuario, FechaModificacion = GETDATE() WHERE Id = @id";

        // El mismo filtro que la sombra del modo de servicio y el aviso de proveedor vencido: líneas -1/1 con Enviado.
        internal const string FILTRO_EN_AIRE = "c.Empresa IN (@empresa, @espejo) AND c.Estado IN (-1, 1) AND c.Enviado = 1";

        /// <summary>
        /// Histórico: los en el aire de ahora y los que se han recibido (alguna línea con albarán) desde @desde. Una consulta por
        /// lanzamiento del histórico, fuera de horario.
        /// </summary>
        internal const string FILTRO_DESDE = "c.Empresa IN (@empresa, @espejo) AND ((c.Estado IN (-1, 1) AND c.Enviado = 1) " +
            "OR (c.Estado >= 2 AND c.FechaAlbarán >= @desde))";

        // {0} = el filtro. FechaPedido/FechaRecibidoEntero solo hacen falta en el histórico (en el job, NULL).
        internal const string SQL_PEDIDOS = @"
SELECT RTRIM(c.Empresa) AS Empresa, c.Número AS Pedido, RTRIM(MAX(c.NºProveedor)) AS Proveedor, MAX(cab.[Fecha Modificación]) AS FechaEnvio,
    {1}
FROM dbo.LinPedidoCmp c WITH (NOLOCK)
LEFT JOIN dbo.CabPedidoCmp cab WITH (NOLOCK) ON cab.Empresa = c.Empresa AND cab.Número = c.Número
WHERE {0}
GROUP BY c.Empresa, c.Número";

        internal const string FECHAS_SIN_HISTORICO = "CAST(NULL AS datetime) AS FechaPedido, CAST(NULL AS datetime) AS FechaRecibidoEntero";
        internal const string FECHAS_HISTORICO = "MAX(cab.Fecha) AS FechaPedido, " +
            "CASE WHEN SUM(CASE WHEN c.Estado IN (-1, 1) THEN 1 ELSE 0 END) > 0 THEN NULL ELSE MAX(c.FechaAlbarán) END AS FechaRecibidoEntero";

        internal static readonly string SQL_PEDIDOS_EN_AIRE = string.Format(SQL_PEDIDOS, FILTRO_EN_AIRE, FECHAS_SIN_HISTORICO);
        internal static readonly string SQL_PEDIDOS_DESDE = string.Format(SQL_PEDIDOS, FILTRO_DESDE, FECHAS_HISTORICO);

        internal const string SQL_PROVEEDORES = @"
SELECT RTRIM(p.Empresa) AS Empresa, RTRIM(p.Número) AS Proveedor, RTRIM(p.Nombre) AS Nombre, RTRIM(p.Web) AS Web, p.ProveedorPrincipal
FROM dbo.Proveedores p WITH (NOLOCK)
WHERE p.Empresa IN (@empresa, @espejo)
  AND p.Número IN (SELECT DISTINCT c.NºProveedor FROM dbo.LinPedidoCmp c WITH (NOLOCK) WHERE {0})";

        internal const string SQL_CONTACTOS = @"
SELECT RTRIM(pc.Empresa) AS Empresa, RTRIM(pc.NºProveedor) AS Proveedor, RTRIM(pc.CorreoElectrónico) AS Correo
FROM dbo.PersonasContactoProveedor pc WITH (NOLOCK)
WHERE pc.Empresa IN (@empresa, @espejo) AND pc.CorreoElectrónico IS NOT NULL AND pc.CorreoElectrónico LIKE '%@%'
  AND pc.NºProveedor IN (SELECT DISTINCT c.NºProveedor FROM dbo.LinPedidoCmp c WITH (NOLOCK) WHERE {0})";

        internal static readonly string SQL_PROVEEDORES_EN_AIRE = string.Format(SQL_PROVEEDORES, FILTRO_EN_AIRE);
        internal static readonly string SQL_CONTACTOS_EN_AIRE = string.Format(SQL_CONTACTOS, FILTRO_EN_AIRE);

        internal const string COLUMNAS_FILA = "Id, MensajeId, Recibido, RemitenteDireccion, RemitenteDominio, Asunto, Agencia, Codigo, Url, Seguimientos, " +
            "Extracto, ProveedorPropuesto, PedidosCandidatos, FechaCaptura, EmpresaReal, PedidoReal, FechaAlbaranReal";

        internal const string SQL_PENDIENTES_MEDIR = @"
SELECT TOP 500 " + COLUMNAS_FILA + ", " + COLUMNAS_ADJUNTOS_NULAS + ", " + COLUMNAS_ENTREGA_NULAS + @"
FROM dbo.ComprasCorreosSeguimiento WITH (NOLOCK)
WHERE PedidoReal IS NULL AND PedidosCandidatos IS NOT NULL AND Recibido >= @desde
ORDER BY Recibido";

        // Por la clave (Id, el índice agrupado), de @cuantos en @cuantos
        internal const string SQL_PENDIENTES_MEDIR_POR_ID = @"
SELECT TOP (@cuantos) " + COLUMNAS_FILA + ", " + COLUMNAS_ADJUNTOS_NULAS + ", " + COLUMNAS_ENTREGA_NULAS + @"
FROM dbo.ComprasCorreosSeguimiento WITH (NOLOCK)
WHERE Id > @despues AND PedidoReal IS NULL AND PedidosCandidatos IS NOT NULL AND Recibido >= @desde
ORDER BY Id";

        // Por la clave de LinPedidoCmp (Empresa, Número): un día de albarán por fila (un pedido recibido en dos veces da dos).
        internal const string SQL_ALBARANES = @"
SELECT RTRIM(l.Empresa) AS Empresa, l.Número AS Pedido, CAST(CAST(l.FechaAlbarán AS date) AS datetime) AS FechaAlbaran
FROM dbo.LinPedidoCmp l WITH (NOLOCK)
WHERE l.Empresa IN (@empresa, @espejo) AND l.Número IN ({0}) AND l.Estado >= 2 AND l.FechaAlbarán IS NOT NULL
GROUP BY l.Empresa, l.Número, CAST(l.FechaAlbarán AS date)";

        internal const string SQL_GUARDAR_REAL = "UPDATE dbo.ComprasCorreosSeguimiento SET EmpresaReal = @empresa, PedidoReal = @pedido, FechaAlbaranReal = @fecha " +
            "WHERE Id = @id AND PedidoReal IS NULL";

        // {0} = las columnas de los adjuntos (o NULL sin el script Issue857b); {1} = las de la entrega estimada (o NULL sin Issue857c)
        internal const string SQL_CORREOS = @"
SELECT TOP 2000 " + COLUMNAS_FILA + @", {0}, {1}
FROM dbo.ComprasCorreosSeguimiento WITH (NOLOCK)
WHERE Recibido >= @desde
ORDER BY Recibido DESC";

        private const int TAMANO_LOTE_IN = 200;

        private class FilaPedido
        {
            public string Empresa { get; set; }
            public int Pedido { get; set; }
            public string Proveedor { get; set; }
            public DateTime? FechaEnvio { get; set; }
            public DateTime? FechaPedido { get; set; }
            public DateTime? FechaRecibidoEntero { get; set; }
        }

        private class FilaProveedor
        {
            public string Empresa { get; set; }
            public string Proveedor { get; set; }
            public string Nombre { get; set; }
            public string Web { get; set; }
            public bool ProveedorPrincipal { get; set; }
        }

        private class FilaContacto
        {
            public string Empresa { get; set; }
            public string Proveedor { get; set; }
            public string Correo { get; set; }
        }

        private readonly Func<NVEntities> crearContexto;

        public RepositorioCorreosSeguimiento(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() => new NVEntities());
        }

        public bool TablasExisten()
        {
            try
            {
                using (NVEntities db = crearContexto())
                {
                    return db.Database.SqlQuery<bool>(SQL_EXISTE).FirstOrDefault();
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool AdjuntosDisponibles() => columnasAdjuntos.Existen(crearContexto);

        /// <summary>¿Tiene ComprasCorreosSeguimiento las columnas de la entrega estimada y EsSalidaNuestra (Scripts/Issue857c)? Con caché.</summary>
        public bool EntregaEstimadaDisponible() => columnasEntrega.Existen(crearContexto);

        public async Task<string> LeerEstado(string buzon)
        {
            using (NVEntities db = crearContexto())
            {
                return (await db.Database.SqlQuery<string>(SQL_LEER_ESTADO, Texto("@buzon", buzon, 100)).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            }
        }

        public async Task GuardarEstado(string buzon, string enlace, string ultimoError)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR_ESTADO, Texto("@buzon", buzon, 100), Texto("@enlace", enlace, -1),
                    Texto("@error", TextoCorreo.Recortar(ultimoError, 500), 500)).ConfigureAwait(false);
            }
        }

        public async Task<bool> Insertar(CorreoSeguimientoFila fila)
        {
            bool conAdjuntos = AdjuntosDisponibles();
            bool conEntrega = EntregaEstimadaDisponible();
            using (NVEntities db = crearContexto())
            {
                var parametros = new List<SqlParameter>
                {
                    Texto("@mensajeId", fila.MensajeId, 450),
                    new SqlParameter("@recibido", SqlDbType.DateTime) { Value = fila.Recibido },
                    Texto("@remitente", fila.RemitenteDireccion, 255),
                    Texto("@dominio", fila.RemitenteDominio, 255),
                    Texto("@asunto", fila.Asunto, 255),
                    Texto("@agencia", fila.Agencia, 50),
                    Texto("@codigo", fila.Codigo, 50),
                    Texto("@url", fila.Url, 1000),
                    Texto("@seguimientos", fila.Seguimientos, 4000),
                    Texto("@extracto", fila.Extracto, 600),
                    Texto("@proveedor", fila.ProveedorPropuesto, 10),
                    Texto("@candidatos", fila.PedidosCandidatos, 2000)
                };
                if (conAdjuntos)
                {
                    parametros.Add(new SqlParameter("@numAdjuntos", SqlDbType.Int) { Value = (object)fila.NumAdjuntos ?? DBNull.Value });
                    parametros.Add(Texto("@adjuntos", fila.Adjuntos, ResumenAdjuntos.LONGITUD_JSON));
                }
                if (conEntrega)
                {
                    parametros.Add(new SqlParameter("@entregaDia", SqlDbType.Date) { Value = (object)fila.EntregaEstimadaDia?.Date ?? DBNull.Value });
                    parametros.Add(new SqlParameter("@entregaDesde", SqlDbType.Time) { Scale = 0, Value = (object)fila.EntregaEstimadaDesde ?? DBNull.Value });
                    parametros.Add(new SqlParameter("@entregaHasta", SqlDbType.Time) { Scale = 0, Value = (object)fila.EntregaEstimadaHasta ?? DBNull.Value });
                    parametros.Add(new SqlParameter("@disponible", SqlDbType.Date) { Value = (object)fila.DisponibleEstimado?.Date ?? DBNull.Value });
                    parametros.Add(new SqlParameter("@esSalida", SqlDbType.Bit) { Value = (object)fila.EsSalidaNuestra ?? DBNull.Value });
                }
                int filas = await db.Database.ExecuteSqlCommandAsync(SqlInsertar(conAdjuntos, conEntrega), parametros.ToArray())
                    .ConfigureAwait(false);
                return filas > 0;
            }
        }

        public async Task<List<RemitenteMarcado>> LeerRemitentes()
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<RemitenteMarcado>(SQL_REMITENTES).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<int> GuardarRemitente(string agencia, string remitente, string usuario)
        {
            using (NVEntities db = crearContexto())
            {
                return (await db.Database.SqlQuery<int>(SQL_GUARDAR_REMITENTE, Texto("@agencia", agencia, 50), Texto("@remitente", remitente, 255),
                    Texto("@usuario", usuario, 50)).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            }
        }

        public async Task<bool> DesactivarRemitente(int id, string usuario)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.ExecuteSqlCommandAsync(SQL_DESACTIVAR_REMITENTE, new SqlParameter("@id", SqlDbType.Int) { Value = id },
                    Texto("@usuario", usuario, 50)).ConfigureAwait(false) > 0;
            }
        }

        public Task<List<PedidoCompraEnAire>> LeerPedidosEnAire(string empresa) =>
            LeerPedidos(SQL_PEDIDOS_EN_AIRE, SQL_PROVEEDORES_EN_AIRE, SQL_CONTACTOS_EN_AIRE, () => Empresas(empresa));

        public Task<List<PedidoCompraEnAire>> LeerPedidosDesde(string empresa, DateTime desde) =>
            LeerPedidos(SQL_PEDIDOS_DESDE, string.Format(SQL_PROVEEDORES, FILTRO_DESDE), string.Format(SQL_CONTACTOS, FILTRO_DESDE),
                () => Empresas(empresa).Concat(new[] { new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde } }).ToArray());

        private async Task<List<PedidoCompraEnAire>> LeerPedidos(string sqlPedidos, string sqlProveedores, string sqlContactos, Func<SqlParameter[]> parametros)
        {
            using (NVEntities db = crearContexto())
            {
                List<PedidoCompraEnAire> pedidos = (await db.Database.SqlQuery<FilaPedido>(sqlPedidos, parametros())
                    .ToListAsync().ConfigureAwait(false))
                    .Select(f => new PedidoCompraEnAire
                    {
                        Empresa = f.Empresa, Pedido = f.Pedido, Proveedor = f.Proveedor, FechaEnvio = f.FechaEnvio,
                        FechaPedido = f.FechaPedido, FechaRecibidoEntero = f.FechaRecibidoEntero
                    })
                    .ToList();
                if (!pedidos.Any())
                {
                    return pedidos;
                }
                List<FilaProveedor> proveedores = await db.Database.SqlQuery<FilaProveedor>(sqlProveedores, parametros())
                    .ToListAsync().ConfigureAwait(false);
                List<FilaContacto> contactos = await db.Database.SqlQuery<FilaContacto>(sqlContactos, parametros())
                    .ToListAsync().ConfigureAwait(false);
                foreach (PedidoCompraEnAire pedido in pedidos)
                {
                    FilaProveedor ficha = proveedores
                        .Where(p => p.Empresa == pedido.Empresa && p.Proveedor == pedido.Proveedor)
                        .OrderByDescending(p => p.ProveedorPrincipal)
                        .FirstOrDefault();
                    pedido.NombreProveedor = ficha?.Nombre;
                    pedido.Web = proveedores.Where(p => p.Empresa == pedido.Empresa && p.Proveedor == pedido.Proveedor)
                        .Select(p => p.Web).FirstOrDefault(w => !string.IsNullOrWhiteSpace(w));
                    pedido.Correos = contactos.Where(c => c.Empresa == pedido.Empresa && c.Proveedor == pedido.Proveedor)
                        .Select(c => c.Correo?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                return pedidos;
            }
        }

        public async Task<List<CorreoSeguimientoFila>> LeerPendientesDeMedir(DateTime desde)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<CorreoSeguimientoFila>(SQL_PENDIENTES_MEDIR,
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde }).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<CorreoSeguimientoFila>> LeerPendientesDeMedirPorId(DateTime desde, int despuesDeId, int cuantos)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<CorreoSeguimientoFila>(SQL_PENDIENTES_MEDIR_POR_ID,
                    new SqlParameter("@cuantos", SqlDbType.Int) { Value = Math.Max(1, cuantos) },
                    new SqlParameter("@despues", SqlDbType.Int) { Value = despuesDeId },
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde }).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<AlbaranPedidoCompra>> LeerAlbaranes(IEnumerable<CandidatoPedidoCompra> pedidos)
        {
            var resultado = new List<AlbaranPedidoCompra>();
            List<int> numeros = (pedidos ?? Enumerable.Empty<CandidatoPedidoCompra>()).Select(p => p.Pedido).Distinct().ToList();
            using (NVEntities db = crearContexto())
            {
                for (int i = 0; i < numeros.Count; i += TAMANO_LOTE_IN)
                {
                    List<int> lote = numeros.Skip(i).Take(TAMANO_LOTE_IN).ToList();
                    var parametros = new List<SqlParameter>(Empresas(Constantes.Empresas.EMPRESA_POR_DEFECTO));
                    var nombres = new List<string>();
                    for (int j = 0; j < lote.Count; j++)
                    {
                        nombres.Add("@n" + j);
                        parametros.Add(new SqlParameter("@n" + j, SqlDbType.Int) { Value = lote[j] });
                    }
                    resultado.AddRange(await db.Database.SqlQuery<AlbaranPedidoCompra>(string.Format(SQL_ALBARANES, string.Join(", ", nombres)),
                        parametros.ToArray()).ToListAsync().ConfigureAwait(false));
                }
            }
            return resultado;
        }

        public async Task GuardarReal(int id, AlbaranPedidoCompra albaran)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR_REAL,
                    new SqlParameter("@id", SqlDbType.Int) { Value = id },
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = albaran.Empresa },
                    new SqlParameter("@pedido", SqlDbType.Int) { Value = albaran.Pedido },
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = albaran.FechaAlbaran }).ConfigureAwait(false);
            }
        }

        public async Task<List<CorreoSeguimientoFila>> LeerCorreos(DateTime desde)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<CorreoSeguimientoFila>(
                    string.Format(SQL_CORREOS, AdjuntosDisponibles() ? COLUMNAS_ADJUNTOS : COLUMNAS_ADJUNTOS_NULAS,
                        EntregaEstimadaDisponible() ? COLUMNAS_ENTREGA : COLUMNAS_ENTREGA_NULAS),
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde }).ToListAsync().ConfigureAwait(false);
            }
        }

        private static SqlParameter[] Empresas(string empresa) => new[]
        {
            new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim() },
            new SqlParameter("@espejo", SqlDbType.Char, 3) { Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO }
        };

        private static SqlParameter Texto(string nombre, string valor, int longitud)
        {
            string recortado = valor == null ? null : longitud > 0 && valor.Length > longitud ? valor.Substring(0, longitud) : valor;
            return new SqlParameter(nombre, SqlDbType.NVarChar, longitud) { Value = (object)recortado ?? DBNull.Value };
        }
    }
}
