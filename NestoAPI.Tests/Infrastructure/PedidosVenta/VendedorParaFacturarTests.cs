using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#813 (Paloma 03/11/26 ×6, factura del pedido 929429; Reina 04/11/26, albarán del 929909 del cliente 27040):
    /// prdCrearAlbaránVta y prdCrearFacturaVta toman el vendedor de la cabecera del pedido cuando el grupo no tiene; con la
    /// cabecera sin vendedor metían NULL («No se puede insertar el valor NULL en la columna Vendedor»). Antes de llamarlos
    /// 400 con lo que hay que hacer (Carlos 04/11/26: solo avisar, no se toma el de la ficha ni se escribe en el pedido).
    /// </summary>
    [TestClass]
    public class VendedorParaFacturarTests
    {
        private const string MENSAJE_27040 = "El pedido 929909 del cliente 27040 no tiene vendedor: póngaselo en el pedido antes de albaranar o facturar";

        private static VendedorParaFacturar Guardia(VendedoresPedidoYCliente datos, List<string> fijados)
            => new VendedorParaFacturar((e, p) => Task.FromResult(datos));

        // ============================================================ núcleo

        [TestMethod]
        public void Resolver_SoloLaCabecera()
        {
            Assert.AreEqual("PA", VendedorParaFacturar.Resolver("PA "));
            Assert.IsNull(VendedorParaFacturar.Resolver("   "));
            Assert.IsNull(VendedorParaFacturar.Resolver(null));
        }

        [TestMethod]
        public async Task Asegurar_PedidoConVendedor_LoDevuelveSinTocarNada()
        {
            var fijados = new List<string>();
            string vendedor = await Guardia(new VendedoresPedidoYCliente { Cliente = "27040", VendedorPedido = "NV " }, fijados)
                .Asegurar("1", 929909);
            Assert.AreEqual("NV", vendedor);
            Assert.AreEqual(0, fijados.Count);
        }

        /// <summary>Carlos 04/11/26: sin vendedor en la cabecera solo se avisa; ni se toma el de la ficha ni se escribe en el pedido.</summary>
        [TestMethod]
        public async Task Asegurar_PedidoSinVendedor_400SinTocarElPedido()
        {
            var fijados = new List<string>();
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Guardia(new VendedoresPedidoYCliente { Cliente = "27040", VendedorPedido = "  " }, fijados).Asegurar("1", 929909));
            Assert.AreEqual(MENSAJE_27040, ex.Message);
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.AreEqual(0, fijados.Count);
        }

        [TestMethod]
        public async Task Asegurar_PedidoInexistente_NullSinLanzar()
        {
            var fijados = new List<string>();
            Assert.IsNull(await Guardia(null, fijados).Asegurar("1", 1));
            Assert.AreEqual(0, fijados.Count);
        }

        // ============================================================ albarán

        private static ServicioAlbaranesVenta ServicioAlbaran(VendedoresPedidoYCliente datos, List<string> pasos)
        {
            return new ServicioAlbaranesVenta(A.Fake<NVEntities>(),
                (e, p, a, u) => Task.CompletedTask,
                (e, p) => Task.FromResult(new List<FaltaSinQuitar>()),
                (e, p) => Task.FromResult<string>(null))
            {
                GuardiaVendedor = new VendedorParaFacturar((e, p) => Task.FromResult(datos)),
                ProcedimientoCrearAlbaranVta = (e, p, f, u) => { pasos.Add("SP " + p); return Task.FromResult(0); }
            };
        }

        [TestMethod]
        public async Task CrearAlbaran_ConVendedorEnLaCabecera_NoTocaNadaYLlamaAlSP()
        {
            var pasos = new List<string>();
            await ServicioAlbaran(new VendedoresPedidoYCliente { Cliente = "27040", Contacto = "0", VendedorPedido = "NV" }, pasos)
                .CrearAlbaran("1", 929909, "NUEVAVISION\\Reina");
            CollectionAssert.AreEqual(new[] { "SP 929909" }, pasos);
        }

        [TestMethod]
        public async Task CrearAlbaran_SinVendedorEnLaCabecera_400SinLlamarAlSP_Reina04Nov()
        {
            var pasos = new List<string>();
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                ServicioAlbaran(new VendedoresPedidoYCliente { Cliente = "27040", Contacto = "0" }, pasos).CrearAlbaran("1", 929909, "NUEVAVISION\\Reina"));
            Assert.AreEqual(MENSAJE_27040, ex.Message);
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.AreEqual(0, pasos.Count);
        }

        // ============================================================ GET api/Clientes/SinVendedor

        private static IPrincipal Usuario(params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Pepe") };
            foreach (string g in grupos)
            {
                claims.Add(new Claim(ClaimTypes.Role, g));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static ClientesController Controlador(IPrincipal usuario, List<int?> consultas)
        {
            var controlador = new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>())
            {
                LeerClientesSinVendedor = meses =>
                {
                    consultas.Add(meses);
                    return Task.FromResult(new List<ClienteSinVendedorDTO>
                    {
                        new ClienteSinVendedorDTO { Empresa = "3", Cliente = "20459", Contacto = "0", Estado = 9, UltimoPedido = new DateTime(2026, 10, 26) }
                    });
                }
            };
            controlador.User = usuario;
            return controlador;
        }

        [TestMethod]
        public async Task GetClientesSinVendedor_Administracion_200ConLaLista()
        {
            var consultas = new List<int?>();
            var ok = (OkNegotiatedContentResult<List<ClienteSinVendedorDTO>>)await Controlador(Usuario(Constantes.GruposSeguridad.ADMINISTRACION), consultas)
                .GetClientesSinVendedor(12);
            Assert.AreEqual("20459", ok.Content[0].Cliente);
            CollectionAssert.AreEqual(new int?[] { 12 }, consultas);
        }

        [TestMethod]
        public async Task GetClientesSinVendedor_SinPermiso_403SinConsultar()
        {
            var consultas = new List<int?>();
            var resultado = (NegotiatedContentResult<string>)await Controlador(Usuario(Constantes.GruposSeguridad.ALMACEN), consultas).GetClientesSinVendedor();
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(0, consultas.Count);
        }

        [TestMethod]
        public async Task GetClientesSinVendedor_MesesCero_400()
        {
            var consultas = new List<int?>();
            Assert.IsInstanceOfType(await Controlador(Usuario(Constantes.GruposSeguridad.DIRECCION), consultas).GetClientesSinVendedor(0),
                typeof(BadRequestErrorMessageResult));
            Assert.AreEqual(0, consultas.Count);
        }
    }
}
