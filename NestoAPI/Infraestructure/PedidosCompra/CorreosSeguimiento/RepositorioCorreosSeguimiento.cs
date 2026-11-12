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

        internal const string SQL_INSERTAR = @"
INSERT INTO dbo.ComprasCorreosSeguimiento (MensajeId, Recibido, RemitenteDireccion, RemitenteDominio, Asunto, Agencia, Codigo, Url, Seguimientos,
    Extracto, ProveedorPropuesto, PedidosCandidatos, FechaCaptura)
SELECT @mensajeId, @recibido, @remitente, @dominio, @asunto, @agencia, @codigo, @url, @seguimientos, @extracto, @proveedor, @candidatos, GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM dbo.ComprasCorreosSeguimiento WITH (UPDLOCK, HOLDLOCK) WHERE MensajeId = @mensajeId)";

        internal const string SQL_EXISTEN_ADJUNTOS = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'NumAdjuntos') IS NULL " +
            "OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'Adjuntos') IS NULL THEN 0 ELSE 1 END AS bit)";

        // Con las columnas de Scripts/Issue857b_ComprasCorreosHistoricoYAdjuntos.sql
        internal const string SQL_INSERTAR_CON_ADJUNTOS = @"
INSERT INTO dbo.ComprasCorreosSeguimiento (MensajeId, Recibido, RemitenteDireccion, RemitenteDominio, Asunto, Agencia, Codigo, Url, Seguimientos,
    Extracto, ProveedorPropuesto, PedidosCandidatos, FechaCaptura, NumAdjuntos, Adjuntos)
SELECT @mensajeId, @recibido, @remitente, @dominio, @asunto, @agencia, @codigo, @url, @seguimientos, @extracto, @proveedor, @candidatos, GETDATE(),
    @numAdjuntos, @adjuntos
WHERE NOT EXISTS (SELECT 1 FROM dbo.ComprasCorreosSeguimiento WITH (UPDLOCK, HOLDLOCK) WHERE MensajeId = @mensajeId)";

        internal const string COLUMNAS_ADJUNTOS = "NumAdjuntos, Adjuntos";
        internal const string COLUMNAS_ADJUNTOS_NULAS = "CAST(NULL AS int) AS NumAdjuntos, CAST(NULL AS nvarchar(2000)) AS Adjuntos";
        /// <summary>Tras la comprobación de las columnas, cuánto vale lo sabido (así, lanzado el script, se nota sin reiniciar).</summary>
        internal static readonly TimeSpan CACHE_ADJUNTOS = TimeSpan.FromMinutes(10);
        private static readonly object bloqueoAdjuntos = new object();
        private static bool? adjuntosDisponibles;
        private static DateTime adjuntosComprobadosUtc;

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
SELECT TOP 500 " + COLUMNAS_FILA + ", " + COLUMNAS_ADJUNTOS_NULAS + @"
FROM dbo.ComprasCorreosSeguimiento WITH (NOLOCK)
WHERE PedidoReal IS NULL AND PedidosCandidatos IS NOT NULL AND Recibido >= @desde
ORDER BY Recibido";

        // Por la clave (Id, el índice agrupado), de @cuantos en @cuantos
        internal const string SQL_PENDIENTES_MEDIR_POR_ID = @"
SELECT TOP (@cuantos) " + COLUMNAS_FILA + ", " + COLUMNAS_ADJUNTOS_NULAS + @"
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

        // {0} = las columnas de los adjuntos (o NULL sin el script Issue857b)
        internal const string SQL_CORREOS = @"
SELECT TOP 2000 " + COLUMNAS_FILA + @", {0}
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

        public bool AdjuntosDisponibles()
        {
            lock (bloqueoAdjuntos)
            {
                if (adjuntosDisponibles.HasValue && DateTime.UtcNow - adjuntosComprobadosUtc < CACHE_ADJUNTOS)
                {
                    return adjuntosDisponibles.Value;
                }
            }
            bool existen;
            try
            {
                using (NVEntities db = crearContexto())
                {
                    existen = db.Database.SqlQuery<bool>(SQL_EXISTEN_ADJUNTOS).FirstOrDefault();
                }
            }
            catch (Exception)
            {
                existen = false;
            }
            lock (bloqueoAdjuntos)
            {
                adjuntosDisponibles = existen;
                adjuntosComprobadosUtc = DateTime.UtcNow;
            }
            return existen;
        }

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
                int filas = await db.Database.ExecuteSqlCommandAsync(conAdjuntos ? SQL_INSERTAR_CON_ADJUNTOS : SQL_INSERTAR, parametros.ToArray())
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
                    string.Format(SQL_CORREOS, AdjuntosDisponibles() ? COLUMNAS_ADJUNTOS : COLUMNAS_ADJUNTOS_NULAS),
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
