using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class PedidosVentaControllerTests
    {

        [TestMethod]
        public void PedidoVentaController_ComprobarSiSePuedenInsertarLineas_SiElPedidoTienePickingYEtiquetaImpresaNoSePuedeCambiarElContacto()
        {

        }

        // NOTA: Los tests del endpoint ObtenerDocumentosImpresion son complejos de mockear
        // debido a la estructura de Entity Framework y las relaciones de navegación.
        // La lógica crítica está testeada en GestorFacturacionRutasTests.ObtenerDocumentosImpresion_*
        // que cubren todos los escenarios de negocio.

        // NestoAPI#200: el correo del pedido lee del DTO 'pedido' (no de la cabecera EF),
        // así que el DTO debe normalizarse cuando iva=null para que coincida con lo persistido.
        // El helper centraliza la lógica de POST y PUT.

        private static Empresa CrearEmpresaConDefaults()
        {
            return new Empresa
            {
                Número = "1",
                FormaPagoEfectivo = "EFC",
                PlazosPagoDefecto = "CONTADO"
            };
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNoNull_NoTocaDto()
        {
            var pedido = new PedidoVentaDTO
            {
                iva = "G",
                formaPago = "RCB",
                plazosPago = "30D",
                ccc = "2",
                periodoFacturacion = "FDM"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.AreEqual("RCB", pedido.formaPago);
            Assert.AreEqual("30D", pedido.plazosPago);
            Assert.AreEqual("2", pedido.ccc);
            Assert.AreEqual("FDM", pedido.periodoFacturacion);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNull_PoneFormaPagoYPlazosPagoDeEmpresa()
        {
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                formaPago = "RCB",
                plazosPago = "30D",
                ccc = "2"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.AreEqual("EFC", pedido.formaPago);
            Assert.AreEqual("CONTADO", pedido.plazosPago);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNull_PoneCccNull()
        {
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                formaPago = "RCB",
                plazosPago = "30D",
                ccc = "2"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.IsNull(pedido.ccc);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNullYPlazoPRE_RespetaFormaPagoYPlazo()
        {
            // En prepago se respeta plazosPago=PRE y la forma de pago original
            // (TARJETA, TRANSFERENCIA, etc.), aunque iva venga null.
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                formaPago = "TAR",
                plazosPago = "PRE",
                ccc = "5"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.AreEqual("TAR", pedido.formaPago);
            Assert.AreEqual("PRE", pedido.plazosPago);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_IvaNullYPlazoPRE_PoneCccNull()
        {
            // El CCC siempre se anula cuando iva=null, incluso en prepago.
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                formaPago = "TAR",
                plazosPago = "PRE",
                ccc = "5"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.IsNull(pedido.ccc);
        }

        [TestMethod]
        public void NormalizarPedidoSiIvaNull_NoTocaPeriodoFacturacion()
        {
            // El helper no gestiona periodoFacturacion. PUT lo fuerza a NRM aparte;
            // POST lo deja como venga del DTO.
            var pedido = new PedidoVentaDTO
            {
                iva = null,
                plazosPago = "30D",
                periodoFacturacion = "FDM"
            };
            var empresa = CrearEmpresaConDefaults();

            PedidosVentaController.NormalizarPedidoSiIvaNull(pedido, empresa);

            Assert.AreEqual("FDM", pedido.periodoFacturacion);
        }
    }
}
