using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.ValidadoresPedido
{
    /// <summary>
    /// Tests para ValidadorGanavisiones.
    /// Issue #94: Sistema Ganavisiones
    ///
    /// Reglas:
    /// - 1 Ganavision = 10 EUR de base bonificable
    /// - Los Ganavisiones se generan de lineas de grupos COS, ACC, PEL
    /// - Los productos con Ganavisiones configurados pueden bonificarse si hay suficientes
    /// </summary>
    [TestClass]
    public class ValidadorGanavisionesTests
    {
        private IServicioPrecios _servicioPrecios;
        private ValidadorGanavisiones _validador;

        [TestInitialize]
        public void Setup()
        {
            _servicioPrecios = A.Fake<IServicioPrecios>();
            _validador = new ValidadorGanavisiones();
        }

        [TestMethod]
        public void EsPedidoValido_ProductoSinGanavisiones_NoAplica()
        {
            // Arrange
            var pedido = CrearPedidoBasico(100m);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD01")).Returns(null);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "PROD01", _servicioPrecios);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada,
                "Si el producto no tiene Ganavisiones configurados, el validador no debe aceptarlo");
        }

        [TestMethod]
        public void EsPedidoValido_ProductoConGanavisiones_SuficientesDisponibles_Valido()
        {
            // Arrange - Pedido de 100 EUR en cosmetica genera 10 Ganavisiones
            // Producto bonificado tiene 5 Ganavisiones (suficientes)
            var pedido = CrearPedidoConBonificado(
                importeLineasBonificables: 100m,
                grupoBonificable: Constantes.Productos.GRUPO_COSMETICA,
                productoBonificado: "REGALO01");

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert
            Assert.IsTrue(resultado.ValidacionSuperada,
                "100 EUR / 10 = 10 Ganavisiones disponibles, producto requiere 5: debe ser valido");
        }

        [TestMethod]
        public void EsPedidoValido_ProductoConGanavisiones_InsuficientesDisponibles_Invalido()
        {
            // Arrange - Pedido de 50 EUR en cosmetica genera 5 Ganavisiones
            // Producto bonificado tiene 10 Ganavisiones (insuficientes)
            var pedido = CrearPedidoConBonificado(
                importeLineasBonificables: 50m,
                grupoBonificable: Constantes.Productos.GRUPO_COSMETICA,
                productoBonificado: "REGALO01");

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(10);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada,
                "50 EUR / 10 = 5 Ganavisiones disponibles, producto requiere 10: debe ser invalido");
            Assert.IsTrue(resultado.Motivo.Contains("solo hay 5 disponibles"),
                $"El motivo deberia mencionar los Ganavisiones disponibles. Motivo actual: {resultado.Motivo}");
        }

        [TestMethod]
        public void EsPedidoValido_GanavisionesExactos_Valido()
        {
            // Arrange - Pedido de 100 EUR genera 10 Ganavisiones
            // Producto bonificado tiene exactamente 10 Ganavisiones
            var pedido = CrearPedidoConBonificado(
                importeLineasBonificables: 100m,
                grupoBonificable: Constantes.Productos.GRUPO_COSMETICA,
                productoBonificado: "REGALO01");

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(10);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert
            Assert.IsTrue(resultado.ValidacionSuperada,
                "Si hay exactamente los Ganavisiones necesarios, debe ser valido");
        }

        [TestMethod]
        public void EsPedidoValido_VariosBonificados_SumaGanavisionesConsumidos()
        {
            // Arrange - Pedido de 100 EUR genera 10 Ganavisiones
            // Dos productos bonificados de 6 Ganavisiones cada uno (12 total, excede)
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD01",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m // Bonificado
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO02",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m // Bonificado
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(6);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO02")).Returns(6);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada,
                "10 Ganavisiones disponibles, se consumen 12 (6+6): debe ser invalido");
        }

        [TestMethod]
        public void EsPedidoValido_CantidadMultiple_MultiplicaGanavisiones()
        {
            // Arrange - Pedido de 100 EUR genera 10 Ganavisiones
            // Producto bonificado de 3 Ganavisiones x 4 unidades = 12 (excede)
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD01",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 4,
                        PrecioUnitario = 0m // Bonificado
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(3);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada,
                "10 Ganavisiones disponibles, se consumen 12 (3x4): debe ser invalido");
        }

        [TestMethod]
        public void EsPedidoValido_GruposNoBonificables_NoGeneranGanavisiones()
        {
            // Arrange - Pedido de 100 EUR en Aparatos (no bonificable) no genera Ganavisiones
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "APARATO01",
                        GrupoProducto = Constantes.Productos.GRUPO_APARATOS, // No bonificable
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("APARATO01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada,
                "Aparatos no generan Ganavisiones, no deberia poder bonificar");
        }

        [TestMethod]
        public void EsPedidoValido_TodosGruposBonificables_GeneranGanavisiones()
        {
            // Arrange - Pedido con COS (30 EUR), ACC (40 EUR), PEL (30 EUR) = 100 EUR = 10 Ganavisiones
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "COS01",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 30m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "ACC01",
                        GrupoProducto = Constantes.Productos.GRUPO_ACCESORIOS,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 40m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PEL01",
                        GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 30m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("COS01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("ACC01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PEL01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(10);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert
            Assert.IsTrue(resultado.ValidacionSuperada,
                "COS+ACC+PEL = 100 EUR = 10 Ganavisiones, producto necesita 10: debe ser valido");
        }

        [TestMethod]
        public void EsPedidoValido_ImporteFraccionado_TruncaGanavisiones()
        {
            // Arrange - Pedido de 19.99 EUR genera 1 Ganavision (truncado, no redondeado)
            var pedido = CrearPedidoConBonificado(
                importeLineasBonificables: 19.99m,
                grupoBonificable: Constantes.Productos.GRUPO_COSMETICA,
                productoBonificado: "REGALO01");

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(2);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada,
                "19.99 EUR / 10 = 1 Ganavision (truncado), producto necesita 2: debe ser invalido");
        }

        [TestMethod]
        public void EsPedidoValido_ServicioNulo_RetornaInvalido()
        {
            // Arrange
            var pedido = CrearPedidoBasico(100m);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "PROD01", null);

            // Assert
            Assert.IsFalse(resultado.ValidacionSuperada);
            Assert.IsTrue(resultado.Motivo.Contains("servicio"));
        }

        [TestMethod]
        public void EsPedidoValido_ProductoNoBonificadoConPrecio_NoConsumeGanavisiones()
        {
            // Arrange - Producto con Ganavisiones pero que NO esta bonificado (tiene precio > 0)
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD01",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 15m // Tiene precio, no esta bonificado
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert
            // El producto tiene Ganavisiones pero no esta bonificado (precio > 0)
            // Este validador solo valida productos a precio 0
            Assert.IsTrue(resultado.ValidacionSuperada,
                "El producto no esta bonificado (precio > 0), no consume Ganavisiones");
        }

        #region Helpers

        private PedidoVentaDTO CrearPedidoBasico(decimal importe)
        {
            return new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD01",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = importe
                    }
                }
            };
        }

        private PedidoVentaDTO CrearPedidoConBonificado(decimal importeLineasBonificables, string grupoBonificable, string productoBonificado)
        {
            return new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD01",
                        GrupoProducto = grupoBonificable,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = importeLineasBonificables
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = productoBonificado,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m // Bonificado
                    }
                }
            };
        }

        #endregion
    }
}
