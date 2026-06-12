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
