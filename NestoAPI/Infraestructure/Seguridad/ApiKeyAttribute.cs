using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// NestoAPI#429 (punto 1): exige una API key en una cabecera, fallando en CERRADO.
    ///
    /// Se declara qué setting y qué cabecera, para que los distintos consumidores puedan tener su
    /// propia clave y se pueda rotar una sin romper las demás:
    ///
    /// <code>
    ///     [ApiKey("ApiKeyPrestashop", "X-API-KEY")]
    ///     public async Task&lt;IHttpActionResult&gt; PrestashopLogin(...)
    /// </code>
    ///
    /// Se responde 401 sin cuerpo a propósito: quien no trae la clave no tiene por qué saber si
    /// falló por ausente, por incorrecta o porque el servidor está mal configurado.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class ApiKeyAttribute : AuthorizationFilterAttribute
    {
        private readonly string _nombreSetting;
        private readonly string _cabecera;

        public ApiKeyAttribute(string nombreSetting, string cabecera)
        {
            _nombreSetting = nombreSetting;
            _cabecera = cabecera;
        }

        /// <summary>Para poder comprobar por reflexión que un endpoint sigue protegido.</summary>
        public string NombreSetting => _nombreSetting;
        public string Cabecera => _cabecera;

        public override void OnAuthorization(HttpActionContext actionContext)
        {
            string esperada = ConfigurationManager.AppSettings[_nombreSetting];
            string recibida = LeerCabecera(actionContext);

            if (!ValidadorApiKey.EsValida(esperada, recibida))
            {
                actionContext.Response = actionContext.Request.CreateResponse(HttpStatusCode.Unauthorized);
                return;
            }

            base.OnAuthorization(actionContext);
        }

        private string LeerCabecera(HttpActionContext actionContext)
        {
            return actionContext.Request.Headers.TryGetValues(_cabecera, out IEnumerable<string> valores)
                ? valores?.FirstOrDefault()
                : null;
        }
    }
}
