using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>11/11/26: ubicaciones de ALG de albaranes que ya no existen (se deshizo el albarán y no volvieron).</summary>
    [TestClass]
    public class UbicacionesHuerfanasJobsServiceTests
    {
        private IServicioNotificacionesPush notificaciones;
        private IServicioCorreoElectronico correo;
        private List<UbicacionHuerfana> filas;
        private DateTime? desdePedido;

        [TestInitialize]
        public void Preparar()
        {
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            correo = A.Fake<IServicioCorreoElectronico>();
            filas = new List<UbicacionHuerfana>();
            desdePedido = null;
        }

        private UbicacionesHuerfanasJobsService Servicio() => new UbicacionesHuerfanasJobsService(
            desde => { desdePedido = desde; return Task.FromResult(filas); },
            () => new List<string> { "Carlos", @"NUEVAVISION\Alfredo", "carlos" },
            notificaciones, correo);

        private static UbicacionHuerfana Fila(int albaran, string producto, int cantidad, DateTime creada, int? pedido = 930128) =>
            new UbicacionHuerfana { Albaran = albaran, Producto = producto, Cantidad = cantidad, FechaCreacion = creada, Pedido = pedido, EmpresaAlbaran = "3" };

        [TestMethod]
        public void Desde_SieteDiasPeroNuncaAntesDelArranque()
        {
            Assert.AreEqual(UbicacionesHuerfanasJobsService.FECHA_ARRANQUE, UbicacionesHuerfanasJobsService.Desde(new DateTime(2026, 11, 12, 4, 30, 0)));
            Assert.AreEqual(new DateTime(2026, 11, 23, 4, 30, 0), UbicacionesHuerfanasJobsService.Desde(new DateTime(2026, 11, 30, 4, 30, 0)));
        }

        [TestMethod]
        public async Task ConHuerfanas_AvisaADireccionYAlmacenPorCampanaYCorreo_ConLaLista()
        {
            DateTime ahora = new DateTime(2026, 11, 20, 4, 30, 0);
            filas.Add(Fila(732688, "37539", -1, ahora.AddDays(-1)));
            filas.Add(Fila(732688, "37539", -2, ahora.AddDays(-1)));
            filas.Add(Fila(732688, "41890", -1, ahora.AddDays(-1)));
            NotificacionPushDTO enviada = null;
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string u, string a, NotificacionPushDTO n) => enviada = n);
            MailMessage correoEnviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => correoEnviado = m);

            int lineas = await Servicio().ComprobarAsync(ahora);

            Assert.AreEqual(2, lineas);
            Assert.AreEqual(new DateTime(2026, 11, 13, 4, 30, 0), desdePedido);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedTwiceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Carlos", A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(enviada.Cuerpo, UbicacionesHuerfanasJobsService.TEXTO);
            StringAssert.Contains(enviada.Cuerpo, "Producto 37539, pedido 930128, albarán 732688, 3 unidades");
            StringAssert.Contains(enviada.Cuerpo, "Producto 41890, pedido 930128, albarán 732688, 1 unidad");
            Assert.AreEqual("Ubicaciones huérfanas del albarán 732688", enviada.Titulo);
            CollectionAssert.AreEquivalent(new[] { Constantes.Correos.CORREO_DIRECCION, Constantes.Correos.ALMACEN },
                correoEnviado.To.Select(t => t.Address).ToArray());
        }

        [TestMethod]
        public async Task SinHuerfanas_NoAvisa()
        {
            Assert.AreEqual(0, await Servicio().ComprobarAsync(new DateTime(2026, 11, 20)));
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LoAnteriorAlArranque_NoAvisa()
        {
            filas.Add(Fila(732939, "37573", -1, new DateTime(2026, 11, 9, 10, 37, 0)));

            Assert.AreEqual(0, await Servicio().ComprobarAsync(new DateTime(2026, 11, 12, 4, 30, 0)));
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiFallaLaLectura_NoLanza()
        {
            var servicio = new UbicacionesHuerfanasJobsService(_ => throw new InvalidOperationException("BD caída"), () => new List<string>(), notificaciones, correo);
            Assert.AreEqual(0, await servicio.ComprobarAsync(new DateTime(2026, 11, 20)));
        }

        [TestMethod]
        public void Sql_EntraPorNumeroDeOrdenYSoloLee()
        {
            StringAssert.Contains(UbicacionesHuerfanasJobsService.SQL, "u.[NºOrden] >= @MinOrden");
            StringAssert.Contains(UbicacionesHuerfanasJobsService.SQL, "u.[Almacén] = 'ALG'");
            Assert.IsFalse(UbicacionesHuerfanasJobsService.SQL.ToUpperInvariant().Contains("UPDATE"));
            Assert.IsFalse(UbicacionesHuerfanasJobsService.SQL.ToUpperInvariant().Contains("DELETE"));
        }
    }
}
