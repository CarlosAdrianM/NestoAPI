using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.CodigosPostales;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// #378: mantenimiento de códigos postales (búsqueda y edición con país y vendedores
    /// por grupo de producto).
    /// </summary>
    [TestClass]
    public class CodigosPostalesControllerTests
    {
        private NVEntities db;
        private CodigosPostalesController controller;
        private DbSet<CodigoPostal> fakeCps;
        private DbSet<VendedorCodigoPostalGrupoProducto> fakeVendedoresGrupo;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeCps = A.Fake<DbSet<CodigoPostal>>(o => o.Implements<IQueryable<CodigoPostal>>().Implements<IDbAsyncEnumerable<CodigoPostal>>());
            fakeVendedoresGrupo = A.Fake<DbSet<VendedorCodigoPostalGrupoProducto>>(o => o.Implements<IQueryable<VendedorCodigoPostalGrupoProducto>>().Implements<IDbAsyncEnumerable<VendedorCodigoPostalGrupoProducto>>());

            A.CallTo(() => db.CodigosPostales).Returns(fakeCps);
            A.CallTo(() => db.VendedoresCodigoPostalGruposProductos).Returns(fakeVendedoresGrupo);

            ConfigurarFakeDbSet(fakeCps, new List<CodigoPostal>().AsQueryable());
            ConfigurarFakeDbSet(fakeVendedoresGrupo, new List<VendedorCodigoPostalGrupoProducto>().AsQueryable());

            controller = new CodigosPostalesController(db);
        }

        private CodigoPostal CpMadrid => new CodigoPostal
        {
            Empresa = "1",
            Número = "28004",
            Descripción = "MADRID",
            Provincia = "MADRID",
            Ruta = "16",
            Vendedor = "JE ",
            Pais = "ES"
        };

        private CodigoPostal CpErmesinde => new CodigoPostal
        {
            Empresa = "1",
            Número = "4445-294",
            Descripción = "ERMESINDE",
            Provincia = "PORTUGAL",
            Ruta = "00",
            Vendedor = "NV ",
            Pais = null
        };

        [TestMethod]
        public void CodigosPostales_Get_FiltraPorNumero()
        {
            ConfigurarFakeDbSet(fakeCps, new List<CodigoPostal> { CpMadrid, CpErmesinde }.AsQueryable());

            var resultado = controller.GetCodigosPostales("28004").Result as OkNegotiatedContentResult<List<CodigoPostalMantenimientoDTO>>;

            Assert.IsNotNull(resultado);
            CodigoPostalMantenimientoDTO unico = resultado.Content.Single();
            Assert.AreEqual("28004", unico.Numero);
            Assert.AreEqual("MADRID", unico.Poblacion);
            Assert.AreEqual("16", unico.Ruta);
            Assert.AreEqual("JE", unico.Vendedor, "Los char de la BD vienen con padding y se devuelven recortados");
            Assert.AreEqual("ES", unico.Pais);
        }

        [TestMethod]
        public void CodigosPostales_Get_FiltraPorPoblacion()
        {
            ConfigurarFakeDbSet(fakeCps, new List<CodigoPostal> { CpMadrid, CpErmesinde }.AsQueryable());

            var resultado = controller.GetCodigosPostales("ermes").Result as OkNegotiatedContentResult<List<CodigoPostalMantenimientoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("4445-294", resultado.Content.Single().Numero);
            Assert.IsNull(resultado.Content.Single().Pais, "Los CPs viejos sin país llegan con país null para poder corregirlos");
        }

        [TestMethod]
        public void CodigosPostales_Get_IncluyeVendedoresPorGrupoProducto()
        {
            ConfigurarFakeDbSet(fakeCps, new List<CodigoPostal> { CpMadrid }.AsQueryable());
            ConfigurarFakeDbSet(fakeVendedoresGrupo, new List<VendedorCodigoPostalGrupoProducto>
            {
                new VendedorCodigoPostalGrupoProducto { Empresa = "1", CodigoPostal = "28004", GrupoProducto = "PEL", Vendedor = "AH " },
                new VendedorCodigoPostalGrupoProducto { Empresa = "1", CodigoPostal = "28005", GrupoProducto = "PEL", Vendedor = "JM " }
            }.AsQueryable());

            var resultado = controller.GetCodigosPostales("28004").Result as OkNegotiatedContentResult<List<CodigoPostalMantenimientoDTO>>;

            Assert.IsNotNull(resultado);
            VendedorGrupoProductoCodigoPostalDTO vendedorGrupo = resultado.Content.Single().VendedoresGrupoProducto.Single();
            Assert.AreEqual("PEL", vendedorGrupo.GrupoProducto);
            Assert.AreEqual("AH", vendedorGrupo.Vendedor);
        }

        [TestMethod]
        public void CodigosPostales_Get_SinFiltro_BadRequest()
        {
            var resultado = controller.GetCodigosPostales(" ").Result;

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void CodigosPostales_Put_ActualizaLosCamposDelCodigoPostal()
        {
            CodigoPostal cp = CpErmesinde;
            ConfigurarFakeDbSet(fakeCps, new List<CodigoPostal> { cp }.AsQueryable());
            CodigoPostalMantenimientoDTO dto = new CodigoPostalMantenimientoDTO
            {
                Empresa = "1",
                Numero = "4445-294",
                Poblacion = "Ermesinde",
                Provincia = "Porto",
                Ruta = "00",
                Vendedor = "NV",
                Pais = "pt"
            };

            var resultado = controller.PutCodigoPostal(dto).Result as OkNegotiatedContentResult<CodigoPostalMantenimientoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("ERMESINDE", cp.Descripción);
            Assert.AreEqual("PORTO", cp.Provincia);
            Assert.AreEqual("PT", cp.Pais, "El país se guarda en mayúsculas");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void CodigosPostales_Put_CamposObligatoriosVaciosNoMachacanLoQueHabia()
        {
            CodigoPostal cp = CpMadrid;
            ConfigurarFakeDbSet(fakeCps, new List<CodigoPostal> { cp }.AsQueryable());
            CodigoPostalMantenimientoDTO dto = new CodigoPostalMantenimientoDTO
            {
                Empresa = "1",
                Numero = "28004",
                Poblacion = null,
                Provincia = null,
                Ruta = null,
                Vendedor = null,
                Pais = ""
            };

            var resultado = controller.PutCodigoPostal(dto).Result as OkNegotiatedContentResult<CodigoPostalMantenimientoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("MADRID", cp.Provincia, "Provincia es NOT NULL: si viene vacía se conserva");
            Assert.AreEqual("16", cp.Ruta);
            Assert.AreEqual("JE ", cp.Vendedor);
            Assert.IsNull(cp.Pais, "El país sí se puede blanquear (país desconocido)");
        }

        [TestMethod]
        public void CodigosPostales_Put_SincronizaVendedoresPorGrupoProducto()
        {
            CodigoPostal cp = CpMadrid;
            ConfigurarFakeDbSet(fakeCps, new List<CodigoPostal> { cp }.AsQueryable());
            VendedorCodigoPostalGrupoProducto peluqueria = new VendedorCodigoPostalGrupoProducto
            { Empresa = "1", CodigoPostal = "28004", GrupoProducto = "PEL", Vendedor = "JE " };
            VendedorCodigoPostalGrupoProducto sobrante = new VendedorCodigoPostalGrupoProducto
            { Empresa = "1", CodigoPostal = "28004", GrupoProducto = "COC", Vendedor = "XX " };
            ConfigurarFakeDbSet(fakeVendedoresGrupo, new List<VendedorCodigoPostalGrupoProducto> { peluqueria, sobrante }.AsQueryable());
            CodigoPostalMantenimientoDTO dto = new CodigoPostalMantenimientoDTO
            {
                Empresa = "1",
                Numero = "28004",
                VendedoresGrupoProducto = new List<VendedorGrupoProductoCodigoPostalDTO>
                {
                    new VendedorGrupoProductoCodigoPostalDTO { GrupoProducto = "PEL", Vendedor = "AH" },
                    new VendedorGrupoProductoCodigoPostalDTO { GrupoProducto = "APA", Vendedor = "JM" }
                }
            };

            var resultado = controller.PutCodigoPostal(dto).Result as OkNegotiatedContentResult<CodigoPostalMantenimientoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("AH", peluqueria.Vendedor, "El grupo que ya existía cambia de vendedor");
            A.CallTo(() => fakeVendedoresGrupo.Remove(sobrante)).MustHaveHappenedOnceExactly();
            A.CallTo(() => fakeVendedoresGrupo.Add(A<VendedorCodigoPostalGrupoProducto>.That.Matches(
                v => v.GrupoProducto == "APA" && v.Vendedor == "JM" && v.CodigoPostal == "28004"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void CodigosPostales_Put_CodigoPostalInexistente_NotFound()
        {
            CodigoPostalMantenimientoDTO dto = new CodigoPostalMantenimientoDTO { Empresa = "1", Numero = "99999" };

            var resultado = controller.PutCodigoPostal(dto).Result;

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
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
