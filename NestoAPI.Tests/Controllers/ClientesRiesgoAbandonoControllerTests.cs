using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Abandono;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#714 (corte 1): GET api/Clientes/RiesgoAbandono/Informe y POST Ejecutar, solo Dirección e Informática.</summary>
    [TestClass]
    public class ClientesRiesgoAbandonoControllerTests
    {
        private static readonly DateTime Hoy = new DateTime(2026, 10, 27, 10, 0, 0);
        private IRepositorioRiesgoAbandono repositorio;
        private ModoAbandono modo;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioRiesgoAbandono>();
            modo = ModoAbandono.Sombra;
            _ = A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerInforme(A<DateTime>._, A<DateTime>._)).Returns(new List<FilaInformeAbandono>());
        }

        private ClientesRiesgoAbandonoController Controlador(string grupo)
        {
            var controller = new ClientesRiesgoAbandonoController(repositorio, () => Hoy, () => modo);
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), grupo == null ? new string[0] : new[] { grupo });
            return controller;
        }

        [TestMethod]
        public void GetInforme_SinSerDireccionNiInformatica_Forbidden()
        {
            var resultado = Controlador("Vendedores").GetInforme() as StatusCodeResult;
            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.LeerInforme(A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetInforme_PorDefecto_Ultimos90DiasHastaHoyIncluido()
        {
            var resultado = Controlador(Constantes.GruposSeguridad.DIRECCION).GetInforme() as OkNegotiatedContentResult<InformeRiesgoAbandonoDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => repositorio.LeerInforme(Hoy.Date.AddDays(-90), Hoy.Date.AddDays(1))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(Hoy.Date, resultado.Content.Hasta);
            Assert.AreEqual("Sombra", resultado.Content.Modo);
        }

        [TestMethod]
        public void GetInforme_Informatica_ConFechas_HastaInclusivo()
        {
            var resultado = Controlador(NovedadesController.GRUPO_INFORMATICA).GetInforme(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)) as OkNegotiatedContentResult<InformeRiesgoAbandonoDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => repositorio.LeerInforme(new DateTime(2026, 11, 1), new DateTime(2026, 12, 1))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(new DateTime(2026, 11, 30), resultado.Content.Hasta);
        }

        [TestMethod]
        public void GetInforme_HastaAnteriorADesde_BadRequest()
        {
            Assert.IsInstanceOfType(Controlador(Constantes.GruposSeguridad.DIRECCION).GetInforme(new DateTime(2026, 11, 10), new DateTime(2026, 11, 9)), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void GetInforme_SinTabla_BadRequestQueDiceElScript()
        {
            _ = A.CallTo(() => repositorio.TablaExiste()).Returns(false);
            var resultado = Controlador(Constantes.GruposSeguridad.DIRECCION).GetInforme() as BadRequestErrorMessageResult;
            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "Issue714_ClientesRiesgoAbandono.sql");
        }

        [TestMethod]
        public void PostEjecutar_Apagado_BadRequest()
        {
            modo = ModoAbandono.Apagado;
            ClientesRiesgoAbandonoController controller = Controlador(Constantes.GruposSeguridad.DIRECCION);
            bool encolado = false;
            controller.Encolar = () => { encolado = true; return "1"; };

            Assert.IsInstanceOfType(controller.PostEjecutar(), typeof(BadRequestErrorMessageResult));
            Assert.IsFalse(encolado);
        }

        [TestMethod]
        public void PostEjecutar_Direccion_Encola()
        {
            ClientesRiesgoAbandonoController controller = Controlador(Constantes.GruposSeguridad.DIRECCION);
            bool encolado = false;
            controller.Encolar = () => { encolado = true; return "42"; };

            System.Web.Http.IHttpActionResult resultado = controller.PostEjecutar();

            Assert.IsTrue(resultado.GetType().Name.StartsWith("OkNegotiatedContentResult"), resultado.GetType().Name);
            Assert.IsTrue(encolado);
        }

        [TestMethod]
        public void PostEjecutar_SinPermiso_Forbidden()
        {
            ClientesRiesgoAbandonoController controller = Controlador(null);
            controller.Encolar = () => throw new InvalidOperationException("no debería encolar");
            var resultado = controller.PostEjecutar() as StatusCodeResult;
            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }
    }
}
