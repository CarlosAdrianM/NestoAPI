using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Globos;
using NestoAPI.Models.Globos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#774: api/Globos (los pendientes del usuario y «visto» por usuario, idempotente).</summary>
    [TestClass]
    public class GlobosControllerTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 2);
        private IRepositorioGlobos repositorio;

        [TestInitialize]
        public void Preparar() => repositorio = A.Fake<IRepositorioGlobos>();

        private GlobosController Controlador(IPrincipal usuario)
            => new GlobosController(repositorio) { User = usuario, Hoy = () => HOY };

        private static IPrincipal Usuario(string nombre, params string[] roles)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        }

        private static GloboFila Globo(int id, string pantalla = "Reparto", string grupos = null, string version = null,
            DateTime? desde = null, DateTime? hasta = null, short orden = 0, bool activo = true, string app = "Ariadna")
            => new GloboFila
            {
                Id = id,
                Aplicacion = app,
                Pantalla = pantalla,
                Ancla = pantalla + ".Control" + id,
                Texto = "Texto " + id,
                Grupos = grupos,
                VersionDesde = version,
                FechaDesde = desde ?? HOY.AddDays(-1),
                FechaHasta = hasta,
                Orden = orden,
                Activo = activo
            };

        private List<GloboDTO> Pedir(IPrincipal usuario, string pantalla = "Reparto", string version = null, string app = "Ariadna")
        {
            IHttpActionResult resultado = Controlador(usuario).GetGlobos(app, pantalla, version);
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<GloboDTO>>));
            return ((OkNegotiatedContentResult<List<GloboDTO>>)resultado).Content;
        }

        // ---- Filtro de pendientes ----

        [TestMethod]
        public void Get_SoloLosNoVistosPorEseUsuario_ConElUsuarioSinDominio()
        {
            _ = A.CallTo(() => repositorio.LeerActivos("Ariadna", "Reparto")).Returns(new List<GloboFila> { Globo(1), Globo(2) });
            _ = A.CallTo(() => repositorio.LeerVistos("Alfredo", A<IReadOnlyCollection<int>>._)).Returns(new HashSet<int> { 1 });

            List<GloboDTO> globos = Pedir(Usuario("NUEVAVISION\\Alfredo"));

            CollectionAssert.AreEqual(new[] { 2 }, globos.Select(g => g.Id).ToList());
        }

        [TestMethod]
        public void Get_FueraDeFechas_NoSale_YLasFechasSonInclusivas()
        {
            _ = A.CallTo(() => repositorio.LeerActivos(A<string>._, A<string>._)).Returns(new List<GloboFila>
            {
                Globo(1, desde: HOY.AddDays(1)),                  // aún no
                Globo(2, desde: HOY.AddDays(-10), hasta: HOY.AddDays(-1)), // ya pasó
                Globo(3, desde: HOY, hasta: HOY),                 // solo hoy
                Globo(4, desde: HOY.AddDays(-3), hasta: null)     // sin fin
            });

            CollectionAssert.AreEqual(new[] { 3, 4 }, Pedir(Usuario("pedro")).Select(g => g.Id).ToList());
        }

        [TestMethod]
        public void Get_PorGrupos_RolSinDominioYSinTildes_OUsuario_OVacioParaTodos()
        {
            _ = A.CallTo(() => repositorio.LeerActivos(A<string>._, A<string>._)).Returns(new List<GloboFila>
            {
                Globo(1, grupos: "Almacén"),
                Globo(2, grupos: "Administración, Dirección"),
                Globo(3, grupos: "carlos; santi"),
                Globo(4, grupos: null),
                Globo(5, grupos: "  ")
            });

            CollectionAssert.AreEqual(new[] { 1, 4, 5 }, Pedir(Usuario("pedro", "NUEVAVISION\\Almacen")).Select(g => g.Id).ToList());
            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, Pedir(Usuario("NUEVAVISION\\Santi")).Select(g => g.Id).ToList());
            CollectionAssert.AreEqual(new[] { 2, 4, 5 }, Pedir(Usuario("laura", "Administración")).Select(g => g.Id).ToList());
        }

        [TestMethod]
        public void Get_VersionDesde_SoloAppsIgualesOPosteriores_YSinVersionDeLaAppSale()
        {
            _ = A.CallTo(() => repositorio.LeerActivos(A<string>._, A<string>._)).Returns(new List<GloboFila>
            {
                Globo(1, version: "2.27"),
                Globo(2, version: null),
                Globo(3, version: "2.30")
            });

            CollectionAssert.AreEqual(new[] { 2 }, Pedir(Usuario("pedro"), version: "2.26").Select(g => g.Id).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2 }, Pedir(Usuario("pedro"), version: "2.27.0").Select(g => g.Id).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, Pedir(Usuario("pedro"), version: null).Select(g => g.Id).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, Pedir(Usuario("pedro"), version: "rara").Select(g => g.Id).ToList());
        }

        [TestMethod]
        public void Get_InactivosDeOtraAppOtraPantallaOSinTexto_NoSalen_YVanPorOrdenEId()
        {
            GloboFila sinTexto = Globo(6);
            sinTexto.Texto = " ";
            _ = A.CallTo(() => repositorio.LeerActivos(A<string>._, A<string>._)).Returns(new List<GloboFila>
            {
                Globo(1, orden: 20),
                Globo(2, activo: false),
                Globo(3, app: "Nesto"),
                Globo(4, pantalla: "Packing"),
                Globo(5, orden: 10),
                sinTexto,
                Globo(7, orden: 10)
            });

            CollectionAssert.AreEqual(new[] { 5, 7, 1 }, Pedir(Usuario("pedro")).Select(g => g.Id).ToList());
        }

        [TestMethod]
        public void Get_SinPantalla_TodosLosDeLaAplicacion()
        {
            _ = A.CallTo(() => repositorio.LeerActivos("Ariadna", null)).Returns(new List<GloboFila> { Globo(1), Globo(2, pantalla: "Packing") });

            CollectionAssert.AreEqual(new[] { 1, 2 }, Pedir(Usuario("pedro"), pantalla: null).Select(g => g.Id).ToList());
        }

        [TestMethod]
        public void Get_LaAplicacionSinDistinguirMayusculas_YLaDevuelveRecortada()
        {
            GloboFila fila = Globo(1);
            fila.Ancla = "  Reparto.EnLaFurgoneta ";
            fila.Titulo = " ";
            _ = A.CallTo(() => repositorio.LeerActivos("Ariadna", "Reparto")).Returns(new List<GloboFila> { fila });

            GloboDTO globo = Pedir(Usuario("pedro"), app: "ariadna").Single();

            Assert.AreEqual("Reparto.EnLaFurgoneta", globo.Ancla);
            Assert.IsNull(globo.Titulo);
        }

        [TestMethod]
        public void Get_SinGlobos_NoPreguntaLosVistos()
        {
            _ = A.CallTo(() => repositorio.LeerActivos(A<string>._, A<string>._)).Returns(new List<GloboFila>());

            Assert.AreEqual(0, Pedir(Usuario("pedro")).Count);
            A.CallTo(() => repositorio.LeerVistos(A<string>._, A<IReadOnlyCollection<int>>._)).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("Odoo")]
        public void Get_AplicacionNoValida_400(string app)
        {
            Assert.IsInstanceOfType(Controlador(Usuario("pedro")).GetGlobos(app, "Reparto"), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Get_SinUsuario_401()
        {
            Assert.IsInstanceOfType(Controlador(new ClaimsPrincipal(new ClaimsIdentity())).GetGlobos("Ariadna", "Reparto"), typeof(UnauthorizedResult));
        }

        // ---- Visto ----

        [TestMethod]
        public void Visto_ConElUsuarioSinDominio_YIdempotente()
        {
            A.CallTo(() => repositorio.MarcarVisto("Carlos", 5)).ReturnsNextFromSequence(ResultadoVistoGlobo.Marcado, ResultadoVistoGlobo.YaEstaba);
            GlobosController controlador = Controlador(Usuario("NUEVAVISION\\Carlos"));

            var primera = (OkNegotiatedContentResult<GloboVistoDTO>)controlador.PostVisto(5);
            var segunda = (OkNegotiatedContentResult<GloboVistoDTO>)controlador.PostVisto(5);

            Assert.IsFalse(primera.Content.YaEstaba);
            Assert.IsTrue(segunda.Content.YaEstaba);
            Assert.AreEqual(5, segunda.Content.Id);
        }

        [TestMethod]
        public void Visto_NoExiste_404()
        {
            _ = A.CallTo(() => repositorio.MarcarVisto(A<string>._, 9)).Returns(ResultadoVistoGlobo.NoExiste);

            var resultado = Controlador(Usuario("pedro")).PostVisto(9) as NegotiatedContentResult<HttpError>;

            Assert.AreEqual(HttpStatusCode.NotFound, resultado?.StatusCode);
        }

        [TestMethod]
        public void Visto_SinScript_400()
        {
            _ = A.CallTo(() => repositorio.MarcarVisto(A<string>._, A<int>._)).Returns(ResultadoVistoGlobo.FaltaScript);

            Assert.IsInstanceOfType(Controlador(Usuario("pedro")).PostVisto(1), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Visto_SinUsuario_401YNoMarca()
        {
            Assert.IsInstanceOfType(Controlador(new ClaimsPrincipal(new ClaimsIdentity())).PostVisto(1), typeof(UnauthorizedResult));
            A.CallTo(() => repositorio.MarcarVisto(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void SqlMarcar_EsIdempotenteEnLaBd()
        {
            // La segunda llamada (o dos a la vez) no inserta otra fila: comprueba antes y trata la violación de la PK como «ya estaba»
            StringAssert.Contains(RepositorioGlobosSql.SQL_MARCAR, "IF EXISTS (SELECT 1 FROM dbo.GlobosVistos WHERE Usuario = @usuario AND GloboId = @id)");
            StringAssert.Contains(RepositorioGlobosSql.SQL_MARCAR, "ERROR_NUMBER() IN (2627, 2601) SELECT 1");
        }
    }
}
