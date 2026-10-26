using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#595 (slice 4): POST api/EnviosAgencias/{id}/ImprimirEtiqueta imprime en el servidor la etiqueta de un
    /// envío ya tramitado (CTT/Innovatrans) en la impresora del usuario. No se llama a ninguna agencia ni impresora real.
    /// </summary>
    [TestClass]
    public class ImprimirEtiquetaEnvioTests
    {
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<AgenciaLlamadaWeb> fakeLlamadas;
        private IFabricaAgenciasRemotas fakeFabrica;
        private IAgenciaRemota fakeAgencia;
        private ILectorParametrosUsuario parametros;
        private IImpresoraEtiquetas impresora;
        private EnviosAgenciasController controller;
        private AgenciaLlamadaWeb auditoria;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.Ignored))
                .Invokes((AgenciaLlamadaWeb l) => auditoria = l)
                .ReturnsLazily((AgenciaLlamadaWeb l) => l);

            fakeAgencia = A.Fake<IAgenciaRemota>();
            A.CallTo(() => fakeAgencia.Intercambios).Returns(new List<IntercambioRemoto>());
            fakeFabrica = A.Fake<IFabricaAgenciasRemotas>();
            // Como la fábrica real: solo las agencias con gestión remota (aquí CTT) devuelven estrategia.
            A.CallTo(() => fakeFabrica.Crear(A<int>._)).Returns(null);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_CTT)).Returns(fakeAgencia);

            parametros = A.Fake<ILectorParametrosUsuario>();
            impresora = A.Fake<IImpresoraEtiquetas>();

            controller = new EnviosAgenciasController(db, fakeFabrica)
            {
                LectorParametros = parametros,
                ImpresoraEtiquetas = impresora
            };
            controller.Request = new System.Net.Http.HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/1/ImprimirEtiqueta") };
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Alfredo") }, "Test"));
            auditoria = null;
        }

        private EnviosAgencia ConEnvio(int agencia, string codigoBarras)
        {
            var envio = new EnviosAgencia
            {
                Numero = 250100, Empresa = "1  ", Agencia = agencia, Pedido = 927700, Estado = 0, Bultos = 2,
                CodigoBarras = codigoBarras, CodPostal = "28001"
            };
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
            return envio;
        }

        [TestMethod]
        public async Task EnvioInexistente_404()
        {
            A.CallTo(() => fakeEnvios.FindAsync(1)).Returns(Task.FromResult<EnviosAgencia>(null));

            Assert.IsInstanceOfType(await controller.ImprimirEtiqueta(1, null), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task AgenciaSinGestionRemota_400QueSigueSaliendoDeNesto()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_GLS, "61771234567890");

            var resultado = await controller.ImprimirEtiqueta(250100, null) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "todavía se imprime desde Nesto (#552)");
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinCodigoDeBarras_409YNoTramita()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, null);

            var resultado = await controller.ImprimirEtiqueta(250100, null) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            Assert.AreEqual("Primero hay que tramitar el envío.", resultado.Content);
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>._)).MustNotHaveHappened();
            A.CallTo(() => fakeAgencia.ReimprimirAsync(A<string>._, A<int?>._, A<int?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ConCodigoDeBarras_ImprimeEnLaImpresoraDelUsuarioYAudita()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, "CTT123  ");
            A.CallTo(() => parametros.LeerParametro("1", "Alfredo", "ImpresoraCodBarras")).Returns("ZEBRA-EXPEDICION ");
            // "^XA^FDHola^XZ" en base64
            A.CallTo(() => fakeAgencia.ReimprimirAsync("CTT123", null, null)).Returns(Task.FromResult(new EtiquetaDataTrans
            {
                Tipo = "application/zpl", Codificacion = "base64", Contenido = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("^XA^FDHola^XZ"))
            }));

            var resultado = await controller.ImprimirEtiqueta(250100, null) as OkNegotiatedContentResult<ImprimirEtiquetaEnvioResultadoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(250100, resultado.Content.Envio);
            Assert.AreEqual("CTT123", resultado.Content.CodigoBarras);
            Assert.AreEqual(2, resultado.Content.Bultos);
            Assert.AreEqual("ZEBRA-EXPEDICION", resultado.Content.Impresora);
            Assert.IsTrue(resultado.Content.Reimpresion);
            A.CallTo(() => impresora.Imprimir("ZEBRA-EXPEDICION", "^XA^FDHola^XZ", A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>._)).MustNotHaveHappened();
            Assert.IsNotNull(auditoria);
            Assert.IsTrue(auditoria.Exito);
            StringAssert.Contains(auditoria.CuerpoLlamada, "reimpresión");
        }

        [TestMethod]
        public async Task ConImpresoraEnLaPeticion_UsaEsaSinMirarLaDelUsuario()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, "CTT123");
            A.CallTo(() => fakeAgencia.ReimprimirAsync("CTT123", null, null)).Returns(Task.FromResult(new EtiquetaDataTrans { Contenido = "^XA^XZ" }));

            var resultado = await controller.ImprimirEtiqueta(250100, new ImprimirEtiquetaEnvioDTO { Impresora = "ZEBRA-ARIADNA" })
                as OkNegotiatedContentResult<ImprimirEtiquetaEnvioResultadoDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => impresora.Imprimir("ZEBRA-ARIADNA", "^XA^XZ", A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => parametros.LeerParametro(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UsuarioSinImpresora_400Claro()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, "CTT123");

            var resultado = await controller.ImprimirEtiqueta(250100, null) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "no tiene impresora de etiquetas");
            A.CallTo(() => fakeAgencia.ReimprimirAsync(A<string>._, A<int?>._, A<int?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LaAgenciaNoDevuelveZpl_502YNoImprime()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, "CTT123");
            A.CallTo(() => parametros.LeerParametro("1", "Alfredo", "ImpresoraCodBarras")).Returns("ZEBRA-EXPEDICION");
            A.CallTo(() => fakeAgencia.ReimprimirAsync("CTT123", null, null)).Returns(Task.FromResult(new EtiquetaDataTrans { Contenido = "JVBERi0=" }));

            var resultado = await controller.ImprimirEtiqueta(250100, null) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.StatusCode);
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.IsFalse(auditoria.Exito);
        }

        [TestMethod]
        public async Task WindowsNoDejaImprimir_502()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, "CTT123");
            A.CallTo(() => parametros.LeerParametro("1", "Alfredo", "ImpresoraCodBarras")).Returns("ZEBRA-EXPEDICION");
            A.CallTo(() => fakeAgencia.ReimprimirAsync("CTT123", null, null)).Returns(Task.FromResult(new EtiquetaDataTrans { Contenido = "^XA^XZ" }));
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._))
                .Throws(new ImpresionEtiquetasException("No se puede abrir la impresora ZEBRA-EXPEDICION (error de Windows 5)."));

            var resultado = await controller.ImprimirEtiqueta(250100, null) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.StatusCode);
            StringAssert.Contains(resultado.Content, "error de Windows 5");
        }

        [TestMethod]
        public async Task LaAgenciaNoResponde_502ConMensajeClaroYNoImprime()
        {
            // Sugerencia 507: con CTT caído, el aviso de borrar el código de barras (Nesto#412) despistaba.
            EnviosAgencia envio = ConEnvio(Constantes.Agencias.AGENCIA_CTT, "CTT123");
            envio.AgenciasTransporte = new AgenciaTransporte { Numero = envio.Agencia, Nombre = "CTT" };
            A.CallTo(() => parametros.LeerParametro("1", "Alfredo", "ImpresoraCodBarras")).Returns("ZEBRA-EXPEDICION");
            A.CallTo(() => fakeAgencia.ReimprimirAsync("CTT123", null, null))
                .ThrowsAsync(new AgenciaRemotaException("CTT (Etiqueta) respondió HTTP 500: CONNECTIVITY") { EsTransitoria = true });

            var resultado = await controller.ImprimirEtiqueta(250100, null) as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.Response.StatusCode);
            var cuerpo = Newtonsoft.Json.Linq.JObject.Parse(await resultado.Response.Content.ReadAsStringAsync());
            Assert.AreEqual(EnviosAgenciasController.CODIGO_AGENCIA_NO_RESPONDE, (string)cuerpo["Codigo"]);
            StringAssert.StartsWith((string)cuerpo["Message"], "CTT no responde ahora mismo");
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.IsFalse(auditoria.Exito);
            StringAssert.Contains(auditoria.TextoRespuestaError, "CONNECTIVITY");
        }
    }
}
