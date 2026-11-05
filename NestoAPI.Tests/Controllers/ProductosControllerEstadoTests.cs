using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#738 (sugerencia 793, Manuel): endpoints del cambio de estado, con NVEntities y repositorio falsos.</summary>
    [TestClass]
    public class ProductosControllerEstadoTests
    {
        private IRepositorioEstadoProductos repositorio;
        private ProductosController controller;

        [TestInitialize]
        public void Setup()
        {
            NVEntities db = A.Fake<NVEntities>();
            repositorio = A.Fake<IRepositorioEstadoProductos>();
            A.CallTo(() => repositorio.LeerEstados(A<string>._)).Returns(Task.FromResult(new List<EstadoProductoDTO>
            {
                new EstadoProductoDTO { Estado = 0, Descripcion = "DISPONIBLE" },
                new EstadoProductoDTO { Estado = 3, Descripcion = "EXTINGUIR", AExtinguir = true }
            }));
            A.CallTo(() => repositorio.Cargar(A<string>._, A<IList<string>>._)).Returns(Task.FromResult(
                new Dictionary<string, DatosEstadoProducto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["17404"] = new DatosEstadoProducto { Producto = "17404", Estado = 0 }
                }));
            A.CallTo(() => repositorio.ProductosDelFiltro(A<string>._, A<FiltroEstadoProductos>._)).Returns(Task.FromResult(new List<string> { "17404" }));
            A.CallTo(() => repositorio.Aplicar(A<string>._, A<IList<DecisionEstadoProducto>>._, A<string>._))
                .Returns(Task.FromResult(new ResultadoAplicacionEstado { Cambiados = 1 }));
            controller = new ProductosController(db, A.Fake<IGestorSincronizacion>(), null, new ServicioEstadoProductos(repositorio));
        }

        private void Usuario(params string[] roles)
        {
            List<Claim> claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Manuel") };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            controller.RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT")) };
        }

        private static CambioEstadoProductosPeticion Peticion(short estado) => new CambioEstadoProductosPeticion
        {
            Empresa = "1",
            Productos = new List<string> { "17404" },
            EstadoNuevo = estado,
            Usuario = "otro"
        };

        [TestMethod]
        public async Task PostEstado_SinSerDeComprasNiDireccion_403ConMensaje()
        {
            Usuario("Almacén");

            var resultado = await controller.PostEstado(Peticion(3)) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(ReglasEstadoProducto.MENSAJE_SIN_PERMISO, resultado.Content);
            A.CallTo(() => repositorio.Aplicar(A<string>._, A<IList<DecisionEstadoProducto>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostEstado_EstadoQueNoExiste_400()
        {
            Usuario("Compras");

            var resultado = await controller.PostEstado(Peticion(8)) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("El estado 8 no existe en la tabla de estados de los productos.", resultado.Message);
        }

        [TestMethod]
        public async Task PostEstado_Direccion_CambiaConElUsuarioDelToken()
        {
            Usuario("NUEVAVISION\\Dirección");

            var resultado = await controller.PostEstado(Peticion(3)) as OkNegotiatedContentResult<ResultadoCambioEstadoProductosDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Cambiados);
            A.CallTo(() => repositorio.Aplicar("1", A<IList<DecisionEstadoProducto>>._, "NUEVAVISION\\Manuel")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostEstadoVistaPrevia_SinPermiso_DevuelveLaVistaConPuedeCambiarFalse()
        {
            Usuario("Almacén");

            var resultado = await controller.PostEstadoVistaPrevia(new VistaPreviaEstadoProductosPeticion
            {
                Filtro = new FiltroEstadoProductos { Productos = new List<string> { "17404" } },
                EstadoNuevo = 3
            }) as OkNegotiatedContentResult<VistaPreviaEstadoProductosDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsFalse(resultado.Content.PuedeCambiar);
            Assert.AreEqual("17404", resultado.Content.Productos.Single().Producto);
        }

        [TestMethod]
        public async Task PostEstadoVistaPrevia_SinFiltro_400()
        {
            Usuario("Compras");

            var resultado = await controller.PostEstadoVistaPrevia(new VistaPreviaEstadoProductosPeticion { EstadoNuevo = 3 }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(ServicioEstadoProductos.MENSAJE_SIN_FILTRO, resultado.Message);
        }

        [TestMethod]
        public async Task GetEstadosProducto_DevuelveLosDeLaTabla()
        {
            Usuario();

            var resultado = await controller.GetEstadosProducto("1") as OkNegotiatedContentResult<List<EstadoProductoDTO>>;

            Assert.AreEqual(2, resultado.Content.Count);
        }
    }
}
