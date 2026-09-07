using System;
using System.Collections.Generic;
using NestoAPI.Infraestructure;
using System.Collections.Concurrent;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// NestoAPI#459: exige credencial, pero vale CUALQUIERA de las dos que usan nuestros clientes:
    /// un JWT (Nesto, NestoApp y TiendasNuevaVision) o una API key de servidor a servidor (el
    /// módulo nestopago de PrestaShop, que llama desde el back de la tienda y no tiene JWT).
    ///
    /// <para>Poner <c>[Authorize]</c> y <c>[ApiKey]</c> juntos NO sirve: los filtros de
    /// autorización se acumulan, así que exigirían las dos credenciales a la vez y ninguno de los
    /// dos consumidores pasaría.</para>
    ///
    /// <para>Además cierra el agujero que motivó la issue: estos endpoints devuelven la
    /// <c>InfoDeuda</c> del cliente que se pase en la URL (deuda vencida, impagados, motivo de
    /// restricción). Con credencial ya no los ve cualquiera, pero un cliente final autenticado
    /// seguiría pudiendo pedir el código de OTRO cliente, así que un JWT con claim
    /// <c>cliente</c> solo puede consultar el suyo. Empleados y vendedores no llevan ese claim y
    /// siguen consultando cualquier cliente, que es lo que hacen a diario desde Nesto y NestoApp:
    /// ojo, NO vale <see cref="ValidadorAccesoCliente"/> aquí, que deniega a los vendedores.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class AutorizadoOApiKeyAttribute : AuthorizationFilterAttribute
    {
        private readonly string _nombreSetting;
        private readonly string _cabecera;

        public AutorizadoOApiKeyAttribute(string nombreSetting, string cabecera)
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
            bool conApiKey = ValidadorApiKey.EsValida(esperada, recibida);
            ClaimsIdentity identity = actionContext.RequestContext?.Principal?.Identity as ClaimsIdentity;

            if (!conApiKey && identity?.IsAuthenticated != true)
            {
                if (!Exigir)
                {
                    // MODO SOMBRA (ver Exigir): se deja pasar y se apunta quién sigue llamando sin
                    // credencial, para poder encenderlo con datos en vez de a ciegas.
                    AvisarLlamadaSinCredencialUnaVez(actionContext);
                    base.OnAuthorization(actionContext);
                    return;
                }

                // Sin cuerpo, como el resto de la casa: quien no trae credencial no tiene por qué
                // saber si falló por ausente, por incorrecta o por mala configuración.
                actionContext.Response = actionContext.Request.CreateResponse(HttpStatusCode.Unauthorized);
                return;
            }

            // La API key es de servidor a servidor: no hay cliente al que atarla.
            if (!conApiKey && !PuedeConsultarCliente(identity, LeerClienteDeLaConsulta(actionContext)))
            {
                actionContext.Response = actionContext.Request.CreateResponse(HttpStatusCode.Forbidden);
                return;
            }

            base.OnAuthorization(actionContext);
        }

        /// <summary>
        /// NestoAPI#459: si vale <c>false</c> (el valor de arranque), una petición SIN credencial
        /// NO se rechaza: se deja pasar y se apunta en ELMAH.
        ///
        /// <para>Existe por el despliegue, no por gusto. El <c>SelectorPlazosPago</c> de Nesto
        /// llamaba a estos endpoints con un HttpClient sin JWT; ya está arreglado, pero Nesto se
        /// distribuye por ClickOnce y cada puesto actualiza cuando reinicia la aplicación, no
        /// cuando publicamos. Exigir credencial el mismo día dejaría sin lista de plazos de pago
        /// —y en silencio— a todo el que no hubiera reiniciado, que es media plantilla de venta.</para>
        ///
        /// <para>Se pone a <c>true</c> en el Web.config cuando el aviso de abajo deje de aparecer
        /// en ELMAH unos días seguidos: entonces sabremos que ya no queda ningún cliente viejo.</para>
        /// </summary>
        internal static bool Exigir =>
            string.Equals(ConfigurationManager.AppSettings[CLAVE_EXIGIR]?.Trim(), "true",
                StringComparison.OrdinalIgnoreCase);

        public const string CLAVE_EXIGIR = "Seguridad:ExigirCredencialPlazosYFormasPago";

        // Una vez por ruta y por arranque del proceso: el selector de plazos se llama en cada
        // cambio de cliente, y un aviso por llamada convertiría ELMAH en un log de tráfico.
        private static readonly ConcurrentDictionary<string, byte> AvisadasSinCredencial =
            new ConcurrentDictionary<string, byte>();

        private static void AvisarLlamadaSinCredencialUnaVez(HttpActionContext actionContext)
        {
            string ruta = actionContext?.Request?.RequestUri?.AbsolutePath ?? "(sin ruta)";
            if (!AvisadasSinCredencial.TryAdd(ruta, 0))
            {
                return;
            }

            ElmahHelper.Log(new Exception(
                $"[Credenciales #459] Llamada SIN credencial a {ruta}. Se ha dejado pasar porque " +
                $"{CLAVE_EXIGIR} está apagado. Cuando este aviso deje de salir unos días, ponerlo a true."),
                "Sistema (control de credenciales)");
        }

        /// <summary>
        /// ¿Puede esta identidad preguntar por ese cliente? Solo se restringe a los JWT de cliente
        /// final (los que llevan el claim <c>cliente</c>, que emite <c>api/auth/token</c> para
        /// TiendasNuevaVision): esos, únicamente el suyo. Los de empleado y vendedor no llevan el
        /// claim y pasan, como hasta ahora.
        /// </summary>
        internal static bool PuedeConsultarCliente(ClaimsIdentity identity, string clienteSolicitado)
        {
            string clienteDelToken = identity?.FindFirst("cliente")?.Value;
            if (string.IsNullOrWhiteSpace(clienteDelToken))
            {
                return true;
            }

            // Sin cliente en la consulta no se está pidiendo nada de nadie (la lista general de
            // plazos o formas de pago de la empresa).
            if (string.IsNullOrWhiteSpace(clienteSolicitado))
            {
                return true;
            }

            // El código de cliente es char en la base de datos y viaja con relleno según quién lo
            // mande, así que se compara como lo haría SQL Server.
            return string.Equals(clienteDelToken.Trim(), clienteSolicitado.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// El parámetro <c>cliente</c> de la query. Se lee a mano porque los filtros de
        /// autorización corren ANTES del model binding: <c>ActionArguments</c> todavía está vacío.
        /// </summary>
        internal static string LeerClienteDeLaConsulta(HttpActionContext actionContext)
        {
            return actionContext?.Request?.GetQueryNameValuePairs()
                ?.FirstOrDefault(p => string.Equals(p.Key, "cliente", StringComparison.OrdinalIgnoreCase))
                .Value;
        }

        private string LeerCabecera(HttpActionContext actionContext)
        {
            return actionContext.Request.Headers.TryGetValues(_cabecera, out IEnumerable<string> valores)
                ? valores?.FirstOrDefault()
                : null;
        }
    }
}
