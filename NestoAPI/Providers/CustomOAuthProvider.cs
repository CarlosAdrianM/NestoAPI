using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OAuth;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Seguridad;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Providers
{
    public class CustomOAuthProvider : OAuthAuthorizationServerProvider
    {

        public override Task ValidateClientAuthentication(OAuthValidateClientAuthenticationContext context)
        {
            _ = context.Validated();
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

            if (!user.EmailConfirmed)
            {
                context.SetError("invalid_grant", "User did not confirm email.");
                return;
            }

            ClaimsIdentity oAuthIdentity = await ConstruirIdentity(user, userManager);

            var ticket = new AuthenticationTicket(oAuthIdentity, null);

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

            // Importante: limpiar IssuedUtc/ExpiresUtc del ticket deserializado (tienen la
            // fecha del refresh_token, 90 días). Al pasar null, OWIN las recalcula usando
            // AccessTokenExpireTimeSpan (30 días), que es lo que queremos para el nuevo
            // access_token.
            context.Ticket.Properties.IssuedUtc = null;
            context.Ticket.Properties.ExpiresUtc = null;

            var nuevoTicket = new AuthenticationTicket(nuevaIdentity, context.Ticket.Properties);

            _ = context.Validated(nuevoTicket);
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
