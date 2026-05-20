using System;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Vendedores;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#201: <c>GET /api/Clientes?empresa=1&amp;filtro=</c> (filtro vacío en el query
    /// string lo binda Web API como <c>null</c>) provoca <c>NullReferenceException</c> en
    /// <see cref="ClientesController.GetClientes(string, string)"/> al acceder a <c>filtro.Length</c>.
    /// El overload con vendedor (<see cref="ClientesController.GetClientes(string, string, string)"/>)
    /// ya gestiona null correctamente lanzando "Por favor, utilice un filtro de al menos 4 caracteres".
    /// Espejamos ese patrón en los otros dos overloads.
    /// </summary>
    [TestClass]
    public class ClientesControllerTests
    {
        private static ClientesController CrearController()
        {
            return new ClientesController(
                A.Fake<IGestorClientes>(),
                A.Fake<IServicioVendedores>(),
                A.Fake<IGestorSincronizacion>());
        }

        [TestMethod]
        public void GetClientes_EmpresaFiltro_FiltroNull_LanzaExcepcionDeValidacion()
        {
            var controller = CrearController();

            var ex = Assert.ThrowsException<Exception>(() =>
                controller.GetClientes("1", filtro: null));

            // No debe ser NullReferenceException: debe ser la validación de filtro mínimo.
            Assert.IsNotInstanceOfType(ex, typeof(NullReferenceException),
                "Filtro null debe disparar la validación de 4 caracteres, no un NullRef.");
            StringAssert.Contains(ex.Message, "4 caracteres");
        }

        [TestMethod]
        public void GetClientes_SoloFiltro_FiltroNull_LanzaExcepcionDeValidacion()
        {
            var controller = CrearController();

            var ex = Assert.ThrowsException<Exception>(() =>
                controller.GetClientes(filtro: null));

            Assert.IsNotInstanceOfType(ex, typeof(NullReferenceException),
                "Filtro null debe disparar la validación de 4 caracteres, no un NullRef.");
            StringAssert.Contains(ex.Message, "4 caracteres");
        }
    }
}
