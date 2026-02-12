using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.Ganavisiones;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Tests para GanavisionesController.
    /// Issue #94: Sistema Ganavisiones - Backend
    /// </summary>
    [TestClass]
    public class GanavisionesControllerTests
    {
        private NVEntities db;
        private GanavisionesController controller;
        private IDbSet<Ganavision> fakeGanavisiones;
        private IDbSet<Producto> fakeProductos;
        private IDbSet<ExtractoProducto> fakeExtractosProducto;
        private IDbSet<LinPedidoVta> fakeLinPedidoVtas;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeGanavisiones = A.Fake<IDbSet<Ganavision>>();
            fakeProductos = A.Fake<IDbSet<Producto>>();
            fakeExtractosProducto = A.Fake<IDbSet<ExtractoProducto>>();
            fakeLinPedidoVtas = A.Fake<IDbSet<LinPedidoVta>>();

            A.CallTo(() => db.Ganavisiones).Returns(fakeGanavisiones);
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            A.CallTo(() => db.ExtractosProducto).Returns(fakeExtractosProducto);
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLinPedidoVtas);

            // Por defecto, extractos vacios y lineas de pedido vacias
            ConfigurarFakeDbSet(fakeExtractosProducto, new List<ExtractoProducto>().AsQueryable());
            ConfigurarFakeDbSet(fakeLinPedidoVtas, new List<LinPedidoVta>().AsQueryable());

            controller = new GanavisionesController(db);
        }

        #region GET Tests

        [TestMethod]
        public async Task GetGanavisiones_SinFiltros_RetornaTodos()
        {
            // Arrange
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Id = 1, Empresa = "1  ", ProductoId = "PROD1", Ganavisiones = 10, FechaDesde = DateTime.Today },
                new Ganavision { Id = 2, Empresa = "1  ", ProductoId = "PROD2", Ganavisiones = 20, FechaDesde = DateTime.Today }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetGanavisiones("1");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<GanavisionDTO>>));
            var okResult = (OkNegotiatedContentResult<List<GanavisionDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content.Count);
        }

        [TestMethod]
        public async Task GetGanavisiones_PorProducto_RetornaSoloEseProducto()
        {
            // Arrange
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Id = 1, Empresa = "1  ", ProductoId = "PROD1", Ganavisiones = 10, FechaDesde = DateTime.Today },
                new Ganavision { Id = 2, Empresa = "1  ", ProductoId = "PROD2", Ganavisiones = 20, FechaDesde = DateTime.Today }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetGanavisiones("1", "PROD1");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<GanavisionDTO>>));
            var okResult = (OkNegotiatedContentResult<List<GanavisionDTO>>)resultado;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual("PROD1", okResult.Content[0].ProductoId.Trim());
        }

        [TestMethod]
        public async Task GetGanavisiones_SoloActivos_FiltraPorFechas()
        {
            // Arrange: Un registro activo y uno expirado
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Id = 1, Empresa = "1  ", ProductoId = "PROD1", Ganavisiones = 10, FechaDesde = DateTime.Today.AddDays(-10), FechaHasta = null },
                new Ganavision { Id = 2, Empresa = "1  ", ProductoId = "PROD2", Ganavisiones = 20, FechaDesde = DateTime.Today.AddDays(-10), FechaHasta = DateTime.Today.AddDays(-1) }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetGanavisiones("1", soloActivos: true);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<GanavisionDTO>>));
            var okResult = (OkNegotiatedContentResult<List<GanavisionDTO>>)resultado;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual("PROD1", okResult.Content[0].ProductoId.Trim());
        }

        [TestMethod]
        public async Task GetGanavision_PorId_RetornaElRegistro()
        {
            // Arrange
            var ganavision = new Ganavision
            {
                Id = 1,
                Empresa = "1  ",
                ProductoId = "PROD1",
                Ganavisiones = 10,
                FechaDesde = DateTime.Today,
                Usuario = "TEST"
            };
            A.CallTo(() => db.Ganavisiones.FindAsync(1)).Returns(Task.FromResult(ganavision));

            // Act
            var resultado = await controller.GetGanavision(1);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<GanavisionDTO>));
            var okResult = (OkNegotiatedContentResult<GanavisionDTO>)resultado;
            Assert.AreEqual(1, okResult.Content.Id);
            Assert.AreEqual(10, okResult.Content.Ganavisiones);
        }

        [TestMethod]
        public async Task GetGanavision_IdInexistente_RetornaNotFound()
        {
            // Arrange
            A.CallTo(() => db.Ganavisiones.FindAsync(999)).Returns(Task.FromResult<Ganavision>(null));

            // Act
            var resultado = await controller.GetGanavision(999);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #endregion

        #region POST Tests

        [TestMethod]
        public async Task PostGanavision_ConGanavisionesEspecificado_UsaValorEspecificado()
        {
            // Arrange
            var dto = new GanavisionCreateDTO
            {
                Empresa = "1",
                ProductoId = "PROD1",
                Ganavisiones = 15,
                FechaDesde = DateTime.Today
            };

            var producto = new Producto
            {
                Empresa = "1  ",
                Número = "PROD1",
                Nombre = "Producto Test",
                PVP = 12.50m
            };
            var productos = new List<Producto> { producto }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            Ganavision ganavisionCreado = null;
            A.CallTo(() => fakeGanavisiones.Add(A<Ganavision>._))
                .Invokes((Ganavision g) => ganavisionCreado = g)
                .Returns(new Ganavision { Id = 1 });
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            // Act
            var resultado = await controller.PostGanavision(dto, "usuario_test");

            // Assert
            Assert.IsNotNull(ganavisionCreado);
            Assert.AreEqual(15, ganavisionCreado.Ganavisiones);
        }

        [TestMethod]
        public async Task PostGanavision_SinGanavisiones_CalculaPorDefectoPrecioAlAlza()
        {
            // Arrange: Producto con precio 12.30 => Ganavisiones = Ceiling(12.30) = 13
            var dto = new GanavisionCreateDTO
            {
                Empresa = "1",
                ProductoId = "PROD1",
                Ganavisiones = null, // No especificado
                FechaDesde = DateTime.Today
            };

            var producto = new Producto
            {
                Empresa = "1  ",
                Número = "PROD1",
                Nombre = "Producto Test",
                PVP = 12.30m
            };
            var productos = new List<Producto> { producto }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            Ganavision ganavisionCreado = null;
            A.CallTo(() => fakeGanavisiones.Add(A<Ganavision>._))
                .Invokes((Ganavision g) => ganavisionCreado = g)
                .Returns(new Ganavision { Id = 1 });
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            // Act
            var resultado = await controller.PostGanavision(dto, "usuario_test");

            // Assert
            Assert.IsNotNull(ganavisionCreado);
            Assert.AreEqual(13, ganavisionCreado.Ganavisiones); // Ceiling(12.30) = 13
        }

        [TestMethod]
        public async Task PostGanavision_PrecioExacto_GanavisionesIgualAlPrecio()
        {
            // Arrange: Producto con precio exacto 15.00 => Ganavisiones = 15
            var dto = new GanavisionCreateDTO
            {
                Empresa = "1",
                ProductoId = "PROD1",
                Ganavisiones = null,
                FechaDesde = DateTime.Today
            };

            var producto = new Producto
            {
                Empresa = "1  ",
                Número = "PROD1",
                Nombre = "Producto Test",
                PVP = 15.00m
            };
            var productos = new List<Producto> { producto }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            Ganavision ganavisionCreado = null;
            A.CallTo(() => fakeGanavisiones.Add(A<Ganavision>._))
                .Invokes((Ganavision g) => ganavisionCreado = g)
                .Returns(new Ganavision { Id = 1 });
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            // Act
            var resultado = await controller.PostGanavision(dto, "usuario_test");

            // Assert
            Assert.IsNotNull(ganavisionCreado);
            Assert.AreEqual(15, ganavisionCreado.Ganavisiones);
        }

        [TestMethod]
        public async Task PostGanavision_ProductoNoExiste_RetornaBadRequest()
        {
            // Arrange
            var dto = new GanavisionCreateDTO
            {
                Empresa = "1",
                ProductoId = "PROD_INEXISTENTE",
                Ganavisiones = 10,
                FechaDesde = DateTime.Today
            };

            var productos = new List<Producto>().AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            // Act
            var resultado = await controller.PostGanavision(dto, "usuario_test");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PostGanavision_ProductoYaExiste_RetornaBadRequest()
        {
            // Arrange
            var dto = new GanavisionCreateDTO
            {
                Empresa = "1",
                ProductoId = "PROD1",
                Ganavisiones = 10,
                FechaDesde = DateTime.Today
            };

            var producto = new Producto
            {
                Empresa = "1  ",
                Número = "PROD1",
                Nombre = "Producto Test",
                PVP = 10m
            };
            var productos = new List<Producto> { producto }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            // Ya existe un Ganavision para este producto
            var ganavisionesExistentes = new List<Ganavision>
            {
                new Ganavision { Id = 1, Empresa = "1  ", ProductoId = "PROD1", Ganavisiones = 5, FechaDesde = DateTime.Today }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisionesExistentes);

            // Act
            var resultado = await controller.PostGanavision(dto, "usuario_test");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("Ya existe"));
        }

        [TestMethod]
        public async Task PostGanavision_RegistraUsuarioYFechas()
        {
            // Arrange
            var dto = new GanavisionCreateDTO
            {
                Empresa = "1",
                ProductoId = "PROD1",
                Ganavisiones = 10,
                FechaDesde = DateTime.Today
            };

            var producto = new Producto
            {
                Empresa = "1  ",
                Número = "PROD1",
                Nombre = "Producto Test",
                PVP = 10m
            };
            var productos = new List<Producto> { producto }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            Ganavision ganavisionCreado = null;
            A.CallTo(() => fakeGanavisiones.Add(A<Ganavision>._))
                .Invokes((Ganavision g) => ganavisionCreado = g)
                .Returns(new Ganavision { Id = 1 });
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            // Act
            var resultado = await controller.PostGanavision(dto, "carlos");

            // Assert
            Assert.AreEqual("carlos", ganavisionCreado.Usuario);
            Assert.IsTrue((DateTime.Now - ganavisionCreado.FechaCreacion).TotalSeconds < 5);
            Assert.IsTrue((DateTime.Now - ganavisionCreado.FechaModificacion).TotalSeconds < 5);
        }

        #endregion

        #region PUT Tests

        [TestMethod]
        public async Task PutGanavision_ActualizaCorrectamente()
        {
            // Arrange
            var ganavisionExistente = new Ganavision
            {
                Id = 1,
                Empresa = "1  ",
                ProductoId = "PROD1",
                Ganavisiones = 10,
                FechaDesde = DateTime.Today.AddDays(-10),
                FechaCreacion = DateTime.Today.AddDays(-10),
                FechaModificacion = DateTime.Today.AddDays(-10),
                Usuario = "original"
            };
            A.CallTo(() => db.Ganavisiones.FindAsync(1)).Returns(Task.FromResult(ganavisionExistente));
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var dto = new GanavisionCreateDTO
            {
                Empresa = "1",
                ProductoId = "PROD1",
                Ganavisiones = 20,
                FechaDesde = DateTime.Today,
                FechaHasta = DateTime.Today.AddMonths(6)
            };

            // Act
            var resultado = await controller.PutGanavision(1, dto, "usuario_modificador");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<GanavisionDTO>));
            Assert.AreEqual(20, ganavisionExistente.Ganavisiones);
            Assert.AreEqual("usuario_modificador", ganavisionExistente.Usuario);
        }

        [TestMethod]
        public async Task PutGanavision_IdInexistente_RetornaNotFound()
        {
            // Arrange
            A.CallTo(() => db.Ganavisiones.FindAsync(999)).Returns(Task.FromResult<Ganavision>(null));

            var dto = new GanavisionCreateDTO
            {
                Empresa = "1",
                ProductoId = "PROD1",
                Ganavisiones = 20,
                FechaDesde = DateTime.Today
            };

            // Act
            var resultado = await controller.PutGanavision(999, dto, "usuario");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #endregion

        #region DELETE Tests

        [TestMethod]
        public async Task DeleteGanavision_EliminaCorrectamente()
        {
            // Arrange
            var ganavision = new Ganavision
            {
                Id = 1,
                Empresa = "1  ",
                ProductoId = "PROD1",
                Ganavisiones = 10,
                FechaDesde = DateTime.Today
            };
            A.CallTo(() => db.Ganavisiones.FindAsync(1)).Returns(Task.FromResult(ganavision));
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            // Act
            var resultado = await controller.DeleteGanavision(1);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<GanavisionDTO>));
            A.CallTo(() => fakeGanavisiones.Remove(ganavision)).MustHaveHappened();
        }

        [TestMethod]
        public async Task DeleteGanavision_IdInexistente_RetornaNotFound()
        {
            // Arrange
            A.CallTo(() => db.Ganavisiones.FindAsync(999)).Returns(Task.FromResult<Ganavision>(null));

            // Act
            var resultado = await controller.DeleteGanavision(999);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #endregion

        #region Obtener Ganavisiones Activos por Producto Tests

        [TestMethod]
        public async Task GetGanavisionesActivoProducto_ProductoConGanavisionActivo_RetornaValor()
        {
            // Arrange
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 15,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetGanavisionesActivoProducto("1", "PROD1");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int?>));
            var okResult = (OkNegotiatedContentResult<int?>)resultado;
            Assert.AreEqual(15, okResult.Content);
        }

        [TestMethod]
        public async Task GetGanavisionesActivoProducto_ProductoSinGanavision_RetornaNull()
        {
            // Arrange
            var ganavisiones = new List<Ganavision>().AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetGanavisionesActivoProducto("1", "PROD_SIN");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int?>));
            var okResult = (OkNegotiatedContentResult<int?>)resultado;
            Assert.IsNull(okResult.Content);
        }

        [TestMethod]
        public async Task GetGanavisionesActivoProducto_GanavisionExpirado_RetornaNull()
        {
            // Arrange: Ganavision con FechaHasta en el pasado
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 15,
                    FechaDesde = DateTime.Today.AddDays(-30),
                    FechaHasta = DateTime.Today.AddDays(-1) // Expirado ayer
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetGanavisionesActivoProducto("1", "PROD1");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int?>));
            var okResult = (OkNegotiatedContentResult<int?>)resultado;
            Assert.IsNull(okResult.Content);
        }

        [TestMethod]
        public async Task GetGanavisionesActivoProducto_GanavisionFuturo_RetornaNull()
        {
            // Arrange: Ganavision con FechaDesde en el futuro
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 15,
                    FechaDesde = DateTime.Today.AddDays(1), // Empieza manana
                    FechaHasta = null
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetGanavisionesActivoProducto("1", "PROD1");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int?>));
            var okResult = (OkNegotiatedContentResult<int?>)resultado;
            Assert.IsNull(okResult.Content);
        }

        #endregion

        #region GetProductosBonificablesIds Tests

        [TestMethod]
        public async Task GetProductosBonificablesIds_RetornaSoloProductosActivos()
        {
            // Arrange: Un producto activo, uno expirado, uno futuro
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD_ACTIVO",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null
                },
                new Ganavision
                {
                    Id = 2,
                    Empresa = "1  ",
                    ProductoId = "PROD_EXPIRADO",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-30),
                    FechaHasta = DateTime.Today.AddDays(-1)
                },
                new Ganavision
                {
                    Id = 3,
                    Empresa = "1  ",
                    ProductoId = "PROD_FUTURO",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(1),
                    FechaHasta = null
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificablesIds("1");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<string>>));
            var okResult = (OkNegotiatedContentResult<List<string>>)resultado;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual("PROD_ACTIVO", okResult.Content[0]);
        }

        [TestMethod]
        public async Task GetProductosBonificablesIds_SinProductos_RetornaListaVacia()
        {
            // Arrange
            var ganavisiones = new List<Ganavision>().AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificablesIds("1");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<string>>));
            var okResult = (OkNegotiatedContentResult<List<string>>)resultado;
            Assert.AreEqual(0, okResult.Content.Count);
        }

        [TestMethod]
        public async Task GetProductosBonificablesIds_ProductosDuplicados_RetornaDistinct()
        {
            // Arrange: Mismo producto con dos registros (uno expirado y uno activo)
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-30),
                    FechaHasta = DateTime.Today.AddDays(-1) // Expirado
                },
                new Ganavision
                {
                    Id = 2,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 10,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null // Activo
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificablesIds("1");

            // Assert: Solo debe aparecer una vez
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<string>>));
            var okResult = (OkNegotiatedContentResult<List<string>>)resultado;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual("PROD1", okResult.Content[0]);
        }

        #endregion

        #region GetProductosBonificables Tests

        [TestMethod]
        public async Task GetProductosBonificables_BaseImponible100_Retorna10GanavisionesDisponibles()
        {
            // Arrange: 100 EUR / 10 = 10 Ganavisiones disponibles
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5, // Menor que 10, se puede bonificar
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto Barato", PVP = 5m }
                },
                new Ganavision
                {
                    Id = 2,
                    Empresa = "1  ",
                    ProductoId = "PROD2",
                    Ganavisiones = 15, // Mayor que 10, NO se puede bonificar
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD2", Nombre = "Producto Caro", PVP = 15m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(10, okResult.Content.GanavisionesDisponibles);
            Assert.AreEqual(1, okResult.Content.Productos.Count);
            Assert.AreEqual("PROD1", okResult.Content.Productos[0].ProductoId.Trim());
        }

        [TestMethod]
        public async Task GetProductosBonificables_BaseImponible0_RetornaListaVacia()
        {
            // Arrange: 0 EUR = 0 Ganavisiones disponibles
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto Test", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 0m);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(0, okResult.Content.Productos.Count);
            Assert.AreEqual(0, okResult.Content.GanavisionesDisponibles);
        }

        [TestMethod]
        public async Task GetProductosBonificables_BaseImponibleNegativa_RetornaBadRequest()
        {
            // Act
            var resultado = await controller.GetProductosBonificables("1", -50m);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetProductosBonificables_TruncaGanavisiones_NoRedondea()
        {
            // Arrange: 19.99 EUR / 10 = 1.999 truncado a 1 Ganavision disponible
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 1, // Igual a 1, se puede bonificar
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto 1", PVP = 1m }
                },
                new Ganavision
                {
                    Id = 2,
                    Empresa = "1  ",
                    ProductoId = "PROD2",
                    Ganavisiones = 2, // Mayor que 1, NO se puede bonificar
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD2", Nombre = "Producto 2", PVP = 2m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 19.99m);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.GanavisionesDisponibles, "19.99/10=1.999 debe truncarse a 1");
            Assert.AreEqual(1, okResult.Content.Productos.Count, "Solo PROD1 con 1 Ganavision debe estar disponible");
        }

        [TestMethod]
        public async Task GetProductosBonificables_FiltraPorFechasActivas()
        {
            // Arrange: Solo devuelve productos con Ganavisiones activos
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD_ACTIVO",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null, // Sin fecha fin, activo
                    Producto = new Producto { Número = "PROD_ACTIVO", Nombre = "Activo", PVP = 5m }
                },
                new Ganavision
                {
                    Id = 2,
                    Empresa = "1  ",
                    ProductoId = "PROD_EXPIRADO",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-30),
                    FechaHasta = DateTime.Today.AddDays(-1), // Expirado ayer
                    Producto = new Producto { Número = "PROD_EXPIRADO", Nombre = "Expirado", PVP = 5m }
                },
                new Ganavision
                {
                    Id = 3,
                    Empresa = "1  ",
                    ProductoId = "PROD_FUTURO",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(1), // Empieza manana
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD_FUTURO", Nombre = "Futuro", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.Productos.Count, "Solo PROD_ACTIVO debe estar disponible");
            Assert.AreEqual("PROD_ACTIVO", okResult.Content.Productos[0].ProductoId.Trim());
        }

        [TestMethod]
        public async Task GetProductosBonificables_OrdenadoPorGanavisionesAscendente()
        {
            // Arrange: Varios productos, deben venir ordenados por Ganavisiones
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD_CARO",
                    Ganavisiones = 8,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD_CARO", Nombre = "Caro", PVP = 8m }
                },
                new Ganavision
                {
                    Id = 2,
                    Empresa = "1  ",
                    ProductoId = "PROD_BARATO",
                    Ganavisiones = 3,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD_BARATO", Nombre = "Barato", PVP = 3m }
                },
                new Ganavision
                {
                    Id = 3,
                    Empresa = "1  ",
                    ProductoId = "PROD_MEDIO",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD_MEDIO", Nombre = "Medio", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(3, okResult.Content.Productos.Count);
            Assert.AreEqual(3, okResult.Content.Productos[0].Ganavisiones, "Primero el mas barato");
            Assert.AreEqual(5, okResult.Content.Productos[1].Ganavisiones, "Segundo el medio");
            Assert.AreEqual(8, okResult.Content.Productos[2].Ganavisiones, "Tercero el mas caro");
        }

        [TestMethod]
        public async Task GetProductosBonificables_ServirJuntoTrue_MuestraProductosConStockEnCualquierAlmacen()
        {
            // Arrange: Producto con stock solo en REI, servirJunto=true debe mostrarlo
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto REI", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "REI", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Act: servirJunto=true (default)
            var resultado = await controller.GetProductosBonificables("1", 100m, "ALG", servirJunto: true);

            // Assert: Debe mostrar el producto aunque no tenga stock en ALG
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.Productos.Count);
        }

        [TestMethod]
        public async Task GetProductosBonificables_ServirJuntoFalse_SoloMuestraProductosConStockEnAlmacen()
        {
            // Arrange: Producto con stock solo en REI, servirJunto=false y almacen=ALG no debe mostrarlo
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto REI", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "REI", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Act: servirJunto=false, almacen=ALG
            var resultado = await controller.GetProductosBonificables("1", 100m, "ALG", servirJunto: false);

            // Assert: No debe mostrar el producto porque no tiene stock en ALG
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(0, okResult.Content.Productos.Count);
        }

        [TestMethod]
        public async Task GetProductosBonificables_ProductoSinStockEnNingunAlmacen_NoAparece()
        {
            // Arrange: Producto sin stock en ningun almacen
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Sin Stock", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Sin extractos = sin stock
            ConfigurarFakeDbSet(fakeExtractosProducto, new List<ExtractoProducto>().AsQueryable());

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m);

            // Assert: No debe mostrar productos sin stock
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(0, okResult.Content.Productos.Count);
        }

        [TestMethod]
        public async Task GetProductosBonificables_IncluyeStocksPorAlmacen()
        {
            // Arrange
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 5 },
                new ExtractoProducto { Número = "PROD1", Almacén = "REI", Cantidad = 3 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.Productos.Count);

            var producto = okResult.Content.Productos[0];
            Assert.IsNotNull(producto.Stocks);
            Assert.AreEqual(8, producto.StockTotal); // 5 + 3

            var stockALG = producto.Stocks.FirstOrDefault(s => s.almacen == "ALG");
            var stockREI = producto.Stocks.FirstOrDefault(s => s.almacen == "REI");
            Assert.IsNotNull(stockALG);
            Assert.IsNotNull(stockREI);
            Assert.AreEqual(5, stockALG.stock);
            Assert.AreEqual(3, stockREI.stock);
        }

        #endregion

        #region ValidarServirJunto Tests

        [TestMethod]
        public async Task ValidarServirJunto_SinProductosBonificados_PuedeDesmarcar()
        {
            // Arrange
            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                ProductosBonificados = new List<string>()
            };

            // Act
            var resultado = await controller.ValidarServirJunto(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ValidarServirJuntoResponse>));
            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
            Assert.AreEqual(0, okResult.Content.ProductosProblematicos.Count);
        }

        [TestMethod]
        public async Task ValidarServirJunto_ProductosConStockEnAlmacen_PuedeDesmarcar()
        {
            // Arrange: Producto con stock en ALG
            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Producto Test" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                ProductosBonificados = new List<string> { "PROD1" }
            };

            // Act
            var resultado = await controller.ValidarServirJunto(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ValidarServirJuntoResponse>));
            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
        }

        [TestMethod]
        public async Task ValidarServirJunto_ProductoSinStockEnAlmacen_NoPuedeDesmarcar()
        {
            // Arrange: Producto con stock en REI pero no en ALG
            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "REI", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Producto Test" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                ProductosBonificados = new List<string> { "PROD1" }
            };

            // Act
            var resultado = await controller.ValidarServirJunto(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ValidarServirJuntoResponse>));
            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsFalse(okResult.Content.PuedeDesmarcar);
            Assert.AreEqual(1, okResult.Content.ProductosProblematicos.Count);
            Assert.AreEqual("PROD1", okResult.Content.ProductosProblematicos[0].ProductoId.Trim());
            Assert.AreEqual("REI", okResult.Content.ProductosProblematicos[0].AlmacenConStock.Trim());
            Assert.IsTrue(okResult.Content.Mensaje.Contains("Producto Test"));
            Assert.IsTrue(okResult.Content.Mensaje.Contains("REI"));
        }

        [TestMethod]
        public async Task ValidarServirJunto_SinAlmacen_RetornaBadRequest()
        {
            // Arrange
            var request = new ValidarServirJuntoRequest
            {
                Almacen = null,
                ProductosBonificados = new List<string> { "PROD1" }
            };

            // Act
            var resultado = await controller.ValidarServirJunto(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task ValidarServirJunto_RequestNulo_RetornaBadRequest()
        {
            // Act
            var resultado = await controller.ValidarServirJunto(null);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task ValidarServirJunto_VariosProductos_AlgunosSinStock_RetornaProblematicos()
        {
            // Arrange: PROD1 con stock en ALG, PROD2 sin stock en ALG (solo REI)
            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 10 },
                new ExtractoProducto { Número = "PROD2", Almacén = "REI", Cantidad = 5 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Producto OK" },
                new Producto { Empresa = "1  ", Número = "PROD2", Nombre = "Producto Problematico" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                ProductosBonificados = new List<string> { "PROD1", "PROD2" }
            };

            // Act
            var resultado = await controller.ValidarServirJunto(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ValidarServirJuntoResponse>));
            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsFalse(okResult.Content.PuedeDesmarcar);
            Assert.AreEqual(1, okResult.Content.ProductosProblematicos.Count);
            Assert.AreEqual("PROD2", okResult.Content.ProductosProblematicos[0].ProductoId.Trim());
        }

        [TestMethod]
        public async Task ValidarServirJunto_ProductoSinStockEnNingunAlmacen_RetornaProblematico()
        {
            // Arrange: Producto sin stock en ningun sitio
            ConfigurarFakeDbSet(fakeExtractosProducto, new List<ExtractoProducto>().AsQueryable());

            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Sin Stock" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                ProductosBonificados = new List<string> { "PROD1" }
            };

            // Act
            var resultado = await controller.ValidarServirJunto(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ValidarServirJuntoResponse>));
            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsFalse(okResult.Content.PuedeDesmarcar);
            Assert.AreEqual(1, okResult.Content.ProductosProblematicos.Count);
            Assert.IsNull(okResult.Content.ProductosProblematicos[0].AlmacenConStock);
        }

        [TestMethod]
        public async Task ValidarServirJunto_TodosProductosConStockEnAlmacen_PuedeDesmarcar()
        {
            // Arrange: Todos los productos con stock en ALG
            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 10 },
                new ExtractoProducto { Número = "PROD2", Almacén = "ALG", Cantidad = 5 },
                new ExtractoProducto { Número = "PROD3", Almacén = "ALG", Cantidad = 3 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Producto 1" },
                new Producto { Empresa = "1  ", Número = "PROD2", Nombre = "Producto 2" },
                new Producto { Empresa = "1  ", Número = "PROD3", Nombre = "Producto 3" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                ProductosBonificados = new List<string> { "PROD1", "PROD2", "PROD3" }
            };

            // Act
            var resultado = await controller.ValidarServirJunto(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ValidarServirJuntoResponse>));
            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
            Assert.AreEqual(0, okResult.Content.ProductosProblematicos.Count);
        }

        #endregion

        #region Filtro por historial de compras del cliente Tests

        [TestMethod]
        public async Task GetProductosBonificables_ClienteNuncaCompro_MuestraTodosLosProductos()
        {
            // Arrange: Cliente sin historial de compras
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto 1", PVP = 5m }
                },
                new Ganavision
                {
                    Id = 2,
                    Empresa = "1  ",
                    ProductoId = "PROD2",
                    Ganavisiones = 3,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD2", Nombre = "Producto 2", PVP = 3m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 10 },
                new ExtractoProducto { Número = "PROD2", Almacén = "ALG", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Sin lineas de pedido para este cliente
            ConfigurarFakeDbSet(fakeLinPedidoVtas, new List<LinPedidoVta>().AsQueryable());

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m, cliente: "15000");

            // Assert: Debe mostrar ambos productos
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(2, okResult.Content.Productos.Count);
        }

        [TestMethod]
        public async Task GetProductosBonificables_ClienteComproProducto_ExcluyeEseProducto()
        {
            // Arrange: Cliente compro PROD1 con BaseImponible > 0
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto Comprado", PVP = 5m }
                },
                new Ganavision
                {
                    Id = 2,
                    Empresa = "1  ",
                    ProductoId = "PROD2",
                    Ganavisiones = 3,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD2", Nombre = "Producto Nuevo", PVP = 3m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 10 },
                new ExtractoProducto { Número = "PROD2", Almacén = "ALG", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Cliente 15000 compro PROD1 con BaseImponible = 25
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1  ", Nº_Cliente = "15000", Producto = "PROD1", Base_Imponible = 25m }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m, cliente: "15000");

            // Assert: Solo PROD2 debe aparecer
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.Productos.Count);
            Assert.AreEqual("PROD2", okResult.Content.Productos[0].ProductoId.Trim());
        }

        [TestMethod]
        public async Task GetProductosBonificables_ClienteRecibioComoBonificacion_SiMuestraProducto()
        {
            // Arrange: Cliente recibio PROD1 como bonificacion (BaseImponible = 0)
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto Bonificado Antes", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Cliente 15000 recibio PROD1 como bonificacion (BaseImponible = 0)
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1  ", Nº_Cliente = "15000", Producto = "PROD1", Base_Imponible = 0m }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m, cliente: "15000");

            // Assert: PROD1 debe aparecer porque solo lo recibio como bonificacion
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.Productos.Count);
            Assert.AreEqual("PROD1", okResult.Content.Productos[0].ProductoId.Trim());
        }

        [TestMethod]
        public async Task GetProductosBonificables_ClienteComproYRecibioComoBonificacion_ExcluyeProducto()
        {
            // Arrange: Cliente compro PROD1 y tambien lo recibio como bonificacion
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto Mixto", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Cliente 15000: una vez lo compro, otra vez lo recibio gratis
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1  ", Nº_Cliente = "15000", Producto = "PROD1", Base_Imponible = 0m },
                new LinPedidoVta { Empresa = "1  ", Nº_Cliente = "15000", Producto = "PROD1", Base_Imponible = 15m }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m, cliente: "15000");

            // Assert: PROD1 NO debe aparecer porque alguna vez lo compro
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(0, okResult.Content.Productos.Count);
        }

        [TestMethod]
        public async Task GetProductosBonificables_SinCliente_NoFiltraPorHistorial()
        {
            // Arrange: Sin especificar cliente, no debe filtrar
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Hay un cliente que compro, pero no especificamos cliente
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1  ", Nº_Cliente = "15000", Producto = "PROD1", Base_Imponible = 25m }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            // Act: Sin especificar cliente
            var resultado = await controller.GetProductosBonificables("1", 100m);

            // Assert: Debe mostrar el producto porque no se filtro por cliente
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.Productos.Count);
        }

        [TestMethod]
        public async Task GetProductosBonificables_OtroClienteCompro_NoAfectaAlClienteActual()
        {
            // Arrange: Otro cliente compro PROD1, pero el cliente actual no
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto { Número = "PROD1", Nombre = "Producto", PVP = 5m }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1", Almacén = "ALG", Cantidad = 10 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Cliente 99999 compro, pero consultamos para cliente 15000
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1  ", Nº_Cliente = "99999", Producto = "PROD1", Base_Imponible = 25m }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            // Act: Consultamos para cliente 15000
            var resultado = await controller.GetProductosBonificables("1", 100m, cliente: "15000");

            // Assert: Debe mostrar el producto porque el cliente 15000 no lo compro
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.Productos.Count);
        }

        [TestMethod]
        public async Task GetProductosBonificables_RetornaIvaDelProducto()
        {
            // Arrange: Producto con IVA_Repercutido G21 (IVA general 21%)
            // Fix: Clientes con recargo de equivalencia (R52) fallaban porque
            // se usaba el IVA del cliente en lugar del IVA del producto
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto
                    {
                        Número = "PROD1",
                        Nombre = "Producto Con IVA",
                        PVP = 5m,
                        IVA_Repercutido = "G21"  // IVA del producto
                    }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.Productos.Count);
            Assert.AreEqual("G21", okResult.Content.Productos[0].Iva,
                "El DTO debe incluir el IVA del producto (IVA_Repercutido) para crear líneas de pedido correctamente");
        }

        [TestMethod]
        public async Task GetProductosBonificables_ProductoSinIva_RetornaIvaNulo()
        {
            // Arrange: Producto sin IVA_Repercutido definido
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision
                {
                    Id = 1,
                    Empresa = "1  ",
                    ProductoId = "PROD1",
                    Ganavisiones = 5,
                    FechaDesde = DateTime.Today.AddDays(-5),
                    FechaHasta = null,
                    Producto = new Producto
                    {
                        Número = "PROD1",
                        Nombre = "Producto Sin IVA",
                        PVP = 5m,
                        IVA_Repercutido = null  // Sin IVA definido
                    }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            // Act
            var resultado = await controller.GetProductosBonificables("1", 100m);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductosBonificablesResponse>));
            var okResult = (OkNegotiatedContentResult<ProductosBonificablesResponse>)resultado;
            Assert.AreEqual(1, okResult.Content.Productos.Count);
            Assert.IsNull(okResult.Content.Productos[0].Iva,
                "Si el producto no tiene IVA_Repercutido, el campo Iva debe ser null");
        }

        #endregion

        #region Stock y CantidadRegalada Tests

        [TestMethod]
        public async Task GetGanavisiones_IncluyeStockDelProducto()
        {
            // Arrange
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Id = 1, Empresa = "1  ", ProductoId = "PROD1          ", Ganavisiones = 10, FechaDesde = DateTime.Today }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var extractos = new List<ExtractoProducto>
            {
                new ExtractoProducto { Número = "PROD1          ", Almacén = "ALG", Cantidad = 5 },
                new ExtractoProducto { Número = "PROD1          ", Almacén = "REI", Cantidad = 3 }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosProducto, extractos);

            // Act
            var resultado = await controller.GetGanavisiones("1");

            // Assert
            var okResult = (OkNegotiatedContentResult<List<GanavisionDTO>>)resultado;
            Assert.AreEqual(8, okResult.Content[0].Stock, "Stock debe ser la suma de todos los almacenes (5+3=8)");
        }

        [TestMethod]
        public async Task GetGanavisiones_IncluyeCantidadRegalada()
        {
            // Arrange
            var fechaDesde = DateTime.Today.AddDays(-30);
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Id = 1, Empresa = "1  ", ProductoId = "PROD1          ", Ganavisiones = 10, FechaDesde = fechaDesde, FechaHasta = null }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Producto = "PROD1          ", Cantidad = 2, Base_Imponible = 0, Fecha_Albarán = DateTime.Today.AddDays(-10) },
                new LinPedidoVta { Producto = "PROD1          ", Cantidad = 1, Base_Imponible = 0, Fecha_Albarán = DateTime.Today.AddDays(-5) }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            // Act
            var resultado = await controller.GetGanavisiones("1");

            // Assert
            var okResult = (OkNegotiatedContentResult<List<GanavisionDTO>>)resultado;
            Assert.AreEqual(3, okResult.Content[0].CantidadRegalada, "CantidadRegalada debe sumar las líneas con BaseImponible=0 (2+1=3)");
        }

        [TestMethod]
        public async Task GetGanavisiones_CantidadRegalada_RespetaRangoFechas()
        {
            // Arrange
            var fechaDesde = DateTime.Today.AddDays(-10);
            var fechaHasta = DateTime.Today.AddDays(-1);
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Id = 1, Empresa = "1  ", ProductoId = "PROD1          ", Ganavisiones = 10, FechaDesde = fechaDesde, FechaHasta = fechaHasta }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var lineas = new List<LinPedidoVta>
            {
                // Dentro del rango
                new LinPedidoVta { Producto = "PROD1          ", Cantidad = 2, Base_Imponible = 0, Fecha_Albarán = DateTime.Today.AddDays(-5) },
                // Fuera del rango (antes de FechaDesde)
                new LinPedidoVta { Producto = "PROD1          ", Cantidad = 3, Base_Imponible = 0, Fecha_Albarán = DateTime.Today.AddDays(-20) },
                // Fuera del rango (después de FechaHasta)
                new LinPedidoVta { Producto = "PROD1          ", Cantidad = 4, Base_Imponible = 0, Fecha_Albarán = DateTime.Today }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            // Act
            var resultado = await controller.GetGanavisiones("1");

            // Assert
            var okResult = (OkNegotiatedContentResult<List<GanavisionDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content[0].CantidadRegalada, "Solo debe sumar líneas dentro del rango de fechas");
        }

        [TestMethod]
        public async Task GetGanavisiones_CantidadRegalada_SinFechaHasta_SumaTodoDesdeFechaDesde()
        {
            // Arrange
            var fechaDesde = DateTime.Today.AddDays(-10);
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Id = 1, Empresa = "1  ", ProductoId = "PROD1          ", Ganavisiones = 10, FechaDesde = fechaDesde, FechaHasta = null }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, ganavisiones);

            var lineas = new List<LinPedidoVta>
            {
                // Dentro del rango (desde FechaDesde en adelante)
                new LinPedidoVta { Producto = "PROD1          ", Cantidad = 2, Base_Imponible = 0, Fecha_Albarán = DateTime.Today.AddDays(-5) },
                new LinPedidoVta { Producto = "PROD1          ", Cantidad = 1, Base_Imponible = 0, Fecha_Albarán = DateTime.Today },
                // Fuera del rango (antes de FechaDesde)
                new LinPedidoVta { Producto = "PROD1          ", Cantidad = 10, Base_Imponible = 0, Fecha_Albarán = DateTime.Today.AddDays(-20) }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            // Act
            var resultado = await controller.GetGanavisiones("1");

            // Assert
            var okResult = (OkNegotiatedContentResult<List<GanavisionDTO>>)resultado;
            Assert.AreEqual(3, okResult.Content[0].CantidadRegalada, "Sin FechaHasta debe sumar todo desde FechaDesde (2+1=3, no incluir el 10 anterior)");
        }

        #endregion

        #region Helper Methods

        private void ConfigurarFakeDbSet<T>(IDbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => fakeDbSet.Provider).Returns(data.Provider);
            A.CallTo(() => fakeDbSet.Expression).Returns(data.Expression);
            A.CallTo(() => fakeDbSet.ElementType).Returns(data.ElementType);
            A.CallTo(() => fakeDbSet.GetEnumerator()).Returns(data.GetEnumerator());
        }

        #endregion
    }
}
