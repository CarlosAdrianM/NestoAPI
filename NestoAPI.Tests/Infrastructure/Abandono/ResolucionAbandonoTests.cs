using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Abandono;
using System;

namespace NestoAPI.Tests.Infrastructure.Abandono
{
    /// <summary>NestoAPI#714 (corte 1): la etiqueta al vencer el horizonte (Compró / Abandono / Baja / Desconocido).</summary>
    [TestClass]
    public class ResolucionAbandonoTests
    {
        private static readonly DateTime Baja = new DateTime(2026, 11, 20, 12, 30, 0);
        private static readonly DateTime Compra = new DateTime(2026, 11, 5);

        [TestMethod]
        public void Decidir_SinCompraNiBaja_Abandono()
        {
            (string resultado, DateTime? fecha) = ResolucionAbandono.Decidir(clienteExiste: true, fechaBaja: null, fechaPrimeraCompra: null);
            Assert.AreEqual(ResultadoAbandono.ABANDONO, resultado);
            Assert.IsNull(fecha);
        }

        [TestMethod]
        public void Decidir_ConCompra_Compro_ConLaFechaDeLaPrimera()
        {
            (string resultado, DateTime? fecha) = ResolucionAbandono.Decidir(true, null, Compra);
            Assert.AreEqual(ResultadoAbandono.COMPRO, resultado);
            Assert.AreEqual(Compra, fecha);
        }

        [TestMethod]
        public void Decidir_ConBaja_Baja_AunqueTambienComprara()
        {
            (string resultado, DateTime? fecha) = ResolucionAbandono.Decidir(true, Baja, Compra);
            Assert.AreEqual(ResultadoAbandono.BAJA, resultado);
            Assert.AreEqual(Baja.Date, fecha, "la fecha del hecho, sin hora");
        }

        [TestMethod]
        public void Decidir_SinFicha_Desconocido()
        {
            (string resultado, DateTime? fecha) = ResolucionAbandono.Decidir(false, Baja, Compra);
            Assert.AreEqual(ResultadoAbandono.DESCONOCIDO, resultado);
            Assert.IsNull(fecha);
        }

        [TestMethod]
        public void EsPositivo_AbandonoYBaja()
        {
            Assert.IsTrue(ResolucionAbandono.EsPositivo(ResultadoAbandono.ABANDONO));
            Assert.IsTrue(ResolucionAbandono.EsPositivo(ResultadoAbandono.BAJA));
            Assert.IsFalse(ResolucionAbandono.EsPositivo(ResultadoAbandono.COMPRO));
            Assert.IsFalse(ResolucionAbandono.EsPositivo(ResultadoAbandono.DESCONOCIDO));
        }

        [TestMethod]
        public void EstadosBaja_LosDeLaIssue_YCoincidenConElSql()
        {
            foreach (int estado in new[] { 9, 99, -1, 11, 22, 93, 95 })
            {
                Assert.IsTrue(ResolucionAbandono.EsEstadoBaja(estado), estado.ToString());
            }
            Assert.IsFalse(ResolucionAbandono.EsEstadoBaja(0));
            Assert.IsFalse(ResolucionAbandono.EsEstadoBaja(5));
            Assert.AreEqual(string.Join(", ", ResolucionAbandono.ESTADOS_BAJA), ResolucionAbandono.ESTADOS_BAJA_SQL);
        }
    }
}
