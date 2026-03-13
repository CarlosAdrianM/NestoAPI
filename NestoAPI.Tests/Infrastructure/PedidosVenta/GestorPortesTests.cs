using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System.Collections.Generic;

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
                new LineaPedidoVentaDTO { tipoLinea = 2, Producto = "75900000", PrecioUnitario = 3, Cantidad = 1 }
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

            bool modificado = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

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

            bool modificado = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

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

            bool modificado = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

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
                CuentaReembolso = "75900000"
            };

            bool modificado = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

            Assert.IsTrue(modificado);
            Assert.AreEqual(3, lineas.Count); // producto + portes + reembolso
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
    }
}
