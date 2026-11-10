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

        private DocumentosEnPapelDelEnvio Nucleo() => new DocumentosEnPapelDelEnvio(pasos, impresora, ex => registrados.Add(ex));

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
            Impresora = m.Impresora, Mensaje = m.Mensaje, Fecha = m.Fecha
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
        public async Task RutaPropia_SeFacturaEImprimeEnElPacking_ComoAntesDel828()
        {
            // Carlos 10/11/26: si no se factura aquí y nadie pasa Facturar rutas propia antes de cargar, el cobro en efectivo al
            // entregar liquida otra deuda o queda a cuenta. Las dos copias salen en el packing, como antes del #828.
            A.CallTo(() => pasos.RutaDelPedido("1", PEDIDO)).Returns("AT");
            var respuesta = ConFactura(PEDIDO, "NV2617986", "HP");
            respuesta.Facturas.Single().DatosImpresion.NumeroCopias = 2;
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(respuesta);

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(PEDIDO, resultado.Respuestas.Single().Key);
            Assert.AreEqual(2, resultado.Respuesta.Facturas.Single().DatosImpresion.NumeroCopias);
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, resultado.Documentos.Single().Estado);
            Assert.IsFalse(resultado.Avisos.Any(a => a.Contains("no se factura al imprimir la etiqueta")), string.Join(" | ", resultado.Avisos));
        }

        [TestMethod]
        public async Task RutaPropia_NotaDeRepartoPropio_SigueSinProcesarse()
        {
            A.CallTo(() => pasos.RutaDelPedido("1", PEDIDO)).Returns("AT");
            A.CallTo(() => pasos.EsNotaDeRepartoPropio("1", PEDIDO)).Returns(true);

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
            Assert.IsTrue(resultado.Avisos.Single().Contains("la procesa Facturar rutas"));
        }

        [TestMethod]
        public void TiposDeRuta_SoloLaPropiaNoCuentaElPapelDelPackingEnFacturarRutas()
        {
            Assert.IsTrue(new RutaAgencia().CuentaPapelDelPackingEnFacturarRutas);
            Assert.IsFalse(new RutaPropia().CuentaPapelDelPackingEnFacturarRutas);
            Assert.IsTrue(new RutaAlmacen().CuentaPapelDelPackingEnFacturarRutas);
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
        public async Task NoMiraLaRuta_SeFacturaAunqueNoSePuedaLeer()
        {
            A.CallTo(() => pasos.RutaDelPedido("1", PEDIDO)).Throws(new InvalidOperationException("sin BD"));

            await Nucleo().Procesar(Peticion());

            A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly();
            A.CallTo(() => pasos.RutaDelPedido(A<string>._, A<int>._)).MustNotHaveHappened();
            Assert.AreEqual(0, registrados.Count);
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
            A.CallTo(() => pasos.LeerMarcas("1", PEDIDO)).ReturnsLazily(() => Task.FromResult(new List<DocumentoEnPapelDTO>
            {
                new DocumentoEnPapelDTO { Empresa = "1", Pedido = PEDIDO, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = "NV2617986", Estado = EstadosDocumentoEnPapel.PENDIENTE },
                new DocumentoEnPapelDTO { Empresa = "1", Pedido = PEDIDO, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = "NV2617987", Estado = EstadosDocumentoEnPapel.PENDIENTE }
            }));
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

        // ------------------------------------------------------------ S2: varios pedidos de la entrega

        [TestMethod]
        public async Task VariosPedidos_UnaFacturaPorPedido_YTodoJuntoParaNesto()
        {
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(ConFactura(PEDIDO, "NV1", "HP"));
            A.CallTo(() => pasos.Facturar("1", OTRO)).Returns(ConFactura(OTRO, "NV2", "HP"));

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion(false, PEDIDO, OTRO, PEDIDO));

            A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly();
            A.CallTo(() => pasos.Facturar("1", OTRO)).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { PEDIDO, OTRO }, resultado.Respuestas.Select(r => r.Key).ToArray());
            CollectionAssert.AreEqual(new[] { "NV1", "NV2" }, resultado.Respuesta.Facturas.Select(f => f.NumeroFactura).ToArray());
            CollectionAssert.AreEqual(new[] { PEDIDO, OTRO }, resultado.Documentos.Select(d => d.Pedido).ToArray());
        }

        // ------------------------------------------------------------ S3: reintento sin volver a facturar

        private void Pendiente(int pedido, string numero)
            => A.CallTo(() => pasos.LeerMarcas("1", pedido)).ReturnsLazily(() => Task.FromResult(new List<DocumentoEnPapelDTO>
            {
                new DocumentoEnPapelDTO { Empresa = "1", Pedido = pedido, Envio = ENVIO, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = numero,
                    Estado = EstadosDocumentoEnPapel.PENDIENTE, Mensaje = "atasco" }
            }));

        private void Regenera(int pedido, string numero, int copias = 1)
        {
            var documentos = new DocumentosImpresionPedidoDTO();
            documentos.Facturas.Add(new FacturaCreadaDTO
            {
                Empresa = "1", NumeroPedido = pedido, NumeroFactura = numero,
                DatosImpresion = new DocumentoParaImprimir { BytesPDF = new byte[] { 1 }, NumeroCopias = copias, TipoBandeja = TipoBandejaImpresion.Lower }
            });
            A.CallTo(() => pasos.Regenerar("1", pedido, TiposDocumentoEnPapel.FACTURA, numero)).Returns(documentos);
        }

        private void LaImpresoraImprimeEn(string nombre)
            => A.CallTo(() => impresora.Imprimir(A<List<DocumentoAImprimirEnServidor>>._, A<string>._))
                .Invokes((List<DocumentoAImprimirEnServidor> docs, string _) => docs.ForEach(d =>
                {
                    if (nombre == null)
                    {
                        d.Aviso = $"No se ha podido imprimir {d.Descripcion} en «HP»: sigue el atasco. Imprímalo desde Nesto.";
                    }
                    else
                    {
                        d.Datos.ImpresoEnServidor = nombre;
                    }
                }));

        [TestMethod]
        public async Task Pendiente_RepetirLaEtiqueta_ReimprimeSinVolverAFacturarlo()
        {
            Pendiente(PEDIDO, "NV2617986");
            Regenera(PEDIDO, "NV2617986");
            LaImpresoraImprimeEn("HP LaserJet Algete");

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion(reimpresion: true));

            // Lo pendiente se reimprime con su PDF regenerado; FacturarPedido se llama igual por si hay algo nuevo (aquí, nada)
            A.CallTo(() => pasos.Regenerar("1", PEDIDO, TiposDocumentoEnPapel.FACTURA, "NV2617986")).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly());
            A.CallTo(() => impresora.Imprimir(A<List<DocumentoAImprimirEnServidor>>.That.Matches(l => l.Count == 1), "NUEVAVISION\\Andre")).MustHaveHappenedOnceExactly();
            DocumentoEnPapelDTO documento = resultado.Documentos.Single();
            Assert.IsTrue(documento.Reimpreso);
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, documento.Estado);
            Assert.AreEqual("HP LaserJet Algete", documento.Impresora);
            Assert.IsNull(documento.Mensaje);
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, marcadas.Single().Estado);
            Assert.IsTrue(resultado.Avisos.Single().Contains("sin volver a facturar"));
        }

        [TestMethod]
        public async Task Pendiente_LaImpresoraSigueFallando_SigueEnPendienteConElMotivo()
        {
            Pendiente(PEDIDO, "NV2617986");
            Regenera(PEDIDO, "NV2617986");
            LaImpresoraImprimeEn(null);

            ResultadoDocumentosEnPapel resultado = await Nucleo().ReimprimirPendientes(Peticion(reimpresion: true));

            Assert.IsTrue(resultado.HayPendientes);
            StringAssert.Contains(marcadas.Single().Mensaje, "sigue el atasco");
            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Pendiente_QueYaNoLlevaPapel_SeDaPorImpresoYNoSeQuedaPendiente()
        {
            Pendiente(PEDIDO, "NV2617986");
            Regenera(PEDIDO, "NV2617986", copias: 0);

            ResultadoDocumentosEnPapel resultado = await Nucleo().ReimprimirPendientes(Peticion(reimpresion: true));

            A.CallTo(() => impresora.Imprimir(A<List<DocumentoAImprimirEnServidor>>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, marcadas.Single().Estado);
            Assert.IsTrue(resultado.Avisos.Single().Contains("ya no lleva documento en papel"));
        }

        [TestMethod]
        public async Task Pendiente_EnNesto_DevuelveElPdfParaImprimirEnLocal()
        {
            A.CallTo(() => impresora.Aplicacion).Returns(AplicacionesDocumentoEnPapel.NESTO);
            Pendiente(PEDIDO, "NV2617986");
            Regenera(PEDIDO, "NV2617986");

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion(reimpresion: true));

            Assert.AreEqual("NV2617986", resultado.Respuesta.Facturas.Single().NumeroFactura);
            Assert.IsNotNull(resultado.Respuesta.Facturas.Single().DatosImpresion.BytesPDF);
            Assert.AreEqual(EstadosDocumentoEnPapel.PENDIENTE, resultado.Documentos.Single().Estado);
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task YaImpresoEnElPacking_RepetirLaEtiqueta_NoLoVuelveAImprimirYLoDice()
        {
            A.CallTo(() => pasos.LeerMarcas("1", PEDIDO)).ReturnsLazily(() => Task.FromResult(new List<DocumentoEnPapelDTO>
            {
                new DocumentoEnPapelDTO { Empresa = "1", Pedido = PEDIDO, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = "NV2617986",
                    Estado = EstadosDocumentoEnPapel.IMPRESO, Impresora = "HP" }
            }));

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion(reimpresion: true));

            Assert.IsTrue(resultado.Documentos.Single().YaEstabaImpreso);
            Assert.AreEqual("La factura NV2617986 ya salió en el packing (en «HP»): no se vuelve a imprimir.", resultado.Avisos.Single());
            A.CallTo(() => pasos.Regenerar(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => pasos.Marcar(A<DocumentoEnPapelDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ReimprimirPendientes_SinNadaPendiente_LoDiceYNoFactura()
        {
            ResultadoDocumentosEnPapel resultado = await Nucleo().ReimprimirPendientes(Peticion(true, PEDIDO, OTRO));

            Assert.AreEqual($"Los pedidos {PEDIDO}, {OTRO} no tienen ningún documento en papel pendiente de imprimir.", resultado.Avisos.Single());
            A.CallTo(() => pasos.Facturar(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ReimprimirPendientes_SiNoSePuedeRegenerar_SeAvisa()
        {
            Pendiente(PEDIDO, "NV2617986");
            A.CallTo(() => pasos.Regenerar(A<string>._, A<int>._, A<string>._, A<string>._)).ThrowsAsync(new InvalidOperationException("No se encontró la factura"));

            ResultadoDocumentosEnPapel resultado = await Nucleo().ReimprimirPendientes(Peticion(true));

            Assert.IsTrue(resultado.HayPendientes);
            Assert.IsTrue(resultado.Avisos.Single().Contains("No se encontró la factura"));
            Assert.AreEqual(1, registrados.Count);
        }

        // ------------------------------------------------------------ Revisión del 10/11/26: lo pendiente ya no impide facturar lo nuevo

        private static readonly DateTime HOY = new DateTime(2026, 11, 10);

        private DocumentosEnPapelDelEnvio NucleoDeHoy()
        {
            DocumentosEnPapelDelEnvio nucleo = Nucleo();
            nucleo.Hoy = () => HOY;
            return nucleo;
        }

        private void Marcas(int pedido, params DocumentoEnPapelDTO[] marcas)
            => A.CallTo(() => pasos.LeerMarcas("1", pedido)).ReturnsLazily(() => Task.FromResult(marcas.Select(Copia).ToList()));

        private static DocumentoEnPapelDTO MarcaDe(string tipo, string numero, string estado, int? envio, DateTime? fecha, int pedido = PEDIDO)
            => new DocumentoEnPapelDTO { Empresa = "1", Pedido = pedido, Envio = envio, Tipo = tipo, Documento = numero, Estado = estado, Fecha = fecha, Impresora = "HP" };

        [TestMethod]
        public async Task Regresion_PendienteDeOtroEnvioDeDiasAtras_NoSeReimprimeYSeFacturaLoDeHoy()
        {
            // El fallo: una marca pendiente vieja (Nesto sin «imprimir documento», confirmación caída…) hacía `continue` y no se facturaba
            Marcas(PEDIDO, MarcaDe(TiposDocumentoEnPapel.FACTURA, "NV2600001", EstadosDocumentoEnPapel.PENDIENTE, 4000, HOY.AddDays(-3)));
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(ConFactura(PEDIDO, "NV2617986", "HP LaserJet Algete"));

            ResultadoDocumentosEnPapel resultado = await NucleoDeHoy().Procesar(Peticion());

            A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly();
            A.CallTo(() => pasos.Regenerar(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => impresora.Imprimir(A<List<DocumentoAImprimirEnServidor>>._, A<string>._)).MustNotHaveHappened();
            DocumentoEnPapelDTO documento = resultado.Documentos.Single();
            Assert.AreEqual("NV2617986", documento.Documento, "Solo lo de hoy va en esta caja");
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, documento.Estado);
            Assert.AreEqual("La factura NV2600001 del pedido 929914 quedó sin imprimir en el packing de otro envío (envío 4000, 07/11): " +
                "no se imprime con esta etiqueta. Si aún hace falta, imprima el documento desde Nesto.", resultado.Avisos.Single());
            Assert.IsFalse(marcadas.Any(m => m.Documento == "NV2600001"), "La marca vieja no se toca");
        }

        [TestMethod]
        public async Task PendienteSinEnvioDeOtroDia_NoSeReimprime_PendienteSinEnvioDeHoy_Si()
        {
            Marcas(PEDIDO,
                MarcaDe(TiposDocumentoEnPapel.FACTURA, "NV2600001", EstadosDocumentoEnPapel.PENDIENTE, null, HOY.AddDays(-1).AddHours(18)),
                MarcaDe(TiposDocumentoEnPapel.FACTURA, "NV2600002", EstadosDocumentoEnPapel.PENDIENTE, null, HOY.AddHours(9)));
            Regenera(PEDIDO, "NV2600002");
            LaImpresoraImprimeEn("HP");

            ResultadoDocumentosEnPapel resultado = await NucleoDeHoy().Procesar(Peticion());

            A.CallTo(() => pasos.Regenerar("1", PEDIDO, TiposDocumentoEnPapel.FACTURA, "NV2600002")).MustHaveHappenedOnceExactly();
            A.CallTo(() => pasos.Regenerar("1", PEDIDO, TiposDocumentoEnPapel.FACTURA, "NV2600001")).MustNotHaveHappened();
            Assert.AreEqual("NV2600002", resultado.Documentos.Single().Documento);
        }

        [TestMethod]
        public async Task PendienteDelMismoEnvio_SeReimprimeYAdemasSeFacturaLoNuevo()
        {
            Marcas(PEDIDO, MarcaDe(TiposDocumentoEnPapel.FACTURA, "NV2617986", EstadosDocumentoEnPapel.PENDIENTE, ENVIO, HOY.AddDays(-2)));
            Regenera(PEDIDO, "NV2617986");
            LaImpresoraImprimeEn("HP");
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(ConFactura(PEDIDO, "NV2617999", "HP"));

            ResultadoDocumentosEnPapel resultado = await NucleoDeHoy().Procesar(Peticion(reimpresion: true));

            CollectionAssert.AreEqual(new[] { "NV2617986", "NV2617999" }, resultado.Documentos.Select(d => d.Documento).ToArray());
            Assert.IsTrue(resultado.Documentos[0].Reimpreso);
            Assert.IsTrue(resultado.Documentos.All(d => d.Estado == EstadosDocumentoEnPapel.IMPRESO));
            CollectionAssert.AreEqual(new[] { "NV2617986", "NV2617999" }, resultado.Respuesta.Facturas.Select(f => f.NumeroFactura).ToArray());
            Assert.AreEqual(PEDIDO, resultado.Respuestas.Single().Key);
        }

        [TestMethod]
        public async Task SoloReimpresion_SinNadaNuevo_NoDiceSinLineasParaFacturar()
        {
            A.CallTo(() => impresora.Aplicacion).Returns(AplicacionesDocumentoEnPapel.NESTO);
            Pendiente(PEDIDO, "NV2617986");
            Regenera(PEDIDO, "NV2617986");
            var sinNada = new FacturarRutasResponseDTO();
            sinNada.PedidosConErrores.Add(new PedidoConErrorDTO
            {
                NumeroPedido = PEDIDO, TipoError = "Sin líneas para facturar", MensajeError = "El pedido 929914 no tiene líneas para facturar…"
            });
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(sinNada);

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.IsFalse(resultado.Respuesta.PedidosConErrores.Any(), "Nesto lo enseñaba como error grave");
            Assert.IsFalse(resultado.Respuestas.Single().Value.PedidosConErrores.Any());
            Assert.AreEqual("NV2617986", resultado.Respuesta.Facturas.Single().NumeroFactura);
        }

        [TestMethod]
        public async Task SinReimpresion_SinLineasParaFacturar_SeSigueDiciendo()
        {
            var sinNada = new FacturarRutasResponseDTO();
            sinNada.PedidosConErrores.Add(new PedidoConErrorDTO { NumeroPedido = PEDIDO, TipoError = "Sin líneas para facturar", MensajeError = "…" });
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(sinNada);

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.AreEqual(1, resultado.Respuesta.PedidosConErrores.Count);
        }

        [TestMethod]
        public async Task ReimprimirPendientes_LoDeOtroEnvioNoSeImprime()
        {
            Marcas(PEDIDO, MarcaDe(TiposDocumentoEnPapel.FACTURA, "NV2600001", EstadosDocumentoEnPapel.PENDIENTE, 4000, HOY.AddDays(-3)));

            ResultadoDocumentosEnPapel resultado = await NucleoDeHoy().ReimprimirPendientes(Peticion(true));

            A.CallTo(() => pasos.Regenerar(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(2, resultado.Avisos.Count);
            StringAssert.Contains(resultado.Avisos[0], "de otro envío");
            Assert.AreEqual($"El pedido {PEDIDO} no tiene ningún documento en papel pendiente de imprimir.", resultado.Avisos[1]);
        }

        [TestMethod]
        public async Task Nesto_SoloConfirmaDocumentosDelPedido()
        {
            Marcas(PEDIDO, MarcaDe(TiposDocumentoEnPapel.FACTURA, "NV2617986", EstadosDocumentoEnPapel.PENDIENTE, ENVIO, HOY));

            bool ajeno = await Nucleo().MarcarImpresionLocal(new ImpresionDocumentoEnPapelDTO
            {
                Empresa = "1", Pedido = PEDIDO, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = "NV2699999", Impreso = true
            });
            bool otroTipo = await Nucleo().MarcarImpresionLocal(new ImpresionDocumentoEnPapelDTO
            {
                Empresa = "1", Pedido = PEDIDO, Tipo = TiposDocumentoEnPapel.ALBARAN, Documento = "NV2617986", Impreso = true
            });
            bool suyo = await Nucleo().MarcarImpresionLocal(new ImpresionDocumentoEnPapelDTO
            {
                Empresa = "1", Pedido = PEDIDO, Tipo = TiposDocumentoEnPapel.FACTURA, Documento = "nv2617986", Impreso = true
            });

            Assert.IsFalse(ajeno);
            Assert.IsFalse(otroTipo);
            Assert.IsTrue(suyo);
            Assert.AreEqual("nv2617986", marcadas.Single().Documento);
        }

        // ------------------------------------------------------------ La carta del cheque regalo (Carlos 10/11/26)

        private static CartaChequeRegaloDTO Carta(string factura, string impresoEn)
            => new CartaChequeRegaloDTO
            {
                Empresa = "3", NumeroPedido = PEDIDO, NumeroFactura = factura,
                Descripcion = NestoAPI.Infraestructure.ChequesRegalo.CartasChequeRegalo.Descripcion(factura),
                DatosImpresion = new DocumentoParaImprimir { BytesPDF = new byte[] { 2 }, NumeroCopias = 1, ImpresoEnServidor = impresoEn }
            };

        [TestMethod]
        public async Task Carta_FacturaImpresaYCartaNo_CadaUnaConSuMarcaYSuMotivo()
        {
            FacturarRutasResponseDTO respuesta = ConFactura(PEDIDO, "NV2617986", "HP");
            respuesta.CartasChequeRegalo.Add(Carta("NV2617986", null));
            respuesta.PedidosConErrores.Add(new PedidoConErrorDTO
            {
                NumeroPedido = PEDIDO, TipoError = ImpresionDocumentosServidor.TIPO_ERROR, Severidad = NivelSeveridad.Warning,
                MensajeError = "No se ha podido imprimir la carta del cheque regalo de la factura NV2617986 en «HP»: sin papel. Imprímalo desde Nesto."
            });
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(respuesta);

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.AreEqual(2, marcadas.Count);
            Assert.AreEqual(TiposDocumentoEnPapel.FACTURA, marcadas[0].Tipo);
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, marcadas[0].Estado);
            Assert.AreEqual(TiposDocumentoEnPapel.CARTA_CHEQUE, marcadas[1].Tipo);
            Assert.AreEqual("NV2617986", marcadas[1].Documento);
            Assert.AreEqual("1", marcadas[1].Empresa, "La marca va con la empresa del pedido, no con la de la factura");
            Assert.AreEqual(EstadosDocumentoEnPapel.PENDIENTE, marcadas[1].Estado);
            StringAssert.Contains(marcadas[1].Mensaje, "carta del cheque");
            Assert.AreEqual("la carta del cheque regalo de la factura NV2617986", resultado.Documentos[1].Descripcion);
        }

        [TestMethod]
        public async Task Carta_LaFacturaFallaYLaCartaSale_LaFacturaNoSeQuedaConElMotivoDeLaCarta()
        {
            FacturarRutasResponseDTO respuesta = ConFactura(PEDIDO, "NV2617986");
            respuesta.CartasChequeRegalo.Add(Carta("NV2617986", "HP"));
            respuesta.PedidosConErrores.Add(new PedidoConErrorDTO
            {
                NumeroPedido = PEDIDO, TipoError = ImpresionDocumentosServidor.TIPO_ERROR, Severidad = NivelSeveridad.Warning,
                MensajeError = "No se ha podido imprimir la factura NV2617986 en «HP»: atasco. Imprímalo desde Nesto."
            });
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(respuesta);

            await Nucleo().Procesar(Peticion());

            Assert.AreEqual(EstadosDocumentoEnPapel.PENDIENTE, marcadas[0].Estado);
            StringAssert.Contains(marcadas[0].Mensaje, "atasco");
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, marcadas[1].Estado);
        }

        [TestMethod]
        public async Task Carta_EnNesto_NoSeMarca_ComoAntes()
        {
            A.CallTo(() => impresora.Aplicacion).Returns(AplicacionesDocumentoEnPapel.NESTO);
            FacturarRutasResponseDTO respuesta = ConFactura(PEDIDO, "NV2617986");
            respuesta.CartasChequeRegalo.Add(Carta("NV2617986", null));
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).Returns(respuesta);

            ResultadoDocumentosEnPapel resultado = await Nucleo().Procesar(Peticion());

            Assert.AreEqual(TiposDocumentoEnPapel.FACTURA, resultado.Documentos.Single().Tipo);
            Assert.AreEqual(TiposDocumentoEnPapel.FACTURA, marcadas.Single().Tipo);
        }

        [TestMethod]
        public async Task Carta_SoloLaCartaPendiente_RepetirF8ReimprimeSoloLaCarta()
        {
            Marcas(PEDIDO,
                MarcaDe(TiposDocumentoEnPapel.FACTURA, "NV2617986", EstadosDocumentoEnPapel.IMPRESO, ENVIO, HOY),
                MarcaDe(TiposDocumentoEnPapel.CARTA_CHEQUE, "NV2617986", EstadosDocumentoEnPapel.PENDIENTE, ENVIO, HOY));
            A.CallTo(() => pasos.RegenerarCartas("1", PEDIDO, "NV2617986")).Returns(new List<CartaChequeRegaloDTO> { Carta("NV2617986", null) });
            LaImpresoraImprimeEn("HP LaserJet Algete");

            ResultadoDocumentosEnPapel resultado = await NucleoDeHoy().Procesar(Peticion(reimpresion: true));

            A.CallTo(() => pasos.Regenerar(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => impresora.Imprimir(A<List<DocumentoAImprimirEnServidor>>.That.Matches(l => l.Count == 1
                && l[0].Descripcion == "la carta del cheque regalo de la factura NV2617986"), A<string>._)).MustHaveHappenedOnceExactly();
            DocumentoEnPapelDTO carta = resultado.Documentos.Single(d => d.Tipo == TiposDocumentoEnPapel.CARTA_CHEQUE);
            Assert.IsTrue(carta.Reimpreso);
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, carta.Estado);
            Assert.IsTrue(resultado.Documentos.Single(d => d.Tipo == TiposDocumentoEnPapel.FACTURA).YaEstabaImpreso, "La factura ya había salido: no se vuelve a imprimir");
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, marcadas.Single().Estado);
            Assert.AreEqual(TiposDocumentoEnPapel.CARTA_CHEQUE, marcadas.Single().Tipo);
            Assert.AreEqual(1, resultado.Respuesta.CartasChequeRegalo.Count);
        }

        [TestMethod]
        public async Task Carta_NingunaSalio_ElReintentoImprimeLasDosEnOrden()
        {
            Marcas(PEDIDO,
                MarcaDe(TiposDocumentoEnPapel.FACTURA, "NV2617986", EstadosDocumentoEnPapel.PENDIENTE, ENVIO, HOY),
                MarcaDe(TiposDocumentoEnPapel.CARTA_CHEQUE, "NV2617986", EstadosDocumentoEnPapel.PENDIENTE, ENVIO, HOY));
            Regenera(PEDIDO, "NV2617986");
            A.CallTo(() => pasos.RegenerarCartas("1", PEDIDO, "NV2617986")).Returns(new List<CartaChequeRegaloDTO> { Carta("NV2617986", null) });
            LaImpresoraImprimeEn("HP");

            ResultadoDocumentosEnPapel resultado = await NucleoDeHoy().ReimprimirPendientes(Peticion(true));

            A.CallTo(() => pasos.Regenerar("1", PEDIDO, TiposDocumentoEnPapel.FACTURA, "NV2617986")).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => pasos.RegenerarCartas("1", PEDIDO, "NV2617986")).MustHaveHappenedOnceExactly());
            CollectionAssert.AreEqual(new[] { TiposDocumentoEnPapel.FACTURA, TiposDocumentoEnPapel.CARTA_CHEQUE }, resultado.Documentos.Select(d => d.Tipo).ToArray());
            Assert.IsTrue(resultado.Documentos.All(d => d.Estado == EstadosDocumentoEnPapel.IMPRESO && d.Reimpreso));
        }

        [TestMethod]
        public async Task Carta_PendienteSinChequeEnVigor_DejaDeEstarPendiente()
        {
            Marcas(PEDIDO, MarcaDe(TiposDocumentoEnPapel.CARTA_CHEQUE, "NV2617986", EstadosDocumentoEnPapel.PENDIENTE, ENVIO, HOY));
            A.CallTo(() => pasos.RegenerarCartas("1", PEDIDO, "NV2617986")).Returns(new List<CartaChequeRegaloDTO>());

            ResultadoDocumentosEnPapel resultado = await NucleoDeHoy().ReimprimirPendientes(Peticion(true));

            A.CallTo(() => impresora.Imprimir(A<List<DocumentoAImprimirEnServidor>>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(EstadosDocumentoEnPapel.IMPRESO, marcadas.Single().Estado);
            StringAssert.Contains(resultado.Avisos.Single(), "ya no tiene cheque regalo en vigor");
        }

        [TestMethod]
        public async Task Carta_PendienteEnNesto_NoSeToca()
        {
            A.CallTo(() => impresora.Aplicacion).Returns(AplicacionesDocumentoEnPapel.NESTO);
            Marcas(PEDIDO, MarcaDe(TiposDocumentoEnPapel.CARTA_CHEQUE, "NV2617986", EstadosDocumentoEnPapel.PENDIENTE, ENVIO, HOY));

            ResultadoDocumentosEnPapel resultado = await NucleoDeHoy().Procesar(Peticion(reimpresion: true));

            A.CallTo(() => pasos.RegenerarCartas(A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
            Assert.IsFalse(resultado.Documentos.Any());
            A.CallTo(() => pasos.Facturar("1", PEDIDO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Carta_VariasCartasDeUnaFactura_UnaMarca_PendienteSiAlgunaNoSale()
        {
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.CartasChequeRegalo.Add(Carta("NV1", "HP"));
            respuesta.CartasChequeRegalo.Add(Carta("NV1", null));

            var enPapel = DocumentosEnPapelDelEnvio.EnPapel(respuesta, "1", PEDIDO, ENVIO, conCartas: true);
            var sinCartas = DocumentosEnPapelDelEnvio.EnPapel(respuesta, "1", PEDIDO, ENVIO);

            Assert.AreEqual(1, enPapel.Count);
            Assert.IsNull(enPapel.Single().Key.DatosImpresion.ImpresoEnServidor);
            Assert.AreEqual(0, sinCartas.Count);
        }

        [TestMethod]
        public void Repositorio_LaMarcaDeLaCartaSoloSeGuardaSiLaCheckLaAdmite()
        {
            StringAssert.Contains(RepositorioDocumentosPapelPacking.SQL_GUARDAR, "@tipo <> 'CartaCheque'");
            StringAssert.Contains(RepositorioDocumentosPapelPacking.SQL_GUARDAR, "name = N'CK_DocumentosPapelPacking_Tipo'");
            StringAssert.Contains(RepositorioDocumentosPapelPacking.SQL_GUARDAR, "definition LIKE N'%''CartaCheque''%'");
            Assert.IsTrue(TiposDocumentoEnPapel.CARTA_CHEQUE.Length <= 12, "Tipo es varchar(12)");
        }

        [TestMethod]
        public void Carta_AvisosParaReimprimir_LosDeLaEmpresaDelPedidoSiElNumeroSaleEnDos()
        {
            var cheques = new List<NestoAPI.Infraestructure.ChequesRegalo.ChequeRegaloSinAviso>
            {
                new NestoAPI.Infraestructure.ChequesRegalo.ChequeRegaloSinAviso { Id = 1, EmpresaFactura = "3", FacturaOrigen = "NV1", Codigo = "C3", ImporteBase = 50 },
                new NestoAPI.Infraestructure.ChequesRegalo.ChequeRegaloSinAviso { Id = 2, EmpresaFactura = "1", FacturaOrigen = "NV1", Codigo = "C1", ImporteBase = 50 }
            };

            var deLa1 = NestoAPI.Infraestructure.ChequesRegalo.CartasChequeRegalo.AvisosParaReimprimir(cheques, "1", "Pepita");
            var deLa5 = NestoAPI.Infraestructure.ChequesRegalo.CartasChequeRegalo.AvisosParaReimprimir(cheques, "5", null);

            Assert.AreEqual(2, deLa1.Single().IdCheque);
            Assert.AreEqual("Pepita", deLa1.Single().NombreDePila);
            Assert.AreEqual("C1", deLa1.Single().Campana.Codigo);
            Assert.AreEqual(1, deLa5.Single().IdCheque);
        }
    }
}
