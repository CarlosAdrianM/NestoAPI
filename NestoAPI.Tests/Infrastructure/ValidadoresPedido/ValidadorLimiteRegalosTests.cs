using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.ValidadoresPedido
{
    [TestClass]
    public class ValidadorLimiteRegalosTests
    {
        private IServicioPrecios _servicioPrecios;
        private ValidadorLimiteRegalos _validador;

        [TestInitialize]
        public void Setup()
        {
            _servicioPrecios = A.Fake<IServicioPrecios>();
            _validador = new ValidadorLimiteRegalos();
        }

        [TestMethod]
        public void EsPedidoValido_PedidoSinRegalos_RetornaValido()
        {
            // Arrange
            var pedido = CrearPedidoConProductosNormales(100m);
            ConfigurarProductoNormal("PROD01");

            // Act
            var resultado = _validador.EsPedidoValido(pedido, _servicioPrecios);

            // Assert
            Assert.IsTrue(resultado.ValidacionSuperada);
        }

        [TestMethod]
        public void EsPedidoValido_RegalosExactamente10Porciento_RetornaValido()
        {
            // Arrange - Pedido de 100€ con regalo de 10€ (exactamente 10%)
            var pedido = CrearPedidoConRegalo(importePedido: 100m, precioTarifaRegalo: 10m);
            ConfigurarProductoNormal("PROD01");
            ConfigurarProductoRegalo("REGALO01");

            // Act
            var resultado = _validador.EsPedidoValido(pedido, _servicioPrecios);

            // Assert
            Assert.IsTrue(resultado.ValidacionSuperada,
                "El pedido debería ser válido cuando los regalos son exactamente el 10%");
        }

        [TestMethod]
        public void EsPedidoValido_RegalosPorDebajoDelLimite_RetornaValido()
        {
            // Arrange - Pedido de 100€ con regalo de 5€ (5%, bajo el límite)
            var pedido = CrearPedidoConRegalo(importePedido: 100m, precioTarifaRegalo: 5m);
            ConfigurarProductoNormal("PROD01");
            ConfigurarProductoRegalo("REGALO01");

            // Act
            var resultado = _validador.EsPedidoValido(pedido, _servicioPrecios);

            // Assert
            Assert.IsTrue(resultado.ValidacionSuperada);
        }

        [TestMethod]
        public void EsPedidoValido_RegalosExcedenlimite_RetornaInvalido()
        {
            // Arrange - Pedido de 100€ con regalo de 15€ (15%, excede el 10%)
            var pedido = CrearPedidoConRegalo(importePedido: 100m, precioTarifaRegalo: 15m);
            ConfigurarProductoNormal("PROD01");
            ConfigurarProductoRegalo("REGALO01");

            // Act
            var resultado = _validador.EsPedidoValido(pedido, _servicioPrecios);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada);
            Assert.IsNotNull(resultado.Errores);
            Assert.IsTrue(resultado.Errores.Count > 0);
        }

        [TestMethod]
        public void EsPedidoValido_PedidoSinProductosNormales_RetornaInvalido()
        {
            // Arrange - Solo tiene regalos, sin productos normales
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        precioTarifa = 10m,
                        PrecioUnitario = 0m // Regalo a precio 0
                    }
                }
            };
            ConfigurarProductoRegalo("REGALO01");

            // Act
            var resultado = _validador.EsPedidoValido(pedido, _servicioPrecios);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada);
            Assert.IsTrue(resultado.Motivo.Contains("sin productos"));
        }

        [TestMethod]
        public void EsPedidoValido_PedidoNulo_RetornaValido()
        {
            // Act
            var resultado = _validador.EsPedidoValido(null, _servicioPrecios);

            // Assert
            Assert.IsTrue(resultado.ValidacionSuperada);
        }

        [TestMethod]
        public void EsPedidoValido_PedidoSinLineas_RetornaValido()
        {
            // Arrange
            var pedido = new PedidoVentaDTO { Lineas = null };

            // Act
            var resultado = _validador.EsPedidoValido(pedido, _servicioPrecios);

            // Assert
            Assert.IsTrue(resultado.ValidacionSuperada);
        }

        [TestMethod]
        public void EsPedidoValido_VariosRegalosExcedenLimite_RetornaTodosLosErrores()
        {
            // Arrange - Pedido de 100€ con 2 regalos de 8€ cada uno (16%, excede)
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        precioTarifa = 8m,
                        PrecioUnitario = 0m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO02",
                        tipoLinea = 1,
                        Cantidad = 1,
                        precioTarifa = 8m,
                        PrecioUnitario = 0m
                    }
                }
            };
            ConfigurarProductoNormal("PROD01");
            ConfigurarProductoRegalo("REGALO01");
            ConfigurarProductoRegalo("REGALO02");

            // Act
            var resultado = _validador.EsPedidoValido(pedido, _servicioPrecios);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada);
            Assert.AreEqual(2, resultado.Errores.Count, "Debería reportar error para cada producto regalo");
        }

        [TestMethod]
        public void EsPedidoValido_LineaTipoTexto_SeIgnora()
        {
            // Arrange - Línea de tipo texto (tipoLinea != 1) no debe afectar
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "TEXTO",
                        tipoLinea = 2, // Tipo texto
                        Cantidad = 1
                    }
                }
            };
            ConfigurarProductoNormal("PROD01");

            // Act
            var resultado = _validador.EsPedidoValido(pedido, _servicioPrecios);

            // Assert
            Assert.IsTrue(resultado.ValidacionSuperada);
        }

        #region Helpers

        private PedidoVentaDTO CrearPedidoConProductosNormales(decimal importe)
        {
            return new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = importe
                    }
                }
            };
        }

        private PedidoVentaDTO CrearPedidoConRegalo(decimal importePedido, decimal precioTarifaRegalo)
        {
            return new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = importePedido
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        precioTarifa = precioTarifaRegalo,
                        PrecioUnitario = 0m // Regalo a precio 0
                    }
                }
            };
        }

        private void ConfigurarProductoNormal(string numero)
        {
            A.CallTo(() => _servicioPrecios.BuscarProducto(numero))
                .Returns(new Producto
                {
                    Número = numero,
                    Ficticio = false,
                    Familia = "Normal"
                });
        }

        private void ConfigurarProductoRegalo(string numero)
        {
            A.CallTo(() => _servicioPrecios.BuscarProducto(numero))
                .Returns(new Producto
                {
                    Número = numero,
                    Ficticio = true,
                    Familia = Constantes.Productos.FAMILIA_BONIFICACION
                });
        }

        #endregion
    }
}
