using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// [Authorize] de api/Almacen (Ariadna): para LEER basta con estar identificado; para ESCRIBIR
    /// (cualquier petición que no sea GET: escaneos, fotos de bultos, ubicar…) hay que ser de Almacén
    /// o de Dirección. Vale igual para los dos caminos de entrada: el rol de Identity «Almacén» de los
    /// mozos que entran por /oauth/token y el grupo de Windows NUEVAVISION\Almacén de Nesto
    /// (IsInRoleSinDominio quita el dominio).
    ///
    /// <para>Así el usuario RevisionGoogle (los revisores de Google Play, sin roles) puede recorrer la
    /// app pero no escribe datos reales. Al ir en el controlador, cualquier POST nuevo queda protegido
    /// sin acordarse de nada.</para>
    /// </summary>
    public class EscrituraSoloAlmacenAttribute : AuthorizeAttribute
    {
        public const string MENSAJE_SIN_PERMISO =
            "Para guardar datos del almacén hay que tener el permiso de Almacén. Pídeselo a informática.";

        private static readonly string[] gruposQuePuedenEscribir =
        {
            Constantes.GruposSeguridad.ALMACEN,
            Constantes.GruposSeguridad.DIRECCION
        };

        public static bool PuedeEscribir(IPrincipal usuario)
        {
            return usuario != null && gruposQuePuedenEscribir.Any(g => usuario.IsInRoleSinDominio(g));
        }

        protected override bool IsAuthorized(HttpActionContext actionContext)
        {
            if (!base.IsAuthorized(actionContext))
            {
                return false;
            }
            return EsLectura(actionContext.Request.Method) ||
                PuedeEscribir(actionContext.ControllerContext.RequestContext.Principal);
        }

        protected override void HandleUnauthorizedRequest(HttpActionContext actionContext)
        {
            IPrincipal usuario = actionContext.ControllerContext.RequestContext.Principal;
            if (usuario?.Identity?.IsAuthenticated == true)
            {
                // Identificado pero sin permiso: 403, no 401 (con 401 la app intentaría refrescar el token)
                actionContext.Response = actionContext.Request.CreateErrorResponse(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
                return;
            }
            base.HandleUnauthorizedRequest(actionContext);
        }

        private static bool EsLectura(HttpMethod metodo)
        {
            return metodo == HttpMethod.Get || metodo == HttpMethod.Head || metodo == HttpMethod.Options;
        }
    }
}
