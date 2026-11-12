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
    }

    public interface IRepositorioCorreosSeguimiento
    {
        /// <summary>¿Se ha lanzado Scripts/Issue857_ComprasCorreosSeguimiento.sql? Sin las tablas no se hace nada.</summary>
        bool TablasExisten();
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

        internal const string SQL_PEDIDOS_EN_AIRE = @"
SELECT RTRIM(c.Empresa) AS Empresa, c.Número AS Pedido, RTRIM(MAX(c.NºProveedor)) AS Proveedor, MAX(cab.[Fecha Modificación]) AS FechaEnvio
FROM dbo.LinPedidoCmp c WITH (NOLOCK)
LEFT JOIN dbo.CabPedidoCmp cab WITH (NOLOCK) ON cab.Empresa = c.Empresa AND cab.Número = c.Número
WHERE " + FILTRO_EN_AIRE + @"
GROUP BY c.Empresa, c.Número";

        internal const string SQL_PROVEEDORES_EN_AIRE = @"
SELECT RTRIM(p.Empresa) AS Empresa, RTRIM(p.Número) AS Proveedor, RTRIM(p.Nombre) AS Nombre, RTRIM(p.Web) AS Web, p.ProveedorPrincipal
FROM dbo.Proveedores p WITH (NOLOCK)
WHERE p.Empresa IN (@empresa, @espejo)
  AND p.Número IN (SELECT DISTINCT c.NºProveedor FROM dbo.LinPedidoCmp c WITH (NOLOCK) WHERE " + FILTRO_EN_AIRE + ")";

        internal const string SQL_CONTACTOS_EN_AIRE = @"
SELECT RTRIM(pc.Empresa) AS Empresa, RTRIM(pc.NºProveedor) AS Proveedor, RTRIM(pc.CorreoElectrónico) AS Correo
FROM dbo.PersonasContactoProveedor pc WITH (NOLOCK)
WHERE pc.Empresa IN (@empresa, @espejo) AND pc.CorreoElectrónico IS NOT NULL AND pc.CorreoElectrónico LIKE '%@%'
  AND pc.NºProveedor IN (SELECT DISTINCT c.NºProveedor FROM dbo.LinPedidoCmp c WITH (NOLOCK) WHERE " + FILTRO_EN_AIRE + ")";

        internal const string SQL_PENDIENTES_MEDIR = @"
SELECT TOP 500 Id, MensajeId, Recibido, RemitenteDireccion, RemitenteDominio, Asunto, Agencia, Codigo, Url, Seguimientos, Extracto, ProveedorPropuesto,
    PedidosCandidatos, FechaCaptura, EmpresaReal, PedidoReal, FechaAlbaranReal
FROM dbo.ComprasCorreosSeguimiento WITH (NOLOCK)
WHERE PedidoReal IS NULL AND PedidosCandidatos IS NOT NULL AND Recibido >= @desde
ORDER BY Recibido";

        // Por la clave de LinPedidoCmp (Empresa, Número): un día de albarán por fila (un pedido recibido en dos veces da dos).
        internal const string SQL_ALBARANES = @"
SELECT RTRIM(l.Empresa) AS Empresa, l.Número AS Pedido, CAST(CAST(l.FechaAlbarán AS date) AS datetime) AS FechaAlbaran
FROM dbo.LinPedidoCmp l WITH (NOLOCK)
WHERE l.Empresa IN (@empresa, @espejo) AND l.Número IN ({0}) AND l.Estado >= 2 AND l.FechaAlbarán IS NOT NULL
GROUP BY l.Empresa, l.Número, CAST(l.FechaAlbarán AS date)";

        internal const string SQL_GUARDAR_REAL = "UPDATE dbo.ComprasCorreosSeguimiento SET EmpresaReal = @empresa, PedidoReal = @pedido, FechaAlbaranReal = @fecha " +
            "WHERE Id = @id AND PedidoReal IS NULL";

        internal const string SQL_CORREOS = @"
SELECT TOP 2000 Id, MensajeId, Recibido, RemitenteDireccion, RemitenteDominio, Asunto, Agencia, Codigo, Url, Seguimientos, Extracto, ProveedorPropuesto,
    PedidosCandidatos, FechaCaptura, EmpresaReal, PedidoReal, FechaAlbaranReal
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
            using (NVEntities db = crearContexto())
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR,
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
                    Texto("@candidatos", fila.PedidosCandidatos, 2000)).ConfigureAwait(false);
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

        public async Task<List<PedidoCompraEnAire>> LeerPedidosEnAire(string empresa)
        {
            using (NVEntities db = crearContexto())
            {
                List<PedidoCompraEnAire> pedidos = (await db.Database.SqlQuery<FilaPedido>(SQL_PEDIDOS_EN_AIRE, Empresas(empresa))
                    .ToListAsync().ConfigureAwait(false))
                    .Select(f => new PedidoCompraEnAire { Empresa = f.Empresa, Pedido = f.Pedido, Proveedor = f.Proveedor, FechaEnvio = f.FechaEnvio })
                    .ToList();
                if (!pedidos.Any())
                {
                    return pedidos;
                }
                List<FilaProveedor> proveedores = await db.Database.SqlQuery<FilaProveedor>(SQL_PROVEEDORES_EN_AIRE, Empresas(empresa))
                    .ToListAsync().ConfigureAwait(false);
                List<FilaContacto> contactos = await db.Database.SqlQuery<FilaContacto>(SQL_CONTACTOS_EN_AIRE, Empresas(empresa))
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
                return await db.Database.SqlQuery<CorreoSeguimientoFila>(SQL_CORREOS,
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
