using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
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
        [TestMethod]
        public void CrearControllerPedidos_LaPeticionLlevaSuContextoDentro_SeCableaSinReventar()
        {
            // Regresión 01/09/26: en producción el HttpRequestMessage lleva dentro su
            // HttpRequestContext (cosa que el resto de tests no tiene, porque su Request es null).
            // Asignar Request antes que RequestContext lanzaba ArgumentException ("la propiedad de
            // contexto de solicitud debe tener un valor nulo o coincidir con
            // ApiController.RequestContext") y ningún cliente podía crear un pedido.
            ClaimsPrincipal principal = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim("cliente", "15191") }, "JWT"));
            HttpConfiguration configuration = new HttpConfiguration();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/Pedidos/Cliente");
            HttpRequestContext contexto = new HttpRequestContext { Principal = principal, Configuration = configuration };
            request.SetRequestContext(contexto);
            PedidosClienteController controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                Configuration = configuration,
                RequestContext = contexto,
                Request = request
            };

            PedidosVentaController delegado = controller.CrearControllerPedidos();

            Assert.AreSame(request, delegado.Request);
            Assert.AreSame(principal, delegado.RequestContext.Principal);
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
