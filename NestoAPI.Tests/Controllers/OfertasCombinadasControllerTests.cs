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
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Results;
using System.Xml.Linq;

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
        public async Task PostOfertaCombinada_GrabaUsuarioDelIdentity_NoElDelParametro()
        {
            // Regresión (issue ofertas combinadas con usuario "NUEVAVISION\RDS2016$"): el usuario de
            // auditoría debe salir del Identity autenticado, NUNCA del parámetro de query que manda
            // el cliente (Nesto lo rellena con Environment.UserName y en el servidor RDS resuelve al
            // machine account del proceso). El parámetro trae el valor "malo" y el Identity el "bueno".
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
            A.CallTo(() => db.SaveChangesAsync())
                .Invokes(() =>
                {
                    if (listaOfertas.Any())
                    {
                        ConfigurarFakeDbSet(fakeOfertasCombinadas, listaOfertas.AsQueryable());
                    }
                })
                .Returns(Task.FromResult(1));

            // Identity autenticado con el usuario real
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos") }, "JWT");
            controller.RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(identity) };

            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Oferta Test",
                ImporteMinimo = 100,
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 1, Precio = 10 },
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD2", Cantidad = 2, Precio = 0 }
                }
            };

            // Act: el parámetro trae el machine account (lo que mandaría el cliente erróneamente)
            var resultado = await controller.PostOfertaCombinada(dto, "NUEVAVISION\\RDS2016$");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaCombinadaDTO>));
            var ofertaCreada = listaOfertas.Single();
            Assert.AreEqual("NUEVAVISION\\Carlos", ofertaCreada.Usuario, "La cabecera debe grabar el usuario del Identity");
            Assert.IsTrue(ofertaCreada.OfertasCombinadasDetalles.All(d => d.Usuario == "NUEVAVISION\\Carlos"),
                "Los detalles deben grabar el usuario del Identity");
        }

        [TestMethod]
        public async Task PostOfertaCombinada_SinIdentity_UsaParametroComoFallback()
        {
            // Sin Identity (tests / llamadas no autenticadas) se conserva el comportamiento anterior:
            // se usa el parámetro como último recurso.
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
            A.CallTo(() => db.SaveChangesAsync())
                .Invokes(() =>
                {
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
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 1, Precio = 10 },
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD2", Cantidad = 2, Precio = 0 }
                }
            };

            // Act: controller sin RequestContext.Principal autenticado
            var resultado = await controller.PostOfertaCombinada(dto, "usuario_param");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaCombinadaDTO>));
            Assert.AreEqual("usuario_param", listaOfertas.Single().Usuario);
        }

        [TestMethod]
        public async Task PostOfertaCombinada_SinDetalles_RetornaBadRequest()
        {
            // Arrange
            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Oferta Sin Lineas",
                ImporteMinimo = 100,
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>()
            };

            // Act
            var resultado = await controller.PostOfertaCombinada(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("al menos un producto"));
        }

        [TestMethod]
        public async Task PostOfertaCombinada_UnaLineaSinImporteMinimo_RetornaBadRequest()
        {
            // Arrange: una sola línea sin importe mínimo no la podría autorizar el validador de precios
            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Oferta Invalida",
                ImporteMinimo = 0,
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
            Assert.IsTrue(badRequest.Message.Contains("importe mínimo"));
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
        public async Task PostOfertaCombinada_DosLineasMismoProducto_CreaOK()
        {
            // Arrange: oferta de un solo producto repartida en dos líneas con precio
            // (p. ej. 2ª unidad al 50 %: una unidad a precio completo y otra a mitad)
            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Producto 1" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var listaOfertas = new List<OfertaCombinada>();
            A.CallTo(() => fakeOfertasCombinadas.Add(A<OfertaCombinada>.Ignored))
                .Invokes((OfertaCombinada o) => { o.Id = 1; listaOfertas.Add(o); })
                .ReturnsLazily((OfertaCombinada o) => o);
            A.CallTo(() => db.SaveChangesAsync())
                .Invokes(() =>
                {
                    if (listaOfertas.Any())
                    {
                        ConfigurarFakeDbSet(fakeOfertasCombinadas, listaOfertas.AsQueryable());
                    }
                })
                .Returns(Task.FromResult(1));

            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "2ª unidad al 50%",
                ImporteMinimo = 0,
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 1, Precio = 20.40m },
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 1, Precio = 10.20m }
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
        public async Task PostOfertaCombinada_UnaLineaConImporteMinimo_CreaOK()
        {
            // Arrange: oferta de un solo producto con el precio total en el importe mínimo
            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "PROD1", Nombre = "Producto 1" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var listaOfertas = new List<OfertaCombinada>();
            A.CallTo(() => fakeOfertasCombinadas.Add(A<OfertaCombinada>.Ignored))
                .Invokes((OfertaCombinada o) => { o.Id = 1; listaOfertas.Add(o); })
                .ReturnsLazily((OfertaCombinada o) => o);
            A.CallTo(() => db.SaveChangesAsync())
                .Invokes(() =>
                {
                    if (listaOfertas.Any())
                    {
                        ConfigurarFakeDbSet(fakeOfertasCombinadas, listaOfertas.AsQueryable());
                    }
                })
                .Returns(Task.FromResult(1));

            var dto = new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "2ª unidad al 50%",
                ImporteMinimo = 30.60m,
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>
                {
                    new OfertaCombinadaDetalleCreateDTO { Producto = "PROD1", Cantidad = 2, Precio = 0 }
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

        #region Grupos de alternativas (ValidarDTO)

        private static OfertaCombinadaCreateDTO CrearDtoConDetalles(params OfertaCombinadaDetalleCreateDTO[] detalles)
        {
            return new OfertaCombinadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Test grupos",
                ImporteMinimo = 100,
                Detalles = new List<OfertaCombinadaDetalleCreateDTO>(detalles)
            };
        }

        [TestMethod]
        public void ValidarDTO_GrupoConCantidadCero_DevuelveError()
        {
            var dto = CrearDtoConDetalles(
                new OfertaCombinadaDetalleCreateDTO { Producto = "CREMA", Cantidad = 1, Precio = 5 },
                new OfertaCombinadaDetalleCreateDTO { Producto = "CAM_S", Cantidad = 0, Precio = 0, GrupoAlternativa = 1 },
                new OfertaCombinadaDetalleCreateDTO { Producto = "CAM_M", Cantidad = 1, Precio = 0, GrupoAlternativa = 1 });

            string error = OfertasCombinadasController.ValidarDTO(dto);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "cantidad mayor que cero");
        }

        [TestMethod]
        public void ValidarDTO_GrupoConCantidadesDistintas_DevuelveError()
        {
            var dto = CrearDtoConDetalles(
                new OfertaCombinadaDetalleCreateDTO { Producto = "CREMA", Cantidad = 1, Precio = 5 },
                new OfertaCombinadaDetalleCreateDTO { Producto = "CAM_S", Cantidad = 1, Precio = 0, GrupoAlternativa = 1 },
                new OfertaCombinadaDetalleCreateDTO { Producto = "CAM_M", Cantidad = 2, Precio = 0, GrupoAlternativa = 1 });

            string error = OfertasCombinadasController.ValidarDTO(dto);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "misma cantidad");
        }

        [TestMethod]
        public void ValidarDTO_GrupoValido_NoDevuelveError()
        {
            var dto = CrearDtoConDetalles(
                new OfertaCombinadaDetalleCreateDTO { Producto = "CREMA", Cantidad = 1, Precio = 5 },
                new OfertaCombinadaDetalleCreateDTO { Producto = "CAM_S", Cantidad = 1, Precio = 0, GrupoAlternativa = 1 },
                new OfertaCombinadaDetalleCreateDTO { Producto = "CAM_M", Cantidad = 1, Precio = 0, GrupoAlternativa = 1 },
                new OfertaCombinadaDetalleCreateDTO { Producto = "CAM_L", Cantidad = 1, Precio = 0, GrupoAlternativa = 1 });

            string error = OfertasCombinadasController.ValidarDTO(dto);

            Assert.IsNull(error, error);
        }

        [TestMethod]
        public void ValidarDTO_SinGrupos_NoDevuelveError()
        {
            var dto = CrearDtoConDetalles(
                new OfertaCombinadaDetalleCreateDTO { Producto = "CREMA", Cantidad = 1, Precio = 5 },
                new OfertaCombinadaDetalleCreateDTO { Producto = "REGALO", Cantidad = 1, Precio = 0 });

            string error = OfertasCombinadasController.ValidarDTO(dto);

            Assert.IsNull(error, error);
        }

        #endregion

        #region Regresión: el Usuario de auditoría no debe ser StoreGenerated (RDS2016$)

        // Si Usuario se marca como Computed en el EDMX, EF no envía el valor que asigna el controller
        // (UsuarioAuditoriaHelper.Resolver) y SQL aplica el DEFAULT de la columna
        // (SYSTEM_USER = NUEVAVISION\RDS2016$). Este test lee el SSDL embebido en el assembly de
        // NestoAPI y verifica que Usuario NO es StoreGeneratedPattern="Computed".
        [DataTestMethod]
        [DataRow("OfertasCombinadas")]
        [DataRow("OfertasCombinadasDetalle")]
        public void Edmx_UsuarioDeOfertasCombinadas_NoEsStoreGenerated(string nombreEntidad)
        {
            var asm = typeof(NVEntities).Assembly;
            string recursoSsdl = asm.GetManifestResourceNames()
                .Single(n => n.EndsWith(".ssdl", StringComparison.OrdinalIgnoreCase));

            XDocument ssdl;
            using (var stream = asm.GetManifestResourceStream(recursoSsdl))
            {
                ssdl = XDocument.Load(stream);
            }

            var propiedadUsuario = ssdl.Descendants()
                .Where(e => e.Name.LocalName == "EntityType" && (string)e.Attribute("Name") == nombreEntidad)
                .SelectMany(e => e.Elements().Where(p => p.Name.LocalName == "Property"))
                .Single(p => (string)p.Attribute("Name") == "Usuario");

            string storeGenerated = (string)propiedadUsuario.Attribute("StoreGeneratedPattern");

            Assert.AreNotEqual("Computed", storeGenerated,
                $"El campo Usuario de {nombreEntidad} está marcado como Computed en el EDMX: EF no enviará " +
                "el usuario de auditoría y SQL grabará el DEFAULT (NUEVAVISION\\RDS2016$).");
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
