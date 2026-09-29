using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Remesas;
using System;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#550: retención en la remesa de los recibos de facturas con envío con retorno. Caso
    /// real 32667: NV2615330 vencía el 22/09 y se remesó ese día; el retorno llegó el 23/09
    /// (miércoles) y la rectificativa se hizo ese mismo día.
    /// </summary>
    [TestClass]
    public class GatingRetornoFacturasTests
    {
        private static readonly DateTime VENCIMIENTO = new DateTime(2026, 9, 22);
        private static readonly DateTime RETORNO = new DateTime(2026, 9, 23);

        private static string Motivo(DateTime? retorno, DateTime hoy, bool negativos = false, int dias = 3, int tope = 15)
            => GatingRetornoFacturas.MotivoRetencion(retorno, VENCIMIENTO, hoy, negativos, dias, tope);

        [TestMethod]
        public void RetornoSinLlegar_SeRetieneYDiceElTope()
        {
            Assert.AreEqual("Retenido: el envío lleva retorno y aún no ha llegado; puede haber rectificativa (#550). " +
                "Sale solo el 07/10/2026 como muy tarde.", Motivo(null, VENCIMIENTO));
        }

        [TestMethod]
        public void RetornoRecibido_SeEsperanTresDiasLaborables()
        {
            // 23/09 (miércoles) + 3 laborables = 28/09 (lunes)
            Assert.AreEqual("Retenido: el retorno llegó el 23/09/2026; se espera por si hay rectificativa hasta el 28/09/2026 (#550).",
                Motivo(RETORNO, new DateTime(2026, 9, 25)));
            Assert.IsNull(Motivo(RETORNO, new DateTime(2026, 9, 28)));
        }

        [TestMethod]
        public void RectificativaHecha_SeLiberaYMandaLaPuertaDeNeteo()
        {
            Assert.IsNull(Motivo(RETORNO, new DateTime(2026, 9, 24), negativos: true));
            Assert.IsNull(Motivo(null, VENCIMIENTO, negativos: true));
        }

        [TestMethod]
        public void Tope_SaleAunqueElRetornoNoLlegueNunca()
        {
            Assert.IsNotNull(Motivo(null, new DateTime(2026, 10, 6)));
            Assert.IsNull(Motivo(null, new DateTime(2026, 10, 7)));
        }

        [TestMethod]
        public void RetornoTardio_LaEsperaNoPasaDelTope()
        {
            // Llega el 05/10: 3 laborables serían el 08/10, pero el tope es el 07/10
            StringAssert.Contains(Motivo(new DateTime(2026, 10, 5), new DateTime(2026, 10, 6)), "hasta el 07/10/2026");
            Assert.IsNull(Motivo(new DateTime(2026, 10, 5), new DateTime(2026, 10, 7)));
        }

        [TestMethod]
        public void DiasParametrizables()
        {
            Assert.IsNull(Motivo(RETORNO, new DateTime(2026, 9, 24), dias: 1));
            Assert.IsNull(Motivo(null, new DateTime(2026, 9, 25), tope: 3));
        }

        [TestMethod]
        public void SumarDiasLaborables_SaltaElFinDeSemana()
        {
            Assert.AreEqual(new DateTime(2026, 9, 28), GatingRetornoFacturas.SumarDiasLaborables(new DateTime(2026, 9, 25), 1));
            Assert.AreEqual(new DateTime(2026, 9, 28), GatingRetornoFacturas.SumarDiasLaborables(new DateTime(2026, 9, 26), 1));
            Assert.AreEqual(new DateTime(2026, 9, 24), GatingRetornoFacturas.SumarDiasLaborables(new DateTime(2026, 9, 23), 1));
        }
    }
}
