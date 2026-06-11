using NestoAPI.Infraestructure.CanalesExternos.Amazon;
using NestoAPI.Models;
using NestoAPI.Models.CanalesExternos;
using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#225: sirve la credencial LWA de Amazon vigente (tabla dbo.AmazonSpApiCredencial,
    /// que mantiene el job de rotación) para que Nesto la consuma en arranque en vez de leerla de
    /// su clavesSecretas.config. Es el cierre del bucle de la auto-rotación: sin esto, al rotar el
    /// secreto los clientes de escritorio se quedarían con el antiguo (muere a los 7 días).
    /// </summary>
    [Authorize]
    public class CredencialesAmazonController : ApiController
    {
        private readonly NVEntities db;
        private readonly IAmazonCredencialStore store;

        public CredencialesAmazonController()
        {
            db = new NVEntities();
            store = new AmazonCredencialStore(db);
        }

        public CredencialesAmazonController(IAmazonCredencialStore store)
        {
            this.store = store;
        }

        // Solo los grupos que ejecutan la integración Amazon: TiendaOnline (módulo de pedidos de
        // CanalesExternos) y Administración (módulo de pagos). El secreto no debe llegar a
        // vendedores ni a clientes de la tienda online.
        internal static readonly string[] GruposAutorizados = { "TiendaOnline", "Administración" };

        [HttpGet]
        [Route("api/CredencialesAmazon")]
        [ResponseType(typeof(CredencialAmazonDTO))]
        public IHttpActionResult GetCredencialAmazon()
        {
            if (!UsuarioEnGrupoAutorizado(User))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }

            AmazonSpApiCredencial credencial = store.Obtener();
            if (credencial == null)
            {
                return NotFound();
            }

            return Ok(new CredencialAmazonDTO
            {
                ClientId = credencial.ClientId?.Trim(),
                ClientSecret = credencial.ClientSecret?.Trim(),
                RefreshToken = credencial.RefreshToken?.Trim(),
                SecretExpiry = credencial.SecretExpiry
            });
        }

        /// <summary>
        /// El JWT de /api/auth/windows-token mete los grupos AD del usuario como claims de rol en
        /// formato "DOMINIO\Grupo"; se compara sin el dominio para no acoplar el código al nombre
        /// NetBIOS. Los tokens de NestoApp y TiendasNuevaVision no llevan estos grupos, así que
        /// quedan excluidos de serie.
        /// </summary>
        internal static bool UsuarioEnGrupoAutorizado(IPrincipal usuario)
        {
            if (!(usuario is ClaimsPrincipal claims))
            {
                return false;
            }
            return claims.Claims
                .Where(c => c.Type == ClaimTypes.Role && !string.IsNullOrEmpty(c.Value))
                .Select(c => SinDominio(c.Value))
                .Any(g => GruposAutorizados.Contains(g, StringComparer.OrdinalIgnoreCase));
        }

        private static string SinDominio(string rol)
        {
            int barra = rol.LastIndexOf('\\');
            return barra >= 0 ? rol.Substring(barra + 1) : rol;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
