using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Sugerencia 551 (Alberto Sancho): cada uno ve por defecto las novedades de su perfil, con «ver todas» para
    /// el resto. Las novedades sin perfiles son para todos, Dirección e Informática ven todas y las sugerencias
    /// no se filtran.
    /// </summary>
    [TestClass]
    public class NovedadesPerfilesTests
    {
        private IServicioNovedades servicio;
        private NovedadesController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            controller = new NovedadesController(servicio)
            {
                Request = new System.Net.Http.HttpRequestMessage()
            };
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "Para todos"),
                Novedad(2, "Del almacén"),
                Novedad(3, "De vendedores y tiendas"),
                Novedad(4, "De administración")
            });
            A.CallTo(() => servicio.LeerPerfiles()).Returns(new Dictionary<int, string>
            {
                [2] = "Almacén",
                [3] = "Vendedores, tiendas",
                [4] = "Administracion"
            });
        }

        private static NovedadDTO Novedad(int id, string titulo) => new NovedadDTO
        {
            Id = id,
            Version = "1.10.40.0",
            Fecha = new DateTime(2026, 10, 9),
            Categoria = "Nuevo",
            Titulo = titulo,
            Ambito = "Nesto"
        };

        private void ComoUsuario(params string[] roles) => ComoUsuarioConClaims(roles, new Claim[0]);

        private void ComoUsuarioConClaims(string[] roles, Claim[] otros)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, @"NUEVAVISION\Prueba"),
                new Claim(ClaimTypes.Name, @"NUEVAVISION\Prueba")
            };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, @"NUEVAVISION\" + r)));
            claims.AddRange(otros);
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        }

        private List<string> Titulos(bool todas = false)
        {
            var resultado = controller.GetNovedades(todas: todas) as OkNegotiatedContentResult<List<NovedadDTO>>;
            Assert.IsNotNull(resultado);
            return resultado.Content.Select(n => n.Titulo).OrderBy(t => t).ToList();
        }

        [TestMethod]
        public void GetNovedades_Almacen_SoloLasSuyasYLasDeTodos()
        {
            ComoUsuario("Almacén", "Usuarios del dominio");

            CollectionAssert.AreEqual(new[] { "Del almacén", "Para todos" }, Titulos());
        }

        [TestMethod]
        public void GetNovedades_ConTodas_LasVeTodas()
        {
            ComoUsuario("Almacén");

            Assert.AreEqual(4, Titulos(todas: true).Count);
        }

        [TestMethod]
        public void GetNovedades_SinPerfilesEnLaNovedad_EsParaTodos()
        {
            ComoUsuario("Tiendas");

            CollectionAssert.AreEqual(new[] { "De vendedores y tiendas", "Para todos" }, Titulos());
        }

        [TestMethod]
        public void GetNovedades_Direccion_VeTodas()
        {
            ComoUsuario("Dirección");

            Assert.AreEqual(4, Titulos().Count);
        }

        [TestMethod]
        public void GetNovedades_InformaticaSinTilde_VeTodas()
        {
            ComoUsuario("Informatica", "Administración");

            Assert.AreEqual(4, Titulos().Count);
        }

        [TestMethod]
        public void GetNovedades_SinToken_VeTodas()
        {
            Assert.AreEqual(4, Titulos().Count, "Sin usuario no se sabe a quién afecta: todas");
        }

        [TestMethod]
        public void GetNovedades_SinGruposConocidos_VeTodas()
        {
            ComoUsuario("Usuarios del dominio");

            Assert.AreEqual(4, Titulos().Count);
        }

        [TestMethod]
        public void GetNovedades_Comercial_VeLasDeVendedores()
        {
            ComoUsuario("Comerciales");

            CollectionAssert.AreEqual(new[] { "De vendedores y tiendas", "Para todos" }, Titulos());
        }

        [TestMethod]
        public void GetNovedades_ComercialQueEsDelAlmacen_NoEsVendedor()
        {
            // Alfredo, Santiago...: están en Comerciales para ver clientes, pero su puesto es el almacén
            ComoUsuario("Comerciales", "Almacén");

            CollectionAssert.AreEqual(new[] { "Del almacén", "Para todos" }, Titulos());
        }

        [TestMethod]
        public void GetNovedades_VendedorDeNestoApp_PorElClaimIsVendedor()
        {
            ComoUsuarioConClaims(new string[0], new[] { new Claim("IsVendedor", "true"), new Claim("Vendedor", "ASH") });

            CollectionAssert.AreEqual(new[] { "De vendedores y tiendas", "Para todos" }, Titulos());
        }

        [TestMethod]
        public void GetNovedades_ComprasYTiendaOnline_SonAdministracion()
        {
            ComoUsuario("Compras");
            CollectionAssert.AreEqual(new[] { "De administración", "Para todos" }, Titulos());

            ComoUsuario("TiendaOnline");
            CollectionAssert.AreEqual(new[] { "De administración", "Para todos" }, Titulos());
        }

        [TestMethod]
        public void GetNovedades_LlevaLosPerfilesCanonicos()
        {
            ComoUsuario("Dirección");

            var resultado = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNull(resultado.Content.Single(n => n.Id == 1).Perfiles, "Sin perfiles = null (para todos)");
            CollectionAssert.AreEqual(new[] { "Vendedores", "Tiendas" }, resultado.Content.Single(n => n.Id == 3).Perfiles);
            CollectionAssert.AreEqual(new[] { "Administración" }, resultado.Content.Single(n => n.Id == 4).Perfiles);
        }

        [TestMethod]
        public void GetNovedades_SiNoSePuedenLeerLosPerfiles_TodasParaTodos()
        {
            A.CallTo(() => servicio.LeerPerfiles()).Throws(new InvalidOperationException("El nombre de columna 'Perfiles' no es válido."));
            ComoUsuario("Almacén");

            Assert.AreEqual(4, Titulos().Count);
        }

        [TestMethod]
        public void GetNovedades_DesdeVersion_SoloCuentaLasDeSuPerfil()
        {
            // El popup de arranque: si en la versión nueva no hay nada suyo, no le sale
            ComoUsuario("Tiendas");
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(2, "Del almacén"),
                Novedad(4, "De administración")
            });

            var resultado = controller.GetNovedades(desdeVersion: "1.10.39.1") as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.AreEqual(0, resultado.Content.Count);
        }

        [TestMethod]
        public void GetSugerencias_NoSeFiltranPorPerfil()
        {
            ComoUsuario("Almacén");
            A.CallTo(() => servicio.LeerSugerencias(A<bool>._)).Returns(new List<NovedadConSugerenciaFila>
            {
                new NovedadConSugerenciaFila { Id = 10, Titulo = "Sugerencia de un vendedor", Ambito = "Nesto", Estado = "Pendiente", TextoOriginal = "x", SugeridaFecha = DateTime.Now },
                new NovedadConSugerenciaFila { Id = 11, Titulo = "Otra", Ambito = "Nesto", Estado = "Pendiente", TextoOriginal = "y", SugeridaFecha = DateTime.Now }
            });
            A.CallTo(() => servicio.LeerPerfiles()).Returns(new Dictionary<int, string> { [10] = "Vendedores", [11] = "Vendedores" });

            var resultado = controller.GetSugerencias() as OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>;

            Assert.AreEqual(2, resultado.Content.Count);
        }

        [TestMethod]
        public void GetMisPerfiles_DiceLosPerfilesYSiPuedeEditar()
        {
            ComoUsuario("Tiendas", "Almacén");
            var resultado = controller.GetMisPerfiles() as OkNegotiatedContentResult<PerfilesUsuarioNovedades>;
            CollectionAssert.AreEqual(new[] { "Almacén", "Tiendas" }, resultado.Content.Perfiles);
            Assert.IsFalse(resultado.Content.VeTodas);
            Assert.IsFalse(resultado.Content.PuedeEditar);
            CollectionAssert.AreEqual(new[] { "Vendedores", "Almacén", "Tiendas", "Administración" }, resultado.Content.Disponibles);

            ComoUsuario("Dirección");
            resultado = controller.GetMisPerfiles() as OkNegotiatedContentResult<PerfilesUsuarioNovedades>;
            Assert.IsTrue(resultado.Content.VeTodas);
            Assert.IsTrue(resultado.Content.PuedeEditar);
        }

        [TestMethod]
        public void PutPerfiles_Direccion_GuardaLosCanonicos()
        {
            ComoUsuario("Dirección");
            A.CallTo(() => servicio.GuardarPerfiles(A<int>._, A<string>._, A<string>._)).Returns(true);

            var resultado = controller.PutPerfiles(7, new PerfilesNovedadDTO { Perfiles = new List<string> { "tiendas", "almacen" } });

            Assert.AreEqual(HttpStatusCode.NoContent, (resultado as StatusCodeResult).StatusCode);
            A.CallTo(() => servicio.GuardarPerfiles(7, "Almacén,Tiendas", A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PutPerfiles_TodosOVacia_GuardaNull()
        {
            ComoUsuario("Informatica");
            A.CallTo(() => servicio.GuardarPerfiles(A<int>._, A<string>._, A<string>._)).Returns(true);

            _ = controller.PutPerfiles(7, new PerfilesNovedadDTO { Perfiles = new List<string> { "Todos" } });
            _ = controller.PutPerfiles(8, new PerfilesNovedadDTO { Perfiles = new List<string>() });
            _ = controller.PutPerfiles(9, new PerfilesNovedadDTO { Perfiles = new List<string> { "Vendedores", "Almacén", "Tiendas", "Administración" } });

            A.CallTo(() => servicio.GuardarPerfiles(A<int>._, null, A<string>._)).MustHaveHappened(3, Times.Exactly);
        }

        [TestMethod]
        public void PutPerfiles_PerfilDesconocido_BadRequest()
        {
            ComoUsuario("Dirección");

            var resultado = controller.PutPerfiles(7, new PerfilesNovedadDTO { Perfiles = new List<string> { "Repartidores" } });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.GuardarPerfiles(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PutPerfiles_SinSerDireccionNiInformatica_Prohibido()
        {
            ComoUsuario("Almacén");

            var resultado = controller.PutPerfiles(7, new PerfilesNovedadDTO { Perfiles = new List<string> { "Almacén" } });

            Assert.AreEqual(HttpStatusCode.Forbidden, (resultado as StatusCodeResult).StatusCode);
        }

        [TestMethod]
        public void PutPerfiles_NovedadQueNoExiste_NotFound()
        {
            ComoUsuario("Dirección");
            A.CallTo(() => servicio.GuardarPerfiles(A<int>._, A<string>._, A<string>._)).Returns(false);

            Assert.IsInstanceOfType(controller.PutPerfiles(99, new PerfilesNovedadDTO()), typeof(NotFoundResult));
        }

        [TestMethod]
        public void PutSugerencia_ConPerfiles_LosGuardaAlImplementarla()
        {
            ComoUsuario("Dirección");
            A.CallTo(() => servicio.ActualizarSugerencia(A<int>._, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).Returns(true);

            var resultado = controller.PutSugerencia(551, new ActualizarSugerenciaNovedadDTO { Version = "1.10.40.0", Perfiles = new List<string> { "Vendedores" } });

            Assert.AreEqual(HttpStatusCode.NoContent, (resultado as StatusCodeResult).StatusCode);
            A.CallTo(() => servicio.GuardarPerfiles(551, "Vendedores", A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PutSugerencia_SinPerfiles_NoLosToca()
        {
            ComoUsuario("Dirección");
            A.CallTo(() => servicio.ActualizarSugerencia(A<int>._, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).Returns(true);

            _ = controller.PutSugerencia(551, new ActualizarSugerenciaNovedadDTO { Estado = "Aceptada" });

            A.CallTo(() => servicio.GuardarPerfiles(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Parsear_DatoMalEscrito_NoEscondeLaNovedad()
        {
            Assert.IsNull(ReglasPerfilesNovedades.Parsear("Almacen-Tiendas"), "Ningún perfil conocido = para todos");
            Assert.IsNull(ReglasPerfilesNovedades.Parsear("Todos"));
            Assert.IsNull(ReglasPerfilesNovedades.Parsear("  "));
            CollectionAssert.AreEqual(new[] { "Almacén" }, ReglasPerfilesNovedades.Parsear("ALMACEN; Repartidores"));
        }
    }
}
