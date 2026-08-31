using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin.Security.DataHandler.Encoder;
using System;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// NestoAPI#427 (punto 1): comprueba que un JWT lo hemos emitido NOSOTROS antes de creerse
    /// nada de lo que dice.
    ///
    /// Los dos endpoints de refresco leían el token con <c>ReadJwtToken</c>, que SOLO PARSEA: no
    /// mira la firma, ni el issuer, ni la audiencia. A partir de ahí se tomaban los claims como
    /// ciertos y se emitía un token nuevo, auténtico y firmado. Como los endpoints son anónimos,
    /// cualquiera podía fabricarse un JWT con el <c>cliente</c> que quisiera —sin saber la clave,
    /// porque nadie la comprobaba— y canjearlo por uno de verdad.
    ///
    /// Se valida con la MISMA clave (<c>as:AudienceSecret</c>), el mismo issuer
    /// (<c>JwtIssuer</c>) y la misma audiencia (<c>as:AudienceId</c>) que usa
    /// <see cref="Providers.CustomJwtFormat"/> al firmarlos, decodificando la clave con el mismo
    /// Base64Url: si se decodificara de otra forma, los bytes no coincidirían y no validaría ni
    /// un token legítimo.
    /// </summary>
    internal static class ValidadorJwt
    {
        /// <summary>
        /// Devuelve el token si la firma, el issuer y la audiencia son correctos; null si no.
        ///
        /// ⚠️⚠️ <b>ValidateLifetime va a FALSE, y NO es un descuido.</b> ⚠️⚠️
        ///
        /// La app TiendasNuevaVision manda a los endpoints de refresco tokens YA CADUCADOS a
        /// propósito: ese es exactamente su mecanismo de renovación — <c>AuthHeaderHandler</c>
        /// detecta el <c>exp</c> pasado y llama al refresco con el token muerto en la mano.
        ///
        /// Si alguien "arregla" esto poniéndolo a true (o quitando la línea, que es peor porque el
        /// valor por defecto es true), TODOS los refrescos fallarán y TODOS los clientes con más
        /// de una hora de sesión se quedarán fuera de la app, sin forma de volver salvo pidiendo
        /// un código nuevo por correo. Y no se enterarían por un error: la app no limpia su estado
        /// cuando el refresco falla (TiendasNuevaVision#50), así que verían "sesión iniciada"
        /// mientras nada funciona.
        ///
        /// La ventana temporal se sigue comprobando A MANO en cada endpoint, como hasta ahora
        /// (un mes en <c>RefreshToken</c>, dos años en <c>RefreshOAuthToken</c>). Que sean
        /// ventanas enormes es un problema distinto (#427 puntos 2 y 3) y se aborda en la segunda
        /// tanda, cuando la app sepa manejar el fin de sesión.
        ///
        /// Hay un test que fija esto: <c>TokenAutenticoPeroCaducado_SeRenueva_ProtegeALaAppMovil</c>.
        /// Si se pone en rojo, la respuesta NO es cambiarlo: es leer esto.
        ///
        /// NO confundir con el falso amigo de <c>StartupJwtConfigurationTests</c>: allí lo que no
        /// se puede hacer es pasarle <c>TokenValidationParameters</c> a
        /// <c>JwtBearerAuthenticationOptions</c> en Startup (OWIN entonces ignora AllowedAudiences
        /// e IssuerSecurityKeyProviders y rechaza todo). Esto de aquí es una validación explícita
        /// dentro del controlador, y es correcta y necesaria.
        /// </summary>
        internal static JwtSecurityToken ValidarFirmaSinCaducidad(string jwt)
        {
            if (string.IsNullOrWhiteSpace(jwt))
            {
                return null;
            }

            TokenValidationParameters parametros = ConstruirParametros();
            if (parametros == null)
            {
                // Falta la configuración: se rechaza a todo el mundo. Fallar en cerrado es lo
                // único aceptable aquí — un refresco que deja de funcionar se ve enseguida; uno
                // que acepta tokens sin validar no se ve nunca.
                return null;
            }

            try
            {
                JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
                _ = handler.ValidateToken(jwt, parametros, out SecurityToken validado);
                return validado as JwtSecurityToken;
            }
            catch (Exception)
            {
                // Firma mala, issuer o audiencia que no son, token ilegible... todo es lo mismo
                // para quien llama: no vale. El detalle no se propaga a propósito.
                return null;
            }
        }

        /// <summary>
        /// Los parámetros de validación, o null si falta algún secreto. Se lee de configuración en
        /// cada llamada (no se cachea) para que rotar la clave no obligue a reciclar el AppPool.
        /// </summary>
        private static TokenValidationParameters ConstruirParametros()
        {
            string secreto = ConfigurationManager.AppSettings["as:AudienceSecret"];
            string issuer = ConfigurationManager.AppSettings["JwtIssuer"];
            string audiencia = ConfigurationManager.AppSettings["as:AudienceId"];

            if (string.IsNullOrWhiteSpace(secreto) ||
                string.IsNullOrWhiteSpace(issuer) ||
                string.IsNullOrWhiteSpace(audiencia))
            {
                return null;
            }

            byte[] clave;
            try
            {
                clave = TextEncodings.Base64Url.Decode(secreto);
            }
            catch (Exception)
            {
                return null;
            }

            return new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(clave),

                ValidateIssuer = true,
                ValidIssuer = issuer,

                ValidateAudience = true,
                ValidAudience = audiencia,

                // ⚠️ LEER EL COMENTARIO DEL MÉTODO ANTES DE TOCAR ESTA LÍNEA. A true deja fuera
                // de la app a todos los clientes con más de una hora de sesión.
                ValidateLifetime = false
            };
        }
    }
}
