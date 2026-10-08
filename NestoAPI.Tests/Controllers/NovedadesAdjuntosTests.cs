using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#616: PDF e imágenes colgados en las novedades (p. ej. las normas de los cupones, #610).
    /// Suben y borran solo Dirección / Informática; los ven todos los que ven las novedades.
    /// </summary>
    [TestClass]
    public class NovedadesAdjuntosTests
    {
        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private IServicioAdjuntosNovedades adjuntos;
        private NovedadesController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            adjuntos = A.Fake<IServicioAdjuntosNovedades>();
            A.CallTo(() => adjuntos.ExisteNovedad(A<int>._)).Returns(true);
            A.CallTo(() => adjuntos.Crear(A<int>._, A<IReadOnlyList<AdjuntoNovedadAGrabar>>._, A<string>._))
                .ReturnsLazily((int novedad, IReadOnlyList<AdjuntoNovedadAGrabar> lista, string usuario) =>
                    lista.Select((a, i) => new AdjuntoNovedadDTO { Id = 100 + i, Nombre = a.Nombre, Tipo = a.Tipo, Tamano = a.Contenido.Length, Orden = i + 1 }).ToList());
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new HttpRequestMessage(),
                Adjuntos = adjuntos
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

        private void ComoInformatica() => ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");

        private static byte[] Bytes(int longitud) => Enumerable.Repeat((byte)0x25, longitud).ToArray();

        private void ConFicheros(params (string Nombre, string Tipo, byte[] Contenido)[] ficheros)
        {
            var multipart = new MultipartFormDataContent();
            foreach ((string nombre, string tipo, byte[] contenido) in ficheros)
            {
                var parte = new ByteArrayContent(contenido);
                if (tipo != null)
                {
                    parte.Headers.ContentType = new MediaTypeHeaderValue(tipo);
                }
                multipart.Add(parte, "fichero", nombre);
            }
            controller.Request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/Novedades/7/Adjuntos") { Content = multipart };
        }

        private static HttpStatusCode Estado(IHttpActionResult resultado)
        {
            switch (resultado)
            {
                case StatusCodeResult s: return s.StatusCode;
                case BadRequestErrorMessageResult _: return HttpStatusCode.BadRequest;
                case NotFoundResult _: return HttpStatusCode.NotFound;
                case NegotiatedContentResult<HttpError> e: return e.StatusCode;
                case NegotiatedContentResult<List<AdjuntoNovedadDTO>> c: return c.StatusCode;
                default: throw new AssertFailedException("Resultado inesperado: " + resultado?.GetType().Name);
            }
        }

        // ---- Subir ----

        [TestMethod]
        public async Task PostAdjuntos_SinSerDireccionNiInformatica_Prohibido()
        {
            ComoUsuarioDeNesto();
            ConFicheros(("Normas.pdf", "application/pdf", Bytes(10)));

            IHttpActionResult resultado = await controller.PostAdjuntos(7);

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(resultado));
            A.CallTo(() => adjuntos.Crear(A<int>._, A<IReadOnlyList<AdjuntoNovedadAGrabar>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void DeleteAdjunto_SinSerDireccionNiInformatica_Prohibido()
        {
            ComoUsuarioDeNesto();

            IHttpActionResult resultado = controller.DeleteAdjunto(5);

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(resultado));
            A.CallTo(() => adjuntos.Borrar(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostAdjuntos_Direccion_Puede_YDevuelve201ConLosCreados()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo", "NUEVAVISION\\Dirección");
            ConFicheros(("Normas cupón.pdf", "application/pdf", Bytes(1000)), ("Cartel.png", "image/png", Bytes(20)));

            var resultado = await controller.PostAdjuntos(7) as NegotiatedContentResult<List<AdjuntoNovedadDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Created, resultado.StatusCode);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.AreEqual("Normas cupón.pdf", resultado.Content[0].Nombre);
            A.CallTo(() => adjuntos.Crear(7, A<IReadOnlyList<AdjuntoNovedadAGrabar>>.That.Matches(l =>
                    l.Count == 2 && l[0].Tipo == "application/pdf" && l[0].Contenido.Length == 1000 && l[1].Tipo == "image/png"),
                "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostAdjuntos_JustoEnElLimite_SeAcepta()
        {
            ComoInformatica();
            ConFicheros(("Grande.pdf", "application/pdf", Bytes(ReglasAdjuntosNovedades.TAMANO_MAXIMO)));

            IHttpActionResult resultado = await controller.PostAdjuntos(7);

            Assert.AreEqual(HttpStatusCode.Created, Estado(resultado));
        }

        [TestMethod]
        public async Task PostAdjuntos_DeMasDe10MB_BadRequest_YNoSeGrabaNinguno()
        {
            ComoInformatica();
            ConFicheros(("Bien.pdf", "application/pdf", Bytes(10)),
                ("Enorme.pdf", "application/pdf", Bytes(ReglasAdjuntosNovedades.TAMANO_MAXIMO + 1)));

            var resultado = await controller.PostAdjuntos(7) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "Enorme.pdf");
            StringAssert.Contains(resultado.Message, "10 MB");
            A.CallTo(() => adjuntos.Crear(A<int>._, A<IReadOnlyList<AdjuntoNovedadAGrabar>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostAdjuntos_TipoNoPermitido_415ConMensaje()
        {
            ComoInformatica();
            ConFicheros(("Hoja.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Bytes(10)));

            var resultado = await controller.PostAdjuntos(7) as NegotiatedContentResult<HttpError>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, resultado.StatusCode);
            StringAssert.Contains(resultado.Content.Message, "Hoja.xlsx");
            StringAssert.Contains(resultado.Content.Message, "PDF");
        }

        [TestMethod]
        public async Task PostAdjuntos_ExtensionPermitidaPeroMimeNo_415()
        {
            ComoInformatica();
            ConFicheros(("Truco.pdf", "application/x-msdownload", Bytes(10)));

            IHttpActionResult resultado = await controller.PostAdjuntos(7);

            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, Estado(resultado));
        }

        [TestMethod]
        public async Task PostAdjuntos_MimeYExtensionNoCasan_BadRequest()
        {
            ComoInformatica();
            ConFicheros(("Foto.pdf", "image/png", Bytes(10)));

            IHttpActionResult resultado = await controller.PostAdjuntos(7);

            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(resultado));
        }

        [TestMethod]
        public async Task PostAdjuntos_SinTipoOOctetStream_MandaLaExtension()
        {
            ComoInformatica();
            ConFicheros(("Foto.JPG", null, Bytes(10)), ("Normas.pdf", "application/octet-stream", Bytes(10)));

            IHttpActionResult resultado = await controller.PostAdjuntos(7);

            Assert.AreEqual(HttpStatusCode.Created, Estado(resultado));
            A.CallTo(() => adjuntos.Crear(7, A<IReadOnlyList<AdjuntoNovedadAGrabar>>.That.Matches(l =>
                l[0].Tipo == "image/jpeg" && l[1].Tipo == "application/pdf"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostAdjuntos_SinMultipart_415()
        {
            ComoInformatica();
            controller.Request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/Novedades/7/Adjuntos")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };

            IHttpActionResult resultado = await controller.PostAdjuntos(7);

            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, Estado(resultado));
        }

        [TestMethod]
        public async Task PostAdjuntos_NovedadQueNoExiste_404()
        {
            ComoInformatica();
            A.CallTo(() => adjuntos.ExisteNovedad(99)).Returns(false);
            ConFicheros(("Normas.pdf", "application/pdf", Bytes(10)));

            IHttpActionResult resultado = await controller.PostAdjuntos(99);

            Assert.AreEqual(HttpStatusCode.NotFound, Estado(resultado));
        }

        [TestMethod]
        public void DeleteAdjunto_Informatica_204_YSiNoExiste404()
        {
            ComoInformatica();
            A.CallTo(() => adjuntos.Borrar(5)).Returns(true);
            A.CallTo(() => adjuntos.Borrar(6)).Returns(false);

            Assert.AreEqual(HttpStatusCode.NoContent, Estado(controller.DeleteAdjunto(5)));
            Assert.AreEqual(HttpStatusCode.NotFound, Estado(controller.DeleteAdjunto(6)));
        }

        // ---- Descargar ----

        [TestMethod]
        public async Task GetAdjunto_DevuelveElFicheroConSuTipoYContentDispositionConTildes()
        {
            byte[] pdf = Bytes(50);
            A.CallTo(() => adjuntos.Leer(5)).Returns(new AdjuntoNovedadContenido { Nombre = "Normas cupón.pdf", Tipo = "application/pdf", Contenido = pdf });

            HttpResponseMessage respuesta = controller.GetAdjunto(5);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("application/pdf", respuesta.Content.Headers.ContentType.MediaType);
            CollectionAssert.AreEqual(pdf, await respuesta.Content.ReadAsByteArrayAsync());
            ContentDispositionHeaderValue disposicion = respuesta.Content.Headers.ContentDisposition;
            Assert.AreEqual("attachment", disposicion.DispositionType);
            Assert.AreEqual("\"Normas cupon.pdf\"", disposicion.FileName, "filename en ASCII para los clientes viejos");
            Assert.AreEqual("Normas cupón.pdf", disposicion.FileNameStar, "filename* con la tilde");
            string cabecera = disposicion.ToString();
            StringAssert.Contains(cabecera, "filename=\"Normas cupon.pdf\"");
            StringAssert.Contains(cabecera, "filename*=utf-8''Normas%20cup%C3%B3n.pdf");
        }

        [TestMethod]
        public void GetAdjunto_QueNoExiste_404()
        {
            A.CallTo(() => adjuntos.Leer(5)).Returns(null);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.GetAdjunto(5).StatusCode);
        }

        [TestMethod]
        public void GetAdjuntos_ListaSinContenidoPorOrden()
        {
            A.CallTo(() => adjuntos.LeerResumen(A<IEnumerable<int>>._)).Returns(new List<AdjuntoNovedadFila>
            {
                new AdjuntoNovedadFila { NovedadId = 7, Id = 2, Nombre = "B.png", Tipo = "image/png", Tamano = 20, Orden = 2 },
                new AdjuntoNovedadFila { NovedadId = 7, Id = 1, Nombre = "A.pdf", Tipo = "application/pdf", Tamano = 10, Orden = 1 }
            });

            var resultado = controller.GetAdjuntos(7) as OkNegotiatedContentResult<List<AdjuntoNovedadDTO>>;

            CollectionAssert.AreEqual(new[] { 1, 2 }, resultado.Content.Select(a => a.Id).ToArray());
            Assert.AreEqual(1, resultado.Content[0].Orden);
        }

        // ---- En las novedades y sugerencias ----

        private static NovedadDTO Novedad(int id, string version) =>
            new NovedadDTO { Id = id, Version = version, Fecha = new DateTime(2026, 10, 8), Categoria = "Nuevo", Titulo = "N" + id, Ambito = "Nesto" };

        [TestMethod]
        public void GetNovedades_LlevanSusAdjuntos_DeUnaSolaConsulta_YListaVaciaSiNoTienen()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO> { Novedad(1, "1.10.38.0"), Novedad(2, "1.10.38.1") });
            A.CallTo(() => adjuntos.LeerResumen(A<IEnumerable<int>>._)).Returns(new List<AdjuntoNovedadFila>
            {
                new AdjuntoNovedadFila { NovedadId = 2, Id = 11, Nombre = "Normas.pdf", Tipo = "application/pdf", Tamano = 1234, Orden = 1 }
            });

            var resultado = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            A.CallTo(() => adjuntos.LeerResumen(A<IEnumerable<int>>.That.Matches(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 2 }))))
                .MustHaveHappenedOnceExactly();
            NovedadDTO dos = resultado.Content.Single(n => n.Id == 2);
            Assert.AreEqual(1, dos.Adjuntos.Count);
            Assert.AreEqual("Normas.pdf", dos.Adjuntos[0].Nombre);
            Assert.AreEqual(1234, dos.Adjuntos[0].Tamano);
            Assert.AreEqual(0, resultado.Content.Single(n => n.Id == 1).Adjuntos.Count);
        }

        [TestMethod]
        public void GetNovedades_SiLaTablaNoExiste_ListaVaciaYNadaFalla()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO> { Novedad(1, "1.10.38.0") });
            A.CallTo(() => adjuntos.LeerResumen(A<IEnumerable<int>>._))
                .Throws(new InvalidOperationException("El nombre de objeto 'dbo.NovedadesAdjuntos' no es válido."));

            var resultado = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotNull(resultado);
            Assert.IsNotNull(resultado.Content.Single().Adjuntos);
            Assert.AreEqual(0, resultado.Content.Single().Adjuntos.Count);
        }

        [TestMethod]
        public void GetSugerencias_YBuscar_LlevanSusAdjuntos()
        {
            var fila = new NovedadConSugerenciaFila { Id = 3, Fecha = DateTime.Today, Categoria = "Nuevo", Titulo = "Cupones", Ambito = "Nesto", Estado = "Pendiente", TextoOriginal = "cupones" };
            A.CallTo(() => servicio.LeerSugerencias(A<bool>._)).Returns(new List<NovedadConSugerenciaFila> { fila });
            A.CallTo(() => servicio.Buscar(A<IReadOnlyList<string>>._)).Returns(new List<NovedadConSugerenciaFila> { fila });
            A.CallTo(() => adjuntos.LeerResumen(A<IEnumerable<int>>._)).Returns(new List<AdjuntoNovedadFila>
            {
                new AdjuntoNovedadFila { NovedadId = 3, Id = 12, Nombre = "Foto.jpg", Tipo = "image/jpeg", Tamano = 99, Orden = 1 }
            });

            var sugerencias = controller.GetSugerencias(incluirCerradas: true) as OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>;
            var encontradas = controller.GetBuscar("cupones") as OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>;

            Assert.AreEqual(12, sugerencias.Content.Single().Adjuntos.Single().Id);
            Assert.AreEqual("image/jpeg", encontradas.Content.Single().Adjuntos.Single().Tipo);
        }

        [TestMethod]
        public void GetNovedades_SinServicioDeAdjuntos_ListaVacia()
        {
            controller.Adjuntos = null;
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO> { Novedad(1, "1.10.38.0") });

            var resultado = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.AreEqual(0, resultado.Content.Single().Adjuntos.Count);
        }

        // ---- Reglas ----

        [TestMethod]
        public void NormalizarNombre_QuitaLaRuta_YRecortaConservandoLaExtension()
        {
            Assert.AreEqual("Normas.pdf", ReglasAdjuntosNovedades.NormalizarNombre("C:\\Users\\Carlos\\Normas.pdf"));
            Assert.AreEqual("Normas.pdf", ReglasAdjuntosNovedades.NormalizarNombre("\"Normas.pdf\""));
            string largo = ReglasAdjuntosNovedades.NormalizarNombre(new string('a', 300) + ".pdf");
            Assert.AreEqual(ReglasAdjuntosNovedades.LONGITUD_MAXIMA_NOMBRE, largo.Length);
            Assert.IsTrue(largo.EndsWith(".pdf"));
        }
    }
}
