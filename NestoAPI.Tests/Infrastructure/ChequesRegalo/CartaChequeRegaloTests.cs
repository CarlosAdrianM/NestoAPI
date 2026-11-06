using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#838: la carta en papel del cheque regalo (como el correo), que se imprime en el servidor detrás de la factura que
    /// genera el cheque, por los tres caminos: Facturar rutas, la etiqueta del packing (#592, que factura por Facturar rutas) y
    /// «Crear factura» del detalle del pedido.
    /// </summary>
    [TestClass]
    public class CartaChequeRegaloTests
    {
        private static readonly byte[] PDF_FACTURA = { 0x25, 0x50, 0x44, 0x46, 0x01 };
        private static readonly byte[] PDF_CARTA = { 0x25, 0x50, 0x44, 0x46, 0x02 };
        private const string USUARIO = "Alfredo";
        private const string IMPRESORA = "HP Administración Alfredo";

        private static CampanaCorreoChequeRegalo Campana() => PlantillaCorreoChequeRegaloTests.Campana();

        private static AvisoChequeRegalo AvisoDe(string factura, string nombreDePila = "Lola") => new AvisoChequeRegalo
        {
            IdCheque = 7,
            Resultado = ResultadoAvisoChequeRegalo.Enviado,
            Correos = "lola@centro.es",
            EmpresaFactura = "1",
            FacturaOrigen = factura,
            Campana = Campana(),
            NombreDePila = nombreDePila
        };

        private static IGeneradorCartaChequeRegalo GeneradorFalso()
        {
            IGeneradorCartaChequeRegalo generador = A.Fake<IGeneradorCartaChequeRegalo>();
            A.CallTo(() => generador.Generar(A<CampanaCorreoChequeRegalo>._, A<string>._)).Returns(PDF_CARTA);
            return generador;
        }

        // ------------------------------------------------------------ el contenido y el PDF

        [TestMethod]
        public void Contenido_EsElDelCorreo_ImporteFechaYNombreDePila()
        {
            ContenidoCartaChequeRegalo contenido = GeneradorPdfCartaChequeRegalo.Contenido(Campana(), "Lola", null);

            Assert.AreEqual("Hola, Lola:", contenido.Parrafos[0]);
            string todo = string.Join("\n", contenido.Parrafos);
            StringAssert.Contains(todo, "un cheque regalo de 50 € de descuento");
            StringAssert.Contains(todo, "hasta el 7 de noviembre");
            StringAssert.Contains(todo, "superior a 250 €");
            CollectionAssert.AreEqual(PlantillaCorreoChequeRegalo.Parrafos(Campana(), "Lola"), contenido.Parrafos, "Los párrafos salen de la plantilla del correo");
            Assert.AreEqual(PlantillaCorreoChequeRegalo.LETRA_PEQUENA, contenido.LetraPequena);
            Assert.AreEqual(PlantillaCorreoChequeRegalo.FIRMA, contenido.Firma);
            Assert.IsNull(contenido.Imagen);
        }

        [TestMethod]
        public void Contenido_ElCorreoDiceLoMismo()
        {
            CorreoChequeRegalo correo = PlantillaCorreoChequeRegalo.Componer(Campana(), "Lola", null, null);
            ContenidoCartaChequeRegalo contenido = GeneradorPdfCartaChequeRegalo.Contenido(Campana(), "Lola", null);

            foreach (string parrafo in contenido.Parrafos)
            {
                StringAssert.Contains(correo.Texto, parrafo);
            }
            StringAssert.Contains(correo.Texto, contenido.LetraPequena);
        }

        [TestMethod]
        public void Contenido_SinNombreDePila_SoloHola_YLaTiendaSinUtm()
        {
            ContenidoCartaChequeRegalo contenido = GeneradorPdfCartaChequeRegalo.Contenido(Campana(), null, new byte[0]);

            Assert.AreEqual("Hola:", contenido.Parrafos[0]);
            Assert.IsNull(contenido.Imagen, "Una imagen vacía es como no tenerla");
            StringAssert.StartsWith(contenido.Tienda, PlantillaCorreoChequeRegalo.TEXTO_BOTON_TIENDA + ": www.");
            Assert.IsFalse(contenido.Tienda.Contains("utm_"));
            Assert.IsFalse(contenido.Tienda.Contains("https://"));
        }

        [TestMethod]
        public void GenerarPdf_CartaA4ConLaImagenDeLaCampana_NoVacia()
        {
            string imagenPedida = null;
            var generador = new GeneradorPdfCartaChequeRegalo(n => { imagenPedida = n; return ImagenesChequeRegalo.Leer(n); });

            byte[] pdf = generador.Generar(Campana(), "Lola");

            Assert.AreEqual("cheque50.jpg", imagenPedida, "La imagen es la de la campaña");
            Assert.IsTrue(pdf.Length > 1000);
            Assert.IsTrue(pdf.Length < 600 * 1024, $"Viaja en la respuesta de la facturación: {pdf.Length} bytes");
            Assert.AreEqual("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [TestMethod]
        public void GenerarPdf_SiLaImagenFalla_LaCartaSaleSinElla()
        {
            var generador = new GeneradorPdfCartaChequeRegalo(n => throw new InvalidOperationException("sin recurso"));

            byte[] pdf = generador.Generar(Campana(), null);

            Assert.AreEqual("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }

        // ------------------------------------------------------------ qué cartas lleva la factura

        [TestMethod]
        public void Crear_UnaCartaPorChequeDeEstaFactura_ConSuNombreYUnaCopia()
        {
            IGeneradorCartaChequeRegalo generador = GeneradorFalso();
            var factura = new CrearFacturaResponseDTO { Empresa = "1", NumeroPedido = 929244, NumeroFactura = "NV2617700" };

            List<CartaChequeRegaloDTO> cartas = CartasChequeRegalo.Crear(
                new[] { AvisoDe("NV2617700 "), AvisoDe("NV2600001") }, factura, "15000 ", generador, null);

            CartaChequeRegaloDTO carta = cartas.Single();
            Assert.AreEqual("Carta cheque regalo NV2617700", carta.Nombre);
            Assert.AreEqual("la carta del cheque regalo de la factura NV2617700", carta.Descripcion);
            Assert.AreEqual("NV2617700", carta.NumeroFactura);
            Assert.AreEqual("1", carta.Empresa);
            Assert.AreEqual(929244, carta.NumeroPedido);
            Assert.AreEqual("15000", carta.Cliente);
            Assert.AreEqual(1, carta.DatosImpresion.NumeroCopias);
            CollectionAssert.AreEqual(PDF_CARTA, carta.DatosImpresion.BytesPDF);
            Assert.IsNull(carta.DatosImpresion.ImpresoEnServidor);
            A.CallTo(() => generador.Generar(A<CampanaCorreoChequeRegalo>.That.Matches(c => c.ImporteBase == 50), "Lola")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Crear_SiLaCartaFalla_ElmahYSinCarta_NoLanza()
        {
            IGeneradorCartaChequeRegalo generador = A.Fake<IGeneradorCartaChequeRegalo>();
            A.CallTo(() => generador.Generar(A<CampanaCorreoChequeRegalo>._, A<string>._)).Throws(new InvalidOperationException("QuestPDF"));
            var errores = new List<Exception>();

            List<CartaChequeRegaloDTO> cartas = CartasChequeRegalo.Crear(new[] { AvisoDe("NV2617700") },
                new CrearFacturaResponseDTO { Empresa = "1", NumeroFactura = "NV2617700" }, "15000", generador, errores.Add);

            Assert.AreEqual(0, cartas.Count);
            StringAssert.Contains(errores.Single().Message, "#838");
            StringAssert.Contains(errores.Single().Message, "NV2617700");
        }

        // ------------------------------------------------------------ al facturar (ServicioFacturas.AnadirChequeRegalo)

        private static IGeneradorChequesRegalo GeneraCheque(bool genera)
        {
            IGeneradorChequesRegalo generador = A.Fake<IGeneradorChequesRegalo>();
            A.CallTo(() => generador.GenerarPorFactura(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._))
                .Returns(Task.FromResult(genera ? new List<string> { "El cliente 15000 ha recibido un cheque regalo de 50 €" } : new List<string>()));
            return generador;
        }

        [TestMethod]
        public async Task AlFacturar_ConCheque_LaRespuestaLlevaLaCarta()
        {
            IAvisadorChequesRegalo avisador = A.Fake<IAvisadorChequesRegalo>();
            A.CallTo(() => avisador.AvisarCliente("15000")).Returns(Task.FromResult(new List<AvisoChequeRegalo> { AvisoDe("NV2617700") }));
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2617700", Empresa = "1", NumeroPedido = 929244 };

            await ServicioFacturas.AnadirChequeRegalo(GeneraCheque(true), respuesta, "15000", new DateTime(2026, 10, 13), "Carlos", avisador,
                generadorCarta: GeneradorFalso());

            CartaChequeRegaloDTO carta = respuesta.CartasChequeRegalo.Single();
            Assert.AreEqual("Carta cheque regalo NV2617700", carta.Nombre);
            CollectionAssert.AreEqual(PDF_CARTA, carta.DatosImpresion.BytesPDF);
        }

        [TestMethod]
        public async Task AlFacturar_SinCheque_NiCarta()
        {
            IAvisadorChequesRegalo avisador = A.Fake<IAvisadorChequesRegalo>();
            IGeneradorCartaChequeRegalo generadorCarta = GeneradorFalso();
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2617700", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(GeneraCheque(false), respuesta, "15000", new DateTime(2026, 10, 13), "Carlos", avisador,
                generadorCarta: generadorCarta);

            Assert.AreEqual(0, respuesta.CartasChequeRegalo.Count);
            A.CallTo(generadorCarta).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AlFacturar_ChequeViejoSinCorreoDeOtraFactura_NoSacaCarta()
        {
            IAvisadorChequesRegalo avisador = A.Fake<IAvisadorChequesRegalo>();
            AvisoChequeRegalo viejo = AvisoDe("NV2600001");
            viejo.Resultado = ResultadoAvisoChequeRegalo.SinCorreo;
            A.CallTo(() => avisador.AvisarCliente("15000")).Returns(Task.FromResult(new List<AvisoChequeRegalo> { viejo }));
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2617700", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(GeneraCheque(true), respuesta, "15000", new DateTime(2026, 10, 13), "Carlos", avisador,
                generadorCarta: GeneradorFalso());

            Assert.AreEqual(0, respuesta.CartasChequeRegalo.Count);
        }

        [TestMethod]
        public async Task AlFacturar_SiLaCartaFalla_LaFacturaSigueConSusAvisos()
        {
            IAvisadorChequesRegalo avisador = A.Fake<IAvisadorChequesRegalo>();
            A.CallTo(() => avisador.AvisarCliente("15000")).Returns(Task.FromResult(new List<AvisoChequeRegalo> { AvisoDe("NV2617700") }));
            IGeneradorCartaChequeRegalo generadorCarta = A.Fake<IGeneradorCartaChequeRegalo>();
            A.CallTo(() => generadorCarta.Generar(A<CampanaCorreoChequeRegalo>._, A<string>._)).Throws(new InvalidOperationException("roto"));
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2617700", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(GeneraCheque(true), respuesta, "15000", new DateTime(2026, 10, 13), "Carlos", avisador,
                generadorCarta: generadorCarta);

            Assert.AreEqual(0, respuesta.CartasChequeRegalo.Count);
            Assert.AreEqual(2, respuesta.Avisos.Count, "El aviso del cheque y el del correo siguen");
        }

        // ------------------------------------------------------------ el avisador deja lo que necesita la carta

        [TestMethod]
        public async Task ElAviso_LlevaLaCampanaLaFacturaYElNombreDePila_TambienSinCorreo()
        {
            IRepositorioAvisoChequesRegalo repositorio = A.Fake<IRepositorioAvisoChequesRegalo>();
            A.CallTo(() => repositorio.ColumnasDisponibles()).Returns(Task.FromResult(true));
            A.CallTo(() => repositorio.LeerChequesSinAviso("16000"))
                .Returns(Task.FromResult(new List<ChequeRegaloSinAviso> { AvisadorChequesRegaloTests.Cheque(id: 8, cliente: "16000", factura: "NV2617700") }));
            A.CallTo(() => repositorio.LeerDestinatario("16000", "1", "NV2617700"))
                .Returns(Task.FromResult(new DestinatarioChequeRegalo { Nombre = "ROSA MARTÍNEZ", Correos = null }));
            var pedidos = new List<List<string>>();
            var avisador = new AvisadorChequesRegalo(repositorio, A.Fake<IServicioCorreoElectronico>(), leerImagen: n => null,
                registrarError: _ => { },
                generarSaludos: nombres =>
                {
                    pedidos.Add(nombres);
                    return Task.FromResult(nombres.ToDictionary(n => n, n => "Hola Rosa", StringComparer.OrdinalIgnoreCase));
                });

            AvisoChequeRegalo aviso = (await avisador.AvisarCliente("16000")).Single();

            Assert.AreEqual(ResultadoAvisoChequeRegalo.SinCorreo, aviso.Resultado);
            Assert.AreEqual("NV2617700", aviso.FacturaOrigen);
            Assert.AreEqual("1", aviso.EmpresaFactura);
            Assert.AreEqual(50, aviso.Campana.ImporteBase);
            Assert.AreEqual(new DateTime(2026, 11, 7), aviso.Campana.CanjeHasta);
            Assert.AreEqual("Rosa", aviso.NombreDePila, "La carta saluda aunque el cliente no tenga correo");
            CollectionAssert.AreEqual(new[] { "ROSA MARTÍNEZ" }, pedidos.Single());
        }

        // ------------------------------------------------------------ la impresión en el servidor

        private ILectorParametrosUsuario lector;
        private IImpresoraDocumentos impresora;
        private List<byte[]> impresos;

        [TestInitialize]
        public void Preparar()
        {
            lector = A.Fake<ILectorParametrosUsuario>();
            impresora = A.Fake<IImpresoraDocumentos>();
            impresos = new List<byte[]>();
            A.CallTo(() => lector.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO, ImpresionDocumentosServidor.PARAMETRO_IMPRESORA))
                .Returns(IMPRESORA);
            A.CallTo(() => impresora.Imprimir(A<byte[]>._, A<string>._, A<int>._, A<TipoBandejaImpresion>._))
                .Invokes((byte[] pdf, string _, int __, TipoBandejaImpresion ___) => impresos.Add(pdf));
        }

        private ImpresionDocumentosServidor Servicio(bool activo) => new ImpresionDocumentosServidor(lector, impresora, () => activo);

        private static CartaChequeRegaloDTO Carta(string factura) => new CartaChequeRegaloDTO
        {
            Empresa = "1",
            NumeroFactura = factura,
            Nombre = CartasChequeRegalo.Nombre(factura),
            Descripcion = CartasChequeRegalo.Descripcion(factura),
            DatosImpresion = new DocumentoParaImprimir { BytesPDF = PDF_CARTA, NumeroCopias = 1, TipoBandeja = TipoBandejaImpresion.AutomaticFeed }
        };

        private static FacturarRutasResponseDTO RespuestaConCarta()
        {
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.Facturas.Add(new FacturaCreadaDTO
            {
                Empresa = "1",
                NumeroPedido = 929244,
                NumeroFactura = "NV2617700",
                DatosImpresion = new DocumentoParaImprimir { BytesPDF = PDF_FACTURA, NumeroCopias = 1, TipoBandeja = TipoBandejaImpresion.Lower }
            });
            respuesta.Albaranes.Add(new AlbaranCreadoDTO
            {
                Empresa = "1",
                NumeroPedido = 929718,
                NumeroAlbaran = 555,
                DatosImpresion = new DocumentoParaImprimir { BytesPDF = new byte[] { 0x25, 0x03 }, NumeroCopias = 1, TipoBandeja = TipoBandejaImpresion.Middle }
            });
            respuesta.CartasChequeRegalo.Add(Carta("NV2617700"));
            return respuesta;
        }

        [TestMethod]
        public void Imprimir_LaCartaSaleJustoDetrasDeSuFactura()
        {
            FacturarRutasResponseDTO respuesta = RespuestaConCarta();

            Servicio(true).Imprimir(respuesta, USUARIO);

            Assert.AreEqual(3, impresos.Count);
            CollectionAssert.AreEqual(PDF_FACTURA, impresos[0]);
            CollectionAssert.AreEqual(PDF_CARTA, impresos[1], "La carta, detrás de la factura y antes que el albarán de otro pedido");
            A.CallTo(() => impresora.Imprimir(PDF_CARTA, IMPRESORA, 1, TipoBandejaImpresion.AutomaticFeed)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(IMPRESORA, respuesta.CartasChequeRegalo.Single().DatosImpresion.ImpresoEnServidor);
        }

        [TestMethod]
        public void Imprimir_LaFacturaSinPapel_LaCartaSaleIgual()
        {
            FacturarRutasResponseDTO respuesta = RespuestaConCarta();
            respuesta.Facturas[0].DatosImpresion = null;

            Servicio(true).Imprimir(respuesta, USUARIO);

            CollectionAssert.AreEqual(PDF_CARTA, impresos[0]);
        }

        [TestMethod]
        public void Imprimir_Apagado_LaCartaNoSeImprimePeroVaEnLaRespuesta()
        {
            FacturarRutasResponseDTO respuesta = RespuestaConCarta();

            Servicio(false).Imprimir(respuesta, USUARIO);

            A.CallTo(impresora).MustNotHaveHappened();
            Assert.IsNull(respuesta.CartasChequeRegalo.Single().DatosImpresion.ImpresoEnServidor);
            CollectionAssert.AreEqual(PDF_CARTA, respuesta.CartasChequeRegalo.Single().DatosImpresion.BytesPDF);
        }

        [TestMethod]
        public void Imprimir_FallaLaCarta_AvisoComoLaFactura()
        {
            A.CallTo(() => impresora.Imprimir(PDF_CARTA, A<string>._, A<int>._, A<TipoBandejaImpresion>._)).Throws(new Exception("Sin papel."));
            FacturarRutasResponseDTO respuesta = RespuestaConCarta();
            respuesta.CartasChequeRegalo[0].Cliente = "15000";

            Servicio(true).Imprimir(respuesta, USUARIO);

            PedidoConErrorDTO aviso = respuesta.PedidosConErrores.Single();
            Assert.AreEqual("No se ha podido imprimir la carta del cheque regalo de la factura NV2617700 en «HP Administración Alfredo»: Sin papel. Imprímalo desde Nesto.", aviso.MensajeError);
            Assert.AreEqual("15000", aviso.Cliente);
            Assert.AreEqual(NivelSeveridad.Warning, aviso.Severidad);
        }

        [TestMethod]
        public void FacturarRutas_LasCartasDeLaFacturaPasanALaRespuestaConSuCliente()
        {
            var resultado = new CrearFacturaResponseDTO { Empresa = "1", NumeroFactura = "NV2617700" };
            resultado.CartasChequeRegalo.Add(Carta("NV2617700"));
            var facturaCreada = new FacturaCreadaDTO { Empresa = "1", NumeroFactura = "NV2617700", Cliente = "15000", Contacto = "0", NombreCliente = "CENTRO LOLA" };
            var respuesta = new FacturarRutasResponseDTO();

            GestorFacturacionRutas.AnadirCartasChequeRegalo(resultado, facturaCreada, respuesta);
            GestorFacturacionRutas.AnadirCartasChequeRegalo(new CrearFacturaResponseDTO { NumeroFactura = "NV2617701" }, facturaCreada, respuesta);

            CartaChequeRegaloDTO carta = respuesta.CartasChequeRegalo.Single();
            Assert.AreEqual("15000", carta.Cliente);
            Assert.AreEqual("CENTRO LOLA", carta.NombreCliente);
        }

        // ------------------------------------------------------------ «Crear factura» del detalle del pedido

        private DocumentoFacturaDetalle Detalle(CabPedidoVta pedido, bool activo)
        {
            IGestorFacturas gestor = A.Fake<IGestorFacturas>();
            A.CallTo(() => gestor.FacturasEnPDF(A<List<Factura>>._, A<bool>._, A<string>._, A<bool>._, A<bool>._)).ReturnsLazily(() => new ByteArrayContent(PDF_FACTURA));
            return new DocumentoFacturaDetalle(gestor, Servicio(activo), (empresa, numero) => Task.FromResult(pedido));
        }

        private static CrearFacturaResponseDTO FacturaConCarta()
        {
            var factura = new CrearFacturaResponseDTO { Empresa = "1", NumeroPedido = 929244, NumeroFactura = "NV2617700" };
            factura.CartasChequeRegalo.Add(Carta("NV2617700"));
            return factura;
        }

        [TestMethod]
        public async Task Detalle_FacturaEnPapel_FacturaYDetrasLaCarta()
        {
            CrearFacturaResponseDTO factura = FacturaConCarta();

            await Detalle(new CabPedidoVta { Empresa = "1", Número = 929244, Ruta = "FW", ComentarioPicking = "Factura física." }, activo: true)
                .Completar(factura, USUARIO);

            Assert.AreEqual(2, impresos.Count);
            CollectionAssert.AreEqual(PDF_FACTURA, impresos[0]);
            CollectionAssert.AreEqual(PDF_CARTA, impresos[1]);
            Assert.AreEqual(IMPRESORA, factura.CartasChequeRegalo.Single().DatosImpresion.ImpresoEnServidor);
            Assert.AreEqual(0, factura.Avisos.Count);
        }

        [TestMethod]
        public async Task Detalle_FacturaSinPapel_SoloLaCarta()
        {
            CrearFacturaResponseDTO factura = FacturaConCarta();

            await Detalle(new CabPedidoVta { Empresa = "1", Ruta = "FW", Comentarios = "Nada especial" }, activo: true).Completar(factura, USUARIO);

            Assert.IsNull(factura.DatosImpresion);
            CollectionAssert.AreEqual(PDF_CARTA, impresos.Single());
        }

        [TestMethod]
        public async Task Detalle_Apagado_NadaSeImprimeYLaCartaVaEnLaRespuesta()
        {
            CrearFacturaResponseDTO factura = FacturaConCarta();

            await Detalle(new CabPedidoVta { Empresa = "1", Ruta = "FW", ComentarioPicking = "Factura física." }, activo: false).Completar(factura, USUARIO);

            A.CallTo(impresora).MustNotHaveHappened();
            CollectionAssert.AreEqual(PDF_CARTA, factura.CartasChequeRegalo.Single().DatosImpresion.BytesPDF);
        }
    }
}
