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

            var ticket = new AuthenticationTicket(oAuthIdentity, null);

            _ = context.Validated(ticket);

        }
    }
}
