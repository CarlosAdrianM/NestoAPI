using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Inventarios
{
    /// <summary>
    /// Épica #784 (incidencia 1186): DELETE api/Inventarios/Filas/{numeroOrden} borra una fila pendiente; y RellenarUbicacion ya no da
    /// por rellenada una ubicación que solo tiene la fila de un «Dejar a cero».
    /// </summary>
    [TestClass]
    public class InventarioBorrarFilaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 11);

        private IRepositorioBorrarFilaInventario repositorio;
        private IUnidadBorrarFilaInventario unidad;
        private ServicioBorrarFilaInventario servicio;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioBorrarFilaInventario>();
            unidad = A.Fake<IUnidadBorrarFilaInventario>();
            A.CallTo(() => repositorio.Empezar(A<string>._)).Returns(unidad);
            A.CallTo(() => unidad.BorrarSiPendiente(A<int>._)).Returns(1);
            servicio = new ServicioBorrarFilaInventario(repositorio);
        }

        private static FilaInventarioBorrar Fila(int numero = 948787, string almacen = "ALG", DateTime? fecha = null, int estado = 1, int? traspaso = null,
            string pasillo = null) => new FilaInventarioBorrar
            {
                NumeroOrden = numero,
                Empresa = "1",
                Almacen = almacen,
                Fecha = fecha ?? HOY.AddHours(10),
                Producto = "44901",
                Pasillo = pasillo,
                Fila = pasillo == null ? null : "002",
                Columna = pasillo == null ? null : "005",
                StockCalculado = 3,
                StockReal = 1,
                Estado = estado,
                Traspaso = traspaso,
                Usuario = "NUEVAVISION\\Alfredo"
            };

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private InventariosController Controlador(string nombre, params string[] grupos)
        {
            return new InventariosController(A.Fake<NVEntities>(), A.Fake<IRepositorioCodigosBarras>())
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                ServicioBorrarFila = servicio,
                AlmacenDelUsuario = (empresa, usuario) => usuario == "Paloma" ? "REI" : null,
                Hoy = () => HOY,
                User = Usuario(nombre, grupos)
            };
        }

        private static HttpStatusCode Codigo(IHttpActionResult resultado)
        {
            switch (resultado)
            {
                case StatusCodeResult s: return s.StatusCode;
                case NegotiatedContentResult<HttpError> e: return e.StatusCode;
                default: throw new AssertFailedException("Resultado inesperado: " + resultado?.GetType().Name);
            }
        }

        [TestMethod]
        public async Task Pendiente_DeHoy_Almacen_204YRastroEnModificaciones()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila());

            IHttpActionResult resultado = await Controlador("Alfredo", "Almacén").DeleteFila(948787);

            Assert.AreEqual(HttpStatusCode.NoContent, Codigo(resultado));
            A.CallTo(() => unidad.BorrarSiPendiente(948787)).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Anotar("Inventarios",
                A<string>.That.Matches(s => s.Contains("948787") && s.Contains("44901") && s.Contains("ALG") && s.Contains("sin ubicación")
                    && s.Contains("teórico 3") && s.Contains("contado 1")),
                A<string>.That.Contains("borrada"),
                "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Empezar("InventarioRellenar:1:ALG")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ConUbicacion_BloqueaTambienLaUbicacionYElRastroLaLleva()
        {
            A.CallTo(() => repositorio.Leer(948796)).Returns(Fila(948796, pasillo: "001"));

            Assert.AreEqual(HttpStatusCode.NoContent, Codigo(await Controlador("Alfredo", "Almacén").DeleteFila(948796)));

            A.CallTo(() => unidad.Bloquear("InventarioRellenar:1:ALG:001:002:005")).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Anotar(A<string>._, A<string>.That.Contains("001-002-005"), A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task NoExiste_404()
        {
            A.CallTo(() => repositorio.Leer(1)).Returns(Task.FromResult<FilaInventarioBorrar>(null));

            Assert.AreEqual(HttpStatusCode.NotFound, Codigo(await Controlador("Alfredo", "Almacén").DeleteFila(1)));
            A.CallTo(() => repositorio.Empezar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Contabilizada_409YNoBorraNiParaDireccion()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(estado: 2, traspaso: 81350));

            var resultado = (NegotiatedContentResult<HttpError>)await Controlador("Carlos", "Dirección").DeleteFila(948787);

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, "contabilizada (traspaso 81350)");
            A.CallTo(() => unidad.BorrarSiPendiente(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DeOtroDia_Almacen_403ConMotivoClaro()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(fecha: HOY.AddDays(-1)));

            var resultado = (NegotiatedContentResult<HttpError>)await Controlador("Alfredo", "Almacén").DeleteFila(948787);

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, "10/11/2026");
            StringAssert.Contains(resultado.Content.Message, ServicioBorrarFilaInventario.MENSAJE_SOLO_HOY);
            A.CallTo(() => repositorio.Empezar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DeOtroDia_Direccion_204()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(fecha: HOY.AddDays(-3)));

            Assert.AreEqual(HttpStatusCode.NoContent, Codigo(await Controlador("Carlos", "Dirección").DeleteFila(948787)));
        }

        [TestMethod]
        public async Task TiendaDeOtroAlmacen_403()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(almacen: "ALG"));

            var resultado = (NegotiatedContentResult<HttpError>)await Controlador("Paloma", "Tiendas").DeleteFila(948787);

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, "REI");
            A.CallTo(() => unidad.BorrarSiPendiente(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task TiendaDeSuAlmacen_204()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila(almacen: "REI"));

            Assert.AreEqual(HttpStatusCode.NoContent, Codigo(await Controlador("Paloma", "Tiendas").DeleteFila(948787)));
        }

        [TestMethod]
        public async Task Compras_NoCuenta_403()
        {
            A.CallTo(() => repositorio.Leer(948787)).Returns(Fila());

            Assert.AreEqual(HttpStatusCode.Forbidden, Codigo(await Controlador("Maria", "Compras").DeleteFila(948787)));
        }

        [TestMethod]
        public async Task SeContabilizaMientrasTanto_409YNoConfirma()
        {
            A.CallTo(() => repositorio.Leer(948787)).ReturnsNextFromSequence(Fila(), Fila(estado: 2, traspaso: 81351));
            A.CallTo(() => unidad.BorrarSiPendiente(948787)).Returns(0);

            Assert.AreEqual(HttpStatusCode.Conflict, Codigo(await Controlador("Alfredo", "Almacén").DeleteFila(948787)));
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
            A.CallTo(() => unidad.Anotar(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        // ---------- Comprobaciones: cada fila lleva su NumeroOrden ----------

        [TestMethod]
        public void Comprobaciones_SinUbicacion_LaFilaLlevaNumeroOrden()
        {
            ComprobacionesInventarioDTO comprobaciones = ComprobadorInventario.Comprobar("ALG", HOY, true,
                new[] { new FilaInventarioPendiente { NumeroOrden = 948787, Producto = "44901", Fecha = HOY, Ubicar = true, StockReal = 1, StockCalculado = 3 } },
                new List<UbicacionAlmacenInventario>());

            ComprobacionInventarioDTO sinUbicacion = comprobaciones.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.SIN_UBICACION);
            Assert.AreEqual(948787, sinUbicacion.Filas.Single().NumeroOrden);
            Assert.AreEqual(948787, sinUbicacion.Filas.Single().NºOrden);
        }

        // ---------- RellenarUbicacion tras un «Dejar a cero» ----------

        [TestMethod]
        public async Task RellenarUbicacion_TresProductosUnoYaConFilaACero_CreaLasDosQueFaltanSinDuplicar()
        {
            var repoUbicaciones = A.Fake<IRepositorioInventarioUbicaciones>();
            var unidadUbicaciones = A.Fake<IUnidadInventarioUbicaciones>();
            var insertadas = new List<Inventario>();
            var yaHabia = new LineaInventarioContadaDTO { NumeroOrden = 948796, Producto = "44901", StockCalculado = 2, StockReal = 0, Pasillo = "001", Fila = "002", Columna = "005" };
            A.CallTo(() => repoUbicaciones.LeerAlmacen("1", "ALG")).Returns(new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => repoUbicaciones.Empezar(A<string>._)).Returns(unidadUbicaciones);
            A.CallTo(() => unidadUbicaciones.Insertar(A<IList<Inventario>>._)).Invokes((IList<Inventario> f) => insertadas.AddRange(f));
            A.CallTo(() => unidadUbicaciones.Pendientes(A<string>._, A<string>._, A<DateTime>._, A<FiltroInventarioContado>._))
                .ReturnsLazily(() => new[] { yaHabia }.Concat(insertadas.Select((f, n) => new LineaInventarioContadaDTO
                {
                    NumeroOrden = 949000 + n, Producto = f.Número, StockCalculado = f.StockCalculado ?? 0, Pasillo = f.Pasillo
                })).ToList());
            A.CallTo(() => unidadUbicaciones.TeoricoUbicacion("1", "ALG", "001", "002", "005")).Returns(new List<StockProductoInventario>
            {
                new StockProductoInventario { Numero = "44901", Stock = 2 },
                new StockProductoInventario { Numero = "27534", Stock = 3 },
                new StockProductoInventario { Numero = "40245 ", Stock = 1 }
            });

            var resultado = await new ServicioInventarioUbicaciones(repoUbicaciones).RellenarUbicacion(new RellenarUbicacionInventarioDTO
            {
                Empresa = "1", Almacen = "ALG", Fecha = HOY, Pasillo = "001", Fila = "002", Columna = "005"
            }, "NUEVAVISION\\Andre");

            Assert.AreEqual(EstadoInventarioUbicaciones.Ok, resultado.Estado);
            CollectionAssert.AreEquivalent(new[] { "27534", "40245" }, insertadas.Select(f => f.Número).ToArray());
            Assert.AreEqual(2, resultado.Respuesta.Filas);
            Assert.AreEqual(3, resultado.Respuesta.Lineas.Count);
            A.CallTo(() => unidadUbicaciones.Confirmar()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RellenarUbicacion_TodosConFila_SigueSiendoYaRellenado()
        {
            var repoUbicaciones = A.Fake<IRepositorioInventarioUbicaciones>();
            var unidadUbicaciones = A.Fake<IUnidadInventarioUbicaciones>();
            A.CallTo(() => repoUbicaciones.LeerAlmacen("1", "ALG")).Returns(new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => repoUbicaciones.Empezar(A<string>._)).Returns(unidadUbicaciones);
            A.CallTo(() => unidadUbicaciones.Pendientes(A<string>._, A<string>._, A<DateTime>._, A<FiltroInventarioContado>._))
                .Returns(new List<LineaInventarioContadaDTO> { new LineaInventarioContadaDTO { NumeroOrden = 1, Producto = "44901" } });
            A.CallTo(() => unidadUbicaciones.TeoricoUbicacion("1", "ALG", "001", "002", "005"))
                .Returns(new List<StockProductoInventario> { new StockProductoInventario { Numero = "44901", Stock = 2 } });

            var resultado = await new ServicioInventarioUbicaciones(repoUbicaciones).RellenarUbicacion(new RellenarUbicacionInventarioDTO
            {
                Empresa = "1", Almacen = "ALG", Fecha = HOY, Pasillo = "001", Fila = "002", Columna = "005"
            }, "NUEVAVISION\\Andre");

            Assert.AreEqual(EstadoInventarioUbicaciones.YaRellenado, resultado.Estado);
            A.CallTo(() => unidadUbicaciones.Insertar(A<IList<Inventario>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Siguientes_UbicacionConMenosFilasQueProductos_NoEstaYaRellenada()
        {
            var repoUbicaciones = A.Fake<IRepositorioInventarioUbicaciones>();
            A.CallTo(() => repoUbicaciones.LeerAlmacen("1", "ALG")).Returns(new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => repoUbicaciones.UbicacionesParaInventario(2)).Returns(new List<UbicacionPrioridadInventario>
            {
                new UbicacionPrioridadInventario { Pasillo = "001", Fila = "002", Columna = "005" },
                new UbicacionPrioridadInventario { Pasillo = "003", Fila = "001", Columna = "002" }
            });
            A.CallTo(() => repoUbicaciones.ProductosPorUbicacion("ALG")).Returns(new List<UbicacionConProductos>
            {
                new UbicacionConProductos { Pasillo = "001", Fila = "002", Columna = "005", Productos = 3 },
                new UbicacionConProductos { Pasillo = "003", Fila = "001", Columna = "002", Productos = 2 }
            });
            A.CallTo(() => repoUbicaciones.UbicacionesRellenadas("1", "ALG", HOY)).Returns(new List<UbicacionConProductos>
            {
                new UbicacionConProductos { Pasillo = "001", Fila = "002", Columna = "005", Productos = 1 },
                new UbicacionConProductos { Pasillo = "003", Fila = "001", Columna = "002", Productos = 2 }
            });

            var resultado = await new ServicioInventarioUbicaciones(repoUbicaciones).Siguientes("1", "ALG", 2, HOY);

            Assert.IsFalse(resultado.Respuesta[0].YaRellenadaHoy);
            Assert.IsTrue(resultado.Respuesta[1].YaRellenadaHoy);
        }
    }
}
