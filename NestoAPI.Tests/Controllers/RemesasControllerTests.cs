using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Nesto#340 Fase 1C.14 slice 2: remesas de cobro para el grid de RemesasViewModel.
    /// </summary>
    [TestClass]
    public class RemesasControllerTests
    {
        [TestMethod]
        public async Task GetRemesas_ConTop_DevuelveLasRemesasDelServicio()
        {
            IRemesasService servicio = A.Fake<IRemesasService>();
            List<RemesaDTO> remesas = new List<RemesaDTO>
            {
                new RemesaDTO { Numero = 10897, Fecha = new DateTime(2026, 7, 17), Importe = 10546.66M, Banco = "5" },
                new RemesaDTO { Numero = 10896, Fecha = new DateTime(2026, 7, 16), Importe = 9420.99M, Banco = "5" }
            };
            _ = A.CallTo(() => servicio.LeerRemesasAsync("1", 100)).Returns(remesas);
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetRemesas("1", 100) as OkNegotiatedContentResult<List<RemesaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.AreEqual(10897, resultado.Content[0].Numero);
        }

        [TestMethod]
        public async Task GetRemesas_SinTop_PideTodasAlServicio()
        {
            // El botón "Ver Todas" llama sin top: el servicio debe recibir null (sin límite).
            IRemesasService servicio = A.Fake<IRemesasService>();
            _ = A.CallTo(() => servicio.LeerRemesasAsync("1", null)).Returns(new List<RemesaDTO>());
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetRemesas("1") as OkNegotiatedContentResult<List<RemesaDTO>>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => servicio.LeerRemesasAsync("1", null)).MustHaveHappenedOnceExactly();
        }
    }
}
