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
    /// NestoAPI#703: al implementar una sugerencia, el ámbito tiene que cuadrar con la versión. El 23/10/26 la 753
    /// (escrita desde Nesto) se implementó en Ariadna 2.17 sin cambiar el ámbito, y el popup de Nesto 1.10.48.1 abrió
    /// la página «2.17» en vez de la de la 1.10.48.0. Dos defensas: el PUT corrige el ámbito y el GET sin ámbito
    /// (Nesto de escritorio) no devuelve versiones que no sean 1.x.
    /// </summary>
    [TestClass]
    public class NovedadesAmbitoVersionTests
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
            ComoDireccion();
            A.CallTo(() => servicio.ActualizarSugerencia(A<int>._, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).Returns(true);
        }

        private void ComoDireccion()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, @"NUEVAVISION\Carlos"),
                new Claim(ClaimTypes.Name, @"NUEVAVISION\Carlos"),
                new Claim(ClaimTypes.Role, @"NUEVAVISION\Dirección")
            };
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        }

        private static NovedadDTO Novedad(int id, string version, string ambito, string titulo = "Titulo") => new NovedadDTO
        {
            Id = id,
            Version = version,
            Fecha = new DateTime(2026, 10, 23),
            Categoria = "Nuevo",
            Titulo = titulo,
            Ambito = ambito
        };

        #region Las reglas puras

        [TestMethod]
        public void AmbitoAlImplementar_SugerenciaDeNestoEnVersion2_PasaAAriadna()
        {
            // El caso de la 753: nació en Nesto y se hizo en Ariadna 2.17
            string ambito = ReglasSugerenciasNovedades.AmbitoAlImplementar("Nesto", "2.17", null, out string error);

            Assert.IsNull(error);
            Assert.AreEqual("Ariadna", ambito);
        }

        [TestMethod]
        public void AmbitoAlImplementar_SugerenciaDeNestoEnVersion1_SeQuedaComoEsta()
        {
            Assert.IsNull(ReglasSugerenciasNovedades.AmbitoAlImplementar("Nesto", "1.10.48.0", null, out string error), "null = no se toca");
            Assert.IsNull(error);
            Assert.IsNull(ReglasSugerenciasNovedades.AmbitoAlImplementar("NestoAPI", "1.10.48.0", null, out error), "NestoAPI también es escritorio");
            Assert.IsNull(error);
        }

        [TestMethod]
        public void AmbitoAlImplementar_SugerenciaDeUnaAppEnVersion1_PasaANesto()
        {
            Assert.AreEqual("Nesto", ReglasSugerenciasNovedades.AmbitoAlImplementar("NestoApp", "1.10.48.0", null, out _));
            Assert.AreEqual("Nesto", ReglasSugerenciasNovedades.AmbitoAlImplementar("Ariadna", "1.10.48.0", null, out _));
        }

        [TestMethod]
        public void AmbitoAlImplementar_SugerenciaDeNestoAppEnVersion2_SeQuedaEnNestoApp()
        {
            // NestoApp también va por 2.x (2.20.5): una suya implementada en 2.21 no es de Ariadna
            Assert.IsNull(ReglasSugerenciasNovedades.AmbitoAlImplementar("NestoApp", "2.21.0", null, out string error));
            Assert.IsNull(error);
            Assert.IsNull(ReglasSugerenciasNovedades.AmbitoAlImplementar("Ariadna", "2.18", null, out error));
            Assert.IsNull(error);
        }

        [TestMethod]
        public void AmbitoAlImplementar_AmbitoExplicito_Manda()
        {
            Assert.AreEqual("NestoApp", ReglasSugerenciasNovedades.AmbitoAlImplementar("Nesto", "2.21.0", "nestoapp", out string error), "Sin distinguir mayúsculas y canónico");
            Assert.IsNull(error);
            Assert.AreEqual("Nesto", ReglasSugerenciasNovedades.AmbitoAlImplementar("Ariadna", null, "Nesto", out error), "También sin versión (reclasificar)");
            Assert.IsNull(error);
        }

        [TestMethod]
        public void AmbitoAlImplementar_AmbitoDesconocido_Error()
        {
            Assert.IsNull(ReglasSugerenciasNovedades.AmbitoAlImplementar("Nesto", "2.17", "Odoo", out string error));
            StringAssert.Contains(error, "Nesto, NestoAPI, NestoApp, Ariadna");
        }

        [TestMethod]
        public void AmbitoAlImplementar_VersionQueNoEs1xNi2x_NoSePuedeDeducir()
        {
            Assert.IsNull(ReglasSugerenciasNovedades.AmbitoAlImplementar("Nesto", "3.0.1", null, out string error));
            StringAssert.Contains(error, "3.0.1");
            StringAssert.Contains(error, "indica el ámbito");
        }

        [TestMethod]
        public void AmbitoAlImplementar_SinVersionNiAmbito_NoTocaNada()
        {
            Assert.IsNull(ReglasSugerenciasNovedades.AmbitoAlImplementar("Nesto", null, null, out string error));
            Assert.IsNull(error);
        }

        [TestMethod]
        public void EsVersionDelEscritorio_Solo1x()
        {
            Assert.IsTrue(ReglasSugerenciasNovedades.EsVersionDelEscritorio("1.10.48.0"));
            Assert.IsFalse(ReglasSugerenciasNovedades.EsVersionDelEscritorio("2.17"));
            Assert.IsFalse(ReglasSugerenciasNovedades.EsVersionDelEscritorio("2.20.5"));
            Assert.IsTrue(ReglasSugerenciasNovedades.EsVersionDelEscritorio(null), "Las sugerencias no tienen versión");
            Assert.IsTrue(ReglasSugerenciasNovedades.EsVersionDelEscritorio("mal grabada"), "Mejor enseñar de más");
        }

        #endregion

        #region PUT api/Novedades/Sugerencias/{id}

        [TestMethod]
        public void PutSugerencia_DeNestoImplementadaEnAriadna217_GrabaAmbitoAriadna()
        {
            A.CallTo(() => servicio.LeerAmbitoSugerencia(753)).Returns("Nesto");
            ActualizarSugerenciaNovedadDTO grabado = null;
            A.CallTo(() => servicio.ActualizarSugerencia(753, A<ActualizarSugerenciaNovedadDTO>._, A<string>._))
                .Invokes((int _, ActualizarSugerenciaNovedadDTO c, string __) => grabado = c)
                .Returns(true);

            var resultado = controller.PutSugerencia(753, new ActualizarSugerenciaNovedadDTO { Version = "2.17" });

            Assert.AreEqual(HttpStatusCode.NoContent, (resultado as StatusCodeResult).StatusCode);
            Assert.AreEqual("Ariadna", grabado.Ambito);
            Assert.AreEqual(ReglasSugerenciasNovedades.ESTADO_IMPLEMENTADA, grabado.Estado);
        }

        [TestMethod]
        public void PutSugerencia_DeNestoImplementadaEn11048_NoTocaElAmbito()
        {
            A.CallTo(() => servicio.LeerAmbitoSugerencia(760)).Returns("Nesto");
            ActualizarSugerenciaNovedadDTO grabado = null;
            A.CallTo(() => servicio.ActualizarSugerencia(760, A<ActualizarSugerenciaNovedadDTO>._, A<string>._))
                .Invokes((int _, ActualizarSugerenciaNovedadDTO c, string __) => grabado = c)
                .Returns(true);

            var resultado = controller.PutSugerencia(760, new ActualizarSugerenciaNovedadDTO { Version = "1.10.48.0" });

            Assert.AreEqual(HttpStatusCode.NoContent, (resultado as StatusCodeResult).StatusCode);
            Assert.IsNull(grabado.Ambito, "null = ISNULL(@ambito, Ambito) lo deja como estaba");
        }

        [TestMethod]
        public void PutSugerencia_ConAmbitoExplicito_SeGrabaEse()
        {
            A.CallTo(() => servicio.LeerAmbitoSugerencia(800)).Returns("Nesto");
            ActualizarSugerenciaNovedadDTO grabado = null;
            A.CallTo(() => servicio.ActualizarSugerencia(800, A<ActualizarSugerenciaNovedadDTO>._, A<string>._))
                .Invokes((int _, ActualizarSugerenciaNovedadDTO c, string __) => grabado = c)
                .Returns(true);

            _ = controller.PutSugerencia(800, new ActualizarSugerenciaNovedadDTO { Version = "2.21.0", Ambito = "NestoApp" });

            Assert.AreEqual("NestoApp", grabado.Ambito);
        }

        [TestMethod]
        public void PutSugerencia_VersionSinProgramaConocido_400SinTocarNada()
        {
            A.CallTo(() => servicio.LeerAmbitoSugerencia(801)).Returns("Nesto");

            var resultado = controller.PutSugerencia(801, new ActualizarSugerenciaNovedadDTO { Version = "3.0.0" });

            var malaPeticion = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(malaPeticion);
            StringAssert.Contains(malaPeticion.Message, "indica el ámbito");
            A.CallTo(() => servicio.ActualizarSugerencia(A<int>._, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PutSugerencia_AmbitoDesconocido_400()
        {
            var resultado = controller.PutSugerencia(802, new ActualizarSugerenciaNovedadDTO { Version = "2.17", Ambito = "Odoo" });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.ActualizarSugerencia(A<int>._, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PutSugerencia_SinVersion_NoLeeNiTocaElAmbito()
        {
            _ = controller.PutSugerencia(803, new ActualizarSugerenciaNovedadDTO { Estado = "Aceptada" });

            A.CallTo(() => servicio.LeerAmbitoSugerencia(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => servicio.ActualizarSugerencia(803, A<ActualizarSugerenciaNovedadDTO>.That.Matches(c => c.Ambito == null), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        #endregion

        #region GET api/Novedades: la defensa del popup de Nesto

        [TestMethod]
        public void GetNovedades_SinAmbito_NoDevuelveUna217AunqueTengaAmbitoNesto()
        {
            // La 753 tal y como quedó el 23/10/26: ámbito Nesto, versión 2.17. Nesto pide sin ámbito y
            // desdeVersion=1.10.47.0; sin esta defensa, 2.17 > 1.10.48 y la ventana abría la página «2.17».
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "1.10.48.0", "Nesto", "Puede ofrecer"),
                Novedad(753, "2.17", "Nesto", "Mal etiquetada")
            });
            controller.Request.Headers.UserAgent.ParseAdd("WPF-Client");

            var popup = controller.GetNovedades(desdeVersion: "1.10.47.0") as OkNegotiatedContentResult<List<NovedadDTO>>;
            var todas = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            CollectionAssert.AreEquivalent(new[] { 1 }, popup.Content.Select(n => n.Id).ToList());
            CollectionAssert.AreEquivalent(new[] { 1 }, todas.Content.Select(n => n.Id).ToList());
        }

        [TestMethod]
        public void GetNovedades_SugerenciaDeNestoImplementadaEnAriadna_SaleEnAriadnaYNoEnNesto()
        {
            // Tras el PUT corregido: ámbito Ariadna, versión 2.17
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "1.10.48.0", "Nesto"),
                Novedad(753, "2.17", "Ariadna", "Reposiciones juntas")
            });

            var nesto = controller.GetNovedades(desdeVersion: "1.10.47.0") as OkNegotiatedContentResult<List<NovedadDTO>>;
            var ariadna = controller.GetNovedades(desdeVersion: "2.16", ambito: "Ariadna") as OkNegotiatedContentResult<List<NovedadDTO>>;

            CollectionAssert.AreEquivalent(new[] { 1 }, nesto.Content.Select(n => n.Id).ToList(), "El popup de Nesto no la enseña");
            CollectionAssert.AreEquivalent(new[] { 753 }, ariadna.Content.Select(n => n.Id).ToList(), "El de Ariadna sí");
        }

        [TestMethod]
        public void GetNovedades_ConAmbito_NoAplicaElCorteDeVersion()
        {
            // Con ámbito, el espacio de versiones es el del producto: una 2.x de Ariadna es normal
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(10, "2.17", "Ariadna"),
                Novedad(11, "2.18", "Ariadna")
            });

            var ariadna = controller.GetNovedades(ambito: "Ariadna") as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.AreEqual(2, ariadna.Content.Count);
        }

        [TestMethod]
        public void GetNovedades_SinAmbito_LasDeVersionNoParseableSiguenSaliendo()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "1.10.48.0", "Nesto"),
                Novedad(2, "por publicar", "Nesto")
            });

            var resultado = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.AreEqual(2, resultado.Content.Count, "Mejor enseñar de más que perder una novedad por un dato mal grabado");
        }

        #endregion
    }
}
