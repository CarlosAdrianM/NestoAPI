using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Infraestructure.Rapports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>NestoAPI#603 (corte 4): memoria de frases, OpenAI una vez al día y caída a las plantillas.</summary>
    [TestClass]
    public class ServicioFrasesRitmoTests
    {
        private static readonly DateTime MANANA = new DateTime(2026, 10, 7, 9, 30, 0);
        private static readonly Func<DateTime, bool> SinFestivos = d => true;

        private IRepositorioFrasesRitmo repositorio;
        private IServicioOpenAI openAI;
        private List<FraseRitmoGuardada> guardadas;
        private bool hayClave;

        [TestInitialize]
        public void Preparar()
        {
            guardadas = new List<FraseRitmoGuardada>();
            hayClave = false;
            repositorio = A.Fake<IRepositorioFrasesRitmo>();
            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<DateTime>._))
                .Returns(Task.FromResult(new DatosFrasesRitmo { Descripcion = "María José del Pozo" }));
            A.CallTo(() => repositorio.LeerUltimas(A<string>._, A<int>._))
                .ReturnsLazily((string v, int n) => Task.FromResult(guardadas.OrderByDescending(f => f.Fecha).Take(n).ToList()));
            A.CallTo(() => repositorio.Guardar(A<FraseRitmoGuardada>._)).Invokes((FraseRitmoGuardada f) => guardadas.Add(f)).Returns(Task.CompletedTask);
            openAI = A.Fake<IServicioOpenAI>();
        }

        private ServicioFrasesRitmo Servicio() => new ServicioFrasesRitmo(repositorio, () => hayClave ? openAI : null);

        private static RitmoContactosDTO Ritmo(int hoy, int mes) => new RitmoContactosDTO
        {
            ContactosHoy = hoy,
            ContactosMes = mes,
            ContactosSemana = 30,
            ObjetivoMes = 310,
            PendientesMaxima = 12,
            PendientesAlta = 85,
            Frase = "Plantilla del corte 1"
        };

        private void OpenAIResponde(string frase)
        {
            hayClave = true;
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).Returns(Task.FromResult(frase));
        }

        [TestMethod]
        public async Task SinOpenAI_PlantillaDelBancoConLosNumerosYQuedaGuardada()
        {
            string frase = await Servicio().Generar("MPP", Ritmo(0, 70), MANANA, SinFestivos);

            Assert.AreEqual(1, guardadas.Count);
            FraseRitmoGuardada guardada = guardadas[0];
            Assert.AreEqual(SituacionesRitmo.ARRANQUE, guardada.Situacion);
            PlantillaFraseRitmo plantilla = BancoFrasesRitmo.Plantillas.Single(p => p.Clave == guardada.Plantilla);
            Assert.AreEqual(plantilla.Texto, guardada.Texto, "Se guarda con las variables, para sustituirlas al día");
            Assert.IsFalse(frase.Contains("{"));
            Assert.AreNotEqual("Plantilla del corte 1", frase);
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MismaSituacionElMismoDia_MismaPlantillaConLosNumerosAlDia()
        {
            guardadas.Add(new FraseRitmoGuardada { Vendedor = "MPP", Fecha = MANANA.AddMinutes(-20), Situacion = SituacionesRitmo.POR_DEBAJO, Plantilla = "d6", Texto = "Llevas {hoy}; faltan {faltan:contacto}." });

            string frase = await Servicio().Generar("MPP", Ritmo(hoy: 5, mes: 75), new DateTime(2026, 10, 7, 13, 30, 0), SinFestivos);

            Assert.AreEqual("Llevas 5; faltan 9 contactos.", frase);
            Assert.AreEqual(1, guardadas.Count, "No guarda otra");
        }

        [TestMethod]
        public async Task CambiaLaSituacion_OtraPlantillaSinRepetirLasDiezUltimas()
        {
            // Las del grupo «por debajo» salvo d3 se han usado hace poco (días anteriores).
            string[] usadas = { "d1", "d2", "d4", "d5", "d6" };
            for (int i = 0; i < usadas.Length; i++)
            {
                guardadas.Add(new FraseRitmoGuardada { Vendedor = "MPP", Fecha = MANANA.AddDays(-1 - i), Situacion = SituacionesRitmo.POR_DEBAJO, Plantilla = usadas[i], Texto = "x" });
            }

            _ = await Servicio().Generar("MPP", Ritmo(hoy: 5, mes: 75), new DateTime(2026, 10, 7, 13, 30, 0), SinFestivos);

            Assert.AreEqual("d3", guardadas.OrderByDescending(f => f.Fecha).First().Plantilla);
        }

        [TestMethod]
        public async Task ConOpenAI_LaPrimeraDelDiaEsSuyaYSeReutilizaSinVolverALlamar()
        {
            OpenAIResponde("Hoy tocan {objetivoHoy:contacto}, {nombre}: empieza por arriba.");

            string primera = await Servicio().Generar("MPP", Ritmo(0, 70), MANANA, SinFestivos);
            string segunda = await Servicio().Generar("MPP", Ritmo(0, 70), MANANA.AddMinutes(10), SinFestivos);

            Assert.AreEqual("Hoy tocan 14 contactos, María: empieza por arriba.", primera);
            Assert.AreEqual(primera, segunda);
            Assert.AreEqual(1, guardadas.Count);
            Assert.AreEqual(GeneradorFrasesRitmo.PLANTILLA_OPENAI, guardadas[0].Plantilla);
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ConOpenAI_ElPromptLlevaLasFrasesRecientes()
        {
            guardadas.Add(new FraseRitmoGuardada { Vendedor = "MPP", Fecha = MANANA.AddDays(-1), Situacion = SituacionesRitmo.ARRANQUE, Plantilla = "a3", Texto = "Frase de ayer {hoy}" });
            OpenAIResponde("¡A por el día!");

            _ = await Servicio().Generar("MPP", Ritmo(0, 70), MANANA, SinFestivos);

            A.CallTo(() => openAI.GenerarContenidoAsync(GeneradorFrasesRitmo.PROMPT_SISTEMA_OPENAI,
                A<string>.That.Contains("- Frase de ayer {hoy}"), A<int>._, A<double>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task OpenAIYaUsadoHoy_SiCambiaLaSituacionVaPlantillaSinLlamar()
        {
            guardadas.Add(new FraseRitmoGuardada { Vendedor = "MPP", Fecha = MANANA, Situacion = SituacionesRitmo.ARRANQUE, Plantilla = GeneradorFrasesRitmo.PLANTILLA_OPENAI, Texto = "De la IA" });
            OpenAIResponde("Otra de la IA");

            _ = await Servicio().Generar("MPP", Ritmo(hoy: 1, mes: 71), MANANA.AddMinutes(30), SinFestivos);

            FraseRitmoGuardada nueva = guardadas.OrderByDescending(f => f.Fecha).First();
            Assert.AreEqual(SituacionesRitmo.PRIMER_CONTACTO, nueva.Situacion);
            Assert.AreNotEqual(GeneradorFrasesRitmo.PLANTILLA_OPENAI, nueva.Plantilla);
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task OpenAIFalla_Plantilla()
        {
            hayClave = true;
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).Throws(new Exception("timeout"));

            string frase = await Servicio().Generar("MPP", Ritmo(0, 70), MANANA, SinFestivos);

            Assert.IsFalse(string.IsNullOrWhiteSpace(frase));
            Assert.AreNotEqual(GeneradorFrasesRitmo.PLANTILLA_OPENAI, guardadas.Single().Plantilla);
        }

        [TestMethod]
        public async Task OpenAIDevuelveAlgoQueNoVale_Plantilla()
        {
            OpenAIResponde("Te quedan 14 contactos");

            _ = await Servicio().Generar("MPP", Ritmo(0, 70), MANANA, SinFestivos);

            Assert.AreNotEqual(GeneradorFrasesRitmo.PLANTILLA_OPENAI, guardadas.Single().Plantilla);
        }

        [TestMethod]
        public async Task SinTablaFrasesRitmo_PlantillaSinGuardarNiLlamarAOpenAI()
        {
            A.CallTo(() => repositorio.LeerUltimas(A<string>._, A<int>._)).Throws(new Exception("Invalid object name 'dbo.FrasesRitmo'"));
            OpenAIResponde("Frase de la IA");

            string frase = await Servicio().Generar("MPP", Ritmo(0, 70), MANANA, SinFestivos);

            Assert.IsFalse(string.IsNullOrWhiteSpace(frase));
            A.CallTo(() => repositorio.Guardar(A<FraseRitmoGuardada>._)).MustNotHaveHappened();
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiFallaTodo_LaFraseDelCorte1()
        {
            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<DateTime>._)).Throws(new Exception("BD caída"));

            Assert.AreEqual("Plantilla del corte 1", await Servicio().Generar("MPP", Ritmo(0, 70), MANANA, SinFestivos));
        }
    }
}
