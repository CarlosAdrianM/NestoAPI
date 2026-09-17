using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Issue Nesto#372: GET api/Novedades devuelve el changelog de usuario, opcionalmente solo
    /// las entradas de versiones posteriores a la última que el usuario ya vio (desdeVersion).
    /// </summary>
    [TestClass]
    public class NovedadesControllerTests
    {
        private IServicioNovedades servicio;
        private NovedadesController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            controller = new NovedadesController(servicio);
        }

        private static NovedadDTO Novedad(int id, string version, string titulo = "Titulo")
        {
            return new NovedadDTO
            {
                Id = id,
                Version = version,
                Fecha = new DateTime(2026, 6, 12),
                Categoria = "Mejorado",
                Titulo = titulo,
                Ambito = "Nesto"
            };
        }

        [TestMethod]
        public void GetNovedades_SinDesdeVersion_DevuelveTodasLasPublicadas()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "1.10.4.0"),
                Novedad(2, "1.10.5.0")
            });

            var resultado = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
        }

        // NestoAPI#489: NestoApp consume el changelog con su propio ámbito y su propio espacio de versiones.

        [TestMethod]
        public void GetNovedades_ConAmbito_SoloDevuelveLasDeEseProducto()
        {
            NovedadDTO app = Novedad(3, "2.20.0", "Solo app");
            app.Ambito = "NestoApp";
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "1.10.4.0"),
                Novedad(2, "1.10.5.0"),
                app
            });

            var resultado = controller.GetNovedades(ambito: "nestoapp") as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.AreEqual(1, resultado.Content.Count, "Solo las de NestoApp (sin distinguir mayúsculas)");
            Assert.AreEqual("Solo app", resultado.Content.Single().Titulo);
        }

        [TestMethod]
        public void GetNovedades_ConAmbitoYDesdeVersion_ElCorteDeVersionEsDentroDelProducto()
        {
            // Nesto va por 1.10.x y NestoApp por 2.x: sin acotar el ámbito antes, desdeVersion=2.20.0
            // descartaría todo lo de Nesto (1.10 < 2.20) y colaría entradas ajenas.
            NovedadDTO appVieja = Novedad(3, "2.19.0", "App vieja");
            appVieja.Ambito = "NestoApp";
            NovedadDTO appNueva = Novedad(4, "2.21.0", "App nueva");
            appNueva.Ambito = "NestoApp";
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "1.10.4.0"),
                Novedad(2, "1.10.30.0"),
                appVieja,
                appNueva
            });

            var app = controller.GetNovedades(desdeVersion: "2.20.0", ambito: "NestoApp") as OkNegotiatedContentResult<List<NovedadDTO>>;
            Assert.AreEqual(1, app.Content.Count);
            Assert.AreEqual("App nueva", app.Content.Single().Titulo);

            var nesto = controller.GetNovedades(desdeVersion: "1.10.5.0", ambito: "Nesto") as OkNegotiatedContentResult<List<NovedadDTO>>;
            Assert.AreEqual(1, nesto.Content.Count);
            Assert.AreEqual(2, nesto.Content.Single().Id, "Las 2.x de la app no se cuelan aunque sean 'posteriores' a 1.10.5");
        }

        [TestMethod]
        public void GetNovedades_SinAmbito_DevuelveNestoYNestoAPIPeroNuncaLasDeLaApp()
        {
            // 17/09/26: el Nesto publicado no manda ámbito y solo desdeVersion=1.10.28.2; las 2.20.x de
            // NestoApp son "posteriores" y se colaron en su popup. Sin ámbito = escritorio (Nesto + NestoAPI).
            NovedadDTO app = Novedad(3, "2.20.1", "Solo app");
            app.Ambito = "NestoApp";
            NovedadDTO api = Novedad(4, "1.10.28.2", "Del API");
            api.Ambito = "NestoAPI";
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO> { Novedad(1, "1.10.28.2"), app, api });

            var todas = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;
            var desde = controller.GetNovedades("1.10.28.0") as OkNegotiatedContentResult<List<NovedadDTO>>;

            CollectionAssert.AreEquivalent(new[] { 1, 4 }, todas.Content.Select(n => n.Id).ToList());
            CollectionAssert.AreEquivalent(new[] { 1, 4 }, desde.Content.Select(n => n.Id).ToList(), "La 2.20.1 no se cuela aunque 2.20 > 1.10");
        }

        [TestMethod]
        public void GetNovedades_ConDesdeVersion_SoloDevuelveVersionesPosteriores()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "1.10.4.2"),
                Novedad(2, "1.10.5.0"),
                Novedad(3, "1.10.10.0") // comparación de Version, no alfabética: 1.10.10 > 1.10.5
            });

            var resultado = controller.GetNovedades("1.10.4.2") as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.IsFalse(resultado.Content.Any(n => n.Id == 1));
        }

        [TestMethod]
        public void GetNovedades_OrdenaPorVersionDescendente()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "1.10.4.0"),
                Novedad(2, "1.10.10.0"),
                Novedad(3, "1.10.5.0")
            });

            var resultado = controller.GetNovedades() as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content[0].Id);
            Assert.AreEqual(3, resultado.Content[1].Id);
            Assert.AreEqual(1, resultado.Content[2].Id);
        }

        [TestMethod]
        public void GetNovedades_DesdeVersionInvalida_DevuelveTodas()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "1.10.4.0"),
                Novedad(2, "1.10.5.0")
            });

            var resultado = controller.GetNovedades("no-es-una-version") as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
        }

        [TestMethod]
        public void GetNovedades_EntradaConVersionInvalida_NoSeFiltraNiRompe()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                Novedad(1, "pendiente"), // entrada sin versión asignada todavía
                Novedad(2, "1.10.5.0")
            });

            var resultado = controller.GetNovedades("1.10.4.0") as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
        }
    }
}
