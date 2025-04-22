using Microsoft.Owin.Security;
using Microsoft.Owin.Security.DataHandler.Encoder;
using NestoAPI.Models;
using System;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Web;

namespace NestoAPI.Providers
{
    public class CustomJwtFormat : ISecureDataFormat<AuthenticationTicket>
    {

        private readonly string _issuer = string.Empty;

        public CustomJwtFormat(string issuer)
        {
            _issuer = issuer;
        }

        public string Protect(AuthenticationTicket data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            _ = HttpContext.Current;

            //// Verificamos si viene de NestoTiendas mediante un header específico
            //if (context != null &&
            //    context.Request.Headers["X-App-Type"] == "NestoTiendas" &&
            //    data.Identity is ClaimsIdentity identity)
            //{
            //    // Obtenemos el ID del usuario/cliente
            //    Claim userIdClaim = identity.FindFirst(ClaimTypes.NameIdentifier);
            //    if (userIdClaim != null)
            //    {
            //        string userId = userIdClaim.Value;

            //        // Verificamos si tiene compras recientes
            //        bool hasRecentPurchases = ClienteHelper.ClienteConComprasRecientes(userId);

            //        // Añadimos el claim solo si viene de NestoTiendas
            //        Claim existingClaim = identity.FindFirst("HasRecentPurchases");
            //        if (existingClaim != null)
            //        {
            //            identity.RemoveClaim(existingClaim);
            //        }
            //        identity.AddClaim(new Claim("HasRecentPurchases", hasRecentPurchases.ToString()));
            //    }
            //}

            string audienceId = ConfigurationManager.AppSettings["as:AudienceId"];

            string symmetricKeyAsBase64 = ConfigurationManager.AppSettings["as:AudienceSecret"];

            byte[] keyByteArray = TextEncodings.Base64Url.Decode(symmetricKeyAsBase64);
            Microsoft.IdentityModel.Tokens.SymmetricSecurityKey securityKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyByteArray);
            Microsoft.IdentityModel.Tokens.SigningCredentials signingKey = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                        securityKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature);

            DateTimeOffset? issued = data.Properties.IssuedUtc;

            DateTimeOffset? expires = data.Properties.ExpiresUtc;

            JwtSecurityToken token = new JwtSecurityToken(_issuer, audienceId, data.Identity.Claims, issued.Value.UtcDateTime, expires.Value.UtcDateTime, signingKey);

            JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();

            string jwt = handler.WriteToken(token);

            return jwt;
        }

        public AuthenticationTicket Unprotect(string protectedText)
        {
            throw new NotImplementedException();
        }

        private bool CheckClientRecentPurchases(string clienteId)
        {
            // Implementa la lógica para verificar si el cliente tiene compras en los últimos 365 días
            // Esto dependerá de tu modelo de datos y base de datos
            try
            {
                using (NVEntities db = new NVEntities()) // Asume que usas Entity Framework, ajusta según tu caso
                {
                    // Verifica si hay al menos una compra en los últimos 365 días
                    DateTime fechaLimite = DateTime.Now.AddDays(-365);

                    // Esta consulta debe ajustarse según tu modelo de datos
                    bool tieneCompras = db.ExtractosCliente
                        .Any(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                        p.TipoApunte == Constantes.ExtractosCliente.TiposApunte.FACTURA && p.Número == clienteId && p.Importe >= 0 && p.Fecha >= fechaLimite);

                    return tieneCompras;
                }
            }
            catch
            {
                // En caso de error, por defecto no mostramos los videos nuevos
                return false;
            }
        }

    }
}
