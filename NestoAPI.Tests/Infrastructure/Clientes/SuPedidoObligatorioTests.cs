using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>NestoAPI#723 (Laura Magán / Chanel): P.O. obligatorio por cliente.</summary>
    [TestClass]
    public class SuPedidoObligatorioTests
    {
        private Func<bool> interruptorAntes;

        [TestInitialize]
        public void Encender() { interruptorAntes = ReglaSuPedidoObligatorio.Interruptor; ReglaSuPedidoObligatorio.Interruptor = () => true; }

        [TestCleanup]
        public void Restaurar() => ReglaSuPedidoObligatorio.Interruptor = interruptorAntes;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            foreach (string g in grupos) claims.Add(new Claim(ClaimTypes.Role, g));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        [TestMethod]
        public void Regla_AlGrabar_SoloSiElClienteExige_YNoEsPresupuesto_YNoTraeSuPedido()
        {
            var sin = new PedidoVentaDTO { cliente = "12345", suPedido = "  " };
            Assert.IsNull(ReglaSuPedidoObligatorio.MotivoAlGrabar(sin, clienteExige: false));
            StringAssert.Contains(ReglaSuPedidoObligatorio.MotivoAlGrabar(sin, clienteExige: true), "12345");
            StringAssert.Contains(ReglaSuPedidoObligatorio.MotivoAlGrabar(sin, clienteExige: true), "Su pedido");
            Assert.IsNull(ReglaSuPedidoObligatorio.MotivoAlGrabar(new PedidoVentaDTO { cliente = "12345", suPedido = "4500123" }, true));
            Assert.IsNull(ReglaSuPedidoObligatorio.MotivoAlGrabar(new PedidoVentaDTO { cliente = "12345", EsPresupuesto = true }, true), "presupuesto");
            Assert.IsNull(ReglaSuPedidoObligatorio.MotivoAlGrabar(null, true));
        }

        [TestMethod]
        public void Regla_AlFacturar_YInterruptor()
        {
            StringAssert.Contains(ReglaSuPedidoObligatorio.MotivoAlFacturar("12345", 929000, null, true), "929000");
            Assert.IsNull(ReglaSuPedidoObligatorio.MotivoAlFacturar("12345", 929000, "PO-1", true));
            Assert.IsNull(ReglaSuPedidoObligatorio.MotivoAlFacturar("12345", 929000, null, false));
            ReglaSuPedidoObligatorio.Interruptor = () => false;
            Assert.IsFalse(ReglaSuPedidoObligatorio.Activa);
            ReglaSuPedidoObligatorio.Interruptor = null;
            Assert.IsFalse(ReglaSuPedidoObligatorio.Activa, "sin clave en el config de los tests, apagado");
        }

        [TestMethod]
        public void SuPedidoFactura_Normalizar()
        {
            Assert.IsNull(SuPedidoFactura.Normalizar("  4500123 ", out string limpio));
            Assert.AreEqual("4500123", limpio);
            Assert.IsNotNull(SuPedidoFactura.Normalizar("   ", out _));
            Assert.IsNotNull(SuPedidoFactura.Normalizar(new string('x', 51), out _));
        }

        [TestMethod]
        public async Task FacturasController_PutSuPedido_SoloAdministracionDireccionInformatica_YCambia()
        {
            var cambios = new List<string>();
            var controlador = new FacturasController(A.Fake<IServicioFacturas>(), A.Fake<IGestorFacturas>())
            {
                CambiarSuPedidoFactura = (empresa, numero, po, usuario) => { cambios.Add($"{empresa}|{numero}|{po}|{usuario}"); return Task.FromResult("viejo"); }
            };
            controlador.User = Usuario("Laura", "Vendedores");
            var prohibido = (NegotiatedContentResult<string>)await controlador.PutSuPedido("1", "NV2616075", new SuPedidoFacturaDTO { SuPedido = "4500123" });
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
            Assert.AreEqual(0, cambios.Count);

            controlador.User = Usuario("Laura", Constantes.GruposSeguridad.ADMINISTRACION);
            Assert.IsInstanceOfType(await controlador.PutSuPedido("1", "NV2616075", new SuPedidoFacturaDTO { SuPedido = " " }), typeof(BadRequestErrorMessageResult));
            var ok = (OkNegotiatedContentResult<SuPedidoFacturaDTO>)await controlador.PutSuPedido("1", "NV2616075", new SuPedidoFacturaDTO { SuPedido = " 4500123 " });
            Assert.AreEqual("4500123", ok.Content.SuPedido);
            Assert.AreEqual("viejo", ok.Content.SuPedidoAnterior);
            Assert.AreEqual("1|NV2616075|4500123|NUEVAVISION\\Laura", cambios[0]);
        }

        [TestMethod]
        public async Task ClientesController_PutSuPedidoObligatorio_GuardaLaMarca()
        {
            var puestos = new List<string>();
            var controlador = new ClientesController(A.Fake<IGestorClientes>(), A.Fake<NestoAPI.Infraestructure.Vendedores.IServicioVendedores>())
            {
                PonerSuPedidoObligatorio = (empresa, cliente, exige, usuario) => { puestos.Add($"{empresa}|{cliente}|{exige}|{usuario}"); return Task.CompletedTask; }
            };
            controlador.User = Usuario("Carlos", Constantes.GruposSeguridad.DIRECCION);
            var ok = (OkNegotiatedContentResult<SuPedidoObligatorioDTO>)await controlador.PutSuPedidoObligatorio("1", "12345", new SuPedidoObligatorioDTO { Exige = true });
            Assert.IsTrue(ok.Content.Exige);
            Assert.AreEqual("1|12345|True|NUEVAVISION\\Carlos", puestos[0]);
            controlador.User = Usuario("Pepe", "Vendedores");
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await controlador.PutSuPedidoObligatorio("1", "12345", new SuPedidoObligatorioDTO { Exige = false })).StatusCode);
        }
    }
}
