using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#748: GET api/EnviosAgencias/{id}/EnlaceSeguimiento. Nesto no puede calcular el enlace del reparto
    /// propio (va firmado con la clave de la API) y se lo pide aquí para activar el botón «Abrir seguimiento».
    /// </summary>
    [TestClass]
    public class EnlaceSeguimientoEnvioTests
    {
        private const string CLAVE = "clave-de-pruebas-del-seguimiento-reparto-propio-748";
        private Func<string> claveOriginal;
        private Func<string> urlOriginal;
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<AgenciaTransporte> fakeAgencias;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Preparar()
        {
            claveOriginal = EnlaceSeguimientoRepartoPropio.LeerClave;
            urlOriginal = EnlaceSeguimientoRepartoPropio.LeerUrlBase;
            EnlaceSeguimientoRepartoPropio.LeerClave = () => CLAVE;
            EnlaceSeguimientoRepartoPropio.LeerUrlBase = () => "https://api.nuevavision.es/";

            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeAgencias = A.Fake<DbSet<AgenciaTransporte>>(o => o.Implements<IQueryable<AgenciaTransporte>>().Implements<IDbAsyncEnumerable<AgenciaTransporte>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasTransportes).Returns(fakeAgencias);
            controller = new EnviosAgenciasController(db);
        }

        [TestCleanup]
        public void Restaurar()
        {
            EnlaceSeguimientoRepartoPropio.LeerClave = claveOriginal;
            EnlaceSeguimientoRepartoPropio.LeerUrlBase = urlOriginal;
        }

        private void ConEnvio(EnviosAgencia envio)
            => A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));

        private void ConAgencia(AgenciaTransporte agencia)
            => A.CallTo(() => fakeAgencias.FindAsync(agencia.Numero)).Returns(Task.FromResult(agencia));

        [TestMethod]
        public async Task GetEnlaceSeguimiento_RepartoPropio_DevuelveLaPaginaPublicaFirmada()
        {
            ConEnvio(new EnviosAgencia { Numero = 123456, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, CodigoBarras = "RP123456" });

            var resultado = await controller.GetEnlaceSeguimiento(123456) as OkNegotiatedContentResult<EnlaceSeguimientoEnvioDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(123456, resultado.Content.Numero);
            Assert.AreEqual("https://api.nuevavision.es/seguimiento/" + EnlaceSeguimientoRepartoPropio.Token(CLAVE, 123456), resultado.Content.Url);
            Assert.IsTrue(EnlaceSeguimientoRepartoPropio.TryLeerEnvio(resultado.Content.Url.Substring(resultado.Content.Url.LastIndexOf('/') + 1), out int envio));
            Assert.AreEqual(123456, envio);
        }

        [TestMethod]
        public async Task GetEnlaceSeguimiento_RepartoPropioSinCodigoBarras_TambienDaElEnlace()
        {
            // Se firma el número del envío, no hace falta el albarán RP{envío}
            ConEnvio(new EnviosAgencia { Numero = 77, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, CodigoBarras = null });

            var resultado = await controller.GetEnlaceSeguimiento(77) as OkNegotiatedContentResult<EnlaceSeguimientoEnvioDTO>;

            Assert.IsNotNull(resultado?.Content.Url);
            A.CallTo(() => fakeAgencias.FindAsync(A<object[]>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetEnlaceSeguimiento_RepartoPropioSinClave_UrlNull()
        {
            EnlaceSeguimientoRepartoPropio.LeerClave = () => null;
            ConEnvio(new EnviosAgencia { Numero = 77, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, CodigoBarras = "RP77" });

            var resultado = await controller.GetEnlaceSeguimiento(77) as OkNegotiatedContentResult<EnlaceSeguimientoEnvioDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsNull(resultado.Content.Url);
        }

        [TestMethod]
        public async Task GetEnlaceSeguimiento_OtraAgencia_UsaSuEstrategia()
        {
            ConAgencia(new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_CTT, Nombre = "CTT" });
            ConEnvio(new EnviosAgencia { Numero = 5, Agencia = Constantes.Agencias.AGENCIA_CTT, CodigoBarras = "0082800082809772536836", CodPostal = "28001" });

            var resultado = await controller.GetEnlaceSeguimiento(5) as OkNegotiatedContentResult<EnlaceSeguimientoEnvioDTO>;

            Assert.AreEqual("https://www.cttexpress.com/localizador-de-envios?sc=0082800082809772536836", resultado?.Content.Url);
        }

        [TestMethod]
        public async Task GetEnlaceSeguimiento_OtraAgenciaSinAlbaran_UrlNull()
        {
            ConAgencia(new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_CTT, Nombre = "CTT" });
            ConEnvio(new EnviosAgencia { Numero = 5, Agencia = Constantes.Agencias.AGENCIA_CTT, CodigoBarras = null });

            var resultado = await controller.GetEnlaceSeguimiento(5) as OkNegotiatedContentResult<EnlaceSeguimientoEnvioDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsNull(resultado.Content.Url);
        }

        [TestMethod]
        public async Task GetEnlaceSeguimiento_EnvioQueNoExiste_404()
        {
            A.CallTo(() => fakeEnvios.FindAsync(A<object[]>._)).Returns(Task.FromResult<EnviosAgencia>(null));

            var resultado = await controller.GetEnlaceSeguimiento(999);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }
    }
}
