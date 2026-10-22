using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Prestashop;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Tests.Infrastructure.Cupones
{
    /// <summary>
    /// NestoAPI#614 (cupones, corte 4): el espejo en PrestaShop (mapeo del cart_rule, cliente ↔ customer por correo sin correo o con
    /// varios, reintentos y aviso, desactivar antes de confirmar el pedido), el uso desde la tienda (carrera Nesto/PrestaShop) y el
    /// cliente del webservice.
    /// </summary>
    [TestClass]
    public class EspejoCuponesPrestashopTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 2, 10, 0, 0);
        private const int PEDIDO = 930500;

        private IRepositorioEspejoCupones repositorio;
        private IClienteWebservicePrestashop tienda;
        private IServicioCorreoElectronico correo;
        private bool encendido;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioEspejoCupones>();
            tienda = A.Fake<IClienteWebservicePrestashop>();
            correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            encendido = true;
            A.CallTo(() => repositorio.LeerCorreosCliente(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new List<string> { "luna@example.com" }));
            A.CallTo(() => tienda.Leer(A<string>.That.StartsWith("customers?")))
                .Returns(Task.FromResult(Customers((5, "luna@example.com", true, false))));
            A.CallTo(() => tienda.Leer(A<string>.That.StartsWith("cart_rules?")))
                .Returns(Task.FromResult("<prestashop><cart_rules></cart_rules></prestashop>"));
            A.CallTo(() => tienda.Crear("cart_rules", A<string>._))
                .Returns(Task.FromResult("<prestashop><cart_rule><id><![CDATA[812]]></id></cart_rule></prestashop>"));
            A.CallTo(() => tienda.Modificar(A<string>._, A<string>._)).Returns(Task.FromResult("<prestashop/>"));
        }

        private ServicioEspejoCuponesPrestashop Servicio(DateTime? ahora = null) =>
            new ServicioEspejoCuponesPrestashop(repositorio, tienda, () => encendido, () => ahora ?? HOY, () => correo);

        private static CuponEspejo Cupon(string estado = EstadosCupon.ACTIVO, string espejo = null, int? idCartRule = null, int? idCustomer = null,
            DateTime? caducidad = null, DateTime? fechaEspejo = null, int intentos = 0) => new CuponEspejo
            {
                Id = 7,
                Codigo = "EV-2610-7K3Q",
                Empresa = "1",
                Cliente = "15191",
                Contacto = "0",
                Importe = 50,
                Origen = OrigenesCupon.EVENTO,
                Evento = "Masterclass Cloasma",
                ValidoDesde = new DateTime(2026, 10, 30),
                Caducidad = caducidad ?? new DateTime(2027, 4, 30),
                Estado = estado,
                UsadoEnPedido = estado == EstadosCupon.USADO ? PEDIDO : (int?)null,
                PedidoUsoTieneLaLinea = estado == EstadosCupon.USADO,
                EstadoEspejo = espejo,
                IdCartRulePrestashop = idCartRule,
                IdCustomerPrestashop = idCustomer,
                FechaEspejo = fechaEspejo,
                EspejoIntentos = intentos
            };

        private static string Customers(params (int id, string email, bool activo, bool invitado)[] cuentas) =>
            new XDocument(new XElement("prestashop", new XElement("customers", cuentas.Select(c => new XElement("customer",
                new XElement("id", c.id), new XElement("email", c.email), new XElement("active", c.activo ? 1 : 0),
                new XElement("is_guest", c.invitado ? 1 : 0), new XElement("deleted", 0)))))).ToString();

        private static XElement Regla(string xml) => XDocument.Parse(xml).Root.Element("cart_rule");

        // ---------------- Mapeo del cart_rule ----------------

        [TestMethod]
        public void XmlCartRule_MapeaLosCamposDelCupon()
        {
            XElement r = Regla(ReglasEspejoPrestashop.XmlCartRule(Cupon(), 5, true, null, 1, 1));

            Assert.IsNull(r.Element("id"), "El POST no lleva id");
            Assert.AreEqual("EV-2610-7K3Q", r.Element("code").Value);
            Assert.AreEqual("5", r.Element("id_customer").Value);
            Assert.AreEqual("50.00", r.Element("reduction_amount").Value, "Importe IVA incluido, con punto");
            Assert.AreEqual("1", r.Element("reduction_tax").Value);
            Assert.AreEqual("100.00", r.Element("minimum_amount").Value, "Pedido mínimo: el doble");
            Assert.AreEqual("1", r.Element("minimum_amount_tax").Value);
            Assert.AreEqual("0", r.Element("minimum_amount_shipping").Value, "Sin portes");
            Assert.AreEqual("2026-10-30 00:00:00", r.Element("date_from").Value, "Desde el día del evento");
            Assert.AreEqual("2027-04-30 23:59:59", r.Element("date_to").Value, "Hasta el último día de la caducidad");
            Assert.AreEqual("1", r.Element("quantity").Value);
            Assert.AreEqual("1", r.Element("quantity_per_user").Value);
            Assert.AreEqual("0", r.Element("partial_use").Value, "Sin uso parcial: no deja otro vale con lo que sobre");
            Assert.AreEqual("0", r.Element("cart_rule_restriction").Value, "Convive con el cheque regalo");
            Assert.AreEqual("0", r.Element("free_shipping").Value);
            Assert.AreEqual("1", r.Element("active").Value);
            Assert.AreEqual("Cupón EV-2610-7K3Q (Masterclass Cloasma)", r.Element("name").Element("language").Value);
            Assert.AreEqual("1", r.Element("name").Element("language").Attribute("id").Value);
        }

        [TestMethod]
        public void XmlCartRule_Desactivado_ActiveYQuantityACeroYConId()
        {
            XElement r = Regla(ReglasEspejoPrestashop.XmlCartRule(Cupon(EstadosCupon.USADO), 5, false, 812, 1, 1));

            Assert.AreEqual("812", r.Element("id").Value);
            Assert.AreEqual("0", r.Element("active").Value);
            Assert.AreEqual("0", r.Element("quantity").Value);
        }

        [TestMethod]
        public void XmlCartRule_ImporteConDecimales_SinComaDeLaCultura()
        {
            CuponEspejo c = Cupon();
            c.Importe = 37.5M;
            XElement r = Regla(ReglasEspejoPrestashop.XmlCartRule(c, 5, true, null, 1, 1));
            Assert.AreEqual("37.50", r.Element("reduction_amount").Value);
            Assert.AreEqual("75.00", r.Element("minimum_amount").Value);
        }

        [TestMethod]
        public void LeerId_DelCartRuleCreadoYDeUnaLista()
        {
            Assert.AreEqual(812, ReglasEspejoPrestashop.LeerId("<prestashop><cart_rule><id><![CDATA[812]]></id></cart_rule></prestashop>", "cart_rule"));
            Assert.AreEqual(9, ReglasEspejoPrestashop.LeerId("<prestashop><cart_rules><cart_rule id=\"9\" /></cart_rules></prestashop>", "cart_rule"));
            Assert.IsNull(ReglasEspejoPrestashop.LeerId("<prestashop><cart_rules></cart_rules></prestashop>", "cart_rule"));
        }

        [TestMethod]
        public void Filtro_EscapaElCorreo()
        {
            Assert.AreEqual("filter[email]=[luna%2Bpedidos%40example.com]", ReglasEspejoPrestashop.Filtro("email", "luna+pedidos@example.com"));
        }

        // ---------------- Cliente ↔ customer ----------------

        [TestMethod]
        public void ElegirCustomer_SinCorreos_Null()
        {
            Assert.IsNull(ReglasEspejoPrestashop.ElegirCustomer(new List<KeyValuePair<string, List<CustomerPrestashop>>>()));
        }

        [TestMethod]
        public void ElegirCustomer_InvitadoOInactivo_NoValen()
        {
            var porCorreo = new List<KeyValuePair<string, List<CustomerPrestashop>>>
            {
                new KeyValuePair<string, List<CustomerPrestashop>>("a@example.com", ReglasEspejoPrestashop.LeerCustomers(Customers((3, "a@example.com", true, true), (4, "a@example.com", false, false))))
            };
            Assert.IsNull(ReglasEspejoPrestashop.ElegirCustomer(porCorreo));
        }

        [TestMethod]
        public void ElegirCustomer_VariasCuentas_ElPrimerCorreoYLaMasReciente()
        {
            var porCorreo = new List<KeyValuePair<string, List<CustomerPrestashop>>>
            {
                new KeyValuePair<string, List<CustomerPrestashop>>("contacto@example.com", ReglasEspejoPrestashop.LeerCustomers(Customers((10, "contacto@example.com", true, false), (25, "contacto@example.com", true, false)))),
                new KeyValuePair<string, List<CustomerPrestashop>>("facturas@example.com", ReglasEspejoPrestashop.LeerCustomers(Customers((99, "facturas@example.com", true, false))))
            };
            Assert.AreEqual(25, ReglasEspejoPrestashop.ElegirCustomer(porCorreo).Id);
        }

        [TestMethod]
        public void OrdenarCorreos_DelContactoPrimeroLuegoFacturasSinRepetirNiVacios()
        {
            var personas = new List<KeyValuePair<string, KeyValuePair<short?, string>>>
            {
                new KeyValuePair<string, KeyValuePair<short?, string>>("1  ", new KeyValuePair<short?, string>(1, "otro@example.com")),
                new KeyValuePair<string, KeyValuePair<short?, string>>("1  ", new KeyValuePair<short?, string>(22, "facturas@example.com ")),
                new KeyValuePair<string, KeyValuePair<short?, string>>("0  ", new KeyValuePair<short?, string>(1, "contacto@example.com")),
                new KeyValuePair<string, KeyValuePair<short?, string>>("0  ", new KeyValuePair<short?, string>(22, "CONTACTO@example.com")),
                new KeyValuePair<string, KeyValuePair<short?, string>>("2  ", new KeyValuePair<short?, string>(1, "   ")),
                new KeyValuePair<string, KeyValuePair<short?, string>>("2  ", new KeyValuePair<short?, string>(1, "sin arroba"))
            };
            // Del contacto 0, el de facturas primero; el mismo correo en mayúsculas no se repite
            CollectionAssert.AreEqual(new[] { "CONTACTO@example.com", "facturas@example.com", "otro@example.com" },
                RepositorioEspejoCupones.OrdenarCorreos(personas, "0"));
        }

        [TestMethod]
        public async Task Espejar_ClienteSinCorreo_SinClienteYNoLlamaALaTienda()
        {
            A.CallTo(() => repositorio.LeerCorreosCliente(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(new List<string>()));

            ResultadoEspejo r = await Servicio().Espejar(Cupon());

            Assert.IsTrue(r.Correcto);
            Assert.AreEqual(EstadosEspejo.SIN_CLIENTE, r.EstadoEspejo);
            StringAssert.Contains(r.Error, "no tiene cuenta en la tienda");
            A.CallTo(() => tienda.Crear(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Espejar_VariosCorreos_ParaEnElPrimeroConCuenta()
        {
            A.CallTo(() => repositorio.LeerCorreosCliente(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new List<string> { "nuevo@example.com", "luna@example.com", "tercero@example.com" }));
            A.CallTo(() => tienda.Leer(A<string>.That.Contains("nuevo%40example.com"))).Returns(Task.FromResult(Customers()));
            A.CallTo(() => tienda.Leer(A<string>.That.Contains("luna%40example.com"))).Returns(Task.FromResult(Customers((5, "luna@example.com", true, false))));

            ResultadoEspejo r = await Servicio().Espejar(Cupon());

            Assert.AreEqual(5, r.IdCustomer);
            A.CallTo(() => tienda.Leer(A<string>.That.Contains("tercero"))).MustNotHaveHappened();
        }

        // ---------------- Crear, reactivar, desactivar ----------------

        [TestMethod]
        public async Task Espejar_Nuevo_CreaElCartRuleYGuardaElId()
        {
            ResultadoEspejo r = await Servicio().Espejar(Cupon());

            Assert.IsTrue(r.Correcto);
            Assert.AreEqual(EstadosEspejo.ACTIVO, r.EstadoEspejo);
            Assert.AreEqual(812, r.IdCartRule);
            Assert.AreEqual(5, r.IdCustomer);
            A.CallTo(() => tienda.Crear("cart_rules", A<string>.That.Contains("<code>EV-2610-7K3Q</code>"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Espejar_ElCodigoYaEstaEnLaTienda_LoReutilizaSinDuplicar()
        {
            // Un POST anterior llegó pero no se apuntó (se cayó la respuesta)
            A.CallTo(() => tienda.Leer(A<string>.That.StartsWith("cart_rules?filter[code]=[EV-2610-7K3Q]")))
                .Returns(Task.FromResult("<prestashop><cart_rules><cart_rule id=\"640\"/></cart_rules></prestashop>"));

            ResultadoEspejo r = await Servicio().Espejar(Cupon());

            Assert.AreEqual(640, r.IdCartRule);
            A.CallTo(() => tienda.Crear(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tienda.Modificar("cart_rules/640", A<string>.That.Contains("<active>1</active>"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Espejar_Usado_LoDesactiva()
        {
            ResultadoEspejo r = await Servicio().Espejar(Cupon(EstadosCupon.USADO, EstadosEspejo.ACTIVO, 812, 5));

            Assert.AreEqual(EstadosEspejo.INACTIVO, r.EstadoEspejo);
            A.CallTo(() => tienda.Modificar("cart_rules/812", A<string>.That.Contains("<active>0</active>"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Espejar_LiberadoTrasUsarse_LoReactivaConElMismoId()
        {
            ResultadoEspejo r = await Servicio().Espejar(Cupon(EstadosCupon.ACTIVO, EstadosEspejo.INACTIVO, 812, 5));

            Assert.AreEqual(EstadosEspejo.ACTIVO, r.EstadoEspejo);
            A.CallTo(() => tienda.Modificar("cart_rules/812", A<string>.That.Contains("<active>1</active>"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => tienda.Leer(A<string>.That.StartsWith("customers?"))).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Espejar_FallaLaTienda_NoLanzaYDevuelveElError()
        {
            A.CallTo(() => tienda.Crear(A<string>._, A<string>._)).ThrowsAsync(new PrestashopWebserviceException("La tienda PrestaShop ha respondido 401.", HttpStatusCode.Unauthorized));

            ResultadoEspejo r = await Servicio().Espejar(Cupon());

            Assert.IsFalse(r.Correcto);
            StringAssert.Contains(r.Error, "401");
            Assert.AreEqual(5, r.IdCustomer, "Lo ya encontrado se guarda para el siguiente intento");
        }

        [TestMethod]
        public void HayQueSincronizar_Casos()
        {
            Assert.IsTrue(ReglasEspejoPrestashop.HayQueSincronizar(Cupon(), HOY), "Activo nunca llevado");
            Assert.IsTrue(ReglasEspejoPrestashop.HayQueSincronizar(Cupon(EstadosCupon.EMITIDO), HOY), "Emitido: va ya, con date_from el día del evento");
            Assert.IsFalse(ReglasEspejoPrestashop.HayQueSincronizar(Cupon(espejo: EstadosEspejo.ACTIVO), HOY), "Ya está");
            Assert.IsTrue(ReglasEspejoPrestashop.HayQueSincronizar(Cupon(EstadosCupon.ANULADO, EstadosEspejo.ACTIVO, 812), HOY), "Anulado activo allí");
            Assert.IsFalse(ReglasEspejoPrestashop.HayQueSincronizar(Cupon(EstadosCupon.USADO), HOY), "Usado nunca llevado: no se crea");
            Assert.IsTrue(ReglasEspejoPrestashop.HayQueSincronizar(Cupon(espejo: EstadosEspejo.ACTIVO, caducidad: new DateTime(2026, 11, 1)), HOY), "Caducado hoy aunque el job no haya pasado");
            Assert.IsFalse(ReglasEspejoPrestashop.HayQueSincronizar(Cupon(espejo: EstadosEspejo.SIN_CLIENTE, fechaEspejo: HOY.AddHours(-3)), HOY), "Sin cliente: una vez al día");
            Assert.IsTrue(ReglasEspejoPrestashop.HayQueSincronizar(Cupon(espejo: EstadosEspejo.SIN_CLIENTE, fechaEspejo: HOY.AddHours(-25)), HOY));
            CuponEspejo libre = Cupon(EstadosCupon.USADO, EstadosEspejo.INACTIVO, 812);
            libre.PedidoUsoTieneLaLinea = false;
            Assert.IsTrue(ReglasEspejoPrestashop.HayQueSincronizar(libre, HOY), "Usado en un pedido que ya no tiene la línea: vuelve a valer");
        }

        // ---------------- Job: reintentos y aviso ----------------

        [TestMethod]
        public async Task ProcesarPendientes_Apagado_NiLee()
        {
            encendido = false;
            Assert.AreEqual(0, await Servicio().ProcesarPendientes(4));
            A.CallTo(() => repositorio.LeerPendientes(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ProcesarPendientes_FallaYLuegoVa_ReintentaEnLaSiguientePasada()
        {
            A.CallTo(() => repositorio.LeerPendientes(A<int>._)).ReturnsLazily(() => Task.FromResult(new List<CuponEspejo> { Cupon() }));
            A.CallTo(() => tienda.Crear(A<string>._, A<string>._))
                .ThrowsAsync(new PrestashopWebserviceException("no contesta")).Once()
                .Then.Returns(Task.FromResult("<prestashop><cart_rule><id>812</id></cart_rule></prestashop>"));

            Assert.AreEqual(0, await Servicio().ProcesarPendientes(4));
            Assert.AreEqual(1, await Servicio().ProcesarPendientes(4));

            A.CallTo(() => repositorio.Guardar(A<ResultadoEspejo>.That.Matches(r => !r.Correcto && r.Error == "no contesta"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Guardar(A<ResultadoEspejo>.That.Matches(r => r.Correcto && r.IdCartRule == 812))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarPendientes_SaltaLosQueYaCoinciden()
        {
            A.CallTo(() => repositorio.LeerPendientes(A<int>._)).Returns(Task.FromResult(new List<CuponEspejo> { Cupon(espejo: EstadosEspejo.ACTIVO, idCartRule: 812) }));

            await Servicio().ProcesarPendientes(4);

            A.CallTo(() => repositorio.Guardar(A<ResultadoEspejo>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ProcesarPendientes_Retrasados_CorreoUnaVezYLosMarca()
        {
            A.CallTo(() => repositorio.LeerPendientes(A<int>._)).Returns(Task.FromResult(new List<CuponEspejo>()));
            CuponEspejo retrasado = Cupon(intentos: 20);
            retrasado.EspejoError = "La tienda PrestaShop ha respondido 401 (Unauthorized) al POST cart_rules.";
            A.CallTo(() => repositorio.LeerRetrasados(4)).Returns(Task.FromResult(new List<CuponEspejo> { retrasado }));
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);

            await Servicio().ProcesarPendientes(4);

            Assert.IsNotNull(enviado);
            Assert.AreEqual(Constantes.Correos.INFORMATICA, enviado.To.Single().Address);
            StringAssert.Contains(enviado.Body, "EV-2610-7K3Q");
            StringAssert.Contains(enviado.Body, "más de 4 horas");
            A.CallTo(() => repositorio.MarcarAvisados(A<IEnumerable<int>>.That.Matches(ids => ids.Single() == 7))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarPendientes_SinRetrasados_SinCorreo()
        {
            A.CallTo(() => repositorio.LeerPendientes(A<int>._)).Returns(Task.FromResult(new List<CuponEspejo>()));
            A.CallTo(() => repositorio.LeerRetrasados(A<int>._)).Returns(Task.FromResult(new List<CuponEspejo>()));

            await Servicio().ProcesarPendientes(4);

            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        // ---------------- Antes de confirmar el pedido ----------------

        [TestMethod]
        public async Task DesactivarAntesDeConfirmar_ActivoEnLaTienda_LoDesactiva()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Task.FromResult(Cupon(EstadosCupon.USADO, EstadosEspejo.ACTIVO, 812, 5)));

            ResultadoEspejo r = await Servicio().DesactivarAntesDeConfirmar(7);

            Assert.IsTrue(r.Correcto);
            Assert.AreEqual(EstadosEspejo.INACTIVO, r.EstadoEspejo);
            A.CallTo(() => tienda.Modificar("cart_rules/812", A<string>.That.Contains("<active>0</active>"))).MustHaveHappenedOnceExactly();
            // Se apunta después de confirmar, fuera de la transacción
            A.CallTo(() => repositorio.Guardar(A<ResultadoEspejo>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DesactivarAntesDeConfirmar_LaTiendaNoContesta_NoParaElPedido()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Task.FromResult(Cupon(EstadosCupon.USADO, EstadosEspejo.ACTIVO, 812, 5)));
            A.CallTo(() => tienda.Modificar(A<string>._, A<string>._)).ThrowsAsync(new PrestashopWebserviceException("no contesta"));

            ResultadoEspejo r = await Servicio().DesactivarAntesDeConfirmar(7);

            Assert.IsFalse(r.Correcto);
            Assert.AreEqual(EstadosEspejo.ACTIVO, r.EstadoEspejo, "Sigue activo allí: lo desactiva el job");
            await Servicio().ApuntarResultado(r);
            A.CallTo(() => repositorio.Guardar(r)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DesactivarAntesDeConfirmar_NoEstaEnLaTiendaOApagado_NadaQueHacer()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Task.FromResult(Cupon(EstadosCupon.USADO, EstadosEspejo.SIN_CLIENTE)));
            Assert.IsNull(await Servicio().DesactivarAntesDeConfirmar(7));

            encendido = false;
            Assert.IsNull(await Servicio().DesactivarAntesDeConfirmar(7));
            A.CallTo(() => tienda.Modificar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Sincronizar_Apagado_NoLeeNada()
        {
            encendido = false;
            await Servicio().Sincronizar(7);
            A.CallTo(() => repositorio.Leer(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Sincronizar_RepositorioFalla_NoLanza()
        {
            A.CallTo(() => repositorio.Leer(7)).ThrowsAsync(new InvalidOperationException("sin columnas"));
            await Servicio().Sincronizar(7);
        }

        // ---------------- Cliente del webservice ----------------

        private class HandlerFalso : HttpMessageHandler
        {
            public HttpStatusCode Codigo = HttpStatusCode.OK;
            public string Cuerpo = "<prestashop/>";
            public bool Colgar;
            public readonly List<HttpRequestMessage> Peticiones = new List<HttpRequestMessage>();
            public readonly List<string> Cuerpos = new List<string>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Peticiones.Add(request);
                Cuerpos.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync());
                if (Colgar)
                {
                    throw new HttpRequestException("No se puede establecer una conexión");
                }
                return new HttpResponseMessage(Codigo) { Content = new StringContent(Cuerpo) };
            }
        }

        [TestMethod]
        public async Task ClienteWebservice_PutConXmlALaRutaDeLaTienda()
        {
            var handler = new HandlerFalso();
            var cliente = new ClienteWebservicePrestashop(() => handler, "https://tienda.example.com/api/", null);

            await cliente.Modificar("cart_rules/812", "<prestashop/>");

            Assert.AreEqual(HttpMethod.Put, handler.Peticiones.Single().Method);
            Assert.AreEqual("https://tienda.example.com/api/cart_rules/812", handler.Peticiones.Single().RequestUri.ToString());
            Assert.AreEqual("<prestashop/>", handler.Cuerpos.Single());
        }

        [TestMethod]
        public async Task ClienteWebservice_Error_ConElMensajeDePrestashopYSinElCorreo()
        {
            var handler = new HandlerFalso
            {
                Codigo = HttpStatusCode.Unauthorized,
                Cuerpo = "<prestashop><errors><error><code><![CDATA[17]]></code><message><![CDATA[Internal error. To see this error please display the PHP errors.]]></message></error></errors></prestashop>"
            };
            var cliente = new ClienteWebservicePrestashop(() => handler, "https://tienda.example.com/api", null);

            PrestashopWebserviceException ex = await Assert.ThrowsExceptionAsync<PrestashopWebserviceException>(
                () => cliente.Leer("customers?filter[email]=[luna%40example.com]"));

            Assert.AreEqual(HttpStatusCode.Unauthorized, ex.Codigo);
            StringAssert.Contains(ex.Message, "401");
            StringAssert.Contains(ex.Message, "Internal error");
            Assert.IsFalse(ex.Message.Contains("luna"), "El correo del cliente no va a ELMAH");
        }

        [TestMethod]
        public async Task ClienteWebservice_SinConexion_ExcepcionPropia()
        {
            var cliente = new ClienteWebservicePrestashop(() => new HandlerFalso { Colgar = true }, "https://tienda.example.com/api", null);

            PrestashopWebserviceException ex = await Assert.ThrowsExceptionAsync<PrestashopWebserviceException>(() => cliente.Crear("cart_rules", "<prestashop/>"));

            Assert.IsNull(ex.Codigo);
            StringAssert.Contains(ex.Message, "no ha contestado");
        }

        // ---------------- Uso desde la tienda (POST api/Cupones/{codigo}/Usar) ----------------

        private IRepositorioCupones cupones;
        private IEspejoCupones espejo;

        private ServicioCupones ServicioUso(bool tiendaEncendida = true)
        {
            cupones = cupones ?? A.Fake<IRepositorioCupones>();
            espejo = espejo ?? A.Fake<IEspejoCupones>();
            return new ServicioCupones(cupones, null, () => HOY, "CUPONEVENTO", espejo, () => correo, () => tiendaEncendida);
        }

        private static IPrincipal Casa() =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\TiendaOnline") }, "prueba"));

        private static readonly UsarCuponTiendaDTO USO = new UsarCuponTiendaDTO { Empresa = "1", Pedido = PEDIDO, ReferenciaTienda = "XKBKNABJK" };

        private void PrepararUso(CuponLeido cupon, string clientePedido = "15191", bool sePuedeUsar = true)
        {
            cupones = A.Fake<IRepositorioCupones>();
            A.CallTo(() => cupones.LeerPorCodigo("EV-2610-7K3Q")).Returns(Task.FromResult(cupon));
            A.CallTo(() => cupones.LeerPorId(cupon.Id)).Returns(Task.FromResult(cupon));
            A.CallTo(() => cupones.LeerClienteDelPedidoConLineaCupon("1", PEDIDO)).Returns(Task.FromResult(clientePedido));
            A.CallTo(() => cupones.UsarEnTienda(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).Returns(Task.FromResult(sePuedeUsar));
        }

        [TestMethod]
        public async Task UsarEnTienda_Apagado_400()
        {
            PrepararUso(CuponesTests.Cupon());
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioUso(false).UsarEnTienda("EV-2610-7K3Q", USO, Casa()));
            StringAssert.Contains(ex.Message, ReglasEspejoPrestashop.CLAVE_INTERRUPTOR);
        }

        [TestMethod]
        public async Task UsarEnTienda_ClienteDeLaTienda_403()
        {
            PrepararUso(CuponesTests.Cupon());
            var cliente = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", "15191") }, "prueba"));
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioUso().UsarEnTienda("EV-2610-7K3Q", USO, cliente));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task UsarEnTienda_PedidoSinLaLinea_400()
        {
            PrepararUso(CuponesTests.Cupon(), clientePedido: null);
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioUso().UsarEnTienda("EV-2610-7K3Q", USO, Casa()));
            StringAssert.Contains(ex.Message, "no tiene la línea del cupón");
            A.CallTo(() => cupones.UsarEnTienda(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UsarEnTienda_NoExiste_404()
        {
            PrepararUso(CuponesTests.Cupon());
            A.CallTo(() => cupones.LeerPorCodigo("EV-0000-XXXX")).Returns(Task.FromResult<CuponLeido>(null));
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioUso().UsarEnTienda("EV-0000-XXXX", USO, Casa()));
            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public async Task UsarEnTienda_Libre_QuedaUsadoConCanalPrestashopYSeDesactivaEnLaTienda()
        {
            PrepararUso(CuponesTests.Cupon());

            UsoCuponTiendaDTO r = await ServicioUso().UsarEnTienda("EV-2610-7K3Q", USO, Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.USADO, r.Resultado);
            Assert.IsFalse(r.AvisoAdministracion);
            A.CallTo(() => cupones.UsarEnTienda(7, "1", PEDIDO, "PrestaShop", A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => espejo.Sincronizar(7)).MustHaveHappenedOnceExactly();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UsarEnTienda_SegundaLlamada_NoHaceNada()
        {
            PrepararUso(CuponesTests.Cupon(EstadosCupon.USADO, pedidoUso: PEDIDO));

            UsoCuponTiendaDTO r = await ServicioUso().UsarEnTienda("EV-2610-7K3Q", USO, Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.YA_EN_ESTE_PEDIDO, r.Resultado);
            A.CallTo(() => cupones.UsarEnTienda(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UsarEnTienda_CarreraYaUsadoEnNesto_ElPedidoEntraYSeAvisaAAdministracion()
        {
            // Lo gastó en los dos sitios a la vez: en Nesto (pedido 930400) y en la tienda
            CuponLeido usadoEnNesto = CuponesTests.Cupon(EstadosCupon.USADO, pedidoUso: 930400);
            PrepararUso(usadoEnNesto, sePuedeUsar: false);
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);

            UsoCuponTiendaDTO r = await ServicioUso().UsarEnTienda("EV-2610-7K3Q", USO, Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.CONFLICTO, r.Resultado);
            Assert.IsTrue(r.AvisoAdministracion);
            StringAssert.Contains(r.Mensaje, "ya estaba usado en el pedido 930400");
            StringAssert.Contains(r.Mensaje, "XKBKNABJK");
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, enviado.To.Single().Address);
            StringAssert.Contains(enviado.Body, "930400");
            A.CallTo(() => espejo.Sincronizar(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UsarEnTienda_Anulado_Conflicto()
        {
            CuponLeido anulado = CuponesTests.Cupon(EstadosCupon.ANULADO);
            anulado.MotivoAnulacion = "No asistió al evento.";
            PrepararUso(anulado, sePuedeUsar: false);

            UsoCuponTiendaDTO r = await ServicioUso().UsarEnTienda("EV-2610-7K3Q", USO, Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.CONFLICTO, r.Resultado);
            StringAssert.Contains(r.Mensaje, "estaba anulado (No asistió al evento.)");
        }

        [TestMethod]
        public async Task UsarEnTienda_PedidoDeOtroCliente_SeUsaPeroSeAvisa()
        {
            PrepararUso(CuponesTests.Cupon(), clientePedido: "31517");

            UsoCuponTiendaDTO r = await ServicioUso().UsarEnTienda("EV-2610-7K3Q", USO, Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.USADO, r.Resultado);
            Assert.IsTrue(r.AvisoAdministracion);
            StringAssert.Contains(r.Mensaje, "31517");
        }

        // ---------------- Emisión, manual y anulación: al espejo ya ----------------

        [TestMethod]
        public async Task Anular_LoLlevaALaTienda()
        {
            PrepararUso(CuponesTests.Cupon());
            A.CallTo(() => cupones.Anular(7, A<string>._, A<string>._)).Returns(Task.FromResult(1));
            var admin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Admin"),
                new Claim(ClaimTypes.Role, "NUEVAVISION\\" + Constantes.GruposSeguridad.ADMINISTRACION) }, "prueba"));

            await ServicioUso().Anular("EV-2610-7K3Q", null, admin);

            A.CallTo(() => espejo.Sincronizar(7)).MustHaveHappenedOnceExactly();
        }
    }
}
