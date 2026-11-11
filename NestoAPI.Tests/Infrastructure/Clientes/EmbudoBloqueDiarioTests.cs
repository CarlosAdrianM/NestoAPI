using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes.Embudo;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// Embudo de Odoo (Carlos 11/11/26): el job «embudo-bloque-diario» lanza cada día a las 7:00 el siguiente bloque de 50 por el mismo
    /// camino que el lanzamiento a mano, con nombre correlativo, sin repetir en el día, con interruptor en BD y avisando a Dirección
    /// si no se puede.
    /// </summary>
    [TestClass]
    public class EmbudoBloqueDiarioTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 12);

        private IRepositorioClientesEmbudo repo;
        private IServicioNotificacionesPush notificaciones;
        private List<Exception> registrados;
        private List<BloqueEmbudo> bloques;
        private bool encendido;

        [TestInitialize]
        public void Setup()
        {
            encendido = true;
            registrados = new List<Exception>();
            bloques = new List<BloqueEmbudo>
            {
                new BloqueEmbudo { Bloque = "NV-2026-11-01", Fecha = new DateTime(2026, 11, 10, 17, 10, 0) },
                new BloqueEmbudo { Bloque = "NV-2026-11-02", Fecha = new DateTime(2026, 11, 11, 12, 35, 0) }
            };
            repo = A.Fake<IRepositorioClientesEmbudo>();
            A.CallTo(() => repo.HayTabla()).Returns(true);
            A.CallTo(() => repo.LeerBloquesDesde(A<DateTime>._)).ReturnsLazily(() => Task.FromResult(bloques));
            Candidatos(60);
            A.CallTo(() => repo.Registrar(A<string>._, A<IList<string>>._, A<string>._))
                .ReturnsLazily((string b, IList<string> clientes, string u) => clientes.Count);
            notificaciones = A.Fake<IServicioNotificacionesPush>();
        }

        private void Candidatos(int cuantos)
            => A.CallTo(() => repo.LeerCandidatos(A<string>._, A<IList<short>>._, A<bool>._))
                .Returns(Enumerable.Range(1, cuantos).Select(i => new CandidatoEmbudo { Cliente = (40000 + i).ToString() }).ToList());

        private EmbudoBloqueDiarioJobsService Servicio()
            => new EmbudoBloqueDiarioJobsService(repo, () => encendido, () => new List<string> { "Carlos", "NUEVAVISION\\Laura" }, notificaciones,
                ex => registrados.Add(ex));

        private void NoSeHaLanzado()
            => A.CallTo(() => repo.Registrar(A<string>._, A<IList<string>>._, A<string>._)).MustNotHaveHappened();

        private void SeHaAvisadoADireccion()
        {
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == EmbudoBloqueDiarioJobsService.TIPO_NOTIFICACION))).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Laura", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, registrados.Count, "Y queda en ELMAH");
        }

        [TestMethod]
        public void SiguienteBloque_CorrelativoEnElMes()
        {
            Assert.AreEqual("NV-2026-11-03", EmbudoBloqueDiarioJobsService.SiguienteBloque(HOY, new[] { "NV-2026-11-01", "NV-2026-11-02 ", "SIEMBRA" }));
            Assert.AreEqual("NV-2026-11-11", EmbudoBloqueDiarioJobsService.SiguienteBloque(HOY, new[] { "NV-2026-11-10", "NV-2026-11-02" }));
        }

        [TestMethod]
        public void SiguienteBloque_CambioDeMes_EmpiezaEn01()
        {
            Assert.AreEqual("NV-2026-12-01", EmbudoBloqueDiarioJobsService.SiguienteBloque(new DateTime(2026, 12, 1), new[] { "NV-2026-11-30" }));
            Assert.AreEqual("NV-2026-12-01", EmbudoBloqueDiarioJobsService.SiguienteBloque(new DateTime(2026, 12, 1), null));
        }

        [TestMethod]
        public async Task Lanzar_ConCincuentaOMas_LanzaElSiguienteBloqueDeCincuentaPorElMismoCamino()
        {
            ResultadoBloqueDiarioEmbudo r = await Servicio().LanzarAsync(HOY);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Lanzado, r);
            A.CallTo(() => repo.Registrar("NV-2026-11-03", A<IList<string>>.That.Matches(c => c.Count == 50), A<string>._)).MustHaveHappenedOnceExactly();
            // Las reglas del lanzamiento a mano: vendedor NV, los estados de siempre, solo con correo
            A.CallTo(() => repo.LeerCandidatos("NV", A<IList<short>>.That.Matches(e => e.SequenceEqual(new short[] { 0, 7, 9, 93, 11, 22 })), true))
                .MustHaveHappened();
            A.CallTo(() => repo.LeerBloquesDesde(new DateTime(2026, 11, 1))).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Lanzar_SiHoyYaHayUnBloque_NoLanzaOtro()
        {
            bloques.Add(new BloqueEmbudo { Bloque = "NV-2026-11-03", Fecha = HOY.AddHours(6) }); // lanzado a mano esta mañana

            ResultadoBloqueDiarioEmbudo r = await Servicio().LanzarAsync(HOY);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.YaLanzadoHoy, r);
            NoSeHaLanzado();
            A.CallTo(() => repo.LeerCandidatos(A<string>._, A<IList<short>>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Lanzar_LaSiembraDeHoyNoCuentaComoBloque()
        {
            bloques.Add(new BloqueEmbudo { Bloque = RepositorioClientesEmbudoSql.BLOQUE_SIEMBRA, Fecha = HOY.AddHours(1) });

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Lanzado, await Servicio().LanzarAsync(HOY));
        }

        [TestMethod]
        public async Task Lanzar_ConElInterruptorApagado_NoHaceNada()
        {
            encendido = false;

            ResultadoBloqueDiarioEmbudo r = await Servicio().LanzarAsync(HOY);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Apagado, r);
            A.CallTo(() => repo.HayTabla()).MustNotHaveHappened();
            NoSeHaLanzado();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Lanzar_BloqueIncompleto_NoLanzaNadaYAvisaADireccion()
        {
            Candidatos(49);

            ResultadoBloqueDiarioEmbudo r = await Servicio().LanzarAsync(HOY);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Incompleto, r);
            NoSeHaLanzado();
            SeHaAvisadoADireccion();
            StringAssert.Contains(registrados.Single().Message, "solo quedan 49");
        }

        [TestMethod]
        public async Task Lanzar_SinCandidatos_NoLanzaNadaYAvisa()
        {
            Candidatos(0);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Incompleto, await Servicio().LanzarAsync(HOY));
            NoSeHaLanzado();
            SeHaAvisadoADireccion();
        }

        [TestMethod]
        public async Task Lanzar_SiFallaLaConsulta_NoLanzaYAvisaSinLanzarExcepcion()
        {
            A.CallTo(() => repo.LeerCandidatos(A<string>._, A<IList<short>>._, A<bool>._)).Throws(new TimeoutException("tiempo agotado"));

            ResultadoBloqueDiarioEmbudo r = await Servicio().LanzarAsync(HOY);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Fallo, r);
            NoSeHaLanzado();
            SeHaAvisadoADireccion();
        }

        [TestMethod]
        public async Task Lanzar_SinTabla_NoLanzaYAvisa()
        {
            A.CallTo(() => repo.HayTabla()).Returns(false);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Fallo, await Servicio().LanzarAsync(HOY));
            NoSeHaLanzado();
            SeHaAvisadoADireccion();
        }
    }
}
