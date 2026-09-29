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

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class OfertasPermitidasFamiliaControllerTests
    {
        private NVEntities db;
        private OfertasPermitidasFamiliaController controller;
        private DbSet<OfertaPermitida> fakeOfertasPermitidas;
        private DbSet<Familia> fakeFamilias;
        private DbSet<SubGruposProducto> fakeSubGrupos;

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

            // NestoAPI#564: subgrupos para validar el SubGrupo de la regla.
            fakeSubGrupos = A.Fake<DbSet<SubGruposProducto>>(o => o.Implements<IQueryable<SubGruposProducto>>().Implements<IDbAsyncEnumerable<SubGruposProducto>>());
            A.CallTo(() => db.SubGruposProductoes).Returns(fakeSubGrupos);
            ConfigurarFakeDbSet(fakeSubGrupos, new List<SubGruposProducto>
            {
                new SubGruposProducto { Empresa = "1  ", Grupo = "PEL", Número = "DES", Descripción = "Desechables" }
            }.AsQueryable());

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
        public async Task PostOfertaPermitidaFamilia_GrabaUsuarioDelIdentity_NoElDelParametro()
        {
            // Regresión: el usuario de auditoría sale del Identity autenticado, no del parámetro de
            // query (que el cliente Nesto puede rellenar con el machine account del servidor RDS).
            var familias = new List<Familia>
            {
                new Familia { Empresa = "1  ", Número = "DeMarca   ", Descripción = "De Marca" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeFamilias, familias);
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida>().AsQueryable());

            OfertaPermitida ofertaGrabada = null;
            A.CallTo(() => fakeOfertasPermitidas.Add(A<OfertaPermitida>.Ignored))
                .Invokes((OfertaPermitida o) => ofertaGrabada = o)
                .ReturnsLazily((OfertaPermitida o) => o);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos") }, "JWT");
            controller.RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(identity) };

            var dto = new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "DeMarca   ",
                CantidadConPrecio = 6,
                CantidadRegalo = 1,
                FiltroProducto = "ESMALTE"
            };

            // Act: el parámetro trae el machine account
            var resultado = await controller.PostOfertaPermitidaFamilia(dto, "NUEVAVISION\\RDS2016$");

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            Assert.IsNotNull(ofertaGrabada);
            Assert.AreEqual("NUEVAVISION\\Carlos", ofertaGrabada.Usuario);
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_FamiliaEnMinusculas_SeGrabaComoEstaEnFamilias()
        {
            // NestoAPI#481 (10/09/26): Manuel tecleó "staleks" y así se quedó en la oferta 800.
            // SQL no distingue mayúsculas, así que funcionaba, pero en pantalla e informes debe
            // verse el nombre canónico de la familia.
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia>
            {
                new Familia { Empresa = "1  ", Número = "Staleks   ", Descripción = "Staleks" }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida>().AsQueryable());
            OfertaPermitida ofertaGrabada = null;
            A.CallTo(() => fakeOfertasPermitidas.Add(A<OfertaPermitida>.Ignored))
                .Invokes((OfertaPermitida o) => ofertaGrabada = o)
                .ReturnsLazily((OfertaPermitida o) => o);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var resultado = await controller.PostOfertaPermitidaFamilia(new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1",
                Familia = "staleks",
                CantidadConPrecio = 6,
                CantidadRegalo = 1
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            Assert.AreEqual("Staleks   ", ofertaGrabada.Familia);
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

        #region NestoAPI#564: Denegar y SubGrupo

        private static readonly List<Familia> FamiliaGenericos = new List<Familia>
        {
            new Familia { Empresa = "1  ", Número = "Genéricos ", Descripción = "Genéricos" }
        };

        private static OfertaPermitida AutorizacionGenericos6mas1(int nOrden = 48) => new OfertaPermitida
        {
            NºOrden = nOrden, Empresa = "1  ", Familia = "Genéricos ",
            CantidadConPrecio = 6, CantidadRegalo = 1, Denegar = false,
            Cliente = null, Número = null, Usuario = "admin", FechaModificación = DateTime.Now
        };

        private static OfertaPermitida DenegacionGenericosDes6mas1(int nOrden = 900) => new OfertaPermitida
        {
            NºOrden = nOrden, Empresa = "1  ", Familia = "Genéricos ", SubGrupo = "DES",
            CantidadConPrecio = 6, CantidadRegalo = 1, Denegar = true,
            Cliente = null, Número = null, Usuario = "admin", FechaModificación = DateTime.Now
        };

        [TestMethod]
        public async Task GetOfertasPermitidasFamilia_DevuelveDenegarYSubGrupo()
        {
            // La denegación no puede parecer otra «Genéricos 6+1» en la pantalla (la borrarían por duplicada).
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida>
            {
                AutorizacionGenericos6mas1(), DenegacionGenericosDes6mas1()
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeFamilias, FamiliaGenericos.AsQueryable());

            var resultado = await controller.GetOfertasPermitidasFamilia("1");

            var lista = ((OkNegotiatedContentResult<List<OfertaPermitidaFamiliaDTO>>)resultado).Content;
            Assert.AreEqual(2, lista.Count);
            Assert.IsFalse(lista[0].Denegar);
            Assert.IsNull(lista[0].SubGrupo);
            Assert.IsTrue(lista[1].Denegar);
            Assert.AreEqual("DES", lista[1].SubGrupo);
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_SinCamposNuevos_AutorizacionDeTodaLaFamilia()
        {
            // Compatibilidad: un Nesto antiguo no manda Denegar ni SubGrupo.
            ConfigurarFakeDbSet(fakeFamilias, FamiliaGenericos.AsQueryable());
            OfertaPermitida grabada = null;
            A.CallTo(() => fakeOfertasPermitidas.Add(A<OfertaPermitida>.Ignored))
                .Invokes((OfertaPermitida o) => grabada = o).ReturnsLazily((OfertaPermitida o) => o);

            var resultado = await controller.PostOfertaPermitidaFamilia(new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1", Familia = "Genéricos", CantidadConPrecio = 6, CantidadRegalo = 1
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            Assert.IsFalse(grabada.Denegar);
            Assert.IsNull(grabada.SubGrupo);
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_DenegacionConSubGrupo_ConviveConLaAutorizacion()
        {
            // Genéricos 6+1 (autorización) ya existe; la denegación Genéricos + DES 6+1 no es un duplicado.
            ConfigurarFakeDbSet(fakeFamilias, FamiliaGenericos.AsQueryable());
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida> { AutorizacionGenericos6mas1() }.AsQueryable());
            OfertaPermitida grabada = null;
            A.CallTo(() => fakeOfertasPermitidas.Add(A<OfertaPermitida>.Ignored))
                .Invokes((OfertaPermitida o) => grabada = o).ReturnsLazily((OfertaPermitida o) => o);

            var resultado = await controller.PostOfertaPermitidaFamilia(new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1", Familia = "Genéricos", CantidadConPrecio = 6, CantidadRegalo = 1,
                SubGrupo = " des ", Denegar = true
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            Assert.IsTrue(grabada.Denegar);
            Assert.AreEqual("DES", grabada.SubGrupo);
            var dto = ((OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>)resultado).Content;
            Assert.IsTrue(dto.Denegar);
            Assert.AreEqual("DES", dto.SubGrupo);
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_DenegacionRepetida_RetornaBadRequest()
        {
            ConfigurarFakeDbSet(fakeFamilias, FamiliaGenericos.AsQueryable());
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida> { DenegacionGenericosDes6mas1() }.AsQueryable());

            var resultado = await controller.PostOfertaPermitidaFamilia(new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1", Familia = "Genéricos", CantidadConPrecio = 6, CantidadRegalo = 1,
                SubGrupo = "DES", Denegar = true
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "Ya existe una denegación");
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_SubGrupoInexistente_RetornaBadRequest()
        {
            ConfigurarFakeDbSet(fakeFamilias, FamiliaGenericos.AsQueryable());

            var resultado = await controller.PostOfertaPermitidaFamilia(new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1", Familia = "Genéricos", CantidadConPrecio = 6, CantidadRegalo = 1,
                SubGrupo = "XYZ", Denegar = true
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "El subgrupo 'XYZ' no existe");
            A.CallTo(() => fakeOfertasPermitidas.Add(A<OfertaPermitida>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostOfertaPermitidaFamilia_SubGrupoDemasiadoLargo_RetornaBadRequest()
        {
            var resultado = await controller.PostOfertaPermitidaFamilia(new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1", Familia = "Genéricos", CantidadConPrecio = 6, CantidadRegalo = 1,
                SubGrupo = "DESE"
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "máximo 3 caracteres");
        }

        [TestMethod]
        public async Task PutOfertaPermitidaFamilia_EditarAutorizacionConDenegacionDeLaMismaFamilia_NoEsDuplicada()
        {
            // El fallo de #564: editar la regla 48 (Genéricos 6+1) daba «ya existe» por culpa de la denegación.
            var autorizacion = AutorizacionGenericos6mas1();
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida> { autorizacion, DenegacionGenericosDes6mas1() }.AsQueryable());
            ConfigurarFakeDbSet(fakeFamilias, FamiliaGenericos.AsQueryable());

            var resultado = await controller.PutOfertaPermitidaFamilia(48, new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1", Familia = "Genéricos", CantidadConPrecio = 12, CantidadRegalo = 2,
                SubGrupo = "", Denegar = false
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            Assert.AreEqual(12, autorizacion.CantidadConPrecio);
            Assert.IsFalse(autorizacion.Denegar);
            Assert.IsNull(autorizacion.SubGrupo);
        }

        [TestMethod]
        public async Task PutOfertaPermitidaFamilia_ClienteAntiguoSinCamposNuevos_ConservaDenegarYSubGrupo()
        {
            // Un Nesto antiguo que edita la denegación no manda Denegar ni SubGrupo: no se pueden borrar.
            var denegacion = DenegacionGenericosDes6mas1();
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida> { AutorizacionGenericos6mas1(), denegacion }.AsQueryable());
            ConfigurarFakeDbSet(fakeFamilias, FamiliaGenericos.AsQueryable());

            var resultado = await controller.PutOfertaPermitidaFamilia(900, new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1", Familia = "Genéricos", CantidadConPrecio = 6, CantidadRegalo = 1
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            Assert.IsTrue(denegacion.Denegar);
            Assert.AreEqual("DES", denegacion.SubGrupo);
        }

        [TestMethod]
        public async Task PutOfertaPermitidaFamilia_SubGrupoVacio_QuitaElSubGrupo()
        {
            var denegacion = DenegacionGenericosDes6mas1();
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida> { denegacion }.AsQueryable());
            ConfigurarFakeDbSet(fakeFamilias, FamiliaGenericos.AsQueryable());

            var resultado = await controller.PutOfertaPermitidaFamilia(900, new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1", Familia = "Genéricos", CantidadConPrecio = 6, CantidadRegalo = 1,
                SubGrupo = "", Denegar = true
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<OfertaPermitidaFamiliaDTO>));
            Assert.IsNull(denegacion.SubGrupo);
            Assert.IsTrue(denegacion.Denegar);
        }

        [TestMethod]
        public async Task PutOfertaPermitidaFamilia_ConvertirEnDenegacionYaExistente_RetornaBadRequest()
        {
            // Pasar la autorización a denegación de DES chocaría con la denegación que ya hay.
            ConfigurarFakeDbSet(fakeOfertasPermitidas, new List<OfertaPermitida> { AutorizacionGenericos6mas1(), DenegacionGenericosDes6mas1() }.AsQueryable());
            ConfigurarFakeDbSet(fakeFamilias, FamiliaGenericos.AsQueryable());

            var resultado = await controller.PutOfertaPermitidaFamilia(48, new OfertaPermitidaFamiliaCreateDTO
            {
                Empresa = "1", Familia = "Genéricos", CantidadConPrecio = 6, CantidadRegalo = 1,
                SubGrupo = "DES", Denegar = true
            }, "testuser");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "Ya existe una denegación");
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
