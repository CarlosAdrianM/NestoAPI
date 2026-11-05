using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>NestoAPI#820 (incidente 04-05/11/26): PublishAsync de Pub/Sub esperó para siempre y paró la cola 14 horas.</summary>
    [TestClass]
    public class EsperaConTimeoutTests
    {
        [TestMethod]
        public async Task Ejecutar_ClienteQueNuncaContestaNiHaceCasoDelToken_FallaAlVencerElPlazoConMensajeClaro()
        {
            // Un cliente falso que no contesta nunca y además ignora la cancelación (como una conexión gRPC colgada).
            var nunca = new TaskCompletionSource<bool>();
            var reloj = Stopwatch.StartNew();

            ServicioExternoSinRespuestaException ex = await AssertLanza(() => EsperaConTimeout.Ejecutar(
                _ => nunca.Task, TimeSpan.FromMilliseconds(200), "Pub/Sub", "al publicar en el topic sincronizacion-productos"));

            reloj.Stop();
            Assert.IsTrue(reloj.Elapsed < TimeSpan.FromSeconds(5), $"Debe cortar al vencer el plazo, no esperar ({reloj.Elapsed})");
            StringAssert.StartsWith(ex.Message, "Pub/Sub no ha contestado en 200 ms al publicar en el topic sincronizacion-productos");
            Assert.IsInstanceOfType(ex, typeof(TimeoutException));
        }

        [TestMethod]
        public async Task Ejecutar_ClienteQueRespetaElToken_CancelaLaLlamadaYDaElMismoError()
        {
            CancellationToken recibido = default;

            ServicioExternoSinRespuestaException ex = await AssertLanza(() => EsperaConTimeout.Ejecutar(
                token => { recibido = token; return Task.Delay(Timeout.Infinite, token); },
                TimeSpan.FromMilliseconds(100), "Pub/Sub", "al publicar en el topic x"));

            Assert.IsTrue(recibido.IsCancellationRequested, "El token que recibe la llamada se cancela al vencer");
            StringAssert.Contains(ex.Message, "no ha contestado en 100 ms");
        }

        [TestMethod]
        public async Task Ejecutar_LlamadaQueContestaATiempo_NoFalla()
        {
            bool hecho = false;

            await EsperaConTimeout.Ejecutar(async _ => { await Task.Delay(10); hecho = true; }, TimeSpan.FromSeconds(5), "Pub/Sub", "x");

            Assert.IsTrue(hecho);
        }

        [TestMethod]
        public async Task Ejecutar_LlamadaQueFallaPorSuCuenta_DejaPasarSuError()
        {
            try
            {
                await EsperaConTimeout.Ejecutar(_ => Task.FromException(new InvalidOperationException("topic no existe")),
                    TimeSpan.FromSeconds(5), "Pub/Sub", "x");
                Assert.Fail("Debía lanzar");
            }
            catch (InvalidOperationException ex)
            {
                Assert.AreEqual("topic no existe", ex.Message);
            }
        }

        [TestMethod]
        public void LeerTiempoMaximo_SinClaveOValorRaro_60Segundos_ConValor_EseValor()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(60), GooglePubSubEventPublisher.LeerTiempoMaximo(null));
            Assert.AreEqual(TimeSpan.FromSeconds(60), GooglePubSubEventPublisher.LeerTiempoMaximo("abc"));
            Assert.AreEqual(TimeSpan.FromSeconds(60), GooglePubSubEventPublisher.LeerTiempoMaximo("0"));
            Assert.AreEqual(TimeSpan.FromSeconds(15), GooglePubSubEventPublisher.LeerTiempoMaximo("15"));
        }

        private static async Task<ServicioExternoSinRespuestaException> AssertLanza(Func<Task> accion)
        {
            try
            {
                await accion();
            }
            catch (ServicioExternoSinRespuestaException ex)
            {
                return ex;
            }
            Assert.Fail("Debía lanzar ServicioExternoSinRespuestaException");
            return null;
        }
    }
}
