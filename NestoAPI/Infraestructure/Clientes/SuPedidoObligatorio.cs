using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Configuration;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#723 (28/10/26, Laura Magán / Chanel): un cliente puede exigir el P.O. («Su pedido») en todos sus pedidos. La marca vive
    /// en ClientesSuPedidoObligatorio (tabla aparte, fuera del EDMX; Scripts/Issue723_ClientesSuPedidoObligatorio.sql). Con ella:
    /// no se graba un pedido sin P.O. (presupuestos aparte), no se factura hasta que lo tenga, y el P.O. de una factura ya emitida
    /// se puede poner después (no entra en el registro de Verifactu). Todo detrás del interruptor Pedidos:SuPedidoObligatorio.
    /// </summary>
    public static class ReglaSuPedidoObligatorio
    {
        public const string CLAVE_INTERRUPTOR = "Pedidos:SuPedidoObligatorio";

        /// <summary>Para los tests. Null = el Web.config (sin clave o distinto de true, apagado).</summary>
        internal static Func<bool> Interruptor { get; set; }

        public static bool Activa
        {
            get
            {
                if (Interruptor != null)
                {
                    return Interruptor();
                }
                try
                {
                    return string.Equals(ConfigurationManager.AppSettings[CLAVE_INTERRUPTOR]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool TieneSuPedido(string suPedido) => !string.IsNullOrWhiteSpace(suPedido);

        /// <summary>El motivo para no grabar el pedido, o null. Los presupuestos no lo exigen (todavía no es un pedido).</summary>
        public static string MotivoAlGrabar(PedidoVentaDTO pedido, bool clienteExige)
        {
            if (pedido == null || !clienteExige || pedido.EsPresupuesto || TieneSuPedido(pedido.suPedido))
            {
                return null;
            }
            return $"El cliente {pedido.cliente?.Trim()} exige el número de pedido (P.O.) en todos sus pedidos: indíquelo en «Su pedido» antes de grabar.";
        }

        /// <summary>El motivo para no facturar el pedido, o null.</summary>
        public static string MotivoAlFacturar(string cliente, int pedido, string suPedido, bool clienteExige)
        {
            if (!clienteExige || TieneSuPedido(suPedido))
            {
                return null;
            }
            return $"El pedido {pedido} no se puede facturar: el cliente {cliente?.Trim()} exige el número de pedido (P.O.) y el pedido no lo tiene. Póngalo en «Su pedido» del pedido.";
        }
    }

    public interface IRepositorioSuPedidoObligatorio
    {
        Task<bool> Exige(string empresa, string cliente);
        Task Poner(string empresa, string cliente, bool exige, string usuario);
    }

    /// <summary>La tabla ClientesSuPedidoObligatorio con SqlClient y su propio contexto (como PedidosRetenidos).</summary>
    public class RepositorioSuPedidoObligatorio : IRepositorioSuPedidoObligatorio
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.ClientesSuPedidoObligatorio', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_EXIGE = "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ClientesSuPedidoObligatorio WITH (NOLOCK) WHERE Empresa = @empresa AND Cliente = @cliente) THEN 1 ELSE 0 END AS bit)";
        internal const string SQL_PONER = "IF NOT EXISTS (SELECT 1 FROM dbo.ClientesSuPedidoObligatorio WHERE Empresa = @empresa AND Cliente = @cliente) " +
            "INSERT INTO dbo.ClientesSuPedidoObligatorio (Empresa, Cliente, Usuario) VALUES (@empresa, @cliente, @usuario)";
        internal const string SQL_QUITAR = "DELETE FROM dbo.ClientesSuPedidoObligatorio WHERE Empresa = @empresa AND Cliente = @cliente";

        private static readonly object candado = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;
        private static readonly TimeSpan cadaCuanto = TimeSpan.FromMinutes(5);
        private readonly Func<NVEntities> crearContexto;

        public RepositorioSuPedidoObligatorio(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
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

        public async Task<bool> Exige(string empresa, string cliente)
        {
            if (!ReglaSuPedidoObligatorio.Activa || string.IsNullOrWhiteSpace(cliente) || !TablaExiste())
            {
                return false;
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<bool>(SQL_EXIGE, Empresa(empresa), Cliente(cliente)).FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Lo mismo, síncrono y sin lanzar (para los gates de facturación): si algo falla, se registra y no exige.</summary>
        public bool ExigeSincronoSeguro(string empresa, string cliente)
        {
            try
            {
                if (!ReglaSuPedidoObligatorio.Activa || string.IsNullOrWhiteSpace(cliente) || !TablaExiste())
                {
                    return false;
                }
                using (NVEntities db = crearContexto())
                {
                    return db.Database.SqlQuery<bool>(SQL_EXIGE, Empresa(empresa), Cliente(cliente)).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[P.O. obligatorio #723] No se ha podido leer si el cliente {cliente?.Trim()} exige P.O.: {ex.Message}", ex));
                return false;
            }
        }

        public async Task Poner(string empresa, string cliente, bool exige, string usuario)
        {
            if (!TablaExiste())
            {
                throw new NestoBusinessException("Falta la tabla ClientesSuPedidoObligatorio (Scripts/Issue723_ClientesSuPedidoObligatorio.sql). Avise a Informática.");
            }
            using (NVEntities db = crearContexto())
            {
                if (exige)
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_PONER, Empresa(empresa), Cliente(cliente),
                        new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = (object)usuario?.Trim() ?? "NestoAPI" }).ConfigureAwait(false);
                }
                else
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_QUITAR, Empresa(empresa), Cliente(cliente)).ConfigureAwait(false);
                }
            }
        }

        private static SqlParameter Empresa(string empresa)
            => new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim() };
        private static SqlParameter Cliente(string cliente) => new SqlParameter("@cliente", SqlDbType.Char, 10) { Value = cliente.Trim() };
    }

    /// <summary>NestoAPI#723: poner o cambiar el P.O. de una factura ya emitida (y de su pedido), con constancia en Modificaciones.</summary>
    public static class SuPedidoFactura
    {
        public const int LONGITUD_MAXIMA = 50;

        /// <summary>El valor limpio o el motivo de rechazo (out). Null = vale.</summary>
        public static string Normalizar(string suPedido, out string limpio)
        {
            limpio = suPedido?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return "Indique el número de pedido (P.O.) del cliente.";
            }
            if (limpio.Length > LONGITUD_MAXIMA)
            {
                return $"El P.O. no puede tener más de {LONGITUD_MAXIMA} caracteres.";
            }
            return null;
        }

        /// <summary>Devuelve el P.O. anterior. Lanza NestoBusinessException (404) si la factura no existe.</summary>
        public static async Task<string> Cambiar(NVEntities db, string empresa, string numeroFactura, string suPedido, string usuario)
        {
            string motivo = Normalizar(suPedido, out string limpio);
            if (motivo != null)
            {
                throw new NestoBusinessException(motivo) { StatusCode = System.Net.HttpStatusCode.BadRequest };
            }
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string numero = numeroFactura?.Trim();
            CabFacturaVta factura = await db.CabsFacturasVtas.FirstOrDefaultAsync(f => f.Empresa == empresaLimpia && f.Número == numero).ConfigureAwait(false);
            if (factura == null)
            {
                throw new NestoBusinessException($"No existe la factura {numero} en la empresa {empresaLimpia}.") { StatusCode = System.Net.HttpStatusCode.NotFound };
            }
            string anterior = factura.SuPedido?.Trim();
            factura.SuPedido = limpio;
            // El pedido del que salió, si lo tiene: así la próxima factura del mismo pedido (parciales) ya lo lleva
            foreach (CabPedidoVta pedido in await db.CabPedidoVtas
                .Where(p => p.Empresa == empresaLimpia && p.LinPedidoVtas.Any(l => l.Nº_Factura == numero)).ToListAsync().ConfigureAwait(false))
            {
                pedido.SuPedido = limpio;
            }
            db.Modificaciones.Add(new Modificacion
            {
                Tabla = "CabFacturaVta",
                Anterior = $"Factura {numero} SuPedido={(string.IsNullOrEmpty(anterior) ? "(vacío)" : anterior)}",
                Nuevo = $"SuPedido={limpio} (P.O. puesto en factura emitida #723)",
                Usuario = usuario
            });
            await db.SaveChangesAsync().ConfigureAwait(false);
            return anterior;
        }
    }
}
