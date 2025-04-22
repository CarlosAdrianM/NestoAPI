//using Microsoft.AspNet.Identity;
//using Microsoft.Owin.Security;
//using NestoAPI.Models;
//using NestoAPI.Providers;
//using System;
//using System.Linq;
//using System.Security.Claims;
//using System.Web.Http;

//namespace NestoAPI.Controllers
//{
//    [Route("api/auth/refreshToken")]
//    public class TokenRefreshController : ApiController
//    {
//        [HttpPost]
//        public IHttpActionResult RefreshToken()
//        {
//            // Verificar el token actual
//            if (!(User.Identity is ClaimsIdentity identity))
//            {
//                return Unauthorized();
//            }

//            string clientId = identity.GetUserId();

//            // Verificar compras recientes
//            bool hasRecentPurchases = CheckClientRecentPurchases(clientId);

//            // Generar un nuevo token con la información actualizada
//            // (Aquí necesitarías acceder a tu servicio de generación de tokens)

//            string newToken = GenerateNewToken(clientId, hasRecentPurchases);

//            return Ok(new { token = newToken });
//        }

//        private bool CheckClientRecentPurchases(string clientId)
//        {
//            using (NVEntities db = new NVEntities())
//            {
//                bool tieneComprasRecientes = db.ExtractosCliente
//                    .Any(e => e.Número == clientId && e.Fecha >= DateTime.Now.AddDays(-365) && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.FACTURA && e.Importe > 0);
//                return tieneComprasRecientes;
//            }
//        }

//        private string GenerateNewToken(string clientId, bool hasRecentPurchases)
//        {
//            // Crear los claims necesarios
//            Claim[] claims = new[]
//            {
//                new Claim(ClaimTypes.NameIdentifier, clientId),
//                new Claim("HasRecentPurchases", hasRecentPurchases.ToString())
//            };

//            // Crear la identidad y el ticket de autenticación
//            ClaimsIdentity identity = new ClaimsIdentity(claims, "Bearer");
//            AuthenticationTicket ticket = new AuthenticationTicket(identity, new AuthenticationProperties
//            {
//                IssuedUtc = DateTime.UtcNow,
//                ExpiresUtc = DateTime.UtcNow.AddDays(1) // Configura la expiración según tus necesidades
//            });

//            // Usar CustomJwtFormat para generar el token
//            CustomJwtFormat jwtFormat = new CustomJwtFormat("carlos");
//            return jwtFormat.Protect(ticket);
//        }
//    }
//}
