using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Tablero;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914, tablero: LA REGLA de la nota semanal, la tendencia, la propuesta de umbrales y las semanas.</summary>
    [TestClass]
    public class ReglaNotaKpiTests
    {
        private static UmbralesKpi Cortes(decimal a, decimal b, decimal c, DateTime? desde = null) => new UmbralesKpi
        {
            Kpi = "PedidoPerfecto",
            TipoDueno = TipoDuenoKpi.Vendedor,
            CortePoorFair = a,
            CorteFairGood = b,
            CorteGoodFantastic = c,
            VigenteDesde = desde ?? ReglaNotaKpi.DESDE_SIEMPRE
        };

        [DataTestMethod]
        [DataRow(59.9, "POOR")]
        [DataRow(60.0, "FAIR")]
        [DataRow(74.9, "FAIR")]
        [DataRow(75.0, "GOOD")]
        [DataRow(89.9, "GOOD")]
        [DataRow(90.0, "FANTASTIC")]
        [DataRow(100.0, "FANTASTIC")]
        public void Nota_MasEsMejor_CadaCorteEmpiezaLaNotaDeArriba(double valor, string nota)
        {
            Assert.AreEqual(nota, ReglaNotaKpi.Nota((decimal)valor, Cortes(60, 75, 90), SentidoKpi.MasEsMejor).ToString());
        }

        [DataTestMethod]
        [DataRow(500.01, "POOR")]
        [DataRow(500.0, "FAIR")]
        [DataRow(200.0, "GOOD")]
        [DataRow(50.0, "FANTASTIC")]
        [DataRow(0.0, "FANTASTIC")]
        public void Nota_MenosEsMejor_ConCortesDeMayorAMenor(double valor, string nota)
        {
            Assert.AreEqual(nota, ReglaNotaKpi.Nota((decimal)valor, Cortes(500, 200, 50), SentidoKpi.MenosEsMejor).ToString());
        }

        [TestMethod]
        public void Nota_SinUmbralesOSinValor_SinNota()
        {
            Assert.IsNull(ReglaNotaKpi.Nota(80, null, SentidoKpi.MasEsMejor));
            Assert.IsNull(ReglaNotaKpi.Nota(null, Cortes(60, 75, 90), SentidoKpi.MasEsMejor));
        }

        [TestMethod]
        public void Vigente_UnCambioNoReescribeLasSemanasPasadas()
        {
            var umbrales = new List<UmbralesKpi>
            {
                Cortes(60, 75, 90),
                Cortes(70, 80, 95, new DateTime(2026, 11, 23))
            };

            UmbralesKpi antes = ReglaNotaKpi.Vigente(umbrales, "PedidoPerfecto", TipoDuenoKpi.Vendedor, new DateTime(2026, 11, 16));
            UmbralesKpi despues = ReglaNotaKpi.Vigente(umbrales, "pedidoperfecto", TipoDuenoKpi.Vendedor, new DateTime(2026, 11, 23));

            Assert.AreEqual(60, antes.CortePoorFair);
            Assert.AreEqual(70, despues.CortePoorFair);
            Assert.AreEqual("FAIR", ReglaNotaKpi.Nota(65, antes, SentidoKpi.MasEsMejor).ToString());
            Assert.AreEqual("POOR", ReglaNotaKpi.Nota(65, despues, SentidoKpi.MasEsMejor).ToString());
        }

        [TestMethod]
        public void Vigente_OtroTipoDeDuenoOAntesDelPrimero_Ninguno()
        {
            var umbrales = new List<UmbralesKpi> { Cortes(60, 75, 90, new DateTime(2026, 11, 23)) };

            Assert.IsNull(ReglaNotaKpi.Vigente(umbrales, "PedidoPerfecto", TipoDuenoKpi.Empresa, new DateTime(2026, 11, 30)));
            Assert.IsNull(ReglaNotaKpi.Vigente(umbrales, "PedidoPerfecto", TipoDuenoKpi.Vendedor, new DateTime(2026, 11, 16)));
        }

        [TestMethod]
        public void Tendencia_ContraLaMediaDeLasCuatroAnteriores()
        {
            // media de 80, 80, 80, 80 (la quinta, 10, no cuenta) = 80
            var anteriores = new decimal?[] { 80, 80, 80, 80, 10 };

            Assert.AreEqual(TendenciaKpi.Sube, ReglaNotaKpi.Tendencia(85, anteriores));
            Assert.AreEqual(TendenciaKpi.Baja, ReglaNotaKpi.Tendencia(75, anteriores));
            Assert.AreEqual(TendenciaKpi.Igual, ReglaNotaKpi.Tendencia(81, anteriores)); // dentro del 2 %
        }

        [TestMethod]
        public void Tendencia_SinAnterioresOSinValor_Null()
        {
            Assert.IsNull(ReglaNotaKpi.Tendencia(80, new decimal?[0]));
            Assert.IsNull(ReglaNotaKpi.Tendencia(80, new decimal?[] { null, null }));
            Assert.IsNull(ReglaNotaKpi.Tendencia(null, new decimal?[] { 80 }));
        }

        [TestMethod]
        public void Tendencia_MediaCero_IgualSoloSiSigueEnCero()
        {
            Assert.AreEqual(TendenciaKpi.Igual, ReglaNotaKpi.Tendencia(0, new decimal?[] { 0, 0 }));
            Assert.AreEqual(TendenciaKpi.Sube, ReglaNotaKpi.Tendencia(12, new decimal?[] { 0, 0 }));
        }

        [TestMethod]
        public void Mejora_DependeDelSentido()
        {
            Assert.AreEqual(true, ReglaNotaKpi.Mejora(TendenciaKpi.Sube, SentidoKpi.MasEsMejor));
            Assert.AreEqual(false, ReglaNotaKpi.Mejora(TendenciaKpi.Sube, SentidoKpi.MenosEsMejor));
            Assert.AreEqual(true, ReglaNotaKpi.Mejora(TendenciaKpi.Baja, SentidoKpi.MenosEsMejor));
            Assert.IsNull(ReglaNotaKpi.Mejora(TendenciaKpi.Igual, SentidoKpi.MasEsMejor));
        }

        [TestMethod]
        public void Proponer_MasEsMejor_Percentiles25_50_75Redondeados()
        {
            // 1..100 → P25 = 25,75; P50 = 50,5; P75 = 75,25
            PropuestaUmbralesKpi p = ReglaNotaKpi.Proponer(Enumerable.Range(1, 100).Select(i => (decimal)i), SentidoKpi.MasEsMejor);

            Assert.AreEqual(26, p.CortePoorFair);
            Assert.AreEqual(51, p.CorteFairGood);
            Assert.AreEqual(75, p.CorteGoodFantastic);
        }

        [TestMethod]
        public void Proponer_MenosEsMejor_AlReves()
        {
            PropuestaUmbralesKpi p = ReglaNotaKpi.Proponer(Enumerable.Range(1, 100).Select(i => (decimal)i), SentidoKpi.MenosEsMejor);

            Assert.AreEqual(75, p.CortePoorFair);
            Assert.AreEqual(51, p.CorteFairGood);
            Assert.AreEqual(26, p.CorteGoodFantastic);
            Assert.IsNull(ReglaNotaKpi.ValidarCortes(p.CortePoorFair, p.CorteFairGood, p.CorteGoodFantastic, SentidoKpi.MenosEsMejor));
        }

        [TestMethod]
        public void Proponer_ConPocasSemanas_Null()
        {
            Assert.IsNull(ReglaNotaKpi.Proponer(new decimal[] { 1, 2, 3, 4, 5, 6, 7 }, SentidoKpi.MasEsMejor));
        }

        [DataTestMethod]
        [DataRow(92.37, 92.0)]
        [DataRow(5.27, 5.3)]
        [DataRow(0.374, 0.37)]
        [DataRow(1234.5, 1200.0)]
        [DataRow(355.0, 360.0)]
        [DataRow(-1234.5, -1200.0)]
        public void Legible_NumerosQueSeLeenBien(double valor, double esperado)
        {
            Assert.AreEqual((decimal)esperado, ReglaNotaKpi.Legible((decimal)valor));
        }

        [TestMethod]
        public void ValidarCortes_FueraDeOrden_ErrorDeUsted()
        {
            StringAssert.Contains(ReglaNotaKpi.ValidarCortes(90, 75, 60, SentidoKpi.MasEsMejor), "de menor a mayor");
            StringAssert.Contains(ReglaNotaKpi.ValidarCortes(50, 200, 500, SentidoKpi.MenosEsMejor), "de mayor a menor");
            Assert.IsNull(ReglaNotaKpi.ValidarCortes(60, 60, 90, SentidoKpi.MasEsMejor));
        }

        [TestMethod]
        public void Semana_LunesCierreYUltimaCerrada()
        {
            DateTime viernes = new DateTime(2026, 11, 27);

            Assert.AreEqual(new DateTime(2026, 11, 23), SemanaKpi.LunesDe(viernes));
            Assert.AreEqual(new DateTime(2026, 11, 23), SemanaKpi.LunesDe(new DateTime(2026, 11, 29))); // domingo
            Assert.AreEqual(new DateTime(2026, 11, 30), SemanaKpi.CerradaEl(new DateTime(2026, 11, 23), 0));
            // Sin maduración, la pasada; con 21 días, la del 26/10 (cerrada el 23/11 + ... )
            Assert.AreEqual(new DateTime(2026, 11, 16), SemanaKpi.UltimaCerrada(0, viernes));
            Assert.AreEqual(new DateTime(2026, 10, 26), SemanaKpi.UltimaCerrada(21, viernes)); // 26/10 + 28 = 23/11 ≤ 27/11
            Assert.AreEqual(new DateTime(2026, 11, 16), SemanaKpi.UltimaCerrada(3, viernes));
            Assert.AreEqual(new DateTime(2026, 11, 9), SemanaKpi.UltimaCerrada(3, new DateTime(2026, 11, 25)));
        }

        [TestMethod]
        public void Periodo_EnCurso_HastaMananaYNuncaMasDeUnaSemana()
        {
            var enCurso = new PeriodoKpi(new DateTime(2026, 11, 25), new DateTime(2026, 11, 28));
            var entera = new PeriodoKpi(new DateTime(2026, 11, 16), new DateTime(2027, 1, 1));

            Assert.AreEqual(new DateTime(2026, 11, 23), enCurso.Lunes);
            Assert.AreEqual(new DateTime(2026, 11, 27), enCurso.UltimoDia);
            Assert.IsFalse(enCurso.Entera);
            Assert.AreEqual(new DateTime(2026, 11, 23), entera.Hasta);
            Assert.IsTrue(entera.Entera);
        }
    }
}
