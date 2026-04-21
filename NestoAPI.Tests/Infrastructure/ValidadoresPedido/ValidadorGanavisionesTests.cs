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
            // Por defecto, stock suficiente para que los tests existentes no fallen
            A.CallTo(() => _servicioPrecios.BuscarStockDisponibleTotal(A<string>.Ignored)).Returns(999);
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

        #region Bug: Oferta 5+5 confunde Ganavisiones (Issue #106)

        [TestMethod]
        public void EsPedidoValido_Oferta5Mas5_NoDebeConsumirGanavisiones()
        {
            // Bug: Un producto con oferta 5+5 tiene una línea gratis (BaseImponible=0).
            // Si ese producto también tiene Ganavisiones configurados, el validador
            // cuenta erróneamente esa línea gratis como consumo de Ganavisiones.
            //
            // Caso real: pedido con 230 EUR bonificables (23 Ganavisiones disponibles),
            // productos 39828 (10 GV) + 43141 (11 GV) = 21 GV consumidos (OK),
            // pero producto 44702 en oferta 5+5 tiene 5 uds gratis con oferta=1
            // y el validador las cuenta como 5 GV más → 26 > 23 → falla.
            //
            // Base bonificable: PROD_COS (180) + 44702 cobrada (5*10=50) = 230 EUR = 23 GV
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    // 180 EUR COS
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD_COS",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 180m
                    },
                    // Ganavisiones reales: 39828 (10 GV) y 43141 (11 GV) = 21 GV
                    new LineaPedidoVentaDTO
                    {
                        Producto = "39828",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m, // Bonificado por Ganavisiones
                        oferta = null // SIN oferta → es Ganavision real
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "43141",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m, // Bonificado por Ganavisiones
                        oferta = null // SIN oferta → es Ganavision real
                    },
                    // Oferta 5+5 del producto 44702 (NO es Ganavision, es oferta combinada)
                    // 5 uds cobradas a 10 EUR = 50 EUR COS (contribuye a base bonificable)
                    new LineaPedidoVentaDTO
                    {
                        Producto = "44702",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 5,
                        PrecioUnitario = 10m,
                        oferta = 1 // Tiene número de oferta
                    },
                    // 5 uds gratis de la oferta → BaseImponible=0, pero tiene oferta=1
                    new LineaPedidoVentaDTO
                    {
                        Producto = "44702",
                        tipoLinea = 1,
                        Cantidad = 5,
                        PrecioUnitario = 0m,
                        oferta = 1 // Tiene número de oferta → NO es Ganavision
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD_COS")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("39828")).Returns(10);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("43141")).Returns(11);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("44702")).Returns(1); // También tiene GV configurados

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "39828", _servicioPrecios);

            // Assert - Base bonificable: 180+50 = 230 EUR → 23 GV disponibles
            // Consumidos reales: 39828(10) + 43141(11) = 21 GV, debe ser válido
            // Bug actual: también cuenta 44702 gratis (5*1=5) → 26 > 23 → falla
            Assert.IsTrue(resultado.ValidacionSuperada,
                $"La línea gratis de la oferta 5+5 (44702, oferta=1) NO debe contar como Ganavision. " +
                $"Resultado: {resultado.Motivo}");
        }

        [TestMethod]
        public void EsPedidoValido_LineaSinOferta_SiConsumeGanavisiones()
        {
            // Contrapartida: una línea gratis SIN oferta SÍ debe contar como Ganavision
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD_COS",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m // 10 GV disponibles
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m,
                        oferta = null // Sin oferta → es Ganavision real
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD_COS")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsTrue(resultado.ValidacionSuperada,
                "Línea sin oferta con GV configurados SÍ debe consumir Ganavisiones (5 consumidos, 10 disponibles)");
        }

        [TestMethod]
        public void EsPedidoValido_LineaConOfertaCero_SiConsumeGanavisiones()
        {
            // oferta=0 se trata como null (sin oferta), así que SÍ consume Ganavisiones
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD_COS",
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
                        PrecioUnitario = 0m,
                        oferta = 0 // oferta=0 equivale a sin oferta
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD_COS")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsTrue(resultado.ValidacionSuperada,
                "oferta=0 equivale a sin oferta, debe contar como Ganavision (5 consumidos, 10 disponibles)");
        }

        [TestMethod]
        public void EsPedidoValido_MensajeError_IncluyeDetalleGanavisionesConsumidos()
        {
            // Cuando faltan Ganavisiones, el mensaje debe indicar claramente
            // cuántos se consumen y cuántos hay disponibles
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD_COS",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 50m // 5 GV disponibles
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m,
                        oferta = null
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD_COS")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(10);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsFalse(resultado.ValidacionSuperada);
            // El mensaje debe incluir: producto, consumidos, disponibles
            Assert.IsTrue(resultado.Motivo.Contains("REGALO01"),
                $"El motivo debe mencionar el producto. Motivo: {resultado.Motivo}");
            Assert.IsTrue(resultado.Motivo.Contains("10") && resultado.Motivo.Contains("5"),
                $"El motivo debe incluir consumidos (10) y disponibles (5). Motivo: {resultado.Motivo}");
        }

        [TestMethod]
        public void EsPedidoValido_MultipleOfertasYGanavisiones_SoloContabilizaGanavisionesReales()
        {
            // Escenario complejo: mezcla de ofertas combinadas y Ganavisiones reales
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    // Base bonificable: 200 EUR COS = 20 GV disponibles
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD_COS",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 200m
                    },
                    // Ganavision real: 15 GV
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO_GV",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m,
                        oferta = null // Ganavision real
                    },
                    // Oferta combinada 1: línea cobrada
                    new LineaPedidoVentaDTO
                    {
                        Producto = "OFERTA_A",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 3,
                        PrecioUnitario = 20m,
                        oferta = 1
                    },
                    // Oferta combinada 1: línea gratis
                    new LineaPedidoVentaDTO
                    {
                        Producto = "OFERTA_A",
                        tipoLinea = 1,
                        Cantidad = 3,
                        PrecioUnitario = 0m,
                        oferta = 1 // Es oferta, NO Ganavision
                    },
                    // Oferta combinada 2: línea gratis de otro producto
                    new LineaPedidoVentaDTO
                    {
                        Producto = "OFERTA_B",
                        tipoLinea = 1,
                        Cantidad = 2,
                        PrecioUnitario = 0m,
                        oferta = 2 // Es oferta, NO Ganavision
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD_COS")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO_GV")).Returns(15);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("OFERTA_A")).Returns(2); // También tiene GV
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("OFERTA_B")).Returns(3); // También tiene GV

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO_GV", _servicioPrecios);

            // Assert - 20 GV disponibles, solo 15 consumidos (REGALO_GV)
            // OFERTA_A y OFERTA_B tienen oferta!=null, NO deben contar
            Assert.IsTrue(resultado.ValidacionSuperada,
                $"Solo REGALO_GV (15 GV) debe contar. OFERTA_A y OFERTA_B tienen oferta!=null. " +
                $"Resultado: {resultado.Motivo}");
        }

        #endregion

        #region Bug: GrupoProducto null al ampliar pedido (Issue #118)

        [TestMethod]
        public void EsPedidoValido_LineasSinGrupoProducto_BuscaGrupoDelServicio()
        {
            // Bug #118: Al ampliar un pedido, las líneas nuevas llegan con GrupoProducto = null
            // porque ni NestoApp ni Nesto envían ese campo en el DTO.
            // El validador debe buscar el grupo del producto vía servicio cuando GrupoProducto es null.
            //
            // Escenario: pedido original (100 EUR COS con GrupoProducto) + ampliación (100 EUR COS sin GrupoProducto)
            // + 2 regalos de 10 Ganavisiones cada uno = 20 consumidos
            // Disponibles esperados: 200 EUR / 10 = 20 → debe pasar
            // Bug actual: solo cuenta 100 EUR (la línea con GrupoProducto) → 10 disponibles < 20 consumidos → falla
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    // Línea original: tiene GrupoProducto (viene de LeerPedido)
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD_COS",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    // Línea de ampliación: NO tiene GrupoProducto (viene de NestoApp/Nesto)
                    new LineaPedidoVentaDTO
                    {
                        Producto = "PROD_COS2",
                        GrupoProducto = null, // <-- el bug: NestoApp no envía GrupoProducto
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    // Regalo original
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m
                    },
                    // Regalo de la ampliación
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO02",
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 0m
                    }
                }
            };

            // PROD_COS2 es cosmética pero GrupoProducto es null → el validador debe buscarlo
            A.CallTo(() => _servicioPrecios.BuscarProducto("PROD_COS2"))
                .Returns(new Producto { Número = "PROD_COS2", Grupo = Constantes.Productos.GRUPO_COSMETICA });

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD_COS")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD_COS2")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(10);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO02")).Returns(10);

            // Act
            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            // Assert - 200 EUR / 10 = 20 GV disponibles, 20 consumidos (10+10): debe pasar
            Assert.IsTrue(resultado.ValidacionSuperada,
                $"200 EUR bonificables (100 con grupo + 100 sin grupo) = 20 GV disponibles, " +
                $"20 consumidos: debe ser válido. Resultado: {resultado.Motivo}");
        }

        [TestMethod]
        public void EsPedidoValido_TodasLineasSinGrupoProducto_BuscaTodosDelServicio()
        {
            // Caso extremo: NINGUNA línea tiene GrupoProducto (ej: pedido creado 100% desde la app)
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "COS01",
                        GrupoProducto = null,
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

            A.CallTo(() => _servicioPrecios.BuscarProducto("COS01"))
                .Returns(new Producto { Número = "COS01", Grupo = Constantes.Productos.GRUPO_COSMETICA });

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("COS01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsTrue(resultado.ValidacionSuperada,
                $"100 EUR COS (grupo resuelto vía servicio) = 10 GV, 5 consumidos: debe ser válido. " +
                $"Resultado: {resultado.Motivo}");
        }

        [TestMethod]
        public void EsPedidoValido_ProductoSinGrupoYNoBonificable_NoGeneraGanavisiones()
        {
            // Verificar que si el servicio devuelve un grupo no bonificable, no se cuentan
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "APARATO01",
                        GrupoProducto = null, // Sin grupo en DTO
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 200m
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

            // El servicio dice que es Aparatos (no bonificable)
            A.CallTo(() => _servicioPrecios.BuscarProducto("APARATO01"))
                .Returns(new Producto { Número = "APARATO01", Grupo = Constantes.Productos.GRUPO_APARATOS });

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("APARATO01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsFalse(resultado.ValidacionSuperada,
                "Aparatos no generan Ganavisiones aunque se resuelva el grupo vía servicio");
        }

        #endregion

        #region Issue #117: Validar stock disponible de productos Ganavisiones

        [TestMethod]
        public void EsPedidoValido_ProductoBonificadoSinStock_Invalido()
        {
            // Hay suficientes Ganavisiones pero no hay stock del producto regalo
            var pedido = CrearPedidoConBonificado(100m, Constantes.Productos.GRUPO_COSMETICA, "REGALO01");

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);
            A.CallTo(() => _servicioPrecios.BuscarStockDisponibleTotal("REGALO01")).Returns(0);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsFalse(resultado.ValidacionSuperada,
                "No debe permitir bonificar un producto sin stock disponible");
            Assert.IsTrue(resultado.Motivo.Contains("stock"),
                $"El motivo debe mencionar stock. Motivo: {resultado.Motivo}");
        }

        [TestMethod]
        public void EsPedidoValido_ProductoBonificadoConStockSuficiente_Valido()
        {
            var pedido = CrearPedidoConBonificado(100m, Constantes.Productos.GRUPO_COSMETICA, "REGALO01");

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);
            A.CallTo(() => _servicioPrecios.BuscarStockDisponibleTotal("REGALO01")).Returns(10);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsTrue(resultado.ValidacionSuperada,
                "Debe permitir bonificar si hay stock suficiente y Ganavisiones suficientes");
        }

        [TestMethod]
        public void EsPedidoValido_ProductoBonificadoConStockJusto_Valido()
        {
            // Stock = 1 y cantidad bonificada = 1 → justo
            var pedido = CrearPedidoConBonificado(100m, Constantes.Productos.GRUPO_COSMETICA, "REGALO01");

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);
            A.CallTo(() => _servicioPrecios.BuscarStockDisponibleTotal("REGALO01")).Returns(1);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsTrue(resultado.ValidacionSuperada,
                "Stock = 1, cantidad = 1: debe ser valido");
        }

        [TestMethod]
        public void EsPedidoValido_MultiplesUnidadesBonificadasSinStockSuficiente_Invalido()
        {
            // Pide 3 unidades bonificadas pero solo hay 2 en stock
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
                        PrecioUnitario = 200m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        tipoLinea = 1,
                        Cantidad = 3,
                        PrecioUnitario = 0m
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("PROD01")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);
            A.CallTo(() => _servicioPrecios.BuscarStockDisponibleTotal("REGALO01")).Returns(2);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsFalse(resultado.ValidacionSuperada,
                "Pide 3 unidades pero solo hay 2 en stock: debe ser invalido");
            Assert.IsTrue(resultado.Motivo.Contains("2") && resultado.Motivo.Contains("3"),
                $"El motivo debe incluir disponible (2) y solicitado (3). Motivo: {resultado.Motivo}");
        }

        [TestMethod]
        public void EsPedidoValido_StockNoSeValidaSiNoHayGanavisionesSuficientes()
        {
            // Si no hay suficientes Ganavisiones, no deberia llegar a validar stock
            var pedido = CrearPedidoConBonificado(10m, Constantes.Productos.GRUPO_COSMETICA, "REGALO01");

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);
            A.CallTo(() => _servicioPrecios.BuscarStockDisponibleTotal("REGALO01")).Returns(0);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsFalse(resultado.ValidacionSuperada);
            // El error debe ser de Ganavisiones insuficientes, no de stock
            Assert.IsTrue(resultado.Motivo.Contains("Ganavisiones"),
                $"El error debe ser de Ganavisiones, no de stock. Motivo: {resultado.Motivo}");
            // No deberia haber llamado a BuscarStockDisponibleTotal
            A.CallTo(() => _servicioPrecios.BuscarStockDisponibleTotal(A<string>.Ignored)).MustNotHaveHappened();
        }

        #endregion

        #region Bug Nesto#346: reproducción con datos reales pedido 915278

        [TestMethod]
        public void EsPedidoValido_Pedido915278MasAmpliacion41264_Permite()
        {
            // Datos reales pedido 915278 (cliente 8874 CANDI):
            //   - 41279 (COS) cantidad 3 × 52,17 → Bruto 156,51 → BaseImponible 156,51
            //   - 28766 (ACC) bonificado 1 × 0,50, DescuentoLinea=1 → BaseImponible 0, GV=1
            //   - 40538 (ACC) bonificado 1 × 1,20, DescuentoLinea=1 → BaseImponible 0, GV=1
            //   - 40605 (ACC) bonificado 1 × 3,00, DescuentoLinea=1 → BaseImponible 0, GV=2
            //   - 40535 (ACC) bonificado 1 × 5,99, DescuentoLinea=1 → BaseImponible 0, GV=4
            //   - 62400000 (cuenta contable, tipoLinea=2) Comisión reembolso 3,00
            // Borrador ampliación:
            //   - 41264 (COS) cantidad 1 × 35,02 → BaseImponible 35,02
            //
            // Cálculo esperado:
            //   - base bonificable = 156,51 + 0×4 + 35,02 = 191,53 → 19 GV disponibles
            //   - consumidos = 1 + 1 + 2 + 4 = 8 GV
            //   - 8 ≤ 19 → debe autorizar 40538
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "41279", GrupoProducto = "COS",
                        tipoLinea = 1, Cantidad = 3, PrecioUnitario = 52.17m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "28766", GrupoProducto = "ACC",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 0.50m, DescuentoLinea = 1m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "40538", GrupoProducto = "ACC",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 1.20m, DescuentoLinea = 1m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "40605", GrupoProducto = "ACC",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 3.00m, DescuentoLinea = 1m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "40535", GrupoProducto = "ACC",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 5.99m, DescuentoLinea = 1m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "62400000", // Cuenta contable (comisión reembolso)
                        tipoLinea = 2, Cantidad = 1, PrecioUnitario = 3.00m
                    },
                    // Línea nueva del borrador ampliación
                    new LineaPedidoVentaDTO
                    {
                        Producto = "41264", GrupoProducto = "COS",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 35.02m
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("28766")).Returns(1);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("40538")).Returns(1);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("40605")).Returns(2);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("40535")).Returns(4);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("41279")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("41264")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("62400000")).Returns(null);

            var resultado = _validador.EsPedidoValido(pedido, "40538", _servicioPrecios);

            Assert.IsTrue(resultado.ValidacionSuperada,
                $"Base bonificable 191,53 EUR → 19 GV, consumidos 8. Debería autorizar 40538. Resultado: {resultado.Motivo}");
        }

        #endregion

        [TestMethod]
        public void EsPedidoValido_SiFaltaLineaGeneradoraDelOriginal_NoAutoriza()
        {
            // Hipótesis del usuario: quizá al validar el pedido unido solo se considera
            // la línea de ampliación (35,02) y los 4 bonificados del original, pero NO la
            // línea generadora del original (156,51). Si fuera así:
            //   base bonificable = 35,02 → 3 GV disponibles
            //   consumidos = 1 + 1 + 2 + 4 = 8 GV
            //   3 < 8 → rechaza → ese sería el bug.
            // Este test documenta explícitamente ese escenario: si en algún punto se
            // pierde la línea 41279, el validador rechaza 40538, que es justo lo que
            // estamos viendo en producción.
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    // FALTA la línea generadora 41279 (156,51)
                    new LineaPedidoVentaDTO
                    {
                        Producto = "28766", GrupoProducto = "ACC",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 0.50m, DescuentoLinea = 1m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "40538", GrupoProducto = "ACC",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 1.20m, DescuentoLinea = 1m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "40605", GrupoProducto = "ACC",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 3.00m, DescuentoLinea = 1m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "40535", GrupoProducto = "ACC",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 5.99m, DescuentoLinea = 1m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "41264", GrupoProducto = "COS",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 35.02m
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("28766")).Returns(1);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("40538")).Returns(1);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("40605")).Returns(2);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("40535")).Returns(4);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("41264")).Returns(null);

            var resultado = _validador.EsPedidoValido(pedido, "40538", _servicioPrecios);

            Assert.IsFalse(resultado.ValidacionSuperada,
                "Con solo la línea de ampliación como generadora (35,02 → 3 GV), 8 consumidos no pueden autorizarse.");
            Assert.IsTrue(resultado.Motivo.Contains("3 disponibles"),
                $"Debe indicar 3 GV disponibles. Motivo: {resultado.Motivo}");
        }

        [TestMethod]
        public void EsPedidoValido_RegaloExistenteSinStock_NoSeRevalidaStock()
        {
            // Bug Nesto#346 CAUSA RAÍZ. Un regalo Ganavisión ya existente en el pedido
            // (id != 0) no debe someterse al check de stock del Issue #117: cuando se
            // creó el pedido ya pasó ese check y su stock quedó reservado (justamente
            // por esta misma línea). Al re-validar tras UnirPedidos con un ampliación,
            // BuscarStockDisponibleTotal ahora devuelve 0 porque cuenta la propia
            // reserva como stock consumido → rechazo incorrecto.
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "41279", GrupoProducto = "COS",
                        tipoLinea = 1, Cantidad = 3, PrecioUnitario = 52.17m,
                        id = 323266100 // línea ya persistida del pedido original
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "40538", GrupoProducto = "ACC",
                        tipoLinea = 1, Cantidad = 1, PrecioUnitario = 1.20m, DescuentoLinea = 1m,
                        id = 323266300 // línea ya persistida del pedido original
                    }
                }
            };

            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("40538")).Returns(1);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("41279")).Returns(null);
            // Stock 0 porque la propia línea del pedido ya reservó la única unidad disponible
            A.CallTo(() => _servicioPrecios.BuscarStockDisponibleTotal("40538")).Returns(0);

            var resultado = _validador.EsPedidoValido(pedido, "40538", _servicioPrecios);

            Assert.IsTrue(resultado.ValidacionSuperada,
                $"La línea 40538 ya existía en el pedido (id != 0), no debe someterse al check de stock. Motivo: {resultado.Motivo}");
        }

        #region Bug Nesto#346: unir pedido que ya tiene un regalo Ganavisión

        [TestMethod]
        public void EsPedidoValido_OriginalConGanavisionMasAmpliacionNormal_Permite()
        {
            // Escenario Nesto#346: un pedido existente que ya tiene un regalo Ganavisión
            // (ya validado en su momento) se une a un pedido nuevo que solo añade una línea
            // de producto normal, sin descuento. El validador debe seguir autorizando el
            // regalo del original porque sigue habiendo Ganavisiones disponibles suficientes.
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    // Línea del pedido original: genera 10 Ganavisiones.
                    new LineaPedidoVentaDTO
                    {
                        Producto = "GENERADOR",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    // Línea del pedido original: regalo Ganavisión a 100% descuento.
                    // Consume 5 Ganavisiones. 10 ≥ 5 → debería pasar.
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 50m,
                        DescuentoLinea = 1m
                    },
                    // Línea recién incorporada desde la ampliación: producto normal sin
                    // descuento. Genera otros 2 Ganavisiones. El pedido resultante tiene
                    // de sobra para mantener el regalo.
                    new LineaPedidoVentaDTO
                    {
                        Producto = "AMPL",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 20m
                    }
                }
            };
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("GENERADOR")).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("AMPL")).Returns(null);

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsTrue(resultado.ValidacionSuperada,
                $"Debería autorizar el regalo (12 Ganavisiones disponibles >= 5 consumidos). Motivo: {resultado.Motivo}");
        }

        [TestMethod]
        public void EsPedidoValido_OriginalConGanavisionMasAmpliacionSinGrupo_Permite()
        {
            // Variante del caso anterior donde la línea nueva de la ampliación llega sin
            // GrupoProducto (Nesto no siempre lo rellena — ver Issue #118). Debe resolverlo
            // vía servicio y, aunque no aportara Ganavisiones, las líneas originales
            // generan ya suficientes.
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        Producto = "GENERADOR",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 100m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "REGALO01",
                        GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 50m,
                        DescuentoLinea = 1m
                    },
                    new LineaPedidoVentaDTO
                    {
                        Producto = "AMPL",
                        // GrupoProducto = null (lo típico cuando la línea viene del cliente)
                        tipoLinea = 1,
                        Cantidad = 1,
                        PrecioUnitario = 20m
                    }
                }
            };
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto(A<string>.Ignored)).Returns(null);
            A.CallTo(() => _servicioPrecios.BuscarGanavisionesProducto("REGALO01")).Returns(5);
            A.CallTo(() => _servicioPrecios.BuscarProducto("AMPL"))
                .Returns(new Producto { Número = "AMPL", Grupo = Constantes.Productos.GRUPO_COSMETICA });

            var resultado = _validador.EsPedidoValido(pedido, "REGALO01", _servicioPrecios);

            Assert.IsTrue(resultado.ValidacionSuperada,
                $"Debería autorizar. Motivo: {resultado.Motivo}");
        }

        #endregion
    }
}
