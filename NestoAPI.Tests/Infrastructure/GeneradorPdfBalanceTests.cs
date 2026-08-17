using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#350: generador QuestPDF de balances. Dos paneles (activo/pasivo) para los
    /// balances y una sola columna para las cuentas de resultados (sin líneas 'P').
    /// </summary>
    [TestClass]
    public class GeneradorPdfBalanceTests
    {
        private static LineaBalanceInformeDTO Linea(string tipo, string descripcion,
            decimal? actual = null, decimal? anterior = null, bool esTotal = false, bool esCabecera = false)
        {
            return new LineaBalanceInformeDTO
            {
                Tipo = tipo,
                Descripcion = descripcion,
                SaldoActual = actual,
                SaldoAnterior = anterior,
                EsTotal = esTotal,
                EsCabecera = esCabecera,
                Porcentaje = actual.HasValue && anterior.HasValue && anterior != 0
                    ? (decimal?)System.Math.Round((actual.Value - anterior.Value) / anterior.Value * 100, 2)
                    : null
            };
        }

        private static BalanceInformeDTO Balance(params LineaBalanceInformeDTO[] lineas)
        {
            return new BalanceInformeDTO
            {
                Empresa = "1",
                NombreEmpresa = "Nueva Visión, S.A.",
                Numero = "BPY",
                Descripcion = "Balance de Pymes",
                Desde = new DateTime(2026, 1, 1),
                Hasta = new DateTime(2026, 6, 30),
                Lineas = new List<LineaBalanceInformeDTO>(lineas)
            };
        }

        private static async Task<byte[]> Bytes(GeneradorPdfBalance generador, BalanceInformeDTO balance)
        {
            return await generador.GenerarPdf(balance).ReadAsByteArrayAsync();
        }

        [TestMethod]
        public async Task GenerarPdf_BalanceConActivoYPasivo_DevuelvePdfValido()
        {
            var balance = Balance(
                Linea("A", "A) ACTIVO NO CORRIENTE", esCabecera: true),
                Linea("A", "I. Existencias.", 635646.36m, 632778.67m),
                Linea("A", "TOTAL ACTIVO (A + B)", 1730990.05m, 2074533.60m, esTotal: true),
                Linea("P", "1. Capital escriturado.", 637831.04m, 637831.04m),
                Linea("P", "TOTAL PATRIMONIO NETO Y PASIVO", 1730990.05m, 2074533.60m, esTotal: true));

            byte[] pdf = await Bytes(new GeneradorPdfBalance(), balance);

            Assert.IsTrue(pdf.Length > 1000, "El PDF debe tener contenido");
            Assert.AreEqual('%', (char)pdf[0]);
            Assert.AreEqual('P', (char)pdf[1]);
        }

        [TestMethod]
        public async Task GenerarPdf_CuentaDeResultadosSinPasivo_DevuelvePdfValido()
        {
            var pyg = Balance(
                Linea("A", "1. Importe neto de la cifra de negocios.", 1291997.47m, 1253386.90m),
                Linea("A", "A) RESULTADO DE EXPLOTACIÓN", 15537.39m, -45313.01m, esTotal: true));
            pyg.Numero = "PGP";
            pyg.Descripcion = "Pérdidas y Ganancias Pymes";

            byte[] pdf = await Bytes(new GeneradorPdfBalance(), pyg);

            Assert.IsTrue(pdf.Length > 1000);
        }

        [TestMethod]
        public void FormatearImporte_CerosDeDetalleEnBlanco_TotalesSiempreConImporte()
        {
            var detalle = new LineaBalanceInformeDTO { EsTotal = false };
            var total = new LineaBalanceInformeDTO { EsTotal = true };

            Assert.AreEqual(string.Empty, GeneradorPdfBalance.FormatearImporte(0m, detalle),
                "Un detalle a cero va en blanco, como los modelos oficiales");
            Assert.AreEqual("0,00 €", GeneradorPdfBalance.FormatearImporte(0m, total));
            Assert.AreEqual(string.Empty, GeneradorPdfBalance.FormatearImporte(null, detalle));
            Assert.AreEqual("1.730.990,05 €", GeneradorPdfBalance.FormatearImporte(1730990.05m, detalle));
        }

        [TestMethod]
        public void FormatearPorcentaje_SinValor_EnBlanco()
        {
            Assert.AreEqual(string.Empty, GeneradorPdfBalance.FormatearPorcentaje(null));
            Assert.AreEqual("-16,56", GeneradorPdfBalance.FormatearPorcentaje(-16.56m));
        }

        [TestMethod]
        public void EmpiezaPorNumero_SoloLasLineasNumeradas()
        {
            Assert.IsTrue(GeneradorPdfBalance.EmpiezaPorNumero("1. Proveedores"));
            Assert.IsFalse(GeneradorPdfBalance.EmpiezaPorNumero("I. Existencias."));
            Assert.IsFalse(GeneradorPdfBalance.EmpiezaPorNumero(null));
        }
    }
}
