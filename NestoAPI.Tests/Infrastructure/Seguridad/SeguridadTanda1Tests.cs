using FakeItEasy;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin.Security.DataHandler.Encoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Seguridad;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Seguridad
{
    /// <summary>
    /// NestoAPI#430 (primera tanda de #427, #428 y #429): las correcciones de seguridad que no
    /// afectan a ningún cliente.
    ///
    /// El bloque que más importa de todo este fichero es
    /// <see cref="TokenAutenticoPeroCaducado_SeRenueva_ProtegeALaAppMovil"/>. Léelo antes de tocar
    /// nada de la validación de JWT.
    /// </summary>
    [TestClass]
    public class ValidadorJwtTests
    {
        // Los mismos nombres de setting que usa CustomJwtFormat al firmar.
        private const string SECRETO = "Y2xhdmVfZGVfcHJ1ZWJhX3BhcmFfbG9zX3Rlc3RzXzEyMzQ1Njc4OTA";
        private const string ISSUER = "carlos";
        private const string AUDIENCIA = "12345678nuevavision0123456789012";

        [TestInitialize]
        public void Inicializar()
        {
            ConfigurationManager.AppSettings["as:AudienceSecret"] = SECRETO;
            ConfigurationManager.AppSettings["JwtIssuer"] = ISSUER;
            ConfigurationManager.AppSettings["as:AudienceId"] = AUDIENCIA;
        }

        /// <summary>
        /// Firma un token igual que <c>CustomJwtFormat.Protect</c>: misma clave decodificada con
        /// Base64Url, mismo algoritmo, mismo issuer y misma audiencia.
        /// </summary>
        private static string FirmarToken(DateTime emitido, DateTime caduca,
            string secreto = SECRETO, string issuer = ISSUER, string audiencia = AUDIENCIA)
        {
            byte[] clave = TextEncodings.Base64Url.Decode(secreto);
            var credenciales = new SigningCredentials(
                new SymmetricSecurityKey(clave), SecurityAlgorithms.HmacSha256Signature);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Email, "cliente@ejemplo.com"),
                new Claim("nif", "12345678Z"),
                new Claim("cliente", "15191")
            };

            var token = new JwtSecurityToken(issuer, audiencia, claims, emitido, caduca, credenciales);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string TokenVivo() =>
            FirmarToken(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddHours(1));

        private static string TokenCaducadoAyer() =>
            FirmarToken(DateTime.UtcNow.AddDays(-1).AddHours(-1), DateTime.UtcNow.AddDays(-1));

        [TestMethod]
        public void TokenValidoYVivo_SeAcepta()
        {
            Assert.IsNotNull(ValidadorJwt.ValidarFirmaSinCaducidad(TokenVivo()));
        }

        /// <summary>
        /// ⚠️⚠️ EL TEST QUE PROTEGE A LA APP MÓVIL. NO lo "arregles" si se pone en rojo. ⚠️⚠️
        ///
        /// La app TiendasNuevaVision manda a los endpoints de refresco tokens YA CADUCADOS a
        /// propósito: ese es su mecanismo de renovación — AuthHeaderHandler ve el exp pasado y
        /// llama al refresco con el token muerto en la mano.
        ///
        /// Por eso ValidadorJwt pone ValidateLifetime = false. Si alguien lo pone a true, o quita
        /// la línea (que es peor, porque por defecto es true), este test se pone rojo — y lo que
        /// estaría avisando es que TODOS los clientes con más de una hora de sesión se quedarían
        /// fuera de la app, sin poder volver salvo pidiendo un código nuevo por correo. Ni siquiera
        /// verían un error: la app no limpia su estado cuando el refresco falla
        /// (TiendasNuevaVision#50), así que verían "sesión iniciada" mientras nada funciona.
        ///
        /// La ventana temporal SÍ se comprueba, pero a mano y en cada endpoint (un mes en
        /// RefreshToken, dos años en RefreshOAuthToken). Eso es otra cosa y va en la segunda tanda.
        /// </summary>
        [TestMethod]
        public void TokenAutenticoPeroCaducado_SeRenueva_ProtegeALaAppMovil()
        {
            JwtSecurityToken validado = ValidadorJwt.ValidarFirmaSinCaducidad(TokenCaducadoAyer());

            Assert.IsNotNull(validado,
                "Un token AUTENTICO pero caducado tiene que validar: es como la app pide la " +
                "renovacion. Si esto falla, ValidateLifetime se ha puesto a true y se quedan " +
                "fuera todos los clientes con mas de una hora de sesion.");
            Assert.AreEqual("15191", validado.Claims.First(c => c.Type == "cliente").Value);
        }

        // El agujero de #427: los endpoints de refresco usaban ReadJwtToken, que solo parsea.
        // Cualquiera podia fabricarse un JWT con el cliente que quisiera y canjearlo por uno
        // autentico, sin saber la clave.
        [TestMethod]
        public void TokenFirmadoConOtraClave_SeRechaza()
        {
            string falsificado = FirmarToken(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddHours(1),
                secreto: "b3RyYV9jbGF2ZV9kaXN0aW50YV9wYXJhX2VsX2F0YWNhbnRlXzEyMzQ1");

            Assert.IsNull(ValidadorJwt.ValidarFirmaSinCaducidad(falsificado));
        }

        [TestMethod]
        public void TokenConLaFirmaManipulada_SeRechaza()
        {
            string[] partes = TokenVivo().Split('.');
            // Se le cambia un caracter a la firma, dejando cabecera y payload intactos.
            string firma = partes[2];
            partes[2] = (firma[0] == 'A' ? "B" : "A") + firma.Substring(1);

            Assert.IsNull(ValidadorJwt.ValidarFirmaSinCaducidad(string.Join(".", partes)));
        }

        [TestMethod]
        public void TokenDeOtroIssuer_SeRechaza()
        {
            string ajeno = FirmarToken(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddHours(1),
                issuer: "otro-emisor");

            Assert.IsNull(ValidadorJwt.ValidarFirmaSinCaducidad(ajeno));
        }

        [TestMethod]
        public void TokenParaOtraAudiencia_SeRechaza()
        {
            string ajeno = FirmarToken(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddHours(1),
                audiencia: "otra-aplicacion");

            Assert.IsNull(ValidadorJwt.ValidarFirmaSinCaducidad(ajeno));
        }

        [TestMethod]
        public void BasuraQueNiSiquieraEsUnToken_SeRechaza()
        {
            Assert.IsNull(ValidadorJwt.ValidarFirmaSinCaducidad("esto no es un jwt"));
            Assert.IsNull(ValidadorJwt.ValidarFirmaSinCaducidad(string.Empty));
            Assert.IsNull(ValidadorJwt.ValidarFirmaSinCaducidad(null));
        }

        // Fallar en cerrado: sin secreto configurado no se valida nada, asi que no se acepta nada.
        [TestMethod]
        public void SinSecretoConfigurado_SeRechazaTodo()
        {
            string tokenBueno = TokenVivo();
            ConfigurationManager.AppSettings["as:AudienceSecret"] = string.Empty;

            Assert.IsNull(ValidadorJwt.ValidarFirmaSinCaducidad(tokenBueno));
        }
    }

    /// <summary>
    /// NestoAPI#428 (punto 5) y #429 (punto 1): comparar secretos sin que el tiempo delate en qué
    /// se parecen.
    /// </summary>
    [TestClass]
    public class ComparacionSeguraTests
    {
        [TestMethod]
        public void CadenasIguales_SonIguales()
        {
            Assert.IsTrue(ComparacionSegura.SonIguales("123456", "123456"));
        }

        [TestMethod]
        public void CadenasDistintas_NoSonIguales()
        {
            Assert.IsFalse(ComparacionSegura.SonIguales("123456", "123457"));
            Assert.IsFalse(ComparacionSegura.SonIguales("123456", "12345"));
            Assert.IsFalse(ComparacionSegura.SonIguales("123456", "1234567"));
        }

        [TestMethod]
        public void DistingueMayusculas()
        {
            Assert.IsFalse(ComparacionSegura.SonIguales("Clave", "clave"));
        }

        /// <summary>
        /// Un nulo NUNCA es igual a nada, ni a otro nulo. Es exactamente la trampa del #429: la
        /// setting sin definir daba null, la peticion sin cabecera daba null, y la comparacion se
        /// superaba dejando el endpoint abierto.
        /// </summary>
        [TestMethod]
        public void NuloNuncaEsIgualANada_NiSiquieraAOtroNulo()
        {
            Assert.IsFalse(ComparacionSegura.SonIguales(null, null));
            Assert.IsFalse(ComparacionSegura.SonIguales(null, "algo"));
            Assert.IsFalse(ComparacionSegura.SonIguales("algo", null));
        }
    }

    /// <summary>
    /// NestoAPI#429 (punto 1): la validación de API key tiene que fallar en CERRADO.
    /// </summary>
    [TestClass]
    public class ValidadorApiKeyTests
    {
        [TestMethod]
        public void ClaveCorrecta_Pasa()
        {
            Assert.IsTrue(ValidadorApiKey.EsValida("la-clave", "la-clave"));
        }

        [TestMethod]
        public void ClaveIncorrecta_NoPasa()
        {
            Assert.IsFalse(ValidadorApiKey.EsValida("la-clave", "otra-clave"));
            Assert.IsFalse(ValidadorApiKey.EsValida("la-clave", "LA-CLAVE"));
        }

        /// <summary>
        /// EL CASO DE #429. Sin la setting configurada, el codigo viejo (`apiKey != _apiKey`)
        /// dejaba pasar a quien no mandara cabecera: null != null es falso. Y prestashop-login
        /// emite JWT de cliente SALTANDOSE el codigo por correo.
        /// </summary>
        [TestMethod]
        public void SinClaveConfigurada_NoPasaNadie_NiSiquieraSinCabecera()
        {
            Assert.IsFalse(ValidadorApiKey.EsValida(null, null), "El caso exacto del #429");
            Assert.IsFalse(ValidadorApiKey.EsValida(null, "lo-que-sea"));
            Assert.IsFalse(ValidadorApiKey.EsValida(string.Empty, string.Empty));
            Assert.IsFalse(ValidadorApiKey.EsValida("   ", "   "));
        }

        [TestMethod]
        public void ConClaveConfiguradaPeroSinCabecera_NoPasa()
        {
            Assert.IsFalse(ValidadorApiKey.EsValida("la-clave", null));
        }
    }

    /// <summary>
    /// NestoAPI#430: los endpoints de refresco, a nivel de controller. Solo se prueban los caminos
    /// de RECHAZO: el de aceptacion acaba en CrearJWTAsync, que consulta la base de datos, y la
    /// propiedad que de verdad importa —que un token caducado siga valiendo— esta cubierta en
    /// ValidadorJwtTests, que es donde vive ValidateLifetime.
    /// </summary>
    [TestClass]
    public class AuthControllerRefrescoTests
    {
        private const string SECRETO = "Y2xhdmVfZGVfcHJ1ZWJhX3BhcmFfbG9zX3Rlc3RzXzEyMzQ1Njc4OTA";

        [TestInitialize]
        public void Inicializar()
        {
            ConfigurationManager.AppSettings["as:AudienceSecret"] = SECRETO;
            ConfigurationManager.AppSettings["JwtIssuer"] = "carlos";
            ConfigurationManager.AppSettings["as:AudienceId"] = "12345678nuevavision0123456789012";
        }

        private static AuthController CrearController(string tokenEnCabecera)
        {
            var controller = new AuthController(A.Fake<IGestorClientes>(), A.Fake<IServicioCorreoElectronico>());
            var request = new HttpRequestMessage();
            if (tokenEnCabecera != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenEnCabecera);
            }
            controller.Request = request;
            return controller;
        }

        private static string TokenFalsificado()
        {
            // Firmado con una clave que no es la nuestra: un atacante puede escribir los claims
            // que quiera, pero no puede firmar.
            byte[] clave = TextEncodings.Base64Url.Decode("b3RyYV9jbGF2ZV9kaXN0aW50YV9wYXJhX2VsX2F0YWNhbnRlXzEyMzQ1");
            var credenciales = new SigningCredentials(
                new SymmetricSecurityKey(clave), SecurityAlgorithms.HmacSha256Signature);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Email, "atacante@ejemplo.com"),
                new Claim("nif", "00000000T"),
                new Claim("cliente", "15191")
            };
            var token = new JwtSecurityToken("carlos", "12345678nuevavision0123456789012", claims,
                DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddHours(1), credenciales);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [TestMethod]
        public async Task RefreshToken_ConFirmaFalsificada_DevuelveUnauthorized()
        {
            AuthController controller = CrearController(TokenFalsificado());

            var resultado = await controller.RefreshToken();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task RefreshOAuthToken_ConFirmaFalsificada_DevuelveUnauthorized()
        {
            AuthController controller = CrearController(TokenFalsificado());

            var resultado = await controller.RefreshOAuthToken();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task RefreshToken_SinCabecera_DevuelveUnauthorized()
        {
            AuthController controller = CrearController(null);

            Assert.IsInstanceOfType(await controller.RefreshToken(), typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task RefreshToken_ConBasuraEnLaCabecera_DevuelveUnauthorized()
        {
            AuthController controller = CrearController("no.es.un.jwt");

            Assert.IsInstanceOfType(await controller.RefreshToken(), typeof(UnauthorizedResult));
        }
    }

    /// <summary>
    /// NestoAPI#429/#430: que prestashop-login siga exigiendo su API key. Es un endpoint anónimo
    /// que emite JWT de cliente SALTÁNDOSE el código por correo, así que perder el atributo sería
    /// dejarlo abierto a internet — y en silencio, porque un 401 por atributo no llega a ELMAH.
    /// </summary>
    [TestClass]
    public class ApiKeyEnPrestashopLoginTests
    {
        [TestMethod]
        public void PrestashopLogin_ExigeLaApiKeyDePrestashop()
        {
            MethodInfo metodo = typeof(AuthController).GetMethods()
                .FirstOrDefault(m => m.Name == "PrestashopLogin");
            Assert.IsNotNull(metodo);

            ApiKeyAttribute atributo = metodo.GetCustomAttributes<ApiKeyAttribute>(inherit: false).FirstOrDefault();

            Assert.IsNotNull(atributo, "PrestashopLogin tiene que llevar [ApiKey]: sin el, queda abierto");
            Assert.AreEqual("ApiKeyPrestashop", atributo.NombreSetting);
            Assert.AreEqual("X-API-KEY", atributo.Cabecera);
        }
    }
}
