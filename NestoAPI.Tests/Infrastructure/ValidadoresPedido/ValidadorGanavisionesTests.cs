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
    }
}
