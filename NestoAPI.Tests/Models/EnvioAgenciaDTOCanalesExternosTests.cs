using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// NestoAPI#258 slice (a): cada agencia declara UNA vez sus identificadores por canal externo
    /// (transportista de Prestashop, CarrierName/ShippingMethod de Amazon) y el nº de seguimiento,
    /// y EnvioAgenciaDTO los expone. Así los canales de Nesto dejan de re-parsear el enlace de
    /// seguimiento (los LeerDatosEnvio gemelos de Prestashop 2e84a88 y Amazon e4d80b3).
    /// Valores tomados de esos dos parches, que a su vez venían del if/else histórico.
    /// </summary>
    [TestClass]
    public class EnvioAgenciaDTOCanalesExternosTests
    {
        [TestMethod]
        public void CanalesExternos_ASM_DeclaraSusIdentificadores()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "ASM", CodigoBarras = "ABC123 " };

            Assert.AreEqual("160", dto.TransportistaPrestashop);
            Assert.AreEqual("GLS", dto.CarrierNameAmazon);
            Assert.AreEqual("Business Parcel", dto.ShippingMethodAmazon);
            Assert.AreEqual("ABC123", dto.NumeroSeguimiento);
        }

        [TestMethod]
        public void CanalesExternos_CorreosExpress_DeclaraSusIdentificadores()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Correos Express", CodigoBarras = "123456789" };

            Assert.AreEqual("105", dto.TransportistaPrestashop);
            Assert.AreEqual("Correos Express", dto.CarrierNameAmazon);
            Assert.AreEqual("ePaq", dto.ShippingMethodAmazon);
            Assert.AreEqual("123456789", dto.NumeroSeguimiento);
        }

        [TestMethod]
        public void CanalesExternos_Sending_DeclaraSusIdentificadores()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Sending", CodigoBarras = "LOC123" };

            Assert.AreEqual("103", dto.TransportistaPrestashop);
            Assert.AreEqual("Sending", dto.CarrierNameAmazon);
            Assert.AreEqual("Send Exprés", dto.ShippingMethodAmazon);
        }

        [TestMethod]
        public void CanalesExternos_Innovatrans_DeclaraSusIdentificadores()
        {
            // GLS e Innovatrans comparten el transportista genérico 160 de Prestashop
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Innovatrans", CodigoBarras = "6520139001" };

            Assert.AreEqual("160", dto.TransportistaPrestashop);
            Assert.AreEqual("Innovatrans", dto.CarrierNameAmazon);
            Assert.AreEqual("Estándar", dto.ShippingMethodAmazon);
            Assert.AreEqual("6520139001", dto.NumeroSeguimiento);
        }

        [TestMethod]
        public void CanalesExternos_OnTime_NoVendePorCanalesExternos()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "OnTime", Cliente = "00001", Pedido = 99999 };

            Assert.IsNull(dto.TransportistaPrestashop);
            Assert.IsNull(dto.CarrierNameAmazon);
            Assert.IsNull(dto.ShippingMethodAmazon);
        }

        [TestMethod]
        public void CanalesExternos_AgenciaDesconocida_DevuelveNull()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "AgenciaQueNoExiste", CodigoBarras = "123" };

            Assert.IsNull(dto.TransportistaPrestashop);
            Assert.IsNull(dto.CarrierNameAmazon);
            Assert.IsNull(dto.ShippingMethodAmazon);
        }
    }
}
