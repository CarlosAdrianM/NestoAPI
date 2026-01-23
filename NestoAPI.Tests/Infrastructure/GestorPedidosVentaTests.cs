using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System.Collections.Generic;

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
    }
}
