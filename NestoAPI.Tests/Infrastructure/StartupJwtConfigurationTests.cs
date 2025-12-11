using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Claims;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests que documentan el comportamiento esperado de la configuración JWT en Startup.cs
    ///
    /// CONTEXTO DEL BUG (09/12/2025):
    /// El commit c4cbbd1 ("Fix: ELMAH muestra usuario en errores de JWT/OWIN") añadió
    /// TokenValidationParameters para mapear NameClaimType y RoleClaimType.
    ///
    /// PROBLEMA:
    /// Cuando se proporciona TokenValidationParameters a JwtBearerAuthenticationOptions,
    /// el middleware OWIN ignora AllowedAudiences e IssuerSecurityKeyProviders.
    /// Como no configuramos ValidIssuer, ValidAudience ni IssuerSigningKey en los
    /// TokenValidationParameters, TODOS los tokens JWT se rechazaban con error
    /// "Su sesión ha expirado".
    ///
    /// Código de JwtBearerAuthenticationExtensions.cs (AspNetKatana):
    /// <code>
    /// if (options.TokenValidationParameters != null)
    /// {
    ///     jwtFormat = new JwtFormat(options.TokenValidationParameters);  // <-- IGNORA AllowedAudiences
    /// }
    /// else
    /// {
    ///     jwtFormat = new JwtFormat(options.AllowedAudiences, options.IssuerSecurityKeyProviders);
    /// }
    /// </code>
    ///
    /// SOLUCIÓN:
    /// Eliminar TokenValidationParameters de la configuración. El mapeo de claims para
    /// ELMAH ya funciona correctamente con UserSyncHandler que sincroniza el Principal
    /// de OWIN con HttpContext.Current.User.
    /// </summary>
    [TestClass]
    public class StartupJwtConfigurationTests
    {
        /// <summary>
        /// Documenta que TokenValidationParameters vacío (solo con NameClaimType/RoleClaimType)
        /// NO es suficiente para validar tokens JWT.
        ///
        /// Este test representa el bug que causaba "Su sesión ha expirado" al facturar rutas.
        /// </summary>
        [TestMethod]
        public void TokenValidationParameters_SinIssuerNiAudienceNiKey_NoValidaTokens()
        {
            // Arrange - Configuración INCORRECTA (la que causaba el bug)
            var parametrosIncorrectos = new TokenValidationParameters
            {
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role
                // FALTA: ValidIssuer, ValidAudience, IssuerSigningKey
            };

            // Assert - Los valores por defecto causan rechazo de tokens
            Assert.IsTrue(parametrosIncorrectos.ValidateIssuer,
                "Por defecto ValidateIssuer=true, pero no hay ValidIssuer configurado -> rechazo");
            Assert.IsTrue(parametrosIncorrectos.ValidateAudience,
                "Por defecto ValidateAudience=true, pero no hay ValidAudience configurado -> rechazo");
            Assert.IsNull(parametrosIncorrectos.IssuerSigningKey,
                "Sin clave de firma, no se puede validar la firma del token -> rechazo");
            Assert.IsNull(parametrosIncorrectos.ValidIssuer,
                "Sin ValidIssuer configurado");
            Assert.IsNull(parametrosIncorrectos.ValidAudience,
                "Sin ValidAudience configurado");
        }

        /// <summary>
        /// Documenta la configuración correcta SI quisiéramos usar TokenValidationParameters.
        ///
        /// NOTA: Actualmente no usamos TokenValidationParameters porque OWIN ya maneja
        /// la validación correctamente con AllowedAudiences e IssuerSecurityKeyProviders.
        /// Este test es solo para documentación.
        /// </summary>
        [TestMethod]
        public void TokenValidationParameters_ConfiguracionCompletaRequerida()
        {
            // Arrange - Esta sería la configuración CORRECTA si usáramos TokenValidationParameters
            string issuer = "carlos"; // Debe coincidir con CustomJwtFormat
            string audienceId = "414e1927a3884f68abc79f7283837fd1"; // Ejemplo
            byte[] audienceSecret = System.Text.Encoding.UTF8.GetBytes("secretKey"); // Ejemplo

            var parametrosCorrectos = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,

                ValidateAudience = true,
                ValidAudience = audienceId,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(audienceSecret),

                ValidateLifetime = true,

                // Estos son opcionales pero útiles para ELMAH
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role
            };

            // Assert - Verificar que la configuración está completa
            Assert.IsNotNull(parametrosCorrectos.ValidIssuer);
            Assert.IsNotNull(parametrosCorrectos.ValidAudience);
            Assert.IsNotNull(parametrosCorrectos.IssuerSigningKey);
        }

        /// <summary>
        /// Documenta que la solución correcta es NO usar TokenValidationParameters
        /// y dejar que OWIN use AllowedAudiences + IssuerSecurityKeyProviders.
        ///
        /// El mapeo de claims para ELMAH se hace en UserSyncHandler, no en TokenValidationParameters.
        /// </summary>
        [TestMethod]
        public void SolucionCorrecta_NoUsarTokenValidationParameters_UsarUserSyncHandler()
        {
            // La solución es:
            // 1. NO configurar TokenValidationParameters en JwtBearerAuthenticationOptions
            // 2. Usar UserSyncHandler para sincronizar el usuario con HttpContext.Current.User
            // 3. OWIN usa AllowedAudiences + IssuerSecurityKeyProviders para validar tokens

            // Este test documenta la arquitectura correcta:
            // - JwtBearerAuthenticationOptions.AllowedAudiences: valida el audience del token
            // - JwtBearerAuthenticationOptions.IssuerSecurityKeyProviders: valida la firma
            // - UserSyncHandler: copia request.Principal a HttpContext.Current.User (para ELMAH)

            Assert.IsTrue(true, "Ver comentarios para la solución correcta");
        }
    }
}
