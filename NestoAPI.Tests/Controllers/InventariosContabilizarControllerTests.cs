using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Inventarios;
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

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#790: permisos de Comprobaciones, Contabilizar y Diferencias de api/Inventarios. Tiendas solo su almacén y a PVP;
    /// Administración, Dirección e Informática cualquiera; Almacén y Compras solo consultan; el coste nunca a las tiendas.
    /// </summary>
    [TestClass]
    public class InventariosContabilizarControllerTests
    {
        private static readonly DateTime DIA = new DateTime(2026, 11, 21);
        private IServicioContabilizarInventario servicio;
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
            servicio = A.Fake<IServicioContabilizarInventario>();
            almacenes = new Dictionary<string, string> { { "Paloma", "REI" }, { "Laura", "ALC" } };
            A.CallTo(() => servicio.Comprobar(A<string>._, A<string>._, A<DateTime>._))
                .ReturnsLazily((string e, string a, DateTime f) => new ComprobacionesInventarioDTO { Almacen = a, Fecha = f });
            A.CallTo(() => servicio.Diferencias(A<string>._, A<string>._, A<DateTime?>._, A<IList<int>>._, A<string>._))
                .ReturnsLazily((string e, string a, DateTime? f, IList<int> t, string v) => new DiferenciasInventarioDTO { Almacen = a, Valoracion = v });
            controller = new InventariosController(A.Fake<NVEntities>())
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                ServicioContabilizar = servicio,
                AlmacenDelUsuario = (empresa, usuario) => almacenes.TryGetValue(usuario, out string a) ? a : null
            };
        }

        private static HttpStatusCode Estado(IHttpActionResult r)
        {
            switch (r)
            {
                case NegotiatedContentResult<HttpError> e: return e.StatusCode;
                case NegotiatedContentResult<ComprobacionesInventarioDTO> c: return c.StatusCode;
                case BadRequestErrorMessageResult _: return HttpStatusCode.BadRequest;
                case OkNegotiatedContentResult<ComprobacionesInventarioDTO> _: return HttpStatusCode.OK;
                case OkNegotiatedContentResult<DiferenciasInventarioDTO> _: return HttpStatusCode.OK;
                case OkNegotiatedContentResult<ContabilizacionInventarioDTO> _: return HttpStatusCode.OK;
                default: throw new AssertFailedException("Resultado inesperado: " + r?.GetType().Name);
            }
        }

        // ---------- Comprobaciones ----------

        [TestMethod]
        public async Task Comprobaciones_TiendaSuAlmacen_Ok()
        {
            controller.User = Usuario("Paloma", "Tiendas");

            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.GetComprobaciones("REI", DIA)));
        }

        [TestMethod]
        public async Task Comprobaciones_TiendaOtroAlmacen_403()
        {
            controller.User = Usuario("Paloma", "Tiendas");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.GetComprobaciones("ALC", DIA)));
            A.CallTo(() => servicio.Comprobar(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobaciones_AlmacenYCompras_CualquierAlmacen()
        {
            controller.User = Usuario("Andre", "Almacén");
            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.GetComprobaciones("ALG", DIA)));

            controller.User = Usuario("Sancho", "Compras");
            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.GetComprobaciones("REI", DIA)));
        }

        [TestMethod]
        public async Task Comprobaciones_SinRolDeInventario_403()
        {
            controller.User = Usuario("Comercial", "Comerciales");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.GetComprobaciones("REI", DIA)));
        }

        [TestMethod]
        public async Task Comprobaciones_AlmacenQueNoExiste_404()
        {
            controller.User = Usuario("Carlos", "Dirección");
            A.CallTo(() => servicio.Comprobar("1", "XXX", DIA)).Returns(Task.FromResult<ComprobacionesInventarioDTO>(null));

            Assert.AreEqual(HttpStatusCode.NotFound, Estado(await controller.GetComprobaciones("XXX", DIA)));
        }

        // ---------- Contabilizar ----------

        private static ContabilizarInventarioParametrosDTO Peticion(string almacen) =>
            new ContabilizarInventarioParametrosDTO { Empresa = "1", Almacen = almacen, Fecha = DIA };

        [TestMethod]
        public async Task Contabilizar_ConBloqueantes_409ConLasComprobaciones()
        {
            controller.User = Usuario("Carlos", "Dirección");
            var bloqueado = new ComprobacionesInventarioDTO
            {
                Almacen = "ALG",
                Bloqueantes = new List<ComprobacionInventarioDTO> { new ComprobacionInventarioDTO { Codigo = CodigosComprobacionInventario.OTRA_FECHA } }
            };
            A.CallTo(() => servicio.Contabilizar("1", "ALG", DIA)).Returns(new ResultadoContabilizarInventario { Bloqueado = bloqueado });

            IHttpActionResult r = await controller.PostContabilizar(Peticion("ALG"));

            var conflicto = (NegotiatedContentResult<ComprobacionesInventarioDTO>)r;
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreSame(bloqueado, conflicto.Content);
        }

        [TestMethod]
        public async Task Contabilizar_TiendaSuAlmacen_Ok()
        {
            controller.User = Usuario("Paloma", "Tiendas");
            A.CallTo(() => servicio.Contabilizar("1", "REI", DIA)).Returns(new ResultadoContabilizarInventario
            {
                Contabilizacion = new ContabilizacionInventarioDTO
                {
                    Almacen = "REI",
                    Contabilizados = new List<UsuarioContabilizadoInventarioDTO> { new UsuarioContabilizadoInventarioDTO { Usuario = "NUEVAVISION\\Paloma", Filas = 300, Traspaso = 80001 } }
                }
            });

            var ok = (OkNegotiatedContentResult<ContabilizacionInventarioDTO>)await controller.PostContabilizar(Peticion("REI"));

            Assert.AreEqual(80001, ok.Content.Contabilizados.Single().Traspaso);
        }

        [TestMethod]
        public async Task Contabilizar_TiendaOtroAlmacen_403()
        {
            controller.User = Usuario("Paloma", "Tiendas");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.PostContabilizar(Peticion("ALC"))));
            A.CallTo(() => servicio.Contabilizar(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Contabilizar_AlmacenYCompras_SoloLectura_403()
        {
            controller.User = Usuario("Andre", "Almacén");
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.PostContabilizar(Peticion("ALG"))));

            controller.User = Usuario("Sancho", "Compras");
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.PostContabilizar(Peticion("ALG"))));

            A.CallTo(() => servicio.Contabilizar(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Contabilizar_AdministracionEInformatica_CualquierAlmacen()
        {
            A.CallTo(() => servicio.Contabilizar(A<string>._, A<string>._, A<DateTime>._)).Returns(new ResultadoContabilizarInventario
            {
                Contabilizacion = new ContabilizacionInventarioDTO
                {
                    Errores = new List<ErrorContabilizarInventarioDTO> { new ErrorContabilizarInventarioDTO { Usuario = "X", Mensaje = "falla" } }
                }
            });

            controller.User = Usuario("Alfredo", "Administración");
            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.PostContabilizar(Peticion("ALC"))));
            controller.User = Usuario("Carlos", "Informática");
            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.PostContabilizar(Peticion("ALG"))));
        }

        [TestMethod]
        public async Task Contabilizar_NadaPendiente_400()
        {
            controller.User = Usuario("Carlos", "Dirección");
            A.CallTo(() => servicio.Contabilizar("1", "REI", DIA)).Returns(new ResultadoContabilizarInventario { Contabilizacion = new ContabilizacionInventarioDTO() });

            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await controller.PostContabilizar(Peticion("REI"))));
        }

        // ---------- Diferencias ----------

        [TestMethod]
        public async Task Diferencias_Tienda_APvp()
        {
            controller.User = Usuario("Paloma", "Tiendas", "Comerciales");

            var ok = (OkNegotiatedContentResult<DiferenciasInventarioDTO>)await controller.GetDiferencias("REI", fecha: DIA);

            Assert.AreEqual(PermisosInventario.VALORACION_PVP, ok.Content.Valoracion);
            A.CallTo(() => servicio.Diferencias("1", "REI", DIA, A<IList<int>>._, PermisosInventario.VALORACION_PVP)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Diferencias_Direccion_ACostePorDefecto_YPuedePedirPvp()
        {
            controller.User = Usuario("Carlos", "Dirección");

            var coste = (OkNegotiatedContentResult<DiferenciasInventarioDTO>)await controller.GetDiferencias("ALG", fecha: DIA);
            var pvp = (OkNegotiatedContentResult<DiferenciasInventarioDTO>)await controller.GetDiferencias("ALG", fecha: DIA, valoracion: "pvp");

            Assert.AreEqual(PermisosInventario.VALORACION_COSTE, coste.Content.Valoracion);
            Assert.AreEqual(PermisosInventario.VALORACION_PVP, pvp.Content.Valoracion);
        }

        [TestMethod]
        public async Task Diferencias_AlmacenYCompras_ACoste()
        {
            controller.User = Usuario("Andre", "Almacén");
            var ok = (OkNegotiatedContentResult<DiferenciasInventarioDTO>)await controller.GetDiferencias("REI", fecha: DIA);

            Assert.AreEqual(PermisosInventario.VALORACION_COSTE, ok.Content.Valoracion);
        }

        [TestMethod]
        public async Task Diferencias_TiendaPideCoste_403()
        {
            controller.User = Usuario("Paloma", "Tiendas");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.GetDiferencias("REI", fecha: DIA, valoracion: "coste")));
            A.CallTo(() => servicio.Diferencias(A<string>._, A<string>._, A<DateTime?>._, A<IList<int>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Diferencias_TiendaOtroAlmacen_403()
        {
            controller.User = Usuario("Laura", "Tiendas");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.GetDiferencias("REI", fecha: DIA)));
        }

        [TestMethod]
        public async Task Diferencias_SinRol_403()
        {
            controller.User = Usuario("Comercial", "Comerciales");

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.GetDiferencias("REI", fecha: DIA)));
        }

        [TestMethod]
        public async Task Diferencias_SinFechaNiTraspaso_400()
        {
            controller.User = Usuario("Carlos", "Dirección");

            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await controller.GetDiferencias("REI")));
        }

        [TestMethod]
        public async Task Diferencias_ValoracionQueNoExiste_400()
        {
            controller.User = Usuario("Carlos", "Dirección");

            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await controller.GetDiferencias("REI", fecha: DIA, valoracion: "tarifa")));
        }

        [TestMethod]
        public async Task Diferencias_PorTraspasos_LosPasaAlServicio()
        {
            controller.User = Usuario("Carlos", "Dirección");

            _ = await controller.GetDiferencias("REI", traspaso: new[] { 80001, 80002, 80001 });

            A.CallTo(() => servicio.Diferencias("1", "REI", null, A<IList<int>>.That.Matches(t => t.Count == 2 && t.Contains(80001) && t.Contains(80002)),
                PermisosInventario.VALORACION_COSTE)).MustHaveHappenedOnceExactly();
        }
    }
}
