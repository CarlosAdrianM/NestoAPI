using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#795: los endpoints de la pantalla de inventario de Ariadna (permisos, lectura del producto y códigos de respuesta) y
    /// los ajustes al GET/POST de #788 (filtro por ubicación y la ubicación que se graba).
    /// </summary>
    [TestClass]
    public class InventariosAriadnaControllerTests
    {
        private static readonly DateTime DIA = new DateTime(2026, 11, 21);
        private const string EAN = "8435454004144";

        private IServicioInventarioUbicaciones servicio;
        private IRepositorioCodigosBarras codigos;
        private NVEntities db;
        private DbSet<Inventario> fakeInventarios;
        private List<Inventario> anadidos;
        private InventariosController controller;
        private Dictionary<string, string> almacenes;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        [TestInitialize]
        public void Inicializar()
        {
            servicio = A.Fake<IServicioInventarioUbicaciones>();
            A.CallTo(() => servicio.Siguientes(A<string>._, A<string>._, A<int>._, A<DateTime>._))
                .Returns(new ResultadoInventarioUbicaciones<List<UbicacionParaInventarioDTO>> { Respuesta = new List<UbicacionParaInventarioDTO>() });
            A.CallTo(() => servicio.RellenarUbicacion(A<RellenarUbicacionInventarioDTO>._, A<string>._))
                .Returns(new ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO> { Respuesta = new RellenarInventarioFilasRespuestaDTO() });
            A.CallTo(() => servicio.RellenarParcial(A<RellenarParcialInventarioDTO>._, A<string>._, A<string>._))
                .Returns(new ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO> { Respuesta = new RellenarInventarioFilasRespuestaDTO() });
            A.CallTo(() => servicio.Contado(A<string>._, A<string>._, A<DateTime>._, A<FiltroInventarioContado>._))
                .Returns(new ResultadoInventarioUbicaciones<InventarioContadoDTO> { Respuesta = new InventarioContadoDTO() });

            codigos = A.Fake<IRepositorioCodigosBarras>();
            A.CallTo(() => codigos.LeerProducto(A<string>._, A<string>._)).Returns(Task.FromResult<Producto>(null));
            A.CallTo(() => codigos.ProductosConCodigo(A<string>._, A<string>._)).Returns(new List<ProductoPorCodigoBarrasDTO>());
            A.CallTo(() => codigos.LeerProducto("1", "40945"))
                .Returns(Task.FromResult(new Producto { Empresa = "1", Número = "40945          ", Nombre = "MASCARILLA", Familia = "Fam", Grupo = "COS", SubGrupo = "COS" }));
            A.CallTo(() => codigos.ProductosConCodigo("1", EAN)).Returns(new List<ProductoPorCodigoBarrasDTO>
            {
                new ProductoPorCodigoBarrasDTO { Producto = "40945", Nombre = "MASCARILLA", Cantidad = 1, Principal = true }
            });

            db = A.Fake<NVEntities>();
            fakeInventarios = A.Fake<DbSet<Inventario>>(o => o.Implements<IQueryable<Inventario>>().Implements<IDbAsyncEnumerable<Inventario>>());
            A.CallTo(() => db.Inventarios).Returns(fakeInventarios);
            Inventarios(new List<Inventario>());
            anadidos = new List<Inventario>();
            A.CallTo(() => fakeInventarios.Add(A<Inventario>._)).Invokes((Inventario i) => anadidos.Add(i)).ReturnsLazily((Inventario i) => i);

            almacenes = new Dictionary<string, string> { { "Paloma", "REI" } };
            controller = new InventariosController(db, codigos)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                ServicioUbicaciones = servicio,
                AlmacenDelUsuario = (empresa, usuario) => almacenes.TryGetValue(usuario, out string a) ? a : null,
                Hoy = () => DIA
            };
            controller.User = Usuario("Andre", "Almacén");
        }

        private static HttpStatusCode Estado(IHttpActionResult r)
        {
            switch (r)
            {
                case NegotiatedContentResult<HttpError> e: return e.StatusCode;
                case NegotiatedContentResult<YaRellenadoInventarioDTO> y: return y.StatusCode;
                case NegotiatedContentResult<ProductoLeidoInventarioDTO> p: return p.StatusCode;
                case BadRequestErrorMessageResult _: return HttpStatusCode.BadRequest;
                case OkNegotiatedContentResult<List<UbicacionParaInventarioDTO>> _: return HttpStatusCode.OK;
                case OkNegotiatedContentResult<RellenarInventarioFilasRespuestaDTO> _: return HttpStatusCode.OK;
                case OkNegotiatedContentResult<InventarioContadoDTO> _: return HttpStatusCode.OK;
                case OkNegotiatedContentResult<Inventario> _: return HttpStatusCode.OK;
                case NotFoundResult _: return HttpStatusCode.NotFound;
                default: throw new AssertFailedException("Resultado inesperado: " + r?.GetType().Name);
            }
        }

        // ---------- Siguientes ----------

        [TestMethod]
        public async Task Siguientes_Almacen_OkConElDiaDeHoyYElNumero()
        {
            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.GetUbicacionesSiguientes("ALG", "1", 20)));
            A.CallTo(() => servicio.Siguientes("1", "ALG", 20, DIA)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Siguientes_TiendaEnAlgete_403()
        {
            controller.User = Usuario("Paloma", "Tiendas");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.GetUbicacionesSiguientes("ALG")));
            A.CallTo(() => servicio.Siguientes(A<string>._, A<string>._, A<int>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Siguientes_Compras_403PorqueNoCuenta()
        {
            controller.User = Usuario("Maria", "Compras");

            var resultado = (NegotiatedContentResult<HttpError>)await controller.GetUbicacionesSiguientes("ALG");

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(PermisosInventario.MENSAJE_SIN_PERMISO_CONTAR, resultado.Content.Message);
        }

        [TestMethod]
        public async Task Siguientes_AlmacenSinUbicaciones_400ConElMensajeDelServicio()
        {
            A.CallTo(() => servicio.Siguientes(A<string>._, A<string>._, A<int>._, A<DateTime>._))
                .Returns(new ResultadoInventarioUbicaciones<List<UbicacionParaInventarioDTO>> { Estado = EstadoInventarioUbicaciones.AlmacenNoAdmite, Mensaje = "Reina no tiene control de ubicaciones" });

            var resultado = (BadRequestErrorMessageResult)await controller.GetUbicacionesSiguientes("REI");

            Assert.AreEqual("Reina no tiene control de ubicaciones", resultado.Message);
        }

        // ---------- RellenarUbicacion ----------

        [TestMethod]
        public async Task RellenarUbicacion_Almacen_OkConElUsuarioDelToken()
        {
            var peticion = new RellenarUbicacionInventarioDTO { Almacen = "ALG", Pasillo = "003", Fila = "001", Columna = "002" };

            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.PostRellenarUbicacion(peticion)));
            A.CallTo(() => servicio.RellenarUbicacion(peticion, "NUEVAVISION\\Andre")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RellenarUbicacion_YaRellenada_409ConLasLineas()
        {
            A.CallTo(() => servicio.RellenarUbicacion(A<RellenarUbicacionInventarioDTO>._, A<string>._))
                .Returns(new ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO>
                {
                    Estado = EstadoInventarioUbicaciones.YaRellenado,
                    Mensaje = "ya está rellenada",
                    Lineas = new List<LineaInventarioContadaDTO> { new LineaInventarioContadaDTO { NumeroOrden = 7 } }
                });

            var resultado = (NegotiatedContentResult<YaRellenadoInventarioDTO>)await controller.PostRellenarUbicacion(
                new RellenarUbicacionInventarioDTO { Almacen = "ALG", Pasillo = "003", Fila = "001", Columna = "002" });

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            Assert.AreEqual("ya está rellenada", resultado.Content.Message);
            Assert.AreEqual(7, resultado.Content.Lineas.Single().NumeroOrden);
        }

        [TestMethod]
        public async Task RellenarUbicacion_SinRol_403()
        {
            controller.User = Usuario("Pepe", "Comerciales");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.PostRellenarUbicacion(new RellenarUbicacionInventarioDTO { Almacen = "ALG" })));
        }

        [TestMethod]
        public async Task RellenarUbicacion_AlmacenQueNoExiste_404()
        {
            A.CallTo(() => servicio.RellenarUbicacion(A<RellenarUbicacionInventarioDTO>._, A<string>._))
                .Returns(new ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO> { Estado = EstadoInventarioUbicaciones.NoExisteAlmacen, Mensaje = "no existe" });

            Assert.AreEqual(HttpStatusCode.NotFound, Estado(await controller.PostRellenarUbicacion(new RellenarUbicacionInventarioDTO { Almacen = "XXX" })));
        }

        // ---------- RellenarParcial ----------

        [TestMethod]
        public async Task RellenarParcial_TiendaSuAlmacenPorCodigo_PasaLaReferencia()
        {
            controller.User = Usuario("Paloma", "Tiendas");
            var peticion = new RellenarParcialInventarioDTO { Almacen = "REI", Producto = EAN };

            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.PostRellenarParcial(peticion)));
            A.CallTo(() => servicio.RellenarParcial(peticion, "40945", "NUEVAVISION\\Paloma")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RellenarParcial_TiendaOtroAlmacen_403()
        {
            controller.User = Usuario("Paloma", "Tiendas");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.PostRellenarParcial(new RellenarParcialInventarioDTO { Almacen = "ALC", Familia = "Lisap" })));
            A.CallTo(() => servicio.RellenarParcial(A<RellenarParcialInventarioDTO>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RellenarParcial_Familia_SinReferencia()
        {
            var peticion = new RellenarParcialInventarioDTO { Almacen = "REI", Familia = "Lisap" };

            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.PostRellenarParcial(peticion)));
            A.CallTo(() => servicio.RellenarParcial(peticion, null, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RellenarParcial_ProductoInexistente_404ConTextoSinLlamarAlServicio()
        {
            var resultado = (NegotiatedContentResult<ProductoLeidoInventarioDTO>)await controller.PostRellenarParcial(
                new RellenarParcialInventarioDTO { Almacen = "REI", Producto = "NOEXISTE" });

            Assert.AreEqual(HttpStatusCode.NotFound, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, "NOEXISTE");
            A.CallTo(() => servicio.RellenarParcial(A<RellenarParcialInventarioDTO>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RellenarParcial_FamiliaYProducto_400()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await controller.PostRellenarParcial(
                new RellenarParcialInventarioDTO { Almacen = "REI", Familia = "Lisap", Producto = "40945" })));
        }

        // ---------- Contado ----------

        [TestMethod]
        public async Task Contado_Compras_PuedeConsultar_SinFechaEsHoy()
        {
            controller.User = Usuario("Maria", "Compras");

            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.GetContado("ALG", pasillo: "003")));
            A.CallTo(() => servicio.Contado("1", "ALG", DIA, A<FiltroInventarioContado>.That.Matches(f => f.Pasillo == "003" && f.Producto == null)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Contado_PorCodigoDeBarras_FiltraPorLaReferencia()
        {
            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.GetContado("REI", fecha: DIA.AddHours(8), producto: EAN)));
            A.CallTo(() => servicio.Contado("1", "REI", DIA, A<FiltroInventarioContado>.That.Matches(f => f.Producto == "40945")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Contado_TiendaOtroAlmacen_403()
        {
            controller.User = Usuario("Paloma", "Tiendas");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.GetContado("ALG")));
        }

        // ---------- GET/POST de #788 con ubicación ----------

        [TestMethod]
        public async Task GetInventario_ConUbicacion_DevuelveLaLineaDeEsaUbicacion()
        {
            Inventarios(new List<Inventario>
            {
                Linea(1, "003", "001", "002"),
                Linea(2, "012", "001", "005"),
                Linea(3, null, null, null)
            });

            var resultado = (OkNegotiatedContentResult<Inventario>)await controller.GetInventario("1", "ALG", DIA, EAN, " 012", "001", "005 ");

            Assert.AreEqual(2, resultado.Content.NºOrden);
        }

        [TestMethod]
        public async Task GetInventario_ConUbicacionSinLinea_404()
        {
            Inventarios(new List<Inventario> { Linea(1, "003", "001", "002") });

            Assert.AreEqual(HttpStatusCode.NotFound, Estado(await controller.GetInventario("1", "ALG", DIA, "40945", "003", "001", "003")));
        }

        [TestMethod]
        public async Task GetInventario_SinUbicacion_ComoSiempre()
        {
            Inventarios(new List<Inventario> { Linea(5, null, null, null) });

            var resultado = (OkNegotiatedContentResult<Inventario>)await controller.GetInventario("1", "REI", DIA, "40945");

            Assert.AreEqual(5, resultado.Content.NºOrden);
        }

        [TestMethod]
        public async Task PostInventario_ConUbicacion_GrabaPasilloFilaYColumna()
        {
            await controller.PostInventario(new Inventario
            {
                Empresa = "1", Almacén = "ALG", Fecha = DIA, Número = EAN, StockReal = 3, StockCalculado = 0,
                Pasillo = "003", Fila = "001", Columna = "002", Usuario = "NUEVAVISION\\Andre"
            });

            Inventario grabada = anadidos.Single();
            Assert.AreEqual("40945", grabada.Número.Trim());
            Assert.AreEqual("003", grabada.Pasillo);
            Assert.AreEqual("001", grabada.Fila);
            Assert.AreEqual("002", grabada.Columna);
            Assert.AreEqual(true, grabada.PorCodigoBarras);
        }

        private Inventario Linea(int orden, string pasillo, string fila, string columna) => new Inventario
        {
            NºOrden = orden, Empresa = "1", Almacén = orden == 5 ? "REI" : "ALG", Fecha = DIA.AddHours(9), Número = "40945          ", Estado = 1,
            Pasillo = pasillo, Fila = fila, Columna = columna
        };

        private void Inventarios(List<Inventario> filas)
        {
            IQueryable<Inventario> data = filas.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<Inventario>)fakeInventarios).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<Inventario>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<Inventario>)fakeInventarios).Provider).Returns(new TestDbAsyncQueryProvider<Inventario>(data.Provider));
            A.CallTo(() => ((IQueryable<Inventario>)fakeInventarios).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<Inventario>)fakeInventarios).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<Inventario>)fakeInventarios).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
