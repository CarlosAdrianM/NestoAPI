using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// POST api/EnviosAgencias/{id}/Tramitar: tramitación server-side de un envío con la agencia
    /// remota. Se fakean la BD (FakeItEasy) y la factory de agencias remotas, así que NO se llama a
    /// la API real de Innovatrans.
    /// </summary>
    [TestClass]
    public class TramitarEnvioTests
    {
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<AgenciaLlamadaWeb> fakeLlamadas;
        private IFabricaAgenciasRemotas fakeFabrica;
        private IAgenciaRemota fakeAgencia;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            fakeAgencia = A.Fake<IAgenciaRemota>();
            fakeFabrica = A.Fake<IFabricaAgenciasRemotas>();

            controller = new EnviosAgenciasController(db, fakeFabrica);
            controller.Request = new System.Net.Http.HttpRequestMessage
            {
                RequestUri = new System.Uri("http://localhost/api/EnviosAgencias/1/Tramitar")
            };
        }

        private void ConEnvio(EnviosAgencia envio)
        {
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
        }

        private static EnviosAgencia EnvioPendiente() => new EnviosAgencia
        {
            Numero = 1,
            Agencia = Constantes.Agencias.AGENCIA_INNOVATRANS,
            Pedido = 12345,
            Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE,
            Nombre = "CLIENTE",
            Direccion = "Calle Mayor 1",
            CodPostal = "28001",
            Poblacion = "MADRID",
            Telefono = "600000000",
            Peso = 1.5m,
            Bultos = 1,
            Reembolso = -1m,
            CodigoBarras = null
        };

        private static EtiquetaDataTrans EtiquetaZpl() => new EtiquetaDataTrans
        {
            Tipo = "application/zpl", Codificacion = "base64", Contenido = "XlhBfkNJMTUw"
        };

        [TestMethod]
        public async Task Tramitar_EnvioInexistente_DevuelveNotFound()
        {
            A.CallTo(() => fakeEnvios.FindAsync(99)).Returns(Task.FromResult<EnviosAgencia>(null));

            var resultado = await controller.TramitarEnvio(99);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Tramitar_AgenciaSinGestionRemota_DevuelveBadRequest()
        {
            EnviosAgencia envio = EnvioPendiente();
            envio.Agencia = Constantes.Agencias.AGENCIA_GLS;
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_GLS)).Returns(null);

            var resultado = await controller.TramitarEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Tramitar_Exito_GuardaAlbaranBultosEstadoYDevuelveZpl()
        {
            EnviosAgencia envio = EnvioPendiente();
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota
                {
                    Exito = true, Albaran = "0123456789", Bultos = 2, Etiqueta = EtiquetaZpl()
                }));

            var resultado = await controller.TramitarEnvio(1);

            var ok = resultado as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual("0123456789", ok.Content.Albaran);
            Assert.AreEqual(2, ok.Content.Bultos);
            Assert.IsFalse(ok.Content.Reimpresion);
            Assert.AreEqual("XlhBfkNJMTUw", ok.Content.EtiquetaContenido);

            // Persistencia en EnviosAgencia.
            Assert.AreEqual("0123456789", envio.CodigoBarras);
            Assert.AreEqual((short)2, envio.Bultos);
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_EN_CURSO, envio.Estado);
            Assert.AreEqual(0m, envio.Reembolso); // sentinel -1 -> 0 al tramitar
            // Auditoría con éxito.
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.That.Matches(l => l.Exito && l.Agencia == "Innovatrans"))).MustHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task Tramitar_YaTramitado_SoloReimprimeSinReinsertar()
        {
            EnviosAgencia envio = EnvioPendiente();
            envio.Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO;
            envio.CodigoBarras = "0123456789"; // ya tiene albarán
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);
            A.CallTo(() => fakeAgencia.ReimprimirAsync("0123456789", null, null)).Returns(Task.FromResult(EtiquetaZpl()));

            var resultado = await controller.TramitarEnvio(1);

            var ok = resultado as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;
            Assert.IsNotNull(ok);
            Assert.IsTrue(ok.Content.Reimpresion);
            // NO se reinsertó el envío.
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Tramitar_FalloEnInsercion_NoGuardaAlbaranYAuditaError()
        {
            EnviosAgencia envio = EnvioPendiente();
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota { Exito = false, Error = "codError 500" }));

            var resultado = await controller.TramitarEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<string>));
            Assert.IsNull(envio.CodigoBarras, "No debe guardar albarán si la inserción falló.");
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_PENDIENTE, envio.Estado, "El envío queda pendiente, no a medias.");
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.That.Matches(l => !l.Exito))).MustHaveHappened();
        }
    }
}
