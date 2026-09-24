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
    /// NestoAPI#520: votos y comentarios de los usuarios en las Novedades. El usuario sale SIEMPRE del
    /// token (nunca del cuerpo), un voto por persona, y el feedback nunca rompe las Novedades.
    /// </summary>
    [TestClass]
    public class NovedadesFeedbackTests
    {
        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private NovedadesController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            A.CallTo(() => feedback.ExisteNovedad(A<int>._)).Returns(true);
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new System.Net.Http.HttpRequestMessage()
            };
        }

        private void ComoUsuarioDeNesto(string usuario = "NUEVAVISION\\Paloma", params string[] roles)
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

        private void ComoVendedorDeLaApp(string idIdentity = "5f1c-guid", string nombre = "manuel@nuevavision.es")
        {
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, idIdentity),
                new Claim(ClaimTypes.Name, nombre)
            }, "Bearer"));
        }

        private static string Png(int bytesExtra = 10)
        {
            var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[bytesExtra]).ToArray();
            return Convert.ToBase64String(bytes);
        }

        // ---- Votos ----

        [TestMethod]
        public void PutVoto_UsaElUsuarioDelToken_YSabeQueEsDeNesto()
        {
            ComoUsuarioDeNesto();

            var resultado = controller.PutVoto(7, new VotoNovedadDTO { Voto = 1 });

            Assert.AreEqual(HttpStatusCode.NoContent, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => feedback.Votar(7, "NUEVAVISION\\Paloma", ReglasFeedbackNovedades.CLIENTE_NESTO, (short)1)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PutVoto_DesdeLaApp_LaClaveEsElIdDeIdentity_NoElNombre()
        {
            // Pregunta de Carlos (23/09): al renovar el token el usuario es el mismo; la clave estable
            // en NestoApp es el Id de Identity, porque el UserName en teoría se puede cambiar.
            ComoVendedorDeLaApp();

            _ = controller.PutVoto(7, new VotoNovedadDTO { Voto = -1 });

            A.CallTo(() => feedback.Votar(7, "5f1c-guid", ReglasFeedbackNovedades.CLIENTE_NESTOAPP, (short)-1)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PutVoto_Cero_QuitaElVoto()
        {
            ComoUsuarioDeNesto();

            _ = controller.PutVoto(7, new VotoNovedadDTO { Voto = 0 });

            A.CallTo(() => feedback.Votar(7, "NUEVAVISION\\Paloma", A<string>._, (short)0)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PutVoto_ValorFueraDeRango_BadRequestYNoVota()
        {
            ComoUsuarioDeNesto();

            var resultado = controller.PutVoto(7, new VotoNovedadDTO { Voto = 5 });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => feedback.Votar(A<int>._, A<string>._, A<string>._, A<short>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PutVoto_SinUsuarioAutenticado_Unauthorized()
        {
            controller.User = new ClaimsPrincipal(new ClaimsIdentity());

            Assert.IsInstanceOfType(controller.PutVoto(7, new VotoNovedadDTO { Voto = 1 }), typeof(UnauthorizedResult));
        }

        [TestMethod]
        public void PutVoto_NovedadQueNoExiste_NotFound()
        {
            ComoUsuarioDeNesto();
            A.CallTo(() => feedback.ExisteNovedad(99)).Returns(false);

            Assert.IsInstanceOfType(controller.PutVoto(99, new VotoNovedadDTO { Voto = 1 }), typeof(NotFoundResult));
        }

        // ---- Comentarios ----

        [TestMethod]
        public void PostComentario_ConCapturaPegada_GrabaElUsuarioDelTokenYElTipoDeLosBytes()
        {
            ComoUsuarioDeNesto();
            ComentarioNovedadAGrabar grabado = null;
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._))
                .Invokes((ComentarioNovedadAGrabar c) => grabado = c).Returns(42);

            var resultado = controller.PostComentario(7, new NuevoComentarioNovedadDTO
            {
                Texto = "  El aviso de ofertas me sale dos veces  ",
                ImagenBase64 = "data:image/png;base64," + Png(),
                ImagenTipo = "image/jpeg", // lo que diga el cliente no manda: manda la firma de los bytes
                VersionCliente = "1.10.29.1"
            }) as OkNegotiatedContentResult<ComentarioNovedadDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(42, resultado.Content.Id);
            Assert.IsTrue(resultado.Content.EsMio);
            Assert.AreEqual("NUEVAVISION\\Paloma", grabado.Usuario);
            Assert.AreEqual("Paloma", grabado.NombreVisible, "Los demás ven el nombre sin el dominio");
            Assert.AreEqual("El aviso de ofertas me sale dos veces", grabado.Texto);
            Assert.AreEqual(ReglasFeedbackNovedades.TIPO_PNG, grabado.ImagenTipo);
            Assert.AreEqual(18, grabado.Imagen.Length);
        }

        [TestMethod]
        public void PostComentario_TextoVacio_BadRequest()
        {
            ComoUsuarioDeNesto();

            Assert.IsInstanceOfType(controller.PostComentario(7, new NuevoComentarioNovedadDTO { Texto = "   " }), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PostComentario_TextoDemasiadoLargo_BadRequest()
        {
            ComoUsuarioDeNesto();

            var resultado = controller.PostComentario(7, new NuevoComentarioNovedadDTO { Texto = new string('x', 2001) });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void PostComentario_ImagenQueNoEsPngNiJpeg_BadRequest()
        {
            ComoUsuarioDeNesto();
            string gif = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("GIF89a-contenido"));

            var resultado = controller.PostComentario(7, new NuevoComentarioNovedadDTO { Texto = "mira", ImagenBase64 = gif });

            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "PNG o JPEG");
        }

        [TestMethod]
        public void PostComentario_ImagenDeMasDe2MB_BadRequest()
        {
            ComoUsuarioDeNesto();

            var resultado = controller.PostComentario(7, new NuevoComentarioNovedadDTO
            {
                Texto = "mira",
                ImagenBase64 = Png(ReglasFeedbackNovedades.TAMANO_MAXIMO_IMAGEN)
            });

            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "2 MB");
        }

        [TestMethod]
        public void DeleteComentario_DeOtraPersona_ForbiddenYNoSeBorra()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Paloma");
            A.CallTo(() => feedback.LeerAutorComentario(5)).Returns("NUEVAVISION\\Manuel");

            var resultado = controller.DeleteComentario(5);

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => feedback.BorrarComentario(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void DeleteComentario_Propio_SeBorra()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Paloma");
            A.CallTo(() => feedback.LeerAutorComentario(5)).Returns("NUEVAVISION\\Paloma");

            var resultado = controller.DeleteComentario(5);

            Assert.AreEqual(HttpStatusCode.NoContent, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => feedback.BorrarComentario(5)).MustHaveHappenedOnceExactly();
        }

        // ---- GET api/Novedades con el feedback ----

        private static NovedadDTO Novedad(int id, string version) =>
            new NovedadDTO { Id = id, Version = version, Fecha = new DateTime(2026, 9, 23), Categoria = "Nuevo", Titulo = "T" + id, Ambito = "Nesto" };

        [TestMethod]
        public void GetNovedades_ElFeedbackDeTodasSaleDeUnaSolaConsulta()
        {
            // Lección del 23/09 (#517): nada de una consulta por elemento.
            ComoUsuarioDeNesto();
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO> { Novedad(1, "1.10.29.0"), Novedad(2, "1.10.29.1") });
            A.CallTo(() => feedback.LeerResumen(A<IEnumerable<int>>._, A<string>._)).Returns(new List<ResumenFeedbackNovedad>
            {
                new ResumenFeedbackNovedad { NovedadId = 2, Positivos = 3, Negativos = 1, Comentarios = 2, MiVoto = 1 }
            });

            var resultado = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            A.CallTo(() => feedback.LeerResumen(A<IEnumerable<int>>._, "NUEVAVISION\\Paloma")).MustHaveHappenedOnceExactly();
            var dos = (NovedadConFeedbackDTO)resultado.Content.Single(n => n.Id == 2);
            var uno = (NovedadConFeedbackDTO)resultado.Content.Single(n => n.Id == 1);
            Assert.AreEqual(3, dos.VotosPositivos);
            Assert.AreEqual(1, dos.VotosNegativos);
            Assert.AreEqual((short)1, dos.MiVoto);
            Assert.AreEqual(2, dos.NumeroComentarios);
            Assert.AreEqual(0, uno.VotosPositivos, "Sin votos = 0, no null");
            Assert.IsNull(uno.MiVoto);
        }

        [TestMethod]
        public void GetNovedades_SiElFeedbackFalla_LasNovedadesSalenIgual()
        {
            // Tablas aún no creadas (API publicada antes del script): las Novedades no se pueden romper.
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO> { Novedad(1, "1.10.29.0") });
            A.CallTo(() => feedback.LeerResumen(A<IEnumerable<int>>._, A<string>._)).Throws(new InvalidOperationException("El nombre de objeto 'NovedadesVotos' no es válido."));

            var resultado = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
        }

        [TestMethod]
        public void GetNovedades_SinServicioDeFeedback_ComoSiempre()
        {
            var sinFeedback = new NovedadesController(servicio) { Request = new System.Net.Http.HttpRequestMessage() };
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO> { Novedad(1, "1.10.29.0") });

            var resultado = sinFeedback.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotInstanceOfType(resultado.Content.Single(), typeof(NovedadConFeedbackDTO));
        }

        // ---- Revisión para el desarrollo ----

        [TestMethod]
        public void GetFeedback_SinSerDireccionNiInformatica_Forbidden()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Paloma", "NUEVAVISION\\Almacén");

            var resultado = controller.GetFeedback();

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => feedback.LeerFeedback(A<DateTime>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetFeedback_Informatica_LeeLoNuevo()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            var desde = new DateTime(2026, 9, 22);
            A.CallTo(() => feedback.LeerFeedback(desde, true)).Returns(new FeedbackNovedadesDTO { Desde = desde });

            var resultado = controller.GetFeedback(desde) as OkNegotiatedContentResult<FeedbackNovedadesDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(desde, resultado.Content.Desde);
        }

        // ---- NestoAPI#531: el asistente IA contesta como él mismo ----

        [TestMethod]
        public void PostComentarioAsistente_GrabaComoElAsistenteYDejaRevisadoLoContestado()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            A.CallTo(() => feedback.ExisteNovedad(7)).Returns(true);
            ComentarioNovedadAGrabar grabado = null;
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._))
                .Invokes((ComentarioNovedadAGrabar c) => grabado = c).Returns(43);

            var resultado = controller.PostComentarioAsistente(7, new NuevoComentarioAsistenteDTO
            {
                Texto = "Hola, Alfredo. Tienes razón...",
                ComentariosContestados = new List<int> { 1, 1 }
            }) as OkNegotiatedContentResult<ComentarioNovedadDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(43, resultado.Content.Id);
            Assert.AreEqual("Claude", grabado.Usuario, "no como quien lanza la petición");
            Assert.AreEqual("Claude (asistente IA)", grabado.NombreVisible);
            Assert.AreEqual(ReglasFeedbackNovedades.CLIENTE_ASISTENTE, grabado.Cliente);
            A.CallTo(() => feedback.MarcarRevisado(1)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PostComentarioAsistente_SinSerDireccionNiInformatica_Forbidden()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Paloma", "NUEVAVISION\\Almacén");

            var resultado = controller.PostComentarioAsistente(7, new NuevoComentarioAsistenteDTO { Texto = "hola" });

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PostComentarioAsistente_NovedadQueNoExiste_NotFound()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            A.CallTo(() => feedback.ExisteNovedad(99)).Returns(false);

            var resultado = controller.PostComentarioAsistente(99, new NuevoComentarioAsistenteDTO { Texto = "hola" });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        // ---- Reglas puras ----

        [TestMethod]
        public void TipoPorContenido_ReconocePngYJpeg()
        {
            Assert.AreEqual(ReglasFeedbackNovedades.TIPO_PNG, ReglasFeedbackNovedades.TipoPorContenido(Convert.FromBase64String(Png())));
            Assert.AreEqual(ReglasFeedbackNovedades.TIPO_JPEG, ReglasFeedbackNovedades.TipoPorContenido(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }));
            Assert.IsNull(ReglasFeedbackNovedades.TipoPorContenido(new byte[] { 1, 2 }));
        }

        [TestMethod]
        public void Cliente_TokenDeLaTienda_SeIdentificaComoTienda()
        {
            var tienda = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "cliente@correo.es"),
                new Claim("cliente", "15191")
            }, "Bearer"));

            Assert.AreEqual(ReglasFeedbackNovedades.CLIENTE_TIENDA, ReglasFeedbackNovedades.Cliente(tienda));
        }
    }
}
