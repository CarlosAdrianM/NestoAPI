using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.VigilanciaHangfire;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.VigilanciaHangfire
{
    /// <summary>
    /// NestoAPI#820 (incidente 04-05/11/26): el único worker de «default» se quedó 14 horas en SincronizarProductos y se
    /// acumularon 635 jobs sin que nadie se enterase.
    /// </summary>
    [TestClass]
    public class VigilanteHangfireTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);

        [TestInitialize]
        public void Inicializar()
        {
            DeduplicadorAvisosHangfire.Reset();
        }

        private static JobEnCurso Job(long id, int minutos, string cola = "default", string nombre = "SincronizacionJobsService.SincronizarProductos")
        {
            return new JobEnCurso { Id = id, Nombre = nombre, Cola = cola, EmpezoUtc = AHORA.AddMinutes(-minutos) };
        }

        #region Evaluar

        [TestMethod]
        public void Evaluar_TodoNormal_SinAvisos()
        {
            List<AvisoHangfire> avisos = VigilanteHangfire.Evaluar(new List<JobEnCurso> { Job(1, 2), Job(2, 15) }, 100, AHORA);

            Assert.AreEqual(0, avisos.Count, "15 minutos justos y 100 encolados aún son normales");
        }

        [TestMethod]
        public void Evaluar_SinDatos_SinAvisos()
        {
            Assert.AreEqual(0, VigilanteHangfire.Evaluar(null, 0, AHORA).Count);
        }

        [TestMethod]
        public void Evaluar_JobViejo_AvisaConNombreColaYTiempo()
        {
            // El caso del 04/11: 146824 en Processing desde las 18:55 UTC.
            List<AvisoHangfire> avisos = VigilanteHangfire.Evaluar(new List<JobEnCurso> { Job(146824, 13 * 60 + 5) }, 3, AHORA);

            Assert.AreEqual(1, avisos.Count);
            Assert.AreEqual("colgado:146824", avisos[0].Clave);
            Assert.AreEqual("Hangfire: job colgado", avisos[0].Titulo);
            Assert.AreEqual("El job SincronizacionJobsService.SincronizarProductos (n.º 146824, cola «default») lleva 13 horas y 5 minutos " +
                "en marcha. Mientras siga así, los demás jobs de su cola no se ejecutan. Compruébelo en /hangfire y, si no avanza, " +
                "recicle el pool de NestoAPI.", avisos[0].Texto);
        }

        [TestMethod]
        public void Evaluar_ColaDeEntrenamiento_TieneMasMargen()
        {
            // Reentrenar el modelo tarda horas por diseño y tiene su propio servidor.
            List<AvisoHangfire> avisos = VigilanteHangfire.Evaluar(new List<JobEnCurso>
            {
                Job(1, 3 * 60, "entrenamiento"),
                Job(2, 3 * 60, "indicadores"),
                Job(3, 9 * 60, "entrenamiento")
            }, 0, AHORA);

            Assert.AreEqual(1, avisos.Count);
            Assert.AreEqual("colgado:3", avisos[0].Clave);
        }

        [TestMethod]
        public void Evaluar_ColaGrande_Avisa()
        {
            List<AvisoHangfire> avisos = VigilanteHangfire.Evaluar(new List<JobEnCurso>(), 635, AHORA);

            Assert.AreEqual(1, avisos.Count);
            Assert.AreEqual(VigilanteHangfire.CLAVE_COLA_GRANDE, avisos[0].Clave);
            Assert.AreEqual("Hay 635 jobs de Hangfire esperando a ejecutarse (lo normal es menos de 100). Puede que un job colgado " +
                "tenga parada la cola: compruébelo en /hangfire.", avisos[0].Texto);
        }

        [TestMethod]
        public void Evaluar_JobViejoYColaGrande_LosDosAvisos()
        {
            List<AvisoHangfire> avisos = VigilanteHangfire.Evaluar(new List<JobEnCurso> { Job(7, 60) }, 101, AHORA);

            CollectionAssert.AreEqual(new[] { "colgado:7", "cola-grande" }, avisos.Select(a => a.Clave).ToArray());
        }

        #endregion

        #region Una vez por episodio

        [TestMethod]
        public void Deduplicador_MismoEpisodio_AvisaSoloLaPrimeraVez()
        {
            List<AvisoHangfire> primera = DeduplicadorAvisosHangfire.Nuevos(VigilanteHangfire.Evaluar(new List<JobEnCurso> { Job(7, 20) }, 200, AHORA));
            List<AvisoHangfire> segunda = DeduplicadorAvisosHangfire.Nuevos(VigilanteHangfire.Evaluar(new List<JobEnCurso> { Job(7, 30) }, 300, AHORA));

            Assert.AreEqual(2, primera.Count);
            Assert.AreEqual(0, segunda.Count, "Sigue el mismo job colgado y la misma cola atascada: no se repite");
        }

        [TestMethod]
        public void Deduplicador_TerminaElEpisodioYVuelve_AvisaOtraVez()
        {
            _ = DeduplicadorAvisosHangfire.Nuevos(VigilanteHangfire.Evaluar(new List<JobEnCurso>(), 200, AHORA));
            List<AvisoHangfire> normal = DeduplicadorAvisosHangfire.Nuevos(VigilanteHangfire.Evaluar(new List<JobEnCurso>(), 5, AHORA));
            List<AvisoHangfire> otraVez = DeduplicadorAvisosHangfire.Nuevos(VigilanteHangfire.Evaluar(new List<JobEnCurso>(), 150, AHORA));

            Assert.AreEqual(0, normal.Count);
            Assert.AreEqual(1, otraVez.Count, "La cola bajó y ha vuelto a subir: es otro episodio");
        }

        [TestMethod]
        public void Deduplicador_OtroJobColgado_EsOtroAviso()
        {
            _ = DeduplicadorAvisosHangfire.Nuevos(VigilanteHangfire.Evaluar(new List<JobEnCurso> { Job(7, 20) }, 0, AHORA));
            List<AvisoHangfire> nuevos = DeduplicadorAvisosHangfire.Nuevos(VigilanteHangfire.Evaluar(new List<JobEnCurso> { Job(7, 30), Job(8, 20) }, 0, AHORA));

            Assert.AreEqual(1, nuevos.Count);
            Assert.AreEqual("colgado:8", nuevos[0].Clave);
        }

        #endregion

        #region Job

        [TestMethod]
        public async Task VigilarUnaVez_JobColgado_AvisaAInformaticaYAElmahUnaSolaVez()
        {
            var repositorio = A.Fake<IRepositorioVigilanciaHangfire>();
            A.CallTo(() => repositorio.LeerEnCurso()).Returns(new List<JobEnCurso> { Job(146824, 60) });
            A.CallTo(() => repositorio.ContarEncolados()).Returns(10);
            var push = A.Fake<IServicioNotificacionesPush>();
            string grupoPedido = null;
            var elmah = new List<Exception>();
            var vigilante = new VigilanteHangfireJobsService(repositorio, () => push,
                g => { grupoPedido = g; return new List<string> { "Carlos", "NUEVAVISION\\carlos" }; }, () => AHORA, elmah.Add);

            List<AvisoHangfire> primera = await vigilante.VigilarUnaVez();
            List<AvisoHangfire> segunda = await vigilante.VigilarUnaVez();

            Assert.AreEqual(1, primera.Count);
            Assert.AreEqual(0, segunda.Count);
            Assert.AreEqual(Constantes.GruposSeguridad.INFORMATICA, grupoPedido);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == VigilanteHangfireJobsService.TIPO_NOTIFICACION && n.Titulo == "Hangfire: job colgado")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, elmah.Count);
            StringAssert.Contains(elmah[0].Message, "146824");
        }

        [TestMethod]
        public async Task VigilarUnaVez_SinAccesoALasTablas_SoloElmahYNoLanza()
        {
            var repositorio = A.Fake<IRepositorioVigilanciaHangfire>();
            A.CallTo(() => repositorio.LeerEnCurso()).ThrowsAsync(new InvalidOperationException("sin conexión"));
            var push = A.Fake<IServicioNotificacionesPush>();
            var elmah = new List<Exception>();
            var vigilante = new VigilanteHangfireJobsService(repositorio, () => push, g => new List<string> { "Carlos" }, () => AHORA, elmah.Add);

            List<AvisoHangfire> avisos = await vigilante.VigilarUnaVez();

            Assert.AreEqual(0, avisos.Count);
            Assert.AreEqual(1, elmah.Count);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void NombreDesdeInvocationData_FormatoDeHangfire_ClaseYMetodo()
        {
            Assert.AreEqual("SincronizacionJobsService.SincronizarProductos", RepositorioVigilanciaHangfire.NombreDesdeInvocationData(
                "{\"t\":\"NestoAPI.Infraestructure.SincronizacionJobsService, NestoAPI\",\"m\":\"SincronizarProductos\"}"));
            Assert.IsNull(RepositorioVigilanciaHangfire.NombreDesdeInvocationData(null));
        }

        #endregion
    }
}
