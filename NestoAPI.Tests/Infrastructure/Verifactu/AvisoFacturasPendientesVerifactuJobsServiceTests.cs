using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// NestoAPI#522: recordatorio diario a administración en la campana de Nesto mientras haya facturas
    /// pendientes de Verifactu o incorrectas en la AEAT (al pulsarlo, Nesto abre la ventana).
    /// </summary>
    [TestClass]
    public class AvisoFacturasPendientesVerifactuJobsServiceTests
    {
        private IServicioFacturasPendientesVerifactu pendientes;
        private IServicioNotificacionesPush notificaciones;
        private IServicioVerifactu verifactu;
        private List<string> grupoAdministracion;
        private Dictionary<string, string> parametros;
        private AvisoFacturasPendientesVerifactuJobsService job;

        [TestInitialize]
        public void Setup()
        {
            pendientes = A.Fake<IServicioFacturasPendientesVerifactu>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            verifactu = A.Fake<IServicioVerifactu>();
            A.CallTo(() => verifactu.EstaHabilitado).Returns(true);
            grupoAdministracion = new List<string> { "Laura", "Enrique" };
            parametros = new Dictionary<string, string>();
            job = new AvisoFacturasPendientesVerifactuJobsService(pendientes, notificaciones, verifactu,
                () => grupoAdministracion, clave => parametros.TryGetValue(clave, out string valor) ? valor : null);
        }

        private void ConPendientes(params string[] situaciones)
        {
            A.CallTo(() => pendientes.Listar()).Returns(situaciones
                .Select((s, i) => new FacturaPendienteVerifactuDTO { Numero = "NV26150" + i, Situacion = s })
                .ToList());
        }

        [TestMethod]
        public async Task ConPendientes_NotaEnLaCampanaDeCadaUnoDeAdministracion_QueAbreLaVentana()
        {
            ConPendientes(ServicioFacturasPendientesVerifactu.SITUACION_PENDIENTE,
                ServicioFacturasPendientesVerifactu.SITUACION_INCORRECTA_AEAT);

            int avisados = await job.AvisarSiHayPendientes();

            Assert.AreEqual(2, avisados);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Laura", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisoFacturasPendientesVerifactuJobsService.TIPO_NOTIFICACION
                    && n.Datos["tipo"] == AvisoFacturasPendientesVerifactuJobsService.TIPO_NOTIFICACION
                    && n.Cuerpo.Contains("Hay 2 facturas") && n.Cuerpo.Contains("1 sin registrar")
                    && n.Cuerpo.Contains("1 incorrecta en la AEAT"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Enrique", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.Ignored)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task SinPendientes_NoAvisa()
        {
            ConPendientes();

            Assert.AreEqual(0, await job.AvisarSiHayPendientes());
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>.Ignored, A<string>.Ignored, A<NotificacionPushDTO>.Ignored))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SoloSinDatosFiscales_NoAvisa()
        {
            // Las del camino viejo (#348) no tienen arreglo desde administración: encenderían la campana cada día
            ConPendientes(ServicioFacturasPendientesVerifactu.SITUACION_SIN_DATOS_FISCALES);

            Assert.AreEqual(0, await job.AvisarSiHayPendientes());
        }

        [TestMethod]
        public async Task InterruptorApagado_NoAvisa()
        {
            ConPendientes(ServicioFacturasPendientesVerifactu.SITUACION_PENDIENTE);
            parametros[AvisoFacturasPendientesVerifactuJobsService.CLAVE_ACTIVO] = "false";

            Assert.AreEqual(0, await job.AvisarSiHayPendientes());
            A.CallTo(() => pendientes.Listar()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task VerifactuDeshabilitado_NoAvisa()
        {
            ConPendientes(ServicioFacturasPendientesVerifactu.SITUACION_PENDIENTE);
            A.CallTo(() => verifactu.EstaHabilitado).Returns(false);

            Assert.AreEqual(0, await job.AvisarSiHayPendientes());
        }

        [TestMethod]
        public async Task ListaDeUsuariosEnElParametro_MandaSobreElGrupo()
        {
            ConPendientes(ServicioFacturasPendientesVerifactu.SITUACION_PENDIENTE);
            parametros[AvisoFacturasPendientesVerifactuJobsService.CLAVE_USUARIOS] = "Aida, NUEVAVISION\\Carlos";

            int avisados = await job.AvisarSiHayPendientes();

            Assert.AreEqual(2, avisados);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Aida", A<string>.Ignored, A<NotificacionPushDTO>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", A<string>.Ignored, A<NotificacionPushDTO>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Laura", A<string>.Ignored, A<NotificacionPushDTO>.Ignored))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public void CrearNotificacion_UnaSola_EnSingular()
        {
            NotificacionPushDTO n = AvisoFacturasPendientesVerifactuJobsService.CrearNotificacion(new List<FacturaPendienteVerifactuDTO>
            {
                new FacturaPendienteVerifactuDTO { Situacion = ServicioFacturasPendientesVerifactu.SITUACION_INCIDENCIA }
            });

            StringAssert.StartsWith(n.Cuerpo, "Hay 1 factura que");
            StringAssert.Contains(n.Cuerpo, "(1 sin registrar)");
        }
    }
}
