using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OAuth;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Seguridad;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Providers
{
    public class CustomOAuthProvider : OAuthAuthorizationServerProvider
    {
        // NestoAPI#624: texto de error_description cuando usuario y contraseña son correctos pero el
        // correo no está confirmado. Se mantiene el código invalid_grant para no romper a los clientes.
        internal const string MENSAJE_USUARIO_NO_ACTIVADO = "Tu usuario aún no está activado: avisa a informática.";

        public override Task ValidateClientAuthentication(OAuthValidateClientAuthenticationContext context)
        {
            // NestoAPI#575: solo Ariadna deja su client_id (para saber desde qué app se entra). Todo lo
            // demás —NestoApp entra sin client_id y renueva con client_id=NestoApp— sigue como siempre.
            // Ojo: NO usar TryGetFormCredentials, que deja el ClientId en el contexto también para NestoApp.
            string aplicacion = AplicacionClienteOAuth.Reconocida(context.Parameters?.Get("client_id"));
            if (aplicacion != null)
            {
                _ = context.Validated(aplicacion);
            }
            else
            {
                _ = context.Validated();
            }
            return Task.FromResult<object>(null);
        }

        public override async Task GrantResourceOwnerCredentials(OAuthGrantResourceOwnerCredentialsContext context)
        {

            //var allowedOrigin = "*";
            //context.OwinContext.Response.Headers.Add("Access-Control-Allow-Origin", new[] { allowedOrigin });

            var userManager = context.OwinContext.GetUserManager<ApplicationUserManager>();

            ApplicationUser user = await userManager.FindAsync(context.UserName, context.Password);

            if (user == null)
            {
                context.SetError("invalid_grant", "The user name or password is incorrect.");
                return;
            }

            // NestoAPI#624: aquí las credenciales ya son correctas (FindAsync devuelve null si la
            // contraseña no coincide), así que decir «no activado» no revela si el usuario existe.
            if (!user.EmailConfirmed)
            {
                context.SetError("invalid_grant", MENSAJE_USUARIO_NO_ACTIVADO);
                RegistrarUsuarioNoActivadoEnElmah(user.UserName, context.ClientId);
                return;
            }

            ClaimsIdentity oAuthIdentity = await ConstruirIdentity(user, userManager);

            // NestoAPI#575: con Ariadna, claim «app» y propiedad para que el refresh_token lo recuerde.
            // Sin aplicación reconocida (NestoApp), el ticket se crea exactamente igual que antes.
            string aplicacion = AplicacionClienteOAuth.Reconocida(context.ClientId);
            AuthenticationProperties propiedades = null;
            if (aplicacion != null)
            {
                propiedades = new AuthenticationProperties();
                AplicacionClienteOAuth.Marcar(oAuthIdentity, propiedades, aplicacion);
            }

            var ticket = new AuthenticationTicket(oAuthIdentity, propiedades);

            _ = context.Validated(ticket);

        }

        // NestoAPI#188: flow de grant_type=refresh_token. Se ejecuta después de que
        // SimpleRefreshTokenProvider.ReceiveAsync haya validado el refresh_token entrante
        // y deserializado el ticket original. Aquí re-validamos que el usuario sigue
        // activo y regeneramos las claims (por si cambió algo en BD: rol, vendedor, etc.).
        public override async Task GrantRefreshToken(OAuthGrantRefreshTokenContext context)
        {
            string userName = context.Ticket.Identity.Name;
            if (string.IsNullOrEmpty(userName))
            {
                context.SetError("invalid_grant", "Refresh token identity missing user name.");
                return;
            }

            ApplicationUserManager userManager = context.OwinContext.GetUserManager<ApplicationUserManager>();
            ApplicationUser user = await userManager.FindByNameAsync(userName);

            if (user == null || !user.EmailConfirmed)
            {
                context.SetError("invalid_grant", "User is no longer available.");
                return;
            }

            ClaimsIdentity nuevaIdentity = await ConstruirIdentity(user, userManager);
            // NestoAPI#575: la aplicación se saca del ticket con el que se entró (no de la petición), así
            // que Ariadna sigue siendo Ariadna al renovar; los refresh_token de NestoApp no la llevan.
            AplicacionClienteOAuth.AñadirClaim(nuevaIdentity, AplicacionClienteOAuth.DelTicket(context.Ticket.Properties));

            // Importante: limpiar IssuedUtc/ExpiresUtc del ticket deserializado (tienen la
            // fecha del refresh_token, 90 días). Al pasar null, OWIN las recalcula usando
            // AccessTokenExpireTimeSpan (30 días), que es lo que queremos para el nuevo
            // access_token.
            context.Ticket.Properties.IssuedUtc = null;
            context.Ticket.Properties.ExpiresUtc = null;

            var nuevoTicket = new AuthenticationTicket(nuevaIdentity, context.Ticket.Properties);

            _ = context.Validated(nuevoTicket);
        }

        /// <summary>
        /// NestoAPI#624: rastro informativo en ELMAH para que informática vea quién intenta entrar con
        /// un usuario sin activar. Nunca rompe la respuesta del token.
        /// </summary>
        private static void RegistrarUsuarioNoActivadoEnElmah(string usuario, string aplicacion)
        {
            try
            {
                Elmah.ErrorLog.GetDefault(HttpContext.Current)?.Log(new Elmah.Error(new Exception(
                    $"[Informativo NestoAPI#624] Login rechazado en /oauth/token: el usuario «{usuario}» tiene la contraseña correcta pero el correo sin confirmar (EmailConfirmed = 0). Aplicación: {aplicacion ?? "NestoApp"}")));
            }
            catch
            {
                // Sin ELMAH (tests) o fallo al registrar: el cliente recibe igualmente el error.
            }
        }

        private static async Task<ClaimsIdentity> ConstruirIdentity(ApplicationUser user, ApplicationUserManager userManager)
        {
            ClaimsIdentity oAuthIdentity = await user.GenerateUserIdentityAsync(userManager, "JWT");

            // Añadir claims de vendedor si el usuario tiene uno asociado
            // Issue #70: Necesario para validación de acceso a recursos por vendedor
            try
            {
                ClaimsVendedorHelper.AñadirClaimsVendedor(oAuthIdentity, user.UserName);
            }
            catch (Exception)
            {
                // Si falla la búsqueda del vendedor, continuamos sin el claim
                // El usuario podrá autenticarse pero no tendrá acceso a recursos restringidos por vendedor
            }

            return oAuthIdentity;
        }
    }
}
