using FakeItEasy;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin.Security.DataHandler.Encoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Seguridad
{
    /// <summary>
    /// NestoAPI#427 (puntos 3 y 4): la caducidad absoluta desde el login original.
    /// </summary>
    [TestClass]
    public class PoliticaRefrescoTokenTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

        private static JwtSecurityToken Token(params Claim[] claims) =>
            new JwtSecurityToken("carlos", "audiencia", claims, AHORA.AddHours(-2), AHORA.AddHours(-1));

        private static Claim LoginUtc(DateTime fecha) =>
            new Claim(PoliticaRefrescoToken.CLAIM_LOGIN_UTC, PoliticaRefrescoToken.Escribir(fecha));

        [TestInitialize]
        public void Inicializar()
        {
            ConfigurationManager.AppSettings[PoliticaRefrescoToken.SETTING_DIAS_TIENDA] = null;
            ConfigurationManager.AppSettings[PoliticaRefrescoToken.SETTING_DIAS_OAUTH] = null;
        }

        /// <summary>
        /// ⚠️ La transición que protege a los clientes que hoy tienen TNV abierta: sus tokens se
        /// emitieron antes de #427 y no llevan login_utc. Pasan, y el reloj empieza ahora.
        /// </summary>
        [TestMethod]
        public void TokenSinLoginUtc_PasaYSaleConLoginUtcAhora_TransicionTNV()
        {
            Assert.AreEqual(AHORA, PoliticaRefrescoToken.FechaLoginParaRefresco(Token(), AHORA, 90));
        }

        [TestMethod]
        public void DentroDelPlazo_PropagaLaFechaDelLoginOriginal()
        {
            DateTime login = AHORA.AddDays(-89);

            Assert.AreEqual(login, PoliticaRefrescoToken.FechaLoginParaRefresco(Token(LoginUtc(login)), AHORA, 90));
        }

        [TestMethod]
        public void PasadoElPlazo_SeRechaza()
        {
            Assert.IsNull(PoliticaRefrescoToken.FechaLoginParaRefresco(Token(LoginUtc(AHORA.AddDays(-91))), AHORA, 90));
        }

        [TestMethod]
        public void LoginUtcIlegible_SeRechaza()
        {
            var token = Token(new Claim(PoliticaRefrescoToken.CLAIM_LOGIN_UTC, "ayer por la tarde"));

            Assert.IsNull(PoliticaRefrescoToken.FechaLoginParaRefresco(token, AHORA, 90));
        }

        [TestMethod]
        public void LoginUtcEnElFuturo_SeRechaza_SalvoElMargenDeReloj()
        {
            Assert.IsNull(PoliticaRefrescoToken.FechaLoginParaRefresco(Token(LoginUtc(AHORA.AddHours(1))), AHORA, 90));
            Assert.IsNotNull(PoliticaRefrescoToken.FechaLoginParaRefresco(Token(LoginUtc(AHORA.AddMinutes(2))), AHORA, 90));
        }

        [TestMethod]
        public void EscribirYLeer_IdaYVuelta_EnSegundosUnix()
        {
            string valor = PoliticaRefrescoToken.Escribir(AHORA);

            Assert.AreEqual(new DateTimeOffset(AHORA).ToUnixTimeSeconds().ToString(), valor);
            Assert.AreEqual(AHORA, PoliticaRefrescoToken.Leer(valor));
        }

        [TestMethod]
        public void DiasMaximos_PorDefecto90Tienda30OAuth()
        {
            Assert.AreEqual(90, PoliticaRefrescoToken.DiasMaximos(
                PoliticaRefrescoToken.SETTING_DIAS_TIENDA, PoliticaRefrescoToken.DIAS_TIENDA_POR_DEFECTO));
            Assert.AreEqual(30, PoliticaRefrescoToken.DiasMaximos(
                PoliticaRefrescoToken.SETTING_DIAS_OAUTH, PoliticaRefrescoToken.DIAS_OAUTH_POR_DEFECTO));
        }

        [TestMethod]
        public void DiasMaximos_SeLeeDeLaSetting_YSiNoValeSeUsaElDefecto()
        {
            ConfigurationManager.AppSettings[PoliticaRefrescoToken.SETTING_DIAS_TIENDA] = "45";
            Assert.AreEqual(45, PoliticaRefrescoToken.DiasMaximos(PoliticaRefrescoToken.SETTING_DIAS_TIENDA, 90));

            foreach (string malo in new[] { "0", "-5", "noventa", "" })
            {
                ConfigurationManager.AppSettings[PoliticaRefrescoToken.SETTING_DIAS_TIENDA] = malo;
                Assert.AreEqual(90, PoliticaRefrescoToken.DiasMaximos(PoliticaRefrescoToken.SETTING_DIAS_TIENDA, 90),
                    "«" + malo + "» no puede significar «sin límite»");
            }
        }
    }

    /// <summary>
    /// NestoAPI#427: los refrescos, a nivel de controller. A diferencia de los de #430, aquí sí
    /// se prueba el camino de aceptación: las dos consultas a BD de CrearJWTAsync se sustituyen
    /// por las costuras del controller y el cliente lo devuelve un IGestorClientes falso.
    /// </summary>
    [TestClass]
    public class AuthControllerRefrescoAbsolutoTests
    {
        // Clave de PRUEBA (no es la de producción), la misma que en SeguridadTanda1Tests.
        private const string SECRETO = "Y2xhdmVfZGVfcHJ1ZWJhX3BhcmFfbG9zX3Rlc3RzXzEyMzQ1Njc4OTA";
        private const string ISSUER = "carlos";
        private const string AUDIENCIA = "12345678nuevavision0123456789012";

        private IGestorClientes _gestor;

        [TestInitialize]
        public void Inicializar()
        {
            ConfigurationManager.AppSettings["as:AudienceSecret"] = SECRETO;
            ConfigurationManager.AppSettings["JwtIssuer"] = ISSUER;
            ConfigurationManager.AppSettings["as:AudienceId"] = AUDIENCIA;
            ConfigurationManager.AppSettings[PoliticaRefrescoToken.SETTING_DIAS_TIENDA] = null;
            ConfigurationManager.AppSettings[PoliticaRefrescoToken.SETTING_DIAS_OAUTH] = null;

            _gestor = A.Fake<IGestorClientes>();
            A.CallTo(() => _gestor.BuscarClientePorEmailNif("cliente@ejemplo.com", "12345678Z"))
                .Returns(Task.FromResult(new ClienteDTO { cliente = "15191" }));
        }

        private AuthController Controller(string token)
        {
            var controller = new AuthController(_gestor, A.Fake<IServicioCorreoElectronico>())
            {
                ComprasRecientesAsync = _ => Task.FromResult(false),
                NivelPreciosAsync = (c, e) => Task.FromResult(PoliticaPreciosOcultos.NivelPrecios.Completo)
            };
            var request = new HttpRequestMessage();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            controller.Request = request;
            return controller;
        }

        private static string Firmar(DateTime caduca, IEnumerable<Claim> claims, string secreto = SECRETO)
        {
            var credenciales = new SigningCredentials(
                new SymmetricSecurityKey(TextEncodings.Base64Url.Decode(secreto)), SecurityAlgorithms.HmacSha256Signature);
            var token = new JwtSecurityToken(ISSUER, AUDIENCIA, claims, caduca.AddHours(-1), caduca, credenciales);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>Un token de TNV como los que emite CrearJWTAsync, caducado hace 10 minutos.</summary>
        private static string TokenTienda(DateTime? loginUtc, string cliente = "15191")
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Email, "cliente@ejemplo.com"),
                new Claim("nif", "12345678Z"),
                new Claim("cliente", cliente)
            };
            if (loginUtc.HasValue)
            {
                claims.Add(new Claim(PoliticaRefrescoToken.CLAIM_LOGIN_UTC, PoliticaRefrescoToken.Escribir(loginUtc.Value)));
            }
            return Firmar(DateTime.UtcNow.AddMinutes(-10), claims);
        }

        private static string TokenOAuth(DateTime caduca, DateTime? loginUtc)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "vendedor") };
            if (loginUtc.HasValue)
            {
                claims.Add(new Claim(PoliticaRefrescoToken.CLAIM_LOGIN_UTC, PoliticaRefrescoToken.Escribir(loginUtc.Value)));
            }
            return Firmar(caduca, claims);
        }

        /// <summary>El token nuevo de un Ok({ token }), ya validado con nuestra clave.</summary>
        private static JwtSecurityToken TokenEmitido(IHttpActionResult resultado)
        {
            object contenido = resultado.GetType().GetProperty("Content")?.GetValue(resultado);
            Assert.IsNotNull(contenido, "Se esperaba Ok({ token }) y ha llegado " + resultado.GetType().Name);
            string jwt = (string)contenido.GetType().GetProperty("token").GetValue(contenido);
            JwtSecurityToken validado = ValidadorJwt.ValidarFirmaSinCaducidad(jwt);
            Assert.IsNotNull(validado, "El token emitido tiene que ir firmado con nuestra clave");
            return validado;
        }

        private static DateTime? LoginUtcDe(JwtSecurityToken token) =>
            PoliticaRefrescoToken.Leer(token.Claims.FirstOrDefault(c => c.Type == PoliticaRefrescoToken.CLAIM_LOGIN_UTC)?.Value);

        private static DateTime Segundos(DateTime fecha) => PoliticaRefrescoToken.Leer(PoliticaRefrescoToken.Escribir(fecha)).Value;

        // ----- refreshToken (TiendasNuevaVision) -----

        [TestMethod]
        public async Task RefreshToken_FirmaInvalida_401SinConsultarAlCliente()
        {
            var claims = new[] { new Claim(ClaimTypes.Email, "cliente@ejemplo.com"), new Claim("nif", "12345678Z"), new Claim("cliente", "15191") };
            string falsificado = Firmar(DateTime.UtcNow.AddMinutes(-10), claims,
                secreto: "b3RyYV9jbGF2ZV9kaXN0aW50YV9wYXJhX2VsX2F0YWNhbnRlXzEyMzQ1");

            var resultado = await Controller(falsificado).RefreshToken();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
            A.CallTo(() => _gestor.BuscarClientePorEmailNif(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RefreshToken_TokenValidoYCaducado_Refresca()
        {
            var resultado = await Controller(TokenTienda(DateTime.UtcNow.AddDays(-3))).RefreshToken();

            JwtSecurityToken nuevo = TokenEmitido(resultado);
            Assert.AreEqual("15191", nuevo.Claims.First(c => c.Type == "cliente").Value);
            Assert.IsTrue(nuevo.ValidTo > DateTime.UtcNow, "El token nuevo tiene que estar vivo");
        }

        [TestMethod]
        public async Task RefreshToken_ClienteAnuladoONoEncontrado_401()
        {
            // BuscarClientePorEmailNif excluye las fichas anuladas (#429): llega un DTO vacío
            A.CallTo(() => _gestor.BuscarClientePorEmailNif("cliente@ejemplo.com", "12345678Z"))
                .Returns(Task.FromResult(new ClienteDTO()));

            var resultado = await Controller(TokenTienda(DateTime.UtcNow.AddDays(-3))).RefreshToken();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task RefreshToken_SeRevalidaContraLaBD_YManda_ElClienteDeLaBD()
        {
            A.CallTo(() => _gestor.BuscarClientePorEmailNif("cliente@ejemplo.com", "12345678Z"))
                .Returns(Task.FromResult(new ClienteDTO { cliente = "15192" }));

            var resultado = await Controller(TokenTienda(DateTime.UtcNow.AddDays(-3), cliente: "15191")).RefreshToken();

            Assert.AreEqual("15192", TokenEmitido(resultado).Claims.First(c => c.Type == "cliente").Value);
            A.CallTo(() => _gestor.BuscarClientePorEmailNif("cliente@ejemplo.com", "12345678Z")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RefreshToken_PropagaLoginUtc()
        {
            DateTime login = Segundos(DateTime.UtcNow.AddDays(-40));

            var resultado = await Controller(TokenTienda(login)).RefreshToken();

            Assert.AreEqual(login, LoginUtcDe(TokenEmitido(resultado)),
                "El refresco NO reinicia el reloj: el token nuevo lleva la fecha del login original");
        }

        [TestMethod]
        public async Task RefreshToken_MasDe90DiasDesdeElLogin_401()
        {
            var resultado = await Controller(TokenTienda(DateTime.UtcNow.AddDays(-91))).RefreshToken();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task RefreshToken_ElLimiteSeConfigura()
        {
            ConfigurationManager.AppSettings[PoliticaRefrescoToken.SETTING_DIAS_TIENDA] = "30";
            try
            {
                Assert.IsInstanceOfType(await Controller(TokenTienda(DateTime.UtcNow.AddDays(-31))).RefreshToken(),
                    typeof(UnauthorizedResult));
                TokenEmitido(await Controller(TokenTienda(DateTime.UtcNow.AddDays(-29))).RefreshToken());
            }
            finally
            {
                ConfigurationManager.AppSettings[PoliticaRefrescoToken.SETTING_DIAS_TIENDA] = null;
            }
        }

        /// <summary>
        /// ⚠️ Compatibilidad TNV: un token emitido ANTES de #427 (sin login_utc) pasa una vez más y
        /// sale con login_utc = ahora. Sin esto, el despliegue echaría a todos los clientes que
        /// tienen la app abierta. Y el token que sale ya lleva el claim, así que el siguiente
        /// refresco cuenta desde hoy.
        /// </summary>
        [TestMethod]
        public async Task RefreshToken_TokenAntiguoSinLoginUtc_PasaUnaVezYSaleConLoginUtcAhora()
        {
            DateTime ahora = Segundos(DateTime.UtcNow);
            AuthController controller = Controller(TokenTienda(loginUtc: null));
            controller.AhoraUtc = () => ahora;

            JwtSecurityToken nuevo = TokenEmitido(await controller.RefreshToken());

            Assert.AreEqual(ahora, LoginUtcDe(nuevo));

            // Y el token nuevo vuelve a refrescar, conservando ese login_utc
            AuthController otra = Controller(nuevo.RawData);
            otra.AhoraUtc = () => ahora.AddDays(1);
            Assert.AreEqual(ahora, LoginUtcDe(TokenEmitido(await otra.RefreshToken())));
        }

        [TestMethod]
        public async Task RefreshToken_TokenCaducadoHaceMasDeUnMes_401_SinCambios()
        {
            var claims = new[] { new Claim(ClaimTypes.Email, "cliente@ejemplo.com"), new Claim("nif", "12345678Z"), new Claim("cliente", "15191") };

            var resultado = await Controller(Firmar(DateTime.UtcNow.AddDays(-32), claims)).RefreshToken();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task RefreshToken_SiFallaLaBD_NoEsUn401_SeVeComoError()
        {
            A.CallTo(() => _gestor.BuscarClientePorEmailNif(A<string>._, A<string>._))
                .Throws(new InvalidOperationException("BD caída"));

            _ = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => Controller(TokenTienda(DateTime.UtcNow.AddDays(-3))).RefreshToken());
        }

        // ----- refreshOAuthToken (NestoApp antigua) -----

        [TestMethod]
        public async Task RefreshOAuthToken_CaducadoHaceMasDe30Dias_401()
        {
            // Antes valía hasta 2 años
            var resultado = await Controller(TokenOAuth(DateTime.UtcNow.AddDays(-31), DateTime.UtcNow.AddDays(-32))).RefreshOAuthToken();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task RefreshOAuthToken_MasDe30DiasDesdeElLogin_401()
        {
            var resultado = await Controller(TokenOAuth(DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddDays(-31))).RefreshOAuthToken();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public void RefreshOAuthToken_LaVentanaEsDe30Dias()
        {
            Assert.AreEqual(30, AuthController.VENTANA_DIAS_REFRESCO_OAUTH);
        }
    }
}
