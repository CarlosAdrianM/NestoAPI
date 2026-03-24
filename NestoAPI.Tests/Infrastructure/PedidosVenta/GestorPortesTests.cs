using Microsoft.VisualStudio.TestTools.UnitTesting;
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

            bool modificado = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

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

            bool modificado = GestorPortes.GestionarLineasPortes(lineas, resultado, "G21", null);

            Assert.IsTrue(modificado, "Debe añadir línea de portes");
            Assert.AreEqual(2, lineas.Count, "1 producto + 1 portes");
            Assert.IsTrue(lineas.Any(l => l.Producto != null && l.Producto.StartsWith("624")),
                "El backend SÍ añade portes. El bug está en NestoApp que calcula el importe " +
                "del enlace de pago ANTES de crear el pedido (sin portes incluidos).");
        }

        #endregion
    }
}
