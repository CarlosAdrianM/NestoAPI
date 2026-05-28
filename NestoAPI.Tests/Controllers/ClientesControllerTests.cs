using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Vendedores;
using System;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class ClientesControllerTests
    {
        // Regresión NestoAPI#201: GetClientes(empresa, filtro) lanzaba NullReferenceException
        // cuando filtro llegaba null (NestoApp manda ?filtro= al limpiar la búsqueda). Ahora debe
        // lanzar la excepción amistosa de "filtro de al menos 4 caracteres", no una NRE.
        [TestMethod]
        public void ClientesController_GetClientes_FiltroNull_NoLanzaNullReference()
        {
            ClientesController controller = new ClientesController(
                A.Fake<IGestorClientes>(),
                A.Fake<IServicioVendedores>(),
                A.Fake<IGestorSincronizacion>());

            try
            {
                _ = controller.GetClientes("1", null);
                Assert.Fail("Debía lanzar una excepción por filtro inválido");
            }
            catch (NullReferenceException)
            {
                Assert.Fail("No debe lanzar NullReferenceException");
            }
            catch (Exception ex)
            {
                Assert.AreEqual("Por favor, utilice un filtro de al menos 4 caracteres", ex.Message);
            }
        }
    }
}
