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
            A.CallTo(() => repositorio.LeerNifCliente(A<string>._, A<string>._)).Returns(Task.FromResult("B12345678"));
            cuentas = A.Fake<IBuscadorCuentaPrestashop>();
            A.CallTo(() => cuentas.CuentaDelCliente(A<string>._, A<string>._)).Returns(Task.FromResult<int?>(5));
            A.CallTo(() => tienda.Leer(A<string>.That.StartsWith("cart_rules?")))
                .Returns(Task.FromResult("<prestashop><cart_rules></cart_rules></prestashop>"));
            A.CallTo(() => tienda.Crear("cart_rules", A<string>._))
                .Returns(Task.FromResult("<prestashop><cart_rule><id><![CDATA[812]]></id></cart_rule></prestashop>"));
            A.CallTo(() => tienda.Modificar(A<string>._, A<string>._)).Returns(Task.FromResult("<prestashop/>"));
        }

        private IBuscadorCuentaPrestashop cuentas;

        private ServicioEspejoCuponesPrestashop Servicio(DateTime? ahora = null) =>
            new ServicioEspejoCuponesPrestashop(repositorio, tienda, () => encendido, () => ahora ?? HOY, () => correo, cuentas: cuentas);

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

        private static string Customers(params (int id, bool activo, bool invitado)[] cuentas) =>
            new XDocument(new XElement("prestashop", new XElement("customers", cuentas.Select(c => new XElement("customer",
                new XElement("id", c.id), new XElement("active", c.activo ? 1 : 0),
                new XElement("is_guest", c.invitado ? 1 : 0), new XElement("deleted", 0)))))).ToString();

        private static string Direcciones(params (int id, int customer, string dni)[] direcciones) =>
            new XDocument(new XElement("prestashop", new XElement("addresses", direcciones.Select(d => new XElement("address",
                new XElement("id", d.id), new XElement("id_customer", d.customer), new XElement("dni", d.dni), new XElement("deleted", 0)))))).ToString();

        private static XElement Regla(string xml) => XDocument.Parse(xml).Root.Element("cart_rule");

        // ---------------- Mapeo del cart_rule ----------------

        [TestMethod]
        public void XmlCartRule_MapeaLosCamposDelCupon()
        {
            XElement r = Regla(ReglasEspejoPrestashop.XmlCartRule(Cupon(), 5, true, null, 1, 1));

            Assert.AreEqual("0", r.Element("product_restriction").Value,
                "Sin restricción por el webservice (no expone las reglas de producto): ver ReglasEspejoPrestashop.XmlCartRule");

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

        // ---------------- Cliente ↔ customer: el casamiento de CanalesExternos (Carlos, 22/10/26) ----------------

        [TestMethod]
        public void Casamiento_LimpiarDni_ComoCanalesExternos()
        {
            Assert.AreEqual("B12345678", CasamientoClientePrestashop.LimpiarDni(" B-12.345.678 "));
            Assert.AreEqual("1234567Z", CasamientoClientePrestashop.LimpiarDni("01234567-Z"));
            Assert.AreEqual(string.Empty, CasamientoClientePrestashop.LimpiarDni(null));
            Assert.AreEqual("12345678", CasamientoClientePrestashop.TextoBusqueda("B-12345678"));
            Assert.AreEqual("X1234", CasamientoClientePrestashop.TextoBusqueda("X1234"), "Sin 5 cifras seguidas, el NIF limpio");
        }

        [TestMethod]
        public async Task Casamiento_ClientesDelDni_LimpiaYBuscaComoPorNifConElMinimoDeCinco()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.BuscarClientesPorNif("B12345678"))
                .Returns(Task.FromResult(new List<ClienteDTO> { new ClienteDTO { cliente = "15191" }, new ClienteDTO { cliente = "20000" } }));

            Assert.AreEqual("15191", (await CasamientoClientePrestashop.ClienteDelDni(servicio, "b-12345678".ToUpperInvariant())).cliente, "El primero");
            Assert.IsNull(await CasamientoClientePrestashop.ClienteDelDni(servicio, "0001-23"), "Menos de 5: el genérico de la tienda");
            A.CallTo(() => servicio.BuscarClientesPorNif("123")).MustNotHaveHappened();
        }

        private BuscadorCuentaPrestashop Buscador(Dictionary<string, string> clientePorDni) =>
            new BuscadorCuentaPrestashop(tienda, dni => Task.FromResult(clientePorDni.TryGetValue(CasamientoClientePrestashop.LimpiarDni(dni), out string c)
                ? new ClienteDTO { cliente = c } : null));

        [TestMethod]
        public async Task Casamiento_CuentaDelCliente_LasDireccionesQueCasanConElYLaMasReciente()
        {
            A.CallTo(() => tienda.Leer(A<string>.That.StartsWith("addresses?filter[dni]=%25[12345678]%25")))
                .Returns(Task.FromResult(Direcciones((1, 10, "B-12345678"), (2, 25, "B12345678"), (3, 40, "212345678X"))));
            A.CallTo(() => tienda.Leer(A<string>.That.StartsWith("customers?filter[id]=[10|25]")))
                .Returns(Task.FromResult(Customers((10, true, false), (25, true, false))));

            int? cuenta = await Buscador(new Dictionary<string, string> { ["B12345678"] = "15191", ["212345678X"] = "99999" })
                .CuentaDelCliente("15191", "B12345678");

            Assert.AreEqual(25, cuenta, "Varias cuentas: la más reciente; la del otro NIF que contiene las cifras no es suya");
        }

        [TestMethod]
        public async Task Casamiento_CuentaDelCliente_SoloInvitadosOSinDirecciones_Null()
        {
            A.CallTo(() => tienda.Leer(A<string>.That.StartsWith("addresses?")))
                .Returns(Task.FromResult(Direcciones((1, 10, "B12345678"))));
            A.CallTo(() => tienda.Leer(A<string>.That.StartsWith("customers?")))
                .Returns(Task.FromResult(Customers((10, true, true))));
            var buscador = Buscador(new Dictionary<string, string> { ["B12345678"] = "15191" });

            Assert.IsNull(await buscador.CuentaDelCliente("15191", "B12345678"), "De invitado: no puede entrar a usar el cupón");
            Assert.IsNull(await buscador.CuentaDelCliente("15191", "123"), "NIF corto: ni se pregunta");
            Assert.IsNull(await buscador.CuentaDelCliente("20000", "B12345678"), "Casa con otro cliente");
        }

        [TestMethod]
        public async Task Espejar_ClienteSinNif_SinClienteYNoLlamaALaTienda()
        {
            A.CallTo(() => repositorio.LeerNifCliente(A<string>._, A<string>._)).Returns(Task.FromResult<string>(null));

            ResultadoEspejo r = await Servicio().Espejar(Cupon());

            Assert.IsTrue(r.Correcto);
            Assert.AreEqual(EstadosEspejo.SIN_CLIENTE, r.EstadoEspejo);
            StringAssert.Contains(r.Error, "no tiene cuenta en la tienda");
            A.CallTo(() => cuentas.CuentaDelCliente(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => tienda.Crear(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Espejar_BuscaLaCuentaConElCasamientoDeCanalesExternos()
        {
            ResultadoEspejo r = await Servicio().Espejar(Cupon());

            Assert.AreEqual(5, r.IdCustomer);
            A.CallTo(() => cuentas.CuentaDelCliente("15191", "B12345678")).MustHaveHappenedOnceExactly();
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
            A.CallTo(() => cuentas.CuentaDelCliente(A<string>._, A<string>._)).MustNotHaveHappened();
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
        public async Task UsarEnTienda_ContratoDeNesto518_PedidoSinLaLineaPorqueYaEstabaUsado_SoloAvisa()
        {
            // Nesto#518: el POST del pedido no pudo reservarlo (ya usado), así que el pedido entra sin la línea y llama a Usar
            PrepararUso(CuponesTests.Cupon(EstadosCupon.USADO, pedidoUso: 930400), clientePedido: null);
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);
            var peticion = new UsarCuponTiendaDTO { Empresa = "1", Pedido = PEDIDO, Canal = "PrestaShop", PedidoCanal = "54321", ImporteDescontado = 50 };

            UsoCuponTiendaDTO r = await ServicioUso().UsarEnTienda("EV-2610-7K3Q", peticion, Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.CONFLICTO, r.Resultado);
            Assert.IsTrue(r.AvisoAdministracion);
            StringAssert.Contains(r.Mensaje, "ya estaba usado en el pedido 930400");
            StringAssert.Contains(r.Mensaje, "54321");
            StringAssert.Contains(r.Mensaje, "50,00 €");
            StringAssert.Contains(enviado.Body, "sin la línea del cupón");
            A.CallTo(() => cupones.UsarEnTienda(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        // ---------------- Base de Ganavisiones (Nesto#518) ----------------

        [TestMethod]
        public async Task LeerPorCodigo_TraeLaBaseYLoQueRestaDeGanavisiones()
        {
            PrepararUso(CuponesTests.Cupon());
            A.CallTo(() => cupones.LeerProductos("1", A<IEnumerable<string>>._)).Returns(Task.FromResult(
                new Dictionary<string, ProductoParaCupon>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CUPONEVENTO"] = new ProductoParaCupon { Numero = "CUPONEVENTO", Grupo = Constantes.Productos.GRUPO_COSMETICA, Ficticio = true }
                }));

            CuponDTO c = await ServicioUso().LeerPorCodigo("EV-2610-7K3Q", Casa());

            Assert.AreEqual(41.32M, c.Base);
            Assert.AreEqual(41.32M, c.RestaDeLaBaseGanavisiones);
        }

        [TestMethod]
        public async Task LeerDelCliente_ProductoDeUnGrupoSinGanavisiones_NoResta()
        {
            PrepararUso(CuponesTests.Cupon());
            A.CallTo(() => cupones.LeerDelCliente("1", "15191")).Returns(Task.FromResult(new List<CuponLeido> { CuponesTests.Cupon() }));
            A.CallTo(() => cupones.LeerProductos("1", A<IEnumerable<string>>._)).Returns(Task.FromResult(
                new Dictionary<string, ProductoParaCupon>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CUPONEVENTO"] = new ProductoParaCupon { Numero = "CUPONEVENTO", Grupo = "PEL", Ficticio = true }
                }));

            CuponDTO c = (await ServicioUso().LeerDelCliente("1", "15191", false, Casa())).Single();

            Assert.AreEqual(41.32M, c.Base);
            Assert.AreEqual(0, c.RestaDeLaBaseGanavisiones);
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
