using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests de los decoradores de reintentos de agencias remotas (NestoAPI#288, punto 1).
    /// Usan la política real (backoff 1s/2s), así que los tests de reintento tardan segundos:
    /// es deliberado, prueban la política que corre en producción.
    /// Reglas: transitorios (DataTransException.EsTransitoria, timeout) se reintentan 2 veces en
    /// consultar/reimprimir; los errores estables no se reintentan; insertar no se reintenta NUNCA
    /// (no es idempotente: podría duplicar expediciones).
    /// </summary>
    [TestClass]
    public class ReintentosAgenciaRemotaTests
    {
        private IAgenciaRemota interior;
        private AgenciaRemotaConReintentos decorador;

        [TestInitialize]
        public void Setup()
        {
            interior = A.Fake<IAgenciaRemota>();
            decorador = new AgenciaRemotaConReintentos(interior);
        }

        private static DataTransException Transitoria()
            => new DataTransException("HTTP 500") { EsTransitoria = true };

        private static DataTransException Estable()
            => new DataTransException("SOAP Fault: credenciales") { EsTransitoria = false };

        [TestMethod]
        public async Task ConsultarSeguimiento_TransitorioUnaVez_ReintentaYDevuelveElResultado()
        {
            var seguimiento = new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Entregado };
            A.CallTo(() => interior.ConsultarSeguimientoAsync("123"))
                .Throws(Transitoria()).Once()
                .Then.Returns(Task.FromResult(seguimiento));

            var resultado = await decorador.ConsultarSeguimientoAsync("123");

            Assert.AreEqual(EstadoEnvioSeguimiento.Entregado, resultado.Estado);
            A.CallTo(() => interior.ConsultarSeguimientoAsync("123")).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_ErrorEstable_NoReintenta()
        {
            A.CallTo(() => interior.ConsultarSeguimientoAsync("123")).Throws(Estable());

            await Assert.ThrowsExceptionAsync<DataTransException>(
                () => decorador.ConsultarSeguimientoAsync("123"));

            A.CallTo(() => interior.ConsultarSeguimientoAsync("123")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_TransitorioPersistente_AgotaLosReintentosYPropaga()
        {
            A.CallTo(() => interior.ConsultarSeguimientoAsync("123")).Throws(Transitoria());

            await Assert.ThrowsExceptionAsync<DataTransException>(
                () => decorador.ConsultarSeguimientoAsync("123"));

            // 1 intento inicial + REINTENTOS
            A.CallTo(() => interior.ConsultarSeguimientoAsync("123"))
                .MustHaveHappened(1 + PoliticasAgenciasRemotas.REINTENTOS, Times.Exactly);
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_Transitorio_NoReintentaNunca()
        {
            var envio = new DatosEnvioRemoto { Referencia = "98765" };
            A.CallTo(() => interior.InsertarYEtiquetarAsync(envio)).Throws(Transitoria());

            await Assert.ThrowsExceptionAsync<DataTransException>(
                () => decorador.InsertarYEtiquetarAsync(envio));

            A.CallTo(() => interior.InsertarYEtiquetarAsync(envio)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Reimprimir_TimeoutUnaVez_ReintentaYDevuelveLaEtiqueta()
        {
            var etiqueta = new EtiquetaDataTrans();
            A.CallTo(() => interior.ReimprimirAsync("123", null, null))
                .Throws(new TaskCanceledException("timeout")).Once()
                .Then.Returns(Task.FromResult(etiqueta));

            var resultado = await decorador.ReimprimirAsync("123");

            Assert.AreSame(etiqueta, resultado);
            A.CallTo(() => interior.ReimprimirAsync("123", null, null)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public void PropiedadesDePasoDirecto_DelegaEnElInterior()
        {
            A.CallTo(() => interior.LoggingDetallado).Returns(true);

            Assert.IsTrue(decorador.LoggingDetallado);
            Assert.AreSame(interior.Intercambios, decorador.Intercambios);
        }

        [TestMethod]
        public async Task SeguimientoConReintentos_TransitorioUnaVez_Reintenta()
        {
            var interiorSeguimiento = A.Fake<ISeguimientoAgenciaRemota>();
            var decoradorSeguimiento = new SeguimientoAgenciaRemotaConReintentos(interiorSeguimiento);
            var seguimiento = new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Tramitado };
            A.CallTo(() => interiorSeguimiento.ConsultarSeguimientoAsync("A1"))
                .Throws(new System.Net.Http.HttpRequestException("conexión")).Once()
                .Then.Returns(Task.FromResult(seguimiento));

            var resultado = await decoradorSeguimiento.ConsultarSeguimientoAsync("A1");

            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, resultado.Estado);
            A.CallTo(() => interiorSeguimiento.ConsultarSeguimientoAsync("A1")).MustHaveHappenedTwiceExactly();
        }
    }
}
