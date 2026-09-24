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
    /// NestoAPI#526: los usuarios sugieren características desde Novedades (novedades sin versión).
    /// NestoAPI#527: buscador de novedades.
    /// </summary>
    [TestClass]
    public class NovedadesSugerenciasTests
    {
        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private NovedadesController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new System.Net.Http.HttpRequestMessage()
            };
        }

        private void ComoUsuarioDeNesto(string usuario = "NUEVAVISION\\Alfredo", params string[] roles)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario),
                new Claim(ClaimTypes.Name, usuario),
                new Claim(ClaimTypes.AuthenticationMethod, "Windows")
            };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        }

        private void ComoVendedorDeLaApp()
        {
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "5f1c-guid"),
                new Claim(ClaimTypes.Name, "manuel@nuevavision.es")
            }, "Bearer"));
        }

        private void ComoClienteDeLaTienda()
        {
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "cliente-guid"),
                new Claim(ClaimTypes.Name, "info@cliente.es"),
                new Claim("cliente", "15191")
            }, "Bearer"));
        }

        private static NovedadConSugerenciaFila Sugerencia(int id, string ambito, string titulo = "Botón para duplicar", DateTime? fecha = null) =>
            new NovedadConSugerenciaFila
            {
                Id = id,
                Version = null,
                Fecha = DateTime.Today,
                Categoria = "Nuevo",
                Titulo = titulo,
                Ambito = ambito,
                TextoOriginal = "Aquí iría bien un botón",
                Estado = ReglasSugerenciasNovedades.ESTADO_PENDIENTE,
                SugeridaFecha = fecha ?? DateTime.Today
            };

        // ---- Crear ----

        [TestMethod]
        public void PostSugerencia_DesdeNesto_NaceSinVersionPendienteYConElTextoTalCual()
        {
            ComoUsuarioDeNesto();
            SugerenciaNovedadAGrabar grabada = null;
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._))
                .Invokes((SugerenciaNovedadAGrabar s) => grabada = s).Returns(360);

            var resultado = controller.PostSugerencia(new NuevoComentarioNovedadDTO
            {
                Texto = "  Aquí iría bien un botón para duplicar el pedido\nsin tener que meterlo otra vez  "
            }) as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(360, resultado.Content.Id);
            Assert.IsNull(resultado.Content.Version, "sin versión: sale por delante de la actual");
            Assert.AreEqual(ReglasSugerenciasNovedades.ESTADO_PENDIENTE, resultado.Content.Estado);
            Assert.AreEqual("Nesto", grabada.Ambito);
            Assert.AreEqual("NUEVAVISION\\Alfredo", grabada.SugeridaPor);
            Assert.AreEqual("Alfredo", grabada.SugeridaNombre);
            Assert.AreEqual("Aquí iría bien un botón para duplicar el pedido", grabada.Titulo, "la primera línea");
            StringAssert.Contains(grabada.TextoOriginal, "sin tener que meterlo otra vez");
        }

        [TestMethod]
        public void PostSugerencia_DesdeNestoApp_EsDeLaApp()
        {
            ComoVendedorDeLaApp();
            SugerenciaNovedadAGrabar grabada = null;
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).Invokes((SugerenciaNovedadAGrabar s) => grabada = s);

            _ = controller.PostSugerencia(new NuevoComentarioNovedadDTO { Texto = "Filtro por ruta" });

            Assert.AreEqual("NestoApp", grabada.Ambito);
        }

        [TestMethod]
        public void PostSugerencia_DesdeLaTienda_Forbidden()
        {
            ComoClienteDeLaTienda();

            var resultado = controller.PostSugerencia(new NuevoComentarioNovedadDTO { Texto = "Quiero más colores" });

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PostSugerencia_SinTexto_BadRequest()
        {
            ComoUsuarioDeNesto();

            Assert.IsInstanceOfType(controller.PostSugerencia(new NuevoComentarioNovedadDTO { Texto = "  " }), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).MustNotHaveHappened();
        }

        // ---- Listar ----

        [TestMethod]
        public void GetSugerencias_SinAmbito_LasDeEscritorioYLasMasVotadasArriba()
        {
            A.CallTo(() => servicio.LeerSugerencias(false)).Returns(new List<NovedadConSugerenciaFila>
            {
                Sugerencia(1, "Nesto", "Poco votada"),
                Sugerencia(2, "NestoApp", "De la app"),
                Sugerencia(3, "Nesto", "Muy votada")
            });
            A.CallTo(() => feedback.LeerResumen(A<IEnumerable<int>>._, A<string>._)).Returns(new List<ResumenFeedbackNovedad>
            {
                new ResumenFeedbackNovedad { NovedadId = 1, Positivos = 1 },
                new ResumenFeedbackNovedad { NovedadId = 3, Positivos = 5, Comentarios = 2 }
            });

            var resultado = controller.GetSugerencias() as OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>;

            CollectionAssert.AreEqual(new[] { 3, 1 }, resultado.Content.Select(s => s.Id).ToArray());
            Assert.AreEqual(2, resultado.Content[0].NumeroComentarios);
            Assert.AreEqual("Aquí iría bien un botón", resultado.Content[0].TextoOriginal);
        }

        [TestMethod]
        public void GetNovedades_NoPideNuncaLasSugerencias()
        {
            // El changelog y el popup salen de LeerNovedadesPublicadas, que filtra Version IS NOT NULL.
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>());

            _ = controller.GetNovedades("1.10.30.0");

            A.CallTo(() => servicio.LeerSugerencias(A<bool>._)).MustNotHaveHappened();
        }

        // ---- Editar (nosotros) ----

        [TestMethod]
        public void PutSugerencia_SinSerDireccionNiInformatica_Forbidden()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo", "NUEVAVISION\\Almacén");

            var resultado = controller.PutSugerencia(7, new ActualizarSugerenciaNovedadDTO { Estado = "Aceptada" });

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
        }

        [TestMethod]
        public void PutSugerencia_ConVersion_PasaAImplementadaYASuSitio()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            ActualizarSugerenciaNovedadDTO grabado = null;
            A.CallTo(() => servicio.ActualizarSugerencia(7, A<ActualizarSugerenciaNovedadDTO>._, A<string>._))
                .Invokes((int id, ActualizarSugerenciaNovedadDTO c, string u) => grabado = c).Returns(true);

            var resultado = controller.PutSugerencia(7, new ActualizarSugerenciaNovedadDTO
            {
                Descripcion = "Nuevo botón «Duplicar» en el detalle del pedido.",
                Version = " 1.10.31.0 "
            });

            Assert.AreEqual(HttpStatusCode.NoContent, ((StatusCodeResult)resultado).StatusCode);
            Assert.AreEqual("1.10.31.0", grabado.Version);
            Assert.AreEqual(ReglasSugerenciasNovedades.ESTADO_IMPLEMENTADA, grabado.Estado);
        }

        [TestMethod]
        public void PutSugerencia_QueNoExiste_NotFound()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            A.CallTo(() => servicio.ActualizarSugerencia(99, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).Returns(false);

            Assert.IsInstanceOfType(controller.PutSugerencia(99, new ActualizarSugerenciaNovedadDTO { Estado = "Aceptada" }), typeof(NotFoundResult));
        }

        // ---- Buscar ----

        [TestMethod]
        public void GetBuscar_SinPalabrasValidas_BadRequest()
        {
            Assert.IsInstanceOfType(controller.GetBuscar(" a "), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.Buscar(A<IReadOnlyList<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetBuscar_DevuelveNovedadesYSugerenciasDelAmbito()
        {
            IReadOnlyList<string> buscadas = null;
            A.CallTo(() => servicio.Buscar(A<IReadOnlyList<string>>._))
                .Invokes((IReadOnlyList<string> p) => buscadas = p)
                .Returns(new List<NovedadConSugerenciaFila>
                {
                    new NovedadConSugerenciaFila { Id = 349, Version = "1.10.30.0", Titulo = "Modificar el reembolso", Ambito = "NestoAPI" },
                    Sugerencia(360, "Nesto", "Reembolso en el listado"),
                    new NovedadConSugerenciaFila { Id = 200, Version = "2.20.7", Titulo = "Reembolso en la app", Ambito = "NestoApp" }
                });

            var resultado = controller.GetBuscar("reembolso envío") as OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>;

            CollectionAssert.AreEqual(new[] { "reembolso", "envío" }, buscadas.ToArray());
            CollectionAssert.AreEqual(new[] { 349, 360 }, resultado.Content.Select(n => n.Id).ToArray(), "sin ámbito: las de escritorio");
            Assert.AreEqual("1.10.30.0", resultado.Content[0].Version);
            Assert.IsNull(resultado.Content[1].Version, "la sugerencia no tiene versión");
        }

        // ---- NestoAPI#535: lo que más gusta, arriba ----

        private static NovedadConFeedbackDTO ConVotos(int id, string version, int positivos, int negativos = 0) =>
            new NovedadConFeedbackDTO { Id = id, Version = version, VotosPositivos = positivos, VotosNegativos = negativos };

        [TestMethod]
        public void OrdenarPorReacciones_DentroDeCadaVersionLasMasVotadasArriba_YLasVersionesNoSeMueven()
        {
            var novedades = new List<NovedadDTO>
            {
                ConVotos(10, "1.10.31.0", 0),
                ConVotos(11, "1.10.31.0", 3),
                ConVotos(12, "1.10.31.0", 3, 1),
                ConVotos(13, "1.10.31.0", 0),
                ConVotos(5, "1.10.30.0", 9)
            };

            List<NovedadDTO> ordenadas = NovedadesController.OrdenarPorReacciones(novedades);

            CollectionAssert.AreEqual(new[] { 11, 12, 10, 13, 5 }, ordenadas.Select(n => n.Id).ToArray(),
                "11 (+3), 12 (+2), y sin votos en su orden (10, 13); la 1.10.30.0 sigue detrás aunque tenga más");
        }

        [TestMethod]
        public void OrdenarPorReacciones_SinFeedback_ElOrdenDeSiempre()
        {
            var novedades = new List<NovedadDTO>
            {
                new NovedadDTO { Id = 2, Version = "1.10.31.0" },
                new NovedadDTO { Id = 1, Version = "1.10.31.0" }
            };

            CollectionAssert.AreEqual(new[] { 2, 1 }, NovedadesController.OrdenarPorReacciones(novedades).Select(n => n.Id).ToArray());
        }

        [TestMethod]
        public void OrdenarPorReacciones_LasNegativasVanAlFinalDeSuVersion()
        {
            var novedades = new List<NovedadDTO> { ConVotos(1, "1.10.31.0", 0, 2), ConVotos(2, "1.10.31.0", 0) };

            CollectionAssert.AreEqual(new[] { 2, 1 }, NovedadesController.OrdenarPorReacciones(novedades).Select(n => n.Id).ToArray());
        }

        // ---- Reglas puras ----

        [TestMethod]
        public void TituloDesde_TextoLargo_SeRecortaConPuntosSuspensivos()
        {
            string titulo = ReglasSugerenciasNovedades.TituloDesde(new string('x', 300));

            Assert.AreEqual(ReglasSugerenciasNovedades.LONGITUD_TITULO, titulo.Length);
            Assert.IsTrue(titulo.EndsWith("…"));
        }

        [TestMethod]
        public void Normalizar_ImplementadaSinVersion_NoVale()
        {
            StringAssert.Contains(ReglasSugerenciasNovedades.Normalizar(new ActualizarSugerenciaNovedadDTO { Estado = "Implementada" }), "versión");
        }

        [TestMethod]
        public void Normalizar_EstadoOVersionRaros_NoValen()
        {
            Assert.IsNotNull(ReglasSugerenciasNovedades.Normalizar(new ActualizarSugerenciaNovedadDTO { Estado = "Hecha" }));
            Assert.IsNotNull(ReglasSugerenciasNovedades.Normalizar(new ActualizarSugerenciaNovedadDTO { Version = "la próxima" }));
            Assert.IsNotNull(ReglasSugerenciasNovedades.Normalizar(new ActualizarSugerenciaNovedadDTO { Categoria = "Otra" }));
        }

        [TestMethod]
        public void Normalizar_EstadoEnMinusculas_SeNormaliza()
        {
            var cambios = new ActualizarSugerenciaNovedadDTO { Estado = "descartada" };

            Assert.IsNull(ReglasSugerenciasNovedades.Normalizar(cambios));
            Assert.AreEqual(ReglasSugerenciasNovedades.ESTADO_DESCARTADA, cambios.Estado);
        }

        [TestMethod]
        public void Palabras_QuitaLasCortasYLasRepetidas()
        {
            CollectionAssert.AreEqual(new[] { "pedido", "de", "regalo" },
                ReglasSugerenciasNovedades.Palabras("pedido de  regalo, a Pedido").ToArray());
        }

        [TestMethod]
        public void PatronLike_EscapaLosComodinesDeSql()
        {
            Assert.AreEqual("%100[%] [_]x[[]%", ReglasSugerenciasNovedades.PatronLike("100% _x["));
        }
    }
}
