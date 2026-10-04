using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // NestoAPI#553 (Carlos, 04/10/26): las diferencias de una reposición se informan a quien creó el traspaso y, si no se
    // sabe quién fue, al grupo Almacén. Al buzón (campana) de Nesto, como los avisos a Compras.
    [TestClass]
    public class AvisadorReposicionesTests
    {
        [TestMethod]
        public async Task Avisar_AQuienCreoElTraspaso_SoloAEl()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorReposiciones(notificaciones, () => new List<string> { "Santiago" });

            await avisador.Avisar("NUEVAVISION\\Andre", "Reposición 80862 (ALG → REI)", new[] { "A: enviado 2, leído 1, diferencia -1" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Andre", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo.Contains("80862") && n.Cuerpo.Contains("leído 1"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Santiago", A<string>.Ignored, A<NotificacionPushDTO>.Ignored))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Avisar_SinCreador_AlGrupoAlmacenConElDominio()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorReposiciones(notificaciones, () => new List<string> { "Santiago", "NUEVAVISION\\Alfredo" });

            await avisador.Avisar(null, "Reposición 80862", new[] { "x" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Santiago", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Avisar_CreadorSinDominio_SeLePoneElDominio()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorReposiciones(notificaciones, () => new List<string>());

            await avisador.Avisar("Reina ", "t", new[] { "x" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Reina", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.Ignored))
                .MustHaveHappenedOnceExactly();
        }
    }
}
