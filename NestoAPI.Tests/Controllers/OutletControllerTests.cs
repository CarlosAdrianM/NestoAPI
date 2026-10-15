using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Outlet;
using NestoAPI.Tests.Infrastructure.Outlet;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#643: GET api/Outlet/Previsualizar (solo Dirección y Compras; nunca escribe).</summary>
    [TestClass]
    public class OutletControllerTests
    {
        private IRepositorioOutlet repositorio;

        private OutletController Controller(params string[] grupos)
        {
            repositorio = A.Fake<IRepositorioOutlet>();
            _ = A.CallTo(() => repositorio.CargarSituacion()).Returns(Task.FromResult(new List<SituacionOutletProducto>
            {
                ReglasOutletTests.Producto("1", "PEL", "TIN"),
                ReglasOutletTests.Producto("2", "COS", "124", estado: 0, actuales: new[] { ReglasOutlet.ESTETICA })
            }));
            ServicioOutlet servicio = new ServicioOutlet(repositorio, clave => "true", e => { });
            return new OutletController(() => servicio)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Usuario"), grupos)
            };
        }

        [TestMethod]
        public async Task Previsualizar_SinSerDireccionNiCompras_Forbidden()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Vendedores", "NUEVAVISION\\Almacén").Previsualizar();

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => repositorio.CargarSituacion()).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow("NUEVAVISION\\Dirección")]
        [DataRow("NUEVAVISION\\Compras")]
        public async Task Previsualizar_DireccionOCompras_DevuelveLaListaSinEscribir(string grupo)
        {
            IHttpActionResult resultado = await Controller(grupo).Previsualizar();

            ResultadoCuadreOutlet contenido = ((OkNegotiatedContentResult<ResultadoCuadreOutlet>)resultado).Content;
            Assert.AreEqual(1, contenido.Entran);
            Assert.AreEqual(1, contenido.Salen);
            Assert.IsTrue(contenido.InterruptorEncendido);
            Assert.IsFalse(contenido.Aplicado);
            A.CallTo(() => repositorio.AplicarCambio(A<CambioOutlet>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.EncolarRepublicacion(A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }
    }
}
