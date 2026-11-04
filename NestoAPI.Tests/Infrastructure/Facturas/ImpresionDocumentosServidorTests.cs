using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Facturas
{
    /// <summary>
    /// NestoAPI#806: la API imprime en el servidor los documentos en papel al facturar, por cualquier camino, en la impresora
    /// de documentos del usuario (ImpresoraDocumentos o la de «(defecto)»), detrás del interruptor Facturacion:ImprimirDocumentosEnServidor.
    /// </summary>
    [TestClass]
    public class ImpresionDocumentosServidorTests
    {
        private const string USUARIO = "NUEVAVISION\\Alfredo";
        private const string IMPRESORA_ALFREDO = "HP Administración Alfredo";
        private const string IMPRESORA_DEFECTO = "HP Administración";
        private static readonly byte[] PDF = { 0x25, 0x50, 0x44, 0x46 };

        private ILectorParametrosUsuario lector;
        private IImpresoraDocumentos impresora;
        private List<Exception> registrados;

        [TestInitialize]
        public void Setup()
        {
            lector = A.Fake<ILectorParametrosUsuario>();
            impresora = A.Fake<IImpresoraDocumentos>();
            registrados = new List<Exception>();
        }

        private ImpresionDocumentosServidor Servicio(bool activo)
            => new ImpresionDocumentosServidor(lector, impresora, () => activo, registrados.Add);

        private void ImpresoraDelUsuario(string usuario, string valor)
            => A.CallTo(() => lector.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO, usuario, ImpresionDocumentosServidor.PARAMETRO_IMPRESORA)).Returns(valor);

        private static DocumentoParaImprimir Datos(int copias, TipoBandejaImpresion bandeja)
            => new DocumentoParaImprimir { BytesPDF = PDF, NumeroCopias = copias, TipoBandeja = bandeja };

        // ------------------------------------------------------------ la impresora: usuario / defecto / ninguna

        [TestMethod]
        public void ElegirImpresora_ElUsuarioTieneLaSuya_LaSuya()
        {
            ImpresoraDelUsuario(USUARIO, " " + IMPRESORA_ALFREDO + " ");
            ImpresoraDelUsuario(ImpresionDocumentosServidor.USUARIO_DEFECTO, IMPRESORA_DEFECTO);

            Assert.AreEqual(IMPRESORA_ALFREDO, Servicio(true).ElegirImpresora(USUARIO));
        }

        [TestMethod]
        public void ElegirImpresora_ElUsuarioNoLaTieneOLaTieneVacia_LaDeDefecto()
        {
            ImpresoraDelUsuario(USUARIO, "  ");
            ImpresoraDelUsuario(ImpresionDocumentosServidor.USUARIO_DEFECTO, IMPRESORA_DEFECTO);

            Assert.AreEqual(IMPRESORA_DEFECTO, Servicio(true).ElegirImpresora(USUARIO));
            Assert.AreEqual(IMPRESORA_DEFECTO, Servicio(true).ElegirImpresora(null));
        }

        /// <summary>Carlos 04/11/26: «(predeterminada)» en el parámetro = la impresora predeterminada de Windows en el servidor.</summary>
        [TestMethod]
        public void EsPredeterminada_SoloConElValorEspecial_SinDistinguirMayusculas()
        {
            Assert.IsTrue(ImpresionDocumentosServidor.EsPredeterminada("(predeterminada)"));
            Assert.IsTrue(ImpresionDocumentosServidor.EsPredeterminada("  (Predeterminada) "));
            Assert.IsFalse(ImpresionDocumentosServidor.EsPredeterminada("HP Administracion"));
            Assert.IsFalse(ImpresionDocumentosServidor.EsPredeterminada(null));
            Assert.IsFalse(ImpresionDocumentosServidor.EsPredeterminada(""));
        }

        [TestMethod]
        public void ElegirImpresora_NiElUsuarioNiDefecto_Ninguna()
        {
            ImpresoraDelUsuario(USUARIO, null);
            ImpresoraDelUsuario(ImpresionDocumentosServidor.USUARIO_DEFECTO, null);

            Assert.IsNull(Servicio(true).ElegirImpresora(USUARIO));
        }

        [TestMethod]
        public void Interruptor_SoloTrueEnciende()
        {
            Assert.IsTrue(InterruptorImpresionDocumentosServidor.EstaActivo("true"));
            Assert.IsTrue(InterruptorImpresionDocumentosServidor.EstaActivo(" TRUE "));
            Assert.IsFalse(InterruptorImpresionDocumentosServidor.EstaActivo("false"));
            Assert.IsFalse(InterruptorImpresionDocumentosServidor.EstaActivo("1"));
            Assert.IsFalse(InterruptorImpresionDocumentosServidor.EstaActivo(null));
        }

        // ------------------------------------------------------------ el servicio

        private static FacturarRutasResponseDTO RespuestaConPapel()
        {
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.Facturas.Add(new FacturaCreadaDTO { Empresa = "1", NumeroPedido = 929244, NumeroFactura = "NV2617700", DatosImpresion = Datos(1, TipoBandejaImpresion.Lower) });
            respuesta.Albaranes.Add(new AlbaranCreadoDTO { Empresa = "1", NumeroPedido = 929718, NumeroAlbaran = 555, DatosImpresion = Datos(2, TipoBandejaImpresion.Middle) });
            respuesta.Facturas.Add(new FacturaCreadaDTO { Empresa = "1", NumeroPedido = 929723, NumeroFactura = "NV2617714" }); // sin papel
            return respuesta;
        }

        [TestMethod]
        public void Imprimir_Encendido_ImprimeCadaDocumentoConSusCopiasYSuBandejaEnLaImpresoraDelUsuario()
        {
            ImpresoraDelUsuario(USUARIO, IMPRESORA_ALFREDO);
            FacturarRutasResponseDTO respuesta = RespuestaConPapel();

            Servicio(true).Imprimir(respuesta, USUARIO);

            A.CallTo(() => impresora.Imprimir(PDF, IMPRESORA_ALFREDO, 1, TipoBandejaImpresion.Lower)).MustHaveHappenedOnceExactly();
            A.CallTo(() => impresora.Imprimir(PDF, IMPRESORA_ALFREDO, 2, TipoBandejaImpresion.Middle)).MustHaveHappenedOnceExactly();
            A.CallTo(() => impresora.Imprimir(A<byte[]>._, A<string>._, A<int>._, A<TipoBandejaImpresion>._)).MustHaveHappenedTwiceExactly();
            Assert.AreEqual(IMPRESORA_ALFREDO, respuesta.Facturas[0].DatosImpresion.ImpresoEnServidor);
            Assert.AreEqual(IMPRESORA_ALFREDO, respuesta.Albaranes[0].DatosImpresion.ImpresoEnServidor);
            Assert.AreEqual(0, respuesta.PedidosConErrores.Count);
            Assert.AreEqual(2, registrados.Count(e => e is ImpresionDocumentoServidorInfo && e.Message.Contains(IMPRESORA_ALFREDO)));
            Assert.IsTrue(registrados.Any(e => e.Message.Contains("la factura NV2617700") && e.Message.Contains("bandeja Lower")));
        }

        [TestMethod]
        public void Imprimir_Apagado_NiImprimeNiLeeElParametro()
        {
            FacturarRutasResponseDTO respuesta = RespuestaConPapel();

            Servicio(false).Imprimir(respuesta, USUARIO);

            A.CallTo(impresora).MustNotHaveHappened();
            A.CallTo(lector).MustNotHaveHappened();
            Assert.IsNull(respuesta.Facturas[0].DatosImpresion.ImpresoEnServidor);
            Assert.AreEqual(0, respuesta.PedidosConErrores.Count);
        }

        [TestMethod]
        public void Imprimir_FallaUnDocumento_AvisoComoWarningYSigueConLosDemas()
        {
            ImpresoraDelUsuario(USUARIO, IMPRESORA_ALFREDO);
            A.CallTo(() => impresora.Imprimir(A<byte[]>._, A<string>._, 1, A<TipoBandejaImpresion>._))
                .Throws(new InvalidOperationException("la impresora «HP Administración Alfredo» no está instalada en el servidor"));
            FacturarRutasResponseDTO respuesta = RespuestaConPapel();

            Servicio(true).Imprimir(respuesta, USUARIO);

            PedidoConErrorDTO aviso = respuesta.PedidosConErrores.Single();
            Assert.AreEqual(NivelSeveridad.Warning, aviso.Severidad);
            Assert.AreEqual(ImpresionDocumentosServidor.TIPO_ERROR, aviso.TipoError);
            Assert.AreEqual(929244, aviso.NumeroPedido);
            Assert.AreEqual("No se ha podido imprimir la factura NV2617700 en «HP Administración Alfredo»: la impresora «HP Administración Alfredo» no está instalada en el servidor. Imprímalo desde Nesto.", aviso.MensajeError);
            Assert.IsNull(respuesta.Facturas[0].DatosImpresion.ImpresoEnServidor);
            Assert.AreEqual(IMPRESORA_ALFREDO, respuesta.Albaranes[0].DatosImpresion.ImpresoEnServidor, "el albarán sí sale");
            Assert.IsTrue(registrados.Any(e => e.InnerException is InvalidOperationException));
        }

        [TestMethod]
        public void Imprimir_SinImpresoraDeDocumentos_AvisoYNoImprime()
        {
            FacturarRutasResponseDTO respuesta = RespuestaConPapel();

            Servicio(true).Imprimir(respuesta, USUARIO);

            A.CallTo(impresora).MustNotHaveHappened();
            Assert.AreEqual(2, respuesta.PedidosConErrores.Count);
            Assert.IsTrue(respuesta.PedidosConErrores.All(e => e.Severidad == NivelSeveridad.Warning));
            Assert.AreEqual("No se ha podido imprimir el albarán 555: sin impresora de documentos (parámetro ImpresoraDocumentos). Imprímalo desde Nesto.",
                respuesta.PedidosConErrores.Single(e => e.NumeroPedido == 929718).MensajeError);
        }

        [TestMethod]
        public void Imprimir_FallaLaLecturaDelParametro_NoRompe()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("sin conexión"));
            FacturarRutasResponseDTO respuesta = RespuestaConPapel();

            Servicio(true).Imprimir(respuesta, USUARIO);

            A.CallTo(impresora).MustNotHaveHappened();
            Assert.AreEqual(2, respuesta.PedidosConErrores.Count);
            Assert.IsTrue(respuesta.PedidosConErrores[0].MensajeError.Contains("sin conexión"));
        }

        [TestMethod]
        public void Imprimir_CeroCopiasOPdfVacio_NoImprime()
        {
            ImpresoraDelUsuario(USUARIO, IMPRESORA_ALFREDO);
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.Facturas.Add(new FacturaCreadaDTO { NumeroFactura = "NV1", DatosImpresion = Datos(0, TipoBandejaImpresion.Lower) });
            respuesta.NotasEntrega.Add(new NotaEntregaCreadaDTO { NumeroPedido = 1, DatosImpresion = new DocumentoParaImprimir { BytesPDF = new byte[0], NumeroCopias = 1 } });

            Servicio(true).Imprimir(respuesta, USUARIO);

            A.CallTo(impresora).MustNotHaveHappened();
            Assert.AreEqual(0, respuesta.PedidosConErrores.Count);
        }

        // ------------------------------------------------------------ la regla (una para todos los caminos)

        [TestMethod]
        public void Regla_FrasesSinMayusculasNiTildesEnComentariosOPicking()
        {
            Assert.IsTrue(ReglaImpresionDocumentos.DebeImprimir(new CabPedidoVta { ComentarioPicking = "FACTURA FISICA." }));
            Assert.IsTrue(ReglaImpresionDocumentos.DebeImprimir(new CabPedidoVta { Comentarios = "Albarán físico, por favor" }));
            Assert.IsTrue(ReglaImpresionDocumentos.DebeImprimir("factura en papel"));
            Assert.IsFalse(ReglaImpresionDocumentos.DebeImprimir(new CabPedidoVta { Comentarios = "Factura por correo" }));
            Assert.IsFalse(ReglaImpresionDocumentos.DebeImprimir((CabPedidoVta)null));
        }

        [TestMethod]
        public void Regla_CopiasYBandejaSegunLaRuta()
        {
            var agenciaConPapel = new CabPedidoVta { Empresa = "1  ", Ruta = "FW", ComentarioPicking = "Factura física." };
            Assert.AreEqual(1, ReglaImpresionDocumentos.NumeroCopias(agenciaConPapel));
            Assert.AreEqual(TipoBandejaImpresion.Lower, ReglaImpresionDocumentos.Bandeja(agenciaConPapel, esFactura: true));
            Assert.AreEqual(TipoBandejaImpresion.Middle, ReglaImpresionDocumentos.Bandeja(agenciaConPapel, esFactura: false));
            Assert.AreEqual(0, ReglaImpresionDocumentos.NumeroCopias(new CabPedidoVta { Empresa = "1", Ruta = "FW" }));
            Assert.AreEqual(0, ReglaImpresionDocumentos.NumeroCopias(new CabPedidoVta { Empresa = "3", Ruta = "FW", ComentarioPicking = "Factura física." }), "traspasado");
            Assert.AreEqual(2, ReglaImpresionDocumentos.NumeroCopias(new CabPedidoVta { Empresa = "1", Ruta = "16" }), "ruta propia");
            Assert.AreEqual(0, ReglaImpresionDocumentos.NumeroCopias(null));
        }

        // ------------------------------------------------------------ la bandeja, como Nesto

        [TestMethod]
        public void SelectorBandeja_PorKindEstandarOPorNombreDeHP()
        {
            var estandar = new List<SelectorBandeja.Bandeja>
            {
                new SelectorBandeja.Bandeja("Superior", 1, 1),
                new SelectorBandeja.Bandeja("Inferior", 4, 4)
            };
            Assert.AreEqual(1, SelectorBandeja.Elegir(estandar, TipoBandejaImpresion.Lower));

            var hp = new List<SelectorBandeja.Bandeja>
            {
                new SelectorBandeja.Bandeja("Selección automática", 15, 15),
                new SelectorBandeja.Bandeja("Bandeja 3", 260, 257),
                new SelectorBandeja.Bandeja("Bandeja 1", 258, 257),
                new SelectorBandeja.Bandeja("Bandeja 2", 259, 257)
            };
            Assert.AreEqual(2, SelectorBandeja.Elegir(hp, TipoBandejaImpresion.Upper), "Bandeja 1");
            Assert.AreEqual(3, SelectorBandeja.Elegir(hp, TipoBandejaImpresion.Middle), "Bandeja 2");
            Assert.AreEqual(1, SelectorBandeja.Elegir(hp, TipoBandejaImpresion.Lower), "Bandeja 3");
            Assert.AreEqual(0, SelectorBandeja.Elegir(hp, TipoBandejaImpresion.AutomaticFeed));
            Assert.AreEqual(-1, SelectorBandeja.Elegir(new List<SelectorBandeja.Bandeja> { new SelectorBandeja.Bandeja("Manual", 300, 257) }, TipoBandejaImpresion.Lower));
            Assert.AreEqual(-1, SelectorBandeja.Elegir(null, TipoBandejaImpresion.Lower));
        }

        // ------------------------------------------------------------ camino 1: Facturar rutas / FacturarPedido (y el packing)

        private GestorFacturacionRutas Gestor(IServicioNotasEntrega notas, bool activo)
        {
            var documentoNota = A.Fake<IServicioDocumentoNotaEntrega>();
            A.CallTo(() => documentoNota.Pdf(A<Factura>._, A<bool>._, A<string>._)).Returns(PDF);
            return new GestorFacturacionRutas(
                A.Fake<NVEntities>(),
                A.Fake<IServicioAlbaranesVenta>(),
                A.Fake<IServicioFacturas>(),
                A.Fake<IGestorFacturas>(),
                A.Fake<IServicioTraspasoEmpresa>(),
                notas,
                A.Fake<IServicioExtractoRuta>())
            {
                ParadasNotaEntrega = A.Fake<IServicioParadasNotaEntrega>(),
                EtiquetasRecogida = A.Fake<IServicioEtiquetasRecogida>(),
                DocumentoNotaEntrega = documentoNota,
                ImpresionDocumentos = Servicio(activo)
            };
        }

        private static CabPedidoVta NotaConPapel() => new CabPedidoVta
        {
            Empresa = "1",
            Número = 929734,
            Ruta = "FW",
            NotaEntrega = true,
            ComentarioPicking = "Factura física.",
            LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.EN_CURSO, Picking = 5, VtoBueno = true, YaFacturado = true }
            }
        };

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task FacturarRutas_ImprimeEnElServidorSoloConElInterruptor(bool activo)
        {
            ImpresoraDelUsuario(USUARIO, IMPRESORA_ALFREDO);
            CabPedidoVta pedido = NotaConPapel();
            var notas = A.Fake<IServicioNotasEntrega>();
            A.CallTo(() => notas.ProcesarNotaEntrega(pedido, USUARIO)).Returns(new NotaEntregaCreadaDTO { Empresa = "1", NumeroPedido = 929734, NumeroLineas = 1 });

            FacturarRutasResponseDTO respuesta = await Gestor(notas, activo).FacturarRutas(new List<CabPedidoVta> { pedido }, USUARIO, DateTime.Today);

            DocumentoParaImprimir datos = respuesta.NotasEntrega.Single().DatosImpresion;
            Assert.IsNotNull(datos, "la respuesta sigue llevando los DatosImpresion");
            if (activo)
            {
                A.CallTo(() => impresora.Imprimir(PDF, IMPRESORA_ALFREDO, 1, TipoBandejaImpresion.Middle)).MustHaveHappenedOnceExactly();
                Assert.AreEqual(IMPRESORA_ALFREDO, datos.ImpresoEnServidor);
            }
            else
            {
                A.CallTo(impresora).MustNotHaveHappened();
                Assert.IsNull(datos.ImpresoEnServidor);
            }
        }

        // ------------------------------------------------------------ camino 2: la etiqueta del packing (Ariadna)

        [TestMethod]
        public void ResumenPacking_ImpresoEnElServidor_DiceDondeYNoPideImprimirDesdeNesto()
        {
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.Facturas.Add(new FacturaCreadaDTO { NumeroFactura = "NV2617700", DatosImpresion = new DocumentoParaImprimir { ImpresoEnServidor = IMPRESORA_DEFECTO } });

            List<string> avisos = ServicioEtiquetaPacking.ResumenFacturacion(respuesta, 929244, reimpresion: false, out _);

            CollectionAssert.Contains(avisos, "El documento en papel del pedido 929244 se ha impreso en «HP Administración».");
            Assert.IsFalse(avisos.Any(a => a.Contains("imprímalo desde Nesto")));
        }

        [TestMethod]
        public void ResumenPacking_FalloAlImprimir_SoloElAvisoDelFallo()
        {
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.Facturas.Add(new FacturaCreadaDTO { NumeroFactura = "NV2617700", DatosImpresion = new DocumentoParaImprimir() });
            respuesta.PedidosConErrores.Add(new PedidoConErrorDTO { TipoError = ImpresionDocumentosServidor.TIPO_ERROR, MensajeError = "No se ha podido imprimir la factura NV2617700 en «X»: atasco. Imprímalo desde Nesto.", Severidad = NivelSeveridad.Warning });

            List<string> avisos = ServicioEtiquetaPacking.ResumenFacturacion(respuesta, 929244, reimpresion: false, out _);

            CollectionAssert.Contains(avisos, "No se ha podido imprimir la factura NV2617700 en «X»: atasco. Imprímalo desde Nesto.");
            Assert.IsFalse(avisos.Any(a => a.Contains("lleva el documento en papel")));
        }

        // ------------------------------------------------------------ camino 3: «Crear factura» del detalle del pedido

        private IGestorFacturas GestorFacturasConPdf()
        {
            var gestorFacturas = A.Fake<IGestorFacturas>();
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, A<bool>._, A<string>._, A<bool>._, A<bool>._)).ReturnsLazily(() => new ByteArrayContent(PDF));
            return gestorFacturas;
        }

        private DocumentoFacturaDetalle Detalle(IGestorFacturas gestorFacturas, CabPedidoVta pedido, bool activo)
            => new DocumentoFacturaDetalle(gestorFacturas, Servicio(activo), (empresa, numero) => Task.FromResult(pedido), registrados.Add);

        private static CrearFacturaResponseDTO Factura() => new CrearFacturaResponseDTO { Empresa = "1", NumeroPedido = 929244, NumeroFactura = "NV2617700" };

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task Detalle_FacturaFisica_DatosImpresionYSoloConElInterruptorSeImprime(bool activo)
        {
            ImpresoraDelUsuario(USUARIO, IMPRESORA_ALFREDO);
            IGestorFacturas gestorFacturas = GestorFacturasConPdf();
            CrearFacturaResponseDTO factura = Factura();

            await Detalle(gestorFacturas, new CabPedidoVta { Empresa = "1", Número = 929244, Ruta = "FW", ComentarioPicking = "Factura física." }, activo)
                .Completar(factura, USUARIO);

            Assert.IsNotNull(factura.DatosImpresion);
            Assert.AreEqual(1, factura.DatosImpresion.NumeroCopias);
            Assert.AreEqual(TipoBandejaImpresion.Lower, factura.DatosImpresion.TipoBandeja);
            CollectionAssert.AreEqual(PDF, factura.DatosImpresion.BytesPDF);
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, true, USUARIO, A<bool>._, A<bool>._)).MustHaveHappenedOnceExactly();
            if (activo)
            {
                A.CallTo(() => impresora.Imprimir(PDF, IMPRESORA_ALFREDO, 1, TipoBandejaImpresion.Lower)).MustHaveHappenedOnceExactly();
                Assert.AreEqual(IMPRESORA_ALFREDO, factura.DatosImpresion.ImpresoEnServidor);
            }
            else
            {
                A.CallTo(impresora).MustNotHaveHappened();
            }
            Assert.AreEqual(0, factura.Avisos.Count);
        }

        [TestMethod]
        public async Task Detalle_SinPapel_NiPdfNiImpresion()
        {
            IGestorFacturas gestorFacturas = GestorFacturasConPdf();
            CrearFacturaResponseDTO factura = Factura();

            await Detalle(gestorFacturas, new CabPedidoVta { Empresa = "1", Ruta = "FW", Comentarios = "Nada especial" }, activo: true).Completar(factura, USUARIO);

            Assert.IsNull(factura.DatosImpresion);
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, A<bool>._, A<string>._, A<bool>._, A<bool>._)).MustNotHaveHappened();
            A.CallTo(impresora).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Detalle_FallaLaImpresora_AvisoEnLaFacturaYNoLanza()
        {
            ImpresoraDelUsuario(USUARIO, IMPRESORA_ALFREDO);
            A.CallTo(() => impresora.Imprimir(A<byte[]>._, A<string>._, A<int>._, A<TipoBandejaImpresion>._)).Throws(new Exception("Sin papel."));
            CrearFacturaResponseDTO factura = Factura();

            await Detalle(GestorFacturasConPdf(), new CabPedidoVta { Empresa = "1", Ruta = "FW", ComentarioPicking = "Factura en papel" }, activo: true).Completar(factura, USUARIO);

            Assert.AreEqual("No se ha podido imprimir la factura NV2617700 en «HP Administración Alfredo»: Sin papel. Imprímalo desde Nesto.", factura.Avisos.Single());
            Assert.IsNull(factura.DatosImpresion.ImpresoEnServidor);
        }

        [TestMethod]
        public async Task Detalle_FallaElPdf_AvisoYNoLanza()
        {
            var gestorFacturas = A.Fake<IGestorFacturas>();
            A.CallTo(() => gestorFacturas.LeerFactura(A<string>._, A<string>._)).Throws(new Exception("no existe"));
            CrearFacturaResponseDTO factura = Factura();

            await Detalle(gestorFacturas, new CabPedidoVta { Empresa = "1", Ruta = "FW", ComentarioPicking = "Factura física" }, activo: true).Completar(factura, USUARIO);

            Assert.IsNull(factura.DatosImpresion);
            Assert.IsTrue(factura.Avisos.Single().Contains("no se ha podido generar el documento"));
            A.CallTo(impresora).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturasController_CrearFactura_PasaPorElDocumentoDelDetalle()
        {
            ImpresoraDelUsuario("Alfredo", IMPRESORA_ALFREDO);
            IGestorFacturas gestorFacturas = GestorFacturasConPdf();
            A.CallTo(() => gestorFacturas.CrearFactura("1", 929244, "Alfredo", A<string>._)).Returns(Factura());
            var controller = new FacturasController(A.Fake<IServicioFacturas>(), gestorFacturas)
            {
                DocumentoDetalle = Detalle(gestorFacturas, new CabPedidoVta { Empresa = "1", Ruta = "FW", ComentarioPicking = "Factura física" }, activo: true)
            };

            var resultado = await controller.CrearFactura(JObject.FromObject(new { Empresa = "1", Pedido = 929244, Usuario = "Alfredo" }));

            var ok = (OkNegotiatedContentResult<CrearFacturaResponseDTO>)resultado;
            Assert.AreEqual(IMPRESORA_ALFREDO, ok.Content.DatosImpresion.ImpresoEnServidor);
            A.CallTo(() => impresora.Imprimir(PDF, IMPRESORA_ALFREDO, 1, TipoBandejaImpresion.Lower)).MustHaveHappenedOnceExactly();
        }
    }
}
