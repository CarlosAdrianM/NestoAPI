using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// Tests de la regla pura <see cref="GeneradorPortes.DebeAnadirComisionReembolso"/>
    /// (NestoAPI#174). La integración con BD (lectura de cabecera, detección de línea
    /// sin picking, creación física de la línea) queda fuera de los tests unitarios:
    /// se basa en los mismos mecanismos que los portes, que funcionan desde hace tiempo.
    /// </summary>
    [TestClass]
    public class GeneradorPortesTest
    {
        [TestMethod]
        public void DebeAnadirComisionReembolso_PedidoNoReembolso_No()
        {
            var resultado = GeneradorPortes.DebeAnadirComisionReembolso(
                esContraReembolso: false,
                noCobrarComisionReembolso: false,
                flagNoCobrarEsEfectivo: true,
                yaHayLineaComisionSinPicking: false);

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void DebeAnadirComisionReembolso_ReembolsoSinLineaPendiente_Si()
        {
            // Caso típico: split de envíos. La línea original del pedido ya cogió
            // picking en el primer envío, al llegar otro envío no queda ninguna sin
            // picking y hay que crear una nueva.
            var resultado = GeneradorPortes.DebeAnadirComisionReembolso(
                esContraReembolso: true,
                noCobrarComisionReembolso: false,
                flagNoCobrarEsEfectivo: true,
                yaHayLineaComisionSinPicking: false);

            Assert.IsTrue(resultado);
        }

        [TestMethod]
        public void DebeAnadirComisionReembolso_ReembolsoConLineaPendiente_NoDuplica()
        {
            // Primer picking: la línea original del pedido (creada al crear el pedido)
            // todavía no tiene picking asignado. No hace falta crear otra: la existente
            // se engancha en este picking y se factura.
            var resultado = GeneradorPortes.DebeAnadirComisionReembolso(
                esContraReembolso: true,
                noCobrarComisionReembolso: false,
                flagNoCobrarEsEfectivo: true,
                yaHayLineaComisionSinPicking: true);

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void DebeAnadirComisionReembolso_FlagNoCobrarActivoYEfectivo_NoCobra()
        {
            // Issue #159: hasta la fecha de corte el flag NoCobrarComisionReembolso
            // exime de cobrar la comisión. Si está activo y vigente, no se añade.
            var resultado = GeneradorPortes.DebeAnadirComisionReembolso(
                esContraReembolso: true,
                noCobrarComisionReembolso: true,
                flagNoCobrarEsEfectivo: true,
                yaHayLineaComisionSinPicking: false);

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void DebeAnadirComisionReembolso_FlagActivoPeroYaNoEsEfectivo_SiCobra()
        {
            // Tras la FECHA_CORTE_NO_COBRAR_COMISION_REEMBOLSO (2026-09-01) el flag
            // se ignora: aunque el pedido lo lleve marcado, se cobra comisión.
            var resultado = GeneradorPortes.DebeAnadirComisionReembolso(
                esContraReembolso: true,
                noCobrarComisionReembolso: true,
                flagNoCobrarEsEfectivo: false,
                yaHayLineaComisionSinPicking: false);

            Assert.IsTrue(resultado);
        }
    }
}
