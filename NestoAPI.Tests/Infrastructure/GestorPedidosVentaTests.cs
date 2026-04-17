using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorPedidosVentaTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 12345;

        #region ImporteReembolso - Issue #250

        [TestMethod]
        public void ImporteReembolso_ConEfectosManualesEFC_DevuelveSumaEfectosEFC()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "TRN" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Efectos manuales: 909.75 TRN + 499.80 EFC (caso real del issue)
            var efectos = new List<EfectoPedidoVenta>
            {
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 909.75m, FormaPago = "TRN" },
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 499.80m, FormaPago = Constantes.FormasPago.EFECTIVO }
            };
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(efectos);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert - Solo debe devolver el importe de los efectos con EFC
            Assert.AreEqual(499.80m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_ConEfectosManualesSinEFC_DevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "EFC" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Efectos manuales: todos son TRN, ninguno EFC
            var efectos = new List<EfectoPedidoVenta>
            {
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 500m, FormaPago = "TRN" },
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 300m, FormaPago = "RCB" }
            };
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(efectos);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert - Aunque la cabecera diga EFC, si hay efectos manuales sin EFC, devuelve 0
            Assert.AreEqual(0m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_SinEfectosManualesYFormaPagoEFC_DevuelveTotalLineas()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "EFC" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Sin efectos manuales
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(new List<EfectoPedidoVenta>());

            // Lineas con picking
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Total = 100m },
                new LinPedidoVta { Total = 50m }
            };
            A.CallTo(() => servicio.CargarLineasPedidoSinPicking(PEDIDO)).Returns(lineas);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert - Comportamiento original: suma las lineas
            Assert.AreEqual(150m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_SinEfectosManualesYFormaPagoTRN_DevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "TRN" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Sin efectos manuales
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(new List<EfectoPedidoVenta>());

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert - Comportamiento original: TRN = sin reembolso
            Assert.AreEqual(0m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_ConMultiplesEfectosEFC_DevuelveSumaTotal()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            var cabecera = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Forma_Pago = "TRN" };
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);

            // Multiples efectos EFC
            var efectos = new List<EfectoPedidoVenta>
            {
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 100m, FormaPago = Constantes.FormasPago.EFECTIVO },
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 200m, FormaPago = Constantes.FormasPago.EFECTIVO },
                new EfectoPedidoVenta { Empresa = EMPRESA, Pedido = PEDIDO, Importe = 50m, FormaPago = "TRN" }
            };
            A.CallTo(() => servicio.CargarEfectosPedido(EMPRESA, PEDIDO)).Returns(efectos);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert
            Assert.AreEqual(300m, resultado);
        }

        [TestMethod]
        public void ImporteReembolso_PedidoNoExiste_DevuelveCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);

            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns((CabPedidoVta)null);

            // Act
            decimal resultado = gestor.ImporteReembolso(EMPRESA, PEDIDO);

            // Assert
            Assert.AreEqual(0m, resultado);
        }

        #endregion

        #region EsLineaPortesOReembolso - Issue #346

        [TestMethod]
        public void EsLineaPortesOReembolso_LineaDeProducto_DevuelveFalse()
        {
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AB01"
            };

            Assert.IsFalse(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaContable624_DevuelveTrue()
        {
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "62400017"
            };

            Assert.IsTrue(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaContable624ConEspacios_DevuelveTrue()
        {
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "  62400005   "
            };

            Assert.IsTrue(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaReembolsoConTextoReembolso_DevuelveTrue()
        {
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "Comisión contra reembolso"
            };

            Assert.IsTrue(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaReembolsoSinTextoReembolso_DevuelveFalse()
        {
            // La cuenta 75900000 también se usa para otras cuentas contables (no solo reembolso).
            // Solo debe tratarse como portes/reembolso si el texto contiene "reembolso".
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "Otra cosa"
            };

            Assert.IsFalse(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_CuentaContable624PeroTipoLineaProducto_DevuelveFalse()
        {
            // Protege de un caso raro: si alguien pone producto 624xxx en tipoLinea PRODUCTO
            // no queremos que se considere portes.
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "62400017"
            };

            Assert.IsFalse(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_ProductoNull_DevuelveFalse()
        {
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = null
            };

            Assert.IsFalse(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        [TestMethod]
        public void EsLineaPortesOReembolso_TextoReembolsoMayusculas_DevuelveTrue()
        {
            // La detección de "reembolso" debe ser case-insensitive.
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "COMISION REEMBOLSO"
            };

            Assert.IsTrue(GestorPedidosVenta.EsLineaPortesOReembolso(linea));
        }

        #endregion

        #region MoverLineasAmpliacionAPedidoOriginal - Issue #346

        private static LineaPedidoVentaDTO NuevaLineaProducto(string producto, short estado = Constantes.EstadosLineaVenta.PENDIENTE, int id = 0)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                estado = estado
            };
        }

        private static LineaPedidoVentaDTO NuevaLineaPortes(string cuenta = "62400017", short estado = Constantes.EstadosLineaVenta.EN_CURSO, int id = 100)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = cuenta,
                estado = estado
            };
        }

        private static LineaPedidoVentaDTO NuevaLineaReembolso(short estado = Constantes.EstadosLineaVenta.EN_CURSO, int id = 200)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL,
                texto = "Comisión contra reembolso",
                estado = estado
            };
        }

        private static PedidoVentaDTO NuevoPedidoDto(params LineaPedidoVentaDTO[] lineas)
        {
            return new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>(lineas)
            };
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineasDeProducto_SeMuevenAlOriginal()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A"));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B"), NuevaLineaProducto("C"));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            Assert.AreEqual(3, original.Lineas.Count);
            Assert.AreEqual(0, ampliacion.Lineas.Count);
            CollectionAssert.AreEquivalent(
                new[] { "A", "B", "C" },
                original.Lineas.Select(l => l.Producto).ToList());
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineaPortesEnAmpliacion_NoSeMueve()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A"), NuevaLineaPortes("62400005"));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B"), NuevaLineaPortes("62400017"));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            // Solo la línea de producto B se ha movido; la de portes del ampliación se ha descartado.
            int lineasDePortesEnOriginal = original.Lineas.Count(l => l.Producto.StartsWith("624"));
            Assert.AreEqual(1, lineasDePortesEnOriginal, "El pedido original no debe quedar con portes duplicados");
            Assert.IsTrue(original.Lineas.Any(l => l.Producto == "B"));
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineaReembolsoEnAmpliacion_NoSeMueve()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A"));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B"), NuevaLineaReembolso());

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            Assert.IsFalse(original.Lineas.Any(l => l.texto?.ToLower().Contains("reembolso") == true));
            Assert.AreEqual(2, original.Lineas.Count);
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineasEnEstadoFacturado_NoSeMueven()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A"));
            var ampliacion = NuevoPedidoDto(
                NuevaLineaProducto("B", estado: Constantes.EstadosLineaVenta.PENDIENTE),
                NuevaLineaProducto("C", estado: Constantes.EstadosLineaVenta.FACTURA));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            Assert.IsTrue(original.Lineas.Any(l => l.Producto == "B"));
            Assert.IsFalse(original.Lineas.Any(l => l.Producto == "C"));
            Assert.IsTrue(ampliacion.Lineas.Any(l => l.Producto == "C"));
        }

        [TestMethod]
        public void MoverLineasAmpliacion_OriginalEsPresupuesto_LineasMovidasQuedanEnPresupuesto()
        {
            var original = NuevoPedidoDto(NuevaLineaProducto("A", estado: Constantes.EstadosLineaVenta.PRESUPUESTO));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B", estado: Constantes.EstadosLineaVenta.PENDIENTE));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            var lineaMovida = original.Lineas.Single(l => l.Producto == "B");
            Assert.AreEqual(Constantes.EstadosLineaVenta.PRESUPUESTO, lineaMovida.estado);
        }

        [TestMethod]
        public void MoverLineasAmpliacion_LineaMovida_PierdeId()
        {
            // Las líneas movidas deben insertarse como nuevas en el pedido original,
            // así que su id debe resetearse a 0 antes del save.
            var original = NuevoPedidoDto(NuevaLineaProducto("A", id: 1));
            var ampliacion = NuevoPedidoDto(NuevaLineaProducto("B", id: 42));

            GestorPedidosVenta.MoverLineasAmpliacionAPedidoOriginal(original, ampliacion);

            var lineaMovida = original.Lineas.Single(l => l.Producto == "B");
            Assert.AreEqual(0, lineaMovida.id);
        }

        #endregion
    }
}
