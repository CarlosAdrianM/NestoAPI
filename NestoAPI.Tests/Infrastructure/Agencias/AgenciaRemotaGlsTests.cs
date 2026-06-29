using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Seguimiento de GLS (GetExpCli): traduce el estado de cabecera (estado/incidencia/pod) al modelo
    /// común. Se usa un fake del cliente de tracking (no se llama a GLS), con el XML real de ejemplo.
    /// </summary>
    [TestClass]
    public class AgenciaRemotaGlsTests
    {
        [TestMethod]
        public async Task ConsultarSeguimiento_Entregado_DevuelveEntregadoConFechaPod()
        {
            var agencia = new AgenciaRemotaGls(Fake(Exp("ENTREGADO", "SIN INCIDENCIA", "22/06/2026 13:53:00")));

            SeguimientoEnvioRemoto seg = await agencia.ConsultarSeguimientoAsync("61197140245758");

            Assert.AreEqual(EstadoEnvioSeguimiento.Entregado, seg.Estado);
            Assert.AreEqual(new DateTime(2026, 6, 22, 13, 53, 0), seg.FechaEntrega);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_ConIncidenciaYSinEntregar_DevuelveIncidentado()
        {
            var agencia = new AgenciaRemotaGls(Fake(Exp("EN REPARTO", "AUSENTE", "")));

            SeguimientoEnvioRemoto seg = await agencia.ConsultarSeguimientoAsync("61197140245758");

            Assert.AreEqual(EstadoEnvioSeguimiento.Incidentado, seg.Estado);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_DevueltoAOrigen_DevuelveDevuelto()
        {
            var agencia = new AgenciaRemotaGls(Fake(Exp("DEVUELTO A ORIGEN", "SIN INCIDENCIA", "")));

            SeguimientoEnvioRemoto seg = await agencia.ConsultarSeguimientoAsync("61197140245758");

            Assert.AreEqual(EstadoEnvioSeguimiento.Devuelto, seg.Estado);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_EnTransitoSinIncidencia_DevuelveTramitado()
        {
            var agencia = new AgenciaRemotaGls(Fake(Exp("EN TRÁNSITO", "SIN INCIDENCIA", "")));

            SeguimientoEnvioRemoto seg = await agencia.ConsultarSeguimientoAsync("61197140245758");

            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, seg.Estado);
            Assert.IsNull(seg.FechaEntrega);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_SinExpEnGls_DevuelveDesconocido()
        {
            // NestoAPI#264: sin <exp> NO es "Tramitado" (eso pisaba un Incidentado/Entregado real con un
            // estado falso). Es Desconocido = sin cambio. Pasa con envío nuevo o con uid de seguimiento mal.
            var agencia = new AgenciaRemotaGls(Fake("<expediciones></expediciones>"));

            SeguimientoEnvioRemoto seg = await agencia.ConsultarSeguimientoAsync("61197140245758");

            Assert.AreEqual(EstadoEnvioSeguimiento.Desconocido, seg.Estado);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_ErrorNoEncuentraExpedicion_DevuelveDesconocidoConDetalle()
        {
            // Caso real (NestoAPI#264): uid de seguimiento incorrecta -> GLS devuelve <Error>. NO debe
            // tratarse como Tramitado (vaciaba los incidentados); Desconocido = sin cambio, con el detalle.
            var agencia = new AgenciaRemotaGls(Fake(
                "<expediciones><Error>No se encuentra la expedición</Error></expediciones>"));

            SeguimientoEnvioRemoto seg = await agencia.ConsultarSeguimientoAsync("61197140245758");

            Assert.AreEqual(EstadoEnvioSeguimiento.Desconocido, seg.Estado);
            Assert.AreEqual("No se encuentra la expedición", seg.Detalle);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_GlsDevuelveHttp500_DevuelveDesconocido()
        {
            // Caso real (NestoAPI#264): asmred devuelve HTTP 500 para un albarán concreto (albarán
            // 61197140246050, 27/06/2026). GetStringAsync lanza HttpRequestException. NO debe tumbar el job
            // ni pisar el estado real: Desconocido = sin cambio. El detalle conserva el mensaje del fallo.
            var agencia = new AgenciaRemotaGls(new FakeTrackingGlsQueFalla(
                new HttpRequestException("El código de estado de la respuesta no indica un resultado correcto: 500 (Internal Server Error).")));

            SeguimientoEnvioRemoto seg = await agencia.ConsultarSeguimientoAsync("61197140246050");

            Assert.AreEqual(EstadoEnvioSeguimiento.Desconocido, seg.Estado);
            StringAssert.Contains(seg.Detalle, "500");
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_GlsTimeout_DevuelveDesconocido()
        {
            // El timeout del HttpClient lanza TaskCanceledException, no HttpRequestException. En la misma
            // caída del 27/06/2026 hubo 2 timeouts: también deben ser Desconocido (sin cambio), no tumbar el job.
            var agencia = new AgenciaRemotaGls(new FakeTrackingGlsQueFalla(
                new TaskCanceledException("Se canceló una tarea.")));

            SeguimientoEnvioRemoto seg = await agencia.ConsultarSeguimientoAsync("61197140246050");

            Assert.AreEqual(EstadoEnvioSeguimiento.Desconocido, seg.Estado);
        }

        private static string Exp(string estado, string incidencia, string pod) =>
            $@"<expediciones><exp>
                 <estado>{estado}</estado>
                 <incidencia>{incidencia}</incidencia>
                 <pod>{pod}</pod>
                 <FPEntrega>22/06/2026 0:00:00</FPEntrega>
               </exp></expediciones>";

        private static IClienteTrackingGls Fake(string xml) => new FakeTrackingGls(xml);

        private class FakeTrackingGls : IClienteTrackingGls
        {
            private readonly string _xml;
            public FakeTrackingGls(string xml) => _xml = xml;
            public Task<XDocument> ConsultarAsync(string albaran) => Task.FromResult(XDocument.Parse(_xml));
        }

        // Fake que simula un fallo HTTP de asmred (p. ej. 500): GetStringAsync lanza HttpRequestException.
        private class FakeTrackingGlsQueFalla : IClienteTrackingGls
        {
            private readonly Exception _ex;
            public FakeTrackingGlsQueFalla(Exception ex) => _ex = ex;
            public Task<XDocument> ConsultarAsync(string albaran) => Task.FromException<XDocument>(_ex);
        }
    }
}
