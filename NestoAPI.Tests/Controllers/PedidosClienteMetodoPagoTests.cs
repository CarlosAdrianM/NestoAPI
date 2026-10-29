using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#728 (TiendasNuevaVision#97): los cobros del pedido y del carrito de la app aceptan un MetodoPago opcional
    /// (C, xpay, google, z). Con Apple Pay / Google Pay / Bizum no hay tarjeta guardada ni se tokeniza, y la app abre
    /// Pago.UrlPaginaPago en el navegador del sistema. Sin MetodoPago, todo como antes.
    /// </summary>
    [TestClass]
    public class PedidosClienteMetodoPagoTests
    {
        private static PedidoClienteRequest Peticion(string metodo, int? tarjetaId = null) => new PedidoClienteRequest
        {
            Lineas = new List<LineaPedidoClienteRequest> { new LineaPedidoClienteRequest { Producto = "12345", Cantidad = 1 } },
            MetodoPago = metodo,
            TarjetaId = tarjetaId
        };

        // ---------------- Validar ----------------

        [TestMethod]
        public void Validar_SinMetodo_ComoSiempre()
        {
            Assert.IsNull(MetodoPagoApp.Validar(Peticion(null, tarjetaId: 7), out string metodo));
            Assert.IsNull(metodo);
            Assert.IsNull(MetodoPagoApp.Validar(Peticion("  "), out metodo));
            Assert.IsNull(metodo);
            Assert.IsTrue(MetodoPagoApp.UsaTarjetaGuardada(null));
        }

        [TestMethod]
        public void Validar_ApplePayYGooglePay_Normalizados()
        {
            Assert.IsNull(MetodoPagoApp.Validar(Peticion("XPAY"), out string metodo));
            Assert.AreEqual(MetodosPagoRedsys.APPLE_PAY, metodo);
            Assert.IsNull(MetodoPagoApp.Validar(Peticion("google"), out metodo));
            Assert.AreEqual(MetodosPagoRedsys.GOOGLE_PAY, metodo);
            Assert.IsFalse(MetodoPagoApp.UsaTarjetaGuardada(metodo));
        }

        [TestMethod]
        public void Validar_TarjetaConTarjetaGuardada_Vale()
        {
            Assert.IsNull(MetodoPagoApp.Validar(Peticion("C", tarjetaId: 7), out string metodo));
            Assert.AreEqual("C", metodo);
            Assert.IsTrue(MetodoPagoApp.UsaTarjetaGuardada(metodo));
        }

        [TestMethod]
        public void Validar_MetodoQueNoExiste_400DeUsted()
        {
            string motivo = MetodoPagoApp.Validar(Peticion("bitcoin"), out string metodo);

            Assert.IsNull(metodo);
            Assert.AreEqual("El método de pago «bitcoin» no es válido. Use C (tarjeta), z (Bizum), xpay (Apple Pay) o google (Google Pay), " +
                "o no lo indique para pagar como siempre.", motivo);
        }

        [TestMethod]
        public void Validar_WalletConTarjetaGuardada_400()
        {
            Assert.AreEqual(MetodoPagoApp.MENSAJE_SIN_TARJETA_GUARDADA, MetodoPagoApp.Validar(Peticion("google", tarjetaId: 7), out _));
            PedidoClienteRequest conGuardada = Peticion("xpay");
            conGuardada.PagarConTarjetaGuardada = true;
            Assert.AreEqual(MetodoPagoApp.MENSAJE_SIN_TARJETA_GUARDADA, MetodoPagoApp.Validar(conGuardada, out _));
        }

        // ---------------- Lo que se manda a IniciarPago ----------------

        private static readonly TarjetaCliente TARJETA = new TarjetaCliente { Id = 7, TokenRedsys = "tok", UltimosDigitos = "4242" };

        [TestMethod]
        public void CobroPedido_ConGooglePay_SinTarjetaGuardadaYConElMetodo()
        {
            var respuesta = new PedidoClienteResponse { Empresa = "1", Numero = 930001, Cliente = "15191", Contacto = "0", Total = 121m };

            SolicitudPagoTPV solicitud = PedidosClienteController.SolicitudCobroPedido(respuesta, "luna@example.com", TARJETA, MetodosPagoRedsys.GOOGLE_PAY);

            Assert.AreEqual(MetodosPagoRedsys.GOOGLE_PAY, solicitud.MetodoPago);
            Assert.IsNull(solicitud.TarjetaGuardada, "Con wallet no hay tarjeta guardada");
            Assert.AreEqual(930001, solicitud.Pedido);
            Assert.AreEqual(121m, solicitud.Importe);
            Assert.IsFalse(MetodosPagoRedsys.PermiteTokenizar(solicitud.MetodoPago), "IniciarPago no pide tokenizar con wallet");
        }

        [TestMethod]
        public void CobroPedido_SinMetodo_ComoAntes()
        {
            var respuesta = new PedidoClienteResponse { Empresa = "1", Numero = 930001, Cliente = "15191", Contacto = "0", Total = 121m };

            SolicitudPagoTPV solicitud = PedidosClienteController.SolicitudCobroPedido(respuesta, "luna@example.com", TARJETA, null);

            Assert.IsNull(solicitud.MetodoPago);
            Assert.AreSame(TARJETA, solicitud.TarjetaGuardada);
            Assert.AreEqual("Pago pedido 930001", solicitud.Descripcion);
        }

        [TestMethod]
        public void CobroCarrito_ConApplePay_EsDelCarritoSinTarjeta()
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "15191", contacto = "0" };

            SolicitudPagoTPV solicitud = PedidosClienteController.SolicitudCobroCarrito(pedido, 55.5m, "luna@example.com", null, MetodosPagoRedsys.APPLE_PAY);

            Assert.IsTrue(solicitud.EsCarritoApp);
            Assert.IsNull(solicitud.Pedido);
            Assert.AreEqual(MetodosPagoRedsys.APPLE_PAY, solicitud.MetodoPago);
            Assert.IsNull(solicitud.TarjetaGuardada);
            Assert.AreEqual(55.5m, solicitud.Importe);
        }

        // ---------------- Controlador ----------------

        private static PedidosClienteController Controller(params Claim[] claims) =>
            new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT")) }
            };

        [TestMethod]
        public async Task PostPagoCarrito_MetodoNoValido_400SinCobrar()
        {
            var resultado = await Controller(new Claim("cliente", "15191")).PostPagoCarrito(Peticion("paypal", tarjetaId: 7));

            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "no es válido");
        }

        [TestMethod]
        public async Task PostPagoCarrito_GooglePaySinTarjeta_NoPideTarjeta()
        {
            // Sin claim de cliente: llega hasta preparar el carrito (401) en vez de quedarse en «Falta la tarjeta»
            var resultado = await Controller().PostPagoCarrito(Peticion("google"));

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPagoCarrito_SinMetodoNiTarjeta_ComoAntes()
        {
            var resultado = await Controller(new Claim("cliente", "15191")).PostPagoCarrito(Peticion(null));

            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "TarjetaId");
        }

        [TestMethod]
        public async Task PostPagoCarrito_ApplePayConTarjetaGuardada_400()
        {
            var resultado = await Controller(new Claim("cliente", "15191")).PostPagoCarrito(Peticion("xpay", tarjetaId: 7));

            Assert.AreEqual(MetodoPagoApp.MENSAJE_SIN_TARJETA_GUARDADA, ((BadRequestErrorMessageResult)resultado).Message);
        }

        [TestMethod]
        public async Task PostPedidoCliente_MetodoNoValido_400()
        {
            var resultado = await Controller(new Claim("cliente", "15191")).PostPedidoCliente(Peticion("transferencia"));

            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "no es válido");
        }

        [TestMethod]
        public async Task PostPedidoCliente_ConCobroDelCarritoYaHecho_ElMetodoNoSeMira()
        {
            // El cobro ya está hecho: lo que diga MetodoPago da igual (se sigue por el camino de siempre, aquí hasta el 401)
            PedidoClienteRequest peticion = Peticion("loquesea");
            peticion.IdPagoCarrito = 689;

            var resultado = await Controller().PostPedidoCliente(peticion);

            Assert.IsNotInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
    }
}
