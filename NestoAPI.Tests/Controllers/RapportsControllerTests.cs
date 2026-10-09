using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#619: POST api/Rapports/ReentrenarModeloLlamadas encola el reentrenamiento (solo Dirección e Informática).</summary>
    [TestClass]
    public class RapportsControllerTests
    {
        private List<(bool simular, string usuario)> encolados;

        private RapportsController Controller(Func<bool, string, string> encolar = null)
        {
            encolados = new List<(bool, string)>();
            return new RapportsController(encolar ?? ((s, u) =>
            {
                encolados.Add((s, u));
                return "42";
            }))
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
        }

        [TestMethod]
        public void ReentrenarModeloLlamadas_SinSerDireccionNiInformatica_Forbidden()
        {
            RapportsController controller = Controller();
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Vendedor"), new[] { "NUEVAVISION\\Vendedores" });

            IHttpActionResult resultado = controller.ReentrenarModeloLlamadas(simular: false);

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            Assert.AreEqual(0, encolados.Count);
        }

        [TestMethod]
        public void ReentrenarModeloLlamadas_SinUsuario_Forbidden()
        {
            RapportsController controller = Controller();

            Assert.IsInstanceOfType(controller.ReentrenarModeloLlamadas(), typeof(StatusCodeResult));
            Assert.AreEqual(0, encolados.Count);
        }

        [TestMethod]
        public void ReentrenarModeloLlamadas_Direccion_EncolaY202()
        {
            RapportsController controller = Controller();
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" });

            IHttpActionResult resultado = controller.ReentrenarModeloLlamadas(simular: false);

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.ExecuteAsync(default).Result.StatusCode);
            Assert.AreEqual(1, encolados.Count);
            Assert.AreEqual((false, "NUEVAVISION\\Carlos"), encolados[0]);
        }

        [TestMethod]
        public void ReentrenarModeloLlamadas_Informatica_PorDefectoSimula()
        {
            RapportsController controller = Controller();
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Informatica" });

            IHttpActionResult resultado = controller.ReentrenarModeloLlamadas();

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.ExecuteAsync(default).Result.StatusCode);
            Assert.IsTrue(encolados[0].simular);
        }

        [TestMethod]
        public void ReentrenarModeloLlamadas_SinHangfire_503()
        {
            RapportsController controller = Controller((s, u) => throw new InvalidOperationException("JobStorage.Current property value has not been initialized"));
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" });

            IHttpActionResult resultado = controller.ReentrenarModeloLlamadas(simular: false);

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.ExecuteAsync(default).Result.StatusCode);
        }
    }
}
