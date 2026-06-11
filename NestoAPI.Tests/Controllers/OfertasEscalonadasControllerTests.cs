using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.OfertasEscalonadas;
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

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class OfertasEscalonadasControllerTests
    {
        private NVEntities db;
        private OfertasEscalonadasController controller;
        private DbSet<OfertaEscalonada> fakeOfertas;
        private DbSet<OfertaEscalonadaProducto> fakeProductosOferta;
        private DbSet<OfertaEscalonadaTramo> fakeTramos;
        private DbSet<Producto> fakeProductos;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeOfertas = A.Fake<DbSet<OfertaEscalonada>>(o => o.Implements<IQueryable<OfertaEscalonada>>().Implements<IDbAsyncEnumerable<OfertaEscalonada>>());
            fakeProductosOferta = A.Fake<DbSet<OfertaEscalonadaProducto>>(o => o.Implements<IQueryable<OfertaEscalonadaProducto>>().Implements<IDbAsyncEnumerable<OfertaEscalonadaProducto>>());
            fakeTramos = A.Fake<DbSet<OfertaEscalonadaTramo>>(o => o.Implements<IQueryable<OfertaEscalonadaTramo>>().Implements<IDbAsyncEnumerable<OfertaEscalonadaTramo>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());

            A.CallTo(() => db.OfertasEscalonadas).Returns(fakeOfertas);
            A.CallTo(() => db.OfertasEscalonadasProductos).Returns(fakeProductosOferta);
            A.CallTo(() => db.OfertasEscalonadasTramos).Returns(fakeTramos);
            A.CallTo(() => db.Productos).Returns(fakeProductos);

            A.CallTo(() => fakeOfertas.Include(A<string>.Ignored)).Returns(fakeOfertas);

            ConfigurarFakeDbSet(fakeOfertas, new List<OfertaEscalonada>().AsQueryable());
            ConfigurarFakeDbSet(fakeProductosOferta, new List<OfertaEscalonadaProducto>().AsQueryable());
            ConfigurarFakeDbSet(fakeTramos, new List<OfertaEscalonadaTramo>().AsQueryable());
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>().AsQueryable());

            controller = new OfertasEscalonadasController(db);
        }

        private static OfertaEscalonada CrearOfertaCompleta(int id = 1)
        {
            return new OfertaEscalonada
            {
                Id = id,
                Empresa = "1  ",
                Nombre = "Escalado Test",
                FechaModificacion = DateTime.Now,
                OfertasEscalonadasProductos = new List<OfertaEscalonadaProducto>
                {
                    new OfertaEscalonadaProducto { Id = 1, Producto = "44707", PrecioBase = 18.50m, Producto1 = new Producto { Nombre = "Producto 1" } },
                    new OfertaEscalonadaProducto { Id = 2, Producto = "44708", PrecioBase = 24.95m, Producto1 = new Producto { Nombre = "Producto 2" } }
                },
                OfertasEscalonadasTramos = new List<OfertaEscalonadaTramo>
                {
                    new OfertaEscalonadaTramo { Id = 1, CantidadMinima = 3, Descuento = 0.10m },
                    new OfertaEscalonadaTramo { Id = 2, CantidadMinima = 2, Descuento = 0.05m }
                }
            };
        }

        #region GET Tests

        [TestMethod]
        public async Task GetOfertasEscalonadas_RetornaConProductosYTramosOrdenados()
        {
            ConfigurarFakeDbSet(fakeOfertas, new List<OfertaEscalonada> { CrearOfertaCompleta() }.AsQueryable());

            var resultado = await controller.GetOfertasEscalonadas("1");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<OfertaEscalonadaDTO>>));
            var okResult = (OkNegotiatedContentResult<List<OfertaEscalonadaDTO>>)resultado;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual(2, okResult.Content[0].Productos.Count);
            Assert.AreEqual("Producto 1", okResult.Content[0].Productos[0].ProductoNombre);
            // Los tramos salen ordenados por cantidad mínima aunque en BD estén desordenados
            Assert.AreEqual(2, okResult.Content[0].Tramos[0].CantidadMinima);
            Assert.AreEqual(3, okResult.Content[0].Tramos[1].CantidadMinima);
        }

        [TestMethod]
        public async Task GetOfertasEscalonadas_SoloActivas_FiltraPorFechas()
        {
            var activa = CrearOfertaCompleta(1);
            activa.Nombre = "Activa";
            activa.FechaDesde = DateTime.Today.AddDays(-10);
            var expirada = CrearOfertaCompleta(2);
            expirada.Nombre = "Expirada";
            expirada.FechaHasta = DateTime.Today.AddDays(-1);
            ConfigurarFakeDbSet(fakeOfertas, new List<OfertaEscalonada> { activa, expirada }.AsQueryable());

            var resultado = await controller.GetOfertasEscalonadas("1", soloActivas: true);

            var okResult = (OkNegotiatedContentResult<List<OfertaEscalonadaDTO>>)resultado;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual("Activa", okResult.Content[0].Nombre);
        }

        [TestMethod]
        public async Task GetOfertaEscalonada_NoExiste_RetornaNotFound()
        {
            var resultado = await controller.GetOfertaEscalonada(999);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #endregion

        #region POST Tests

        private List<OfertaEscalonada> PrepararPost()
        {
            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "44707", Nombre = "Producto 1", PVP = 18.50m },
                new Producto { Empresa = "1  ", Número = "44708", Nombre = "Producto 2", PVP = 24.95m }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var listaOfertas = new List<OfertaEscalonada>();
            A.CallTo(() => fakeOfertas.Add(A<OfertaEscalonada>.Ignored))
                .Invokes((OfertaEscalonada o) => { o.Id = 1; listaOfertas.Add(o); })
                .ReturnsLazily((OfertaEscalonada o) => o);
            A.CallTo(() => db.SaveChangesAsync())
                .Invokes(() =>
                {
                    if (listaOfertas.Any())
                    {
                        ConfigurarFakeDbSet(fakeOfertas, listaOfertas.AsQueryable());
                    }
                })
                .Returns(Task.FromResult(1));
            return listaOfertas;
        }

        private static OfertaEscalonadaCreateDTO CrearDTOValido()
        {
            return new OfertaEscalonadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Escalado Test",
                Productos = new List<OfertaEscalonadaProductoCreateDTO>
                {
                    new OfertaEscalonadaProductoCreateDTO { Producto = "44707", PrecioBase = 18.50m },
                    new OfertaEscalonadaProductoCreateDTO { Producto = "44708", PrecioBase = 24.95m }
                },
                Tramos = new List<OfertaEscalonadaTramoCreateDTO>
                {
                    new OfertaEscalonadaTramoCreateDTO { CantidadMinima = 2, Descuento = 0.05m },
                    new OfertaEscalonadaTramoCreateDTO { CantidadMinima = 3, Descuento = 0.10m }
                }
            };
        }

        [TestMethod]
        public async Task PostOfertaEscalonada_ConProductosYTramos_CreaOK()
        {
            PrepararPost();

            var resultado = await controller.PostOfertaEscalonada(CrearDTOValido(), "testuser");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaEscalonadaDTO>));
            A.CallTo(() => fakeOfertas.Add(A<OfertaEscalonada>.Ignored)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task PostOfertaEscalonada_SinPrecioBase_PrecargaElPVPDeFicha()
        {
            var listaOfertas = PrepararPost();

            var dto = CrearDTOValido();
            dto.Productos.ForEach(p => p.PrecioBase = null);

            var resultado = await controller.PostOfertaEscalonada(dto, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaEscalonadaDTO>));
            var ofertaCreada = listaOfertas.Single();
            Assert.AreEqual(18.50m, ofertaCreada.OfertasEscalonadasProductos.Single(p => p.Producto == "44707").PrecioBase,
                "Sin precio explícito debe precargarse el PVP de la ficha");
            Assert.AreEqual(24.95m, ofertaCreada.OfertasEscalonadasProductos.Single(p => p.Producto == "44708").PrecioBase);
        }

        [TestMethod]
        public async Task PostOfertaEscalonada_GrabaUsuarioDelIdentity_NoElDelParametro()
        {
            var listaOfertas = PrepararPost();

            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos") }, "JWT");
            controller.RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(identity) };

            var resultado = await controller.PostOfertaEscalonada(CrearDTOValido(), "NUEVAVISION\\RDS2016$");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaEscalonadaDTO>));
            var ofertaCreada = listaOfertas.Single();
            Assert.AreEqual("NUEVAVISION\\Carlos", ofertaCreada.Usuario);
            Assert.IsTrue(ofertaCreada.OfertasEscalonadasProductos.All(p => p.Usuario == "NUEVAVISION\\Carlos"));
            Assert.IsTrue(ofertaCreada.OfertasEscalonadasTramos.All(t => t.Usuario == "NUEVAVISION\\Carlos"));
        }

        [TestMethod]
        public async Task PostOfertaEscalonada_SinTramos_RetornaBadRequest()
        {
            var dto = CrearDTOValido();
            dto.Tramos = new List<OfertaEscalonadaTramoCreateDTO>();

            var resultado = await controller.PostOfertaEscalonada(dto, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.IsTrue(((BadRequestErrorMessageResult)resultado).Message.Contains("al menos un tramo"));
        }

        [TestMethod]
        public async Task PostOfertaEscalonada_ProductoNoExiste_RetornaBadRequest()
        {
            PrepararPost();
            var dto = CrearDTOValido();
            dto.Productos.Add(new OfertaEscalonadaProductoCreateDTO { Producto = "NOEXISTE" });

            var resultado = await controller.PostOfertaEscalonada(dto, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.IsTrue(((BadRequestErrorMessageResult)resultado).Message.Contains("NOEXISTE"));
        }

        [TestMethod]
        public async Task PostOfertaEscalonada_DescuentoNoCreciente_RetornaBadRequest()
        {
            var dto = CrearDTOValido();
            dto.Tramos = new List<OfertaEscalonadaTramoCreateDTO>
            {
                new OfertaEscalonadaTramoCreateDTO { CantidadMinima = 2, Descuento = 0.10m },
                new OfertaEscalonadaTramoCreateDTO { CantidadMinima = 3, Descuento = 0.05m }
            };

            var resultado = await controller.PostOfertaEscalonada(dto, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.IsTrue(((BadRequestErrorMessageResult)resultado).Message.Contains("mayor cuanto mayor"));
        }

        [TestMethod]
        public async Task PostOfertaEscalonada_TramosConCantidadRepetida_RetornaBadRequest()
        {
            var dto = CrearDTOValido();
            dto.Tramos = new List<OfertaEscalonadaTramoCreateDTO>
            {
                new OfertaEscalonadaTramoCreateDTO { CantidadMinima = 2, Descuento = 0.05m },
                new OfertaEscalonadaTramoCreateDTO { CantidadMinima = 2, Descuento = 0.10m }
            };

            var resultado = await controller.PostOfertaEscalonada(dto, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.IsTrue(((BadRequestErrorMessageResult)resultado).Message.Contains("misma cantidad"));
        }

        [TestMethod]
        public async Task PostOfertaEscalonada_ProductoRepetido_RetornaBadRequest()
        {
            var dto = CrearDTOValido();
            dto.Productos.Add(new OfertaEscalonadaProductoCreateDTO { Producto = "44707", PrecioBase = 18.50m });

            var resultado = await controller.PostOfertaEscalonada(dto, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.IsTrue(((BadRequestErrorMessageResult)resultado).Message.Contains("repetido"));
        }

        #endregion

        #region PUT / DELETE Tests

        [TestMethod]
        public async Task PutOfertaEscalonada_EliminaHijosQueNoVienenYCreaNuevos()
        {
            var oferta = CrearOfertaCompleta(5);
            ConfigurarFakeDbSet(fakeOfertas, new List<OfertaEscalonada> { oferta }.AsQueryable());
            var productos = new List<Producto>
            {
                new Producto { Empresa = "1  ", Número = "44707", Nombre = "Producto 1", PVP = 18.50m },
                new Producto { Empresa = "1  ", Número = "44951", Nombre = "Producto 3", PVP = 30m }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var dto = new OfertaEscalonadaCreateDTO
            {
                Empresa = "1",
                Nombre = "Escalado Modificado",
                Productos = new List<OfertaEscalonadaProductoCreateDTO>
                {
                    // Mantiene el producto 1 (Id=1), quita el 2, añade el 44951 nuevo
                    new OfertaEscalonadaProductoCreateDTO { Id = 1, Producto = "44707", PrecioBase = 17m },
                    new OfertaEscalonadaProductoCreateDTO { Producto = "44951" }
                },
                Tramos = new List<OfertaEscalonadaTramoCreateDTO>
                {
                    // Mantiene el tramo Id=2 y quita el Id=1
                    new OfertaEscalonadaTramoCreateDTO { Id = 2, CantidadMinima = 2, Descuento = 0.05m }
                }
            };

            var resultado = await controller.PutOfertaEscalonada(5, dto, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaEscalonadaDTO>));
            A.CallTo(() => fakeProductosOferta.Remove(A<OfertaEscalonadaProducto>.That.Matches(p => p.Id == 2)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => fakeProductosOferta.Add(A<OfertaEscalonadaProducto>.That.Matches(p => p.Producto == "44951" && p.PrecioBase == 30m)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => fakeTramos.Remove(A<OfertaEscalonadaTramo>.That.Matches(t => t.Id == 1)))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(17m, oferta.OfertasEscalonadasProductos.Single(p => p.Id == 1).PrecioBase,
                "El producto que se mantiene debe actualizar su precio base");
        }

        [TestMethod]
        public async Task PutOfertaEscalonada_NoExiste_RetornaNotFound()
        {
            var resultado = await controller.PutOfertaEscalonada(999, CrearDTOValido(), "testuser");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task DeleteOfertaEscalonada_EliminaOfertaYTodosLosHijos()
        {
            var oferta = CrearOfertaCompleta(7);
            ConfigurarFakeDbSet(fakeOfertas, new List<OfertaEscalonada> { oferta }.AsQueryable());
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var resultado = await controller.DeleteOfertaEscalonada(7);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaEscalonadaDTO>));
            A.CallTo(() => fakeProductosOferta.Remove(A<OfertaEscalonadaProducto>.Ignored)).MustHaveHappenedTwiceExactly();
            A.CallTo(() => fakeTramos.Remove(A<OfertaEscalonadaTramo>.Ignored)).MustHaveHappenedTwiceExactly();
            A.CallTo(() => fakeOfertas.Remove(A<OfertaEscalonada>.Ignored)).MustHaveHappenedOnceExactly();
        }

        #endregion

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
    }
}
