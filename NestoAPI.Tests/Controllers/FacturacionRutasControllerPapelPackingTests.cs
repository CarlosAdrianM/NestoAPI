using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#828 (S4): Facturar rutas dice lo que ya salió en el packing («N facturas en papel ya impresas en el packing»), lo que
    /// quedó pendiente y lo que sale en la tanda con su envío para localizar la caja.
    /// </summary>
    [TestClass]
    public class FacturacionRutasControllerPapelPackingTests
    {
        private IServicioPedidosParaFacturacion servicioPedidos;
        private IRepositorioDocumentosPapelPacking marcas;
        private FacturacionRutasController controller;
        private List<int> pedidosPreguntados;

        [TestInitialize]
        public void Preparar()
        {
            servicioPedidos = A.Fake<IServicioPedidosParaFacturacion>();
            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(A<string>._, A<DateTime>._)).Returns(Task.FromResult(new List<CabPedidoVta>()));
            var agrupar = A.Fake<IServicioAgruparPorPO>();
            A.CallTo(() => agrupar.EvaluarYProcesar(A<string>._, A<string>._)).Returns(Task.FromResult(new ResultadoAgrupacionPO()));
            marcas = A.Fake<IRepositorioDocumentosPapelPacking>();
            pedidosPreguntados = new List<int>();
            controller = new FacturacionRutasController(A.Fake<NVEntities>(), servicioPedidos, agrupar)
            {
                GuardiaNotas = A.Fake<IGuardiaNotaSinPacking>(),
                MarcasPapel = marcas,
                Hoy = () => new DateTime(2026, 11, 10),
                EnviosDePedidos = pedidos =>
                {
                    pedidosPreguntados.AddRange(pedidos);
                    return Task.FromResult(new Dictionary<int, int> { { 929914, 251001 } });
                }
            };
            var identidad = new ClaimsIdentity("TestAuth");
            identidad.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            identidad.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\Alfredo"));
            controller.User = new ClaimsPrincipal(identidad);
        }

        private void Marcas(params DocumentoEnPapelDTO[] filas)
            => A.CallTo(() => marcas.LeerDesde(new DateTime(2026, 11, 10))).Returns(Task.FromResult(filas.ToList()));

        private static DocumentoEnPapelDTO Marca(int pedido, string factura, string estado, int? envio = null)
            => new DocumentoEnPapelDTO { Empresa = "1", Pedido = pedido, Envio = envio, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = factura, Estado = estado };

        [TestMethod]
        public async Task Agencias_DiceLasYaImpresasEnElPackingYLasPendientesConSuEnvio()
        {
            Marcas(Marca(1, "NV1", EstadosDocumentoEnPapel.IMPRESO), Marca(2, "NV2", EstadosDocumentoEnPapel.IMPRESO),
                Marca(3, "NV3", EstadosDocumentoEnPapel.PENDIENTE, 250999));

            var ok = (OkNegotiatedContentResult<FacturarRutasResponseDTO>)await controller.FacturarRutas(new FacturarRutasRequestDTO { TipoRuta = "AGENCIA" });

            Assert.AreEqual(2, ok.Content.ImpresosEnPacking.Count);
            Assert.AreEqual("NV3", ok.Content.PendientesEnPacking.Single().Documento);
            StringAssert.StartsWith(ok.Content.ResumenPapelPacking, "2 facturas en papel ya impresas en el packing.");
            StringAssert.Contains(ok.Content.ResumenPapelPacking, "Sin imprimir en el packing (1): la factura NV3 (pedido 3, envío 250999)");
        }

        [TestMethod]
        public async Task RutaPropia_NoMiraElPacking()
        {
            var ok = (OkNegotiatedContentResult<FacturarRutasResponseDTO>)await controller.FacturarRutas(new FacturarRutasRequestDTO { TipoRuta = "PROPIA" });

            A.CallTo(() => marcas.LeerDesde(A<DateTime>._)).MustNotHaveHappened();
            Assert.IsNull(ok.Content.ResumenPapelPacking);
        }

        [TestMethod]
        public async Task LoQueSaleEnLaTanda_LlevaSuEnvioParaLocalizarLaCaja()
        {
            var response = new FacturarRutasResponseDTO();
            response.Facturas.Add(new FacturaCreadaDTO { NumeroPedido = 929914, NumeroFactura = "NV2617986", DatosImpresion = new DocumentoParaImprimir { NumeroCopias = 1 } });
            response.Facturas.Add(new FacturaCreadaDTO { NumeroPedido = 929999, NumeroFactura = "NV2617999", DatosImpresion = new DocumentoParaImprimir { NumeroCopias = 0 } });
            response.Albaranes.Add(new AlbaranCreadoDTO { NumeroPedido = 929000, NumeroAlbaran = 777, DatosImpresion = new DocumentoParaImprimir { NumeroCopias = 1 } });
            Marcas();

            await controller.AnotarPapelDelPacking(response, "AGENCIA");

            Assert.AreEqual(251001, response.Facturas[0].Envio);
            Assert.IsNull(response.Facturas[1].Envio, "sin papel no se busca su envío");
            CollectionAssert.AreEquivalent(new[] { 929914, 929000 }, pedidosPreguntados);
            Assert.AreEqual("En papel en esta facturación (2): la factura NV2617986 (pedido 929914, envío 251001); el albarán 777 (pedido 929000, sin envío).",
                response.ResumenPapelPacking);
        }

        [TestMethod]
        public async Task SiFallaLaLectura_LaFacturacionSigue()
        {
            A.CallTo(() => marcas.LeerDesde(A<DateTime>._)).ThrowsAsync(new InvalidOperationException("sin BD"));
            var response = new FacturarRutasResponseDTO();

            await controller.AnotarPapelDelPacking(response, "AGENCIA");

            Assert.AreEqual(0, response.ImpresosEnPacking.Count);
        }

        [TestMethod]
        public void Resumen_UnaSolaYConAlbaranes()
        {
            var una = new FacturarRutasResponseDTO();
            una.ImpresosEnPacking.Add(Marca(1, "NV1", EstadosDocumentoEnPapel.IMPRESO));
            var mezcla = new FacturarRutasResponseDTO();
            mezcla.ImpresosEnPacking.Add(Marca(1, "NV1", EstadosDocumentoEnPapel.IMPRESO));
            mezcla.ImpresosEnPacking.Add(new DocumentoEnPapelDTO { Pedido = 2, Tipo = TiposDocumentoEnPapel.ALBARAN, Documento = "7", Estado = EstadosDocumentoEnPapel.IMPRESO });

            Assert.AreEqual("1 factura en papel ya impresa en el packing.", FacturacionRutasController.ResumenPapelPacking(una));
            Assert.AreEqual("2 documentos en papel ya impresos en el packing.", FacturacionRutasController.ResumenPapelPacking(mezcla));
            Assert.IsNull(FacturacionRutasController.ResumenPapelPacking(new FacturarRutasResponseDTO()));
        }
    }
}
