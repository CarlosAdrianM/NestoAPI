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

        // ------------------------------------------------------------ NestoAPI#828 (S5): la etiqueta de Nesto, con el mismo núcleo

        private IPasosDocumentosEnPapel PasosPapelFalsos(bool saltarPackingEsperado = false)
        {
            var pasos = A.Fake<IPasosDocumentosEnPapel>();
            A.CallTo(() => pasos.RutaDelPedido(A<string>._, A<int>._)).Returns("FW");
            A.CallTo(() => pasos.LeerMarcas(A<string>._, A<int>._)).ReturnsLazily(() => Task.FromResult(new List<DocumentoEnPapelDTO>()));
            controller.CrearPasosPapel = (usuario, saltar) =>
            {
                Assert.AreEqual(saltarPackingEsperado, saltar);
                return pasos;
            };
            return pasos;
        }

        [TestMethod]
        public async Task FacturarEtiqueta_FacturaConElNucleoYDevuelveElPapelParaImprimirEnNesto()
        {
            IPasosDocumentosEnPapel pasos = PasosPapelFalsos(saltarPackingEsperado: true);
            var facturado = new FacturarRutasResponseDTO();
            facturado.Facturas.Add(new FacturaCreadaDTO { Empresa = "1", NumeroPedido = 929914, NumeroFactura = "NV2617986",
                DatosImpresion = new DocumentoParaImprimir { BytesPDF = new byte[] { 1 }, NumeroCopias = 1 } });
            A.CallTo(() => pasos.Facturar("1", 929914)).Returns(facturado);

            var ok = (OkNegotiatedContentResult<FacturarEtiquetaResponseDTO>)await controller.FacturarEtiqueta(
                new FacturarEtiquetaRequestDTO { Empresa = "1", Pedido = 929914, Envio = 251001, SaltarPacking = true });

            Assert.AreEqual("NV2617986", ok.Content.Facturas.Single().NumeroFactura);
            Assert.IsNotNull(ok.Content.Facturas.Single().DatosImpresion.BytesPDF);
            DocumentoEnPapelDTO documento = ok.Content.DocumentosEnPapel.Single();
            Assert.AreEqual(EstadosDocumentoEnPapel.PENDIENTE, documento.Estado);
            Assert.AreEqual(251001, documento.Envio);
            A.CallTo(() => pasos.Marcar(A<DocumentoEnPapelDTO>._, AplicacionesDocumentoEnPapel.NESTO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FacturarEtiqueta_RutaPropia_NoFacturaYLoDice()
        {
            IPasosDocumentosEnPapel pasos = PasosPapelFalsos();
            A.CallTo(() => pasos.RutaDelPedido("1", 929914)).Returns("16");

            var ok = (OkNegotiatedContentResult<FacturarEtiquetaResponseDTO>)await controller.FacturarEtiqueta(
                new FacturarEtiquetaRequestDTO { Empresa = "1", Pedido = 929914 });

            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
            Assert.IsTrue(ok.Content.AvisosPapel.Single().Contains("no se factura al imprimir la etiqueta"));
        }

        [TestMethod]
        public async Task FacturarEtiqueta_SinPermiso_403()
        {
            var identidad = new ClaimsIdentity("TestAuth");
            identidad.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.TIENDAS + "x"));
            identidad.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\Otro"));
            controller.User = new ClaimsPrincipal(identidad);

            var resultado = await controller.FacturarEtiqueta(new FacturarEtiquetaRequestDTO { Empresa = "1", Pedido = 929914 });

            Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
        }

        [TestMethod]
        public async Task ImpresionDocumentoEnPapel_MarcaLoQueHaImpresoNesto()
        {
            IPasosDocumentosEnPapel pasos = PasosPapelFalsos();

            var resultado = await controller.ImpresionDocumentoEnPapel(new ImpresionDocumentoEnPapelDTO
            {
                Empresa = "1", Pedido = 929914, Envio = 251001, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = "NV2617986", Impreso = true, Impresora = "HP Agencias"
            });

            Assert.AreEqual(System.Net.HttpStatusCode.NoContent, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => pasos.Marcar(A<DocumentoEnPapelDTO>.That.Matches(m => m.Documento == "NV2617986" && m.Estado == EstadosDocumentoEnPapel.IMPRESO
                && m.Impresora == "HP Agencias"), AplicacionesDocumentoEnPapel.NESTO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ImpresionDocumentoEnPapel_SinDocumento_400()
        {
            var resultado = await controller.ImpresionDocumentoEnPapel(new ImpresionDocumentoEnPapelDTO { Pedido = 929914 });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
    }
}
