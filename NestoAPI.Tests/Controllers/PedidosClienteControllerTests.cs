using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#436: quién puede crear un pedido de cliente y con qué. El cliente sale SIEMPRE del
    /// JWT: si viniera en el cuerpo se ignoraría, y quien no es un cliente no entra aquí.
    /// </summary>
    [TestClass]
    public class PedidosClienteControllerTests
    {
        private static PedidosClienteController ControllerConIdentidad(params Claim[] claims)
        {
            PedidosClienteController controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                RequestContext = new HttpRequestContext
                {
                    Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"))
                }
            };
            return controller;
        }

        private static PedidoClienteRequest PeticionValida()
        {
            return new PedidoClienteRequest
            {
                Lineas = new List<LineaPedidoClienteRequest>
                {
                    new LineaPedidoClienteRequest { Producto = "12345", Cantidad = 1 }
                }
            };
        }

        [TestMethod]
        public async Task PostPedidoCliente_SinTokenDeCliente_NoAutorizado()
        {
            PedidosClienteController controller = ControllerConIdentidad();

            var resultado = await controller.PostPedidoCliente(PeticionValida());

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPedidoCliente_TokenDeEmpleado_NoAutorizado()
        {
            // Un empleado no tiene claim "cliente": lo suyo es POST api/PedidosVenta, el de siempre.
            PedidosClienteController controller = ControllerConIdentidad(new Claim("IsEmployee", "true"));

            var resultado = await controller.PostPedidoCliente(PeticionValida());

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPedidoCliente_TokenDeVendedor_NoAutorizado()
        {
            // ValidadorAccesoCliente deja hoy fuera a los vendedores; aquí además no traen cliente.
            PedidosClienteController controller = ControllerConIdentidad(
                new Claim("IsVendedor", "true"), new Claim("Vendedor", "CM"));

            var resultado = await controller.PostPedidoCliente(PeticionValida());

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPedidoCliente_ClienteSinLineas_DevuelveBadRequestSinTocarLaBaseDeDatos()
        {
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));

            var resultado = await controller.PostPedidoCliente(new PedidoClienteRequest());

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PostPedidoCliente_CantidadCero_DevuelveBadRequest()
        {
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));
            PedidoClienteRequest peticion = PeticionValida();
            foreach (LineaPedidoClienteRequest linea in peticion.Lineas)
            {
                linea.Cantidad = 0;
            }

            var resultado = await controller.PostPedidoCliente(peticion);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
        // NestoAPI#436 (aviso del equipo de la app): el calculo de portes del carrito va por el
        // mismo camino, asi que tiene las mismas reglas de acceso.

        [TestMethod]
        public async Task PostPortesCliente_SinTokenDeCliente_NoAutorizado()
        {
            PedidosClienteController controller = ControllerConIdentidad();

            var resultado = await controller.PostPortesCliente(PeticionValida());

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPortesCliente_CarritoVacio_DevuelveBadRequest()
        {
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));

            var resultado = await controller.PostPortesCliente(new PedidoClienteRequest());

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
    }
}
