using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models.Novedades;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#822 / Nesto#623: GET api/Novedades/{id} sin ámbito. La campana es una por persona y un aviso puede ser de una
    /// sugerencia de Ariadna o NestoApp: Nesto la pide por su id, sea del ámbito que sea y esté como esté.
    /// </summary>
    [TestClass]
    public class NovedadesPorIdTests
    {
        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private NovedadesController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            A.CallTo(() => servicio.LeerPrivadas()).Returns(new List<SugerenciaPrivadaFila>
            {
                new SugerenciaPrivadaFila { Id = 900, SugeridaPor = "NUEVAVISION\\Manuel", SugeridaNombre = "Manuel" }
            });
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                new NovedadDTO { Id = 10, Version = "2.30", Ambito = "Ariadna", Titulo = "Inventario en la PDA", Categoria = "Nuevo" },
                new NovedadDTO { Id = 11, Version = "1.10.56.0", Ambito = "Nesto", Titulo = "Otra", Categoria = "Nuevo" }
            });
            A.CallTo(() => servicio.LeerSugerencias(true)).Returns(new List<NovedadConSugerenciaFila>
            {
                Sugerencia(1053, "Ariadna", ReglasSugerenciasNovedades.ESTADO_PENDIENTE, "NUEVAVISION\\Paloma"),
                Sugerencia(1054, "NestoApp", "Descartada", "NUEVAVISION\\Alfredo"),
                Sugerencia(900, "Nesto", ReglasSugerenciasNovedades.ESTADO_PENDIENTE, "NUEVAVISION\\Manuel")
            });
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new HttpRequestMessage()
            };
            ComoUsuario("NUEVAVISION\\Alfredo");
        }

        private void ComoUsuario(string usuario)
        {
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, usuario),
                new Claim(ClaimTypes.Name, usuario),
                new Claim(ClaimTypes.AuthenticationMethod, "Windows")
            }, "Bearer"));
        }

        private static NovedadConSugerenciaFila Sugerencia(int id, string ambito, string estado, string autor) =>
            new NovedadConSugerenciaFila
            {
                Id = id,
                Version = null,
                Fecha = DateTime.Today,
                Categoria = "Nuevo",
                Titulo = "Sugerencia " + id,
                Ambito = ambito,
                TextoOriginal = "Texto",
                Estado = estado,
                SugeridaPor = autor,
                SugeridaNombre = autor.Substring(autor.IndexOf('\\') + 1),
                SugeridaFecha = DateTime.Today
            };

        [TestMethod]
        public void GetNovedad_SugerenciaDeAriadna_SeDevuelveConSuAmbitoYComoSugerencia()
        {
            var resultado = controller.GetNovedad(1053) as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado, "Mismo DTO que GET api/Novedades/Sugerencias");
            Assert.AreEqual(1053, resultado.Content.Id);
            Assert.AreEqual("Ariadna", resultado.Content.Ambito);
            Assert.IsTrue(resultado.Content.EsSugerencia);
            Assert.AreEqual(ReglasSugerenciasNovedades.ESTADO_PENDIENTE, resultado.Content.Estado);
        }

        [TestMethod]
        public void GetNovedad_SugerenciaCerradaDeOtraAplicacion_TambienSale()
        {
            var resultado = controller.GetNovedad(1054) as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("NestoApp", resultado.Content.Ambito);
            Assert.AreEqual("Descartada", resultado.Content.Estado);
            A.CallTo(() => servicio.LeerSugerencias(true)).MustHaveHappened();
        }

        [TestMethod]
        public void GetNovedad_NovedadDelChangelogDeAriadna_SeDevuelveComoNovedad()
        {
            var resultado = controller.GetNovedad(10) as OkNegotiatedContentResult<NovedadDTO>;

            Assert.IsNotNull(resultado, "Mismo DTO que GET api/Novedades");
            Assert.AreEqual("Ariadna", resultado.Content.Ambito);
            Assert.AreEqual("2.30", resultado.Content.Version);
            Assert.IsFalse(resultado.Content.EsSugerencia);
            A.CallTo(() => servicio.LeerSugerencias(A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetNovedad_NoExiste_NotFound()
        {
            Assert.IsInstanceOfType(controller.GetNovedad(424242), typeof(NotFoundResult));
        }

        [TestMethod]
        public void GetNovedad_PrivadaDeOtro_NotFound()
        {
            Assert.IsInstanceOfType(controller.GetNovedad(900), typeof(NotFoundResult), "NestoAPI#650: para quien no la puede ver, no existe");
        }

        [TestMethod]
        public void GetNovedad_PrivadaDelAutor_Sale()
        {
            ComoUsuario("NUEVAVISION\\Manuel");

            var resultado = controller.GetNovedad(900) as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Content.Privada);
        }

        [TestMethod]
        public void GetNovedad_EsSugerenciaSaleEnElJson()
        {
            JObject sugerencia = JObject.FromObject(new SugerenciaNovedadDTO { Id = 1, Ambito = "Ariadna" });
            JObject novedad = JObject.FromObject(new NovedadDTO { Id = 2, Ambito = "Nesto" });

            Assert.AreEqual(true, (bool)sugerencia["EsSugerencia"]);
            Assert.AreEqual(false, (bool)novedad["EsSugerencia"]);
            Assert.AreEqual("Ariadna", (string)sugerencia["Ambito"]);
        }

        [TestMethod]
        public void GetNovedad_RutaPorIdYConAuthorize()
        {
            MethodInfo metodo = typeof(NovedadesController).GetMethod(nameof(NovedadesController.GetNovedad));

            Assert.IsTrue(metodo.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Any(r => r.Template == "api/Novedades/{id:int}"));
            Assert.IsTrue(metodo.GetCustomAttributes(typeof(AuthorizeAttribute), false).Any());
            Assert.IsTrue(metodo.GetCustomAttributes(typeof(HttpGetAttribute), false).Any());
        }
    }
}
