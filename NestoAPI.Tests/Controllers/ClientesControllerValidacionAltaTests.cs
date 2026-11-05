using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#819 (sugerencia 1049, Alfredo 05/11/26): POST y PUT api/Clientes recortan los textos antes de validar y,
    /// si algo no cabe, el 400 dice qué campo y cuál es el límite (nunca más BadRequest(ModelState)).
    /// </summary>
    [TestClass]
    public class ClientesControllerValidacionAltaTests
    {
        private IGestorClientes _gestor;

        private ClientesController Controller()
        {
            _gestor = A.Fake<IGestorClientes>();
            _ = A.CallTo(() => _gestor.CrearCliente(A<ClienteCrear>._, A<NVEntities>._))
                .Returns(new Cliente { Empresa = "1", Nº_Cliente = "42109", Contacto = "0" });
            _ = A.CallTo(() => _gestor.ModificarCliente(A<ClienteCrear>._, A<NVEntities>._))
                .Returns(new Cliente { Empresa = "1", Nº_Cliente = "42109", Contacto = "0" });
            return new ClientesController(_gestor, A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>());
        }

        private static ClienteCrear Alta(string correo)
        {
            return new ClienteCrear
            {
                Empresa = "1",
                Nombre = "PELUQUERÍA PRUEBA",
                PersonasContacto = new List<PersonaContactoDTO>
                {
                    new PersonaContactoDTO { Numero = 1, Nombre = "Alfredo", CorreoElectronico = correo }
                }
            };
        }

        private static string Correo(int longitud) => new string('a', longitud - 15) + "@nuevavision.es";

        [TestMethod]
        public async Task PostCliente_CorreoDe50ConEspacios_SeRecortaYSeCrea()
        {
            var controller = Controller();
            // El ModelState del binding se calculó sin recortar: el controlador debe limpiarlo y volver a validar.
            controller.ModelState.AddModelError("clienteCrear.PersonasContacto[0].CorreoElectronico", "demasiado largo");

            var resultado = await controller.PostCliente(Alta(Correo(50) + new string(' ', 15)));

            Assert.IsNotInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.IsNotInstanceOfType(resultado, typeof(InvalidModelStateResult));
            _ = A.CallTo(() => _gestor.CrearCliente(
                    A<ClienteCrear>.That.Matches(c => c.PersonasContacto.Single().CorreoElectronico == Correo(50)), A<NVEntities>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostCliente_CorreoDe63_400ConElCampoYElLimite()
        {
            var controller = Controller();

            var resultado = await controller.PostCliente(Alta(Correo(63)));

            var error = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(error, "Debe ser un 400 con mensaje, no BadRequest(ModelState)");
            Assert.AreEqual("El correo electrónico de la persona de contacto no puede superar 50 caracteres (tiene 63).", error.Message);
            A.CallTo(() => _gestor.CrearCliente(A<ClienteCrear>._, A<NVEntities>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutCliente_CorreoDe63YTelefonoDe35_400ConLosDos()
        {
            var controller = Controller();
            ClienteCrear cliente = Alta(Correo(63));
            cliente.Telefono = new string('9', 35);

            var resultado = await controller.PutCliente(cliente);

            var error = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(error);
            StringAssert.Contains(error.Message, "El teléfono no puede superar 29 caracteres (tiene 35).");
            StringAssert.Contains(error.Message, "correo electrónico de la persona de contacto no puede superar 50");
            A.CallTo(() => _gestor.ModificarCliente(A<ClienteCrear>._, A<NVEntities>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutCliente_CorreoDe50ConEspacios_SeRecortaYSeModifica()
        {
            var controller = Controller();

            var resultado = await controller.PutCliente(Alta(Correo(50) + "               "));

            Assert.IsNotInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            _ = A.CallTo(() => _gestor.ModificarCliente(A<ClienteCrear>._, A<NVEntities>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostCliente_DatoQueNoSePudoLeer_400QueDiceCual()
        {
            var controller = Controller();
            controller.ModelState.AddModelError("clienteCrear.Estado", new System.FormatException("Could not convert string to short"));

            var resultado = await controller.PostCliente(Alta(Correo(30)));

            Assert.AreEqual("El dato «Estado» no tiene un formato válido.", (resultado as BadRequestErrorMessageResult)?.Message);
        }

        // NestoAPI#821: el CP español que no existe y no viene de Google → 400 con el motivo, no un 500.

        [TestMethod]
        public async Task PostCliente_CodigoPostalNoExiste_400ConElMensaje()
        {
            var controller = Controller();
            _ = A.CallTo(() => _gestor.CrearCliente(A<ClienteCrear>._, A<NVEntities>._))
                .Throws(new NestoAPI.Infraestructure.Clientes.CodigoPostalNoExisteException(GestorClientes.MensajeCodigoPostalNoExiste("48530")));

            var resultado = await controller.PostCliente(Alta(Correo(30)));

            Assert.AreEqual("No existe el código postal 48530. Elija la dirección en el buscador de Google para darlo de alta.",
                (resultado as BadRequestErrorMessageResult)?.Message);
        }

        [TestMethod]
        public async Task PutCliente_CodigoPostalNoExiste_400ConElMensaje()
        {
            var controller = Controller();
            _ = A.CallTo(() => _gestor.ModificarCliente(A<ClienteCrear>._, A<NVEntities>._))
                .Throws(new NestoAPI.Infraestructure.Clientes.CodigoPostalNoExisteException(GestorClientes.MensajeCodigoPostalNoExiste("48530")));

            var resultado = await controller.PutCliente(Alta(Correo(30)));

            StringAssert.Contains((resultado as BadRequestErrorMessageResult)?.Message, "Elija la dirección en el buscador de Google");
        }

        [TestMethod]
        public async Task PostCliente_SinCuerpo_400ConMensaje()
        {
            var resultado = await Controller().PostCliente(null);

            Assert.AreEqual("No han llegado los datos del cliente.", (resultado as BadRequestErrorMessageResult)?.Message);
        }
    }
}
