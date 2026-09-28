using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// AritmeticaMoney: la división money / int de SQL Server TRUNCA a 4 decimales hacia cero (Issue #547, §1.4).
    /// Los valores esperados están comprobados en el servidor (SELECT cast(x as money) / n).
    /// </summary>
    [TestClass]
    public class AritmeticaMoneyTests
    {
        [TestMethod]
        public void Dividir_TruncaEnVezDeRedondear()
        {
            Assert.AreEqual(32.1662m, AritmeticaMoney.Dividir(385.995m, 12)); // 32,16625 (41281)
            Assert.AreEqual(7.8847m, AritmeticaMoney.Dividir(94.617m, 12));   // 7,88475 (40985)
            Assert.AreEqual(0.6666m, AritmeticaMoney.Dividir(2m, 3));
            Assert.AreEqual(0.2187m, AritmeticaMoney.Dividir(7m, 32));
        }

        [TestMethod]
        public void Dividir_EnNegativosTruncaHaciaCero()
        {
            Assert.AreEqual(-0.1818m, AritmeticaMoney.Dividir(-1.091m, 6));   // 44904
            Assert.AreEqual(-0.0312m, AritmeticaMoney.Dividir(-1m, 32));
            Assert.AreEqual(10.9214m, AritmeticaMoney.Dividir(-76.45m, -7)); // 16137: abono entre abono
        }

        [TestMethod]
        public void Dividir_SiEsExactaNoCambiaNada()
        {
            Assert.AreEqual(3.675m, AritmeticaMoney.Dividir(44.1m, 12));
            Assert.AreEqual(5.25m, AritmeticaMoney.Dividir(21m, 4));
            Assert.AreEqual(0m, AritmeticaMoney.Dividir(0m, 12));
        }

        [TestMethod]
        [ExpectedException(typeof(DivideByZeroException))]
        public void Dividir_EntreCeroLanzaExcepcion()
        {
            // El SP protege todas sus divisiones con el WHERE: dividir entre 0 aquí es un fallo de la calculadora.
            AritmeticaMoney.Dividir(1m, 0);
        }

        [TestMethod]
        public void Truncar_CuatroDecimalesHaciaCero()
        {
            Assert.AreEqual(1.2345m, AritmeticaMoney.Truncar(1.23459999m));
            Assert.AreEqual(-1.2345m, AritmeticaMoney.Truncar(-1.23459999m));
            Assert.AreEqual(1.2m, AritmeticaMoney.Truncar(1.2m));
        }
    }
}
