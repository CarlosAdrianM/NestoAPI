using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class VendedoresControllerTests
    {
        private IServicioVendedores _servicio;
        private VendedoresController _controller;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioVendedores>();
            _controller = new VendedoresController(_servicio);
        }

        [TestMethod]
        public async Task GetVendedores_EquipoSinEmpresa_Devuelve400SinLlegarAlServicio()
        {
            // 02/09/26: la ficha de cliente de Nesto (Nesto#458) pedía el equipo con la empresa
            // en blanco y acababa en un 500 con ArgumentNullException en ELMAH (12 en una mañana).
            IHttpActionResult resultado = await _controller.GetVendedores("", "AL");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => _servicio.VendedoresEquipo(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetVendedores_EquipoConEmpresa_DelegaEnElServicio()
        {
            var equipo = new List<VendedorDTO> { new VendedorDTO { vendedor = "AL", nombre = "Alfredo" } };
            A.CallTo(() => _servicio.VendedoresEquipo("1", "AL")).Returns(equipo);

            IHttpActionResult resultado = await _controller.GetVendedores("1", "AL");

            var ok = resultado as OkNegotiatedContentResult<List<VendedorDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreSame(equipo, ok.Content);
        }
    }
}
