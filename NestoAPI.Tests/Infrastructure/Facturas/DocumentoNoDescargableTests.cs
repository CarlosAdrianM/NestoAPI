using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.EnlacesDescarga;
using NestoAPI.Infraestructure.EnvioDocumentosCorreo;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Facturas
{
    /// <summary>
    /// Carlos 25/11/26: «antes no se podían descargar facturas de la empresa 3, ni pedidos con el IVA a NULL». LA REGLA
    /// <see cref="ReglaDocumentoNoDescargable"/>: nada de la empresa espejo, ni una factura de una serie sin descarga (GB), ni el
    /// documento de un pedido que va a la espejo (IVA de la cabecera NULL o en blanco) sale hacia fuera. Lo interno sigue igual.
    /// </summary>
    [TestClass]
    public class DocumentoNoDescargableTests
    {
        private const int PEDIDO = 931600;

        // ================================================================ LA REGLA (pura)

        [TestMethod]
        public void Regla_EmpresaEspejo_NoSeSirveNada()
        {
            Assert.IsTrue(ReglaDocumentoNoDescargable.EsEmpresaEspejo("3"));
            Assert.IsTrue(ReglaDocumentoNoDescargable.EsEmpresaEspejo(" 3 "));
            Assert.IsFalse(ReglaDocumentoNoDescargable.EsEmpresaEspejo("1"));
            Assert.IsFalse(ReglaDocumentoNoDescargable.EsEmpresaEspejo(null));
            Assert.IsTrue(ReglaDocumentoNoDescargable.NoSeSirveFactura("3", "NV2616199"));
            Assert.IsTrue(ReglaDocumentoNoDescargable.NoSeSirvePedido("3", "G21"));
        }

        [TestMethod]
        public void Regla_FacturaDeSerieSinDescarga_ComoHoy()
        {
            Assert.IsTrue(ReglaDocumentoNoDescargable.NoSeSirveFactura("1", "GB2600123"));
            Assert.IsFalse(ReglaDocumentoNoDescargable.NoSeSirveFactura("1", "NV2616199"));
            Assert.IsFalse(ReglaDocumentoNoDescargable.NoSeSirveFactura("1", "CV2600010"));
            Assert.IsFalse(ReglaDocumentoNoDescargable.NoSeSirveFactura("1", null));
            Assert.IsFalse(ReglaDocumentoNoDescargable.NoSeSirveFactura("1", "N"));
        }

        [TestMethod]
        public void Regla_PedidoConIvaNuloOEnBlanco_NoSeSirve()
        {
            Assert.IsTrue(ReglaDocumentoNoDescargable.NoSeSirvePedido("1", null));
            Assert.IsTrue(ReglaDocumentoNoDescargable.NoSeSirvePedido("1", ""));
            Assert.IsTrue(ReglaDocumentoNoDescargable.NoSeSirvePedido("1", "   "));
            Assert.IsFalse(ReglaDocumentoNoDescargable.NoSeSirvePedido("1", "G21"));
        }

        [TestMethod]
        public void Regla_MensajeUnicoDeUsted()
        {
            Assert.AreEqual("El documento 931600 no se puede descargar.", ReglaDocumentoNoDescargable.Mensaje(" 931600 "));
            NestoBusinessException ex = ReglaDocumentoNoDescargable.Excepcion("GB2600123");
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual("El documento GB2600123 no se puede descargar.", ex.Message);
        }

        [TestMethod]
        public void Regla_NumeroDePedido_ComoLeerFacturas()
        {
            Assert.IsTrue(ReglaDocumentoNoDescargable.EsNumeroDePedido("931600", out int pedido));
            Assert.AreEqual(931600, pedido);
            Assert.IsFalse(ReglaDocumentoNoDescargable.EsNumeroDePedido("NV2616199", out _));
            Assert.IsFalse(ReglaDocumentoNoDescargable.EsNumeroDePedido("-5", out _));
        }

        // ================================================================ el comprobador (lee la cabecera)

        private static IServicioFacturas ServicioConPedido(string iva, int numero = PEDIDO)
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            A.CallTo(() => servicio.CargarCabPedido("1", numero)).Returns(new CabPedidoVta { Empresa = "1", Número = numero, IVA = iva });
            A.CallTo(() => servicio.CargarCabPedidoPorAlbaran("1", 812345)).Returns(new CabPedidoVta { Empresa = "1", Número = numero, IVA = iva });
            return servicio;
        }

        [TestMethod]
        public void Comprobador_PedidoSinIva_403()
        {
            var comprobador = new ComprobadorDocumentoDescargable(ServicioConPedido(null));

            NestoBusinessException ex = Assert.ThrowsException<NestoBusinessException>(() => comprobador.ComprobarDocumento("1", "931600"));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual("El documento 931600 no se puede descargar.", ex.Message);
        }

        [TestMethod]
        public void Comprobador_PedidoConIva_Sigue()
        {
            new ComprobadorDocumentoDescargable(ServicioConPedido("G21")).ComprobarDocumento("1", "931600");
        }

        [TestMethod]
        public void Comprobador_PedidoQueNoExiste_SigueParaDarSuNoExiste()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            A.CallTo(() => servicio.CargarCabPedido(A<string>._, A<int>._)).Returns(null);

            new ComprobadorDocumentoDescargable(servicio).ComprobarPedido("1", 999999);
        }

        [TestMethod]
        public void Comprobador_AlbaranDeUnPedidoSinIva_403()
        {
            var comprobador = new ComprobadorDocumentoDescargable(ServicioConPedido(" "));

            NestoBusinessException ex = Assert.ThrowsException<NestoBusinessException>(() => comprobador.ComprobarAlbaran("1", 812345));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual("El documento 812345 no se puede descargar.", ex.Message);
            new ComprobadorDocumentoDescargable(ServicioConPedido("G21")).ComprobarAlbaran("1", 812345);
        }

        [TestMethod]
        public void Comprobador_EmpresaEspejo_NoLeeNiLaCabecera()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            var comprobador = new ComprobadorDocumentoDescargable(servicio);

            _ = Assert.ThrowsException<NestoBusinessException>(() => comprobador.ComprobarPedido("3", PEDIDO));
            _ = Assert.ThrowsException<NestoBusinessException>(() => comprobador.ComprobarAlbaran("3", 812345));
            _ = Assert.ThrowsException<NestoBusinessException>(() => comprobador.ComprobarDocumento("3", "NV2616199"));
            A.CallTo(() => servicio.CargarCabPedido(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        // ================================================================ GET api/Facturas (PDF) y FacturaJson

        private static FacturasController Controlador(IServicioFacturas servicio, IGestorFacturas gestor)
        {
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\Paloma"));
            identity.AddClaim(new Claim("IsEmployee", "true"));
            return new FacturasController(servicio, gestor) { User = new ClaimsPrincipal(identity) };
        }

        private static IGestorFacturas GestorQueGenera()
        {
            IGestorFacturas gestor = A.Fake<IGestorFacturas>();
            A.CallTo(() => gestor.LeerFacturas(A<List<FacturaLookup>>._)).Returns(new List<Factura> { new Factura() });
            A.CallTo(() => gestor.FacturasEnPDF(A<List<Factura>>._, A<bool>._, A<string>._, A<bool>._, A<bool>._))
                .ReturnsLazily(() => new ByteArrayContent(new byte[] { 1, 2, 3 }));
            A.CallTo(() => gestor.LeerFactura(A<string>._, A<string>._)).Returns(new Factura());
            return gestor;
        }

        [TestMethod]
        public async Task GetFactura_PdfDeUnPedidoSinIva_403SinGenerarNada()
        {
            IGestorFacturas gestor = GestorQueGenera();

            HttpResponseMessage respuesta = await Controlador(ServicioConPedido(null), gestor).GetFactura("1", "931600");

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.AreEqual("El documento 931600 no se puede descargar.", await respuesta.Content.ReadAsStringAsync());
            A.CallTo(() => gestor.LeerFacturas(A<List<FacturaLookup>>._)).MustNotHaveHappened();
            A.CallTo(() => gestor.FacturasEnPDF(A<List<Factura>>._, A<bool>._, A<string>._, A<bool>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetFactura_PdfDeUnPedidoConIva_SigueSaliendo()
        {
            HttpResponseMessage respuesta = await Controlador(ServicioConPedido("G21"), GestorQueGenera()).GetFactura("1", "931600");

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("application/pdf", respuesta.Content.Headers.ContentType.MediaType);
        }

        [TestMethod]
        public async Task GetFactura_FacturaDeSerieSinDescarga_403ComoHoy()
        {
            IGestorFacturas gestor = GestorQueGenera();

            HttpResponseMessage respuesta = await Controlador(A.Fake<IServicioFacturas>(), gestor).GetFactura("1", "GB2600123");

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.AreEqual("El documento GB2600123 no se puede descargar.", await respuesta.Content.ReadAsStringAsync());
            A.CallTo(() => gestor.LeerFacturas(A<List<FacturaLookup>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetFactura_FacturaDeLaEmpresaEspejo_403()
        {
            HttpResponseMessage respuesta = await Controlador(A.Fake<IServicioFacturas>(), GestorQueGenera()).GetFactura("3", "NV2600001");

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }

        [TestMethod]
        public async Task GetFacturaJson_DeUnPedidoSinIva_403()
        {
            IGestorFacturas gestor = GestorQueGenera();

            var resultado = await Controlador(ServicioConPedido(""), gestor).GetFacturaJson("1", "931600") as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual("El documento 931600 no se puede descargar.", resultado.Content);
            A.CallTo(() => gestor.LeerFactura(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetFacturaJson_FacturaNormal_SigueSaliendo()
        {
            IHttpActionResult resultado = await Controlador(A.Fake<IServicioFacturas>(), GestorQueGenera()).GetFacturaJson("1", "NV2616199");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<Factura>));
        }

        [TestMethod]
        public async Task EnviarFacturasPorCorreo_DeLaEmpresaEspejo_403SinMandarNada()
        {
            IGestorFacturas gestor = GestorQueGenera();

            var resultado = await Controlador(A.Fake<IServicioFacturas>(), gestor).EnviarPorCorreo(new EnvioFacturasCorreoDTO
            {
                Empresa = "3",
                Facturas = new List<string> { "GB2600123" },
                Correos = new List<string> { "cliente@correo.es" }
            }) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual("El documento GB2600123 no se puede descargar.", resultado.Content);
            A.CallTo(() => gestor.EnviarFacturasACorreo(A<string>._, A<IEnumerable<string>>._, A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }

        // ================================================================ enlace de descarga (WhatsApp) y correo

        private const string TOKEN = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";

        [TestMethod]
        public async Task EnlaceDescarga_PedidoSinIva_NiSeCrea()
        {
            IRepositorioEnlacesDescarga repositorio = A.Fake<IRepositorioEnlacesDescarga>();
            IProveedorDocumentosEnlace proveedor = A.Fake<IProveedorDocumentosEnlace>();
            A.CallTo(() => proveedor.LeerDatos(TiposDocumentoDescarga.PEDIDO, "1", "931600"))
                .ThrowsAsync(ReglaDocumentoNoDescargable.Excepcion("931600"));
            var gestor = new GestorEnlacesDescarga(repositorio, proveedor, () => new DateTime(2026, 11, 25, 10, 0, 0), () => "https://api/", () => TOKEN);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => gestor.Crear(new PeticionEnlaceDescargaDTO { Tipo = "Pedido", Empresa = "1", Numero = "931600" }, "NUEVAVISION\\Paloma"));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual("El documento 931600 no se puede descargar.", ex.Message);
            A.CallTo(() => repositorio.Guardar(A<EnlaceDescargaDocumento>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnlaceDescarga_EnlaceAntiguoDeUnPedidoQueAhoraEsSinIva_PaginaDeNoDisponible()
        {
            IRepositorioEnlacesDescarga repositorio = A.Fake<IRepositorioEnlacesDescarga>();
            IProveedorDocumentosEnlace proveedor = A.Fake<IProveedorDocumentosEnlace>();
            A.CallTo(() => repositorio.Leer(TOKEN)).Returns(Task.FromResult(new EnlaceDescargaDocumento
            {
                Token = TOKEN, Tipo = TiposDocumentoDescarga.PEDIDO, Empresa = "1", Numero = "931600",
                Creado = new DateTime(2026, 11, 20), Caduca = new DateTime(2026, 12, 20)
            }));
            A.CallTo(() => proveedor.Pdf(TiposDocumentoDescarga.PEDIDO, "1", "931600")).ThrowsAsync(ReglaDocumentoNoDescargable.Excepcion("931600"));
            var gestor = new GestorEnlacesDescarga(repositorio, proveedor, () => new DateTime(2026, 11, 25, 10, 0, 0), () => "https://api/", () => TOKEN);

            DescargaDocumento descarga = await gestor.Descargar(TOKEN);

            Assert.IsFalse(descarga.EsPdf);
            Assert.AreEqual(HttpStatusCode.NotFound, descarga.Estado);
            A.CallTo(() => repositorio.RegistrarDescarga(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CorreoDelPedido_SinIva_403SinMandarNada()
        {
            IProveedorDocumentosCorreo proveedor = A.Fake<IProveedorDocumentosCorreo>();
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => proveedor.PdfPedido("1", PEDIDO, A<string>._)).ThrowsAsync(ReglaDocumentoNoDescargable.Excepcion("931600"));
            var gestor = new GestorEnvioDocumentosCorreo(proveedor, correo);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => gestor.EnviarPedido("1", PEDIDO,
                new EnvioDocumentoCorreoDTO { Destinatarios = new List<string> { "cliente@correo.es" } }, "NUEVAVISION\\Paloma"));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        // ================================================================ lo interno sigue leyendo el pedido sin IVA

        private static IServicioFacturas ServicioConPedidoCompleto(string iva, bool notaEntrega)
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabPedidoVta cab = A.Fake<CabPedidoVta>();
            cab.Empresa = "1";
            cab.Número = PEDIDO;
            cab.IVA = iva;
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            cab.CCC = "1";
            cab.Forma_Pago = "EFC";
            cab.Fecha = new DateTime(2026, 11, 25);
            cab.Primer_Vencimiento = new DateTime(2026, 11, 25);
            cab.PlazosPago = "UnPlazo";
            cab.NotaEntrega = notaEntrega;
            A.CallTo(() => servicio.CargarPlazosPago(A<string>.Ignored, A<string>.Ignored)).Returns(new PlazoPago { Número = "UnPlazo", Nº_Plazos = 1 });
            cab.LinPedidoVtas.Add(new LinPedidoVta
            {
                Cantidad = 1,
                Texto = "PRODUCTO SIN IVA",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 20M,
                ImporteIVA = 0,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = 0,
                PorcentajeRE = 0M,
                Estado = (short)(notaEntrega ? Constantes.EstadosLineaVenta.EN_CURSO : Constantes.EstadosLineaVenta.PRESUPUESTO)
            });
            A.CallTo(() => servicio.CargarCabPedido("1", PEDIDO)).Returns(cab);
            return servicio;
        }

        [TestMethod]
        public void Interno_LeerPedidoSinIva_SigueLeyendose()
        {
            // Facturar rutas (GestorFacturacionRutas) y el documento de la nota de entrega del repartidor (ServicioDocumentoNotaEntrega)
            // leen con GestorFacturas.LeerPedido: la regla NO está ahí dentro
            IGestorFacturas gestor = new GestorFacturas(ServicioConPedidoCompleto(null, notaEntrega: false));

            Factura factura = gestor.LeerPedido("1", PEDIDO);

            Assert.AreEqual(PEDIDO.ToString(), factura.NumeroFactura);
            Assert.AreEqual(20M, factura.ImporteTotal);
        }

        [TestMethod]
        public void Interno_NotaDeEntregaSinIva_SigueLeyendose()
        {
            IGestorFacturas gestor = new GestorFacturas(ServicioConPedidoCompleto("", notaEntrega: true));

            Factura factura = gestor.LeerPedido("1", PEDIDO);

            Assert.AreEqual(Constantes.Facturas.TiposDocumento.NOTA_ENTREGA, factura.TipoDocumento);
        }
    }
}
