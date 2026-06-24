using System;
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
        public async Task ConsultarSeguimiento_SinDatosEnGls_DevuelveTramitado()
        {
            var agencia = new AgenciaRemotaGls(Fake("<expediciones></expediciones>"));

            SeguimientoEnvioRemoto seg = await agencia.ConsultarSeguimientoAsync("61197140245758");

            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, seg.Estado);
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
    }
}
