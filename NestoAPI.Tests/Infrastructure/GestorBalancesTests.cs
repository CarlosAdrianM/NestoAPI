using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#350: motor de balances (BPY/PGP) de las tablas Balances/LinBalance. La semántica
    /// se calibró el 17/08/26 contra la contabilidad real y los PDFs del viejo de junio/26
    /// (80/80 líneas cuadradas): signo literal de la fórmula × saldo Debe−Haber del patrón,
    /// Tipo 'P' negado, totales por suma de grupos. Las fórmulas de los tests son las REALES.
    /// </summary>
    [TestClass]
    public class GestorBalancesTests
    {
        // ============================ ParsearFormula ============================

        [TestMethod]
        public void ParsearFormula_PatronesConSignosLiterales_RespetaElSignoDeCadaTermino()
        {
            // I. Inmovilizado intangible (BPY): 20 − 280 − 290
            var terminos = GestorBalances.ParsearFormula(
                "(([Nº Cuenta] Like '20%') - [Nº Cuenta] Like '280%') - [Nº Cuenta] Like '290%'");

            Assert.AreEqual(3, terminos.Count);
            Assert.AreEqual(1, terminos[0].Signo);
            Assert.AreEqual("20", terminos[0].Prefijo);
            Assert.AreEqual(-1, terminos[1].Signo);
            Assert.AreEqual("280", terminos[1].Prefijo);
            Assert.AreEqual(-1, terminos[2].Signo);
            Assert.AreEqual("290", terminos[2].Prefijo);
        }

        [TestMethod]
        public void ParsearFormula_PrimerTerminoNegativo_LoDetecta()
        {
            // IV. (Acciones y participaciones...) (BPY): −108 −109
            var terminos = GestorBalances.ParsearFormula(
                "( - [Nº Cuenta] Like '108%') - [Nº Cuenta] Like '109%'");

            Assert.AreEqual(2, terminos.Count);
            Assert.AreEqual(-1, terminos[0].Signo);
            Assert.AreEqual(-1, terminos[1].Signo);
        }

        [TestMethod]
        public void ParsearFormula_SinFormula_DevuelveVacio()
        {
            Assert.AreEqual(0, GestorBalances.ParsearFormula(null).Count);
            Assert.AreEqual(0, GestorBalances.ParsearFormula("  ").Count);
            Assert.AreEqual(0, GestorBalances.ParsearFormula("1+2").Count, "Las fórmulas de totales no llevan patrones");
        }

        // ============================ EvaluarDetalle ============================

        private static Dictionary<string, decimal> Saldos(params (string cuenta, decimal saldo)[] valores)
        {
            return valores.ToDictionary(v => v.cuenta, v => v.saldo);
        }

        [TestMethod]
        public void EvaluarDetalle_ElComodinDelViejoNoCasaNada_PermiteEmpezarRestando()
        {
            // 1. INCN (PGP): 0 − 700 − 701 − ... (los ingresos tienen D−H negativo → positivo)
            var terminos = GestorBalances.ParsearFormula(
                "(([Nº Cuenta] Like '99999%') - [Nº Cuenta] Like '700%') - [Nº Cuenta] Like '705%'");
            var saldos = Saldos(("70000000", -1000m), ("70000001", -200m), ("70500000", -91.99m));

            decimal valor = GestorBalances.EvaluarDetalle(terminos, saldos, esPasivo: false);

            Assert.AreEqual(1291.99m, valor);
        }

        [TestMethod]
        public void EvaluarDetalle_TipoPasivo_NiegaElTotal()
        {
            // 1. Capital escriturado (BPY): 100+101+102, cuentas acreedoras (D−H negativo)
            var terminos = GestorBalances.ParsearFormula(
                "(([Nº Cuenta] Like '100%') + [Nº Cuenta] Like '101%') + [Nº Cuenta] Like '102%'");
            var saldos = Saldos(("10000000", -637831.04m));

            decimal valor = GestorBalances.EvaluarDetalle(terminos, saldos, esPasivo: true);

            Assert.AreEqual(637831.04m, valor);
        }

        [TestMethod]
        public void EvaluarDetalle_LaAmortizacionSeRestaSolaPorSuSigno()
        {
            // II. Inmovilizado material (BPY): 21 +281 −291 +23 — la 281 (amortización, saldo
            // acreedor) va SUMANDO en la fórmula porque su D−H ya es negativo.
            var terminos = GestorBalances.ParsearFormula(
                "((([Nº Cuenta] Like '21%') + [Nº Cuenta] Like '281%') - [Nº Cuenta] Like '291%') + [Nº Cuenta] Like '23%'");
            var saldos = Saldos(("21000000", 100000m), ("28100000", -40000m));

            decimal valor = GestorBalances.EvaluarDetalle(terminos, saldos, esPasivo: false);

            Assert.AreEqual(60000m, valor);
        }

        // ============================ Evaluar (líneas completas) ============================

        private static GestorBalances.LineaBalanceDefinicion Linea(int orden, string tipo, int grupo,
            string descripcion, string formula, bool esTotal = false)
        {
            return new GestorBalances.LineaBalanceDefinicion
            {
                Orden = orden,
                Tipo = tipo,
                Grupo = grupo,
                Descripcion = descripcion,
                Formula = formula,
                EsTotal = esTotal
            };
        }

        [TestMethod]
        public void Evaluar_TotalesPorGrupos_SumanLasLineasDeDetalleDeSusGrupos()
        {
            var definiciones = new List<GestorBalances.LineaBalanceDefinicion>
            {
                Linea(1, "A", 1, "I. Tesorería", "[Nº Cuenta] Like '57%'"),
                Linea(2, "A", 1, "Total A)...", "1", esTotal: true),
                Linea(3, "A", 2, "II. Existencias", "[Nº Cuenta] Like '30%'"),
                Linea(4, "A", 2, "Total B)...", "2", esTotal: true),
                Linea(5, "A", 3, "TOTAL ACTIVO", "1+2", esTotal: true)
            };
            var saldosN = Saldos(("57200005", 150m), ("30000000", 50m));
            var saldosN1 = Saldos(("57200005", 100m), ("30000000", 100m));

            List<LineaBalanceInformeDTO> lineas = GestorBalances.Evaluar(definiciones, saldosN, saldosN1);

            Assert.AreEqual(150m, lineas.Single(l => l.Orden == 2).SaldoActual);
            Assert.AreEqual(50m, lineas.Single(l => l.Orden == 4).SaldoActual);
            LineaBalanceInformeDTO totalActivo = lineas.Single(l => l.Orden == 5);
            Assert.AreEqual(200m, totalActivo.SaldoActual);
            Assert.AreEqual(200m, totalActivo.SaldoAnterior);
            Assert.AreEqual(0m, totalActivo.Porcentaje);
        }

        [TestMethod]
        public void Evaluar_EpigrafeSinFormula_EsCabeceraSinImportes()
        {
            var definiciones = new List<GestorBalances.LineaBalanceDefinicion>
            {
                Linea(1, "A", 1, "A) ACTIVO NO CORRIENTE", null),
                Linea(2, "A", 1, "I. Tesorería", "[Nº Cuenta] Like '57%'")
            };

            List<LineaBalanceInformeDTO> lineas = GestorBalances.Evaluar(
                definiciones, Saldos(("57000000", 10m)), Saldos());

            LineaBalanceInformeDTO cabecera = lineas.Single(l => l.Orden == 1);
            Assert.IsTrue(cabecera.EsCabecera);
            Assert.IsNull(cabecera.SaldoActual);
            Assert.IsNull(cabecera.Porcentaje);
        }

        [TestMethod]
        public void Evaluar_OrdenaPorGrupoYDentroPorOrden()
        {
            // La línea "Total A) Patrimonio neto" del BPY real tiene NºOrden 54254, POSTERIOR al
            // total general 54253, pero se presenta dentro de su grupo 5.
            var definiciones = new List<GestorBalances.LineaBalanceDefinicion>
            {
                Linea(54253, "P", 7, "TOTAL PN Y PASIVO", "4+5", esTotal: true),
                Linea(54254, "P", 5, "Total A) Patrimonio neto...", "4+5", esTotal: true),
                Linea(54220, "P", 4, "1. Capital escriturado.", "[Nº Cuenta] Like '100%'")
            };

            List<LineaBalanceInformeDTO> lineas = GestorBalances.Evaluar(definiciones, Saldos(), Saldos());

            CollectionAssert.AreEqual(new List<int> { 54220, 54254, 54253 },
                lineas.Select(l => l.Orden).ToList());
        }

        [TestMethod]
        public void CalcularPorcentaje_SinAnnoAnterior_EsNull()
        {
            // El viejo pintaba ruido (−100,00 / 4.829,65 sobre bases despreciables): sin base no hay %.
            Assert.IsNull(GestorBalances.CalcularPorcentaje(100m, 0m));
            Assert.AreEqual(-16.56m, GestorBalances.CalcularPorcentaje(1730990.05m, 2074533.60m));
            Assert.AreEqual(27.95m, GestorBalances.CalcularPorcentaje(-491538.08m, -384166.01m),
                "Con base negativa el signo del % sigue el sentido de la variación (caso real del PDF)");
        }
    }
}
