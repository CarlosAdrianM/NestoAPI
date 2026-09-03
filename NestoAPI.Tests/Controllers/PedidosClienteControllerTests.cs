using System.Collections.Generic;
using NestoAPI.Infraestructure.Clientes;
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
using NestoAPI.Models.PedidosBase;
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
        #region NestoAPI#452: el cobro directo (MIT) cobraba la base imponible, sin IVA ni portes

        [TestMethod]
        public void RellenarPorcentajesIva_AntesDeCobrar_ElTotalDejaDeSerLaBasePelada()
        {
            // 03/09/26 en producción: el cobro directo pidió 0,75 EUR (la base) y el pedido valía
            // 0,91 EUR. El DTO recién construido solo trae el CÓDIGO de IVA ("G21"); el porcentaje
            // se rellenaba dentro de PostPedidoVenta, que va DESPUÉS del cobro.
            PedidoVentaDTO pedido = PedidoDeLaApp(precio: 0.75M, codigoIva: "G21");

            Assert.AreEqual(0.75M, pedido.Total, "sin porcentaje de IVA, el total es la base: el fallo");

            PedidosClienteController.RellenarPorcentajesIva(pedido);

            Assert.AreEqual(0.91M, pedido.Total, "con el 21 % ya es el importe que hay que cobrar");
            Assert.AreEqual(0.75M, pedido.BaseImponible, "la base no cambia");
        }

        [TestMethod]
        public void RellenarPorcentajesIva_SinParametros_NoRevienta()
        {
            PedidoVentaDTO pedido = PedidoDeLaApp(0.75M, "G21");
            pedido.ParametrosIva = new List<ParametrosIvaBase>();

            PedidosClienteController.RellenarPorcentajesIva(pedido);

            Assert.AreEqual(0.75M, pedido.Total);
        }

        [TestMethod]
        public void DiferenciaCobroPedido_LoCobradoYElPedidoCoinciden_NoAvisa()
        {
            Assert.IsNull(PedidosClienteController.DiferenciaCobroPedido(0.91M, 0.91M, 925347, "F1CC0DC15191"));
        }

        [TestMethod]
        public void DiferenciaCobroPedido_SeCobraDeMenos_AvisaConLosDosImportes()
        {
            string aviso = PedidosClienteController.DiferenciaCobroPedido(0.75M, 0.91M, 925347, "418F0BC15191");

            Assert.IsNotNull(aviso);
            StringAssert.Contains(aviso, "de MENOS");
            StringAssert.Contains(aviso, "0,75");
            StringAssert.Contains(aviso, "0,91");
            StringAssert.Contains(aviso, "925347");
            StringAssert.Contains(aviso, "418F0BC15191");
        }

        [TestMethod]
        public void DiferenciaCobroPedido_SeCobraDeMas_TambienAvisa()
        {
            string aviso = PedidosClienteController.DiferenciaCobroPedido(10M, 9M, 925347, "X");

            Assert.IsNotNull(aviso);
            StringAssert.Contains(aviso, "de MÁS");
        }

        private static PedidoVentaDTO PedidoDeLaApp(decimal precio, string codigoIva)
        {
            return new PedidoVentaDTO
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                cliente = "15191",
                iva = "G",
                ParametrosIva = new List<ParametrosIvaBase>
                {
                    new ParametrosIvaBase
                    {
                        CodigoIvaProducto = codigoIva,
                        PorcentajeIvaProducto = 0.21M,
                        PorcentajeRecargoEquivalencia = 0M
                    }
                },
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                        Producto = "12345",
                        Cantidad = 1,
                        PrecioUnitario = precio,
                        iva = codigoIva
                    }
                }
            };
        }

        #endregion

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
        public async Task PostPortesCliente_UsuarioSinPrecios_NoCalculaPortes()
        {
            // NestoAPI#446: "te faltan X € para el envío gratis" es un importe
            foreach (string nivel in new[] { "SinPrecios", "SinDescuentos" })
            {
                PedidosClienteController controller = ControllerConIdentidad(
                    new Claim("cliente", "15191"), new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, nivel));

                var resultado = await controller.PostPortesCliente(PeticionValida());

                var badRequest = resultado as BadRequestErrorMessageResult;
                Assert.IsNotNull(badRequest, nivel);
                Assert.AreEqual(PoliticaPreciosOcultos.MOTIVO_PORTES, badRequest.Message);
            }
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
