using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.OfertasCombinadas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class OfertasCombinadasControllerTests
    {
        private NVEntities db;
        private OfertasCombinadasController controller;
        private DbSet<OfertaCombinada> fakeOfertasCombinadas;
        private DbSet<OfertaCombinadaDetalle> fakeOfertasCombinadasDetalles;
        private DbSet<Producto> fakeProductos;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeOfertasCombinadas = A.Fake<DbSet<OfertaCombinada>>(o => o.Implements<IQueryable<OfertaCombinada>>().Implements<IDbAsyncEnumerable<OfertaCombinada>>());
            fakeOfertasCombinadasDetalles = A.Fake<DbSet<OfertaCombinadaDetalle>>(o => o.Implements<IQueryable<OfertaCombinadaDetalle>>().Implements<IDbAsyncEnumerable<OfertaCombinadaDetalle>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());

            A.CallTo(() => db.OfertasCombinadas).Returns(fakeOfertasCombinadas);
            A.CallTo(() => db.OfertasCombinadasDetalles).Returns(fakeOfertasCombinadasDetalles);
            A.CallTo(() => db.Productos).Returns(fakeProductos);

            A.CallTo(() => fakeOfertasCombinadas.Include(A<string>.Ignored)).Returns(fakeOfertasCombinadas);

            ConfigurarFakeDbSet(fakeOfertasCombinadas, new List<OfertaCombinada>().AsQueryable());
            ConfigurarFakeDbSet(fakeOfertasCombinadasDetalles, new List<OfertaCombinadaDetalle>().AsQueryable());
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>().AsQueryable());

            controller = new OfertasCombinadasController(db);
        }

        #region GET Tests

        [TestMethod]
        public async Task GetOfertasCombinadas_RetornaTodasConDetalles()
        {
            // Arrange
            var ofertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 1, Empresa = "1  ", Nombre = "Oferta 1", ImporteMinimo = 100,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { Id = 1, Producto = "PROD1", Cantidad = 1, Precio = 10, Producto1 = new Producto { Nombre = "Producto 1" } },
                        new OfertaCombinadaDetalle { Id = 2, Producto = "PROD2", Cantidad = 2, Precio = 0, Producto1 = new Producto { Nombre = "Producto 2" } }
                    }
                },
                new OfertaCombinada
                {
                    Id = 2, Empresa = "1  ", Nombre = "Oferta 2", ImporteMinimo = 0,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { Id = 3, Producto = "PROD3", Cantidad = 1, Precio = 5, Producto1 = new Producto { Nombre = "Producto 3" } },
                        new OfertaCombinadaDetalle { Id = 4, Producto = "PROD4", Cantidad = 0, Precio = 0, Producto1 = new Producto { Nombre = "Producto 4" } }
                    }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasCombinadas, ofertas);

            // Act
            var resultado = await controller.GetOfertasCombinadas("1");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<OfertaCombinadaDTO>>));
            var okResult = (OkNegotiatedContentResult<List<OfertaCombinadaDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content.Count);
            Assert.AreEqual(2, okResult.Content[0].Detalles.Count);
            Assert.AreEqual("Producto 1", okResult.Content[0].Detalles[0].ProductoNombre);
        }

        [TestMethod]
        public async Task GetOfertasCombinadas_SoloActivas_FiltraPorFechas()
        {
            // Arrange
            var ofertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 1, Empresa = "1  ", Nombre = "Activa",
                    FechaDesde = DateTime.Today.AddDays(-10), FechaHasta = null,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>()
                },
                new OfertaCombinada
                {
                    Id = 2, Empresa = "1  ", Nombre = "Expirada",
                    FechaDesde = DateTime.Today.AddDays(-30), FechaHasta = DateTime.Today.AddDays(-1),
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>()
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasCombinadas, ofertas);

            // Act
            var resultado = await controller.GetOfertasCombinadas("1", soloActivas: true);

            // Assert
            var okResult = (OkNegotiatedContentResult<List<OfertaCombinadaDTO>>)resultado;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual("Activa", okResult.Content[0].Nombre);
        }

        [TestMethod]
        public async Task GetOfertaCombinada_PorId_RetornaConDetalles()
        {
            // Arrange
            var ofertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 5, Empresa = "1  ", Nombre = "Oferta Test",
                    ImporteMinimo = 50, FechaModificacion = DateTime.Now,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { Id = 10, Producto = "PROD1", Cantidad = 1, Precio = 15, Producto1 = new Producto { Nombre = "Producto 1" } },
                        new OfertaCombinadaDetalle { Id = 11, Producto = "PROD2", Cantidad = 2, Precio = 0, Producto1 = new Producto { Nombre = "Producto 2" } }
                    }
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasCombinadas, ofertas);

            // Act
            var resultado = await controller.GetOfertaCombinada(5);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaCombinadaDTO>));
            var okResult = (OkNegotiatedContentResult<OfertaCombinadaDTO>)resultado;
            Assert.AreEqual(5, okResult.Content.Id);
            Assert.AreEqual("Oferta Test", okResult.Content.Nombre);
            Assert.AreEqual(2, okResult.Content.Detalles.Count);
        }

        [TestMethod]
        public async Task GetOfertaCombinada_NoExiste_RetornaNotFound()
        {
            // Arrange
            ConfigurarFakeDbSet(fakeOfertasCombinadas, new List<OfertaCombinada>().AsQueryable());

            // Act
            var resultado = await controller.GetOfertaCombinada(999);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #endregion

        #region POST Tests

        [TestMethod]
        public async Task PostOfertaCombinada_ConDosDetalles_CreaOK()
        {
            // Arrange
            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Producto 1" },
                new Producto { Empresa = "1  ", Número = "PROD2", Nombre = "Producto 2" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var listaOfertas = new List<OfertaCombinada>();
            A.CallTo(() => fakeOfertasCombinadas.Add(A<OfertaCombinada>.Ignored))
                .Invokes((OfertaCombinada o) => { o.Id = 1; listaOfertas.Add(o); })
                .ReturnsLazily((OfertaCombinada o) => o);

            // Despues de SaveChanges, recargar devuelve la oferta creada
            A.CallTo(() => db.SaveChangesAsync())
                .Invokes(() =>
                {
                    // Reconfigurar el DbSet para que la recarga encuentre la oferta
                    if (listaOfertas.Any())
                    {
                        ConfigurarFakeDbSet(fakeOfertasCombinadas, listaOfertas.AsQueryable());
                    }
                })
                .Returns(Task.FromResult(1));

            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Oferta Test",
                ImporteMinimo = 100,
                FechaDesde = DateTime.Today,
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 1, Precio = 10 },
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD2", Cantidad = 2, Precio = 0 }
                }
            };

            // Act
            var resultado = await controller.PostOfertaCombinada(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaCombinadaDTO>));
            A.CallTo(() => fakeOfertasCombinadas.Add(A<OfertaCombinada>.Ignored)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task PostOfertaCombinada_MenosDeDosDetalles_RetornaBadRequest()
        {
            // Arrange
            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Oferta Invalida",
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 1, Precio = 10 }
                }
            };

            // Act
            var resultado = await controller.PostOfertaCombinada(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("al menos 2 productos"));
        }

        [TestMethod]
        public async Task PostOfertaCombinada_ProductoNoExiste_RetornaBadRequest()
        {
            // Arrange
            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Producto 1" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Oferta Test",
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 1, Precio = 10 },
                    new OfertaCombinadaDetalleCreateDTO { Producto = "NOEXISTE", Cantidad = 1, Precio = 5 }
                }
            };

            // Act
            var resultado = await controller.PostOfertaCombinada(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("NOEXISTE"));
        }

        [TestMethod]
        public async Task PostOfertaCombinada_ProductoDuplicado_RetornaBadRequest()
        {
            // Arrange
            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Oferta Duplicada",
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 1, Precio = 10 },
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 2, Precio = 5 }
                }
            };

            // Act
            var resultado = await controller.PostOfertaCombinada(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("duplicado"));
        }

        [TestMethod]
        public async Task PostOfertaCombinada_FechasIncoherentes_RetornaBadRequest()
        {
            // Arrange
            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Oferta Fechas Mal",
                FechaDesde = DateTime.Today.AddDays(10),
                FechaHasta = DateTime.Today,
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 1, Precio = 10 },
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD2", Cantidad = 1, Precio = 5 }
                }
            };

            // Act
            var resultado = await controller.PostOfertaCombinada(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("fecha"));
        }

        #endregion

        #region PUT Tests

        [TestMethod]
        public async Task PutOfertaCombinada_ActualizaCabeceraYGestionaLineas()
        {
            // Arrange
            var detalleExistente1 = new OfertaCombinadaDetalle { Id = 10, Empresa = "1  ", OfertaId = 1, Producto = "PROD1", Cantidad = 1, Precio = 10 };
            var detalleExistente2 = new OfertaCombinadaDetalle { Id = 11, Empresa = "1  ", OfertaId = 1, Producto = "PROD2", Cantidad = 2, Precio = 5 };
            var ofertaExistente = new OfertaCombinada
            {
                Id = 1, Empresa = "1  ", Nombre = "Oferta Original", ImporteMinimo = 100,
                FechaModificacion = DateTime.Now.AddDays(-1),
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle> { detalleExistente1, detalleExistente2 }
            };

            var ofertas = new List<OfertaCombinada> { ofertaExistente }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasCombinadas, ofertas);

            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Producto 1" },
                new Producto { Empresa = "1  ", Número = "PROD3", Nombre = "Producto 3" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            // PUT: mantiene PROD1 (Id=10), elimina PROD2 (Id=11), aniade PROD3 (Id=0)
            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Oferta Modificada",
                ImporteMinimo = 200,
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Id = 10, Producto = "PROD1", Cantidad = 3, Precio = 15 },
                    new OfertaCombinadaDetalleCreateDTO { Id = 0, Producto = "PROD3", Cantidad = 1, Precio = 8 }
                }
            };

            // Act
            var resultado = await controller.PutOfertaCombinada(1, dto, "testuser");

            // Assert
            Assert.AreEqual("Oferta Modificada", ofertaExistente.Nombre);
            Assert.AreEqual(200m, ofertaExistente.ImporteMinimo);
            Assert.AreEqual(3, detalleExistente1.Cantidad);
            Assert.AreEqual(15m, detalleExistente1.Precio);
            A.CallTo(() => db.OfertasCombinadasDetalles.Remove(detalleExistente2)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.OfertasCombinadasDetalles.Add(A<OfertaCombinadaDetalle>.That.Matches(d => d.Producto == "PROD3"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        #endregion

        #region DELETE Tests

        [TestMethod]
        public async Task DeleteOfertaCombinada_Existe_EliminaOK()
        {
            // Arrange
            var detalle1 = new OfertaCombinadaDetalle { Id = 10, Producto = "PROD1" };
            var detalle2 = new OfertaCombinadaDetalle { Id = 11, Producto = "PROD2" };
            var oferta = new OfertaCombinada
            {
                Id = 1, Empresa = "1  ", Nombre = "Oferta a Eliminar",
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle> { detalle1, detalle2 }
            };

            var ofertas = new List<OfertaCombinada> { oferta }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasCombinadas, ofertas);

            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            // Act
            var resultado = await controller.DeleteOfertaCombinada(1);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaCombinadaDTO>));
            A.CallTo(() => db.OfertasCombinadasDetalles.Remove(A<OfertaCombinadaDetalle>.Ignored)).MustHaveHappened(2, Times.Exactly);
            A.CallTo(() => fakeOfertasCombinadas.Remove(oferta)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        #endregion

        #region Helpers

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        #endregion
    }
}
