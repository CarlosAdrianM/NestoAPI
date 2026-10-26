using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#653 (Ariadna#35): las respuestas de reposiciones y diferencias traen la foto de cada producto, puesta por
    /// FichasProductoAlmacen (el único sitio) en una sola llamada por respuesta; sin foto, null.
    /// </summary>
    [TestClass]
    public class FotosReposicionesTests
    {
        private const string URL_CERA = "https://tienda/22624-home_default/lata-cera-oro.jpg";

        private IFotosProductoAlmacen fotos;
        private IServicioPreparacionReposicion preparacion;
        private IServicioDiferenciasReposicion diferencias;

        [TestInitialize]
        public void Preparar()
        {
            fotos = A.Fake<IFotosProductoAlmacen>();
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).Returns(new Dictionary<string, string>
            {
                ["22624"] = URL_CERA,
                ["18004"] = null
            });
            preparacion = A.Fake<IServicioPreparacionReposicion>();
            diferencias = A.Fake<IServicioDiferenciasReposicion>();
        }

        private ReposicionesController Controlador(bool conFichas = true)
        {
            IPermisoRellenarReposicionManual permiso = A.Fake<IPermisoRellenarReposicionManual>();
            A.CallTo(() => permiso.Puede(A<IPrincipal>._)).Returns(true);
            IFichasProductoAlmacen fichas = conFichas ? new FichasProductoAlmacen(A.Fake<IRepositorioFichasProducto>(), fotos) : null;
            return new ReposicionesController(null, preparacion, permisoManual: permiso, diferencias: diferencias, fichas: fichas)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre"), new Claim(ClaimTypes.Role, "Almacén") }, "Bearer"))
            };
        }

        private static ReposicionEnPreparacionDTO Reposicion()
        {
            return new ReposicionEnPreparacionDTO
            {
                Origen = "ALC",
                Destino = "ALG",
                Lineas = new List<LineaReposicionEnPreparacionDTO>
                {
                    new LineaReposicionEnPreparacionDTO { NumeroOrden = 1, Producto = "22624          ", Cantidad = 2 },
                    new LineaReposicionEnPreparacionDTO { NumeroOrden = 2, Producto = "18004", Cantidad = 1 }
                }
            };
        }

        private void UnaSolaLlamadaALasFotos()
        {
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).MustHaveHappenedOnceExactly();
        }

        // ---------------------------------------------------------------- FichasProductoAlmacen.CompletarFotos

        [TestMethod]
        public async Task CompletarFotos_PoneLaFoto_SinLeerLaFicha_YSinFotoQuedaNull()
        {
            IRepositorioFichasProducto repositorio = A.Fake<IRepositorioFichasProducto>();
            var conFoto = new LineaPropuestaReposicionDTO { Producto = "22624 " };
            var sinFoto = new LineaPropuestaReposicionDTO { Producto = "18004" };

            await new FichasProductoAlmacen(repositorio, fotos).CompletarFotos(new[] { conFoto, sinFoto, conFoto });

            Assert.AreEqual(URL_CERA, conFoto.UrlFoto);
            Assert.IsNull(sinFoto.UrlFoto);
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>.That.Matches(p => p.Count() == 2))).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.LeerFichas(A<string>._, A<IReadOnlyCollection<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CompletarFotos_SinServicioDeFotosOSinProductos_NoHaceNada()
        {
            var linea = new LineaPropuestaReposicionDTO { Producto = "22624" };

            await new FichasProductoAlmacen(A.Fake<IRepositorioFichasProducto>()).CompletarFotos(new[] { linea });
            await new FichasProductoAlmacen(A.Fake<IRepositorioFichasProducto>(), fotos).CompletarFotos(null);

            Assert.IsNull(linea.UrlFoto);
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- endpoints de reposiciones

        [TestMethod]
        public async Task GetEnPreparacion_TraeLaFotoDeCadaLinea()
        {
            A.CallTo(() => preparacion.LeerEnPreparacion("1", "ALC")).Returns(Reposicion());

            var ok = (OkNegotiatedContentResult<ReposicionEnPreparacionDTO>)await Controlador().GetEnPreparacion("ALC");

            Assert.AreEqual(URL_CERA, ok.Content.Lineas[0].UrlFoto);
            Assert.IsNull(ok.Content.Lineas[1].UrlFoto);
            UnaSolaLlamadaALasFotos();
        }

        [TestMethod]
        public async Task GetEnPreparacion_SinNinguna_Es404SinPreguntarFotos()
        {
            A.CallTo(() => preparacion.LeerEnPreparacion("1", "ALC")).Returns((ReposicionEnPreparacionDTO)null);

            IHttpActionResult resultado = await Controlador().GetEnPreparacion("ALC");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostCrear_LaReposicionCreadaTraeLaFoto()
        {
            A.CallTo(() => preparacion.Crear(A<CrearReposicionDTO>._, A<IPrincipal>._)).Returns(Reposicion());

            var creada = (NegotiatedContentResult<ReposicionEnPreparacionDTO>)await Controlador()
                .PostCrear(new CrearReposicionDTO { Origen = "ALG", Destino = "ALC" });

            Assert.AreEqual(HttpStatusCode.Created, creada.StatusCode);
            Assert.AreEqual(URL_CERA, creada.Content.Lineas[0].UrlFoto);
            Assert.IsNull(creada.Content.Lineas[1].UrlFoto);
            UnaSolaLlamadaALasFotos();
        }

        [TestMethod]
        public async Task PutCantidad_LaReposicionDevueltaTraeLaFoto()
        {
            A.CallTo(() => preparacion.CambiarCantidad("1", "ALC", 1, 1, A<IPrincipal>._)).Returns(Reposicion());

            var ok = (OkNegotiatedContentResult<ReposicionEnPreparacionDTO>)await Controlador()
                .PutCantidad(1, new CambiarCantidadReposicionDTO { Cantidad = 1 }, "ALC");

            Assert.AreEqual(URL_CERA, ok.Content.Lineas[0].UrlFoto);
            UnaSolaLlamadaALasFotos();
        }

        [TestMethod]
        public async Task PostTerminar_LasLineasTerminadasTraenLaFoto()
        {
            A.CallTo(() => preparacion.Terminar("1", "ALC", A<IPrincipal>._, A<IReadOnlyCollection<string>>._)).Returns(new ResultadoTerminarReposicionDTO
            {
                NumTraspaso = 81010,
                Lineas = new List<LineaTraspasoTerminadoDTO>
                {
                    new LineaTraspasoTerminadoDTO { Producto = "22624", Cantidad = 2 },
                    new LineaTraspasoTerminadoDTO { Producto = "18004", Cantidad = 1 }
                }
            });

            var ok = (OkNegotiatedContentResult<ResultadoTerminarReposicionDTO>)await Controlador().PostTerminar("ALC", null);

            Assert.AreEqual(URL_CERA, ok.Content.Lineas[0].UrlFoto);
            Assert.IsNull(ok.Content.Lineas[1].UrlFoto);
            UnaSolaLlamadaALasFotos();
        }

        [TestMethod]
        public async Task SinFichas_LaRespuestaSaleIgualSinFoto()
        {
            A.CallTo(() => preparacion.LeerEnPreparacion("1", "ALC")).Returns(Reposicion());

            var ok = (OkNegotiatedContentResult<ReposicionEnPreparacionDTO>)await Controlador(conFichas: false).GetEnPreparacion("ALC");

            Assert.AreEqual(2, ok.Content.Lineas.Count);
            Assert.IsTrue(ok.Content.Lineas.All(l => l.UrlFoto == null));
        }

        // ---------------------------------------------------------------- endpoints de diferencias

        [TestMethod]
        public async Task GetDiferencias_TraenLaFoto()
        {
            A.CallTo(() => diferencias.Listar("1", "Pendiente", "ALG", A<IPrincipal>._)).Returns(new List<DiferenciaReposicionDTO>
            {
                new DiferenciaReposicionDTO { Id = 1, Producto = "22624", Diferencia = -1 },
                new DiferenciaReposicionDTO { Id = 2, Producto = "18004", Diferencia = 1 }
            });

            var ok = (OkNegotiatedContentResult<List<DiferenciaReposicionDTO>>)await Controlador().GetDiferencias(almacen: "ALG");

            Assert.AreEqual(URL_CERA, ok.Content[0].UrlFoto);
            Assert.IsNull(ok.Content[1].UrlFoto);
            UnaSolaLlamadaALasFotos();
        }

        [TestMethod]
        public async Task GetDiferencia_TraeLaFoto()
        {
            A.CallTo(() => diferencias.Leer("1", 1, A<IPrincipal>._)).Returns(new DiferenciaReposicionDTO { Id = 1, Producto = "22624 ", Diferencia = -1 });

            var ok = (OkNegotiatedContentResult<DiferenciaReposicionDTO>)await Controlador().GetDiferencia(1);

            Assert.AreEqual(URL_CERA, ok.Content.UrlFoto);
        }

        [TestMethod]
        public async Task PostResolverDiferencia_LaDiferenciaResueltaTraeLaFoto()
        {
            A.CallTo(() => diferencias.Resolver("1", 1, A<ResolverDiferenciaReposicionDTO>._, A<IPrincipal>._)).Returns(new ResultadoResolverDiferenciaReposicionDTO
            {
                Diferencia = new DiferenciaReposicionDTO { Id = 1, Producto = "22624", Diferencia = -1 },
                Mensaje = "Cuadra."
            });

            var ok = (OkNegotiatedContentResult<ResultadoResolverDiferenciaReposicionDTO>)await Controlador()
                .PostResolverDiferencia(1, new ResolverDiferenciaReposicionDTO { Resolucion = "Confirmada" });

            Assert.AreEqual(URL_CERA, ok.Content.Diferencia.UrlFoto);
        }
    }
}
