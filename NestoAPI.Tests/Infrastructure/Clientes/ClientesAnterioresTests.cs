using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Clientes.Origenes;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#876 cortes 1 y 2: el cliente que cambia de forma fiscal se da de alta con su número anterior (origen CAMBIO-FISCAL) y
    /// la pieza única «clientes equivalentes» (el cliente y sus anteriores, en cadena) queda lista para las consultas comerciales.
    /// </summary>
    [TestClass]
    public class ClientesAnterioresTests
    {
        private const string CAMBIO = ReglaClienteAnterior.ORIGEN_CAMBIO_FISCAL;

        private IRepositorioOrigenesCliente origenes;
        private IRepositorioClientesAnteriores anteriores;
        private List<string> guardados;
        private List<Exception> errores;

        [TestInitialize]
        public void Preparar()
        {
            ClientesEquivalentes.Invalidar();
            guardados = new List<string>();
            errores = new List<Exception>();
            origenes = A.Fake<IRepositorioOrigenesCliente>();
            A.CallTo(() => origenes.LeerCatalogo()).Returns(Task.FromResult(new List<OrigenCliente>
            {
                new OrigenCliente { Codigo = CAMBIO, Descripcion = "Cambio de forma fiscal (cliente que ya lo era)", Activo = true, EsLead = false, HorasParaLlamar = 24 },
                new OrigenCliente { Codigo = "WEB", Descripcion = "Web", Activo = true, EsLead = false, HorasParaLlamar = 24 }
            }));
            anteriores = A.Fake<IRepositorioClientesAnteriores>();
            A.CallTo(() => anteriores.TablaExiste()).Returns(true);
            A.CallTo(() => anteriores.LeerCliente("1", "15191")).Returns(Task.FromResult(new DatosClienteAnterior { Cliente = "15191", Nif = "12345678Z" }));
            A.CallTo(() => anteriores.Guardar(A<string>._, A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((string e, string c, string a, string u) => { guardados.Add($"{e}|{c}|{a}|{u}"); return Task.CompletedTask; });
        }

        private static IGestorClientes GestorQueGuarda()
        {
            IGestorClientes gestor = A.Fake<IGestorClientes>();
            A.CallTo(() => gestor.CrearCliente(A<ClienteCrear>._, A<NVEntities>._)).Returns(new Cliente { Empresa = "1  ", Nº_Cliente = "42109     ", Contacto = "0  " });
            return gestor;
        }

        private ClientesController Controlador(IGestorClientes gestor)
        {
            var controlador = new ClientesController(gestor, A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>())
            {
                RepositorioOrigenes = origenes,
                RepositorioClientesAnteriores = anteriores,
                RegistrarErrorOrigen = errores.Add,
                RelojOrigenes = () => new DateTime(2026, 11, 26, 10, 0, 0)
            };
            controlador.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos"), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.DIRECCION)
            }, "prueba"));
            return controlador;
        }

        private static ClienteCrear Alta(string origen, string anterior, string nif = "B12345678") => new ClienteCrear
        {
            Empresa = "1",
            Nombre = "PELUQUERÍA NUEVA, S.L.",
            Nif = nif,
            Origen = origen,
            ClienteAnterior = anterior,
            PersonasContacto = new List<PersonaContactoDTO>()
        };

        // ---------------- corte 1: el alta ----------------

        [TestMethod]
        public async Task PostCliente_CambioFiscalConAnterior_LoGuardaTrasElAlta()
        {
            IGestorClientes gestor = GestorQueGuarda();

            var creado = await Controlador(gestor).PostCliente(Alta(CAMBIO, " 15191 ")) as CreatedAtRouteNegotiatedContentResult<Cliente>;

            Assert.IsNotNull(creado);
            CollectionAssert.AreEqual(new[] { "1|42109|15191|NUEVAVISION\\Carlos" }, guardados);
            Assert.AreEqual("15191", creado.Content.ClienteAnterior);
            Assert.IsNull(creado.Content.AvisoClienteAnterior);
        }

        [TestMethod]
        public async Task PostCliente_CambioFiscalSinAnterior_400SinDarDeAlta()
        {
            IGestorClientes gestor = GestorQueGuarda();

            var malo = await Controlador(gestor).PostCliente(Alta(CAMBIO, null)) as BadRequestErrorMessageResult;

            Assert.AreEqual(ReglaClienteAnterior.MOTIVO_FALTA, malo?.Message);
            A.CallTo(() => gestor.CrearCliente(A<ClienteCrear>._, A<NVEntities>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostCliente_AnteriorQueNoExiste_400()
        {
            IGestorClientes gestor = GestorQueGuarda();
            A.CallTo(() => anteriores.LeerCliente("1", "99999")).Returns(Task.FromResult<DatosClienteAnterior>(null));

            var malo = await Controlador(gestor).PostCliente(Alta(CAMBIO, "99999")) as BadRequestErrorMessageResult;

            Assert.AreEqual("No existe el cliente 99999 en la empresa 1: compruebe el número de cliente anterior.", malo?.Message);
            A.CallTo(() => gestor.CrearCliente(A<ClienteCrear>._, A<NVEntities>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostCliente_AnteriorSinOrigenCambioFiscal_400()
        {
            var malo = await Controlador(GestorQueGuarda()).PostCliente(Alta("WEB", "15191")) as BadRequestErrorMessageResult;

            Assert.AreEqual(ReglaClienteAnterior.MOTIVO_SIN_CAMBIO_FISCAL, malo?.Message);
        }

        [TestMethod]
        public async Task PostCliente_ContactoDelMismoCliente_400()
        {
            ClienteCrear alta = Alta(CAMBIO, "15191");
            alta.EsContacto = true;
            alta.Cliente = "15191";

            var malo = await Controlador(GestorQueGuarda()).PostCliente(alta) as BadRequestErrorMessageResult;

            Assert.AreEqual("El cliente anterior no puede ser el mismo cliente (15191).", malo?.Message);
        }

        [TestMethod]
        public async Task PostCliente_MismoNif_SeDaDeAltaConAviso()
        {
            var creado = await Controlador(GestorQueGuarda()).PostCliente(Alta(CAMBIO, "15191", "12345678-z")) as CreatedAtRouteNegotiatedContentResult<Cliente>;

            Assert.IsNotNull(creado);
            Assert.AreEqual(1, guardados.Count);
            StringAssert.Contains(creado.Content.AvisoClienteAnterior, "mismo NIF (12345678Z)");
        }

        [TestMethod]
        public async Task PostCliente_SinTabla_ComoHoy_NiSeExigeNiSeGuarda()
        {
            A.CallTo(() => anteriores.TablaExiste()).Returns(false);
            IGestorClientes gestor = GestorQueGuarda();

            var creado = await Controlador(gestor).PostCliente(Alta(CAMBIO, null)) as CreatedAtRouteNegotiatedContentResult<Cliente>;

            Assert.IsNotNull(creado);
            A.CallTo(() => anteriores.Guardar(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostCliente_SinOrigenNiAnterior_NoMiraNada()
        {
            var creado = await Controlador(GestorQueGuarda()).PostCliente(Alta(null, null)) as CreatedAtRouteNegotiatedContentResult<Cliente>;

            Assert.IsNotNull(creado);
            A.CallTo(() => anteriores.TablaExiste()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostCliente_FallaAlGuardarElEnlace_ElClienteQuedaCreadoYVaAElmah()
        {
            A.CallTo(() => anteriores.Guardar(A<string>._, A<string>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("caída"));

            var creado = await Controlador(GestorQueGuarda()).PostCliente(Alta(CAMBIO, "15191")) as CreatedAtRouteNegotiatedContentResult<Cliente>;

            Assert.IsNotNull(creado);
            Assert.IsTrue(errores.Any(e => e.Message.Contains("42109") && e.Message.Contains("15191")));
        }

        [TestMethod]
        public async Task GetClienteCrear_TraeElClienteAnterior()
        {
            IGestorClientes gestor = A.Fake<IGestorClientes>();
            A.CallTo(() => gestor.ConstruirClienteCrear("1", "42109", "0")).Returns(Task.FromResult(new ClienteCrear { Empresa = "1", Cliente = "42109" }));
            A.CallTo(() => anteriores.LeerAnterior("1", "42109")).Returns(Task.FromResult("15191"));

            var ok = await Controlador(gestor).GetClienteCrear("1", "42109", "0") as OkNegotiatedContentResult<ClienteCrear>;

            Assert.AreEqual("15191", ok.Content.ClienteAnterior);
        }

        [TestMethod]
        public void AvisoMismoNif_DistintoOVacio_Null()
        {
            var anterior = new DatosClienteAnterior { Cliente = "15191", Nif = "12345678Z" };
            Assert.IsNull(ReglaClienteAnterior.AvisoMismoNif(anterior, "B12345678"));
            Assert.IsNull(ReglaClienteAnterior.AvisoMismoNif(anterior, null));
            Assert.IsNull(ReglaClienteAnterior.AvisoMismoNif(new DatosClienteAnterior { Cliente = "15191" }, "12345678Z"));
        }

        // ---------------- corte 2: clientes equivalentes ----------------

        [TestMethod]
        public void Cadena_SigueLosAnteriores_DelMasRecienteAlMasAntiguo()
        {
            var enlaces = new Dictionary<string, string> { ["300"] = "200", ["200"] = "100" };

            CollectionAssert.AreEqual(new[] { "300", "200", "100" }, ReglaClientesEquivalentes.Cadena(" 300 ", enlaces).ToList());
            CollectionAssert.AreEqual(new[] { "200", "100" }, ReglaClientesEquivalentes.Cadena("200", enlaces).ToList());
            CollectionAssert.AreEqual(new[] { "999" }, ReglaClientesEquivalentes.Cadena("999", enlaces).ToList());
            Assert.AreEqual(0, ReglaClientesEquivalentes.Cadena(" ", enlaces).Count);
        }

        [TestMethod]
        public void Cadena_ConUnCiclo_NoSeCuelga()
        {
            var enlaces = new Dictionary<string, string> { ["A"] = "B", ["B"] = "C", ["C"] = "A" };

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, ReglaClientesEquivalentes.Cadena("A", enlaces).ToList());
        }

        [TestMethod]
        public void Cadena_MuyLarga_ParaEnElTope()
        {
            var enlaces = Enumerable.Range(1, 100).ToDictionary(i => i.ToString(), i => (i + 1).ToString());

            Assert.AreEqual(ReglaClientesEquivalentes.MAXIMO_SALTOS + 1, ReglaClientesEquivalentes.Cadena("1", enlaces).Count);
        }

        [TestMethod]
        public async Task ClientesEquivalentes_LeeLaEmpresaUnaVezYUsaLaCache()
        {
            A.CallTo(() => anteriores.LeerEnlaces("1")).Returns(Task.FromResult(new List<EnlaceClienteAnterior>
            {
                new EnlaceClienteAnterior { Empresa = "1", Cliente = "42109     ", ClienteAnterior = "15191     ", Fecha = new DateTime(2026, 11, 26) }
            }));
            DateTime ahora = new DateTime(2026, 11, 26, 10, 0, 0);
            var equivalentes = new ClientesEquivalentes(anteriores, () => ahora);

            CollectionAssert.AreEqual(new[] { "42109", "15191" }, (await equivalentes.De("1", "42109")).ToList());
            CollectionAssert.AreEqual(new[] { "15191" }, (await equivalentes.De("1  ", "15191")).ToList());
            A.CallTo(() => anteriores.LeerEnlaces("1")).MustHaveHappenedOnceExactly();

            ahora = ahora.Add(ClientesEquivalentes.DURACION_CACHE).AddSeconds(1);
            _ = await equivalentes.De("1", "42109");
            A.CallTo(() => anteriores.LeerEnlaces("1")).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task ClientesEquivalentes_SinTablaOSiFalla_SoloElCliente()
        {
            A.CallTo(() => anteriores.TablaExiste()).Returns(false);
            CollectionAssert.AreEqual(new[] { "42109" }, (await new ClientesEquivalentes(anteriores).De("1", "42109")).ToList());

            ClientesEquivalentes.Invalidar();
            A.CallTo(() => anteriores.TablaExiste()).Returns(true);
            A.CallTo(() => anteriores.LeerEnlaces(A<string>._)).Throws(new InvalidOperationException("caída"));
            CollectionAssert.AreEqual(new[] { "42109" }, (await new ClientesEquivalentes(anteriores).De("1", "42109")).ToList());
        }
    }
}
