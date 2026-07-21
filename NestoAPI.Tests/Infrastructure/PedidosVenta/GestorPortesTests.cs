using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    [TestClass]
    public class GestorPortesTests
    {
        #region EsContraReembolso

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConCCC_NoEsReembolso()
        {
            Assert.IsFalse(GestorPortes.EsContraReembolso("EFC", null, "123456", "NRM", false));
        }

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConFDM_NoEsReembolso()
        {
            Assert.IsFalse(GestorPortes.EsContraReembolso("EFC", null, null, "FDM", false));
        }

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConTransferencia_NoEsReembolso()
        {
            Assert.IsFalse(GestorPortes.EsContraReembolso("TRN", null, null, "NRM", false));
        }

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConTarjeta_NoEsReembolso()
        {
            Assert.IsFalse(GestorPortes.EsContraReembolso("TAR", null, null, "NRM", false));
        }

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConCNF_NoEsReembolso()
        {
            Assert.IsFalse(GestorPortes.EsContraReembolso("CNF", null, null, "NRM", false));
        }

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConCHC_NoEsReembolso()
        {
            Assert.IsFalse(GestorPortes.EsContraReembolso("CHC", null, null, "NRM", false));
        }

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConNotaEntrega_NoEsReembolso()
        {
            Assert.IsFalse(GestorPortes.EsContraReembolso("EFC", null, null, "NRM", true));
        }

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConPrepago_NoEsReembolso()
        {
            Assert.IsFalse(GestorPortes.EsContraReembolso("EFC", "PRE", null, "NRM", false));
        }

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConEfectivo_SiEsReembolso()
        {
            Assert.IsTrue(GestorPortes.EsContraReembolso("EFC", "CONTADO", null, "NRM", false));
        }

        [TestMethod]
        public void GestorPortes_EsContraReembolso_ConReciboBancario_SiEsReembolso()
        {
            Assert.IsTrue(GestorPortes.EsContraReembolso("RCB", null, null, "NRM", false));
        }

        #endregion

        #region EsProvincial

        [TestMethod]
        public void GestorPortes_EsProvincial_CodigoPostal28_EsProvincial()
        {
            Assert.IsTrue(GestorPortes.EsProvincial("28100"));
        }

        [TestMethod]
        public void GestorPortes_EsProvincial_CodigoPostal19_EsProvincial()
        {
            Assert.IsTrue(GestorPortes.EsProvincial("19001"));
        }

        [TestMethod]
        public void GestorPortes_EsProvincial_CodigoPostal45_EsProvincial()
        {
            Assert.IsTrue(GestorPortes.EsProvincial("45001"));
        }

        [TestMethod]
        public void GestorPortes_EsProvincial_CodigoPostal08_NoProvincia()
        {
            Assert.IsFalse(GestorPortes.EsProvincial("08001"));
        }

        [TestMethod]
        public void GestorPortes_EsProvincial_CodigoPostalNulo_NoProvincia()
        {
            Assert.IsFalse(GestorPortes.EsProvincial(null));
        }

        #endregion

        #region CalcularPortes

        [TestMethod]
        public void GestorPortes_CalcularPortes_CanalExterno_PortesGratis()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                EsCanalExterno = true,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis);
            Assert.AreEqual(0, resultado.ImportePortes);
        }

        [TestMethod]
        public void GestorPortes_EsCanalExterno_Amazon_DevuelveTrue()
        {
            Assert.IsTrue(Constantes.FormasVenta.EsCanalExterno(Constantes.FormasVenta.AMAZON));
        }

        [TestMethod]
        public void GestorPortes_EsCanalExterno_TiendaOnline_DevuelveTrue()
        {
            Assert.IsTrue(Constantes.FormasVenta.EsCanalExterno(Constantes.FormasVenta.TIENDA_ONLINE));
        }

        [TestMethod]
        public void GestorPortes_EsCanalExterno_FormaVentaNormal_DevuelveFalse()
        {
            Assert.IsFalse(Constantes.FormasVenta.EsCanalExterno("EFC"));
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_Glovo_PortesGratis()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "GLV",
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis);
            Assert.AreEqual(0, resultado.ImportePortes);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_RutaSinPortes_PortesGratis()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "XX",
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis);
            Assert.AreEqual(0, resultado.ImportePortes);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_RutaSinPortes_SiCalculaReembolso()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "XX",
                FormaPago = "EFC",
                PlazosPago = "CONTADO",
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis);
            Assert.AreEqual(0, resultado.ImportePortes);
            Assert.IsTrue(resultado.EsContraReembolso);
            Assert.AreEqual(Constantes.Portes.INCREMENTO_REEMBOLSO, resultado.ComisionReembolso);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_NotaEntrega_PortesGratis()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                NotaEntrega = true,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_SuperaUmbral_PortesGratis()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 75
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis);
            Assert.AreEqual(0, resultado.ImportePortes);
            Assert.AreEqual(0, resultado.ImporteFaltaParaPortesGratis);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_Provincial_PortesProvinciales()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(Constantes.Portes.PROVINCIAL, resultado.ImportePortes);
            Assert.AreEqual(Constantes.Cuentas.CUENTA_PORTES_ONTIME, resultado.CuentaPortes);
            Assert.AreEqual(25, resultado.ImporteFaltaParaPortesGratis);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_Peninsular_PortesPeninsulares()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "08001",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(Constantes.Portes.PENINSULAR, resultado.ImportePortes);
            Assert.AreEqual(Constantes.Cuentas.CUENTA_PORTES_CEX, resultado.CuentaPortes);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_Baleares_PortesBaleares()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "07001",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(Constantes.Portes.BALEARES, resultado.ImportePortes);
            Assert.AreEqual(Constantes.Cuentas.CUENTA_PORTES_CEX, resultado.CuentaPortes);
            Assert.AreEqual(100, resultado.ImporteFaltaParaPortesGratis);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_CanariasLasPalmas_PortesCanarias()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "35001",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(Constantes.Portes.CANARIAS, resultado.ImportePortes);
            Assert.AreEqual(Constantes.Cuentas.CUENTA_PORTES_CEX, resultado.CuentaPortes);
            Assert.AreEqual(350, resultado.ImporteFaltaParaPortesGratis);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_CanariasTenerife_PortesCanarias()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "38001",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(Constantes.Portes.CANARIAS, resultado.ImportePortes);
            Assert.AreEqual(Constantes.Cuentas.CUENTA_PORTES_CEX, resultado.CuentaPortes);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_BalearesSinProductos_DevuelvePortesBaleares()
        {
            // NestoAPI#192: la rama BaseImponibleProductos<=0 también debe devolver
            // el importe correcto por zona, para que el cliente pueda mostrarlo
            // cuando se añadan productos.
            var input = new PedidoPortesInput
            {
                CodigoPostal = "07001",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 0
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis, "Sin productos no se cobran portes");
            Assert.AreEqual(Constantes.Portes.BALEARES, resultado.ImportePortes);
            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO_BALEARES, resultado.ImporteMinimoPedidoSinPortes);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_CanariasSinProductos_DevuelvePortesCanarias()
        {
            // NestoAPI#192: idem para Canarias en la rama BaseImponibleProductos<=0.
            var input = new PedidoPortesInput
            {
                CodigoPostal = "35001",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 0
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis);
            Assert.AreEqual(Constantes.Portes.CANARIAS, resultado.ImportePortes);
            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS, resultado.ImporteMinimoPedidoSinPortes);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_ContraReembolso_ComisionReembolso()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = "EFC",
                PlazosPago = "CONTADO",
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.EsContraReembolso);
            Assert.AreEqual(Constantes.Portes.INCREMENTO_REEMBOLSO, resultado.ComisionReembolso);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_ConTarjeta_SinComisionReembolso()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = "TAR",
                PlazosPago = "CONTADO",
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.EsContraReembolso);
            Assert.AreEqual(0, resultado.ComisionReembolso);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_RutaNula_EsRutaConPortes()
        {
            // Ruta null se considera ruta con portes (por GestorImportesMinimos.esRutaConPortes)
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = null,
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(Constantes.Portes.PROVINCIAL, resultado.ImportePortes);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_ImporteMinimoPedidoSinPortes_Estandar_Devuelve75()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO, resultado.ImporteMinimoPedidoSinPortes);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_Peninsular_Umbral100()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "08001",
                Ruta = "FW",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO_PENINSULAR, resultado.ImporteMinimoPedidoSinPortes);
            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(50, resultado.ImporteFaltaParaPortesGratis);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_Espejo_Umbral150()
        {
            // Pedido espejo (IVA null) → umbral 150€
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = null,
                BaseImponibleProductos = 100
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO_ESPEJO, resultado.ImporteMinimoPedidoSinPortes);
            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(50, resultado.ImporteFaltaParaPortesGratis);
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_PrecioPublicoFinal_Umbral10()
        {
            // Precio público final → umbral 10€
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = "EFC",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                EsPrecioPublicoFinal = true,
                BaseImponibleProductos = 5
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE_PRECIO_PUBLICO_FINAL, resultado.ImporteMinimoPedidoSinPortes);
            Assert.IsFalse(resultado.PortesGratis);
        }

        [TestMethod]
        public void GestorPortes_ObtenerUmbralPortesGratis_Provincial_Devuelve75()
        {
            Assert.AreEqual(75, GestorPortes.ObtenerUmbralPortesGratis("28001", iva: "G21"));
            Assert.AreEqual(75, GestorPortes.ObtenerUmbralPortesGratis("19001", iva: "G21"));
            Assert.AreEqual(75, GestorPortes.ObtenerUmbralPortesGratis("45001", iva: "G21"));
        }

        [TestMethod]
        public void GestorPortes_ObtenerUmbralPortesGratis_Peninsular_Devuelve100()
        {
            Assert.AreEqual(100, GestorPortes.ObtenerUmbralPortesGratis("08001", iva: "G21"));
            Assert.AreEqual(100, GestorPortes.ObtenerUmbralPortesGratis("41001", iva: "G21"));
        }

        [TestMethod]
        public void GestorPortes_ObtenerUmbralPortesGratis_Baleares_Devuelve150()
        {
            Assert.AreEqual(150, GestorPortes.ObtenerUmbralPortesGratis("07001", iva: "G21"));
        }

        [TestMethod]
        public void GestorPortes_ObtenerUmbralPortesGratis_Canarias_Devuelve400()
        {
            Assert.AreEqual(400, GestorPortes.ObtenerUmbralPortesGratis("35001", iva: "G21"));
            Assert.AreEqual(400, GestorPortes.ObtenerUmbralPortesGratis("38001", iva: "G21"));
        }

        [TestMethod]
        public void GestorPortes_ObtenerUmbralPortesGratis_Espejo_Devuelve150()
        {
            Assert.AreEqual(150, GestorPortes.ObtenerUmbralPortesGratis("28001", iva: null));
            Assert.AreEqual(150, GestorPortes.ObtenerUmbralPortesGratis("28001", iva: ""));
        }

        [TestMethod]
        public void GestorPortes_ObtenerUmbralPortesGratis_PrecioPublicoFinal_Devuelve10()
        {
            Assert.AreEqual(10, GestorPortes.ObtenerUmbralPortesGratis("28001", esPrecioPublicoFinal: true, iva: "G21"));
        }

        [TestMethod]
        public void GestorPortes_ObtenerUmbralPortesGratis_CodigoPostalNulo_DevuelveUmbralMasAlto()
        {
            // Sin código postal no podemos determinar la zona geográfica.
            // Devolvemos el umbral más alto (Canarias, 400€) como valor conservador.
            // El cliente debe hacer una nueva llamada cuando disponga del CP real.
            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS,
                GestorPortes.ObtenerUmbralPortesGratis(null, iva: "G21"),
                "CP null debe devolver el umbral más alto (Canarias)");
            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS,
                GestorPortes.ObtenerUmbralPortesGratis("", iva: "G21"),
                "CP vacío debe devolver el umbral más alto (Canarias)");
        }

        #endregion

        #region CalcularBaseImponibleProductos

        [TestMethod]
        public void GestorPortes_CalcularBaseImponibleProductos_ExcluyePortes()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 100, Cantidad = 1 },
                new LineaPedidoVentaDTO { tipoLinea = 2, Producto = "62400002", PrecioUnitario = 3.5M, Cantidad = 1 }
            };

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas);

            Assert.AreEqual(100, resultado);
        }

        [TestMethod]
        public void GestorPortes_CalcularBaseImponibleProductos_ExcluyeReembolso()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 100, Cantidad = 1 },
                new LineaPedidoVentaDTO { tipoLinea = 2, Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL, PrecioUnitario = 3, Cantidad = 1 }
            };

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas);

            Assert.AreEqual(100, resultado);
        }

        [TestMethod]
        public void GestorPortes_CalcularBaseImponibleProductos_IncluyeCuentasNoPortes()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 100, Cantidad = 1 },
                new LineaPedidoVentaDTO { tipoLinea = 2, Producto = "70000001", PrecioUnitario = 20, Cantidad = 1 }
            };

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas);

            Assert.AreEqual(120, resultado);
        }

        #endregion

        #region CalcularBaseImponibleProductos con servir junto (#211)

        // Caso del issue: 3 líneas de producto normal + 1 línea de producto estado != 0, mismo almacén.
        // Issue #299: la regla mira el estado del PRODUCTO (EstadoProducto); el estado de la LÍNEA
        // se pone a 1 (EN_CURSO) en todas para probar que no influye (antes se confundían).
        private static HashSet<LineaPedidoVentaDTO> LineasEjemplo211(short estadoUltima)
        {
            return new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 10, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = 0 },
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD2", PrecioUnitario = 10, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = 0 },
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD3", PrecioUnitario = 10, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = 0 },
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD4", PrecioUnitario = 10, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = estadoUltima }
            };
        }

        [TestMethod]
        public void GestorPortes_CalcularBase_ServirJunto_IncluyeLineaSobrePedido()
        {
            // Servir junto = una única entrega: la línea estado != 0 (aunque no haya stock) cuenta. Base = 40.
            var lineas = LineasEjemplo211(estadoUltima: 4);
            var stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.Stock("PROD4", "ALG")).Returns(0);

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas, servirJunto: true, stocks);

            Assert.AreEqual(40, resultado, "Con servir junto todas las líneas cuentan para la base de portes");
        }

        [TestMethod]
        public void GestorPortes_CalcularBase_SinServirJunto_SinStockAlmacen_ExcluyeLineaSobrePedido()
        {
            // Sin servir junto, la línea estado != 0 sin stock en el almacén es sobre pedido → se excluye. Base = 30.
            // (Rojo sin el fix: la sobrecarga antigua sumaba las 4 líneas = 40.)
            var lineas = LineasEjemplo211(estadoUltima: 4);
            var stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.Stock("PROD4", "ALG")).Returns(0);

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas, servirJunto: false, stocks);

            Assert.AreEqual(30, resultado, "Sin servir junto, la línea sobre pedido no cuenta en esta entrega");
        }

        [TestMethod]
        public void GestorPortes_CalcularBase_SinServirJunto_ConStockAlmacen_IncluyeLinea()
        {
            // Sin servir junto pero la línea estado != 0 SÍ tiene stock en el almacén → no es sobre pedido. Base = 40.
            var lineas = LineasEjemplo211(estadoUltima: 4);
            var stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.Stock("PROD4", "ALG")).Returns(5);

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas, servirJunto: false, stocks);

            Assert.AreEqual(40, resultado);
        }

        [TestMethod]
        public void GestorPortes_CalcularBase_SinServirJunto_TodoEstado0_NoConsultaStock()
        {
            // Estado 0 nunca es sobre pedido: no debe consultarse el stock (caso normal, sin coste extra).
            var lineas = LineasEjemplo211(estadoUltima: 0);
            var stocks = A.Fake<IGestorStocks>();

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas, servirJunto: false, stocks);

            Assert.AreEqual(40, resultado);
            A.CallTo(() => stocks.Stock(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GestorPortes_CalcularBase_GestorStocksNull_CuentaTodasLasLineas()
        {
            // Sin gestor de stocks (fallback defensivo) se cuenta todo, como la sobrecarga antigua.
            var lineas = LineasEjemplo211(estadoUltima: 4);

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas, servirJunto: false, gestorStocks: null);

            Assert.AreEqual(40, resultado);
        }

        #endregion

        #region CalcularBaseImponibleProductos con EstadoProducto (#299)

        // Regresión #299 (pedido real 922175): EsLineaSobrePedidoEnAlmacen comparaba el estado de
        // la LÍNEA de venta (-1 pendiente / 1 en curso) como si fuera el estado del PRODUCTO, así
        // que el chequeo de stock de #211 se aplicaba a TODAS las líneas vivas y se cobraban
        // portes al cliente cuando a nosotros nos faltaba stock de un producto normal.
        [TestMethod]
        public void GestorPortes_CalcularBase_SinServirJunto_ProductoNormalSinStock_NoSeExcluye()
        {
            // Producto NORMAL (EstadoProducto = 0) en línea EN CURSO (estado de línea = 1) sin
            // stock: nunca es sobre pedido, debe contar en la base de portes. Total < 150 para
            // que el backstop de importe original no enmascare la regla.
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 60, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = 0 },
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD2", PrecioUnitario = 40, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = 0 }
            };
            var stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.Stock(A<string>._, A<string>._)).Returns(0);

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas, servirJunto: false, stocks);

            Assert.AreEqual(100, resultado,
                "Un producto normal (EstadoProducto = 0) cuenta en la base aunque falte stock; el estado de la LÍNEA no pinta nada aquí");
        }

        [TestMethod]
        public void GestorPortes_CalcularBase_SinServirJunto_EstadoProductoNull_NoSeExcluye()
        {
            // Los clientes (Nesto/NestoApp) no envían EstadoProducto: null = desconocido se trata
            // como NO sobre pedido (mejor perder una exclusión que cobrar portes indebidos).
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 60, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = null },
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD2", PrecioUnitario = 40, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = null }
            };
            var stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.Stock(A<string>._, A<string>._)).Returns(0);

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas, servirJunto: false, stocks);

            Assert.AreEqual(100, resultado);
        }

        [TestMethod]
        public void GestorPortes_CalcularBase_SinServirJunto_SobrePedidoRealSinStock_SiSeExcluye()
        {
            // La regla de #211 sigue viva con el campo correcto: producto sobre pedido de verdad
            // (EstadoProducto != 0) sin stock en el almacén → fuera de la base de esta entrega.
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 60, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = 0 },
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD2", PrecioUnitario = 40, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = 4 }
            };
            var stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.Stock("PROD2", "ALG")).Returns(0);

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas, servirJunto: false, stocks);

            Assert.AreEqual(60, resultado);
        }

        // Backstop del picking espejado en el PUT (#299 punto 2, caso real 922175: 231,04 € y se
        // le añadieron portes): si los productos del pedido llegan a IMPORTE_SIN_PORTES (150 €),
        // nunca hay portes, así que no se aplica ninguna exclusión por sobre pedido.
        [TestMethod]
        public void GestorPortes_CalcularBase_SinServirJunto_ImporteOriginalLlegaAlImporteSinPortes_CuentaTodo()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "37515", PrecioUnitario = 33.43M, Cantidad = 5, almacen = "ALG", estado = 1, EstadoProducto = 4 },
                new LineaPedidoVentaDTO { tipoLinea = 1, Producto = "PROD2", PrecioUnitario = 63.89M, Cantidad = 1, almacen = "ALG", estado = 1, EstadoProducto = 0 }
            };
            var stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.Stock("37515", "ALG")).Returns(2); // stock insuficiente (piden 5)

            decimal resultado = GestorPortes.CalcularBaseImponibleProductos(lineas, servirJunto: false, stocks);

            Assert.AreEqual(231.04M, resultado,
                "Con importe original >= IMPORTE_SIN_PORTES el picking nunca cobra portes; el PUT debe comportarse igual");
        }

        #endregion

        #region EsSobrePedidoParaPortes

        [TestMethod]
        public void GestorPortes_EsSobrePedidoParaPortes_Estado0_NuncaEsSobrePedido()
        {
            // Estado 0 = producto normal, nunca es sobre pedido independientemente del stock
            Assert.IsFalse(GestorPortes.EsSobrePedidoParaPortes(
                estadoProducto: 0, cantidadPedida: 100,
                stockDisponibleAlmacen: 0, stockDisponibleTodosAlmacenes: 0,
                servirJunto: true));
        }

        [TestMethod]
        public void GestorPortes_EsSobrePedidoParaPortes_Estado4_ConServirJunto_StockGlobalSuficiente_NoEsSobrePedido()
        {
            // Bug real: producto estado 4 (a extinguir), 2 unidades pedidas
            // Stock almacén Algete: 1 (insuficiente), stock global: 4 (suficiente)
            // Con servirJunto activo: debe mirar stock global → NO es sobre pedido
            //
            // Antes del fix, Nesto y NestoApp solo miraban cantidadDisponible (del almacén),
            // ignorando servirJunto y StockDisponibleTodosLosAlmacenes.
            // Eso hacía que la línea se excluyera de baseImponibleParaPortes,
            // cobrando portes indebidamente (80,96€ en vez de 156,96€).
            Assert.IsFalse(GestorPortes.EsSobrePedidoParaPortes(
                estadoProducto: 4, cantidadPedida: 2,
                stockDisponibleAlmacen: 1, stockDisponibleTodosAlmacenes: 4,
                servirJunto: true));
        }

        [TestMethod]
        public void GestorPortes_EsSobrePedidoParaPortes_Estado4_SinServirJunto_StockAlmacenInsuficiente_EsSobrePedido()
        {
            // Mismo caso pero sin servirJunto: debe mirar solo stock almacén → SÍ es sobre pedido
            Assert.IsTrue(GestorPortes.EsSobrePedidoParaPortes(
                estadoProducto: 4, cantidadPedida: 2,
                stockDisponibleAlmacen: 1, stockDisponibleTodosAlmacenes: 4,
                servirJunto: false));
        }

        [TestMethod]
        public void GestorPortes_EsSobrePedidoParaPortes_Estado4_SinServirJunto_StockAlmacenSuficiente_NoEsSobrePedido()
        {
            // Sin servirJunto pero el almacén tiene suficiente stock
            Assert.IsFalse(GestorPortes.EsSobrePedidoParaPortes(
                estadoProducto: 4, cantidadPedida: 2,
                stockDisponibleAlmacen: 3, stockDisponibleTodosAlmacenes: 5,
                servirJunto: false));
        }

        [TestMethod]
        public void GestorPortes_EsSobrePedidoParaPortes_Estado4_ConServirJunto_StockGlobalInsuficiente_EsSobrePedido()
        {
            // Ni siquiera el stock global es suficiente → sobre pedido en cualquier caso
            Assert.IsTrue(GestorPortes.EsSobrePedidoParaPortes(
                estadoProducto: 4, cantidadPedida: 5,
                stockDisponibleAlmacen: 1, stockDisponibleTodosAlmacenes: 3,
                servirJunto: true));
        }

        #endregion

        #region GestionarLineasPortes

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_NecesitaPortes_AnadeLinea()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 50, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                }
            };
            var resultado = new ResultadoPortes
            {
                ImportePortes = 3.5M,
                PortesGratis = false,
                CuentaPortes = "62400002"
            };

            var resultadoGestion = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);
            bool modificado = resultadoGestion.Modificado;

            Assert.IsTrue(modificado);
            Assert.AreEqual(2, lineas.Count);
        }

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_PortesGratis_NoAnadeLinea()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 200, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                }
            };
            var resultado = new ResultadoPortes
            {
                ImportePortes = 0,
                PortesGratis = true
            };

            var resultadoGestion = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);
            bool modificado = resultadoGestion.Modificado;

            Assert.IsFalse(modificado);
            Assert.AreEqual(1, lineas.Count);
        }

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_YaTienePortes_NoAnadeOtra()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 50, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                },
                new LineaPedidoVentaDTO
                {
                    id = 5, tipoLinea = 2, Producto = "62400002", PrecioUnitario = 3.5M, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                }
            };
            var resultado = new ResultadoPortes
            {
                ImportePortes = 3.5M,
                PortesGratis = false,
                CuentaPortes = "62400002"
            };

            var resultadoGestion = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);
            bool modificado = resultadoGestion.Modificado;

            Assert.IsFalse(modificado);
            Assert.AreEqual(2, lineas.Count);
        }

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_ConReembolso_AnadeLineaReembolso()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 50, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                }
            };
            var resultado = new ResultadoPortes
            {
                ImportePortes = 3.5M,
                PortesGratis = false,
                CuentaPortes = "62400002",
                EsContraReembolso = true,
                ComisionReembolso = 3M,
                CuentaReembolso = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL
            };

            var resultadoGestion = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);
            bool modificado = resultadoGestion.Modificado;

            Assert.IsTrue(modificado);
            Assert.AreEqual(3, lineas.Count); // producto + portes + reembolso
        }

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_LineaReembolsoExistenteYPortesGratis_NoSeElimina()
        {
            // Protege contra la colisión si la cuenta de reembolso empieza por 624:
            // el filtro lineaPortesExistente (StartsWith "624") podría confundir la línea
            // de reembolso con una de portes y, al ir PortesGratis=true, eliminarla.
            var lineaReembolso = new LineaPedidoVentaDTO
            {
                id = 0,
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "Comisión contra reembolso",
                PrecioUnitario = 3M,
                Cantidad = 1,
                almacen = "ALG",
                delegacion = "ALG",
                formaVenta = "EFC",
                estado = Constantes.EstadosLineaVenta.EN_CURSO,
                usuario = "test"
            };
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 500, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test"
                },
                lineaReembolso
            };
            var resultado = new ResultadoPortes
            {
                ImportePortes = 0,
                PortesGratis = true,
                EsContraReembolso = true,
                ComisionReembolso = 3M,
                CuentaReembolso = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL
            };

            GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

            Assert.IsTrue(lineas.Contains(lineaReembolso),
                "La línea de reembolso no debe eliminarse cuando los portes son gratis. " +
                "Si este test falla tras cambiar CUENTA_PORTES_VENTA_GENERAL a una cuenta 624xxx, " +
                "es que lineaPortesExistente (StartsWith \"624\") la ha confundido con una línea de portes.");
        }

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_LineaReembolsoExistenteYPortesCobrables_AnadePortesComoLineaSeparada()
        {
            // Si ya existe línea de reembolso y los portes NO son gratis, debe añadirse
            // una línea de portes aparte (total 3 líneas). Con la colisión 624 vs 624,
            // lineaPortesExistente encontraría la línea de reembolso, creería que ya hay
            // portes y no añadiría la nueva.
            var lineaReembolso = new LineaPedidoVentaDTO
            {
                id = 0,
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "Comisión contra reembolso",
                PrecioUnitario = 3M,
                Cantidad = 1,
                almacen = "ALG",
                delegacion = "ALG",
                formaVenta = "EFC",
                estado = Constantes.EstadosLineaVenta.EN_CURSO,
                usuario = "test"
            };
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 50, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                },
                lineaReembolso
            };
            var resultado = new ResultadoPortes
            {
                ImportePortes = 3.5M,
                PortesGratis = false,
                CuentaPortes = "62400002",
                EsContraReembolso = true,
                ComisionReembolso = 3M,
                CuentaReembolso = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL
            };

            GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

            Assert.AreEqual(3, lineas.Count,
                "Debe haber producto + reembolso existente + portes nueva. " +
                "Si este test falla tras cambiar a una cuenta 624xxx, es que GestionarLineasPortes " +
                "ha encontrado la reembolso al buscar lineaPortesExistente y no ha añadido los portes.");
            Assert.IsTrue(lineas.Any(l => l.Producto?.Trim() == "62400002"),
                "Debe haberse añadido la línea de portes con su cuenta específica");
            Assert.IsTrue(lineas.Contains(lineaReembolso),
                "La línea de reembolso original debe seguir ahí");
        }

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_Presupuesto_PortesEnEstadoPresupuesto()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 50, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = Constantes.EstadosLineaVenta.PRESUPUESTO, usuario = "test", fechaEntrega = System.DateTime.Today
                }
            };
            var resultado = new ResultadoPortes
            {
                ImportePortes = 3.5M,
                PortesGratis = false,
                CuentaPortes = "62400002"
            };

            var resultadoGestion = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);
            bool modificado = resultadoGestion.Modificado;

            Assert.IsTrue(modificado);
            var lineaPortes = lineas.Single(l => l.Producto == "62400002");
            Assert.AreEqual(Constantes.EstadosLineaVenta.PRESUPUESTO, lineaPortes.estado);
        }

        #endregion

        #region AnadirPortes

        [TestMethod]
        public void GestorPortes_AnadirPortesFalse_DevuelvePortesGratisSinImporte()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28001",
                Ruta = "FW",
                FormaPago = "EFC",
                CCC = null,
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 10,
                AnadirPortes = false
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis);
            Assert.AreEqual(0, resultado.ImportePortes);
            Assert.AreEqual(0, resultado.ImporteFaltaParaPortesGratis);
            Assert.AreEqual(0, resultado.ComisionReembolso);
        }

        [TestMethod]
        public void GestorPortes_AnadirPortesTrue_CalculaPortesNormalmente()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28001",
                Ruta = "FW",
                FormaPago = "EFC",
                CCC = null,
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 10,
                AnadirPortes = true
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(Constantes.Portes.PROVINCIAL, resultado.ImportePortes);
        }

        [TestMethod]
        public void GestorPortes_AnadirPortesPorDefectoEsTrue()
        {
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28001",
                Ruta = "FW",
                FormaPago = "EFC",
                CCC = null,
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 10
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(Constantes.Portes.PROVINCIAL, resultado.ImportePortes);
        }

        #endregion

        #region Escenarios integración POST/PUT

        [TestMethod]
        public void GestorPortes_PUT_SuperaMinimo_DebeQuitarPortesExistentesEnBD()
        {
            // BUG REAL (pedido 912479): POST crea pedido con 2 líneas (36.90+13.63=50.53€ < 75€)
            // → se añaden portes. Luego se añade otra línea (36.90€) → total 87.43€ > 75€.
            // Los portes (ya guardados en BD, id > 0) deberían eliminarse en el PUT.
            //
            // Este test simula el PUT donde las líneas de portes ya están en BD (id > 0).
            // GestionarLineasPortes solo borra líneas con id=0, así que el PUT del controller
            // tiene lógica inline para borrar las de BD. Aquí verificamos que CalcularPortes
            // devuelve PortesGratis=true para que el controller pueda actuar.
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28210", // Valdemorillo, Madrid
                Ruta = "FW",
                FormaPago = "EFC",
                PlazosPago = "CONTADO",
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 87.43M // 36.90 + 13.63 + 36.90
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis,
                "Con 87.43€ > 75€ umbral, PortesGratis debe ser true para que el PUT borre los portes de BD");
            Assert.AreEqual(0, resultado.ImportePortes,
                "No debe cobrar portes");
        }

        [TestMethod]
        public void GestorPortes_CalcularPortes_SinProductos_BaseImponibleCero_DebeIndicarPortesInnecesarios()
        {
            // BUG REAL (pedido 912267): El usuario borra todas las líneas de producto para
            // desactivar el pedido. Al quedar BaseImponible=0, CalcularPortes dice PortesGratis=false
            // (porque 0 < 75) y el PUT no borra los portes. El pedido se factura con solo portes.
            //
            // Comportamiento esperado: si no hay productos (base=0), los portes no tienen sentido.
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28210",
                Ruta = "FW",
                FormaPago = "RCB",
                PlazosPago = "1/7",
                CCC = "5",
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 0 // No hay productos
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.PortesGratis,
                "Sin productos (base=0), no se deben cobrar portes");
            Assert.AreEqual(Constantes.Portes.PROVINCIAL, resultado.ImportePortes,
                "Debe devolver el importe de portes aunque no los cobre, para que el cliente " +
                "pueda mostrarlo cuando se añadan productos y no se llegue al mínimo");
            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO, resultado.ImporteMinimoPedidoSinPortes,
                "El umbral debe calcularse aunque no haya productos (para que los clientes comparen localmente)");
            Assert.AreEqual(GestorImportesMinimos.IMPORTE_MINIMO, resultado.ImporteFaltaParaPortesGratis,
                "Falta el umbral completo porque base es 0");
        }

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_CanalExternoConPortes_NoDebeBorrarlos()
        {
            // BUG REAL (pedido 912302): Amazon envía un pedido con portes (cuenta 62400003).
            // En el POST, GestionarLineasPortes se llama con PortesGratis=true (canal externo).
            // Como la línea de portes tiene id=0 (nueva), el método la BORRA.
            // Los portes de Amazon se pierden.
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "41214", PrecioUnitario = 5.67M, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "STK",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                },
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "42269", PrecioUnitario = 5.66M, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "STK",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                },
                new LineaPedidoVentaDTO
                {
                    // Portes que vienen de Amazon (id=0 porque es línea nueva del POST)
                    id = 0,
                    tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                    Producto = "62400003",
                    PrecioUnitario = 3.99M,
                    Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "STK",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today,
                    texto = "Portes"
                }
            };

            // Para canal externo, CalcularPortes devuelve PortesGratis=true
            var resultado = new ResultadoPortes
            {
                PortesGratis = true,
                ImportePortes = 0
            };

            GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

            // BUG: GestionarLineasPortes borra la línea de portes de Amazon porque
            // tiene id=0 y el resultado dice PortesGratis=true (rama else if).
            Assert.AreEqual(3, lineas.Count,
                "Los portes de Amazon (canal externo) deben preservarse. " +
                "Actualmente GestionarLineasPortes los borra porque id=0 y PortesGratis=true.");
        }

        [TestMethod]
        public void GestorPortes_PedidoTarjetaPrepago_SiAñadePortes()
        {
            // Escenario 4: Pedido con FormaPago=TAR y PlazosPago=PRE.
            // Verifica que CalcularPortes SÍ añade portes para pedidos de tarjeta.
            // El bug real está en NestoApp (plantilla-venta.component.ts:464) que envía
            // el enlace de pago usando totalPedido del FRONTEND (sin portes) antes de que
            // el backend los añada en el POST. Pero al menos el backend los añade correctamente.
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 50, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                }
            };

            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = Constantes.FormasPago.TARJETA,
                PlazosPago = Constantes.PlazosPago.PREPAGO,
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.PortesGratis, "No llega al mínimo, debe cobrar portes");
            Assert.AreEqual(Constantes.Portes.PROVINCIAL, resultado.ImportePortes, "Portes provinciales");
            Assert.IsFalse(resultado.EsContraReembolso, "TAR no es contra reembolso");

            var resultadoGestion = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);
            bool modificado = resultadoGestion.Modificado;

            Assert.IsTrue(modificado, "Debe añadir línea de portes");
            Assert.AreEqual(2, lineas.Count, "1 producto + 1 portes");
            Assert.IsTrue(lineas.Any(l => l.Producto != null && l.Producto.StartsWith("624")),
                "El backend SÍ añade portes. El bug está en NestoApp que calcula el importe " +
                "del enlace de pago ANTES de crear el pedido (sin portes incluidos).");
        }

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_EliminaPortes_RegistraImporte()
        {
            // Línea de portes nueva (id=0) que se eliminará porque PortesGratis=true
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 200, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                },
                new LineaPedidoVentaDTO
                {
                    id = 0, tipoLinea = 2, Producto = "62400002", PrecioUnitario = 3.5M, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                }
            };
            var resultado = new ResultadoPortes { PortesGratis = true, ImportePortes = 0 };

            var resultadoGestion = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

            Assert.IsTrue(resultadoGestion.Modificado);
            Assert.AreEqual(3.5M, resultadoGestion.ImportePortesEliminados,
                "Debe registrar el importe de portes eliminados para auditoría");
            Assert.AreEqual(1, lineas.Count, "Solo debe quedar la línea de producto");
        }

        [TestMethod]
        public void GestorPortes_GestionarLineasPortes_NoEliminaPortes_ImporteEliminadosCero()
        {
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = 1, Producto = "PROD1", PrecioUnitario = 50, Cantidad = 1,
                    almacen = "ALG", delegacion = "ALG", formaVenta = "EFC",
                    estado = 1, usuario = "test", fechaEntrega = System.DateTime.Today
                }
            };
            var resultado = new ResultadoPortes { ImportePortes = 3.5M, PortesGratis = false };

            var resultadoGestion = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

            Assert.IsTrue(resultadoGestion.Modificado, "Debe añadir portes");
            Assert.AreEqual(0M, resultadoGestion.ImportePortesEliminados,
                "No se eliminaron portes, el importe debe ser 0");
        }

        #endregion

        #region Issue #159 - Flag NoCobrarComisionReembolso y corte 2026-09-01

        [TestCleanup]
        public void LimpiarOverridesReembolso()
        {
            GestorPortes.FechaActualParaPruebas = null;
            GestorPortes.IncrementoReembolsoParaPruebas = null;
        }

        [TestMethod]
        public void FlagNoCobrarReembolsoEsEfectivo_FechaAntesDelCorte_DevuelveTrue()
        {
            GestorPortes.FechaActualParaPruebas = new System.DateTime(2026, 8, 31, 23, 59, 59);

            Assert.IsTrue(GestorPortes.FlagNoCobrarReembolsoEsEfectivo(),
                "Antes del 2026-09-01 el flag debe poder usarse.");
        }

        [TestMethod]
        public void FlagNoCobrarReembolsoEsEfectivo_FechaExactaDelCorte_DevuelveFalse()
        {
            // A partir de FECHA_CORTE_NO_COBRAR_COMISION_REEMBOLSO (exclusiva del "antes")
            // el flag ya no tiene efecto.
            GestorPortes.FechaActualParaPruebas = new System.DateTime(2026, 9, 1, 0, 0, 0);

            Assert.IsFalse(GestorPortes.FlagNoCobrarReembolsoEsEfectivo());
        }

        [TestMethod]
        public void FlagNoCobrarReembolsoEsEfectivo_FechaPosteriorAlCorte_DevuelveFalse()
        {
            GestorPortes.FechaActualParaPruebas = new System.DateTime(2027, 1, 15);

            Assert.IsFalse(GestorPortes.FlagNoCobrarReembolsoEsEfectivo());
        }

        private static PedidoPortesInput InputReembolsoTest()
        {
            return new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = "EFC",
                PlazosPago = "CONTADO",
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50
            };
        }

        [TestMethod]
        public void CalcularPortes_ConIncrementoActivoYFlagFalse_CobraComisionReembolso()
        {
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;
            var input = InputReembolsoTest();
            input.NoCobrarComisionReembolso = false;

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.EsContraReembolso);
            Assert.AreEqual(3M, resultado.ComisionReembolso);
        }

        [TestMethod]
        public void CalcularPortes_ConFlagTrueAntesDelCorte_NoCobraComisionReembolso()
        {
            // Antes del corte, si el vendedor marca el flag, no se aplica la comisión
            // aunque la forma de pago sea contra reembolso e INCREMENTO > 0.
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;
            GestorPortes.FechaActualParaPruebas = new System.DateTime(2026, 4, 20);
            var input = InputReembolsoTest();
            input.NoCobrarComisionReembolso = true;

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.EsContraReembolso, "Sigue siendo contra reembolso");
            Assert.AreEqual(0M, resultado.ComisionReembolso,
                "El flag antes del corte debe anular el cobro de la comisión");
        }

        [TestMethod]
        public void CalcularPortes_ConFlagTrueDespuesDelCorte_SeIgnoraElFlagYSeCobra()
        {
            // A partir de 2026-09-01 el flag deja de tener efecto y siempre se cobra.
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;
            GestorPortes.FechaActualParaPruebas = new System.DateTime(2026, 9, 1);
            var input = InputReembolsoTest();
            input.NoCobrarComisionReembolso = true;

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.AreEqual(3M, resultado.ComisionReembolso,
                "Tras la fecha de corte el flag debe ignorarse");
        }

        [TestMethod]
        public void CalcularPortes_PagoConTarjetaYFlagTrue_NoCobraPorQueNoEsReembolso()
        {
            // El flag solo aplica si ya era contra reembolso. Si la forma de pago no lo es,
            // no hay comisión independientemente del flag.
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;
            var input = InputReembolsoTest();
            input.FormaPago = Constantes.FormasPago.TARJETA;
            input.NoCobrarComisionReembolso = true;

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.EsContraReembolso);
            Assert.AreEqual(0M, resultado.ComisionReembolso);
        }

        [TestMethod]
        public void CalcularPortes_ConIvaNull_TratarComoEfectivoContadoSinCCC()
        {
            // NestoAPI, al guardar un pedido con IVA=null, resetea CCC, FormaPago, PlazosPago
            // y PeriodoFacturacion a (null, EFC, CONTADO, NRM) para forzar contado. El cálculo
            // de portes debe hacer el mismo supuesto al recibir IVA=null, para que clientes
            // que envíen el pedido sin IVA todavía asignado (p. ej. NestoApp antes de tener
            // cliente completo) obtengan un resultado coherente con lo que se acabará guardando.
            // En concreto: aunque los otros campos digan que NO es contra reembolso, con IVA=null
            // debe tratarse como SÍ contra reembolso.
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                Iva = null,                 // ← clave del test
                // Valores que por sí solos descartarían contra reembolso:
                FormaPago = Constantes.FormasPago.TRANSFERENCIA,
                PlazosPago = Constantes.PlazosPago.PREPAGO,
                CCC = "1234",
                PeriodoFacturacion = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES,
                BaseImponibleProductos = 50
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.EsContraReembolso,
                "Con IVA=null el backend reseteará los otros campos a contado/efectivo/sin CCC, " +
                "así que CalcularPortes debe hacer el mismo supuesto y considerarlo contra reembolso.");
            Assert.AreEqual(3M, resultado.ComisionReembolso);
        }

        [TestMethod]
        public void CalcularPortes_IncrementoCero_NuncaCobraAunSinFlag()
        {
            // Estado actual de producción: INCREMENTO_REEMBOLSO = 0 → no se cobra nunca.
            // Este test documenta el comportamiento "fase de solo visibilidad".
            GestorPortes.IncrementoReembolsoParaPruebas = 0M;
            var input = InputReembolsoTest();
            input.NoCobrarComisionReembolso = false;

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.EsContraReembolso);
            Assert.AreEqual(0M, resultado.ComisionReembolso);
        }

        #endregion

        #region Issue #199 - EsContraReembolso es decisión de cabecera, no de líneas

        [TestMethod]
        public void CalcularPortes_BaseImponibleCero_CabeceraEsContraReembolso_FlagSiTrue()
        {
            // Pedido reproductor 1/916614: el producto está marcado como sobre-pedido
            // (EstadoProducto=1, LineaParcial=0), por lo que el endpoint GET excluye
            // su importe de BaseImponibleProductos. Antes del fix, la flag
            // EsContraReembolso quedaba en false por defecto y la UI ocultaba la casilla
            // "No cobrar comisión contra reembolso", aunque la cabecera EFC sin CCC sí
            // es contra reembolso y la línea de comisión existía en el pedido.
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = Constantes.FormasPago.EFECTIVO,
                PlazosPago = Constantes.PlazosPago.CONTADO,
                CCC = null,
                PeriodoFacturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 0
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.EsContraReembolso,
                "EsContraReembolso depende solo de la cabecera; con base 0 debe seguir siendo true.");
            Assert.AreEqual(0M, resultado.ComisionReembolso,
                "Sin productos no se cobra comisión; solo la flag refleja la cabecera.");
        }

        [TestMethod]
        public void CalcularPortes_AnadirPortesFalse_CabeceraEsContraReembolso_FlagSiTrue()
        {
            // Almacén REI/ALC desactiva AnadirPortes. La flag EsContraReembolso debe
            // seguir reflejando la cabecera (mismo razonamiento que el caso BaseImponible=0).
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = Constantes.FormasPago.EFECTIVO,
                PlazosPago = Constantes.PlazosPago.CONTADO,
                CCC = null,
                PeriodoFacturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50,
                AnadirPortes = false
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.EsContraReembolso);
            Assert.AreEqual(0M, resultado.ComisionReembolso,
                "Con AnadirPortes=false no se cobra comisión, pero la flag debe ir a true.");
        }

        [TestMethod]
        public void CalcularPortes_BaseImponibleCero_CabeceraNoEsReembolso_FlagSiFalse()
        {
            // Sanity: si la cabecera no es contra reembolso (TAR), la flag sigue en false
            // aunque movamos el cálculo arriba.
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = "FW",
                FormaPago = Constantes.FormasPago.TARJETA,
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 0
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsFalse(resultado.EsContraReembolso);
        }

        #endregion

        #region Bug — cliente con CCC pero cabecera EFC/CONTADO debe mostrar checkbox y limpiar correo

        [TestMethod]
        public void CalcularPortes_FormaPagoEFCConCccDelCliente_EsContraReembolsoSiTrue()
        {
            // Escenario reproductor: cliente con FP=RCB, plazos=1/30, CCC=1. En la plantilla
            // de ventas el usuario elige FP=EFC y plazos=CONTADO. La UI no limpia el CCC, así
            // que CalcularPortes recibe CCC="1". Al persistir, EFC.CCCObligatorio=false anula
            // el CCC en la cabecera y el pedido se cobra contra reembolso. CalcularPortes debe
            // reflejarlo para que la UI muestre la casilla "No cobrar comisión por reembolso".
            var input = new PedidoPortesInput
            {
                CodigoPostal = "28100",
                Ruta = Constantes.Pedidos.RUTA_AGENCIA_FW,
                FormaPago = Constantes.FormasPago.EFECTIVO,
                PlazosPago = Constantes.PlazosPago.CONTADO,
                CCC = "1",
                PeriodoFacturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                NotaEntrega = false,
                EsCanalExterno = false,
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = 50M
            };

            var resultado = GestorPortes.CalcularPortes(input);

            Assert.IsTrue(resultado.EsContraReembolso,
                "Con FP=EFC y plazos=CONTADO el pedido es contra reembolso aunque el CCC " +
                "venga informado del cliente: EFC.CCCObligatorio=false anula el CCC en cabecera.");
        }

        [TestMethod]
        public void GestionarLineasPortes_LineaReembolsoPersistidaSinComision_SeQuitaDelDTO()
        {
            // Bug: al modificar un pedido con NoCobrarComisionReembolso=true, CalcularPortes
            // devuelve ComisionReembolso=0. El PUT del controller quita la línea de reembolso
            // de la BD (cabPedidoVta.LinPedidoVtas) pero NO del DTO (pedido.Lineas), por lo que
            // el correo de modificación sigue mostrando la línea. GestionarLineasPortes debe
            // quitarla del DTO aunque la línea ya esté persistida (id != 0), igual que el
            // controller hace para la línea de portes.
            var lineaReembolsoPersistida = new LineaPedidoVentaDTO
            {
                id = 12345,
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "Comisión contra reembolso",
                PrecioUnitario = 3M,
                Cantidad = 1,
                almacen = "ALG",
                delegacion = "ALG",
                formaVenta = "EFC",
                estado = Constantes.EstadosLineaVenta.EN_CURSO,
                usuario = "test"
            };
            var lineas = new HashSet<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO
                {
                    tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                    Producto = "PROD1",
                    PrecioUnitario = 50M,
                    Cantidad = 1,
                    almacen = "ALG",
                    delegacion = "ALG",
                    formaVenta = "EFC",
                    estado = Constantes.EstadosLineaVenta.EN_CURSO,
                    usuario = "test"
                },
                lineaReembolsoPersistida
            };
            var resultado = new ResultadoPortes
            {
                EsContraReembolso = true,
                ComisionReembolso = 0M,
                PortesGratis = true
            };

            GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

            Assert.IsFalse(lineas.Contains(lineaReembolsoPersistida),
                "La línea de reembolso persistida debe quitarse del DTO cuando ComisionReembolso=0, " +
                "para que el correo de modificación del pedido no la siga mostrando.");
        }

        #endregion

        #region EsComisionReembolsoViva (NestoAPI#335)

        private static LinPedidoVta LineaEntidad(byte? tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
            string producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
            string texto = "Comisión contra reembolso", short estado = Constantes.EstadosLineaVenta.EN_CURSO,
            int picking = 0)
        {
            return new LinPedidoVta
            {
                TipoLinea = tipoLinea,
                Producto = producto,
                Texto = texto,
                Estado = estado,
                Picking = picking
            };
        }

        [TestMethod]
        public void EsComisionReembolsoViva_ComisionPendienteSinPicking_LaDetecta()
        {
            Assert.IsTrue(GestorPortes.EsComisionReembolsoViva(
                LineaEntidad(estado: Constantes.EstadosLineaVenta.PENDIENTE)));
        }

        [TestMethod]
        public void EsComisionReembolsoViva_ComisionReservadaPorPicking_TambienLaDetecta()
        {
            // NestoAPI#335: el caso de los pedidos 920278 y 922604. La versión anterior exigía
            // Picking == 0 al borrar, así que la comisión reservada quedaba viva (y el cliente
            // habría pagado una comisión que ya no procede si nadie la quitaba a mano).
            Assert.IsTrue(GestorPortes.EsComisionReembolsoViva(
                LineaEntidad(picking: 98982)));
        }

        [TestMethod]
        public void EsComisionReembolsoViva_TextoEnMayusculas_LaDetecta()
        {
            Assert.IsTrue(GestorPortes.EsComisionReembolsoViva(
                LineaEntidad(texto: "COMISIÓN CONTRA REEMBOLSO")));
        }

        [TestMethod]
        public void EsComisionReembolsoViva_LineaDePortesSinReembolso_NoEsComision()
        {
            // Los portes también van a cuenta 624xxx: solo el texto los distingue (issue #159)
            Assert.IsFalse(GestorPortes.EsComisionReembolsoViva(
                LineaEntidad(texto: "Portes")));
        }

        [TestMethod]
        public void EsComisionReembolsoViva_ComisionYaFacturada_NoEstaViva()
        {
            Assert.IsFalse(GestorPortes.EsComisionReembolsoViva(
                LineaEntidad(estado: Constantes.EstadosLineaVenta.FACTURA)));
        }

        [TestMethod]
        public void EsComisionReembolsoViva_LineaDeProducto_NoEsComision()
        {
            Assert.IsFalse(GestorPortes.EsComisionReembolsoViva(
                LineaEntidad(tipoLinea: Constantes.TiposLineaVenta.PRODUCTO, producto: "23130")));
        }

        #endregion
    }
}
