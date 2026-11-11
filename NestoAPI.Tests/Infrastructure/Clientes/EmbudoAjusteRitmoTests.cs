using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes.Embudo;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// Embudo de Odoo (Carlos 11/11/26): el job «embudo-ajuste-ritmo» lee la recomendación de Odoo y ajusta solo el tamaño del bloque
    /// (escalones 50 → 75 → 100, tope 100) o apaga el interruptor. Reglas puras y job sin red ni BD.
    /// </summary>
    [TestClass]
    public class EmbudoAjusteRitmoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 20, 5, 30, 0, DateTimeKind.Utc);

        private static RecomendacionRitmoOdoo Rec(string que, int diasDato = 1, int? recomendado = null)
            => new RecomendacionRitmoOdoo { Recomendacion = que, CalculadoEl = AHORA.AddDays(-diasDato), EscalonRecomendado = recomendado, Motivo = "pocos rebotes" };

        // ---------------------------------------------------------------- Reglas (puras)

        [TestMethod]
        public void Subir_ConMasDeSieteDias_UnEscalon()
        {
            DecisionRitmoEmbudo d = ReglasRitmoEmbudo.Decidir(Rec("subir", recomendado: 100), 50, true, AHORA.AddDays(-8), AHORA);

            Assert.AreEqual(AccionRitmoEmbudo.Subir, d.Accion);
            Assert.AreEqual(75, d.TamanoNuevo, "Nunca más de un escalón por vez");
        }

        [TestMethod]
        public void Subir_ConMenosDeSieteDiasDesdeElUltimoCambio_NoSube()
        {
            DecisionRitmoEmbudo d = ReglasRitmoEmbudo.Decidir(Rec("subir"), 50, true, AHORA.AddDays(-6), AHORA);

            Assert.AreEqual(AccionRitmoEmbudo.Nada, d.Accion);
            Assert.AreEqual(50, d.TamanoNuevo);
        }

        [TestMethod]
        public void Subir_SinCambioAnterior_Sube()
        {
            Assert.AreEqual(AccionRitmoEmbudo.Subir, ReglasRitmoEmbudo.Decidir(Rec("subir"), 50, true, null, AHORA).Accion);
        }

        [TestMethod]
        public void Subir_DatoDeOchoDiasOMas_NoSubeYEsNoValido()
        {
            DecisionRitmoEmbudo d = ReglasRitmoEmbudo.Decidir(Rec("subir", diasDato: 8), 50, true, null, AHORA);

            Assert.AreEqual(AccionRitmoEmbudo.DatoNoValido, d.Accion);
            Assert.AreEqual(50, d.TamanoNuevo);
        }

        [TestMethod]
        public void Subir_NuncaPorEncimaDelRecomendadoNiDeCien()
        {
            Assert.AreEqual(AccionRitmoEmbudo.Nada, ReglasRitmoEmbudo.Decidir(Rec("subir", recomendado: 50), 50, true, null, AHORA).Accion);
            Assert.AreEqual(60, ReglasRitmoEmbudo.Decidir(Rec("subir", recomendado: 60), 50, true, null, AHORA).TamanoNuevo);
            Assert.AreEqual(AccionRitmoEmbudo.Nada, ReglasRitmoEmbudo.Decidir(Rec("subir", recomendado: 500), 100, true, null, AHORA).Accion, "Tope 100");
            Assert.AreEqual(100, ReglasRitmoEmbudo.Decidir(Rec("subir", recomendado: 500), 75, true, null, AHORA).TamanoNuevo);
        }

        [TestMethod]
        public void Bajar_EnElActoUnEscalon_YEnCincuentaSeQueda()
        {
            DecisionRitmoEmbudo d = ReglasRitmoEmbudo.Decidir(Rec("bajar"), 100, true, AHORA.AddHours(-1), AHORA);

            Assert.AreEqual(AccionRitmoEmbudo.Bajar, d.Accion, "Aunque haya cambiado hace una hora");
            Assert.AreEqual(75, d.TamanoNuevo);
            Assert.AreEqual(AccionRitmoEmbudo.Nada, ReglasRitmoEmbudo.Decidir(Rec("bajar"), 50, true, null, AHORA).Accion);
        }

        [TestMethod]
        public void Parar_EnElActo_YSiYaEstaApagadoNada()
        {
            Assert.AreEqual(AccionRitmoEmbudo.Parar, ReglasRitmoEmbudo.Decidir(Rec("parar"), 75, true, AHORA.AddHours(-1), AHORA).Accion);
            Assert.AreEqual(AccionRitmoEmbudo.Nada, ReglasRitmoEmbudo.Decidir(Rec("parar"), 75, false, null, AHORA).Accion);
        }

        [TestMethod]
        public void Mantener_Nada_YLoQueNoSeEntiendeEsNoValido()
        {
            Assert.AreEqual(AccionRitmoEmbudo.Nada, ReglasRitmoEmbudo.Decidir(Rec("mantener"), 75, true, null, AHORA).Accion);
            Assert.AreEqual(AccionRitmoEmbudo.DatoNoValido, ReglasRitmoEmbudo.Decidir(Rec("acelerar"), 75, true, null, AHORA).Accion);
            Assert.AreEqual(AccionRitmoEmbudo.DatoNoValido, ReglasRitmoEmbudo.Decidir(new RecomendacionRitmoOdoo { Recomendacion = "subir" }, 75, true, null, AHORA).Accion);
        }

        [TestMethod]
        public void Leer_ConTolerancia_CamposDeMasYDeMenos()
        {
            RecomendacionRitmoOdoo r = RecomendacionRitmoOdoo.Leer(
                "{\"calculadoEl\":\"2026-11-19T22:00:00Z\",\"recomendacion\":\"SUBIR\",\"escalonRecomendado\":75,\"enviados\":1200,\"rebotes\":3,\"nuevo\":{\"x\":1}}");

            Assert.AreEqual("subir", r.Recomendacion);
            Assert.AreEqual(new DateTime(2026, 11, 19, 22, 0, 0, DateTimeKind.Utc), r.CalculadoEl);
            Assert.AreEqual(75, r.EscalonRecomendado);
            Assert.IsNull(r.Motivo);
            StringAssert.Contains(r.TextoCifras(), "enviados 1200");
            Assert.IsNull(RecomendacionRitmoOdoo.Leer("<html>error</html>"));
        }

        // ---------------------------------------------------------------- Job (sin red ni BD)

        private class ParametrosFalsos : IParametrosEmbudoDiario
        {
            public Dictionary<string, string> Valores { get; } = new Dictionary<string, string>();
            public List<string> ConRastro { get; } = new List<string>();
            public string Leer(string clave) => Valores.TryGetValue(clave, out string v) ? v : null;
            public Task Escribir(string clave, string valor, string usuario, string detalle = null, bool dejarRastro = true)
            {
                Valores[clave] = valor;
                if (dejarRastro)
                {
                    ConRastro.Add($"{clave}={valor} | {detalle}");
                }
                return Task.CompletedTask;
            }
        }

        private ParametrosFalsos parametros;
        private IServicioNotificacionesPush notificaciones;
        private Dictionary<string, string> config;
        private (HttpStatusCode, string) respuesta;
        private int peticiones;
        private string claveEnviada;

        [TestInitialize]
        public void Setup()
        {
            parametros = new ParametrosFalsos();
            parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_INTERRUPTOR] = "1";
            parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO] = "50";
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            config = new Dictionary<string, string>
            {
                [EmbudoAjusteRitmoJobsService.CLAVE_URL] = "https://odoo.ejemplo/",
                [EmbudoAjusteRitmoJobsService.CLAVE_SECRETO] = "secreto"
            };
            peticiones = 0;
            Responder("subir", AHORA.AddDays(-1));
        }

        private void Responder(string que, DateTime calculado)
            => respuesta = (HttpStatusCode.OK, $"{{\"calculadoEl\":\"{calculado:o}\",\"recomendacion\":\"{que}\",\"escalonRecomendado\":100,\"enviados\":900,\"motivo\":\"Pocos rebotes\"}}");

        private EmbudoAjusteRitmoJobsService Job()
            => new EmbudoAjusteRitmoJobsService(parametros, c => config.TryGetValue(c, out string v) ? v : null,
                (url, clave) => { peticiones++; claveEnviada = clave; StringAssert.EndsWith(url, EmbudoAjusteRitmoJobsService.RUTA); return Task.FromResult(respuesta); },
                () => new List<string> { "Carlos" }, notificaciones, ex => { });

        private int Avisos() => Fake.GetCalls(notificaciones).Count();

        [TestMethod]
        public async Task Job_SinConfiguracion_NoHaceNadaNiAvisa()
        {
            config.Remove(EmbudoAjusteRitmoJobsService.CLAVE_SECRETO);

            Assert.AreEqual(ResultadoAjusteRitmoEmbudo.SinConfiguracion, await Job().AjustarAsync(AHORA));
            Assert.AreEqual(0, peticiones);
            Assert.AreEqual(0, Avisos());
            Assert.AreEqual("50", parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO]);
        }

        [TestMethod]
        public async Task Job_Subir_CambiaElTamanoConRastroYAvisa()
        {
            Assert.AreEqual(ResultadoAjusteRitmoEmbudo.Cambiado, await Job().AjustarAsync(AHORA));

            Assert.AreEqual("secreto", claveEnviada);
            Assert.AreEqual("75", parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO]);
            Assert.IsNotNull(parametros.Leer(EmbudoAjusteRitmoJobsService.PARAMETRO_FECHA_CAMBIO_TAMANO));
            Assert.AreEqual("subir", parametros.Leer(EmbudoAjusteRitmoJobsService.PARAMETRO_RECOMENDACION));
            string rastro = parametros.ConRastro.Single();
            StringAssert.Contains(rastro, "Pocos rebotes");
            StringAssert.Contains(rastro, "enviados 900");
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Cuerpo.Contains("de 50 a 75")))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Job_Parar_ApagaElInterruptor()
        {
            Responder("parar", AHORA.AddDays(-1));

            Assert.AreEqual(ResultadoAjusteRitmoEmbudo.Cambiado, await Job().AjustarAsync(AHORA));
            Assert.AreEqual("0", parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_INTERRUPTOR]);
            Assert.AreEqual("50", parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO]);
            Assert.AreEqual(1, Avisos());
        }

        [TestMethod]
        public async Task Job_403_NoTocaNadaYAvisaUnaVezAlDia()
        {
            respuesta = (HttpStatusCode.Forbidden, string.Empty);

            Assert.AreEqual(ResultadoAjusteRitmoEmbudo.Fallo, await Job().AjustarAsync(AHORA));
            Assert.AreEqual(ResultadoAjusteRitmoEmbudo.Fallo, await Job().AjustarAsync(AHORA.AddHours(1)));
            Assert.AreEqual("50", parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO]);
            Assert.AreEqual(1, Avisos(), "Una vez al día mientras dure");
            Assert.AreEqual(0, parametros.ConRastro.Count);
        }

        [TestMethod]
        public async Task Job_DatoViejo_NoSubeYAvisa()
        {
            Responder("subir", AHORA.AddDays(-9));

            Assert.AreEqual(ResultadoAjusteRitmoEmbudo.DatoNoValido, await Job().AjustarAsync(AHORA));
            Assert.AreEqual("50", parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO]);
            Assert.AreEqual(1, Avisos());
        }

        [TestMethod]
        public async Task Job_ErrorDeRed_NoTocaNadaYAvisa()
        {
            var job = new EmbudoAjusteRitmoJobsService(parametros, c => config.TryGetValue(c, out string v) ? v : null,
                (url, clave) => throw new System.Net.Http.HttpRequestException("sin red"), () => new List<string> { "Carlos" }, notificaciones, ex => { });

            Assert.AreEqual(ResultadoAjusteRitmoEmbudo.Fallo, await job.AjustarAsync(AHORA));
            Assert.AreEqual("50", parametros.Valores[EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO]);
            Assert.AreEqual(1, Avisos());
        }

        [TestMethod]
        public async Task Configuracion_DevuelveLaUltimaRecomendacionYElUltimoCambio()
        {
            _ = await Job().AjustarAsync(AHORA);
            IRepositorioClientesEmbudo repo = A.Fake<IRepositorioClientesEmbudo>();
            A.CallTo(() => repo.HayTabla()).Returns(false);

            ConfiguracionEmbudoDiarioDTO c = await new ServicioConfiguracionEmbudoDiario(parametros, repo).Leer();

            Assert.AreEqual(75, c.TamanoBloque);
            Assert.AreEqual("subir", c.UltimaRecomendacion);
            Assert.AreEqual("Pocos rebotes", c.UltimaRecomendacionMotivo);
            Assert.AreEqual(AHORA, c.UltimaRecomendacionFecha);
            Assert.AreEqual(AHORA, c.FechaUltimoCambioTamano);
        }
    }
}
