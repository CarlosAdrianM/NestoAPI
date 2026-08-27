using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// NestoAPI#240: caracterización de EnvioAgenciaDTO.EnlaceSeguimiento. Bloquea las URLs y el
    /// contrato de "sin URL" ("" si la agencia se conoce pero faltan datos, "error, agencia no
    /// definida" si no se conoce) antes/después de mover la lógica a RegistroSeguimientoAgencias.
    /// </summary>
    [TestClass]
    public class EnvioAgenciaDTOSeguimientoTests
    {
        [TestMethod]
        public void EnlaceSeguimiento_ASM_ConCodigoBarrasYCodigoPostal_DevuelveUrlGls()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "ASM", CodigoBarras = "ABC123", CodigoPostal = "28001" };
            Assert.AreEqual("https://mygls.gls-spain.es/e/ABC123/28001", dto.EnlaceSeguimiento);
        }

        [TestMethod]
        public void EnlaceSeguimiento_ASM_SinCodigoPostal_DevuelveVacio()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "ASM", CodigoBarras = "ABC123", CodigoPostal = null };
            Assert.AreEqual(string.Empty, dto.EnlaceSeguimiento);
        }

        [TestMethod]
        public void EnlaceSeguimiento_OnTime_ConClienteYPedido_DevuelveUrlConReferencia()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "OnTime", Cliente = "00001", Pedido = 99999 };
            string url = dto.EnlaceSeguimiento;
            StringAssert.Contains(url, "ontimegts.alertran.net");
            StringAssert.Contains(url, "cliente=02890107");
            StringAssert.Contains(url, "00001-99999");
        }

        [TestMethod]
        public void EnlaceSeguimiento_CorreosExpress_ConCodigoBarras_DevuelveUrl()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Correos Express", CodigoBarras = "123456789" };
            Assert.AreEqual("https://s.correosexpress.com/c?n=123456789", dto.EnlaceSeguimiento);
        }

        [TestMethod]
        public void EnlaceSeguimiento_CorreosExpress_SinCodigoBarras_DevuelveVacio()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Correos Express", CodigoBarras = null };
            Assert.AreEqual(string.Empty, dto.EnlaceSeguimiento);
        }

        [TestMethod]
        public void EnlaceSeguimiento_Sending_ConIdentificadorYCodigoBarras_DevuelveUrl()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Sending", AgenciaIdentificador = "CLIENTE001", CodigoBarras = "LOC123" };
            Assert.AreEqual("https://info.sending.es/fgts/pub/locNumServ.seam?cliente=CLIENTE001&localizador=LOC123", dto.EnlaceSeguimiento);
        }

        [TestMethod]
        public void EnlaceSeguimiento_Innovatrans_ConCodigoBarras_DevuelveUrlTipSa()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Innovatrans", CodigoBarras = "6520139001" };
            Assert.AreEqual("https://aplicaciones.tip-sa.com/cliente/datos_env.php?id=0280400280406520139001", dto.EnlaceSeguimiento);
        }

        [TestMethod]
        public void EnlaceSeguimiento_Innovatrans_RecortaElAlbaran()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Innovatrans", CodigoBarras = "  6520139001  " };
            Assert.AreEqual("https://aplicaciones.tip-sa.com/cliente/datos_env.php?id=0280400280406520139001", dto.EnlaceSeguimiento);
        }

        [TestMethod]
        public void EnlaceSeguimiento_AgenciaDesconocida_DevuelveError()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "AgenciaQueNoExiste", CodigoBarras = "123" };
            Assert.AreEqual("error, agencia no definida", dto.EnlaceSeguimiento);
        }

        // ===== NestoAPI#417: tracking para Prestashop =====
        //
        // El transportista genérico 160 (GLS/Innovatrans) tiene la plantilla de URL vacía en la
        // tienda ("https://@"), así que si viaja el número pelado el cliente ve un texto muerto.
        // Para esas agencias viaja el ENLACE completo sin esquema (la plantilla antepone el
        // https://); CEX y Sending tienen transportista propio con plantilla y siguen con el
        // número de siempre.

        [TestMethod]
        public void TrackingPrestashop_ASM_ViajaElEnlaceCompletoSinEsquema()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "ASM", CodigoBarras = "61197140248079", CodigoPostal = "31010" };
            Assert.AreEqual("mygls.gls-spain.es/e/61197140248079/31010", dto.TrackingPrestashop);
        }

        [TestMethod]
        public void TrackingPrestashop_ASM_SinCodigoPostal_CaeAlNumeroPelado()
        {
            // Sin CP no hay enlace posible: mejor el número que nada.
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "ASM", CodigoBarras = "61197140248079", CodigoPostal = null };
            Assert.AreEqual("61197140248079", dto.TrackingPrestashop);
        }

        [TestMethod]
        public void TrackingPrestashop_Innovatrans_ViajaElEnlaceCompletoSinEsquema()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Innovatrans", CodigoBarras = "6520139001" };
            Assert.AreEqual("aplicaciones.tip-sa.com/cliente/datos_env.php?id=0280400280406520139001", dto.TrackingPrestashop);
        }

        [TestMethod]
        public void TrackingPrestashop_CorreosExpress_SigueSiendoElNumeroPelado()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Correos Express", CodigoBarras = "123456789" };
            Assert.AreEqual("123456789", dto.TrackingPrestashop);
        }

        [TestMethod]
        public void TrackingPrestashop_Sending_SigueSiendoElNumeroPelado()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Sending", AgenciaIdentificador = "CLIENTE001", CodigoBarras = "LOC123" };
            Assert.AreEqual("LOC123", dto.TrackingPrestashop);
        }

        [TestMethod]
        public void TrackingPrestashop_OnTime_NoVendePorPrestashop_Null()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "OnTime", Cliente = "00001", Pedido = 99999 };
            Assert.IsNull(dto.TrackingPrestashop);
        }

        [TestMethod]
        public void TrackingPrestashop_AgenciaDesconocida_Null()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "AgenciaQueNoExiste", CodigoBarras = "123" };
            Assert.IsNull(dto.TrackingPrestashop);
        }
    }
}
