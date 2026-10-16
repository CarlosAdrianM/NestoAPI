using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#652: el cheque regalo en la tienda PrestaShop (módulo nestocheques). GET api/ChequesRegalo/Cliente y
    /// POST api/ChequesRegalo/ComprobarCarrito con la X-API-KEY de PrestaShop; las reglas son las del canje de
    /// api/PedidosVenta, sin duplicar.
    /// </summary>
    [TestClass]
    public class PrestashopChequesRegaloTests
    {
        private const string PRODUCTO_CHEQUE = "CHEQUE50_OCT26";
        private const string CAMPANA = "CHEQUE50_OCT_2026";
        private const string CLIENTE = "15191";
        private const string SETTING_TEST = "ApiKeyPrestashopTest652";
        private static readonly DateTime HOY = new DateTime(2026, 10, 20, 10, 0, 0);

        private IRepositorioCanjeChequesRegalo repositorio;
        private ServicioCanjeChequesRegalo servicio;
        private DateTime ahora;

        [TestInitialize]
        public void Inicializar()
        {
            ahora = HOY;
            repositorio = A.Fake<IRepositorioCanjeChequesRegalo>();
            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo> { CanjeChequesRegaloTests.Campana() });
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._)).Returns(Productos());
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque() });
            servicio = new ServicioCanjeChequesRegalo(repositorio, () => ahora);
        }

        private static ChequeRegaloCliente Cheque(string estado = "Generado", int? pedidoCanje = null, bool pedidoCanjeTieneLaLinea = false)
            => new ChequeRegaloCliente
            {
                Id = 7,
                Campana = CAMPANA,
                Empresa = "1",
                Cliente = CLIENTE,
                EmpresaFactura = "1",
                FacturaOrigen = "NV2612345",
                FechaFactura = new DateTime(2026, 10, 14),
                FechaGeneracion = new DateTime(2026, 10, 14, 18, 0, 0),
                FechaActivacion = new DateTime(2026, 10, 14, 18, 0, 0),
                Estado = estado,
                PedidoCanje = pedidoCanje,
                EmpresaCanje = pedidoCanje == null ? null : "1",
                PedidoCanjeTieneLaLinea = pedidoCanjeTieneLaLinea
            };

        private static Dictionary<string, ProductoParaChequeRegalo> Productos() => RepositorioCanjeChequesRegalo.Diccionario(new[]
        {
            new ProductoParaChequeRegalo { Numero = PRODUCTO_CHEQUE, Nombre = "CHEQUE REGALO 50 € (OCT 2026)", Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "37800", Nombre = "CREMA FACIAL", Grupo = "COS", IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "PACK", Nombre = "PACK 26 NAVIDAD AINHOA", Grupo = "COS", IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "PELU", Nombre = "TINTE", Grupo = "PEL", IvaRepercutido = "G21" }
        });

        private static ComprobarCarritoChequeRegaloDTO Carrito(params (string producto, decimal baseImponible)[] lineas) => new ComprobarCarritoChequeRegaloDTO
        {
            Empresa = "1",
            Cliente = CLIENTE,
            Lineas = lineas.Select(l => new LineaCarritoChequeRegaloDTO { Producto = l.producto, Cantidad = 1, BaseImponible = l.baseImponible }).ToList()
        };

        private Task<ComprobacionCarritoChequeRegaloDTO> Comprobar(ComprobarCarritoChequeRegaloDTO carrito)
            => new CarritoChequesRegalo(servicio).ComprobarCarritoTienda(carrito, ahora);

        // ===== ComprobarCarrito: las reglas del canje =====

        [TestMethod]
        public async Task ComprobarCarrito_Aplicable_SinCodigoNiFalta_YNoReservaNada()
        {
            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(Carrito(("37800", 250.01M)));

            Assert.IsTrue(r.Aplicable, r.Mensaje);
            Assert.AreEqual(50M, r.Importe);
            Assert.IsNull(r.Codigo);
            Assert.IsNull(r.Falta);
            Assert.AreEqual("Puede usar su cheque regalo de 50,00 € en este pedido.", r.Mensaje);
            A.CallTo(() => repositorio.ReservarCheque(A<int>._, A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LiberarChequesDelPedido(A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ComprobarCarrito_ClienteSinCheque()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._)).Returns(new List<ChequeRegaloCliente>());

            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(Carrito(("37800", 400M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_SIN_CHEQUE, r.Codigo);
            Assert.AreEqual(0M, r.Importe);
            Assert.AreEqual("No tiene ningún cheque regalo para usar en este pedido.", r.Mensaje);
        }

        [TestMethod]
        public async Task ComprobarCarrito_ClienteGenerico_SinCheque()
        {
            ComprobarCarritoChequeRegaloDTO carrito = Carrito(("37800", 400M));
            carrito.Cliente = "31517";

            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(carrito);

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_SIN_CHEQUE, r.Codigo);
        }

        [TestMethod]
        public async Task ComprobarCarrito_ChequeYaUsadoEnOtroPedido()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(estado: "Canjeado", pedidoCanje: 929999, pedidoCanjeTieneLaLinea: true) });

            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(Carrito(("37800", 400M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_YA_USADO, r.Codigo);
            Assert.AreEqual(50M, r.Importe);
            Assert.AreEqual("Su cheque regalo ya está aplicado en el pedido 929999: solo se puede usar una vez.", r.Mensaje);
        }

        [TestMethod]
        public async Task ComprobarCarrito_FueraDePlazo()
        {
            ahora = new DateTime(2026, 11, 8, 9, 0, 0);

            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(Carrito(("37800", 400M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_FUERA_DE_PLAZO, r.Codigo);
            Assert.AreEqual("El plazo para usar su cheque regalo ya ha terminado.", r.Mensaje);
        }

        [TestMethod]
        public async Task ComprobarCarrito_MinimoNoSuperado_DiceLoQueFalta()
        {
            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(Carrito(("37800", 212.60M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO, r.Codigo);
            Assert.AreEqual(37.41M, r.Falta);
            StringAssert.StartsWith(r.Mensaje, "Le faltan 37,41 € de producto para poder usar su cheque regalo.");
            StringAssert.Contains(r.Mensaje, "superar 250,00 €");
        }

        [TestMethod]
        public async Task ComprobarCarrito_JustoElMinimo_NoVale_HayQueSuperarlo()
        {
            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(Carrito(("37800", 250M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(0.01M, r.Falta);
        }

        [TestMethod]
        public async Task ComprobarCarrito_ProductosExcluidos_NoComputan()
        {
            // PACK 26 (prefijo) y grupo PEL van en el pedido, pero no suman para el mínimo; un producto que no existe en
            // Nesto tampoco suma
            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(Carrito(("37800", 200M), ("PACK", 300M), ("PELU", 300M), ("NOEXISTE", 300M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO, r.Codigo);
            Assert.AreEqual(50.01M, r.Falta);
        }

        [TestMethod]
        public async Task ComprobarCarrito_SiLaTiendaMandaElPropioCheque_NoCuentaDosVeces()
        {
            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(Carrito(("37800", 300M), (PRODUCTO_CHEQUE, -50M)));

            Assert.IsTrue(r.Aplicable, r.Mensaje);
        }

        [TestMethod]
        public async Task ComprobarCarrito_CampanaApagada_SinCheque()
        {
            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo> { CanjeChequesRegaloTests.Campana(activa: false) });

            ComprobacionCarritoChequeRegaloDTO r = await Comprobar(Carrito(("37800", 400M)));

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_SIN_CHEQUE, r.Codigo);
        }

        // ===== El controller =====

        private ChequesRegaloController Controlador(ClaimsIdentity identidad = null)
        {
            var controller = new ChequesRegaloController(A.Fake<NVEntities>()) { ServicioCanje = servicio, Ahora = () => ahora };
            if (identidad != null)
            {
                controller.RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(identidad) };
            }
            return controller;
        }

        [TestMethod]
        public async Task ControllerComprobarCarrito_ConApiKey_200ConElResultado()
        {
            var ok = await Controlador().ComprobarCarrito(Carrito(("37800", 300M))) as OkNegotiatedContentResult<ComprobacionCarritoChequeRegaloDTO>;

            Assert.IsNotNull(ok);
            Assert.IsTrue(ok.Content.Aplicable);
        }

        [TestMethod]
        public async Task ControllerComprobarCarrito_SinCliente_400()
        {
            Assert.IsInstanceOfType(await Controlador().ComprobarCarrito(null), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await Controlador().ComprobarCarrito(new ComprobarCarritoChequeRegaloDTO { Empresa = "1" }), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task ControllerComprobarCarrito_UnClienteDeLaTiendaConJwt_SoloElSuyo()
        {
            var resultado = await Controlador(new ClaimsIdentity(new[] { new Claim("cliente", "20000") }, "JWT"))
                .ComprobarCarrito(Carrito(("37800", 300M))) as StatusCodeResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        // ===== La credencial =====

        [TestMethod]
        [DataRow(nameof(ChequesRegaloController.GetChequeCliente))]
        [DataRow(nameof(ChequesRegaloController.ComprobarCarrito))]
        public void Endpoints_AdmitenJwtOApiKeyDePrestashop_SinModoSombra(string metodo)
        {
            MethodInfo info = typeof(ChequesRegaloController).GetMethod(metodo);
            AutorizadoOApiKeyAttribute atributo = info.GetCustomAttributes<AutorizadoOApiKeyAttribute>(inherit: false).FirstOrDefault();

            Assert.IsNotNull(atributo, $"{metodo} tiene que exigir JWT o la API key de PrestaShop");
            Assert.AreEqual("ApiKeyPrestashop", atributo.NombreSetting);
            Assert.AreEqual("X-API-KEY", atributo.Cabecera);
            Assert.IsTrue(atributo.ExigirSiempre, "Sin ExigirSiempre, con el interruptor de #459 apagado quedaría abierto a internet");
            Assert.IsFalse(info.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Any(),
                "[Authorize] junto al atributo exigiría las dos credenciales y la tienda (solo API key) no pasaría");
        }

        [TestMethod]
        public void ApiKeyMala_401_AunqueElInterruptorDe459EsteApagado()
        {
            Assert.IsFalse(AutorizadoOApiKeyAttribute.Exigir, "Premisa: el modo sombra de #459 sigue apagado");
            ConfigurationManager.AppSettings[SETTING_TEST] = "la-clave";
            HttpActionContext contexto = Contexto("otra-clave");

            Atributo().OnAuthorization(contexto);

            Assert.IsNotNull(contexto.Response);
            Assert.AreEqual(HttpStatusCode.Unauthorized, contexto.Response.StatusCode);
        }

        [TestMethod]
        public void SinCredencial_401()
        {
            ConfigurationManager.AppSettings[SETTING_TEST] = "la-clave";
            HttpActionContext contexto = Contexto(null);

            Atributo().OnAuthorization(contexto);

            Assert.IsNotNull(contexto.Response);
            Assert.AreEqual(HttpStatusCode.Unauthorized, contexto.Response.StatusCode);
        }

        [TestMethod]
        public void ApiKeyBuena_Pasa()
        {
            ConfigurationManager.AppSettings[SETTING_TEST] = "la-clave";
            HttpActionContext contexto = Contexto("la-clave");

            Atributo().OnAuthorization(contexto);

            Assert.IsNull(contexto.Response);
        }

        [TestMethod]
        public void SinExigirSiempre_ElModoSombraDe459DejaPasar()
        {
            // Lo de siempre para plazos y formas de pago: no se ha tocado
            ConfigurationManager.AppSettings[SETTING_TEST] = "la-clave";
            HttpActionContext contexto = Contexto(null);

            new AutorizadoOApiKeyAttribute(SETTING_TEST, "X-API-KEY").OnAuthorization(contexto);

            Assert.IsNull(contexto.Response);
        }

        private static AutorizadoOApiKeyAttribute Atributo() => new AutorizadoOApiKeyAttribute(SETTING_TEST, "X-API-KEY") { ExigirSiempre = true };

        private static HttpActionContext Contexto(string clave)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.nuevavision.es/api/ChequesRegalo/ComprobarCarrito");
            request.SetConfiguration(new HttpConfiguration());
            if (clave != null)
            {
                request.Headers.Add("X-API-KEY", clave);
            }
            return new HttpActionContext
            {
                ControllerContext = new HttpControllerContext { Request = request }
            };
        }
    }
}
