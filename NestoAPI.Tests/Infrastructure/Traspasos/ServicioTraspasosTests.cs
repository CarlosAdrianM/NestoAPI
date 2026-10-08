using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models.Traspasos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Traspasos
{
    /// <summary>
    /// NestoAPI#553 (fase 1, corte a): propuesta de traspaso de solo lectura.
    /// </summary>
    [TestClass]
    public class ServicioTraspasosTests
    {
        private IRepositorioTraspasos repositorio;
        private ServicioTraspasos servicio;
        private static readonly DateTime AHORA = new DateTime(2026, 9, 29, 10, 0, 0);

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioTraspasos>();
            servicio = new ServicioTraspasos(repositorio, () => AHORA);
        }

        private static LineaReposicionStockSP Fila(string producto, short? cantidad) => new LineaReposicionStockSP
        {
            Empresa = "1",
            Número = producto.PadRight(15),
            Grupo = "COS",
            Texto = ("Producto " + producto).PadRight(50),
            Almacen = "REI",
            StockOrigen = 10,
            StockDestino = 1,
            CantidadMaximaDestino = 3,
            CantidadPendienteServirOrigen = 0,
            CantidadPendienteServirDestino = 0,
            CantidadReposicion = cantidad,
            Multiplos = 1
        };

        [TestMethod]
        public async Task LeerPropuesta_SoloDevuelveLineasConCantidadMayorQueCero_YRecortadas()
        {
            A.CallTo(() => repositorio.LeerPropuesta("1", "ALG", "REI")).Returns(new List<LineaReposicionStockSP>
            {
                Fila("222", 2), Fila("111", 1), Fila("333", 0), Fila("444", -3), Fila("555", null)
            });

            PropuestaTraspasoDTO propuesta = await servicio.LeerPropuesta("1", "ALG", "REI");

            CollectionAssert.AreEqual(new[] { "111", "222" }, propuesta.Lineas.Select(l => l.Producto).ToArray());
            Assert.AreEqual("Producto 111", propuesta.Lineas[0].Nombre);
            Assert.AreEqual(1, propuesta.Lineas[0].CantidadReposicion);
            Assert.AreEqual("1", propuesta.Empresa);
            Assert.AreEqual("ALG", propuesta.Origen);
            Assert.AreEqual("REI", propuesta.Destino);
            Assert.AreEqual(AHORA, propuesta.Fecha);
        }

        [TestMethod]
        public async Task LeerPropuesta_RecortaYPasaAMayusculasLosParametros()
        {
            A.CallTo(() => repositorio.LeerPropuesta(A<string>._, A<string>._, A<string>._))
                .Returns(new List<LineaReposicionStockSP>());

            await servicio.LeerPropuesta(" 1  ", " alg ", "rei  ");

            A.CallTo(() => repositorio.LeerPropuesta("1", "ALG", "REI")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LeerPropuesta_SinEmpresa_UsaLaEmpresaPorDefecto()
        {
            await servicio.LeerPropuesta(null, "ALC", "ALG");

            A.CallTo(() => repositorio.LeerPropuesta("1", "ALC", "ALG")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LeerPropuesta_OrigenIgualADestino_LanzaArgumentException_YNoLlamaAlSP()
        {
            await AssertLanzaArgumentException(() => servicio.LeerPropuesta("1", "ALG", "alg"));
            A.CallTo(() => repositorio.LeerPropuesta(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeerPropuesta_AlmacenQueNoEsDeReposicion_LanzaArgumentException()
        {
            await AssertLanzaArgumentException(() => servicio.LeerPropuesta("1", "ALG", "XXX"));
            await AssertLanzaArgumentException(() => servicio.LeerPropuesta("1", "TRA", "REI"));
            await AssertLanzaArgumentException(() => servicio.LeerPropuesta("1", null, "REI"));
            await AssertLanzaArgumentException(() => servicio.LeerPropuesta("1", "ALG", " "));
            await AssertLanzaArgumentException(() => servicio.LeerPropuesta("1", "ALG'; DROP TABLE x--", "REI"));
            A.CallTo(() => repositorio.LeerPropuesta(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeerPropuesta_EmpresaDemasiadoLarga_LanzaArgumentException()
        {
            await AssertLanzaArgumentException(() => servicio.LeerPropuesta("1234", "ALG", "REI"));
        }

        // ---------- Controlador ----------

        [TestMethod]
        public void TraspasosController_TieneAuthorizeDeClase()
        {
            Assert.IsTrue(typeof(TraspasosController).GetCustomAttributes<AuthorizeAttribute>(inherit: false).Any());
        }

        [TestMethod]
        public async Task GetPropuesta_DevuelveOkConLaPropuesta()
        {
            var fake = A.Fake<IServicioTraspasos>();
            var propuesta = new PropuestaTraspasoDTO { Empresa = "1", Origen = "ALG", Destino = "REI" };
            A.CallTo(() => fake.LeerPropuesta("1", "ALG", "REI")).Returns(propuesta);
            var controller = new TraspasosController(fake);

            IHttpActionResult resultado = await controller.GetPropuesta("1", "ALG", "REI");

            Assert.AreSame(propuesta, ((OkNegotiatedContentResult<PropuestaTraspasoDTO>)resultado).Content);
        }

        [TestMethod]
        public async Task GetPropuesta_ParametrosInvalidos_Devuelve400()
        {
            var controller = new TraspasosController(new ServicioTraspasos(repositorio));

            IHttpActionResult resultado = await controller.GetPropuesta("1", "REI", "REI");

            var badRequest = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequest);
            StringAssert.Contains(badRequest.Message, "no pueden ser el mismo");
        }

        private static async Task AssertLanzaArgumentException(Func<Task> accion)
        {
            try
            {
                await accion();
            }
            catch (ArgumentException)
            {
                return;
            }
            Assert.Fail("Tenía que lanzar ArgumentException");
        }
    }
}
