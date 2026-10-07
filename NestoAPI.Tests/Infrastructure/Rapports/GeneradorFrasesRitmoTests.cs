using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rapports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>
    /// NestoAPI#603 (corte 4): la situación del vendedor, el banco de plantillas, la elección sin repetir, las variables y
    /// la validación de la frase de OpenAI. Octubre de 2026 sin festivos: 22 laborables; el 7 es miércoles y el 30, viernes.
    /// </summary>
    [TestClass]
    public class GeneradorFrasesRitmoTests
    {
        private static readonly Func<DateTime, bool> SinFestivos = d => true;

        private static RitmoContactosDTO Ritmo(int hoy, int mes, int semana = 0, int objetivoMes = 310, int maxima = 12, int alta = 85)
        {
            return new RitmoContactosDTO
            {
                ContactosHoy = hoy,
                ContactosSemana = semana,
                ContactosMes = mes,
                ObjetivoMes = objetivoMes,
                PendientesMaxima = maxima,
                PendientesAlta = alta
            };
        }

        private static SituacionRitmo Situacion(RitmoContactosDTO ritmo, DateTime ahora, Dictionary<DateTime, int> dias = null,
            int pedidosHoy = 0, string nombre = "María", Func<DateTime, bool> esLaborable = null)
        {
            return GeneradorFrasesRitmo.CalcularSituacion(ritmo, ahora, esLaborable ?? SinFestivos, dias, pedidosHoy, nombre);
        }

        // ---------------- Situación ----------------

        [TestMethod]
        public void Situacion_ObjetivoHoy_EsElDelPrincipioDelDiaYNoBajaAlLlamar()
        {
            // 310 de objetivo, 70 contactos antes de hoy y 18 laborables del 7 al 30 (incluido): ⌈240 / 18⌉ = 14.
            SituacionRitmo s = Situacion(Ritmo(hoy: 9, mes: 79), new DateTime(2026, 10, 7, 13, 30, 0));

            Assert.AreEqual(14, s.ObjetivoHoy);
            Assert.AreEqual(5, s.Faltan);
        }

        [TestMethod]
        public void Situacion_SinContactosHoy_EsArranqueSegunElDia()
        {
            Assert.AreEqual(SituacionesRitmo.ARRANQUE, Situacion(Ritmo(0, 70), new DateTime(2026, 10, 7, 9, 30, 0)).Clave);
            Assert.AreEqual(SituacionesRitmo.ARRANQUE_LUNES, Situacion(Ritmo(0, 40), new DateTime(2026, 10, 5, 9, 30, 0)).Clave);
            Assert.AreEqual(SituacionesRitmo.ARRANQUE_VIERNES, Situacion(Ritmo(0, 90), new DateTime(2026, 10, 9, 9, 30, 0)).Clave);
        }

        [TestMethod]
        public void Situacion_UltimoDiaLaborableDelMes()
        {
            SituacionRitmo s = Situacion(Ritmo(0, 280), new DateTime(2026, 10, 30, 9, 30, 0));

            Assert.IsTrue(s.UltimoDiaLaborableMes);
            Assert.IsTrue(s.Viernes);
            Assert.AreEqual(SituacionesRitmo.ULTIMO_DIA_MES, s.Clave);
        }

        [TestMethod]
        public void Situacion_NoLaborable_SabadoYFestivo()
        {
            Assert.AreEqual(SituacionesRitmo.NO_LABORABLE, Situacion(Ritmo(0, 100), new DateTime(2026, 10, 10, 10, 0, 0)).Clave);
            SituacionRitmo festivo = Situacion(Ritmo(0, 100), new DateTime(2026, 10, 12, 10, 0, 0), esLaborable: d => d != new DateTime(2026, 10, 12));
            Assert.AreEqual(SituacionesRitmo.NO_LABORABLE, festivo.Clave);
            Assert.AreEqual(0, festivo.ObjetivoHoy);
        }

        [TestMethod]
        public void Situacion_PorDebajo_ProporcionalALaHoraYPorLaTardeApretar()
        {
            // A las 13:30 ha pasado la mitad de la jornada (9 a 18): lo esperado es ⌊14 × 0,5⌋ = 7.
            SituacionRitmo mediodia = Situacion(Ritmo(hoy: 5, mes: 75, semana: 30), new DateTime(2026, 10, 7, 13, 30, 0));
            Assert.AreEqual(AvanceRitmo.PorDebajo, mediodia.EstadoDia);
            Assert.AreEqual(SituacionesRitmo.POR_DEBAJO, mediodia.Clave);
            Assert.IsFalse(mediodia.Tarde);

            SituacionRitmo tarde = Situacion(Ritmo(hoy: 5, mes: 75, semana: 30), new DateTime(2026, 10, 7, 16, 0, 0));
            Assert.IsTrue(tarde.Tarde);
            Assert.AreEqual(SituacionesRitmo.TARDE_APRETAR, tarde.Clave);
        }

        [TestMethod]
        public void Situacion_PorLaMananaTempranoSinLlevarMuchos_NoEstaPorDebajo()
        {
            SituacionRitmo s = Situacion(Ritmo(hoy: 2, mes: 72, semana: 30), new DateTime(2026, 10, 7, 9, 45, 0));

            Assert.AreEqual(AvanceRitmo.EnLinea, s.EstadoDia);
        }

        [TestMethod]
        public void Situacion_EnLinea_DiaSemanaYMes()
        {
            // Mes: 4,5 de 22 laborables consumidos → 63,4 esperados; 66 llevados. Semana: 2,5 de 5 → 35,2; 36 llevados.
            SituacionRitmo s = Situacion(Ritmo(hoy: 8, mes: 66, semana: 36), new DateTime(2026, 10, 7, 13, 30, 0));

            Assert.AreEqual(AvanceRitmo.EnLinea, s.EstadoDia);
            Assert.AreEqual(AvanceRitmo.EnLinea, s.EstadoSemana);
            Assert.AreEqual(AvanceRitmo.EnLinea, s.EstadoMes);
            Assert.AreEqual(SituacionesRitmo.EN_LINEA, s.Clave);
        }

        [TestMethod]
        public void Situacion_PorEncimaDelMes_EsBuenRitmo()
        {
            SituacionRitmo s = Situacion(Ritmo(hoy: 8, mes: 90, semana: 36), new DateTime(2026, 10, 7, 13, 30, 0));

            Assert.AreEqual(AvanceRitmo.PorEncima, s.EstadoMes);
            Assert.AreEqual(SituacionesRitmo.BUEN_RITMO, s.Clave);
        }

        [TestMethod]
        public void Situacion_PorDebajoDeLaSemana()
        {
            SituacionRitmo s = Situacion(Ritmo(hoy: 8, mes: 66, semana: 20), new DateTime(2026, 10, 7, 13, 30, 0));

            Assert.AreEqual(AvanceRitmo.PorDebajo, s.EstadoSemana);
        }

        [TestMethod]
        public void Situacion_PrimerContactoYPrimerPedido()
        {
            SituacionRitmo contacto = Situacion(Ritmo(hoy: 1, mes: 71), new DateTime(2026, 10, 7, 9, 40, 0));
            Assert.IsTrue(contacto.PrimerContactoDelDia);
            Assert.AreEqual(SituacionesRitmo.PRIMER_CONTACTO, contacto.Clave);

            SituacionRitmo pedido = Situacion(Ritmo(hoy: 3, mes: 73), new DateTime(2026, 10, 7, 10, 0, 0), pedidosHoy: 1);
            Assert.IsTrue(pedido.PrimerPedidoDelDia);
            Assert.AreEqual(SituacionesRitmo.PRIMER_PEDIDO, pedido.Clave);
        }

        [TestMethod]
        public void Situacion_ObjetivoDelDiaCumplido()
        {
            var dias = new Dictionary<DateTime, int> { [new DateTime(2026, 10, 6)] = 20 };
            SituacionRitmo s = Situacion(Ritmo(hoy: 14, mes: 84), new DateTime(2026, 10, 7, 12, 0, 0), dias);

            Assert.IsTrue(s.ObjetivoDiaCumplido);
            Assert.IsFalse(s.MejorDiaDelMes);
            Assert.AreEqual(SituacionesRitmo.OBJETIVO_CUMPLIDO, s.Clave);
        }

        [TestMethod]
        public void Situacion_MejorDiaDelMes()
        {
            var dias = new Dictionary<DateTime, int> { [new DateTime(2026, 10, 6)] = 10, [new DateTime(2026, 10, 1)] = 12, [new DateTime(2026, 9, 30)] = 30 };
            SituacionRitmo s = Situacion(Ritmo(hoy: 15, mes: 85), new DateTime(2026, 10, 7, 12, 0, 0), dias);

            Assert.IsTrue(s.MejorDiaDelMes, "Septiembre no cuenta para el mejor día de octubre");
            Assert.AreEqual(SituacionesRitmo.MEJOR_DIA, s.Clave);
        }

        [TestMethod]
        public void Situacion_ObjetivoDelMesCubierto()
        {
            Assert.AreEqual(SituacionesRitmo.MES_CUMPLIDO, Situacion(Ritmo(hoy: 4, mes: 320), new DateTime(2026, 10, 27, 11, 0, 0)).Clave);
        }

        [TestMethod]
        public void Situacion_Racha_DiasLaborablesSeguidosConElObjetivoDiarioMedio()
        {
            // 310 / 22 = 14,1 → 15 al día. El fin de semana no corta la racha; el 1 (10 contactos) sí.
            var dias = new Dictionary<DateTime, int>
            {
                [new DateTime(2026, 10, 6)] = 15,
                [new DateTime(2026, 10, 5)] = 16,
                [new DateTime(2026, 10, 2)] = 15,
                [new DateTime(2026, 10, 1)] = 10,
                [new DateTime(2026, 9, 30)] = 40
            };
            SituacionRitmo s = Situacion(Ritmo(hoy: 3, mes: 100), new DateTime(2026, 10, 7, 10, 0, 0), dias);
            Assert.AreEqual(3, s.Racha);

            SituacionRitmo cumplido = Situacion(Ritmo(hoy: 20, mes: 120), new DateTime(2026, 10, 7, 17, 0, 0), dias);
            Assert.IsTrue(cumplido.ObjetivoDiaCumplido);
            Assert.AreEqual(4, cumplido.Racha, "Hoy, con el objetivo cumplido, suma");
        }

        [TestMethod]
        public void Situacion_Hitos_LunesViernesTarde()
        {
            Assert.IsTrue(Situacion(Ritmo(3, 40), new DateTime(2026, 10, 5, 10, 0, 0)).Lunes);
            Assert.IsTrue(Situacion(Ritmo(3, 90), new DateTime(2026, 10, 9, 10, 0, 0)).Viernes);
            Assert.IsTrue(Situacion(Ritmo(3, 70), new DateTime(2026, 10, 7, 15, 0, 0)).Tarde);
            Assert.IsFalse(Situacion(Ritmo(3, 70), new DateTime(2026, 10, 7, 14, 59, 0)).Tarde);
        }

        [TestMethod]
        public void Situacion_PendientesDeLaListaYNombre()
        {
            SituacionRitmo s = Situacion(Ritmo(3, 70, maxima: 7, alta: 23), new DateTime(2026, 10, 7, 10, 0, 0), nombre: " Lidia ");

            Assert.AreEqual(7, s.Maxima);
            Assert.AreEqual(23, s.Alta);
            Assert.AreEqual("Lidia", s.Nombre);
        }

        // ---------------- Banco ----------------

        [TestMethod]
        public void Banco_TieneAlMenos40PlantillasConClaveUnica()
        {
            IReadOnlyList<PlantillaFraseRitmo> banco = BancoFrasesRitmo.Plantillas;

            Assert.IsTrue(banco.Count >= 40, $"Hay {banco.Count}");
            Assert.AreEqual(banco.Count, banco.Select(p => p.Clave).Distinct().Count());
        }

        [TestMethod]
        public void Banco_TodasLasSituacionesTienenPlantillasYElComodinNoTieneCondiciones()
        {
            string[] situaciones = typeof(SituacionesRitmo).GetFields().Select(f => (string)f.GetValue(null)).ToArray();
            foreach (string situacion in situaciones)
            {
                Assert.IsTrue(BancoFrasesRitmo.Plantillas.Count(p => p.Situacion == situacion) >= 3, situacion);
            }
            Assert.IsTrue(BancoFrasesRitmo.Plantillas.Where(p => p.Situacion == SituacionesRitmo.GENERAL).All(p => p.Condicion == null));
        }

        [TestMethod]
        public void Banco_SoloUsaVariablesConocidasYCabeEn160ConNumerosGrandes()
        {
            var s = new SituacionRitmo
            {
                Hoy = 120, ObjetivoHoy = 140, Faltan = 120, Semana = 420, Mes = 1790, ObjetivoMes = 3100,
                Maxima = 120, Alta = 850, Racha = 12, Nombre = "Inmaculada"
            };
            foreach (PlantillaFraseRitmo plantilla in BancoFrasesRitmo.Plantillas)
            {
                foreach (Match m in Regex.Matches(plantilla.Texto, @"\{(\w+)(?::[^{}]+)?\}"))
                {
                    Assert.IsTrue(GeneradorFrasesRitmo.Variables.Contains(m.Groups[1].Value), $"{plantilla.Clave}: {m.Value}");
                }
                string frase = GeneradorFrasesRitmo.Sustituir(plantilla.Texto, s);
                Assert.IsFalse(frase.Contains("{"), plantilla.Clave);
                Assert.IsTrue(frase.Length <= GeneradorFrasesRitmo.LONGITUD_MAXIMA, $"{plantilla.Clave} ({frase.Length}): {frase}");
                Assert.IsFalse(frase.Contains("Carlos"), plantilla.Clave);
                if (plantilla.Texto.Contains("{nombre}"))
                {
                    Assert.IsNotNull(plantilla.Condicion, $"{plantilla.Clave} usa el nombre y tiene que exigirlo");
                    Assert.IsFalse(plantilla.Condicion(new SituacionRitmo { Maxima = 5, Alta = 5, Faltan = 5, ObjetivoHoy = 5, Racha = 5, Semana = 5 }),
                        $"{plantilla.Clave} sin nombre no debería valer");
                }
            }
        }

        // ---------------- Elección ----------------

        [TestMethod]
        public void Elegir_NoRepiteLasRecientes()
        {
            SituacionRitmo s = Situacion(Ritmo(hoy: 5, mes: 75, semana: 30), new DateTime(2026, 10, 7, 13, 30, 0));
            List<PlantillaFraseRitmo> candidatas = GeneradorFrasesRitmo.Candidatas(s);
            Assert.IsTrue(candidatas.Count >= 3);
            var recientes = candidatas.Skip(1).Select(p => p.Clave).ToList();

            PlantillaFraseRitmo elegida = GeneradorFrasesRitmo.Elegir(s, recientes, "MPP", new DateTime(2026, 10, 7));

            Assert.AreEqual(candidatas[0].Clave, elegida.Clave);
        }

        [TestMethod]
        public void Elegir_SiTodasSonRecientes_LaQueLlevaMasSinSalir()
        {
            SituacionRitmo s = Situacion(Ritmo(hoy: 5, mes: 75, semana: 30), new DateTime(2026, 10, 7, 13, 30, 0));
            List<string> recientes = GeneradorFrasesRitmo.Candidatas(s).Select(p => p.Clave).ToList();

            PlantillaFraseRitmo elegida = GeneradorFrasesRitmo.Elegir(s, recientes, "MPP", new DateTime(2026, 10, 7));

            Assert.AreEqual(recientes.Last(), elegida.Clave);
        }

        [TestMethod]
        public void Elegir_EsEstableParaElMismoVendedorDiaYSituacion()
        {
            SituacionRitmo s = Situacion(Ritmo(hoy: 0, mes: 70), new DateTime(2026, 10, 7, 9, 30, 0));

            string una = GeneradorFrasesRitmo.Elegir(s, new List<string>(), "MPP", new DateTime(2026, 10, 7)).Clave;
            string otra = GeneradorFrasesRitmo.Elegir(s, new List<string>(), "MPP", new DateTime(2026, 10, 7)).Clave;

            Assert.AreEqual(una, otra);
        }

        [TestMethod]
        public void Elegir_SoloPlantillasCuyaCondicionSeCumple()
        {
            // Sin nombre, sin Máxima, sin Alta y sin racha: en el arranque solo valen a3 y a5.
            SituacionRitmo s = Situacion(Ritmo(hoy: 0, mes: 70, semana: 30, maxima: 0, alta: 0), new DateTime(2026, 10, 7, 9, 30, 0), nombre: null);

            CollectionAssert.AreEquivalent(new[] { "a3", "a5" }, GeneradorFrasesRitmo.Candidatas(s).Select(p => p.Clave).ToList());
        }

        [TestMethod]
        public void Elegir_SiNoValeNingunaDelGrupo_UsaElComodin()
        {
            var s = new SituacionRitmo { Clave = SituacionesRitmo.PRIMER_PEDIDO };

            Assert.IsTrue(GeneradorFrasesRitmo.Candidatas(s).Any(p => p.Situacion == SituacionesRitmo.PRIMER_PEDIDO), "pp2 vale siempre");
            s.Clave = "NoExiste";
            Assert.IsTrue(GeneradorFrasesRitmo.Candidatas(s).All(p => p.Situacion == SituacionesRitmo.GENERAL));
        }

        // ---------------- Variables ----------------

        [TestMethod]
        public void Sustituir_VariablesYPlurales()
        {
            var s = new SituacionRitmo { Hoy = 1, ObjetivoHoy = 14, Faltan = 13, Racha = 2, Mes = 79, ObjetivoMes = 310, Nombre = "Paloma", Maxima = 1 };

            Assert.AreEqual("Vamos a por ello, Paloma: 13 contactos y el día está hecho.",
                GeneradorFrasesRitmo.Sustituir("Vamos a por ello, {nombre}: {faltan:contacto} y el día está hecho.", s));
            Assert.AreEqual("1 contacto, 2 días, 1 cliente, 79 de 310.",
                GeneradorFrasesRitmo.Sustituir("{hoy:contacto}, {racha:día}, {maxima:cliente}, {mes} de {objetivoMes}.", s));
        }

        [TestMethod]
        public void Sustituir_VariableDesconocida_SeQuedaTalCual()
        {
            Assert.AreEqual("Hola {jefe}", GeneradorFrasesRitmo.Sustituir("Hola {jefe}", new SituacionRitmo()));
        }

        [TestMethod]
        public void NombreDePila_PrimeraPalabraConMayusculaInicial()
        {
            Assert.AreEqual("María", GeneradorFrasesRitmo.NombreDePila("María José del Pozo Poveda"));
            Assert.AreEqual("Iñaki", GeneradorFrasesRitmo.NombreDePila("IÑAKI MARTÍNEZ"));
            Assert.IsNull(GeneradorFrasesRitmo.NombreDePila("  "));
        }

        // ---------------- OpenAI ----------------

        [TestMethod]
        public void ValidarFraseOpenAI_LimpiaComillasYEspacios()
        {
            var s = new SituacionRitmo { Faltan = 3, Nombre = "Javier" };

            Assert.AreEqual("Te quedan {faltan:contacto}, {nombre}. ¡A por ellos!",
                GeneradorFrasesRitmo.ValidarFraseOpenAI("\"Te quedan {faltan:contacto},\n {nombre}. ¡A por ellos!\"", s, null));
        }

        [TestMethod]
        public void ValidarFraseOpenAI_RechazaLoQueNoVale()
        {
            var s = new SituacionRitmo { Faltan = 3 };

            Assert.IsNull(GeneradorFrasesRitmo.ValidarFraseOpenAI("", s, null), "vacía");
            Assert.IsNull(GeneradorFrasesRitmo.ValidarFraseOpenAI("Te quedan 3 contactos", s, null), "números escritos a mano");
            Assert.IsNull(GeneradorFrasesRitmo.ValidarFraseOpenAI("Te quedan {pendientes}", s, null), "variable inventada");
            Assert.IsNull(GeneradorFrasesRitmo.ValidarFraseOpenAI("Ánimo, {nombre}", s, null), "nombre sin nombre");
            Assert.IsNull(GeneradorFrasesRitmo.ValidarFraseOpenAI(new string('a', 161), s, null), "larga");
            Assert.IsNull(GeneradorFrasesRitmo.ValidarFraseOpenAI("¡A por el día!", s, new[] { "¡a por el día!" }), "repetida");
        }

        [TestMethod]
        public void MensajeOpenAI_LlevaLosNumerosLasVariablesYLasRecientes()
        {
            SituacionRitmo s = Situacion(Ritmo(hoy: 5, mes: 75, semana: 30), new DateTime(2026, 10, 7, 16, 0, 0), nombre: "Lidia");

            string mensaje = GeneradorFrasesRitmo.MensajeOpenAI(s, new[] { "Frase de ayer" });

            StringAssert.Contains(mensaje, "{faltan} contactos que faltan hoy: 9");
            StringAssert.Contains(mensaje, "{maxima} clientes de prioridad Máxima esperando: 12");
            StringAssert.Contains(mensaje, "{nombre} nombre del comercial: Lidia");
            StringAssert.Contains(mensaje, "es por la tarde");
            StringAssert.Contains(mensaje, "- Frase de ayer");
            StringAssert.Contains(GeneradorFrasesRitmo.PROMPT_SISTEMA_OPENAI, "160");
        }
    }
}
