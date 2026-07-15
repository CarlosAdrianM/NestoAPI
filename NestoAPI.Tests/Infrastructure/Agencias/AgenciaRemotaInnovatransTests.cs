using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Estrategia remota de Innovatrans + mapeo de tipo de servicio. Se prueba con un fake del cliente
    /// SOAP (IClienteSoapDataTrans): NO se llama a producción ni se monta HTTP, solo se verifica la
    /// orquestación (insertar -> etiquetar) y la traducción del envío a la petición DataTrans.
    /// </summary>
    [TestClass]
    public class AgenciaRemotaInnovatransTests
    {
        [TestMethod]
        public void TipoServicio_PorZona_EligeElCodigoCorrecto()
        {
            Assert.AreEqual("0048", MapeadorTipoServicioDataTrans.TipoServicioDesdeCodigoPostal("28001")); // Madrid
            Assert.AreEqual("0048", MapeadorTipoServicioDataTrans.TipoServicioDesdeCodigoPostal("46001")); // Peninsular
            Assert.AreEqual("0014", MapeadorTipoServicioDataTrans.TipoServicioDesdeCodigoPostal("1000-001")); // Portugal
            Assert.AreEqual("0EXP", MapeadorTipoServicioDataTrans.TipoServicioDesdeCodigoPostal("07001")); // Baleares
        }

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentException))]
        public void TipoServicio_Canarias_Lanza_PorqueVaPorCanteras()
        {
            MapeadorTipoServicioDataTrans.TipoServicioDesdeCodigoPostal("35001"); // Las Palmas
        }

        [TestMethod]
        [ExpectedException(typeof(System.ArgumentException))]
        public void TipoServicio_ZonaNoCubierta_Lanza()
        {
            // "EXTER" es el centinela de Extranjero (CalcularZona no distingue países por CP: un CP
            // de 5 dígitos cualquiera cae en Peninsular, así que para forzar Extranjero usamos EXTER).
            MapeadorTipoServicioDataTrans.TipoServicioDesdeCodigoPostal("EXTER");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_Exito_InsertaConLosDatosYDevuelveAlbaranYEtiqueta()
        {
            var fake = new FakeClienteSoap();
            // El <bultos> del insert (aquí "2") se IGNORA: DTX lo devuelve siempre 1/vacío. Los bultos
            // salen del nº de etiquetas ZPL reales (NestoAPI#270); RespEtiquetaZpl trae 1 etiqueta.
            fake.Responder("InsertarEnvios", RespInsertar("0123456789", "2"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZpl());

            IAgenciaRemota agencia = new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente());

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsTrue(r.Exito);
            Assert.AreEqual("0123456789", r.Albaran);
            Assert.AreEqual(1, r.Bultos, "Los bultos salen del nº de etiquetas ZPL, no del <bultos> del insert.");
            Assert.IsTrue(r.Etiqueta.Exito);
            Assert.AreEqual("XlhBfkNJMTUw", r.Etiqueta.Contenido);

            // Insertó primero y luego pidió la etiqueta ZPL del albarán devuelto.
            Assert.AreEqual("InsertarEnvios", fake.Llamadas[0].Operacion);
            Assert.AreEqual("BusquedaEtiquetas", fake.Llamadas[1].Operacion);

            string insertar = fake.Llamadas[0].Xml;
            StringAssert.Contains(insertar, "<com:tipoServ>0048</com:tipoServ>");          // Madrid -> Economy
            StringAssert.Contains(insertar, "<com:nombreRem>NUEVA VISION</com:nombreRem>"); // remitente fijo
            StringAssert.Contains(insertar, "<com:provinciaRem>028</com:provinciaRem>");    // CP 28119 -> 028
            StringAssert.Contains(insertar, "<com:largo>32</com:largo>");  // caja mediana por defecto
            StringAssert.Contains(insertar, "<com:ancho>23</com:ancho>");
            StringAssert.Contains(insertar, "<com:alto>29</com:alto>");

            string etiqueta = fake.Llamadas[1].Xml;
            StringAssert.Contains(etiqueta, "<mes:albaran>0123456789</mes:albaran>");
            StringAssert.Contains(etiqueta, "<mes:formato>1</mes:formato>"); // ZPL
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_DestinoPortugal_EnviaCodPostalComprimidoYTipoServPortugal()
        {
            // Portugal: tipoServ 0014 (14H) y codPostalDes comprimido a "6"+4 dígitos (1000-001 -> 61000),
            // regla del integrador (22/06/26). Antes mandábamos el canónico "1000-001": regresión.
            // paisDes va SIEMPRE "ESP" (23/06/26): DataTrans canaliza Portugal vía España; mandar "PRT"
            // lo rechazaba con codError 402 "No existe agencia asociada al país" (verificado en prod,
            // albarán 6521355001 con paisDes=ESP). El CP "6"+4 y la provincia "053" identifican Portugal.
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertar("9990001112", "1"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZpl());

            DatosEnvioRemoto envio = EnvioMadrid();
            envio.CodigoPostal = "3830-004"; // Ílhavo (Aveiro)
            envio.Poblacion = "ÍLHAVO-AVEIRO"; // viene de la dirección con tilde y sufijo del distrito

            await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(envio);

            string insertar = fake.Llamadas[0].Xml;
            StringAssert.Contains(insertar, "<com:paisDes>ESP</com:paisDes>");
            Assert.IsFalse(insertar.Contains("<com:paisDes>PRT</com:paisDes>"),
                "paisDes no debe viajar como PRT: DataTrans rechaza Portugal con país PRT.");
            StringAssert.Contains(insertar, "<com:tipoServ>0014</com:tipoServ>");
            StringAssert.Contains(insertar, "<com:codPostalDes>63830</com:codPostalDes>");
            StringAssert.Contains(insertar, "<com:provinciaDes>053</com:provinciaDes>");
            // Población normalizada al catálogo de DTX (sin tilde, mayúsculas, sin el sufijo).
            StringAssert.Contains(insertar, "<com:poblacionDes>ILHAVO</com:poblacionDes>");
            // Portugal SÍ canaliza por población.
            StringAssert.Contains(insertar, "<com:canalizarPorPoblacion>true</com:canalizarPorPoblacion>");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_DestinoEspana_NoCanalizaPorPoblacion()
        {
            // En España NO se manda canalizarPorPoblacion (canaliza por CP/provincia como siempre);
            // activarlo arriesgaría que poblaciones con tildes/variantes no cuadren con el catálogo.
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertar("9990001112", "1"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZpl());

            await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsFalse(fake.Llamadas[0].Xml.Contains("canalizarPorPoblacion"),
                "España no debe canalizar por población.");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_PesoCero_NoLlamaAlSoapYDevuelveErrorClaro()
        {
            var fake = new FakeClienteSoap();

            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Peso = 0m;

            ResultadoTramitacionRemota r = await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(envio);

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "peso");
            Assert.AreEqual(0, fake.Llamadas.Count, "Con peso 0 no debe llamar a DataTrans.");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_SiElInsertFalla_NoPideEtiquetaYDevuelveError()
        {
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertarError("500", "Tipo de servicio no valido"));

            IAgenciaRemota agencia = new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente());

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "Tipo de servicio");
            Assert.AreEqual(1, fake.Llamadas.Count, "No debe pedir etiqueta si el envío fue rechazado.");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_AlbaranGuionConCodError_RechazaConElMsgErrorYNoPideEtiqueta()
        {
            // Caso real (pedido 920350 a Portugal): DTX devuelve albarán "-" con codError 402 y un
            // msgError. Es un RECHAZO, no un albarán válido: hay que dar el msgError, no pedir etiqueta
            // (evita el mensaje confuso "no devolvió etiqueta ZPL") ni persistir un albarán fantasma.
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios",
                @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                     <ns4:InsertarEnviosTypeOut xmlns:ns4=""http://messageout.dtx.sw""><ns4:resultado>
                       <ns2:albaran xmlns:ns2=""http://complexType.dtx.sw"">-</ns2:albaran>
                       <ns2:codError xmlns:ns2=""http://complexType.dtx.sw"">402</ns2:codError>
                       <ns2:msgError xmlns:ns2=""http://complexType.dtx.sw"">Error. No existe agencia asociada al pais indicado.</ns2:msgError>
                     </ns4:resultado></ns4:InsertarEnviosTypeOut></soapenv:Body></soapenv:Envelope>");

            ResultadoTramitacionRemota r = await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "No existe agencia asociada");
            Assert.IsNull(r.Albaran, "No debe persistir el albarán placeholder \"-\".");
            Assert.AreEqual(1, fake.Llamadas.Count, "No debe pedir etiqueta si el insert fue rechazado.");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_AlbaranConTextoDeError_NoEsExitoNiPideEtiqueta()
        {
            // DTX a veces devuelve codError=200 pero mete un texto de error en el campo albarán.
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertar("ERROR: Se ha producido una excepcion: java.lang.NullPointerException", "1"));

            ResultadoTramitacionRemota r = await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "NullPointerException");
            Assert.AreEqual(1, fake.Llamadas.Count, "No debe pedir etiqueta si el envío fue rechazado.");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_InsertOkPeroEtiquetaEsPdf_PreservaAlbaranYNoEsExito()
        {
            // Innovatrans no tiene ZPL para este envío → devuelve un PDF. El insert SÍ se hizo (albarán
            // asignado): debemos preservar el albarán (para no reinsertar en un reintento) pero NO dar
            // por buena la etiqueta (un PDF es inservible para la Zebra).
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertar("6520139001", "1"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaPdf());

            ResultadoTramitacionRemota r = await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsFalse(r.Exito, "Un PDF no es una etiqueta ZPL válida.");
            Assert.AreEqual("6520139001", r.Albaran, "El albarán debe conservarse para no reinsertar.");
            Assert.AreEqual(1, r.Bultos);
            StringAssert.Contains(r.Error, "ZPL");
        }

        // --- NestoAPI#300: 405 "Canalizacion incorrecta" en CPs con varias poblaciones ---
        // Verificado en prod (15/07/26, envío 246812): DTX solo canaliza por CP si el CP tiene UNA
        // población en su catálogo; con varias exige que poblacionDes coincida con el texto EXACTO
        // del catálogo (sin necesidad de canalizarPorPoblacion: cambiar AVILÉS por AVILES bastó).

        [TestMethod]
        public async Task InsertarYEtiquetar_CanalizacionIncorrecta_ReintentaConLaPoblacionDelCatalogo()
        {
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertarError("405", "Error. Canalizacion incorrecta."));
            fake.Responder("InsertarEnvios", RespInsertar("6530724009", "1"));
            fake.Responder("BuscarPoblacion", RespBuscarPoblacion("AVILES", "CALIERO (ENTREVIÑAS AVILES)", "ENTREVIÑAS (AVILES)"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZpl());

            DatosEnvioRemoto envio = EnvioMadrid();
            envio.CodigoPostal = "33401";
            envio.Poblacion = "AVILÉS"; // con tilde, como viene de la ficha del cliente

            ResultadoTramitacionRemota r = await NuevaAgenciaConLectura(fake).InsertarYEtiquetarAsync(envio);

            Assert.IsTrue(r.Exito, $"Debe reintentar con la población del catálogo y triunfar. Error: {r.Error}");
            Assert.AreEqual("6530724009", r.Albaran);
            Assert.AreEqual(4, fake.Llamadas.Count);
            Assert.AreEqual("InsertarEnvios", fake.Llamadas[0].Operacion);
            Assert.AreEqual("BuscarPoblacion", fake.Llamadas[1].Operacion);
            Assert.AreEqual("InsertarEnvios", fake.Llamadas[2].Operacion);
            Assert.AreEqual("BusquedaEtiquetas", fake.Llamadas[3].Operacion);
            StringAssert.Contains(fake.Llamadas[2].Xml, "<com:poblacionDes>AVILES</com:poblacionDes>");
            Assert.IsFalse(fake.Llamadas[2].Xml.Contains("canalizarPorPoblacion"),
                "En España no hace falta el flag: basta el texto exacto del catálogo (verificado en prod).");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_CanalizacionIncorrectaSinMatch_DevuelveErrorConLasPoblacionesValidas()
        {
            // Si ninguna población del catálogo casa con la nuestra, NO se reintenta a ciegas: se
            // devuelve un error accionable con las opciones válidas para que el almacén corrija.
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertarError("405", "Error. Canalizacion incorrecta."));
            fake.Responder("BuscarPoblacion", RespBuscarPoblacion("VILLAPERI", "LUGONES"));

            DatosEnvioRemoto envio = EnvioMadrid();
            envio.CodigoPostal = "33199";
            envio.Poblacion = "POBLACION INVENTADA";

            ResultadoTramitacionRemota r = await NuevaAgenciaConLectura(fake).InsertarYEtiquetarAsync(envio);

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "VILLAPERI");
            StringAssert.Contains(r.Error, "LUGONES");
            Assert.AreEqual(1, fake.Llamadas.Count(l => l.Operacion == "InsertarEnvios"),
                "Sin match no debe reinsertar a ciegas.");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_CanalizacionIncorrectaSinOperacionesDeLectura_DevuelveElErrorOriginal()
        {
            // Sin OperacionesLecturaDataTrans cableadas no se puede consultar el catálogo: se
            // devuelve el error original de DTX, como antes (defensivo, no debe lanzar).
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertarError("405", "Error. Canalizacion incorrecta."));

            IAgenciaRemota agencia = new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente());
            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "Canalizacion incorrecta");
            Assert.AreEqual(1, fake.Llamadas.Count);
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_ReembolsoCentinela_ViajaComoCero()
        {
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertar("0000000001", "1"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZpl());

            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Reembolso = -1m; // centinela "no cobrar"

            await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(envio);

            StringAssert.Contains(fake.Llamadas[0].Xml, "<com:reembolso>0</com:reembolso>");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_TresEtiquetas_PersisteTresBultos()
        {
            // DataTrans devuelve <bultos>=1 en el insert (siempre, poco fiable) pero pedimos 3 bultos y DTX
            // genera 3 etiquetas ZPL: se persisten 3 (el recuento real de etiquetas), no 1. Regresión:
            // envíos de varios bultos quedaban persistidos como 1 (casos reales Estela/Dumapacar 25-jun-2026).
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertar("6522393002", "1"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZplConEtiquetas(3));

            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Bultos = 3;

            ResultadoTramitacionRemota r = await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(envio);

            Assert.IsTrue(r.Exito);
            Assert.AreEqual(3, r.Bultos, "Debe persistir el nº de etiquetas ZPL realmente generadas.");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_MenosEtiquetasQueBultosPedidos_PersisteLasEtiquetasReales()
        {
            // NestoAPI#270: se piden 6 bultos pero DTX solo genera 1 etiqueta. Antes se persistía el mayor
            // (6) tapando el problema; ahora persistimos la realidad (1 etiqueta = 1 bulto), que es lo que
            // se imprime y lo que la agencia ha registrado. La discrepancia se registra aparte para escalar.
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertar("6527885001", "1"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZplConEtiquetas(1));

            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Bultos = 6;

            ResultadoTramitacionRemota r = await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(envio);

            Assert.IsTrue(r.Exito);
            Assert.AreEqual(1, r.Bultos, "No debe persistir el mayor: gana el nº de etiquetas realmente generadas.");
        }

        // --- NestoAPI#270: el recuento de bultos sale del nº de etiquetas ^XA reales ---

        [TestMethod]
        public void ContarEtiquetasZpl_Base64ConVariasEtiquetas_CuentaLosBloquesXA()
        {
            string zpl = "^XA^CI28^FDbulto1^FS^XZ^XA^CI28^FDbulto2^FS^XZ";
            string base64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(zpl));

            Assert.AreEqual(2, AgenciaRemotaInnovatrans.ContarEtiquetasZpl(base64));
        }

        [TestMethod]
        public void ContarEtiquetasZpl_ZplEnCrudo_CuentaLosBloquesXA()
        {
            Assert.AreEqual(3, AgenciaRemotaInnovatrans.ContarEtiquetasZpl("^XA...^XZ^XA...^XZ^XA...^XZ"));
        }

        [TestMethod]
        public void ContarEtiquetasZpl_ContenidoNoZpl_DevuelveCero()
        {
            // PDF (base64 de "%PDF"), vacío y base64 inválido: no se reconoce -> 0 (el llamante cae a lo pedido).
            Assert.AreEqual(0, AgenciaRemotaInnovatrans.ContarEtiquetasZpl("JVBERi0xLjcK"));
            Assert.AreEqual(0, AgenciaRemotaInnovatrans.ContarEtiquetasZpl(""));
            Assert.AreEqual(0, AgenciaRemotaInnovatrans.ContarEtiquetasZpl("XlhB@@@no-es-base64"));
        }

        [TestMethod]
        public async Task Reimprimir_PideLaEtiquetaZplDelAlbaran()
        {
            var fake = new FakeClienteSoap();
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZpl());

            await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .ReimprimirAsync("0123456789");

            Assert.AreEqual("BusquedaEtiquetas", fake.Llamadas.Single().Operacion);
            StringAssert.Contains(fake.Llamadas[0].Xml, "<mes:formato>1</mes:formato>");
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_EstadoEntregado_DevuelveEntregadoConFechaReal()
        {
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(
                ("DOCUMENTADO", "24/06/2026", "12:06:00"),
                ("ENTREGADO", "25/06/2026", "10:30:00")));
            fake.Responder("ConsultarIncidencias", RespIncidencias()); // sin incidencias

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6521905001");

            Assert.AreEqual(EstadoEnvioSeguimiento.Entregado, seg.Estado);
            Assert.AreEqual(new System.DateTime(2026, 6, 25, 10, 30, 0), seg.FechaEntrega);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_IncidenciaSinResolver_DevuelveIncidentado()
        {
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(("EN TRÁNSITO", "24/06/2026", "18:00:00")));
            fake.Responder("ConsultarIncidencias", RespIncidencias(("AUSENTE", false)));

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6521905001");

            Assert.AreEqual(EstadoEnvioSeguimiento.Incidentado, seg.Estado);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_IncidenciaResueltaYEntregado_DevuelveEntregado()
        {
            // Una incidencia YA resuelta no cuenta como incidentado: si está entregado, es Entregado.
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(("ENTREGADO", "26/06/2026", "09:15:00")));
            fake.Responder("ConsultarIncidencias", RespIncidencias(("DIRECCIÓN ERRÓNEA", true)));

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6521905001");

            Assert.AreEqual(EstadoEnvioSeguimiento.Entregado, seg.Estado);
            Assert.AreEqual(new System.DateTime(2026, 6, 26, 9, 15, 0), seg.FechaEntrega);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_SinEntregaNiIncidencia_DevuelveTramitado()
        {
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(("DOCUMENTADO", "24/06/2026", "12:06:00")));
            fake.Responder("ConsultarIncidencias", RespIncidencias());

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6521905001");

            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, seg.Estado);
            Assert.IsNull(seg.FechaEntrega);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_DevueltoAOrigen_DevuelveDevuelto()
        {
            // Devuelto es terminal y manda aunque la incidencia esté cerrada: el paquete ya volvió.
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(
                ("EN TRÁNSITO", "24/06/2026", "18:00:00"),
                ("DEVUELTO A ORIGEN", "27/06/2026", "11:00:00")));
            fake.Responder("ConsultarIncidencias", RespIncidencias(("AUSENTE REITERADO", true)));

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6521905001");

            Assert.AreEqual(EstadoEnvioSeguimiento.Devuelto, seg.Estado);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_ConsultarIncidencias_NoEnviaBuscar()
        {
            // Regresión #254: el WSDL del servicio Incidencias NO define el subelemento <buscar> (sí el de
            // Estados). Enviarlo provocaba HTTP 500 "Unexpected subelement buscar" en Axis2 y dejaba sin
            // efecto TODO el seguimiento (la excepción abortaba ConsultarSeguimiento para cada envío).
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(("DOCUMENTADO", "24/06/2026", "12:06:00")));
            fake.Responder("ConsultarIncidencias", RespIncidencias());

            await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6521905001");

            string incidencias = fake.Llamadas.Single(l => l.Operacion == "ConsultarIncidencias").Xml;
            StringAssert.Contains(incidencias, "<mes:albaran>6521905001</mes:albaran>");
            Assert.IsFalse(incidencias.Contains("buscar"),
                "ConsultarIncidencias no debe enviar <buscar>: Axis2 lo rechaza con HTTP 500.");

            // ConsultarEstados sí lo admite y debe seguir enviándolo.
            string estados = fake.Llamadas.Single(l => l.Operacion == "ConsultarEstados").Xml;
            StringAssert.Contains(estados, "<mes:buscar>1</mes:buscar>");
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_EstadoNoContemplado_DevuelveTramitadoSinRomper()
        {
            // Un estado que no reconocemos (ni entrega/devolución/incidencia, ni un "en tránsito" conocido)
            // cae en el catch-all Tramitado y se loguea para descubrirlo (NestoAPI#259). El logueo nunca
            // debe romper el seguimiento: el resultado sigue siendo Tramitado.
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(("RETENIDO EN ADUANA", "26/06/2026", "09:00:00")));
            fake.Responder("ConsultarIncidencias", RespIncidencias());

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6521905001");

            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, seg.Estado);
            Assert.AreEqual("RETENIDO EN ADUANA", seg.Detalle);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_SecuenciaRealConReparto_DevuelveEntregado()
        {
            // Caso real albarán 6521905001 (NestoAPI#260): la web de GLS marcaba Entregado pero el WS iba
            // por detrás y su último evento era REPARTO. Al día siguiente sí constaba ENTREGADO. La secuencia
            // completa (LEIDO EN DESTINO 08:08 -> REPARTO 08:27 -> ENTREGADO 16:08) debe resolver a Entregado
            // con la fecha real, sin que los estados intermedios despisten a la detección de entrega.
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(
                ("DOCUMENTADO", "24/06/2026", "16:43:06"),
                ("DOCUMENTADO", "24/06/2026", "17:01:26"),
                ("LEIDO EN DESTINO", "25/06/2026", "08:08:11"),
                ("REPARTO", "25/06/2026", "08:27:39"),
                ("ENTREGADO", "25/06/2026", "16:08:24")));
            fake.Responder("ConsultarIncidencias", RespIncidencias());

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6521905001");

            Assert.AreEqual(EstadoEnvioSeguimiento.Entregado, seg.Estado);
            Assert.AreEqual(new System.DateTime(2026, 6, 25, 16, 8, 24), seg.FechaEntrega);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_EstadoIncidencia_DevuelveIncidentado()
        {
            // Caso real albarán 6522393004 (NestoAPI#259): DataTrans devuelve la incidencia como ESTADO en
            // ConsultarEstados ('INCIDENCIA'), pero ConsultarIncidencias viene vacío para el mismo envío.
            // Antes caía en el catch-all -> Tramitado y NO entraba en "Incidentados". Debe ser Incidentado.
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(
                ("DOCUMENTADO", "26/06/2026", "12:06:00"),
                ("INCIDENCIA", "27/06/2026", "09:00:00")));
            fake.Responder("ConsultarIncidencias", RespIncidencias()); // sin incidencias en el servicio Incidencias

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6522393004");

            Assert.AreEqual(EstadoEnvioSeguimiento.Incidentado, seg.Estado);
            Assert.AreEqual("INCIDENCIA", seg.Detalle);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_IncidenciaSeguidaDeTransito_DevuelveTramitado()
        {
            // Una INCIDENCIA resuelta (seguida de un evento de tránsito posterior) NO es incidentado: manda
            // el último evento. Garantiza que no marcamos incidentado un envío que ya volvió a circular.
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(
                ("INCIDENCIA", "27/06/2026", "09:00:00"),
                ("EN TRÁNSITO", "28/06/2026", "08:00:00")));
            fake.Responder("ConsultarIncidencias", RespIncidencias());

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6522393004");

            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, seg.Estado);
            Assert.AreEqual("EN TRÁNSITO", seg.Detalle);
        }

        [TestMethod]
        public async Task ConsultarSeguimiento_RepartoSinEntrega_DevuelveTramitado()
        {
            // REPARTO/LEIDO EN DESTINO son tránsito intermedio (catalogados en NestoAPI#260): mientras no
            // haya ENTREGADO, el envío sigue Tramitado (no terminal), y ya no se loguean como "no contemplado".
            var fake = new FakeClienteSoap();
            fake.Responder("ConsultarEstados", RespEstados(
                ("LEIDO EN DESTINO", "25/06/2026", "08:08:11"),
                ("REPARTO", "25/06/2026", "08:27:39")));
            fake.Responder("ConsultarIncidencias", RespIncidencias());

            SeguimientoEnvioRemoto seg = await NuevaAgenciaConLectura(fake).ConsultarSeguimientoAsync("6521905001");

            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, seg.Estado);
            Assert.AreEqual("REPARTO", seg.Detalle);
            Assert.IsNull(seg.FechaEntrega);
        }

        private static AgenciaRemotaInnovatrans NuevaAgenciaConLectura(FakeClienteSoap fake)
            => new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente(), null, new OperacionesLecturaDataTrans(fake));

        private static DireccionDataTrans Remitente() => new DireccionDataTrans
        {
            Nombre = "NUEVA VISION", Telefono = "916280826", CodigoPostal = "28119",
            Poblacion = "ALGETE", Direccion = "Poligono"
        };

        private static DatosEnvioRemoto EnvioMadrid() => new DatosEnvioRemoto
        {
            Referencia = "PED12345", Nombre = "CLIENTE", Telefono = "600000000",
            CodigoPostal = "28001", Poblacion = "MADRID", Direccion = "Calle Mayor 1",
            Peso = 1.5m, Bultos = 1
        };

        private static string RespInsertar(string albaran, string bultos) =>
            $@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                 <ns4:InsertarEnviosTypeOut xmlns:ns4=""http://messageout.dtx.sw""><ns4:resultado>
                   <ns2:albaran xmlns:ns2=""http://complexType.dtx.sw"">{albaran}</ns2:albaran>
                   <ns2:bultos xmlns:ns2=""http://complexType.dtx.sw"">{bultos}</ns2:bultos>
                 </ns4:resultado></ns4:InsertarEnviosTypeOut></soapenv:Body></soapenv:Envelope>";

        private static string RespInsertarError(string codError, string msg) =>
            $@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                 <ns4:InsertarEnviosTypeOut xmlns:ns4=""http://messageout.dtx.sw""><ns4:resultado>
                   <ns2:albaran xmlns:ns2=""http://complexType.dtx.sw""/>
                   <ns2:codError xmlns:ns2=""http://complexType.dtx.sw"">{codError}</ns2:codError>
                   <ns2:msgError xmlns:ns2=""http://complexType.dtx.sw"">{msg}</ns2:msgError>
                 </ns4:resultado></ns4:InsertarEnviosTypeOut></soapenv:Body></soapenv:Envelope>";

        private static string RespEtiquetaZpl() =>
            @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                <ns4:BusquedaEtiquetasTypeOut xmlns:ns4=""http://messageout.dtx.sw""><ns4:return>
                  <ns5:tipo xmlns:ns5=""http://listType.dtx.sw"">application/zpl</ns5:tipo>
                  <ns5:codificacion xmlns:ns5=""http://listType.dtx.sw"">base64</ns5:codificacion>
                  <ns5:contenido xmlns:ns5=""http://listType.dtx.sw"">XlhBfkNJMTUw</ns5:contenido>
                </ns4:return><ns4:returnError/></ns4:BusquedaEtiquetasTypeOut></soapenv:Body></soapenv:Envelope>";

        // Etiqueta ZPL con N bloques (^XA...^XZ), como DTX devuelve un envío de N bultos (un ^XA por bulto),
        // en base64 (igual que la respuesta real). Sirve para verificar que los bultos persistidos salen del
        // recuento real de etiquetas y no del <bultos> del insert (NestoAPI#270).
        private static string RespEtiquetaZplConEtiquetas(int numeroEtiquetas)
        {
            string zpl = string.Concat(Enumerable.Repeat("^XA^CI28^FDbulto^FS^XZ", numeroEtiquetas));
            string base64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(zpl));
            return $@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                <ns4:BusquedaEtiquetasTypeOut xmlns:ns4=""http://messageout.dtx.sw""><ns4:return>
                  <ns5:tipo xmlns:ns5=""http://listType.dtx.sw"">application/zpl</ns5:tipo>
                  <ns5:codificacion xmlns:ns5=""http://listType.dtx.sw"">base64</ns5:codificacion>
                  <ns5:contenido xmlns:ns5=""http://listType.dtx.sw"">{base64}</ns5:contenido>
                </ns4:return><ns4:returnError/></ns4:BusquedaEtiquetasTypeOut></soapenv:Body></soapenv:Envelope>";
        }

        // Etiqueta que DTX devuelve cuando NO tiene ZPL para el envío: un PDF en base64 (empieza por
        // "JVBE" = base64 de "%PDF"). EsZpl debe rechazarla.
        private static string RespEtiquetaPdf() =>
            @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                <ns4:BusquedaEtiquetasTypeOut xmlns:ns4=""http://messageout.dtx.sw""><ns4:return>
                  <ns5:tipo xmlns:ns5=""http://listType.dtx.sw"">application/pdf</ns5:tipo>
                  <ns5:codificacion xmlns:ns5=""http://listType.dtx.sw"">base64</ns5:codificacion>
                  <ns5:contenido xmlns:ns5=""http://listType.dtx.sw"">JVBERi0xLjcK</ns5:contenido>
                </ns4:return><ns4:returnError/></ns4:BusquedaEtiquetasTypeOut></soapenv:Body></soapenv:Envelope>";

        // Respuesta de BuscarPoblacion con las poblaciones del catálogo de DTX para un CP (#300).
        private static string RespBuscarPoblacion(params string[] poblaciones)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string poblacion in poblaciones)
            {
                sb.Append($@"<ns5:resultado>
                  <ns2:poblacion xmlns:ns2=""http://complexType.dtx.sw"">{poblacion}</ns2:poblacion>
                  <ns2:kilometros xmlns:ns2=""http://complexType.dtx.sw"">0.0</ns2:kilometros>
                </ns5:resultado>");
            }
            return $@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                <ns5:BuscarPoblacionTypeOut xmlns:ns5=""http://messageout.dtx.sw"">{sb}<ns5:respuesta>{(poblaciones.Length > 0 ? 200 : 300)}</ns5:respuesta></ns5:BuscarPoblacionTypeOut>
              </soapenv:Body></soapenv:Envelope>";
        }

        // Respuesta de ConsultarEstados con N eventos (nombre/fecha/hora). Sin eventos -> respuesta 300.
        private static string RespEstados(params (string Nombre, string Fecha, string Hora)[] estados)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < estados.Length; i++)
            {
                sb.Append($@"<ns5:resultado>
                  <ns2:numero xmlns:ns2=""http://complexType.dtx.sw"">{i + 1}</ns2:numero>
                  <ns2:codigo xmlns:ns2=""http://complexType.dtx.sw"">{i}</ns2:codigo>
                  <ns2:nombre xmlns:ns2=""http://complexType.dtx.sw"">{estados[i].Nombre}</ns2:nombre>
                  <ns2:fecha xmlns:ns2=""http://complexType.dtx.sw"">{estados[i].Fecha}</ns2:fecha>
                  <ns2:hora xmlns:ns2=""http://complexType.dtx.sw"">{estados[i].Hora}</ns2:hora>
                </ns5:resultado>");
            }
            return $@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                <ns5:ConsultarEstadosTypeOut xmlns:ns5=""http://messageout.dtx.sw"">{sb}<ns5:respuesta>{(estados.Length > 0 ? 200 : 300)}</ns5:respuesta></ns5:ConsultarEstadosTypeOut>
              </soapenv:Body></soapenv:Envelope>";
        }

        // Respuesta de ConsultarIncidencias con N incidencias (nombre/resuelta). Sin incidencias -> respuesta 300.
        private static string RespIncidencias(params (string Nombre, bool Resuelta)[] incidencias)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < incidencias.Length; i++)
            {
                sb.Append($@"<ns5:resultado>
                  <ns2:numero xmlns:ns2=""http://complexType.dtx.sw"">{i + 1}</ns2:numero>
                  <ns2:codigo xmlns:ns2=""http://complexType.dtx.sw"">{i}</ns2:codigo>
                  <ns2:nombre xmlns:ns2=""http://complexType.dtx.sw"">{incidencias[i].Nombre}</ns2:nombre>
                  <ns2:resuelta xmlns:ns2=""http://complexType.dtx.sw"">{(incidencias[i].Resuelta ? "1" : "0")}</ns2:resuelta>
                  <ns2:fecha xmlns:ns2=""http://complexType.dtx.sw"">24/06/2026</ns2:fecha>
                  <ns2:hora xmlns:ns2=""http://complexType.dtx.sw"">18:00:00</ns2:hora>
                </ns5:resultado>");
            }
            return $@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                <ns5:ConsultarIncidenciasTypeOut xmlns:ns5=""http://messageout.dtx.sw"">{sb}<ns5:respuesta>{(incidencias.Length > 0 ? 200 : 300)}</ns5:respuesta></ns5:ConsultarIncidenciasTypeOut>
              </soapenv:Body></soapenv:Envelope>";
        }

        /// <summary>Fake de IClienteSoapDataTrans: responde por operación y captura las llamadas.</summary>
        private class FakeClienteSoap : IClienteSoapDataTrans
        {
            // Cola por operación: llamar varias veces a Responder para la misma operación encola
            // respuestas secuenciales (1ª llamada → 1ª respuesta...); la última respuesta se repite
            // si hay más llamadas que respuestas. Necesario para el retry de canalización (#300):
            // el primer InsertarEnvios devuelve 405 y el segundo, éxito.
            private readonly Dictionary<string, Queue<string>> _respuestas = new Dictionary<string, Queue<string>>();
            public List<(string Servicio, string Operacion, string Xml)> Llamadas { get; } =
                new List<(string, string, string)>();

            public void Responder(string operacion, string xmlRespuesta)
            {
                if (!_respuestas.TryGetValue(operacion, out Queue<string> cola))
                {
                    cola = new Queue<string>();
                    _respuestas[operacion] = cola;
                }
                cola.Enqueue(xmlRespuesta);
            }

            public Task<XDocument> EjecutarAsync(string servicio, string operacion, params XElement[] parametros)
            {
                // Declaramos mes/com en el envoltorio (como el envelope real) para que los prefijos
                // salgan sin xmlns inline y los asserts puedan buscar <com:tipoServ>, <mes:albaran>...
                XNamespace mes = "http://messagein.dtx.sw";
                XNamespace com = "http://complexType.dtx.sw";
                var cuerpo = new XElement("params",
                    new XAttribute(XNamespace.Xmlns + "mes", mes.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "com", com.NamespaceName),
                    parametros.Cast<object>().ToArray());
                Llamadas.Add((servicio, operacion, cuerpo.ToString()));
                Queue<string> cola = _respuestas[operacion];
                string respuesta = cola.Count > 1 ? cola.Dequeue() : cola.Peek();
                return Task.FromResult(XDocument.Parse(respuesta));
            }
        }
    }
}
