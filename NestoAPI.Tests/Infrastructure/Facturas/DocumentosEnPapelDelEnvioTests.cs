using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Facturas
{
    /// <summary>
    /// NestoAPI#828: el núcleo del papel del packing (DocumentosEnPapelDelEnvio), el mismo para la etiqueta de Ariadna y la de Nesto.
    /// </summary>
    [TestClass]
    public class DocumentosEnPapelDelEnvioTests
    {
        private const int PEDIDO = 929914;
        private const int OTRO = 929915;
        private const int ENVIO = 5001;
        private IPasosDocumentosEnPapel pasos;
        private IImpresoraPapelPacking impresora;
        private List<Exception> registrados;
        private List<DocumentoEnPapelDTO> marcadas;

        [TestInitialize]
        public void Preparar()
        {
            pasos = A.Fake<IPasosDocumentosEnPapel>();
            impresora = A.Fake<IImpresoraPapelPacking>();
            A.CallTo(() => impresora.Aplicacion).Returns(AplicacionesDocumentoEnPapel.ARIADNA);
            registrados = new List<Exception>();
            marcadas = new List<DocumentoEnPapelDTO>();
            A.CallTo(() => pasos.RutaDelPedido(A<string>._, A<int>._)).Returns("FW");
            A.CallTo(() => pasos.EsNotaDeRepartoPropio(A<string>._, A<int>._)).Returns(false);
            A.CallTo(() => pasos.LeerMarcas(A<string>._, A<int>._)).ReturnsLazily(() => Task.FromResult(new List<DocumentoEnPapelDTO>()));
            A.CallTo(() => pasos.Marcar(A<DocumentoEnPapelDTO>._, A<string>._)).Invokes((DocumentoEnPapelDTO m, string _) => marcadas.Add(Copia(m)))
                .Returns(Task.CompletedTask);
            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).ReturnsLazily(() => Task.FromResult(new FacturarRutasResponseDTO()));
        }

        private DocumentosEnPapelDelEnvio Nucleo() => new DocumentosEnPapelDelEnvio(pasos, impresora, ex => registrados.Add(ex))
        {
            TipoDeRuta = ruta => ruta == "AT" ? (ITipoRuta)new RutaPropia() : new RutaAgencia()
        };

        private static PeticionDocumentosEnPapel Peticion(bool reimpresion = false, params int[] pedidos)
            => new PeticionDocumentosEnPapel
            {
                Empresa = "1",
                Pedidos = (pedidos.Length == 0 ? new[] { PEDIDO } : pedidos).ToList(),
                Envio = ENVIO,
                Reimpresion = reimpresion,
                Usuario = "NUEVAVISION\\Andre"
            };

        private static DocumentoEnPapelDTO Copia(DocumentoEnPapelDTO m) => new DocumentoEnPapelDTO
        {
            Empresa = m.Empresa, Pedido = m.Pedido, Envio = m.Envio, Tipo = m.Tipo, Documento = m.Documento, Estado = m.Estado,
            Impresora = m.Impresora, Mensaje = m.Mensaje
        };

        private static FacturarRutasResponseDTO ConFactura(int pedido, string numero, string impresoEn = null, int copias = 1)
        {
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.Facturas.Add(new FacturaCreadaDTO
            {
                Empresa = "1",
                NumeroPedido = pedido,
                NumeroFactura = numero,
                DatosImpresion = copias < 0 ? null : new DocumentoParaImprimir
                {
                    BytesPDF = new byte[] { 1 },
                    NumeroCopias = copias,
                    TipoBandeja = TipoBandejaImpresion.Lower,
                    ImpresoEnServidor = impresoEn
                }
            });
            return respuesta;
        }

        // ------------------------------------------------------------ S1: núcleo + marca

        [TestMethod]
        public async Task FacturaConPapelImpresaEnElServidor_QuedaMarcadaImpresa()
        {
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(ConFactura(PEDIDO, "NV2617986", "HP LaserJet Algete"));

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            DocumentoEnPapelDTO documento = resultado.Documentos.Single();
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, documento.Estado);
            Assert.AreEqual("NV2617986", documento.Documento);
            Assert.AreEqual(TiposDocumentoEnPapel.FACTURA, documento.Tipo);
            Assert.AreEqual(ENVIO, documento.Envio);
            Assert.AreEqual("HP LaserJet Algete", documento.Impresora);
            Assert.IsFalse(resultado.HayPendientes);
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, marcadas.Single().Estado);
            A.CallTo(() => pasos.Marcar(A<DocumentoEnPapelDTO>._, AplicacionesDocumentoEnPapel.ARIADNA)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, resultado.Respuestas.Count);
        }

        [TestMethod]
        public async Task FacturaConPapelQueNoSale_QuedaPendienteConElMotivoDeLaImpresora()
        {
            FacturarRutasResponseDTO respuesta = ConFactura(PEDIDO, "NV2617986");
            respuesta.PedidosConErrores.Add(new PedidoConErrorDTO
            {
                NumeroPedido = PEDIDO,
                TipoError = ImpresionDocumentosServidor.TIPO_ERROR,
                MensajeError = "No se ha podido imprimir la factura NV2617986 en «HP»: sin papel. Imprímalo desde Nesto.",
                Severidad = NivelSeveridad.Warning
            });
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(respuesta);

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.IsTrue(resultado.HayPendientes);
            Assert.AreEqual(EstadosDocumentoEnPapel.PENDIENTE, marcadas.Single().Estado);
            StringAssert.Contains(marcadas.Single().Mensaje, "sin papel");
        }

        [TestMethod]
        public async Task FacturaSinPapel_NoSeMarcaNada()
        {
            // Sin la frase de papel (también las simplificadas): sin DatosImpresion o con 0 copias
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(ConFactura(PEDIDO, "NV2617986", copias: 0));

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.IsFalse(resultado.Documentos.Any());
            A.CallTo(() => pasos.Marcar(A<DocumentoEnPapelDTO>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(1, resultado.Respuestas.Count);
        }

        [TestMethod]
        public async Task AlbaranFisicoDeFinDeMes_TambienSeMarca()
        {
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.Albaranes.Add(new AlbaranCreadoDTO
            {
                Empresa = "1", NumeroPedido = PEDIDO, NumeroAlbaran = 777,
                DatosImpresion = new DocumentoParaImprimir { BytesPDF = new byte[] { 1 }, NumeroCopias = 1, ImpresoEnServidor = "HP" }
            });
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(respuesta);

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.AreEqual(TiposDocumentoEnPapel.ALBARAN, resultado.Documentos.Single().Tipo);
            Assert.AreEqual("777", resultado.Documentos.Single().Documento);
            Assert.AreEqual("el albarán 777", resultado.Documentos.Single().Descripcion);
        }

        [TestMethod]
        public async Task RutaPropia_NoSeFacturaAlHacerLaEtiqueta_LaFacturaFacturarRutasConSusDosCopias()
        {
            A.CallTo(() => pasos.RutaDelPedido("1", PEDIDO)).Returns("AT");

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
            Assert.IsTrue(resultado.Avisos.Single().Contains("no se factura al imprimir la etiqueta"));
            Assert.IsTrue(resultado.Avisos.Single().Contains("(AT)"));
            Assert.IsFalse(resultado.Respuestas.Any());
        }

        [TestMethod]
        public void TiposDeRuta_SoloLaPropiaNoImprimeAlCerrarElEnvio()
        {
            Assert.IsTrue(new RutaAgencia().ImprimeAlCerrarEnvio);
            Assert.IsFalse(new RutaPropia().ImprimeAlCerrarEnvio);
            Assert.IsTrue(new RutaAlmacen().ImprimeAlCerrarEnvio);
        }

        [TestMethod]
        public async Task NotaDeRepartoPropio_NoSeProcesa()
        {
            A.CallTo(() => pasos.EsNotaDeRepartoPropio("1", PEDIDO)).Returns(true);

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
            Assert.IsTrue(resultado.Avisos.Single().Contains("la procesa Facturar rutas"));
        }

        [TestMethod]
        public async Task SiNoSeSabeLaRuta_SeFacturaComoHastaAhora()
        {
            A.CallTo(() => pasos.RutaDelPedido("1", PEDIDO)).Throws(new InvalidOperationException("sin BD"));

            await Nucleo().Procesar(Peticion());

            A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, registrados.Count);
        }

        [TestMethod]
        public async Task FacturarFalla_SeAvisaYNoSeMarca()
        {
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).ThrowsAsync(new InvalidOperationException("Su usuario no tiene permiso para facturar."));

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.IsTrue(resultado.Avisos.Single().StartsWith($"No se ha podido facturar el pedido {PEDIDO}: Su usuario no tiene permiso"));
            A.CallTo(() => pasos.Marcar(A<DocumentoEnPapelDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiFallaLaMarca_NoRompeNada()
        {
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(ConFactura(PEDIDO, "NV2617986", "HP"));
            A.CallTo(() => pasos.Marcar(A<DocumentoEnPapelDTO>._, A<string>._)).ThrowsAsync(new InvalidOperationException("sin tabla"));

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, resultado.Documentos.Single().Estado);
            Assert.AreEqual(1, registrados.Count);
        }

        [TestMethod]
        public async Task Nesto_ElPapelQuedaPendienteHastaQueNestoConfirme()
        {
            A.CallTo(() => impresora.Aplicacion).Returns(AplicacionesDocumentoEnPapel.NESTO);
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(ConFactura(PEDIDO, "NV2617986"));

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.AreEqual(EstadosDocumentoEnPapel.PENDIENTE, resultado.Documentos.Single().Estado);
            Assert.AreEqual("Lo imprime Nesto.", resultado.Documentos.Single().Mensaje);
            Assert.IsNotNull(resultado.Respuesta.Facturas.Single().DatosImpresion, "Nesto necesita el PDF para imprimirlo en local");
            A.CallTo(() => pasos.Marcar(A<DocumentoEnPapelDTO>._, AplicacionesDocumentoEnPapel.NESTO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Nesto_ConfirmaLaImpresionEnLocal()
        {
            await Nucleo().MarcarImpresionLocal(new ImpresionDocumentoEnPapelDTO
            {
                Empresa = "1", Pedido = PEDIDO, Envio = ENVIO, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = " NV2617986 ", Impreso = true, Impresora = "HP Nesto"
            });
            await Nucleo().MarcarImpresionLocal(new ImpresionDocumentoEnPapelDTO
            {
                Empresa = "1", Pedido = PEDIDO, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = "NV2617987", Impreso = false
            });

            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, marcadas[0].Estado);
            Assert.AreEqual("NV2617986", marcadas[0].Documento);
            Assert.AreEqual(EstadosDocumentoEnPapel.PENDIENTE, marcadas[1].Estado);
            Assert.AreEqual("Nesto no lo ha podido imprimir.", marcadas[1].Mensaje);
        }

        [TestMethod]
        public void Repositorio_SinLaTablaNoLeeNiEscribe()
        {
            StringAssert.StartsWith(RepositorioDocumentosPapelPacking.SQL_LEER, "IF OBJECT_ID(N'dbo.DocumentosPapelPacking', N'U') IS NOT NULL");
            StringAssert.StartsWith(RepositorioDocumentosPapelPacking.SQL_LEER_DESDE, "IF OBJECT_ID(N'dbo.DocumentosPapelPacking', N'U') IS NOT NULL");
            StringAssert.StartsWith(RepositorioDocumentosPapelPacking.SQL_GUARDAR.Trim(), "IF OBJECT_ID(N'dbo.DocumentosPapelPacking', N'U') IS NOT NULL");
        }

        [TestMethod]
        public async Task Repositorio_SiLaBDFalla_DevuelveVacioYNoLanza()
        {
            var registrosRepo = new List<Exception>();
            var repositorio = new RepositorioDocumentosPapelPacking(() => throw new InvalidOperationException("sin BD"), ex => registrosRepo.Add(ex));

            List<DocumentoEnPapelDTO> leidas = await repositorio.Leer("1", PEDIDO);
            await repositorio.Guardar(new DocumentoEnPapelDTO { Pedido = PEDIDO, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = "NV1" }, "Ariadna", "Andre");
            List<DocumentoEnPapelDTO> desde = await repositorio.LeerDesde(DateTime.Today);

            Assert.AreEqual(0, leidas.Count);
            Assert.AreEqual(0, desde.Count);
            Assert.AreEqual(3, registrosRepo.Count);
        }
    }
}
