using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// DELETE api/Inventarios/Rellenar: deshacer el rellenado del inventario de una tienda (todas las filas pendientes de ese
    /// almacén y ese día, de cualquier usuario), solo Dirección e Informática y solo en almacenes sin ubicaciones.
    /// </summary>
    [TestClass]
    public class InventariosDeshacerRellenarTests
    {
        private const string USUARIO = @"NUEVAVISION\Carlos";
        private readonly DateTime dia = new DateTime(2026, 11, 12);

        private IRepositorioDeshacerRellenarInventario repositorio;
        private IUnidadDeshacerRellenarInventario unidad;
        private AlmacenInventario almacen;
        private List<BorradasInventarioUsuario> borradas;

        [TestInitialize]
        public void Inicializar()
        {
            almacen = new AlmacenInventario { Nombre = "Reina", ControlUbicaciones = false };
            borradas = new List<BorradasInventarioUsuario>
            {
                new BorradasInventarioUsuario { Usuario = @"NUEVAVISION\Paloma", Filas = 2900, FilasContadas = 10, UnidadesContadas = 25 },
                new BorradasInventarioUsuario { Usuario = @"NUEVAVISION\Laura", Filas = 50, FilasContadas = 2, UnidadesContadas = 6 }
            };
            repositorio = A.Fake<IRepositorioDeshacerRellenarInventario>();
            unidad = A.Fake<IUnidadDeshacerRellenarInventario>();
            A.CallTo(() => repositorio.LeerAlmacen(A<string>._, A<string>._)).ReturnsLazily(() => Task.FromResult(almacen));
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).Returns(Task.FromResult(unidad));
            A.CallTo(() => unidad.BorrarPendientes(A<string>._, A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(borradas));
            // Rellenar por error (25/11/26): lo que se comprueba antes de borrar, con las mismas filas
            A.CallTo(() => unidad.LeerPendientes(A<string>._, A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(borradas
                .Select(b => new ResumenPendientesInventarioUsuario { Usuario = b.Usuario, Filas = b.Filas, FilasContadas = b.FilasContadas, UnidadesContadas = b.UnidadesContadas })
                .ToList()));
        }

        private ServicioDeshacerRellenarInventario Servicio() => new ServicioDeshacerRellenarInventario(repositorio);

        // ---------- Servicio ----------

        [TestMethod]
        public async Task Deshacer_BorraLasPendientesDelDia_SumaLoContadoYDejaUnaSolaFilaDeRastro()
        {
            ResultadoDeshacerRellenarInventario resultado = await Servicio().Deshacer(null, " rei ", dia.AddHours(15), true, USUARIO);

            Assert.AreEqual(EstadoDeshacerRellenarInventario.Ok, resultado.Estado);
            Assert.AreEqual("REI", resultado.Respuesta.Almacen);
            Assert.AreEqual(dia, resultado.Respuesta.Fecha);
            Assert.AreEqual(2950, resultado.Respuesta.FilasBorradas);
            Assert.AreEqual(12, resultado.Respuesta.FilasContadas);
            Assert.AreEqual(31, resultado.Respuesta.UnidadesContadas);
            A.CallTo(() => repositorio.Empezar("1", "REI")).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.BorrarPendientes("1", "REI", dia)).MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Anotar(ServicioDeshacerRellenarInventario.TABLA_MODIFICACIONES,
                    A<string>.That.Matches(a => a.Contains("\"FilasBorradas\":2950") && a.Contains("\"Almacen\":\"REI\"") && a.Contains("2026-11-12")),
                    A<string>.That.Matches(n => n.Contains("2950 filas pendientes borradas") && n.Contains("12 con algo contado") && n.Contains("31 unidades")),
                    USUARIO))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Deshacer_SinNadaPendiente_CerosYSinRastro()
        {
            borradas.Clear();

            ResultadoDeshacerRellenarInventario resultado = await Servicio().Deshacer("1", "ALC", dia, true, USUARIO);

            Assert.AreEqual(EstadoDeshacerRellenarInventario.Ok, resultado.Estado);
            Assert.AreEqual(0, resultado.Respuesta.FilasBorradas);
            Assert.AreEqual(0, resultado.Respuesta.FilasContadas);
            Assert.AreEqual(0, resultado.Respuesta.UnidadesContadas);
            A.CallTo(() => unidad.Anotar(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Deshacer_AlmacenConUbicaciones_NoBorraNada()
        {
            almacen = new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true };

            ResultadoDeshacerRellenarInventario resultado = await Servicio().Deshacer("1", "ALG", dia, true, USUARIO);

            Assert.AreEqual(EstadoDeshacerRellenarInventario.ConUbicaciones, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "Algete se inventaría por ubicaciones");
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Deshacer_SinFecha_PeticionIncorrecta()
        {
            ResultadoDeshacerRellenarInventario resultado = await Servicio().Deshacer("1", "REI", null, true, USUARIO);

            Assert.AreEqual(EstadoDeshacerRellenarInventario.PeticionIncorrecta, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "Falta la fecha");
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Deshacer_SinPermiso_NoMiraNada()
        {
            ResultadoDeshacerRellenarInventario resultado = await Servicio().Deshacer("1", "REI", dia, false, USUARIO);

            Assert.AreEqual(EstadoDeshacerRellenarInventario.SinPermiso, resultado.Estado);
            A.CallTo(() => repositorio.LeerAlmacen(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void SqlBorrar_SoloPendientesDeEseAlmacenYDia()
        {
            string sql = RepositorioDeshacerRellenarInventarioSql.SQL_BORRAR;
            StringAssert.Contains(sql, "Estado = 1 AND NºTraspaso IS NULL");
            StringAssert.Contains(sql, "Almacén = @Almacen AND Fecha >= @Desde AND Fecha < @Hasta");
            Assert.IsFalse(sql.Contains("ExtractoProducto"));
        }

        // ---------- Controlador ----------

        private InventariosController Controlador(params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, USUARIO) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new InventariosController(A.Fake<NVEntities>(), A.Fake<IRepositorioCodigosBarras>())
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                ServicioDeshacerRellenar = Servicio(),
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"))
            };
        }

        [TestMethod]
        public async Task Delete_Direccion_200ConElResumen()
        {
            var resultado = await Controlador(Constantes.GruposSeguridad.DIRECCION).DeleteRellenar("REI", dia) as OkNegotiatedContentResult<DeshacerRellenarInventarioRespuestaDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2950, resultado.Content.FilasBorradas);
        }

        [TestMethod]
        public async Task Delete_Informatica_200()
        {
            var resultado = await Controlador(Constantes.GruposSeguridad.INFORMATICA).DeleteRellenar("REI", dia, "1");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<DeshacerRellenarInventarioRespuestaDTO>));
        }

        [TestMethod]
        public async Task Delete_AdministracionOTiendas_403()
        {
            foreach (string grupo in new[] { Constantes.GruposSeguridad.ADMINISTRACION, Constantes.GruposSeguridad.TIENDAS, Constantes.GruposSeguridad.ALMACEN })
            {
                var resultado = await Controlador(grupo).DeleteRellenar("REI", dia) as NegotiatedContentResult<HttpError>;

                Assert.IsNotNull(resultado, grupo);
                Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode, grupo);
            }
            A.CallTo(() => unidad.BorrarPendientes(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        // ---------- Rellenar por error (25/11/26): quien lo rellenó lo deshace ----------

        private InventariosController ControladorDe(string usuario, params string[] grupos)
        {
            InventariosController controller = Controlador(grupos);
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, usuario) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
            return controller;
        }

        [TestMethod]
        public async Task Delete_QuienLoRellenoYNadaContado_200()
        {
            borradas.Clear();
            borradas.Add(new BorradasInventarioUsuario { Usuario = @"NUEVAVISION\Paloma", Filas = 3032 });

            var resultado = await ControladorDe(@"NUEVAVISION\paloma", Constantes.GruposSeguridad.TIENDAS).DeleteRellenar("ALC", dia)
                as OkNegotiatedContentResult<DeshacerRellenarInventarioRespuestaDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(3032, resultado.Content.FilasBorradas);
            A.CallTo(() => unidad.Confirmar()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Delete_QuienLoRellenoConFilasContadas_409SinBorrar()
        {
            borradas.Clear();
            borradas.Add(new BorradasInventarioUsuario { Usuario = @"NUEVAVISION\Paloma", Filas = 3032, FilasContadas = 12, UnidadesContadas = 30 });

            var resultado = await ControladorDe(@"NUEVAVISION\Paloma", Constantes.GruposSeguridad.TIENDAS).DeleteRellenar("ALC", dia) as NegotiatedContentResult<HttpError>;

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            Assert.AreEqual("No puede deshacer el rellenado: ya hay 12 filas contadas: pídaselo a Dirección.", resultado.Content.Message);
            A.CallTo(() => unidad.BorrarPendientes(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => unidad.Confirmar()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Delete_ConFilasDeOtroUsuario_403DiciendoDeQuien()
        {
            var resultado = await ControladorDe(@"NUEVAVISION\Paloma", Constantes.GruposSeguridad.TIENDAS).DeleteRellenar("ALC", dia) as NegotiatedContentResult<HttpError>;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, "hay filas de otro usuario (Laura: 50)");
            A.CallTo(() => unidad.BorrarPendientes(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Delete_SinGrupoDeRellenar_403SinMirarNada()
        {
            var resultado = await ControladorDe(@"NUEVAVISION\Paloma", Constantes.GruposSeguridad.COMPRAS).DeleteRellenar("ALC", dia) as NegotiatedContentResult<HttpError>;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.Empezar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Regla_DireccionSiempre_LosDemasSoloLoSuyoSinContar()
        {
            var deOtro = new[] { new ResumenPendientesInventarioUsuario { Usuario = @"NUEVAVISION\Laura", Filas = 2 } };
            var suyoContado = new[] { new ResumenPendientesInventarioUsuario { Usuario = "Paloma", Filas = 2, FilasContadas = 1 } };
            var suyo = new[] { new ResumenPendientesInventarioUsuario { Usuario = @"NUEVAVISION\Paloma ", Filas = 2 } };

            Assert.IsTrue(ReglaDeshacerRellenadoPropio.Decidir(true, "Carlos", deOtro).Puede);
            Assert.AreEqual(HttpStatusCode.Forbidden, ReglaDeshacerRellenadoPropio.Decidir(false, @"NUEVAVISION\Paloma", deOtro).Estado);
            DecisionDeshacerRellenado contado = ReglaDeshacerRellenadoPropio.Decidir(false, @"NUEVAVISION\Paloma", suyoContado);
            Assert.AreEqual(HttpStatusCode.Conflict, contado.Estado);
            Assert.AreEqual("No puede deshacer el rellenado: ya hay 1 fila contada: pídaselo a Dirección.", contado.Mensaje);
            Assert.IsTrue(ReglaDeshacerRellenadoPropio.Decidir(false, @"NUEVAVISION\PALOMA", suyo).Puede);
            Assert.IsTrue(ReglaDeshacerRellenadoPropio.Decidir(false, "Paloma", new ResumenPendientesInventarioUsuario[0]).Puede);
            Assert.IsFalse(ReglaDeshacerRellenadoPropio.Decidir(false, null, suyo).Puede);
        }

        [TestMethod]
        public void SqlPendientesPorUsuario_ContadaEsStockRealDistintoDeCero_YBloqueaAlDeshacer()
        {
            StringAssert.Contains(SqlRellenadoInventario.SqlPendientesPorUsuario(true), "WITH (UPDLOCK, HOLDLOCK)");
            Assert.IsFalse(SqlRellenadoInventario.SqlPendientesPorUsuario(false).Contains("UPDLOCK"));
            StringAssert.Contains(SqlRellenadoInventario.SqlPendientesPorUsuario(false), "ISNULL(StockReal, 0) <> 0");
            StringAssert.Contains(SqlRellenadoInventario.SqlPendientesPorUsuario(false), "Estado = 1 AND NºTraspaso IS NULL");
        }

        [TestMethod]
        public async Task Delete_SinFechaOAlgete_400()
        {
            var sinFecha = await Controlador(Constantes.GruposSeguridad.DIRECCION).DeleteRellenar("REI", null) as NegotiatedContentResult<HttpError>;
            almacen = new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true };
            var algete = await Controlador(Constantes.GruposSeguridad.DIRECCION).DeleteRellenar("ALG", dia) as NegotiatedContentResult<HttpError>;

            Assert.AreEqual(HttpStatusCode.BadRequest, sinFecha.StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, algete.StatusCode);
        }
    }
}
