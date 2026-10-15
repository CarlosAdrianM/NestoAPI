using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#644: la tienda descarga cada noche la clasificación de más vendidos completa para
    /// ordenar por «Relevancia», y el ProductoDTO que se publica por Nesto_sync deja de llevar
    /// siempre un 0 en ClasificacionMasVendidos.
    /// </summary>
    [TestClass]
    public class ProductosClasificacionMasVendidosTests
    {
        private const string SETTING_TEST = "ApiKeyPrestashopTest644";

        private NVEntities db;
        private DbSet<ClasificacionMasVendido> fakeClasificacion;
        private DbSet<Producto> fakeProductos;
        private ProductosController controller;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeClasificacion = A.Fake<DbSet<ClasificacionMasVendido>>(o => o.Implements<IQueryable<ClasificacionMasVendido>>().Implements<IDbAsyncEnumerable<ClasificacionMasVendido>>());
            A.CallTo(() => db.ClasificacionMasVendidos).Returns(fakeClasificacion);
            A.CallTo(() => fakeClasificacion.AsNoTracking()).Returns(fakeClasificacion);
            ConfigurarFakeDbSet(fakeClasificacion, new List<ClasificacionMasVendido>().AsQueryable());

            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            A.CallTo(() => fakeProductos.Include(A<string>.Ignored)).Returns(fakeProductos);
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>().AsQueryable());

            controller = new ProductosController(db);
        }

        // ===== El endpoint =====

        [TestMethod]
        public async Task GetClasificacionMasVendidos_DevuelveTodasLasDeLaEmpresaOrdenadasYConTrim()
        {
            ConfigurarFakeDbSet(fakeClasificacion, new List<ClasificacionMasVendido>
            {
                new ClasificacionMasVendido { Empresa = "1", Producto = "35894          ", Posicion = 3 },
                new ClasificacionMasVendido { Empresa = "1", Producto = "17404          ", Posicion = 1 },
                new ClasificacionMasVendido { Empresa = "3", Producto = "99999          ", Posicion = 1 },
                new ClasificacionMasVendido { Empresa = "1", Producto = "44724          ", Posicion = 2 }
            }.AsQueryable());

            var resultado = await controller.GetClasificacionMasVendidos("1")
                as OkNegotiatedContentResult<List<ClasificacionMasVendidosDTO>>;

            Assert.IsNotNull(resultado);
            CollectionAssert.AreEqual(new[] { "17404", "44724", "35894" }, resultado.Content.Select(c => c.Producto).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, resultado.Content.Select(c => c.Posicion).ToArray());
        }

        [TestMethod]
        public async Task GetClasificacionMasVendidos_SinEmpresa_UsaLaDeDefecto()
        {
            ConfigurarFakeDbSet(fakeClasificacion, new List<ClasificacionMasVendido>
            {
                new ClasificacionMasVendido { Empresa = "1", Producto = "17404", Posicion = 1 },
                new ClasificacionMasVendido { Empresa = "3", Producto = "99999", Posicion = 1 }
            }.AsQueryable());

            var resultado = await controller.GetClasificacionMasVendidos()
                as OkNegotiatedContentResult<List<ClasificacionMasVendidosDTO>>;

            Assert.AreEqual(1, resultado.Content.Count);
            Assert.AreEqual("17404", resultado.Content[0].Producto);
        }

        [TestMethod]
        public async Task GetClasificacionMasVendidos_TablaVacia_ListaVaciaNoNull()
        {
            var resultado = await controller.GetClasificacionMasVendidos("1")
                as OkNegotiatedContentResult<List<ClasificacionMasVendidosDTO>>;

            Assert.IsNotNull(resultado.Content);
            Assert.AreEqual(0, resultado.Content.Count);
        }

        // ===== La API key =====

        [TestMethod]
        public void GetClasificacionMasVendidos_ExigeLaApiKeyDePrestashopEnXApiKey()
        {
            MethodInfo metodo = typeof(ProductosController).GetMethod(nameof(ProductosController.GetClasificacionMasVendidos));
            ApiKeyAttribute atributo = metodo.GetCustomAttributes<ApiKeyAttribute>(inherit: false).FirstOrDefault();

            Assert.IsNotNull(atributo, "Sin [ApiKey] el endpoint queda abierto a internet");
            Assert.AreEqual("ApiKeyPrestashop", atributo.NombreSetting);
            Assert.AreEqual("X-API-KEY", atributo.Cabecera);
        }

        [TestMethod]
        public void ApiKey_SinCabecera_401()
        {
            ConfigurationManager.AppSettings[SETTING_TEST] = "la-clave";
            HttpActionContext contexto = Contexto(null);

            new ApiKeyAttribute(SETTING_TEST, "X-API-KEY").OnAuthorization(contexto);

            Assert.IsNotNull(contexto.Response);
            Assert.AreEqual(HttpStatusCode.Unauthorized, contexto.Response.StatusCode);
        }

        [TestMethod]
        public void ApiKey_ConClaveMala_401()
        {
            ConfigurationManager.AppSettings[SETTING_TEST] = "la-clave";
            HttpActionContext contexto = Contexto("otra-clave");

            new ApiKeyAttribute(SETTING_TEST, "X-API-KEY").OnAuthorization(contexto);

            Assert.IsNotNull(contexto.Response);
            Assert.AreEqual(HttpStatusCode.Unauthorized, contexto.Response.StatusCode);
        }

        [TestMethod]
        public void ApiKey_ConLaClaveBuena_Pasa()
        {
            ConfigurationManager.AppSettings[SETTING_TEST] = "la-clave";
            HttpActionContext contexto = Contexto("la-clave");

            new ApiKeyAttribute(SETTING_TEST, "X-API-KEY").OnAuthorization(contexto);

            Assert.IsNull(contexto.Response);
        }

        private static HttpActionContext Contexto(string clave)
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.nuevavision.es/api/Productos/ClasificacionMasVendidos?empresa=1");
            request.SetConfiguration(new HttpConfiguration());
            if (clave != null)
            {
                request.Headers.Add("X-API-KEY", clave);
            }
            return new HttpActionContext
            {
                ControllerContext = new HttpControllerContext { Request = request }
            };
        }

        // ===== El campo del ProductoDTO que viaja por Nesto_sync =====

        [TestMethod]
        public async Task GetProductoFicha_RellenaLaClasificacionMasVendidos()
        {
            ConfigurarFakeDbSet(fakeProductos, new List<Producto> { ProductoDePrueba("41269") }.AsQueryable());
            ConfigurarFakeDbSet(fakeClasificacion, new List<ClasificacionMasVendido>
            {
                new ClasificacionMasVendido { Empresa = "1", Producto = "11111", Posicion = 1 },
                new ClasificacionMasVendido { Empresa = "1", Producto = "41269", Posicion = 57 }
            }.AsQueryable());

            var resultado = await controller.GetProducto("1", "41269", fichaCompleta: false)
                as OkNegotiatedContentResult<ProductoDTO>;

            Assert.AreEqual(57, resultado.Content.ClasificacionMasVendidos);
        }

        [TestMethod]
        public async Task GetProductoFicha_SinClasificacion_SeQuedaEn0()
        {
            ConfigurarFakeDbSet(fakeProductos, new List<Producto> { ProductoDePrueba("41269") }.AsQueryable());
            ConfigurarFakeDbSet(fakeClasificacion, new List<ClasificacionMasVendido>
            {
                new ClasificacionMasVendido { Empresa = "1", Producto = "11111", Posicion = 1 }
            }.AsQueryable());

            var resultado = await controller.GetProducto("1", "41269", fichaCompleta: false)
                as OkNegotiatedContentResult<ProductoDTO>;

            Assert.AreEqual(0, resultado.Content.ClasificacionMasVendidos);
        }

        private static Producto ProductoDePrueba(string numero) => new Producto
        {
            Empresa = "1",
            Número = numero,
            Nombre = "PRODUCTO DE PRUEBA",
            Grupo = "COS",
            SubGrupo = "CRE",
            Estado = 0,
            PVP = 10M,
            Kits = new List<Kit>()
        };

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
