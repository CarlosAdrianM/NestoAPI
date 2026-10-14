using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Picking
{
    /// <summary>
    /// Sugerencia 575 (Alfredo, almacén): el aviso de ruta propia antes del picking (lectura con el alcance del picking y
    /// reglas) y el cambio de ruta que pide almacén desde él (solo a AT/16, solo si la ruta sigue siendo la que se vio, y con
    /// el usuario del JWT para LogModificaciones).
    /// </summary>
    [TestClass]
    public class GestorSugerenciasRutaTests
    {
        private static readonly DateTime FECHA_PICKING = new DateTime(2026, 10, 14, 15, 30, 0);
        private IRepositorioSugerenciasRuta repo;
        private GestorSugerenciasRuta gestor;
        private List<Exception> elmah;

        [TestInitialize]
        public void Inicializar()
        {
            repo = A.Fake<IRepositorioSugerenciasRuta>();
            elmah = new List<Exception>();
            gestor = new GestorSugerenciasRuta(repo) { RegistrarEnElmah = elmah.Add };
        }

        private static DatosPedidoSugerenciaRuta Candidato(int pedido, string empresa = "3  ", string ruta = "FW ", string cp = "28100", string rutaUltimo = null)
        {
            return new DatosPedidoSugerenciaRuta { Empresa = empresa, Pedido = pedido, Ruta = ruta, CodigoPostal = cp, RutaUltimoPedido = rutaUltimo, Cliente = "5825" };
        }

        // ===== Lectura

        [TestMethod]
        public async Task LeerSugerencias_DevuelveSoloLosQueCumplenAlgunaRegla_OrdenadosPorPedido()
        {
            A.CallTo(() => repo.LeerCandidatos(A<string>._, A<int?>._, A<string>._, A<DateTime>._))
                .Returns(new List<DatosPedidoSugerenciaRuta>
                {
                    Candidato(928300, cp: "28830"),
                    Candidato(928200, empresa: "1  ", cp: "28100"),      // empresa 1 sin más motivos: nada
                    Candidato(928100, empresa: "3  ", cp: "19200"),      // fuera de Madrid: nada
                    Candidato(928050, empresa: "3  ", cp: "19200", rutaUltimo: "16 "), // empresa 3 fuera de Madrid, último por la 16
                    Candidato(928040, empresa: "1  ", rutaUltimo: "16 ")  // empresa 1: el último pedido no cuenta (13/10/26)
                });

            List<SugerenciaRutaPedidoDTO> sugerencias = await gestor.LeerSugerencias(null, null, null, FECHA_PICKING);

            CollectionAssert.AreEqual(new[] { 928050, 928300 }, sugerencias.Select(s => s.Pedido).ToArray(),
                "Ordenadas por empresa y pedido");
            Assert.AreEqual("16", sugerencias[0].RutaPropuesta);
            Assert.AreEqual("AT", sugerencias[1].RutaPropuesta);
        }

        [TestMethod]
        public async Task LeerSugerencias_PickingDeRutas_SinFiltro_YConElDiaDelPicking()
        {
            await gestor.LeerSugerencias("1", 0, "  ", FECHA_PICKING);

            A.CallTo(() => repo.LeerCandidatos(null, null, null, FECHA_PICKING.Date)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LeerSugerencias_PickingDeUnPedido_FiltraPorEmpresaYPedido()
        {
            await gestor.LeerSugerencias("3 ", 928241, "5825", FECHA_PICKING);

            A.CallTo(() => repo.LeerCandidatos("3", 928241, null, FECHA_PICKING.Date)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LeerSugerencias_PickingDeUnPedidoSinEmpresa_UsaLaEmpresaPorDefecto()
        {
            await gestor.LeerSugerencias(null, 928241, null, FECHA_PICKING);

            A.CallTo(() => repo.LeerCandidatos("1", 928241, null, FECHA_PICKING.Date)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LeerSugerencias_PickingDeUnCliente_FiltraPorCliente()
        {
            await gestor.LeerSugerencias(null, null, " 5825 ", FECHA_PICKING);

            A.CallTo(() => repo.LeerCandidatos(null, null, "5825", FECHA_PICKING.Date)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LeerSugerencias_PedidosQueYaVanPorNuestraRuta_NoSalen()
        {
            A.CallTo(() => repo.LeerCandidatos(A<string>._, A<int?>._, A<string>._, A<DateTime>._))
                .Returns(new List<DatosPedidoSugerenciaRuta> { Candidato(1, ruta: "AT "), Candidato(2, ruta: "16 ", rutaUltimo: "AT") });

            Assert.AreEqual(0, (await gestor.LeerSugerencias(null, null, null, FECHA_PICKING)).Count);
        }

        // ===== Cambio de ruta

        private static CambioRutaPedidoRequest Cambio(int pedido, string rutaNueva, string rutaAnterior = "FW", string empresa = "3")
        {
            return new CambioRutaPedidoRequest { Empresa = empresa, Pedido = pedido, RutaAnterior = rutaAnterior, RutaNueva = rutaNueva };
        }

        [TestMethod]
        public async Task CambiarRutas_CambiaCadaPedidoConElUsuarioYElFormulario()
        {
            A.CallTo(() => repo.CambiarRuta(A<string>._, A<int>._, A<string>._, A<string>._, A<string>._, A<string>._)).Returns(1);

            List<ResultadoCambioRutaDTO> resultados = await gestor.CambiarRutas(
                new[] { Cambio(928241, "16"), Cambio(928038, " at ", rutaAnterior: "fw ") }, @"NUEVAVISION\Alfredo");

            Assert.IsTrue(resultados.All(r => r.Cambiado && r.Error == null));
            A.CallTo(() => repo.CambiarRuta("3", 928241, "FW", "16", @"NUEVAVISION\Alfredo", GestorSugerenciasRuta.FORMULARIO)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.CambiarRuta("3", 928038, "FW", "AT", @"NUEVAVISION\Alfredo", GestorSugerenciasRuta.FORMULARIO)).MustHaveHappenedOnceExactly();
        }

        [DataTestMethod]
        [DataRow("FW")]
        [DataRow("00")]
        [DataRow("")]
        [DataRow(null)]
        public async Task CambiarRutas_SoloANuestraRuta(string rutaNueva)
        {
            List<ResultadoCambioRutaDTO> resultados = await gestor.CambiarRutas(new[] { Cambio(928241, rutaNueva, rutaAnterior: "AT") }, "Alfredo");

            Assert.IsFalse(resultados.Single().Cambiado);
            StringAssert.Contains(resultados.Single().Error, "AT o 16");
            A.CallTo(() => repo.CambiarRuta(A<string>._, A<int>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CambiarRutas_SiLaRutaYaNoEsLaQueSeVio_NoSeTocaYSeDice()
        {
            A.CallTo(() => repo.CambiarRuta(A<string>._, A<int>._, A<string>._, A<string>._, A<string>._, A<string>._)).Returns(0);

            ResultadoCambioRutaDTO resultado = (await gestor.CambiarRutas(new[] { Cambio(928241, "16") }, "Alfredo")).Single();

            Assert.IsFalse(resultado.Cambiado);
            StringAssert.Contains(resultado.Error, "ya no va por la FW");
        }

        [TestMethod]
        public async Task CambiarRutas_SinRutaAnteriorOSinPedido_NoSeToca()
        {
            List<ResultadoCambioRutaDTO> resultados = await gestor.CambiarRutas(
                new[] { Cambio(928241, "16", rutaAnterior: " "), Cambio(0, "16") }, "Alfredo");

            Assert.IsTrue(resultados.All(r => !r.Cambiado && r.Error != null));
            A.CallTo(() => repo.CambiarRuta(A<string>._, A<int>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CambiarRutas_SiFallaUno_SigueConElRestoYLoDejaEnElmah()
        {
            A.CallTo(() => repo.CambiarRuta(A<string>._, 1, A<string>._, A<string>._, A<string>._, A<string>._)).ThrowsAsync(new InvalidOperationException("bloqueo"));
            A.CallTo(() => repo.CambiarRuta(A<string>._, 2, A<string>._, A<string>._, A<string>._, A<string>._)).Returns(1);

            List<ResultadoCambioRutaDTO> resultados = await gestor.CambiarRutas(new[] { Cambio(1, "16"), Cambio(2, "AT") }, "Alfredo");

            Assert.IsFalse(resultados[0].Cambiado);
            StringAssert.Contains(resultados[0].Error, "bloqueo");
            Assert.IsTrue(resultados[1].Cambiado);
            Assert.AreEqual(1, elmah.Count);
        }

        [TestMethod]
        public async Task CambiarRutas_SinUsuario_QuedaDesconocidoNuncaVacio()
        {
            A.CallTo(() => repo.CambiarRuta(A<string>._, A<int>._, A<string>._, A<string>._, A<string>._, A<string>._)).Returns(1);

            await gestor.CambiarRutas(new[] { Cambio(928241, "16", empresa: null) }, null);

            A.CallTo(() => repo.CambiarRuta("1", 928241, "FW", "16", NestoAPI.Infraestructure.UsuarioAuditoriaHelper.DESCONOCIDO, A<string>._)).MustHaveHappenedOnceExactly();
        }

        // ===== Controller

        private PickingController Controller(string usuario = @"NUEVAVISION\Alfredo", bool esCliente = false, bool deAlmacen = true)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, usuario) };
            if (deAlmacen)
            {
                claims.Add(new Claim(ClaimTypes.Role, @"NUEVAVISION\" + NestoAPI.Models.Constantes.GruposSeguridad.ALMACEN));
            }
            if (esCliente)
            {
                claims.Add(new Claim("cliente", "15191"));
            }
            return new PickingController
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt")),
                CrearGestorSugerenciasRuta = () => gestor,
                FechaPicking = () => FECHA_PICKING
            };
        }

        [TestMethod]
        public async Task Controller_CambiarRuta_UsaElUsuarioDelJwtYNoElQueMandaElCliente()
        {
            A.CallTo(() => repo.CambiarRuta(A<string>._, A<int>._, A<string>._, A<string>._, A<string>._, A<string>._)).Returns(1);

            var respuesta = await Controller().PostCambiarRuta(new CambiarRutaPedidosRequest
            {
                Usuario = @"NUEVAVISION\RDS2016$",
                Cambios = new List<CambioRutaPedidoRequest> { Cambio(928241, "16") }
            }) as OkNegotiatedContentResult<List<ResultadoCambioRutaDTO>>;

            Assert.IsNotNull(respuesta);
            Assert.IsTrue(respuesta.Content.Single().Cambiado);
            A.CallTo(() => repo.CambiarRuta("3", 928241, "FW", "16", @"NUEVAVISION\Alfredo", A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controller_CambiarRuta_SinCambios_BadRequest()
        {
            Assert.IsInstanceOfType(await Controller().PostCambiarRuta(new CambiarRutaPedidosRequest()), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await Controller().PostCambiarRuta(null), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Controller_UnClienteDeLaTienda_NoVeNiCambiaRutas()
        {
            var lectura = await Controller(esCliente: true).GetSugerenciasRuta() as StatusCodeResult;
            var cambio = await Controller(esCliente: true).PostCambiarRuta(new CambiarRutaPedidosRequest
            {
                Cambios = new List<CambioRutaPedidoRequest> { Cambio(928241, "16") }
            }) as StatusCodeResult;

            Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, lectura?.StatusCode);
            Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, cambio?.StatusCode);
            A.CallTo(repo).MustNotHaveHappened();
        }

        // Revisión 13/10/26: un vendedor (sin el grupo Almacén ni Dirección) no ve la propuesta ni cambia rutas.
        [TestMethod]
        public async Task Controller_UnVendedorSinPermisoDeAlmacen_NoVeNiCambiaRutas()
        {
            var lectura = await Controller(@"NUEVAVISION\Javier", deAlmacen: false).GetSugerenciasRuta() as StatusCodeResult;
            var cambio = await Controller(@"NUEVAVISION\Javier", deAlmacen: false).PostCambiarRuta(new CambiarRutaPedidosRequest
            {
                Cambios = new List<CambioRutaPedidoRequest> { Cambio(928241, "16") }
            }) as StatusCodeResult;

            Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, lectura?.StatusCode);
            Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, cambio?.StatusCode);
            A.CallTo(repo).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controller_SugerenciasRuta_ConElAlcanceDelPickingYElDiaDelPicking()
        {
            await Controller().GetSugerenciasRuta("3", 928241, null);
            await Controller().GetSugerenciasRuta(cliente: "5825");
            await Controller().GetSugerenciasRuta();

            A.CallTo(() => repo.LeerCandidatos("3", 928241, null, FECHA_PICKING.Date)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.LeerCandidatos(null, null, "5825", FECHA_PICKING.Date)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.LeerCandidatos(null, null, null, FECHA_PICKING.Date)).MustHaveHappenedOnceExactly();
        }

        // ===== Revisión 13/10/26: el contexto que crea el controlador se libera

        // Sin subclase de NVEntities en este ensamblado: EF buscaría aquí su DbConfiguration (la de integración) y fallaría
        private static bool EstaLiberado(NestoAPI.Models.NVEntities contexto)
        {
            try
            {
                _ = ((System.Data.Entity.Infrastructure.IObjectContextAdapter)contexto).ObjectContext;
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        [TestMethod]
        public void Controller_AlLiberarse_LiberaElContextoQueHaCreadoParaLasSugerenciasDeRuta()
        {
            var contextos = new List<NestoAPI.Models.NVEntities>();
            var controller = new PickingController
            {
                CrearContexto = () => { var contexto = new NestoAPI.Models.NVEntities(); contextos.Add(contexto); return contexto; }
            };

            // El gestor por defecto (el de producción) es el que pide el contexto; dos usos en la misma petición comparten uno.
            Assert.IsNotNull(controller.CrearGestorSugerenciasRuta());
            Assert.IsNotNull(controller.CrearGestorSugerenciasRuta());
            controller.Dispose();

            Assert.AreEqual(1, contextos.Count);
            Assert.IsTrue(EstaLiberado(contextos.Single()));
        }

        [TestMethod]
        public void Controller_SinHaberUsadoElContexto_NoLoCreaYSeLiberaSinErrores()
        {
            int creados = 0;
            var controller = new PickingController { CrearContexto = () => { creados++; return new NestoAPI.Models.NVEntities(); } };

            controller.Dispose();

            Assert.AreEqual(0, creados);
        }
    }
}
