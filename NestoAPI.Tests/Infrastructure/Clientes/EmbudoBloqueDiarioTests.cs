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
        private ParametrosFalsos parametros;

        private class ParametrosFalsos : IParametrosEmbudoDiario
        {
            public Dictionary<string, string> Valores { get; } = new Dictionary<string, string>();
            public List<string> Escritos { get; } = new List<string>();
            public string Leer(string clave) => Valores.TryGetValue(clave, out string v) ? v : null;
            public Task Escribir(string clave, string valor, string usuario, string detalle = null, bool dejarRastro = true)
            {
                Valores[clave] = valor;
                if (dejarRastro)
                {
                    Escritos.Add($"{clave}={valor} por {usuario}");
                }
                return Task.CompletedTask;
            }
        }

        [TestInitialize]
        public void Setup()
        {
            parametros = new ParametrosFalsos();
            parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_INTERRUPTOR] = "1";
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
            => new EmbudoBloqueDiarioJobsService(repo, parametros, () => new List<string> { "Carlos", "NUEVAVISION\\Laura" }, notificaciones,
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
            parametros.Valores.Remove(EmbudoBloqueDiarioJobsService.PARAMETRO_INTERRUPTOR); // sin fila: apagado

            ResultadoBloqueDiarioEmbudo r = await Servicio().LanzarAsync(HOY);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Apagado, r);
            A.CallTo(() => repo.HayTabla()).MustNotHaveHappened();
            NoSeHaLanzado();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Lanzar_MenosDeCincuenta_LanzaConLosQueHayYSinAviso()
        {
            Candidatos(45); // agotada la cola, unos 45 al mes

            ResultadoBloqueDiarioEmbudo r = await Servicio().LanzarAsync(HOY);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.LanzadoIncompleto, r);
            A.CallTo(() => repo.Registrar("NV-2026-11-03", A<IList<string>>.That.Matches(c => c.Count == 45), A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public async Task Lanzar_SinCandidatos_NoLanzaNadaNiAvisa_SoloElRegistro()
        {
            Candidatos(0);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.SinCandidatos, await Servicio().LanzarAsync(HOY));
            NoSeHaLanzado();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            Assert.AreEqual(1, registrados.Count, "Solo queda en ELMAH");
        }

        [TestMethod]
        public async Task Lanzar_SiFallaElLanzamiento_AvisaADireccion()
        {
            A.CallTo(() => repo.Registrar(A<string>._, A<IList<string>>._, A<string>._)).Throws(new TimeoutException("tiempo agotado"));

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Fallo, await Servicio().LanzarAsync(HOY));
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

        [TestMethod]
        public async Task Lanzar_ElInterruptorACeroTambienEsApagado()
        {
            parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_INTERRUPTOR] = "0";

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Apagado, await Servicio().LanzarAsync(HOY));
            NoSeHaLanzado();
        }

        [TestMethod]
        public async Task Lanzar_ConElTamanoDeLaBD_LanzaEseTamano()
        {
            parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO] = "80";
            Candidatos(200);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Lanzado, await Servicio().LanzarAsync(HOY));
            A.CallTo(() => repo.Registrar("NV-2026-11-03", A<IList<string>>.That.Matches(c => c.Count == 80), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Lanzar_TamanoPorEncimaDelMaximo_SeRecortaYSeRegistra()
        {
            parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO] = "9000";
            Candidatos(600);

            Assert.AreEqual(ResultadoBloqueDiarioEmbudo.Lanzado, await Servicio().LanzarAsync(HOY));
            A.CallTo(() => repo.Registrar(A<string>._, A<IList<string>>.That.Matches(c => c.Count == EmbudoBloqueDiarioJobsService.TAMANO_MAXIMO), A<string>._))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, registrados.Count);
        }

        [TestMethod]
        public void TamanoBloque_SinFilaONoValido_Cincuenta()
        {
            Assert.AreEqual(50, EmbudoBloqueDiarioJobsService.TamanoBloque(null));
            Assert.AreEqual(50, EmbudoBloqueDiarioJobsService.TamanoBloque("abc"));
            Assert.AreEqual(50, EmbudoBloqueDiarioJobsService.TamanoBloque("0"));
            Assert.AreEqual(1, EmbudoBloqueDiarioJobsService.TamanoBloque(" 1 "));
            Assert.AreEqual(100, EmbudoBloqueDiarioJobsService.TamanoBloque("101"));
            Assert.AreEqual(100, EmbudoBloqueDiarioJobsService.TamanoBloque("500"));
        }

        [TestMethod]
        public async Task Lanzar_ElLanzamientoAManoSigueTopadoEnCincuenta()
        {
            await Assert.ThrowsExceptionAsync<PeticionEmbudoNoValidaException>(() => new ServicioEmbudoClientes(repo)
                .Republicar(new PeticionRepublicarEmbudo { Bloque = "B", Maximo = 51 }, "Carlos"));
        }

        // ---------------------------------------------------------------- Configuración (GET/PUT api/Clientes/Embudo/Configuracion)

        [TestMethod]
        public async Task Configuracion_Leer_EncendidoTamanoYUltimoBloque()
        {
            parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO] = "70";
            bloques.Add(new BloqueEmbudo { Bloque = RepositorioClientesEmbudoSql.BLOQUE_SIEMBRA, Fecha = HOY.AddHours(9) });

            ConfiguracionEmbudoDiarioDTO c = await new ServicioConfiguracionEmbudoDiario(parametros, repo).Leer();

            Assert.IsTrue(c.Encendido);
            Assert.AreEqual(70, c.TamanoBloque);
            Assert.AreEqual("NV-2026-11-02", c.UltimoBloque, "La siembra no es un bloque");
            Assert.AreEqual(new DateTime(2026, 11, 11, 12, 35, 0), c.FechaUltimoBloque);
        }

        [TestMethod]
        public async Task Configuracion_Cambiar_EscribeLoQueVieneConQuienLoCambia()
        {
            ConfiguracionEmbudoDiarioDTO c = await new ServicioConfiguracionEmbudoDiario(parametros, repo)
                .Cambiar(new CambioConfiguracionEmbudoDiarioDTO { Encendido = false, TamanoBloque = 80 }, "NUEVAVISION\\Carlos");

            Assert.IsFalse(c.Encendido);
            Assert.AreEqual(80, c.TamanoBloque);
            CollectionAssert.AreEqual(new[] { "EmbudoBloqueDiario=0 por NUEVAVISION\\Carlos", "EmbudoTamanoBloque=80 por NUEVAVISION\\Carlos" }, parametros.Escritos);
        }

        [TestMethod]
        public async Task Configuracion_Cambiar_TamanoFueraDeLimitesOVacio_400SinEscribir()
        {
            var servicio = new ServicioConfiguracionEmbudoDiario(parametros, repo);

            await Assert.ThrowsExceptionAsync<PeticionEmbudoNoValidaException>(() => servicio.Cambiar(new CambioConfiguracionEmbudoDiarioDTO { TamanoBloque = 101 }, "Carlos"));
            await Assert.ThrowsExceptionAsync<PeticionEmbudoNoValidaException>(() => servicio.Cambiar(new CambioConfiguracionEmbudoDiarioDTO { TamanoBloque = 0 }, "Carlos"));
            await Assert.ThrowsExceptionAsync<PeticionEmbudoNoValidaException>(() => servicio.Cambiar(new CambioConfiguracionEmbudoDiarioDTO(), "Carlos"));
            Assert.AreEqual(0, parametros.Escritos.Count);
        }
    }
}
