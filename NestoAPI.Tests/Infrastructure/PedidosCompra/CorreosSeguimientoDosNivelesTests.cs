using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#857f (Carlos 19/11/26): la fecha en dos niveles. Nivel 1, el formato del aviso del proveedor (DDUUEETT); nivel 2, la agencia
    /// (SEUR y UPS dicen su entrega y su estado; Schenker compone su enlace). Con los cuatro ejemplos reales del buzón (12-19/11/26).
    /// </summary>
    [TestClass]
    public class CorreosSeguimientoDosNivelesTests
    {
        #region Ejemplos reales

        internal const string DDUUEETT_TEXTO = "Te informamos que tu pedido ha salido de nuestro almacén y está en camino.\n" +
            "Transportista: SCHENKER STANDARD\nNúmero de seguimiento: ESMNR264773770\nPara una entrega exitosa, por favor consulta el archivo adjunto.";
        internal const string URL_DSV = "https://www.dsv.com/mydsv/tracking-public/?language_region=es-ES_ES&refNumber=ESMNR264773770";

        internal const string SEUR_PREVISTA_HTML = "<html><body><p>¡Hola, Nueva Vision S.a.!</p><p>En camino</p><p>En reparto</p><p>Entregado</p>" +
            "<p>Entrega prevista</p><p>Jueves 19 de Noviembre</p>" +
            "<p>Tu envío PV26-56989 de Postquam en C/ Rio Tietar 11 Nave 22, Algete, 28110 está en camino.</p>" +
            "<p><a href=\"https://click.mail.seur.info/?qs=8f3a1b2c4d5e6f\">Más información en SEUR.com</a></p>" +
            "<p><a href=\"https://click.mail.seur.info/?qs=99aa77bb66cc\">Darse de baja</a></p></body></html>";
        internal const string SEUR_PREVISTA_ASUNTO = "🗓️ Nueva Vision S.a., tu envío ya tiene fecha prevista de entrega";

        internal const string SEUR_HOY_HTML = "<html><body><p>¡Hola, Nueva Vision!</p><p>Tu envío está en reparto.</p>" +
            "<p>Se entregará HOY de 13:45 a 14:45 horas</p><p><a href=\"https://click.mail.seur.info/?qs=abcdef123456\">Ver mi envío</a></p></body></html>";
        internal const string SEUR_HOY_ASUNTO = "🚚 ¡Nueva Vision, tu envío de IBEROCEL - PARAISO está en reparto!";

        internal const string SEUR_PUBLICIDAD_HTML = "<html><body><p>¡Envía con SEUR y ahorra un 20 % en tus envíos este Black Friday!</p>" +
            "<p><a href=\"https://click.mail.seur.info/?qs=promo123456\">Descubre la oferta</a></p><p>Darse de baja</p></body></html>";

        internal const string UPS_ESTIMADA = "¡Tu paquete está de camino!\nDe MBE STORE C/O MULTIBOXES S.L\n\nEntrega estimada\nLunes 16/11/2026\nentre 10:45 - 14:45\n\n" +
            "Número de seguimiento\n1Z21E7X76890915153";
        internal const string UPS_REPROGRAMADA = "Entrega reprogramada\nViernes 13/11/2026\nentre 10:45 - 14:45\n\nNúmero de seguimiento\n1Z21E7X76890915153";
        internal const string UPS_REPROGRAMADA_FINAL = "Entrega reprogramada\nViernes 13/11/2026\nFinal del día\n\nNúmero de seguimiento\n1Z21E7X76890915153";
        internal const string UPS_ENTREGADO = "Tu paquete se ha entregado.\nDe MBE STORE C/O MULTIBOXES S.L\nEntregado\nViernes 13/11/2026 10:45\n\n" +
            "Número de seguimiento\n1Z21E7X76890915153";

        internal const string NACEX_HTML = "<html><body><p>Hola, ya tenemos tu envío de LASHES AND MORE SL y está en camino con el número de seguimiento 3300/11571609.</p>" +
            "<p><a href=\"https://www.nacex.es/s.do?exp=503411773\">Sigue tu envío</a></p>" +
            "<p><a href=\"https://www.nacex.es/seguimientoDetalle.do?agencia_origen=3300&amp;numero_albaran=11571609&amp;internacional=0\">Detalle</a></p></body></html>";

        private static int siguiente = 1;

        private static CorreoCompras Correo(string remitente, string asunto, string cuerpo, DateTime recibido, bool html = false) => new CorreoCompras
        {
            Id = "g" + siguiente, MensajeId = "<m" + siguiente++ + "@ejemplo.test>", Recibido = recibido, RemitenteDireccion = remitente,
            Asunto = asunto, Cuerpo = cuerpo, CuerpoEsHtml = html
        };

        private static AnalisisCorreoSeguimiento Analizar(CorreoCompras correo) =>
            AnalizadorCorreoSeguimiento.Analizar(correo, AgenciasEntradaRegistradas.Todas, new List<RemitenteMarcado>());

        #endregion

        #region Nivel 1: el formato del aviso del proveedor

        [TestMethod]
        public void Dduueett_FormatoPropio_SchenkerConSuNumeroYElEnlaceQueComponeSuClase()
        {
            CorreoCompras correo = Correo("logistics@dduueett.com", "Tu pedido ha sido enviado", DDUUEETT_TEXTO, new DateTime(2026, 11, 18, 18, 0, 0));

            AnalisisCorreoSeguimiento a = Analizar(correo);

            Assert.IsTrue(a.Guardar);
            Assert.AreEqual(FormatoAvisoSalidaDduueett.NOMBRE, a.FormatoAviso);
            Assert.AreEqual("DB Schenker", a.Agencia);
            Assert.AreEqual("ESMNR264773770", a.Seguimientos.Single().Codigo);
            Assert.AreEqual(URL_DSV, a.Seguimientos.Single().Url);
            Assert.IsNull(a.EntregaEstimada, "El aviso no dice entrega: va por recibido + 3 laborables (#857g)");
            Assert.IsNull(a.OrigenEntrega);
            Assert.AreEqual(new DateTime(2026, 11, 23), ReglaFechaRecepcionPorCorreo.Calcular(
                new CorreoSeguimientoFila { Recibido = correo.Recibido, Agencia = a.Agencia }, new DateTime(2026, 11, 18)));
        }

        [TestMethod]
        public void Formatos_ElDeDduueettReconoceSuCuerpoYSuPackingList_YLoDemasVaAlGenerico()
        {
            Assert.AreEqual(FormatoAvisoSalidaDduueett.NOMBRE, FormatosAvisoSalida.Para(DDUUEETT_TEXTO).Nombre);
            Assert.AreEqual(FormatoAvisoSalidaDduueett.NOMBRE, FormatosAvisoSalida.Para(CorreosSeguimientoAvisoSalidaTests.PDF_DDUUEETT).Nombre);
            Assert.AreEqual(FormatoAvisoSalidaGenerico.NOMBRE, FormatosAvisoSalida.Para("Su pedido ha salido.\nAgencia: SEUR\nNº de seguimiento: XY12345678").Nombre);
            Assert.AreEqual(FormatoAvisoSalidaGenerico.NOMBRE, FormatosAvisoSalida.Para("Transportista: SEUR").Nombre, "Sin número ni packing list no es su formato");

            AvisoSalidaLeido leido = FormatosAvisoSalida.Leer(DDUUEETT_TEXTO, new DateTime(2026, 11, 18), AgenciasEntradaRegistradas.Todas);
            Assert.AreEqual("DB Schenker", leido.Agencia.Nombre);
            CollectionAssert.AreEqual(new[] { "ESMNR264773770" }, leido.Codigos);
        }

        [TestMethod]
        public void Dduueett_ElPdfLoLeeSuFormato_SinCodigosDelGenerico()
        {
            var enAire = new[] { new PedidoCompraEnAire { Empresa = "1", Pedido = 220923, Proveedor = "1021" } };
            AvisoSalidaPdf pdf = ExtractorAvisoSalida.Extraer(CorreosSeguimientoAvisoSalidaTests.PDF_DDUUEETT, new DateTime(2026, 11, 17), enAire, enAire,
                AgenciasEntradaRegistradas.Todas);

            Assert.AreEqual(FormatoAvisoSalidaDduueett.NOMBRE, pdf.Formato);
            Assert.AreEqual(220923, pdf.Pedido.Pedido);
            Assert.AreEqual("DB Schenker", pdf.Agencia.Nombre);
            Assert.AreEqual(0, pdf.Codigos.Count, "El packing list no trae número de envío");
            Assert.AreEqual(0, ExtractorAvisoSalida.Extraer("Expedición 1234567 de cosas", DateTime.Today, null, null, null).Codigos.Count,
                "Del genérico no se toman códigos del PDF (como antes)");
        }

        #endregion

        #region Nivel 2: SEUR

        [TestMethod]
        public void Seur_FechaPrevista_SinCodigoEnLosEnlaces_SeGuardaConLaReferenciaDelRemitente()
        {
            CorreoCompras correo = Correo("infoenvios@mail.seur.info", SEUR_PREVISTA_ASUNTO, SEUR_PREVISTA_HTML, new DateTime(2026, 11, 18, 9, 12, 0), html: true);

            AnalisisCorreoSeguimiento a = Analizar(correo);

            Assert.IsTrue(a.Guardar);
            Assert.AreEqual("SEUR", a.Agencia);
            Assert.AreEqual("PV26-56989", a.Seguimientos.Single().Codigo);
            Assert.AreEqual(new DateTime(2026, 11, 19), a.EntregaEstimada.Dia);
            Assert.AreEqual(EstadoEnvioAgencia.EnCamino, a.EstadoEnvio, "La barra «En camino En reparto Entregado» no es el estado");
            Assert.AreEqual(OrigenEntregaCorreo.AGENCIA, a.OrigenEntrega);
            Assert.AreEqual("postquam", a.RemitentePaquete);
        }

        [TestMethod]
        public void Seur_EnRepartoHoy_SinCodigo_SeGuardaPorLoQueDice_ConElRemitenteDelAsunto()
        {
            CorreoCompras correo = Correo("infoenvios@mail.seur.info", SEUR_HOY_ASUNTO, SEUR_HOY_HTML, new DateTime(2026, 11, 19, 8, 30, 0), html: true);

            AnalisisCorreoSeguimiento a = Analizar(correo);

            Assert.IsTrue(a.Guardar);
            Assert.IsTrue(a.GuardadoPorNoticiaDeAgencia);
            Assert.AreEqual("SEUR", a.Agencia);
            Assert.AreEqual(0, a.Seguimientos.Count);
            Assert.AreEqual(new DateTime(2026, 11, 19), a.EntregaEstimada.Dia, "HOY = el día del correo");
            Assert.AreEqual(new TimeSpan(13, 45, 0), a.EntregaEstimada.Desde);
            Assert.AreEqual(new TimeSpan(14, 45, 0), a.EntregaEstimada.Hasta);
            Assert.AreEqual(EstadoEnvioAgencia.EnReparto, a.EstadoEnvio);
            Assert.AreEqual("iberocel - paraiso", a.RemitentePaquete);

            CorreoSeguimientoFila fila = ServicioCorreosSeguimientoCompras.Fila(correo, a, null);
            Assert.AreEqual("EnReparto", fila.EstadoEnvio);
            Assert.AreEqual(OrigenEntregaCorreo.AGENCIA, fila.OrigenEntrega);
            Assert.IsNull(fila.Codigo);
        }

        [TestMethod]
        public void Seur_PublicidadDeLaAgencia_NoSeGuarda()
        {
            Assert.IsFalse(Analizar(Correo("novedades@mail.seur.info", "Black Friday en SEUR", SEUR_PUBLICIDAD_HTML, new DateTime(2026, 11, 19, 10, 0, 0), html: true)).Guardar);
            Assert.IsFalse(Analizar(Correo("infoenvios@mail.seur.info", "Tu opinión nos importa", "<p>¿Qué tal fue tu última entrega con nosotros?</p>",
                new DateTime(2026, 11, 19, 10, 0, 0), html: true)).Guardar);
        }

        [TestMethod]
        public void Seur_FechaSinAnio_CambioDeAnio()
        {
            CorreoCompras correo = Correo("infoenvios@mail.seur.info", SEUR_PREVISTA_ASUNTO,
                SEUR_PREVISTA_HTML.Replace("Jueves 19 de Noviembre", "Viernes 2 de Enero"), new DateTime(2026, 12, 30, 9, 0, 0), html: true);

            Assert.AreEqual(new DateTime(2027, 1, 2), Analizar(correo).EntregaEstimada.Dia);
        }

        [TestMethod]
        public void Emoji_EnElAsunto_NoRompeLaNormalizacion()
        {
            // Regresión: «🚚 ¡…!» (par sustituto) lanzaba ArgumentException al normalizar carácter a carácter
            string normalizado = ExtractorEntregaEstimada.Normalizar("🚚 ¡Tu envío está en reparto!");
            Assert.AreEqual("🚚 ¡Tu envío está en reparto!".Length, normalizado.Length);
            Assert.IsNotNull(AgenciaPorNombreEnTexto.Buscar("🗓️ Transportista: SEUR", AgenciasEntradaRegistradas.Todas));
        }

        [TestMethod]
        public void ReglaAnio_ElDelCorreoOElSiguienteSiQuedaAtras()
        {
            Assert.AreEqual(new DateTime(2026, 11, 19), ReglaAnioEntregaAgencia.Resolver(19, 11, null, new DateTime(2026, 11, 18)));
            Assert.AreEqual(new DateTime(2026, 11, 18), ReglaAnioEntregaAgencia.Resolver(18, 11, null, new DateTime(2026, 11, 18, 23, 0, 0)), "El mismo día no es «antes»");
            Assert.AreEqual(new DateTime(2027, 1, 2), ReglaAnioEntregaAgencia.Resolver(2, 1, null, new DateTime(2026, 12, 30)));
            Assert.AreEqual(new DateTime(2026, 1, 2), ReglaAnioEntregaAgencia.Resolver(2, 1, 2026, new DateTime(2026, 12, 30)), "Con año, el que dice");
            Assert.IsNull(ReglaAnioEntregaAgencia.Resolver(31, 11, null, new DateTime(2026, 11, 18)));
        }

        [TestMethod]
        public void Seur_Entregado_SeGuardaElEstadoSinFecha()
        {
            CorreoCompras correo = Correo("infoenvios@mail.seur.info", "Nueva Vision, tu envío ha sido entregado",
                "<p>Tu envío PV26-56989 de Postquam ha sido entregado.</p>", new DateTime(2026, 11, 19, 12, 0, 0), html: true);

            AnalisisCorreoSeguimiento a = Analizar(correo);

            Assert.IsTrue(a.Guardar);
            Assert.AreEqual(EstadoEnvioAgencia.Entregado, a.EstadoEnvio);
            Assert.IsNull(a.EntregaEstimada);
        }

        [TestMethod]
        public void Seur_ReenviadoPorUnCompanero_TambienEsSuyo()
        {
            string cuerpo = "---------- Forwarded message ---------\nFrom: SEUR <infoenvios@mail.seur.info>\nDate: hoy\n\nSe entregará HOY de 13:45 a 14:45 horas";
            AnalisisCorreoSeguimiento a = Analizar(Correo("compras@nuevavision.es", "RV: " + SEUR_HOY_ASUNTO, cuerpo, new DateTime(2026, 11, 19, 9, 0, 0)));

            Assert.AreEqual("SEUR", a.Agencia);
            Assert.AreEqual(EstadoEnvioAgencia.EnReparto, a.EstadoEnvio);
        }

        #endregion

        #region Nivel 2: UPS

        [TestMethod]
        public void Ups_Estimada_EnCaminoConTramoYEnlace()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("pkginfo@ups.com", "Tu paquete está de camino", UPS_ESTIMADA, new DateTime(2026, 11, 12, 20, 15, 0)));

            Assert.AreEqual("UPS", a.Agencia);
            Assert.AreEqual(EstadoEnvioAgencia.EnCamino, a.EstadoEnvio);
            Assert.AreEqual(new DateTime(2026, 11, 16), a.EntregaEstimada.Dia);
            Assert.AreEqual(new TimeSpan(14, 45, 0), a.EntregaEstimada.Hasta);
            Assert.AreEqual(OrigenEntregaCorreo.AGENCIA, a.OrigenEntrega);
            Assert.AreEqual("https://www.ups.com/track?tracknum=1Z21E7X76890915153", a.Seguimientos.Single().Url);
            Assert.AreEqual("mbe store c/o multiboxes s.l", a.RemitentePaquete);
        }

        [DataTestMethod]
        [DataRow(UPS_REPROGRAMADA, true)]
        [DataRow(UPS_REPROGRAMADA_FINAL, false)]
        public void Ups_Reprogramada(string texto, bool conTramo)
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("pkginfo@ups.com", "Actualización de UPS: Fecha de entrega del paquete reprogramada", texto,
                new DateTime(2026, 11, 13, 8, 0, 0)));

            Assert.AreEqual(EstadoEnvioAgencia.Reprogramada, a.EstadoEnvio);
            Assert.AreEqual(new DateTime(2026, 11, 13), a.EntregaEstimada.Dia);
            Assert.AreEqual(conTramo, a.EntregaEstimada.Hasta.HasValue);
        }

        [TestMethod]
        public void Ups_Entregado_EstadoSinFecha()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("pkginfo@ups.com", "Actualización de UPS: Paquete entregado", UPS_ENTREGADO, new DateTime(2026, 11, 13, 10, 50, 0)));

            Assert.AreEqual(EstadoEnvioAgencia.Entregado, a.EstadoEnvio);
            Assert.IsNull(a.EntregaEstimada);
            Assert.IsTrue(EntregaConfirmadaCorreo.Es(new CorreoSeguimientoFila { EstadoEnvio = "Entregado" }));
        }

        #endregion

        #region Nacex (implementación por defecto) y encapsulación

        [TestMethod]
        public void Nacex_CodigoDelEnlace_ElRemitenteDelPaquete_YSuEstado()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("no-reply@nacex.com", "Tu envío está en camino", NACEX_HTML, new DateTime(2026, 11, 13, 9, 0, 0), html: true));

            Assert.AreEqual("Nacex", a.Agencia);
            Assert.AreEqual("11571609", a.Seguimientos.First().Codigo);
            Assert.AreEqual("lashes and more sl", a.RemitentePaquete);
            Assert.AreEqual(EstadoEnvioAgencia.EnCamino, a.EstadoEnvio, "#857g: Nacex lee su estado («está en camino»)");
        }

        [TestMethod]
        public void Schenker_NuevoEnvio_CasoReal_ElNumeroEstaEnElTexto()
        {
            // Carlos 20/11/26: el aviso de Schenker no trae el código en ningún enlace y el remitente del paquete viene en blanco.
            const string cuerpo = "Apreciado cliente,\nEl remitente\nha generado un nuevo envío para usted con número ESVIC262013536 Puede seguir el estado del envio en el siguiente link:\n"
                + "http://www.dbschenker.es/log-es-es/start/\nIntroduciendo en la casilla de Quick Tracking el número: ESVIC262013536\n\n"
                + "Dear customer,\nThe shipper\nhas created a new order for you with number ESVIC262013536 You can follow the status of the shipment on the following link:\n"
                + "https://www.dbschenker.com/global\nEntering the number ESVIC262013536 in the box Tracking ID";

            AnalisisCorreoSeguimiento a = Analizar(Correo("gestor_aut@schenker.com", "Nuevo envío DB Schenker/New order DB Schenker", cuerpo, new DateTime(2026, 11, 20, 8, 0, 0), html: false));

            Assert.IsTrue(a.Guardar);
            Assert.AreEqual("DB Schenker", a.Agencia);
            Assert.AreEqual("ESVIC262013536", a.Seguimientos.First().Codigo);
            Assert.AreEqual(EstadoEnvioAgencia.EnCamino, a.EstadoEnvio);
        }

        [DataTestMethod]
        [DataRow("ENM (Llegado)", EstadoEnvioAgencia.EnCamino)]
        [DataRow("DOT (Salido a reparto)", EstadoEnvioAgencia.EnReparto)]
        [DataRow("DLV (Entregado)", EstadoEnvioAgencia.Entregado)]
        [DataRow("POD (ePOD disponible)", EstadoEnvioAgencia.Entregado)]
        public void Schenker_CorreosDeEstadoDeDsv_CasoReal_MismaAgenciaCodigoYEstado(string estado, EstadoEnvioAgencia esperado)
        {
            // Los de noreply@mydsv.dsv.com del 19/11/26 se guardaron como «DSV», sin código ni estado, y no se unieron al aviso
            // del proveedor (DB Schenker, ESMNR264773770).
            string cuerpo = "Estimado Cliente,\nA continuación encontrará el estado de sus envíos\nEstado del Envío:\n" + estado
                + "\nPara obtener información general, haga clic en el siguiente enlace\n"
                + "https://mydsv.dsv.com/app/tracking-public/?refNumber=ESMNR264773770&refType=Stt\nSaludos,\nSu Equipo de eServices";

            AnalisisCorreoSeguimiento a = Analizar(Correo("noreply@mydsv.dsv.com", "Nº deTracking Stt : ESMNR264773770 (Notificación) - Current Status",
                cuerpo, new DateTime(2026, 11, 19, 16, 40, 0), html: false));

            Assert.IsTrue(a.Guardar);
            Assert.AreEqual("DB Schenker", a.Agencia);
            Assert.AreEqual("ESMNR264773770", a.Seguimientos.First().Codigo);
            Assert.AreEqual(esperado, a.EstadoEnvio);
        }

        [TestMethod]
        public void Schenker_UnIbanOUnaPalabra_NoSonSuNumeroDeEnvio()
        {
            AnalisisCorreoSeguimiento a = Analizar(Correo("administracion@ejemplo-proveedor.es", "Factura",
                "Puede pagar en la cuenta ES7621000418401234567891. Referencia ESPECIAL123456789.", new DateTime(2026, 11, 20, 8, 0, 0), html: false));

            Assert.AreNotEqual("DB Schenker", a.Agencia);
        }

        [TestMethod]
        public void Enlace_LoComponeSoloLaClaseDeSuAgencia()
        {
            Assert.AreEqual(URL_DSV, EnlaceSeguimientoAgencia.De("DB Schenker", "ESMNR264773770"));
            Assert.AreEqual("https://www.ups.com/track?tracknum=1Z21E7X76890915153", EnlaceSeguimientoAgencia.De("ups", " 1Z21E7X76890915153 "));
            Assert.IsNull(EnlaceSeguimientoAgencia.De("GLS", "1378206148"), "Donde no se sabe, null");
            Assert.IsNull(EnlaceSeguimientoAgencia.De("Transportes inventados", "ABC123456"));
            Assert.IsNull(EnlaceSeguimientoAgencia.De("DB Schenker", "x"), "Un código que no es código, sin enlace");
            Assert.IsFalse(AgenciasEntradaRegistradas.Todas.OfType<IConsultaEntregaAgencia>().Any(), "Hoy ninguna agencia consulta");
        }

        [TestMethod]
        public void Dto_SinUrlGuardada_ElEnlaceDeSuAgencia()
        {
            CorreoSeguimientoCompraDTO dto = ComprasAgenciasEntradaController.Dto(new CorreoSeguimientoFila
            {
                Id = 7, Agencia = "DB Schenker", Codigo = "ESMNR264773770", EstadoEnvio = "EnCamino", OrigenEntrega = "Agencia"
            });

            Assert.AreEqual(URL_DSV, dto.Url);
            Assert.AreEqual(URL_DSV, dto.Seguimientos.Single().Url);
            Assert.AreEqual("EnCamino", dto.EstadoEnvio);
            Assert.AreEqual("Agencia", dto.OrigenEntrega);
        }

        #endregion

        #region Precedencia de la fecha

        private static CorreoSeguimientoFila F(int id, DateTime recibido, DateTime? entrega, string origen, string codigo = "1Z21E7X76890915153") =>
            new CorreoSeguimientoFila { Id = id, Recibido = recibido, Codigo = codigo, Agencia = "UPS", EntregaEstimadaDia = entrega, OrigenEntrega = origen };

        private static readonly DateTime HOY = new DateTime(2026, 11, 18);

        [TestMethod]
        public void Precedencia_LaAgenciaMandaSobreElProveedor_AunqueElProveedorEscribaDespues()
        {
            var envio = new List<CorreoSeguimientoFila>
            {
                F(1, HOY.AddHours(9), new DateTime(2026, 11, 23), OrigenEntregaCorreo.AGENCIA),
                F(2, HOY.AddHours(12), new DateTime(2026, 11, 25), OrigenEntregaCorreo.PROVEEDOR)
            };
            Assert.AreEqual(new DateTime(2026, 11, 23), ReglaFechaRecepcionPorCorreo.Calcular(envio, HOY));
            Assert.AreEqual(OrigenEntregaCorreo.AGENCIA, ReglaFechaRecepcionPorCorreo.Origen(envio));
        }

        [TestMethod]
        public void Precedencia_SinAgencia_ElProveedor_YSinNinguna_LasHoras()
        {
            var proveedor = new List<CorreoSeguimientoFila> { F(1, HOY.AddHours(9), new DateTime(2026, 11, 25), OrigenEntregaCorreo.PROVEEDOR) };
            Assert.AreEqual(new DateTime(2026, 11, 25), ReglaFechaRecepcionPorCorreo.Calcular(proveedor, HOY));
            Assert.AreEqual(OrigenEntregaCorreo.PROVEEDOR, ReglaFechaRecepcionPorCorreo.Origen(proveedor));

            var nada = new List<CorreoSeguimientoFila> { F(1, HOY.AddHours(9), null, null) };
            Assert.AreEqual(new DateTime(2026, 11, 19), ReglaFechaRecepcionPorCorreo.Calcular(nada, HOY), "UPS: recibido + 24 h");
            Assert.AreEqual(ReglaFechaRecepcionPorCorreo.ORIGEN_HORAS, ReglaFechaRecepcionPorCorreo.Origen(nada));
        }

        [TestMethod]
        public void Precedencia_LaConsultaMasRecienteCorrigeAlCorreoDeLaAgencia_YNuncaAntesDeHoy()
        {
            CorreoSeguimientoFila correo = F(1, HOY.AddDays(-2), new DateTime(2026, 11, 19), OrigenEntregaCorreo.AGENCIA);
            CorreoSeguimientoFila consultado = F(2, HOY.AddDays(-3), new DateTime(2026, 11, 24), OrigenEntregaCorreo.CONSULTA);
            consultado.EntregaConsultadaEl = HOY.AddHours(7.5);
            Assert.AreEqual(new DateTime(2026, 11, 24), ReglaFechaRecepcionPorCorreo.Calcular(new[] { correo, consultado }, HOY));
            Assert.AreEqual(OrigenEntregaCorreo.CONSULTA, ReglaFechaRecepcionPorCorreo.Origen(new[] { correo, consultado }));

            Assert.AreEqual(HOY, ReglaFechaRecepcionPorCorreo.Calcular(new[] { F(3, HOY.AddDays(-5), new DateTime(2026, 11, 14), OrigenEntregaCorreo.AGENCIA) }, HOY));
        }

        [TestMethod]
        public void Precedencia_CorreosDeAntesSinOrigen_ComoSiempre()
        {
            var envio = new List<CorreoSeguimientoFila>
            {
                F(1, HOY.AddHours(9), new DateTime(2026, 11, 23), null),
                F(2, HOY.AddHours(12), new DateTime(2026, 11, 25), null)
            };
            Assert.AreEqual(new DateTime(2026, 11, 25), ReglaFechaRecepcionPorCorreo.Calcular(envio, HOY), "El último que la dice");
        }

        #endregion

        #region De punta a punta (job, repositorio en memoria)

        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private ILectorBuzonCompras lector;
        private LecturaBuzonCompras lectura;
        private List<Exception> errores;
        private List<string> promesas;
        private DateTime ahora;
        private bool interruptorAplicar;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            errores = new List<Exception>();
            promesas = new List<string>();
            interruptorAplicar = true;
            ahora = new DateTime(2026, 11, 18, 10, 0, 0);
            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns("buzon@ejemplo.test");
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(lectura));
            ServicioConsultaEntregaAgencias.OlvidarSinTablaApuntado();
        }

        private ServicioAplicacionCorreosCompras Aplicacion() => new ServicioAplicacionCorreosCompras(repositorio, () => interruptorAplicar,
            A.Fake<IAvisadorCorreosPorAsignar>(), (empresa, productos, descripcion, causas) => promesas.AddRange(productos), () => ahora, errores.Add);

        private async Task Pasada(params CorreoCompras[] correos)
        {
            lectura = new LecturaBuzonCompras { Correos = correos.ToList() };
            _ = await new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add, horaCorte: () => new TimeSpan(11, 0, 0), esFestivo: _ => false,
                aplicacion: Aplicacion()).Pasada(ahora);
        }

        private const string PROVEEDOR_TEXTO = "Su pedido: 220950 ha salido hoy.\nEntrega estimada: 25/11/2026\nTracking: 1Z21E7X76890915153";

        private void UnPedido() => repositorio.AnadirPedido(220950, "1080", "COSMETICOS INVENTADOS, S.L.", new DateTime(2026, 11, 10, 9, 0, 0),
            new DateTime(2026, 12, 10), new DateTime(2026, 12, 10));

        [TestMethod]
        public async Task Job_LaFechaDeLaAgenciaCorrigeLaDelAvisoDelProveedor_ConRastroYPromesa()
        {
            UnPedido();
            await Pasada(Correo("envios@cosmeticos-inventados.es", "Su pedido ha salido", PROVEEDOR_TEXTO, ahora.AddMinutes(-30)));
            Assert.IsTrue(repositorio.Lineas[220950].All(l => l.FechaRecepcion == new DateTime(2026, 11, 25)), "Primero, la del proveedor");
            Assert.AreEqual(OrigenEntregaCorreo.PROVEEDOR, repositorio.Correos.Single().OrigenEntrega);

            ahora = ahora.AddHours(3);
            await Pasada(Correo("pkginfo@ups.com", "Tu paquete está de camino", UPS_ESTIMADA.Replace("Lunes 16/11/2026", "Lunes 23/11/2026"), ahora.AddMinutes(-10)));

            Assert.IsTrue(repositorio.Lineas[220950].All(l => l.FechaRecepcion == new DateTime(2026, 11, 23)), "La de la agencia manda");
            Assert.AreEqual(2, repositorio.Rastros.Count);
            Assert.AreEqual(OrigenEntregaCorreo.AGENCIA, repositorio.Rastros.Last().OrigenFecha);
            Assert.AreEqual(0, errores.Count, string.Join(" | ", errores.Select(e => e.Message)));
        }

        [TestMethod]
        public async Task Job_UnEntregadoDeLaAgencia_NoPoneFecha_PeroGuardaElEstado()
        {
            UnPedido();
            await Pasada(Correo("envios@cosmeticos-inventados.es", "Su pedido ha salido", PROVEEDOR_TEXTO, ahora.AddMinutes(-30)));
            ahora = new DateTime(2026, 11, 19, 12, 0, 0);
            int rastros = repositorio.Rastros.Count;

            await Pasada(Correo("pkginfo@ups.com", "Actualización de UPS: Paquete entregado", UPS_ENTREGADO.Replace("13/11/2026", "19/11/2026"), ahora.AddMinutes(-10)));

            Assert.AreEqual(rastros, repositorio.Rastros.Count, "No cambia la fecha");
            Assert.IsTrue(repositorio.Lineas[220950].All(l => l.FechaRecepcion == new DateTime(2026, 11, 25)));
            CorreoSeguimientoFila entregado = repositorio.Correos.Last();
            Assert.AreEqual("Entregado", entregado.EstadoEnvio);
            Assert.IsNull(entregado.PedidoAplicado);
        }

        [TestMethod]
        public async Task Job_SeurSinCodigo_SeGuarda_YVaPorElRemitenteDelPaqueteRecordado()
        {
            repositorio.AnadirPedido(220960, "1090", "POSTQUAM COSMETIC, S.L.", new DateTime(2026, 11, 12, 9, 0, 0), new DateTime(2026, 12, 10));
            repositorio.RemitentesPaquete.Add(new RemitentePaqueteRecordado { Id = 1, RemitentePaquete = "postquam", Empresa = "1", Proveedor = "1090" });

            await Pasada(Correo("infoenvios@mail.seur.info", SEUR_PREVISTA_ASUNTO, SEUR_PREVISTA_HTML, ahora.AddMinutes(-20), html: true));

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual("SEUR", fila.Agencia);
            Assert.AreEqual("PV26-56989", fila.Codigo);
            Assert.AreEqual(220960, fila.PedidoAplicado);
            Assert.IsTrue(repositorio.Lineas[220960].All(l => l.FechaRecepcion == new DateTime(2026, 11, 19)));
        }

        [TestMethod]
        public async Task Reanalizar_InsertaLosQueAntesNoSeGuardaban_SinAplicar()
        {
            CorreoCompras seur = Correo("infoenvios@mail.seur.info", SEUR_HOY_ASUNTO, SEUR_HOY_HTML, ahora.AddHours(-1), html: true);
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._))
                .Returns(Task.FromResult(new LecturaBuzonCompras { Correos = new List<CorreoCompras> { seur } }));

            ResultadoReanalisisCorreos resultado = await new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add,
                horaCorte: () => new TimeSpan(11, 0, 0), esFestivo: _ => false, aplicacion: Aplicacion()).Reanalizar(7, ahora);

            Assert.AreEqual(1, resultado.Insertados);
            Assert.AreEqual("EnReparto", repositorio.Correos.Single().EstadoEnvio);
            Assert.AreEqual(0, repositorio.LlamadasAplicar);
        }

        #endregion

        #region Consulta a la agencia (marco)

        /// <summary>Una agencia de prueba que sabe consultar (en producción, ninguna todavía).</summary>
        private class AgenciaConsultable : AgenciaEntradaPorPatrones, IConsultaEntregaAgencia
        {
            public Func<string, CancellationToken, Task<EntregaAgencia>> Respuesta { get; set; }
            public int Llamadas { get; private set; }
            public override string Nombre => "UPS";
            protected override string[] Dominios => new[] { "ups.com" };

            public Task<EntregaAgencia> ConsultarEntrega(string codigo, CancellationToken cancelacion)
            {
                Llamadas++;
                return Respuesta(codigo, cancelacion);
            }
        }

        private AgenciaConsultable consultable;
        private bool interruptorConsulta;

        private ServicioConsultaEntregaAgencias Consulta() => new ServicioConsultaEntregaAgencias(repositorio, Aplicacion(),
            new IAgenciaEntrada[] { consultable }, () => interruptorConsulta, () => ahora, errores.Add, TimeSpan.FromMilliseconds(200));

        private async Task PrepararAplicado()
        {
            UnPedido();
            await Pasada(Correo("pkginfo@ups.com", "Tu paquete está de camino", UPS_ESTIMADA.Replace("Lunes 16/11/2026", "Lunes 23/11/2026"), ahora.AddMinutes(-10)));
            // El aviso de UPS no dice de qué pedido es: lo asigna Compras
            if (!repositorio.Correos.Single().PedidoAplicado.HasValue)
            {
                _ = await Aplicacion().Asignar(repositorio.Correos.Single().Id, new PeticionAsignarCorreoCompraDTO { Pedido = 220950 }, false, "Compras");
            }
            Assert.IsTrue(repositorio.Lineas[220950].All(l => l.FechaRecepcion == new DateTime(2026, 11, 23)));
            consultable = new AgenciaConsultable
            {
                Respuesta = (c, t) => Task.FromResult(new EntregaAgencia { Entrega = new EntregaEstimada { Dia = new DateTime(2026, 11, 26) }, Estado = EstadoEnvioAgencia.Reprogramada })
            };
            ahora = new DateTime(2026, 11, 19, 7, 30, 0);
        }

        [TestMethod]
        public async Task Consulta_InterruptorApagado_NiPregunta()
        {
            await PrepararAplicado();
            interruptorConsulta = false;

            ResultadoConsultaEntregaAgencias r = await Consulta().Pasada();

            Assert.AreEqual(EstadoConsultaEntregaAgencias.Apagado, r.Estado);
            Assert.AreEqual(0, consultable.Llamadas);
            Assert.IsTrue(repositorio.Lineas[220950].All(l => l.FechaRecepcion == new DateTime(2026, 11, 23)));
        }

        [TestMethod]
        public async Task Consulta_Encendido_CorrigeLaFechaPorElMismoCamino()
        {
            await PrepararAplicado();
            interruptorConsulta = true;
            int rastros = repositorio.Rastros.Count;

            ResultadoConsultaEntregaAgencias r = await Consulta().Pasada();

            Assert.AreEqual(1, r.Consultados);
            Assert.AreEqual(1, r.Corregidos);
            Assert.IsTrue(repositorio.Lineas[220950].All(l => l.FechaRecepcion == new DateTime(2026, 11, 26)));
            Assert.AreEqual(rastros + 1, repositorio.Rastros.Count);
            Assert.IsTrue(repositorio.Rastros.Last().Corregir);
            Assert.AreEqual(OrigenEntregaCorreo.CONSULTA, repositorio.Rastros.Last().OrigenFecha);
            Assert.AreEqual("Reprogramada", repositorio.Correos.Single().EstadoEnvio);
            Assert.AreEqual(ahora, repositorio.Correos.Single().EntregaConsultadaEl);
        }

        [TestMethod]
        public async Task Consulta_ConElInterruptorDeAplicarApagado_GuardaLoQueDicePeroNoEscribeEnCompras()
        {
            await PrepararAplicado();
            interruptorConsulta = true;
            interruptorAplicar = false;

            ResultadoConsultaEntregaAgencias r = await Consulta().Pasada();

            Assert.AreEqual(0, r.Corregidos);
            Assert.AreEqual(new DateTime(2026, 11, 26), repositorio.Correos.Single().EntregaEstimadaDia);
            Assert.IsTrue(repositorio.Lineas[220950].All(l => l.FechaRecepcion == new DateTime(2026, 11, 23)));
        }

        [TestMethod]
        public async Task Consulta_LaAgenciaFallaOTarda_NoRompeNiCambiaNada()
        {
            await PrepararAplicado();
            interruptorConsulta = true;
            consultable.Respuesta = (c, t) => throw new InvalidOperationException("429 Too Many Requests");

            ResultadoConsultaEntregaAgencias r = await Consulta().Pasada();
            Assert.AreEqual(1, r.Consultados);
            Assert.AreEqual(0, r.Contestados);

            consultable.Respuesta = async (c, t) => { await Task.Delay(5000).ConfigureAwait(false); return new EntregaAgencia { Estado = EstadoEnvioAgencia.EnCamino }; };
            r = await Consulta().Pasada();
            Assert.AreEqual(0, r.Contestados);
            Assert.IsTrue(repositorio.Lineas[220950].All(l => l.FechaRecepcion == new DateTime(2026, 11, 23)));
            Assert.IsTrue(errores.Count >= 2, "Se apunta");
        }

        [TestMethod]
        public async Task Consulta_PedidoYaRecibido_NoSePregunta()
        {
            await PrepararAplicado();
            interruptorConsulta = true;
            repositorio.Recibir(220950, ahora.Date);

            ResultadoConsultaEntregaAgencias r = await Consulta().Pasada();

            Assert.AreEqual(0, r.Consultados);
            Assert.AreEqual(0, consultable.Llamadas);
        }

        [TestMethod]
        public async Task Consulta_SinLasColumnas_NoHaceNada()
        {
            await PrepararAplicado();
            interruptorConsulta = true;
            repositorio.ConEstado = false;

            Assert.AreEqual(EstadoConsultaEntregaAgencias.SinTabla, (await Consulta().Pasada()).Estado);
            Assert.AreEqual(0, consultable.Llamadas);
        }

        #endregion

        #region Reenvío a Compras

        [TestMethod]
        public void Reenvio_ElEstadoDeQuienEsLaFechaYElEnlace()
        {
            var fila = new CorreoSeguimientoFila
            {
                Agencia = "DB Schenker", Codigo = "ESMNR264773770", EntregaEstimadaDia = new DateTime(2026, 11, 23), OrigenEntrega = OrigenEntregaCorreo.AGENCIA,
                EstadoEnvio = "EnReparto"
            };
            string cabecera = System.Net.WebUtility.HtmlDecode(TextosReenvioCorreoSeguimiento.Cabecera(fila, new ResultadoCorreoGuardado(), "x@y.es", new DateTime(2026, 11, 18)));

            StringAssert.Contains(cabecera, "Entrega que anuncia (la agencia): 23/11/2026");
            StringAssert.Contains(cabecera, "Estado según la agencia: en reparto");
            StringAssert.Contains(cabecera, URL_DSV);
            Assert.AreEqual("La agencia dice que ya está entregado: no se pone ninguna fecha (la recepción la marca el albarán).",
                TextosReenvioCorreoSeguimiento.QueSeHaHecho(new ResultadoCorreoGuardado { Desenlace = DesenlaceCorreoSeguimiento.Entregado }));
        }

        #endregion
    }
}
