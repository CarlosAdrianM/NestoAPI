using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593 (aviso): el correo al cliente con su cheque (texto de Alberto, 09/10/26), la nota al pie de la
    /// factura y el aviso a quien factura. Importe, mínimo, fecha e imagen salen de la campaña.
    /// </summary>
    [TestClass]
    public class PlantillaCorreoChequeRegaloTests
    {
        internal static CampanaCorreoChequeRegalo Campana(decimal importe = 50, decimal minimo = 250, string imagen = "cheque50.jpg") => new CampanaCorreoChequeRegalo
        {
            Codigo = "CHEQUE50_OCT_2026",
            ImporteBase = importe,
            MinimoCanje = minimo,
            CanjeHasta = new DateTime(2026, 11, 7),
            DiasEsperaTrasEntrega = 0,
            ImagenCorreo = imagen,
            Activa = true
        };

        private static readonly byte[] IMAGEN = { 0xFF, 0xD8, 0xFF, 0xE0 };

        [TestMethod]
        public void ElCorreoLlevaNombreImporteMinimoYFechaDeLaCampana()
        {
            CorreoChequeRegalo correo = PlantillaCorreoChequeRegalo.Componer(Campana(), "Centro Lola", IMAGEN, "cid:cheque-regalo");

            Assert.AreEqual("Con tu factura, 50 € de descuento para tu próximo pedido", correo.Asunto);
            StringAssert.Contains(correo.Html, "Hola, Centro Lola:");
            StringAssert.Contains(correo.Html, "un cheque regalo de 50 € de descuento para tu próximo pedido.");
            StringAssert.Contains(correo.Html, "desde el día de hoy hasta el 7 de noviembre en un pedido de productos computables superior a 250 €, después de los descuentos habituales.");
            StringAssert.Contains(correo.Html, "¿Qué necesitas para tu centro? Contacta con tu comercial o con nuestra tienda");
            StringAssert.Contains(correo.Html, "productos de peluquería ni Packs de Navidad «PACK 26»");
            StringAssert.Contains(correo.Html, "Un solo uso por código de cliente.");
            StringAssert.Contains(correo.Html, "El equipo de Nueva Visión.");
        }

        [TestMethod]
        public void OtraCampanaOtroImporteYFecha_NadaFijoEnElCodigo()
        {
            CampanaCorreoChequeRegalo febrero = Campana(importe: 30, minimo: 150);
            febrero.CanjeHasta = new DateTime(2027, 2, 26);

            CorreoChequeRegalo correo = PlantillaCorreoChequeRegalo.Componer(febrero, "Centro Lola", null, null);

            StringAssert.StartsWith(correo.Asunto, "Con tu factura, 30 €");
            StringAssert.Contains(correo.Html, "hasta el 26 de febrero");
            StringAssert.Contains(correo.Html, "superior a 150 €");
            Assert.IsFalse(correo.Html.Contains(" 50 €"));
            Assert.IsFalse(correo.Html.Contains("noviembre"));
        }

        [TestMethod]
        public void LaImagenVaArribaEnLineaConTextoAlternativo_YAnchoDeMovil()
        {
            CorreoChequeRegalo correo = PlantillaCorreoChequeRegalo.Componer(Campana(), "Centro Lola", IMAGEN, "cid:cheque-regalo");

            StringAssert.Contains(correo.Html, "src=\"cid:cheque-regalo\"");
            StringAssert.Contains(correo.Html, "alt=\"Cheque regalo de 50 € de descuento para tu próximo pedido, hasta el 7 de noviembre\"");
            StringAssert.Contains(correo.Html, "width:100%;max-width:600px");
            StringAssert.Contains(correo.Html, "name=\"viewport\"");
            Assert.IsTrue(correo.Html.IndexOf("<img", StringComparison.Ordinal) < correo.Html.IndexOf("Hola,", StringComparison.Ordinal), "La imagen va arriba");
            Assert.AreEqual("cheque50.jpg", correo.NombreImagen);
        }

        [TestMethod]
        public void LaLetraPequenaVaEnTamanoMenor()
        {
            CorreoChequeRegalo correo = PlantillaCorreoChequeRegalo.Componer(Campana(), "Centro Lola", IMAGEN, "cid:cheque-regalo");

            int letra = correo.Html.IndexOf("Letra pequeña", StringComparison.Ordinal);
            int celda = correo.Html.LastIndexOf("<td", letra, StringComparison.Ordinal);
            StringAssert.Contains(correo.Html.Substring(celda, letra - celda), "font-size:12px");
        }

        [TestMethod]
        public void SinImagen_NoHayEtiquetaImg()
        {
            CorreoChequeRegalo correo = PlantillaCorreoChequeRegalo.Componer(Campana(imagen: null), "Centro Lola", null, "cid:cheque-regalo");

            Assert.IsFalse(correo.Html.Contains("<img"));
            Assert.IsNull(correo.Imagen);
        }

        [TestMethod]
        public void ElTextoPlanoDiceLoMismoSinHtml()
        {
            CorreoChequeRegalo correo = PlantillaCorreoChequeRegalo.Componer(Campana(), "Centro Lola", IMAGEN, "cid:cheque-regalo");

            StringAssert.StartsWith(correo.Texto, "Hola, Centro Lola:");
            StringAssert.Contains(correo.Texto, "hasta el 7 de noviembre");
            StringAssert.Contains(correo.Texto, "Letra pequeña: Importes expresados en base imponible");
            Assert.IsFalse(correo.Texto.Contains("<"));
        }

        [TestMethod]
        public void ElNombreSeEscapaEnElHtml_YSinNombreSaleElGenerico()
        {
            Assert.IsTrue(PlantillaCorreoChequeRegalo.Componer(Campana(), "Pérez & Hijos", null, null).Html.Contains("Hola, Pérez &amp; Hijos:"));
            Assert.IsTrue(PlantillaCorreoChequeRegalo.Componer(Campana(), "  ", null, null).Html.Contains("Hola, Nombre del cliente:"));
        }

        [TestMethod]
        public void LaNotaAlPieResumeElChequeYRemiteAlCorreo()
        {
            string nota = PlantillaCorreoChequeRegalo.NotaAlPie(new ChequeRegaloDeFactura
            {
                ImporteBase = 50,
                MinimoCanje = 250,
                CanjeHasta = new DateTime(2026, 11, 7),
                FechaAvisoCorreo = new DateTime(2026, 10, 13)
            });

            Assert.AreEqual("Con esta factura tienes un cheque regalo de 50 € para tu próximo pedido de más de 250 € de producto " +
                "computable, hasta el 7/11/2026. Condiciones en el correo que te hemos enviado.", nota);
        }

        [TestMethod]
        public void LaNotaAlPie_SinCorreoMandado_NoDiceQueSeLoHemosEnviado()
        {
            string nota = PlantillaCorreoChequeRegalo.NotaAlPie(new ChequeRegaloDeFactura
            {
                ImporteBase = 50,
                MinimoCanje = 250,
                CanjeHasta = new DateTime(2026, 11, 7)
            });

            Assert.IsFalse(nota.Contains("correo"));
            StringAssert.Contains(nota, "Tu comercial");
        }

        [TestMethod]
        public void LaImagenDeOctubreEstaIncrustadaEnLaApi()
        {
            byte[] imagen = ImagenesChequeRegalo.Leer("cheque50.jpg");

            Assert.IsNotNull(imagen);
            Assert.IsTrue(imagen.Length > 10000);
            Assert.AreEqual(0xFF, imagen[0]);
            Assert.AreEqual(0xD8, imagen[1]);
            Assert.IsNull(ImagenesChequeRegalo.Leer("no_existe.jpg"));
            Assert.IsNull(ImagenesChequeRegalo.Leer(null));
        }

        [TestMethod]
        public void ElMensajeLlevaTextoPlanoYHtmlConLaImagenEnLinea()
        {
            CorreoChequeRegalo correo = PlantillaCorreoChequeRegalo.Componer(Campana(), "Centro Lola", IMAGEN, "cid:cheque-regalo");

            using (MailMessage mail = AvisadorChequesRegalo.CrearMensaje(correo, new[] { "lola@centro.es" }, copiaOculta: true))
            {
                Assert.AreEqual(2, mail.AlternateViews.Count);
                Assert.AreEqual("text/plain", mail.AlternateViews[0].ContentType.MediaType);
                Assert.AreEqual("text/html", mail.AlternateViews[1].ContentType.MediaType);
                LinkedResource imagen = mail.AlternateViews[1].LinkedResources.Single();
                Assert.AreEqual("cheque-regalo", imagen.ContentId);
                Assert.AreEqual("image/jpeg", imagen.ContentType.MediaType);
                Assert.AreEqual("lola@centro.es", mail.To.Single().Address);
                Assert.AreEqual(AvisadorChequesRegalo.REMITENTE_POR_DEFECTO, mail.From.Address);
                Assert.AreEqual(Constantes.Correos.INFORMATICA, mail.Bcc.Single().Address, "Copia oculta como los correos comerciales (post-compra)");
            }
        }
    }

    [TestClass]
    public class AvisadorChequesRegaloTests
    {
        private IRepositorioAvisoChequesRegalo repositorio;
        private IServicioCorreoElectronico servicioCorreo;
        private List<Exception> errores;
        private List<(string Para, string Asunto, int Imagenes, string Bcc)> enviados;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioAvisoChequesRegalo>();
            servicioCorreo = A.Fake<IServicioCorreoElectronico>();
            errores = new List<Exception>();
            enviados = new List<(string, string, int, string)>();
            A.CallTo(() => repositorio.ColumnasDisponibles()).Returns(Task.FromResult(true));
            A.CallTo(() => repositorio.LeerCampana(A<string>._)).Returns(Task.FromResult<CampanaCorreoChequeRegalo>(null));
            A.CallTo(() => repositorio.LeerDestinatario(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult<DestinatarioChequeRegalo>(null));
            A.CallTo(() => repositorio.ReservarAviso(A<int>._)).Returns(Task.FromResult(true));
            A.CallTo(() => repositorio.LeerDestinatario("15000", "1", "NV2600001"))
                .Returns(Task.FromResult(new DestinatarioChequeRegalo { Nombre = "CENTRO LOLA", Correos = "lola@centro.es, mal correo" }));
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => enviados.Add((string.Join(",", m.To.Select(t => t.Address)), m.Subject,
                    m.AlternateViews.Sum(v => v.LinkedResources.Count), string.Join(",", m.Bcc.Select(b => b.Address)))))
                .Returns(true);
        }

        private AvisadorChequesRegalo Nuevo() => new AvisadorChequesRegalo(repositorio, servicioCorreo,
            leerImagen: n => n == null ? null : new byte[] { 0xFF, 0xD8 }, registrarError: errores.Add);

        internal static ChequeRegaloSinAviso Cheque(int id = 7, string cliente = "15000", string factura = "NV2600001", string correoAviso = null)
            => new ChequeRegaloSinAviso
            {
                Id = id,
                Cliente = cliente,
                EmpresaFactura = "1",
                FacturaOrigen = factura,
                CorreoAviso = correoAviso,
                Codigo = "CHEQUE50_OCT_2026",
                ImporteBase = 50,
                MinimoCanje = 250,
                CanjeHasta = new DateTime(2026, 11, 7),
                ImagenCorreo = "cheque50.jpg",
                Activa = true
            };

        private void ChequesSinAviso(string cliente, params ChequeRegaloSinAviso[] cheques)
            => A.CallTo(() => repositorio.LeerChequesSinAviso(cliente)).Returns(Task.FromResult(cheques.ToList()));

        [TestMethod]
        public async Task AlGenerarse_LeMandaElCorreoConLaImagenYLoMarca()
        {
            ChequesSinAviso("15000", Cheque());

            List<AvisoChequeRegalo> avisos = await Nuevo().AvisarCliente("15000");

            Assert.AreEqual(ResultadoAvisoChequeRegalo.Enviado, avisos.Single().Resultado);
            Assert.AreEqual(1, enviados.Count);
            Assert.AreEqual("lola@centro.es", enviados[0].Para, "El correo mal escrito de la ficha se salta");
            Assert.AreEqual("Con tu factura, 50 € de descuento para tu próximo pedido", enviados[0].Asunto);
            Assert.AreEqual(1, enviados[0].Imagenes, "La imagen va en línea (CID)");
            A.CallTo(() => repositorio.ReservarAviso(7)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustHaveHappenedOnceExactly())
                .Then(A.CallTo(() => repositorio.ConfirmarAviso(7, "lola@centro.es")).MustHaveHappenedOnceExactly());
            StringAssert.Contains(avisos[0].TextoParaQuienFactura, "lola@centro.es");
        }

        [TestMethod]
        public async Task SiOtroLoEstaMandando_NoSeMandaDosVeces()
        {
            // La marca va ANTES de mandar (UPDATE … WHERE FechaAvisoCorreo IS NULL): si no entra, otro lo hizo
            ChequesSinAviso("15000", Cheque());
            A.CallTo(() => repositorio.ReservarAviso(7)).Returns(Task.FromResult(false));

            List<AvisoChequeRegalo> avisos = await Nuevo().AvisarCliente("15000");

            Assert.AreEqual(ResultadoAvisoChequeRegalo.YaAvisado, avisos.Single().Resultado);
            Assert.AreEqual(0, enviados.Count);
            Assert.IsNull(avisos[0].TextoParaQuienFactura);
        }

        [TestMethod]
        public async Task SiYaSeLeMando_NoVuelveASalir()
        {
            // El repositorio solo devuelve los que tienen FechaAvisoCorreo NULL
            ChequesSinAviso("15000");

            List<AvisoChequeRegalo> avisos = await Nuevo().AvisarCliente("15000");

            Assert.AreEqual(0, avisos.Count);
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public async Task SinCorreoDeFacturas_NoRompe_YSeApuntaUnaSolaVez()
        {
            A.CallTo(() => repositorio.LeerDestinatario("16000", A<string>._, A<string>._))
                .Returns(Task.FromResult(new DestinatarioChequeRegalo { Nombre = "SIN CORREO", Correos = null }));
            ChequesSinAviso("16000", Cheque(id: 8, cliente: "16000"));

            List<AvisoChequeRegalo> avisos = await Nuevo().AvisarCliente("16000");

            Assert.AreEqual(ResultadoAvisoChequeRegalo.SinCorreo, avisos.Single().Resultado);
            Assert.AreEqual(0, enviados.Count);
            A.CallTo(() => repositorio.ReservarAviso(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.AnularReserva(8, AvisadorChequesRegalo.SIN_CORREO)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(avisos[0].TextoParaQuienFactura, "avísale tú");

            // La noche siguiente sigue sin correo: ni otro ELMAH ni otra escritura
            ChequesSinAviso("16000", Cheque(id: 8, cliente: "16000", correoAviso: AvisadorChequesRegalo.SIN_CORREO));
            _ = await Nuevo().AvisarCliente("16000");
            Assert.AreEqual(1, errores.Count);
            A.CallTo(() => repositorio.AnularReserva(8, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task SiElCorreoNoSale_SeDeshaceLaMarcaParaLaReconciliacion()
        {
            ChequesSinAviso("15000", Cheque());
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(false);

            List<AvisoChequeRegalo> avisos = await Nuevo().AvisarCliente("15000");

            Assert.AreEqual(ResultadoAvisoChequeRegalo.FalloEnvio, avisos.Single().Resultado);
            A.CallTo(() => repositorio.AnularReserva(7, AvisadorChequesRegalo.FALLO_ENVIO)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.ConfirmarAviso(A<int>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task SiElSmtpLanza_SeDeshaceLaMarcaYNoRompe()
        {
            ChequesSinAviso("15000", Cheque());
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).Throws(new SmtpException("caído"));

            List<AvisoChequeRegalo> avisos = await Nuevo().AvisarCliente("15000");

            Assert.AreEqual(ResultadoAvisoChequeRegalo.FalloEnvio, avisos.Single().Resultado);
            A.CallTo(() => repositorio.AnularReserva(7, AvisadorChequesRegalo.FALLO_ENVIO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task SinLasColumnasDelScript_NoHaceNada()
        {
            A.CallTo(() => repositorio.ColumnasDisponibles()).Returns(Task.FromResult(false));

            Assert.AreEqual(0, (await Nuevo().AvisarCliente("15000")).Count);
            Assert.AreEqual(0, await Nuevo().AvisarPendientes());
            A.CallTo(() => repositorio.LeerChequesSinAviso(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LaReconciliacion_MandaLosQueSeQuedaronSinCorreo()
        {
            A.CallTo(() => repositorio.LeerDestinatario("16000", "1", "NV2600002"))
                .Returns(Task.FromResult(new DestinatarioChequeRegalo { Nombre = "OTRO", Correos = "otro@centro.es" }));
            A.CallTo(() => repositorio.LeerDestinatario("17000", "1", "NV2600003")).Throws(new InvalidOperationException("se cae"));
            ChequesSinAviso(null, Cheque(), Cheque(id: 8, cliente: "17000", factura: "NV2600003"),
                Cheque(id: 9, cliente: "16000", factura: "NV2600002", correoAviso: AvisadorChequesRegalo.FALLO_ENVIO));

            int avisados = await Nuevo().AvisarPendientes();

            Assert.AreEqual(2, avisados, "Uno que falle no para a los demás");
            CollectionAssert.AreEquivalent(new[] { "lola@centro.es", "otro@centro.es" }, enviados.Select(e => e.Para).ToList());
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task Previsualizar_ConLaImagenDentroYElNombreDelCliente()
        {
            A.CallTo(() => repositorio.LeerCampana("CHEQUE50_OCT_2026")).Returns(Task.FromResult(PlantillaCorreoChequeRegaloTests.Campana()));
            A.CallTo(() => repositorio.LeerDestinatario("15000", null, null))
                .Returns(Task.FromResult(new DestinatarioChequeRegalo { Nombre = "CENTRO LOLA", Correos = "lola@centro.es" }));

            CorreoChequeRegalo correo = await Nuevo().Previsualizar("CHEQUE50_OCT_2026", "15000");

            StringAssert.Contains(correo.Html, "src=\"data:image/jpeg;base64,");
            StringAssert.Contains(correo.Html, "Hola, CENTRO LOLA:");
            Assert.AreEqual(0, enviados.Count, "Previsualizar no manda nada");
        }

        [TestMethod]
        public async Task EnviarPrueba_ALaDireccionDada_SinMarcarNada()
        {
            A.CallTo(() => repositorio.LeerCampana("CHEQUE50_OCT_2026")).Returns(Task.FromResult(PlantillaCorreoChequeRegaloTests.Campana()));

            bool? enviado = await Nuevo().EnviarPrueba("CHEQUE50_OCT_2026", "alberto@nuevavision.es", null);

            Assert.AreEqual(true, enviado);
            Assert.AreEqual("alberto@nuevavision.es", enviados.Single().Para);
            StringAssert.StartsWith(enviados[0].Asunto, "[PRUEBA] ");
            Assert.AreEqual(1, enviados[0].Imagenes);
            Assert.AreEqual(string.Empty, enviados[0].Bcc);
            A.CallTo(() => repositorio.ReservarAviso(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CampanaQueNoExiste_Null()
        {
            Assert.IsNull(await Nuevo().Previsualizar("NO_EXISTE", null));
            Assert.IsNull(await Nuevo().EnviarPrueba("NO_EXISTE", "a@b.es", null));
        }
    }

    [TestClass]
    public class AvisoChequeRegaloEnFacturaTests
    {
        [TestMethod]
        public async Task AlFacturar_QuienFacturaSabeQueTieneChequeYQueLeHaLlegadoElCorreo()
        {
            IGeneradorChequesRegalo generador = A.Fake<IGeneradorChequesRegalo>();
            IAvisadorChequesRegalo avisador = A.Fake<IAvisadorChequesRegalo>();
            A.CallTo(() => generador.GenerarPorFactura("1", "NV2600001", "15000", A<DateTime>._, "Carlos"))
                .Returns(Task.FromResult(new List<string> { "El cliente 15000 ha recibido un cheque regalo de 50 €" }));
            A.CallTo(() => avisador.AvisarCliente("15000")).Returns(Task.FromResult(new List<AvisoChequeRegalo>
            {
                new AvisoChequeRegalo { Resultado = ResultadoAvisoChequeRegalo.Enviado, Correos = "lola@centro.es" }
            }));
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2600001", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(generador, respuesta, "15000", new DateTime(2026, 10, 13), "Carlos", avisador);

            Assert.AreEqual(2, respuesta.Avisos.Count);
            StringAssert.StartsWith(respuesta.Avisos[0], "El cliente 15000 ha recibido un cheque regalo de 50 €");
            Assert.AreEqual("Le hemos mandado el cheque por correo a lola@centro.es.", respuesta.Avisos[1]);
        }

        [TestMethod]
        public async Task SiLaFacturaNoGeneraCheque_NoSeMandaNada()
        {
            IGeneradorChequesRegalo generador = A.Fake<IGeneradorChequesRegalo>();
            IAvisadorChequesRegalo avisador = A.Fake<IAvisadorChequesRegalo>();
            A.CallTo(() => generador.GenerarPorFactura(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._))
                .Returns(Task.FromResult(new List<string>()));
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2600001", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(generador, respuesta, "15000", new DateTime(2026, 10, 13), "Carlos", avisador);

            Assert.AreEqual(0, respuesta.Avisos.Count);
            A.CallTo(() => avisador.AvisarCliente(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiElCorreoFalla_LaFacturaSigueYQuienFacturaLoSabe()
        {
            IGeneradorChequesRegalo generador = A.Fake<IGeneradorChequesRegalo>();
            IAvisadorChequesRegalo avisador = A.Fake<IAvisadorChequesRegalo>();
            A.CallTo(() => generador.GenerarPorFactura(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._))
                .Returns(Task.FromResult(new List<string> { "Cheque" }));
            A.CallTo(() => avisador.AvisarCliente(A<string>._)).Throws(new InvalidOperationException("sin red"));
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2600001", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(generador, respuesta, "15000", new DateTime(2026, 10, 13), "Carlos", avisador);

            Assert.AreEqual(2, respuesta.Avisos.Count);
            StringAssert.Contains(respuesta.Avisos[1], "No se ha podido mandar el correo del cheque");
        }

        [TestMethod]
        public void ElAvisoAQuienFacturaDiceQueHaRecibidoElCheque()
        {
            string aviso = ReglasChequeRegalo.TextoAviso(ReglasChequeRegaloTests.Campana(), "15000");

            StringAssert.StartsWith(aviso, "El cliente 15000 ha recibido un cheque regalo de 50 €");
            StringAssert.Contains(aviso, "más de 250 € de producto computable");
        }

        private static GestorFacturas GestorConFactura(IServicioFacturas servicio)
        {
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Serie = "NV";
            cab.Fecha = new DateTime(2026, 10, 13);
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Número = "NV11111";
            cab.LinPedidoVtas.Add(new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 10, 13),
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            });
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(new List<VencimientoFactura> { new VencimientoFactura { FormaPago = "EFC", Importe = 20, ImportePendiente = 0 } });
            return new GestorFacturas(servicio);
        }

        [TestMethod]
        public void LaFacturaQueGeneroElCheque_LoDiceAlPie()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            A.CallTo(() => servicio.LeerNotaChequeRegalo(A<string>._, "NV11111")).Returns("Con esta factura tienes un cheque regalo de 50 €");

            Factura factura = GestorConFactura(servicio).LeerFactura("1", "NV11111");

            int notasSerie = new NestoAPI.Models.Facturas.SeriesFactura.SerieNV().Notas.Count;
            Assert.AreEqual(notasSerie + 1, factura.NotasAlPie.Count, "Las notas de la serie se conservan");
            Assert.AreEqual("Con esta factura tienes un cheque regalo de 50 €", factura.NotasAlPie.Last().Nota);
        }

        [TestMethod]
        public void SinCheque_NiNotaNiFallo()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            A.CallTo(() => servicio.LeerNotaChequeRegalo(A<string>._, A<string>._)).Throws(new InvalidOperationException("sin tabla"));

            Factura factura = GestorConFactura(servicio).LeerFactura("1", "NV11111");

            Assert.AreEqual(new NestoAPI.Models.Facturas.SeriesFactura.SerieNV().Notas.Count, factura.NotasAlPie.Count);
        }
    }

    [TestClass]
    public class ChequesRegaloControllerTests
    {
        private IAvisadorChequesRegalo avisador;

        [TestInitialize]
        public void Preparar()
        {
            avisador = A.Fake<IAvisadorChequesRegalo>();
            A.CallTo(() => avisador.Previsualizar(A<string>._, A<string>._)).Returns(Task.FromResult<CorreoChequeRegalo>(null));
            A.CallTo(() => avisador.EnviarPrueba(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult<bool?>(null));
            A.CallTo(() => avisador.Previsualizar("CHEQUE50_OCT_2026", A<string>._))
                .Returns(Task.FromResult(new CorreoChequeRegalo { Html = "<html>cheque</html>" }));
            A.CallTo(() => avisador.EnviarPrueba("CHEQUE50_OCT_2026", A<string>._, A<string>._)).Returns(Task.FromResult<bool?>(true));
        }

        private ChequesRegaloController Controlador(params string[] grupos)
        {
            var controlador = new ChequesRegaloController(avisador)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Alguien"), grupos.Select(g => "NUEVAVISION\\" + g).ToArray()),
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            return controlador;
        }

        [TestMethod]
        public async Task SinSerDireccionNiInformatica_403()
        {
            var previsualizar = await Controlador("Almacén").PrevisualizarCorreo("CHEQUE50_OCT_2026", "15000") as StatusCodeResult;
            var prueba = await Controlador("Almacén").EnviarCorreoPrueba("CHEQUE50_OCT_2026", "a@b.es") as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, previsualizar?.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, prueba?.StatusCode);
            A.CallTo(() => avisador.Previsualizar(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.EnviarPrueba(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Direccion_VeElHtml()
        {
            var resultado = await Controlador("Dirección").PrevisualizarCorreo("CHEQUE50_OCT_2026", "15000") as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("text/html", resultado.Response.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("<html>cheque</html>", await resultado.Response.Content.ReadAsStringAsync());
        }

        [TestMethod]
        public async Task Informatica_MandaLaPrueba_YConCorreoMalEscrito400()
        {
            IHttpActionResult ok = await Controlador("Informatica").EnviarCorreoPrueba("CHEQUE50_OCT_2026", "alberto@nuevavision.es");
            StringAssert.StartsWith(ok.GetType().Name, "OkNegotiatedContentResult");
            A.CallTo(() => avisador.EnviarPrueba("CHEQUE50_OCT_2026", "alberto@nuevavision.es", null)).MustHaveHappenedOnceExactly();

            Assert.IsInstanceOfType(await Controlador("Informatica").EnviarCorreoPrueba("CHEQUE50_OCT_2026", "no es un correo"),
                typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task CampanaQueNoExiste_404()
        {
            Assert.IsInstanceOfType(await Controlador("Dirección").PrevisualizarCorreo("NO_EXISTE"), typeof(NotFoundResult));
        }
    }
}
