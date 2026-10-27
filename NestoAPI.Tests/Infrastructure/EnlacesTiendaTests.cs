using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Depositos;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models;
using NestoAPI.Models.Depositos;
using NestoAPI.Tests.Infrastructure.ChequesRegalo;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#706: todo enlace a la tienda en un correo lleva utm_source, utm_medium y utm_campaign, y se construye en
    /// un único sitio (EnlacesTienda).
    /// </summary>
    [TestClass]
    public class EnlacesTiendaTests
    {
        private const string FICHA = "https://www.productosdeesteticaypeluqueriaprofesional.com/123-crema.html";

        #region Constructor común

        [TestMethod]
        public void ConUtm_UrlLimpia_AnadeLasTresEnOrden()
        {
            Assert.AreEqual(FICHA + "?utm_source=a&utm_medium=email&utm_campaign=c", EnlacesTienda.ConUtm(FICHA, "a", "email", "c"));
        }

        [TestMethod]
        public void ConUtm_QuitaLasHeredadasYNoDuplica()
        {
            string heredada = FICHA + "?utm_source=nuevavision&utm_campaign=nesto&utm_medium=correopedido";

            string resultado = EnlacesTienda.ConUtm(heredada, "nesto", "email", "correo_pedido");

            Assert.AreEqual(FICHA + "?utm_source=nesto&utm_medium=email&utm_campaign=correo_pedido", resultado);
            Assert.AreEqual(resultado, EnlacesTienda.ConUtm(resultado, "nesto", "email", "correo_pedido"), "Aplicarlo dos veces no duplica");
        }

        [TestMethod]
        public void ConUtm_ConservaOtrosParametrosYElFragmento()
        {
            string resultado = EnlacesTienda.ConUtm("https://tienda.es/p.html?id=7&UTM_SOURCE=vieja&color=rojo#opiniones", "s", "email", "c");

            Assert.AreEqual("https://tienda.es/p.html?id=7&color=rojo&utm_source=s&utm_medium=email&utm_campaign=c#opiniones", resultado);
        }

        [TestMethod]
        public void ConUtm_ContentYTerm_VanDetras()
        {
            Assert.AreEqual(FICHA + "?utm_source=s&utm_medium=email&utm_campaign=c&utm_content=recomendado&utm_term=12345",
                EnlacesTienda.ConUtm(FICHA, "s", "email", "c", "recomendado", "12345"));
        }

        [TestMethod]
        public void ConUtm_SinUrlOUrlNoValida_DevuelveNull()
        {
            Assert.IsNull(EnlacesTienda.ConUtm(null, "s", "email", "c"));
            Assert.IsNull(EnlacesTienda.ConUtm("", "s", "email", "c"));
            Assert.IsNull(EnlacesTienda.ConUtm("   ", "s", "email", "c"));
            Assert.IsNull(EnlacesTienda.ConUtm("&utm_medium=correopedido", "s", "email", "c"));
            Assert.IsNull(EnlacesTienda.ConUtm("ftp://tienda.es/p.html", "s", "email", "c"));
        }

        [TestMethod]
        public void ConUtm_EscapaLosValores()
        {
            StringAssert.EndsWith(EnlacesTienda.ConUtm(FICHA, "s", "email", "c", null, "A&B 1"), "&utm_term=A%26B%201");
        }

        [TestMethod]
        public void QuitarUtm_DejaLaUrlSinUtm()
        {
            Assert.AreEqual(FICHA, EnlacesTienda.QuitarUtm(FICHA + "?utm_source=x&utm_medium=y"));
            Assert.AreEqual(FICHA + "?id=1", EnlacesTienda.QuitarUtm(FICHA + "?utm_source=x&id=1"));
            Assert.IsNull(EnlacesTienda.QuitarUtm(null));
        }

        [TestMethod]
        public void ParaPantallaNesto_MantieneLasDeSiempre()
        {
            Assert.AreEqual(FICHA + "?utm_source=nuevavision&utm_campaign=nesto", EnlacesTienda.ParaPantallaNesto(FICHA));
            Assert.IsNull(EnlacesTienda.ParaPantallaNesto(null));
        }

        [TestMethod]
        public void GooglePlay_LlevaElReferrerCodificado()
        {
            Assert.AreEqual("https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas" +
                "&referrer=utm_source%3Dcorreo_envio%26utm_medium%3Demail", EnlacesTienda.GooglePlay("correo_envio"));
        }

        #endregion

        #region ProductoDTO.RutaEnlace (lo que responde el PHP)

        [TestMethod]
        public void NormalizarRutaEnlace_UrlBuena_LaDevuelveLimpia()
        {
            Assert.AreEqual(FICHA, ProductoDTO.NormalizarRutaEnlace(FICHA + "\r\n"));
            Assert.AreEqual(FICHA, ProductoDTO.NormalizarRutaEnlace(FICHA + "?utm_source=nuevavision&utm_campaign=nesto"));
        }

        [TestMethod]
        public void NormalizarRutaEnlace_VacioOSinProducto_DevuelveNull()
        {
            Assert.IsNull(ProductoDTO.NormalizarRutaEnlace(null));
            Assert.IsNull(ProductoDTO.NormalizarRutaEnlace(""));
            Assert.IsNull(ProductoDTO.NormalizarRutaEnlace("https://www.productosdeesteticaypeluqueriaprofesional.com/-.html"));
            Assert.IsNull(ProductoDTO.NormalizarRutaEnlace("https://www.productosdeesteticaypeluqueriaprofesional.com/-.html?x=1"));
        }

        #endregion

        #region Un test por correo: sus enlaces llevan las tres UTM

        [TestMethod]
        public void CorreoPedido_EnlaceProducto_LlevaNestoEmailCorreoPedido_YSinUrlNoHayEnlace()
        {
            Assert.AreEqual(FICHA + "?utm_source=nesto&utm_medium=email&utm_campaign=correo_pedido", GestorPresupuestos.EnlaceProductoCorreo(FICHA));
            Assert.IsNull(GestorPresupuestos.EnlaceProductoCorreo(null));
        }

        [TestMethod]
        public void CorreoDeposito_LaTablaEnlazaConNestoEmailCorreoDeposito_YSinUrlNoPintaEnlace()
        {
            var correo = new DepositoCorreoProveedor
            {
                DatosCorreo = new List<DatosCorreoDeposito>
                {
                    new DatosCorreoDeposito { ProductoId = "123", Nombre = "CREMA", Enlace = FICHA },
                    new DatosCorreoDeposito { ProductoId = "456", Nombre = "SIN FICHA", Enlace = null }
                }
            };

            string html = GestorDepositos.GenerarTablaHTML(correo).ToString();

            StringAssert.Contains(html, "<a href=\"" + FICHA + "?utm_source=nesto&utm_medium=email&utm_campaign=correo_deposito\">CREMA</a>");
            StringAssert.Contains(html, "<td>SIN FICHA</td>");
            Assert.IsFalse(html.Contains("correodeposito"));
        }

        [TestMethod]
        public void CorreoEnvio_FirmaEnlazaALaLandingConSusUtm_SinBitly()
        {
            string firma = GestorEnviosAgencia.FirmaCorreoEnvio();

            StringAssert.Contains(firma, "href=\"https://www.productosdeesteticaypeluqueriaprofesional.com/40110-ofertas?utm_source=correo_envio&utm_medium=email&utm_campaign=firma_envio\"");
            Assert.IsFalse(firma.Contains("bit.ly"));
        }

        [TestMethod]
        public void CorreoEnvio_GooglePlayLlevaElReferrer()
        {
            string pie = GestorEnviosAgencia.GenerarPiePromocionApp(null);

            StringAssert.Contains(pie, "&referrer=utm_source%3Dcorreo_envio%26utm_medium%3Demail");
        }

        [TestMethod]
        public void CorreoFactura_GooglePlayLlevaElReferrer()
        {
            StringAssert.Contains(GestorFacturas.GenerarSeccionDescargaApp(), "&referrer=utm_source%3Dcorreo_factura%26utm_medium%3Demail");
        }

        [TestMethod]
        public void CorreoReposicion_MantieneSusUtm()
        {
            Assert.AreEqual(FICHA + "?utm_source=correo_reposicion&utm_medium=email&utm_campaign=reposicion",
                PlantillaRecordatorioReposicion.ConUtm(FICHA + "?utm_source=nuevavision&utm_medium=email&utm_campaign=postcompra"));
            Assert.IsNull(PlantillaRecordatorioReposicion.ConUtm(null));
        }

        [TestMethod]
        public void CorreoChequeRegalo_LlevaElBotonALaTiendaConLasTresUtm_EnHtmlYTexto()
        {
            CorreoChequeRegalo correo = PlantillaCorreoChequeRegalo.Componer(PlantillaCorreoChequeRegaloTests.Campana(), "Lola", null, null);
            const string ENLACE = "https://www.productosdeesteticaypeluqueriaprofesional.com/?utm_source=correo_cheque_regalo&utm_medium=email&utm_campaign=CHEQUE50_OCT_2026";

            StringAssert.Contains(correo.Html, "href=\"" + ENLACE.Replace("&", "&amp;") + "\"");
            StringAssert.Contains(correo.Html, ">Canjéelo en la tienda online</a>");
            StringAssert.Contains(correo.Texto, "Canjéelo en la tienda online: " + ENLACE);
            Assert.IsTrue(correo.Html.IndexOf("Canjéelo en la tienda online") < correo.Html.IndexOf("Letra pequeña"), "El botón va antes de la letra pequeña");
        }

        [TestMethod]
        public void EtiquetaImpresa_QrLlevaMediumQr_YSinFichaNoHayQr()
        {
            Assert.AreEqual(FICHA + "?utm_source=nuevavision&utm_medium=qr&utm_campaign=tienda_alcobendas", GeneradorPdfEtiquetasTienda.UrlQr(FICHA));
            Assert.IsNull(GeneradorPdfEtiquetasTienda.UrlQr(""));
            Assert.IsNull(GeneradorPdfEtiquetasTienda.UrlQr("https://www.productosdeesteticaypeluqueriaprofesional.com/-.html"));
        }

        #endregion
    }
}
