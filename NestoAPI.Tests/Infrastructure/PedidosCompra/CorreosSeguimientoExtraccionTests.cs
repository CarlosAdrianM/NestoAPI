using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#857: extracción pura de enlaces y códigos de seguimiento (todos los correos son INVENTADOS).</summary>
    [TestClass]
    public class CorreosSeguimientoExtraccionTests
    {
        private static CorreoCompras Html(string cuerpo, string asunto = "Su envío", string remitente = "avisos@ejemplo-tienda.es")
            => new CorreoCompras
            {
                Id = "AAA",
                MensajeId = "<" + Guid.NewGuid() + "@ejemplo.test>",
                Recibido = new DateTime(2026, 11, 12, 10, 0, 0),
                RemitenteDireccion = remitente,
                Asunto = asunto,
                Cuerpo = cuerpo,
                CuerpoEsHtml = true
            };

        private static CorreoCompras Texto(string cuerpo, string asunto = "Su envío", string remitente = "avisos@ejemplo-tienda.es")
        {
            CorreoCompras correo = Html(cuerpo, asunto, remitente);
            correo.CuerpoEsHtml = false;
            return correo;
        }

        private static AnalisisCorreoSeguimiento Analizar(CorreoCompras correo, params RemitenteMarcado[] marcados)
            => AnalizadorCorreoSeguimiento.Analizar(correo, AgenciasEntradaRegistradas.Todas, marcados);

        private static string Safelink(string destino)
            => "https://eur01.safelinks.protection.outlook.com/?url=" + Uri.EscapeDataString(destino) + "&amp;data=05%7C01%7Cxx&amp;reserved=0";

        #region Enlaces

        [TestMethod]
        public void Extraer_HrefsYUrlsEnTexto_SinRepetir()
        {
            List<string> enlaces = ExtractorEnlaces.Extraer(
                "<p>Mire <a href=\"https://www.ejemplo.com/a?x=1&amp;y=2\">aquí</a> o https://www.ejemplo.com/a?x=1&amp;y=2.</p>" +
                "<a href='http://otro.ejemplo.org/b'>b</a><a href=\"mailto:alguien@ejemplo.com\">c</a>", true);

            CollectionAssert.AreEqual(new[] { "https://www.ejemplo.com/a?x=1&y=2", "http://otro.ejemplo.org/b" }, enlaces);
        }

        [TestMethod]
        public void Extraer_EnTextoPlano_QuitaLaPuntuacionFinal()
        {
            List<string> enlaces = ExtractorEnlaces.Extraer("Siga su envío en https://www.ejemplo.com/t/ABC123456). Gracias.", false);

            CollectionAssert.AreEqual(new[] { "https://www.ejemplo.com/t/ABC123456" }, enlaces);
        }

        [TestMethod]
        public void Extraer_SinCuerpo_NoHayEnlaces()
        {
            Assert.AreEqual(0, ExtractorEnlaces.Extraer(null, true).Count);
            Assert.AreEqual(0, ExtractorEnlaces.Extraer("Hola, sin enlaces.", false).Count);
        }

        [TestMethod]
        public void Desenvolver_SafelinkDeOutlook()
        {
            string destino = "https://www.gls-spain.es/es/recibir-paquetes/seguimiento-envio/?match=61771234567";
            Assert.AreEqual(destino, ExtractorEnlaces.Desenvolver(System.Net.WebUtility.HtmlDecode(Safelink(destino))));
        }

        [TestMethod]
        public void Desenvolver_RedireccionDentroDeSafelink_DosCapas()
        {
            string destino = "https://www.mrw.es/seguimiento_envios/MRW_resultados_consultas.asp?envio=0274512345678";
            string clic = "https://click.ejemplo-boletin.com/track/click?u=abc&redirect=" + Uri.EscapeDataString(destino);
            Assert.AreEqual(destino, ExtractorEnlaces.Desenvolver(System.Net.WebUtility.HtmlDecode(Safelink(clic))));
        }

        [TestMethod]
        public void Desenvolver_GoogleYProofpoint()
        {
            Assert.AreEqual("https://www.ups.com/track?tracknum=1Z999AA10123456784",
                ExtractorEnlaces.Desenvolver("https://www.google.com/url?q=https%3A%2F%2Fwww.ups.com%2Ftrack%3Ftracknum%3D1Z999AA10123456784&sa=D"));
            Assert.AreEqual("https://www.dhl.com/es-es/home/tracking.html",
                ExtractorEnlaces.Desenvolver("https://urldefense.proofpoint.com/v2/url?u=https-3A__www.dhl.com_es-2Des_home_tracking.html&d=DwMF"));
        }

        [TestMethod]
        public void Desenvolver_LoQueNoSePuedeSinPeticion_SeQuedaIgual()
        {
            const string opaco = "https://ejemplo.list-manage.com/track/click?u=1a2b3c&id=9z8y7x";
            Assert.AreEqual(opaco, ExtractorEnlaces.Desenvolver(opaco));
        }

        #endregion

        #region Agencias

        [DataTestMethod]
        [DataRow("https://www.gls-spain.es/es/recibir-paquetes/seguimiento-envio/?match=61771234567", "GLS", "61771234567")]
        [DataRow("https://gls-group.eu/track/ZX12AB34", "GLS", "ZX12AB34")]
        [DataRow("https://www.seur.com/livetracking/?segOnlineIdentificador=08123456789012", "SEUR", "08123456789012")]
        [DataRow("https://www.mrw.es/seguimiento_envios/MRW_resultados_consultas.asp?modo=nacional&envio=0274512345678", "MRW", "0274512345678")]
        [DataRow("https://www.dhl.com/es-es/home/tracking/tracking-parcel.html?submit=1&tracking-id=JJD000390001234567", "DHL", "JJD000390001234567")]
        [DataRow("https://www.ups.com/track?loc=es_ES&tracknum=1Z999AA10123456784", "UPS", "1Z999AA10123456784")]
        [DataRow("https://s.correosexpress.com/SeguimientoSinCP/search?shippingNumber=3230001234567890", "Correos Express", "3230001234567890")]
        [DataRow("https://www.correos.es/es/es/herramientas/localizador/envios/detalle?tracking-number=PQ4BAA0123456780128001X", "Correos", "PQ4BAA0123456780128001X")]
        [DataRow("https://www.cttexpress.com/localizador-de-envios/?sc=0082800012345678", "CTT Express", "0082800012345678")]
        [DataRow("https://www.nacex.es/seguimientoDetalle.do?agencia_origen=2801&numero_albaran=12345678", "Nacex", "12345678")]
        [DataRow("https://www.fedex.com/fedextrack/?trknbr=771234567890", "TNT/FedEx", "771234567890")]
        [DataRow("https://www.tnt.com/express/es_es/site/herramientas-envio/seguimiento.html?searchType=con&cons=323456789", "TNT/FedEx", "323456789")]
        [DataRow("https://elogistics.dachser.com/shp2/?l=es&q=DE1234567890", "Dachser", "DE1234567890")]
        [DataRow("https://www.dsv.com/es-es/herramientas/rastreo-de-envios?trackingNumber=DSV7712345", "DSV", "DSV7712345")]
        [DataRow("https://www.tip-sa.com/es/seguimiento?ref=280012345678", "TIPSA", "280012345678")]
        [DataRow("https://www.envialia.com/seguimiento/?nexp=0012345678", "Envialia", "0012345678")]
        [DataRow("https://www.zeleris.com/seguimiento_envio.aspx?id_seguimiento=ZL1234567", "Zeleris", "ZL1234567")]
        [DataRow("https://www.redur.es/seguimiento?ref=RD20261112", "Redur/Rhenus", "RD20261112")]
        [DataRow("https://www.xpo.com/tracking/?ref=XP99887766", "XPO/GXO", "XP99887766")]
        [DataRow("https://www.dbschenker.com/app/tracking-public/?refNumber=ESMAD1234567&refType=WaybillNo", "DB Schenker", "ESMAD1234567")]
        [DataRow("https://tracking.dpd.de/status/es_ES/parcel/01234567890123", "DPD", "01234567890123")]
        [DataRow("https://www.palletways.com/es/tracking?ref=PW123456", "Palletways", "PW123456")]
        [DataRow("https://tracking.mbe.es/?c=ABC12345", "MBE", "ABC12345")]
        public void CadaAgencia_SeReconoceYSacaElCodigo(string url, string agencia, string codigo)
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Html($"<p>Su pedido va de camino: <a href=\"{url.Replace("&", "&amp;")}\">seguir envío</a></p>"));

            Assert.IsTrue(analisis.Guardar, url);
            Assert.AreEqual(agencia, analisis.Agencia, url);
            Assert.AreEqual(codigo, analisis.Seguimientos.Single().Codigo, url);
            Assert.AreEqual(url, analisis.Seguimientos.Single().Url);
            Assert.IsTrue(analisis.HorasHastaEntrega == 24 || analisis.HorasHastaEntrega == 48);
        }

        [TestMethod]
        public void Agencias_CorreosNoSeConfundeConCorreosExpress()
        {
            Assert.AreEqual("Correos Express", Analizar(Html("<a href=\"https://www.correosexpress.com/web/correosexpress/seguimiento?n=3230001234567\">x</a>")).Agencia);
            Assert.AreEqual("Correos", Analizar(Html("<a href=\"https://www.correos.es/es/es/herramientas/localizador/envios/detalle?tracking-number=PQ4BAA012345678\">x</a>")).Agencia);
        }

        [TestMethod]
        public void Agencias_TodasTienenNombreDistintoYHorasDe24O48()
        {
            Assert.AreEqual(AgenciasEntradaRegistradas.Todas.Count, AgenciasEntradaRegistradas.Todas.Select(a => a.Nombre).Distinct().Count());
            Assert.IsTrue(AgenciasEntradaRegistradas.Todas.All(a => a.HorasHastaEntrega == 24 || a.HorasHastaEntrega == 48));
        }

        [TestMethod]
        public void EnlacesDeLaAgenciaQueNoSonSeguimiento_NoCuentan()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Html(
                "<a href=\"https://www.gls-spain.es/es/\">GLS</a> <a href=\"https://www.gls-spain.es/es/politica-de-privacidad/\">privacidad</a>" +
                "<a href=\"https://www.facebook.com/glsspain\">fb</a> <img src=\"https://www.gls-spain.es/logo.png\">"));

            Assert.IsFalse(analisis.Guardar);
        }

        [TestMethod]
        public void SafelinkConVariosEnlaces_SeQuedaConLosDeSeguimiento()
        {
            string uno = "https://www.seur.com/livetracking/?segOnlineIdentificador=08111111111111";
            string dos = "https://www.seur.com/livetracking/?segOnlineIdentificador=08222222222222";
            AnalisisCorreoSeguimiento analisis = Analizar(Html(
                $"<p>Bulto 1: <a href=\"{Safelink(uno)}\">ver</a></p><p>Bulto 2: <a href=\"{Safelink(dos)}\">ver</a></p>" +
                $"<p><a href=\"{Safelink("https://www.seur.com/")}\">seur.com</a></p>"));

            Assert.AreEqual("SEUR", analisis.Agencia);
            CollectionAssert.AreEqual(new[] { "08111111111111", "08222222222222" }, analisis.Seguimientos.Select(s => s.Codigo).ToArray());
            CollectionAssert.AreEqual(new[] { uno, dos }, analisis.Seguimientos.Select(s => s.Url).ToArray());
        }

        [TestMethod]
        public void UpsSinEnlace_ElCodigo1ZDelAsuntoBasta()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Texto("Su paquete sale hoy.", asunto: "Envío UPS 1Z999AA10123456784"));

            Assert.AreEqual("UPS", analisis.Agencia);
            Assert.AreEqual("1Z999AA10123456784", analisis.Seguimientos.Single().Codigo);
            Assert.IsNull(analisis.Seguimientos.Single().Url);
        }

        [TestMethod]
        public void CorreoDeLaPropiaAgenciaSinEnlace_CodigoConEtiquetaDelTexto()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Texto("Hola. Número de envío: 61779876543. Le llegará mañana.",
                asunto: "Aviso de entrega", remitente: "noreply@gls-spain.es"));

            Assert.AreEqual("GLS", analisis.Agencia);
            Assert.AreEqual("61779876543", analisis.Seguimientos.Single().Codigo);
        }

        #endregion

        #region Genérico y remitentes marcados

        [TestMethod]
        public void Generico_CaptaUnaAgenciaDesconocidaSinAgencia()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Html(
                "<p>Su mercancía sale hoy con nosotros. <a href=\"https://portal.transportes-inventados.es/tracking?expedicion=TI2026111201\">Seguimiento</a></p>",
                remitente: "trafico@transportes-inventados.es"));

            Assert.IsTrue(analisis.Guardar);
            Assert.IsNull(analisis.Agencia, "El genérico no inventa agencia");
            Assert.AreEqual("TI2026111201", analisis.Seguimientos.Single().Codigo);
        }

        [TestMethod]
        public void Generico_CodigoEnElAsuntoSinEnlace()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Texto("Adjuntamos albarán.", asunto: "Pedido enviado - nº de seguimiento: AB12345678"));

            Assert.IsTrue(analisis.Guardar);
            Assert.AreEqual("AB12345678", analisis.Seguimientos.Single().Codigo);
        }

        [TestMethod]
        public void SinEnlacesNiCodigos_NoSeGuarda()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Html("<p>Buenos días, le adjunto la factura del mes. Un saludo.</p>" +
                "<a href=\"https://www.ejemplo-proveedor.es/\">web</a>", asunto: "Factura"));

            Assert.IsFalse(analisis.Guardar);
            Assert.IsNull(analisis.Extracto);
        }

        [TestMethod]
        public void NuestrosEnlaces_NoCuentanComoSeguimiento()
        {
            Assert.IsFalse(Analizar(Html("<a href=\"https://www.nuevavision.es/seguimiento-pedido?id=123456\">x</a>")).Guardar);
        }

        [TestMethod]
        public void RemitenteMarcado_SeGuardaAunqueNoHayaEnlaceConSuAgencia()
        {
            var marcado = new RemitenteMarcado { Id = 1, Agencia = "Transportes Inventados", Remitente = "transportes-inventados.es", Activo = true };

            AnalisisCorreoSeguimiento analisis = Analizar(Texto("Mañana entregamos 2 palés.", remitente: "Trafico@Transportes-Inventados.es"), marcado);

            Assert.IsTrue(analisis.Guardar);
            Assert.AreEqual("Transportes Inventados", analisis.Agencia);
            Assert.AreSame(marcado, analisis.RemitenteMarcado);
            Assert.AreEqual(0, analisis.Seguimientos.Count);
        }

        [TestMethod]
        public void RemitenteMarcadoInactivo_NoCuenta()
        {
            var marcado = new RemitenteMarcado { Agencia = "X", Remitente = "transportes-inventados.es", Activo = false };
            Assert.IsFalse(Analizar(Texto("Mañana entregamos.", remitente: "trafico@transportes-inventados.es"), marcado).Guardar);
        }

        [TestMethod]
        public void RemitenteMarcado_EnUnReenvioCuentaElOriginal()
        {
            var marcado = new RemitenteMarcado { Agencia = "Transportes Inventados", Remitente = "trafico@transportes-inventados.es", Activo = true };
            CorreoCompras correo = Html("<p>Te lo reenvío.</p><p>-----Mensaje original-----<br>De: Tráfico &lt;trafico@transportes-inventados.es&gt;<br>" +
                "Enviado el: miércoles<br>Asunto: Entrega</p><p>Mañana entregamos.</p>", remitente: "compras@nuevavision.es");

            AnalisisCorreoSeguimiento analisis = Analizar(correo, marcado);

            Assert.IsTrue(analisis.Guardar);
            CollectionAssert.Contains(analisis.RemitentesOriginales, "trafico@transportes-inventados.es");
        }

        [TestMethod]
        public void RemitenteMarcado_UnDominioNoCasaConOtroQueAcabaIgual()
        {
            var marcado = new RemitenteMarcado { Remitente = "inventados.es", Activo = true };
            Assert.IsTrue(marcado.Casa("a@inventados.es"));
            Assert.IsTrue(marcado.Casa("a@avisos.inventados.es"));
            Assert.IsFalse(marcado.Casa("a@transportes-inventados.es"));
        }

        #endregion

        #region Agencia por su nombre tras una etiqueta

        private const string TEXTO_WEELKO =
            "¡Su pedido está preparado y ha salido de nuestro almacén! Podrá seguir el estado de la entrega a través del siguiente enlace: " +
            "Tracking Número de pedido WK/OUT/77626 Referencia cliente 29636 El pedido será enviado por: MAILBOXES Gracias por su compra.";

        [TestMethod]
        public void PorNombre_Weelko_EnlaceDeDominioDesconocidoYEnviadoPorMailboxes_EsMbe()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Html(
                $"<p>{TEXTO_WEELKO.Replace("Tracking", "<a href=\"https://www.weelko.com/tracking/WK-OUT-77626\">Tracking</a>")}</p>",
                asunto: "Su Traslado WK-OUT-77626", remitente: "noreply@weelko.com"));

            Assert.IsTrue(analisis.Guardar);
            Assert.AreEqual("MBE", analisis.Agencia);
            Assert.AreEqual(24, analisis.HorasHastaEntrega);
            Assert.IsTrue(analisis.AgenciaPorNombreEnTexto);
            Assert.AreEqual("WK-OUT-77626", analisis.Seguimientos.Single().Codigo);
            Assert.AreEqual("https://www.weelko.com/tracking/WK-OUT-77626", analisis.Seguimientos.Single().Url);
        }

        [TestMethod]
        public void PorNombre_Weelko_EnlaceDeMbe_EsMbePorElDominio()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Html(
                $"<p>{TEXTO_WEELKO.Replace("Tracking", "<a href=\"https://tracking.mbe.es/?c=ABC12345\">Tracking</a>")}</p>",
                asunto: "Su Traslado WK-OUT-77626", remitente: "noreply@weelko.com"));

            Assert.AreEqual("MBE", analisis.Agencia);
            Assert.IsFalse(analisis.AgenciaPorNombreEnTexto, "El dominio de MBE la reconoce antes que el nombre");
            Assert.AreEqual("ABC12345", analisis.Seguimientos.Single().Codigo);
        }

        [TestMethod]
        public void PorNombre_Dduueett_TransportistaSchenkerSinEnlaces_EsDbSchenker()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Texto(
                "Te informamos que tu pedido ha salido de nuestro almacén y está en camino.\n" +
                "Transportista: SCHENKER STANDARD\nNúmero de seguimiento: ESMNR264647742\n" +
                "Para una entrega exitosa, por favor consulta el archivo adjunto.",
                asunto: "Tu pedido ha sido enviado", remitente: "logistics@dduueett.com"));

            Assert.AreEqual("DB Schenker", analisis.Agencia);
            Assert.AreEqual(48, analisis.HorasHastaEntrega);
            Assert.IsTrue(analisis.AgenciaPorNombreEnTexto);
            Assert.AreEqual("ESMNR264647742", analisis.Seguimientos.Single().Codigo);
            Assert.IsNull(analisis.Seguimientos.Single().Url);
        }

        [DataTestMethod]
        [DataRow("Agencia: SEUR", "SEUR")]
        [DataRow("Enviado con mrw desde Barcelona", "MRW")]
        [DataRow("Carrier: DHL Express", "DHL")]
        [DataRow("Shipped via FedEx Ground", "TNT/FedEx")]
        [DataRow("Transporte: Tipsa (24 h)", "TIPSA")]
        [DataRow("Agencia de transporte: Correos Express", "Correos Express")]
        [DataRow("Transportista: CORREOS", "Correos")]
        [DataRow("Expedido por: Mail Boxes Etc", "MBE")]
        [DataRow("Transportista: CTT", "CTT Express")]
        public void PorNombre_ConEtiqueta_PoneLaAgencia(string linea, string agencia)
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Texto($"Su pedido ha salido.\n{linea}\nNº de seguimiento: XY12345678"));

            Assert.AreEqual(agencia, analisis.Agencia, linea);
            Assert.IsTrue(analisis.AgenciaPorNombreEnTexto, linea);
        }

        [TestMethod]
        public void PorNombre_SinEtiqueta_SigueSinAgencia()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Texto(
                "Su pedido ha salido. Nº de seguimiento: XY12345678. Gracias por confiar en Correos y en Envialia."));

            Assert.IsTrue(analisis.Guardar);
            Assert.IsNull(analisis.Agencia);
            Assert.IsFalse(analisis.AgenciaPorNombreEnTexto);
        }

        [TestMethod]
        public void PorNombre_EtiquetaConUnaAgenciaDesconocida_SigueSinAgencia()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Texto("Transportista: Transportes Pepe\nNº de seguimiento: XY12345678"));

            Assert.IsTrue(analisis.Guardar);
            Assert.IsNull(analisis.Agencia);
        }

        [TestMethod]
        public void PorNombre_ElNombreLejosDeLaEtiqueta_NoCuenta()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Texto(
                "Transportista: el que tenemos contratado para su zona, que suele llegar en dos días, como siempre con Correos\n" +
                "Nº de seguimiento: XY12345678"));

            Assert.IsNull(analisis.Agencia);
        }

        [TestMethod]
        public void PorNombre_SinSeguimiento_NoSeGuardaNiPoneAgencia()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Texto("Transportista: SEUR. Mañana sale su pedido."));

            Assert.IsFalse(analisis.Guardar);
            Assert.IsNull(analisis.Agencia);
        }

        [TestMethod]
        public void PorNombre_CorreoYaReconocidoPorDominio_NoCambia()
        {
            AnalisisCorreoSeguimiento analisis = Analizar(Html(
                "<p>Transportista: SCHENKER</p><a href=\"https://www.gls-spain.es/es/recibir-paquetes/seguimiento-envio/?match=61771234567\">seguir</a>"));

            Assert.AreEqual("GLS", analisis.Agencia);
            Assert.IsFalse(analisis.AgenciaPorNombreEnTexto);
        }

        [TestMethod]
        public void PorNombre_RemitenteMarcadoConAgencia_Manda()
        {
            var marcado = new RemitenteMarcado { Agencia = "Transportes Inventados", Remitente = "transportes-inventados.es", Activo = true };

            AnalisisCorreoSeguimiento analisis = Analizar(Texto("Transportista: SEUR\nNº de seguimiento: XY12345678",
                remitente: "trafico@transportes-inventados.es"), marcado);

            Assert.AreEqual("Transportes Inventados", analisis.Agencia);
            Assert.IsFalse(analisis.AgenciaPorNombreEnTexto);
        }

        #endregion

        #region Extracto y clave

        [TestMethod]
        public void Extracto_CortoYAlrededorDelCodigo()
        {
            string relleno = string.Concat(Enumerable.Repeat("Texto de relleno sin interés. ", 80));
            CorreoCompras correo = Texto(relleno + "Su número de envío: 61771112223 llega mañana. " + relleno, remitente: "noreply@gls-spain.es");

            AnalisisCorreoSeguimiento analisis = Analizar(correo);

            Assert.IsTrue(analisis.Extracto.Length <= AnalizadorCorreoSeguimiento.LONGITUD_EXTRACTO);
            StringAssert.Contains(analisis.Extracto, "61771112223");
        }

        [TestMethod]
        public void Html_ATexto_SinEtiquetasNiEstilos()
        {
            string texto = TextoCorreo.HtmlATexto("<html><head><style>p{color:red}</style></head><body><p>Hola&nbsp;<b>mundo</b></p><p>Adiós</p></body></html>");
            Assert.AreEqual("Hola mundo\nAdiós", texto);
        }

        [TestMethod]
        public void Clave_ElMessageIdYSiEsMuyLargoSuHash()
        {
            Assert.AreEqual("<a@b>", new CorreoCompras { MensajeId = " <a@b> ", Id = "graph" }.Clave());
            Assert.AreEqual("graph", new CorreoCompras { Id = "graph" }.Clave());
            string largo = new CorreoCompras { MensajeId = new string('x', 500) }.Clave();
            StringAssert.StartsWith(largo, "sha256:");
            Assert.IsTrue(largo.Length < 100);
        }

        #endregion
    }
}
