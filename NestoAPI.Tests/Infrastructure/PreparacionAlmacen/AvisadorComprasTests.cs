using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // NestoAPI#559: lo que tiene que ver Compras de una recepción llega al buzón (campana) de Nesto de cada uno.
    [TestClass]
    public class AvisadorComprasTests
    {
        [TestMethod]
        public async Task Avisar_LlegaAlBuzonDeNestoDeCadaUnoDeCompras_ConElDominio()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorCompras(notificaciones, () => new List<string> { "Santiago", "NUEVAVISION\\Andre" });

            await avisador.Avisar("Recepción del proveedor 65 (Pedro)", new[] { "Del producto A han llegado 2 unidades más" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Santiago", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo.Contains("65") && n.Cuerpo.Contains("2 unidades más")))).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Andre", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Avisar_ConTipo_LoPoneEnLaNotificacion()
        {
            // NestoAPI#556: una falta del packing no es una recepción: lleva su propio tipo
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorCompras(notificaciones, () => new List<string> { "Santiago" });

            await avisador.Avisar("Packing: no aparece un producto", new[] { "Pedido 927700" }, AvisadorCompras.TIPO_PACKING_FALTA);

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Santiago", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == "PackingFalta" && n.Datos["tipo"] == "PackingFalta"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Avisar_SinAvisos_NoMandaNada()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorCompras(notificaciones, () => new List<string> { "Santiago" });

            await avisador.Avisar("x", new string[0]);

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>.Ignored, A<string>.Ignored, A<NotificacionPushDTO>.Ignored)).MustNotHaveHappened();
        }
    }
}
