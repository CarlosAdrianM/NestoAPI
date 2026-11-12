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
