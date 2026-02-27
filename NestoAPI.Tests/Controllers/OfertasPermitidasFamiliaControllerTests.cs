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
    public class OfertasPermitidasFamiliaControllerTests
    {
        private NVEntities db;
        private OfertasPermitidasFamiliaController controller;
        private DbSet<OfertaPermitida> fakeOfertasPermitidas;
        private DbSet<Familia> fakeFamilias;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeOfertasPermitidas = A.Fake<DbSet<OfertaPermitida>>(o => o.Implements<IQueryable<OfertaPermitida>>().Implements<IDbAsyncEnumerable<OfertaPermitida>>());
            fakeFamilias = A.Fake<DbSet<Familia>>(o => o.Implements<IQueryable<Familia>>().Implements<IDbAsyncEnumerable<Familia>>());

            A.CallTo(() => db.OfertasPermitidas).Returns(fakeOfertasPermitidas);
            A.CallTo(() => db.Familias).Returns(fakeFamilias);

            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida>().AsQueryable());
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia>().AsQueryable());

            controller = new OfertasPermitidasFamiliaController(db);
        }

        #region GET Tests

        [TestMethod]
        public async Task GetOfertasPermitidasFamilia_RetornaSoloGenericasPorFamilia()
        {
            // Arrange: mezcla de ofertas genéricas por familia y específicas por cliente/producto
            var ofertas = new List<OfertaPermitida>
            {
                // Genérica por familia (la que debe devolver)
                new OfertaPermitida
                {
                    NºOrden = 1, Empresa = "1  ", Familia = "DeMarca   ",
                    CantidadConPrecio = 6, CantidadRegalo = 1,
                    FiltroProducto = "ESMALTE", Cliente = null, Número = null,
                    Usuario = "admin", FechaModificación = DateTime.Now
                },
                // Específica por cliente (no debe devolver)
                new OfertaPermitida
                {
                    NºOrden = 2, Empresa = "1  ", Familia = "DeMarca   ",
                    CantidadConPrecio = 3, CantidadRegalo = 1,
                    Cliente = "12345     ", Número = null,
                    Usuario = "admin", FechaModificación = DateTime.Now
                },
                // Específica por producto (no debe devolver)
                new OfertaPermitida
                {
                    NºOrden = 3, Empresa = "1  ", Familia = null,
                    CantidadConPrecio = 2, CantidadRegalo = 1,
                    Cliente = null, Número = "PROD1          ",
                    Usuario = "admin", FechaModificación = DateTime.Now
                },
                // Otra genérica por familia
                new OfertaPermitida
                {
                    NºOrden = 4, Empresa = "1  ", Familia = "Aparatos  ",
                    CantidadConPrecio = 3, CantidadRegalo = 1,
                    FiltroProducto = null, Cliente = null, Número = null,
                    Usuario = "admin", FechaModificación = DateTime.Now
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasPermitidas, ofertas);

            var familias = new List<Familia>
            {
                new Familia { Empresa = "1  ", Número = "DeMarca   ", Descripción = "De Marca" },
                new Familia { Empresa = "1  ", Número = "Aparatos  ", Descripción = "Aparatos" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeFamilias, familias);

            // Act
            var resultado = await controller.GetOfertasPermitidasFamilia("1");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<OfertaPermitidaFamiliaDTO>>));
            var okResult = (OkNegotiatedContentResult<List<OfertaPermitidaFamiliaDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content.Count);
            Assert.AreEqual("DeMarca", okResult.Content[0].Familia);
            Assert.AreEqual("De Marca", okResult.Content[0].FamiliaDescripcion);
            Assert.AreEqual(6, okResult.Content[0].CantidadConPrecio);
            Assert.AreEqual(1, okResult.Content[0].CantidadRegalo);
            Assert.AreEqual("ESMALTE", okResult.Content[0].FiltroProducto);
        }

        [TestMethod]
        public async Task GetOfertaPermitidaFamilia_PorNOrden_RetornaOK()
        {
            // Arrange
            var ofertas = new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    NºOrden = 5, Empresa = "1  ", Familia = "DeMarca   ",
                    CantidadConPrecio = 6, CantidadRegalo = 1,
                    FiltroProducto = "ESMALTE", Cliente = null, Número = null,
                    Usuario = "admin", FechaModificación = DateTime.Now
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasPermitidas, ofertas);

            var familias = new List<Familia>
            {
                new Familia { Empresa = "1  ", Número = "DeMarca   ", Descripción = "De Marca" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeFamilias, familias);

            // Act
            var resultado = await controller.GetOfertaPermitidaFamilia(5);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            var okResult = (OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>)resultado;
            Assert.AreEqual(5, okResult.Content.NOrden);
            Assert.AreEqual("DeMarca", okResult.Content.Familia);
        }

        [TestMethod]
        public async Task GetOfertaPermitidaFamilia_NoExiste_RetornaNotFound()
        {
            // Arrange
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida>().AsQueryable());

            // Act
            var resultado = await controller.GetOfertaPermitidaFamilia(999);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #endregion

        #region POST Tests

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_DatosValidos_CreaOK()
        {
            // Arrange
            var familias = new List<Familia>
            {
                new Familia { Empresa = "1  ", Número = "DeMarca   ", Descripción = "De Marca" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeFamilias, familias);

            // No hay ofertas existentes (no duplicada)
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida>().AsQueryable());

            A.CallTo(() => fakeOfertasPermitidas.Add(A<OfertaPermitida>.Ignored))
                .ReturnsLazily((OfertaPermitida o) => o);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var dto = new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "DeMarca   ",
                CantidadConPrecio = 6,
                CantidadRegalo = 1,
                FiltroProducto = "ESMALTE"
            };

            // Act
            var resultado = await controller.PostOfertaPermitidaFamilia(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            A.CallTo(() => fakeOfertasPermitidas.Add(A<OfertaPermitida>.That.Matches(
                o => o.Familia == "DeMarca   " && o.CantidadConPrecio == 6 && o.CantidadRegalo == 1
            ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_FamiliaVacia_RetornaBadRequest()
        {
            // Arrange
            var dto = new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "",
                CantidadConPrecio = 6,
                CantidadRegalo = 1
            };

            // Act
            var resultado = await controller.PostOfertaPermitidaFamilia(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("familia"));
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_CantidadConPrecioCero_RetornaBadRequest()
        {
            // Arrange
            var dto = new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "DeMarca   ",
                CantidadConPrecio = 0,
                CantidadRegalo = 1
            };

            // Act
            var resultado = await controller.PostOfertaPermitidaFamilia(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("cantidad con precio"));
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_CantidadRegaloCero_RetornaBadRequest()
        {
            // Arrange
            var dto = new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "DeMarca   ",
                CantidadConPrecio = 6,
                CantidadRegalo = 0
            };

            // Act
            var resultado = await controller.PostOfertaPermitidaFamilia(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("cantidad de regalo"));
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_FamiliaNoExiste_RetornaBadRequest()
        {
            // Arrange
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia>().AsQueryable());
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida>().AsQueryable());

            var dto = new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "NOEXISTE",
                CantidadConPrecio = 6,
                CantidadRegalo = 1
            };

            // Act
            var resultado = await controller.PostOfertaPermitidaFamilia(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("NOEXISTE"));
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_Duplicada_RetornaBadRequest()
        {
            // Arrange
            var familias = new List<Familia>
            {
                new Familia { Empresa = "1  ", Número = "DeMarca   ", Descripción = "De Marca" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeFamilias, familias);

            var ofertasExistentes = new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    NºOrden = 1, Empresa = "1  ", Familia = "DeMarca   ",
                    CantidadConPrecio = 6, CantidadRegalo = 1,
                    FiltroProducto = "ESMALTE", Cliente = null, Número = null
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasPermitidas, ofertasExistentes);

            var dto = new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "DeMarca   ",
                CantidadConPrecio = 3,
                CantidadRegalo = 1,
                FiltroProducto = "ESMALTE"
            };

            // Act
            var resultado = await controller.PostOfertaPermitidaFamilia(dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            var badRequest = (BadRequestErrorMessageResult)resultado;
            Assert.IsTrue(badRequest.Message.Contains("Ya existe"));
        }

        #endregion

        #region PUT Tests

        [TestMethod]
        public async Task PutOfertaPermitidaFamilia_DatosValidos_ActualizaOK()
        {
            // Arrange
            var ofertaExistente = new OfertaPermitida
            {
                NºOrden = 1, Empresa = "1  ", Familia = "DeMarca   ",
                CantidadConPrecio = 6, CantidadRegalo = 1,
                FiltroProducto = "ESMALTE", Cliente = null, Número = null,
                Usuario = "admin", FechaModificación = DateTime.Now.AddDays(-1)
            };
            var ofertas = new List<OfertaPermitida> { ofertaExistente }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasPermitidas, ofertas);

            var familias = new List<Familia>
            {
                new Familia { Empresa = "1  ", Número = "DeMarca   ", Descripción = "De Marca" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeFamilias, familias);

            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var dto = new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "DeMarca   ",
                CantidadConPrecio = 12,
                CantidadRegalo = 2,
                FiltroProducto = "ESMALTE NUEVO"
            };

            // Act
            var resultado = await controller.PutOfertaPermitidaFamilia(1, dto, "testuser");

            // Assert
            Assert.AreEqual(12, ofertaExistente.CantidadConPrecio);
            Assert.AreEqual(2, ofertaExistente.CantidadRegalo);
            Assert.AreEqual("ESMALTE NUEVO", ofertaExistente.FiltroProducto);
            Assert.AreEqual("testuser", ofertaExistente.Usuario);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task PutOfertaPermitidaFamilia_NoExiste_RetornaNotFound()
        {
            // Arrange
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida>().AsQueryable());

            var dto = new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "DeMarca   ",
                CantidadConPrecio = 6,
                CantidadRegalo = 1
            };

            // Act
            var resultado = await controller.PutOfertaPermitidaFamilia(999, dto, "testuser");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #endregion

        #region DELETE Tests

        [TestMethod]
        public async Task DeleteOfertaPermitidaFamilia_Existe_EliminaOK()
        {
            // Arrange
            var oferta = new OfertaPermitida
            {
                NºOrden = 1, Empresa = "1  ", Familia = "DeMarca   ",
                CantidadConPrecio = 6, CantidadRegalo = 1,
                Cliente = null, Número = null,
                Usuario = "admin", FechaModificación = DateTime.Now
            };
            var ofertas = new List<OfertaPermitida> { oferta }.AsQueryable();
            ConfigurarFakeDbSet(fakeOfertasPermitidas, ofertas);

            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            // Act
            var resultado = await controller.DeleteOfertaPermitidaFamilia(1);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            A.CallTo(() => fakeOfertasPermitidas.Remove(oferta)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task DeleteOfertaPermitidaFamilia_NoExiste_RetornaNotFound()
        {
            // Arrange
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida>().AsQueryable());

            // Act
            var resultado = await controller.DeleteOfertaPermitidaFamilia(999);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
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
