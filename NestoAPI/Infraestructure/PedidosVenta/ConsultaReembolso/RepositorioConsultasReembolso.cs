using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso
{
    /// <summary>NestoAPI#734: la tabla ConsultasReembolsoPedido y lo que hace falta del pedido, sus enlaces de pago y sus envíos.</summary>
    public interface IRepositorioConsultasReembolso
    {
        /// <summary>¿Está lanzado Scripts/Issue734_ConsultasReembolso.sql?</summary>
        bool TablaExiste();

        /// <summary>La consulta abierta del pedido (Respondida NULL) o null.</summary>
        Task<ConsultaReembolsoDTO> LeerAbierta(string empresa, int pedido);

        /// <summary>La última consulta del pedido (abierta o contestada) o null. Null también si no hay tabla.</summary>
        Task<ConsultaReembolsoDTO> LeerUltima(string empresa, int pedido);

        /// <summary>Las abiertas de la empresa (con <paramref name="soloAbiertas"/>) o, sin él, también las contestadas de los últimos días.</summary>
        Task<List<ConsultaReembolsoListadoDTO>> LeerListado(string empresa, bool soloAbiertas);

        /// <summary>Inserta y devuelve el Id.</summary>
        Task<int> Insertar(ConsultaReembolsoDTO consulta);

        /// <summary>Guarda la respuesta (solo si seguía abierta). True si la ha contestado ESTA llamada.</summary>
        Task<bool> Responder(ConsultaReembolsoDTO consulta);

        /// <summary>Deshace una respuesta que no se ha podido aplicar: la consulta vuelve a estar abierta.</summary>
        Task Reabrir(int id);

        /// <summary>Apunta en la consulta (ya contestada) los Id de PagosTPV anulados, separados por comas.</summary>
        Task ApuntarEnlacesAnulados(int id, string anulados);

        /// <summary>Los enlaces de pago (TPV virtual) pendientes o autorizados del pedido.</summary>
        Task<List<EnlacePagoPedido>> LeerEnlaces(string empresa, int pedido);

        /// <summary>Forma de pago, plazos, CCC… del pedido y la cuenta del cliente; null si el pedido no existe.</summary>
        Task<DatosPagoPedidoConsulta> LeerDatosPago(string empresa, int pedido);

        /// <summary>La forma de pago de la empresa, o null si no existe.</summary>
        Task<FormaPagoConsulta> LeerFormaPago(string empresa, string formaPago);

        Task<bool> ExistenPlazosPago(string empresa, string plazosPago);

        /// <summary>Lo que cobraría hoy la agencia (GET PedidosVenta/ImporteReembolso).</summary>
        decimal ImporteReembolso(string empresa, int pedido);

        /// <summary>Lo pagado por adelantado: prepagos sin facturar y enlaces de pago cobrados (#569, #679).</summary>
        decimal PrepagosPendientes(string empresa, int pedido);

        /// <summary>
        /// Cambia la forma de pago, los plazos y el CCC del pedido (y Usuario y Fecha Modificación, para que Nesto vea que ha
        /// cambiado) y deja constancia en Modificaciones de quién y por qué.
        /// </summary>
        Task CambiarFormaPago(string empresa, int pedido, string formaPago, string plazosPago, string ccc, string usuario, string anterior, string nuevo);

        /// <summary>Anula esos enlaces (PagosTPV TPV virtual) si siguen pendientes. Devuelve los que ha anulado.</summary>
        Task<List<int>> AnularEnlaces(string empresa, IEnumerable<int> ids);

        /// <summary>Los envíos del pedido.</summary>
        Task<List<EnviosAgencia>> LeerEnvios(string empresa, int pedido);
    }

    /// <summary>
    /// NestoAPI#734: SQL a mano, fuera del EDMX, como PedidosRetenidos (#698): si el script no se ha lanzado, la API no se cae;
    /// los endpoints contestan que falta la tabla y la propuesta de envío sigue como hasta ahora. Cada operación abre su contexto.
    /// </summary>
    public class RepositorioConsultasReembolso : IRepositorioConsultasReembolso
    {
        /// <summary>Sin <c>abiertas</c>, el listado trae también las contestadas de estos últimos días.</summary>
        public const int DIAS_CONTESTADAS = 30;

        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.ConsultasReembolsoPedido', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string COLUMNAS = "r.Id, RTRIM(r.Empresa) AS Empresa, r.Pedido, r.ImporteEnlaces, r.NumeroEnlaces, r.RetencionId, r.Comentario, " +
            "RTRIM(r.Usuario) AS Usuario, r.Fecha, r.Respondida, RTRIM(r.UsuarioRespuesta) AS UsuarioRespuesta, r.ConReembolso, r.AnularEnlaces, " +
            "r.EnlacesAnulados, RTRIM(r.FormaPagoAnterior) AS FormaPagoAnterior, RTRIM(r.PlazosPagoAnterior) AS PlazosPagoAnterior, " +
            "RTRIM(r.FormaPago) AS FormaPago, RTRIM(r.PlazosPago) AS PlazosPago, r.ComentarioRespuesta";

        internal const string SQL_ABIERTA = "SELECT TOP 1 " + COLUMNAS + " FROM dbo.ConsultasReembolsoPedido r WITH (NOLOCK) " +
            "WHERE r.Empresa = @empresa AND r.Pedido = @pedido AND r.Respondida IS NULL ORDER BY r.Id DESC";

        internal const string SQL_ULTIMA = "SELECT TOP 1 " + COLUMNAS + " FROM dbo.ConsultasReembolsoPedido r WITH (NOLOCK) " +
            "WHERE r.Empresa = @empresa AND r.Pedido = @pedido ORDER BY r.Id DESC";

        internal const string SQL_LISTADO = "SELECT " + COLUMNAS + ", RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto, " +
            "RTRIM(cl.Nombre) AS NombreCliente, RTRIM(c.Vendedor) AS Vendedor " +
            "FROM dbo.ConsultasReembolsoPedido r WITH (NOLOCK) " +
            "LEFT JOIN dbo.CabPedidoVta c WITH (NOLOCK) ON c.Empresa = r.Empresa AND c.Número = r.Pedido " +
            "LEFT JOIN dbo.Clientes cl WITH (NOLOCK) ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto " +
            "WHERE r.Empresa = @empresa AND (r.Respondida IS NULL OR (@soloAbiertas = 0 AND r.Respondida >= @desde)) " +
            "ORDER BY CASE WHEN r.Respondida IS NULL THEN 0 ELSE 1 END, r.Fecha, r.Id";

        internal const string SQL_INSERTAR = "INSERT INTO dbo.ConsultasReembolsoPedido (Empresa, Pedido, ImporteEnlaces, NumeroEnlaces, RetencionId, Comentario, Usuario, Fecha) " +
            "VALUES (@empresa, @pedido, @importe, @numero, @retencion, @comentario, @usuario, @fecha); SELECT CAST(SCOPE_IDENTITY() AS int)";

        internal const string SQL_RESPONDER = "UPDATE dbo.ConsultasReembolsoPedido SET Respondida = @respondida, UsuarioRespuesta = @usuario, " +
            "ConReembolso = @conReembolso, AnularEnlaces = @anular, EnlacesAnulados = @anulados, FormaPagoAnterior = @fpAnterior, " +
            "PlazosPagoAnterior = @plAnterior, FormaPago = @fp, PlazosPago = @pl, ComentarioRespuesta = @comentario " +
            "WHERE Id = @id AND Respondida IS NULL";

        internal const string SQL_REABRIR = "UPDATE dbo.ConsultasReembolsoPedido SET Respondida = NULL, UsuarioRespuesta = NULL, ConReembolso = NULL, " +
            "AnularEnlaces = NULL, EnlacesAnulados = NULL, FormaPagoAnterior = NULL, PlazosPagoAnterior = NULL, FormaPago = NULL, PlazosPago = NULL, " +
            "ComentarioRespuesta = NULL WHERE Id = @id";

        internal const string SQL_ANULADOS = "UPDATE dbo.ConsultasReembolsoPedido SET EnlacesAnulados = @anulados WHERE Id = @id";

        internal const string SQL_DATOS_PAGO = @"SELECT RTRIM(c.Empresa) AS Empresa, c.Número AS Pedido, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto,
       RTRIM(cl.Nombre) AS NombreCliente, RTRIM(c.[Forma Pago]) AS FormaPago, RTRIM(c.PlazosPago) AS PlazosPago, RTRIM(c.CCC) AS CCC,
       RTRIM(c.[Periodo Facturacion]) AS PeriodoFacturacion, c.NotaEntrega, RTRIM(cl.CCC) AS CCCCliente
FROM dbo.CabPedidoVta c WITH (NOLOCK)
LEFT JOIN dbo.Clientes cl WITH (NOLOCK) ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto
WHERE c.Empresa = @empresa AND c.Número = @pedido";

        internal const string SQL_FORMA_PAGO = "SELECT RTRIM(f.Número) AS Numero, RTRIM(f.Descripción) AS Descripcion, f.CCCObligatorio " +
            "FROM dbo.FormasPago f WITH (NOLOCK) WHERE f.Empresa = @empresa AND f.Número = @forma";

        internal const string SQL_PLAZOS = "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.PlazosPago p WITH (NOLOCK) WHERE p.Empresa = @empresa AND p.Número = @plazos) " +
            "THEN 1 ELSE 0 END AS bit)";

        // Solo esas columnas (y Usuario/Fecha Modificación, que Nesto mira para saber que el pedido ha cambiado).
        internal const string SQL_CAMBIAR_FORMA_PAGO = "UPDATE dbo.CabPedidoVta SET [Forma Pago] = @forma, PlazosPago = @plazos, CCC = @ccc, " +
            "Usuario = @usuario, [Fecha Modificación] = GETDATE() WHERE Empresa = @empresa AND Número = @pedido";

        internal const string SQL_MODIFICACION = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES ('CabPedidoVta', @anterior, @nuevo, @usuario)";

        // Solo los enlaces de pago de verdad (TPV virtual) que siguen pendientes: uno cobrado entretanto no se toca. OUTPUT INTO
        // una variable (y no OUTPUT a secas) para que no falle si algún día PagosTPV tiene triggers.
        internal const string SQL_ANULAR_ENLACES = "DECLARE @anulados TABLE (Id int); " +
            "UPDATE dbo.PagosTPV SET Estado = @cancelado, FechaActualizacion = GETDATE() OUTPUT inserted.Id INTO @anulados " +
            "WHERE Empresa = @empresa AND Tipo = @tipo AND Estado = @pendiente AND Id IN ({0}); SELECT Id FROM @anulados ORDER BY Id";

        private static readonly object candado = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;
        private static readonly TimeSpan cadaCuanto = TimeSpan.FromMinutes(5);

        private readonly Func<NVEntities> crearContexto;
        private readonly Func<IServicioPedidosVenta> servicioPedidosVenta;

        public RepositorioConsultasReembolso(Func<NVEntities> crearContexto = null, Func<IServicioPedidosVenta> servicioPedidosVenta = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
            this.servicioPedidosVenta = servicioPedidosVenta ?? (() => new ServicioPedidosVenta());
        }

        public bool TablaExiste()
        {
            lock (candado)
            {
                if (existe || DateTime.Now - ultimaComprobacion < cadaCuanto)
                {
                    return existe;
                }
                ultimaComprobacion = DateTime.Now;
            }
            bool ahora;
            try
            {
                using (NVEntities db = crearContexto())
                {
                    ahora = db.Database.SqlQuery<bool>(SQL_EXISTE).FirstOrDefault();
                }
            }
            catch
            {
                ahora = false;
            }
            lock (candado)
            {
                existe = ahora;
            }
            return ahora;
        }

        public Task<ConsultaReembolsoDTO> LeerAbierta(string empresa, int pedido) => LeerUna(SQL_ABIERTA, empresa, pedido);

        public Task<ConsultaReembolsoDTO> LeerUltima(string empresa, int pedido) => LeerUna(SQL_ULTIMA, empresa, pedido);

        private async Task<ConsultaReembolsoDTO> LeerUna(string sql, string empresa, int pedido)
        {
            if (!TablaExiste())
            {
                return null;
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<ConsultaReembolsoDTO>(sql, ParametroEmpresa(empresa), ParametroPedido(pedido))
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<ConsultaReembolsoListadoDTO>> LeerListado(string empresa, bool soloAbiertas)
        {
            if (!TablaExiste())
            {
                return new List<ConsultaReembolsoListadoDTO>();
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<ConsultaReembolsoListadoDTO>(SQL_LISTADO, ParametroEmpresa(empresa),
                    new SqlParameter("@soloAbiertas", SqlDbType.Bit) { Value = soloAbiertas },
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = DateTime.Today.AddDays(-DIAS_CONTESTADAS) })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<int> Insertar(ConsultaReembolsoDTO consulta)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<int>(SQL_INSERTAR,
                    ParametroEmpresa(consulta.Empresa),
                    ParametroPedido(consulta.Pedido),
                    new SqlParameter("@importe", SqlDbType.Decimal) { Precision = 12, Scale = 2, Value = consulta.ImporteEnlaces },
                    new SqlParameter("@numero", SqlDbType.Int) { Value = consulta.NumeroEnlaces },
                    new SqlParameter("@retencion", SqlDbType.Int) { Value = (object)consulta.RetencionId ?? DBNull.Value },
                    Texto("@comentario", consulta.Comentario, 500),
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = consulta.Usuario ?? string.Empty },
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = consulta.Fecha })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> Responder(ConsultaReembolsoDTO consulta)
        {
            using (NVEntities db = crearContexto())
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(SQL_RESPONDER,
                    new SqlParameter("@respondida", SqlDbType.DateTime) { Value = (object)consulta.Respondida ?? DBNull.Value },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = consulta.UsuarioRespuesta ?? string.Empty },
                    new SqlParameter("@conReembolso", SqlDbType.Bit) { Value = (object)consulta.ConReembolso ?? DBNull.Value },
                    new SqlParameter("@anular", SqlDbType.Bit) { Value = (object)consulta.AnularEnlaces ?? DBNull.Value },
                    Texto("@anulados", consulta.EnlacesAnulados, 500),
                    Texto("@fpAnterior", consulta.FormaPagoAnterior, 3),
                    Texto("@plAnterior", consulta.PlazosPagoAnterior, 10),
                    Texto("@fp", consulta.FormaPago, 3),
                    Texto("@pl", consulta.PlazosPago, 10),
                    Texto("@comentario", consulta.ComentarioRespuesta, 500),
                    new SqlParameter("@id", SqlDbType.Int) { Value = consulta.Id }).ConfigureAwait(false);
                return filas > 0;
            }
        }

        public async Task Reabrir(int id)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_REABRIR, new SqlParameter("@id", SqlDbType.Int) { Value = id }).ConfigureAwait(false);
            }
        }

        public async Task ApuntarEnlacesAnulados(int id, string anulados)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_ANULADOS, Texto("@anulados", anulados, 500),
                    new SqlParameter("@id", SqlDbType.Int) { Value = id }).ConfigureAwait(false);
            }
        }

        public Task<List<EnlacePagoPedido>> LeerEnlaces(string empresa, int pedido)
        {
            using (NVEntities db = crearContexto())
            {
                return Task.FromResult(LectorEnlacesPagoPedido.Leer(db, empresa, pedido));
            }
        }

        public async Task<DatosPagoPedidoConsulta> LeerDatosPago(string empresa, int pedido)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<DatosPagoPedidoConsulta>(SQL_DATOS_PAGO, ParametroEmpresa(empresa), ParametroPedido(pedido))
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<FormaPagoConsulta> LeerFormaPago(string empresa, string formaPago)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<FormaPagoConsulta>(SQL_FORMA_PAGO, ParametroEmpresa(empresa),
                    new SqlParameter("@forma", SqlDbType.Char, 3) { Value = formaPago?.Trim() ?? string.Empty })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> ExistenPlazosPago(string empresa, string plazosPago)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<bool>(SQL_PLAZOS, ParametroEmpresa(empresa),
                    new SqlParameter("@plazos", SqlDbType.Char, 10) { Value = plazosPago?.Trim() ?? string.Empty })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public decimal ImporteReembolso(string empresa, int pedido)
            => new GestorPedidosVenta(servicioPedidosVenta()).ImporteReembolso(Empresa(empresa), pedido);

        public decimal PrepagosPendientes(string empresa, int pedido)
            => new GestorPedidosVenta(servicioPedidosVenta()).ImportePrepagosPendientes(Empresa(empresa), pedido);

        public async Task CambiarFormaPago(string empresa, int pedido, string formaPago, string plazosPago, string ccc, string usuario, string anterior, string nuevo)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_CAMBIAR_FORMA_PAGO,
                    new SqlParameter("@forma", SqlDbType.Char, 3) { Value = formaPago },
                    new SqlParameter("@plazos", SqlDbType.Char, 10) { Value = (object)plazosPago ?? DBNull.Value },
                    new SqlParameter("@ccc", SqlDbType.Char, 3) { Value = (object)ccc ?? DBNull.Value },
                    new SqlParameter("@usuario", SqlDbType.VarChar, 30) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) },
                    ParametroEmpresa(empresa), ParametroPedido(pedido)).ConfigureAwait(false);
                // Con el usuario de verdad (como #698): con EF, Usuario caería en su DEFAULT suser_sname().
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_MODIFICACION,
                    new SqlParameter("@anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                    new SqlParameter("@nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) }).ConfigureAwait(false);
            }
        }

        public async Task<List<int>> AnularEnlaces(string empresa, IEnumerable<int> ids)
        {
            List<int> lista = (ids ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().ToList();
            if (!lista.Any())
            {
                return new List<int>();
            }
            using (NVEntities db = crearContexto())
            {
                // Enteros: van en el texto sin riesgo de inyección.
                string sql = string.Format(CultureInfo.InvariantCulture, SQL_ANULAR_ENLACES, string.Join(", ", lista));
                return await db.Database.SqlQuery<int>(sql,
                    new SqlParameter("@cancelado", SqlDbType.NVarChar, 20) { Value = Constantes.EstadosPagoTPV.CANCELADO },
                    // PagosTPV.Empresa es nvarchar(2), no char(3) como CabPedidoVta.
                    new SqlParameter("@empresa", SqlDbType.NVarChar, 3) { Value = Empresa(empresa) },
                    new SqlParameter("@tipo", SqlDbType.NVarChar, 20) { Value = Constantes.TiposPagoTPV.TPV_VIRTUAL },
                    new SqlParameter("@pendiente", SqlDbType.NVarChar, 20) { Value = Constantes.EstadosPagoTPV.PENDIENTE })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<EnviosAgencia>> LeerEnvios(string empresa, int pedido)
        {
            string empresaPedido = Empresa(empresa);
            using (NVEntities db = crearContexto())
            {
                return await db.EnviosAgencias.AsNoTracking()
                    .Where(e => e.Empresa == empresaPedido && e.Pedido == pedido)
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        private static SqlParameter Texto(string nombre, string valor, int longitud)
        {
            string limpio = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            if (limpio != null && limpio.Length > longitud)
            {
                limpio = limpio.Substring(0, longitud);
            }
            return new SqlParameter(nombre, SqlDbType.NVarChar, longitud) { Value = (object)limpio ?? DBNull.Value };
        }

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        private static SqlParameter ParametroEmpresa(string empresa) => new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = Empresa(empresa) };

        private static SqlParameter ParametroPedido(int pedido) => new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido };
    }
}
