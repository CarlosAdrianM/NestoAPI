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
            fake.Responder("InsertarEnvios", RespInsertar("0123456789", "2"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZpl());

            IAgenciaRemota agencia = new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente());

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsTrue(r.Exito);
            Assert.AreEqual("0123456789", r.Albaran);
            Assert.AreEqual(2, r.Bultos);
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
        public async Task InsertarYEtiquetar_RespuestaBultosMenorQueLoPedido_ConservaLosBultosReales()
        {
            // DataTrans devuelve <bultos>=1 en el insert (poco fiable) pero pedimos 3 bultos (Paqs=3):
            // el resultado debe conservar 3 (los que generan las 3 etiquetas), no caer a 1. Regresión:
            // envíos de varios bultos quedaban persistidos como 1 (casos reales Estela/Dumapacar 25-jun-2026).
            var fake = new FakeClienteSoap();
            fake.Responder("InsertarEnvios", RespInsertar("6522393002", "1"));
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZpl());

            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Bultos = 3;

            ResultadoTramitacionRemota r = await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .InsertarYEtiquetarAsync(envio);

            Assert.IsTrue(r.Exito);
            Assert.AreEqual(3, r.Bultos, "Debe conservar los bultos pedidos (Paqs), no el <bultos> de la respuesta.");
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

        // Etiqueta que DTX devuelve cuando NO tiene ZPL para el envío: un PDF en base64 (empieza por
        // "JVBE" = base64 de "%PDF"). EsZpl debe rechazarla.
        private static string RespEtiquetaPdf() =>
            @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""><soapenv:Body>
                <ns4:BusquedaEtiquetasTypeOut xmlns:ns4=""http://messageout.dtx.sw""><ns4:return>
                  <ns5:tipo xmlns:ns5=""http://listType.dtx.sw"">application/pdf</ns5:tipo>
                  <ns5:codificacion xmlns:ns5=""http://listType.dtx.sw"">base64</ns5:codificacion>
                  <ns5:contenido xmlns:ns5=""http://listType.dtx.sw"">JVBERi0xLjcK</ns5:contenido>
                </ns4:return><ns4:returnError/></ns4:BusquedaEtiquetasTypeOut></soapenv:Body></soapenv:Envelope>";

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
            private readonly Dictionary<string, string> _respuestas = new Dictionary<string, string>();
            public List<(string Servicio, string Operacion, string Xml)> Llamadas { get; } =
                new List<(string, string, string)>();

            public void Responder(string operacion, string xmlRespuesta) => _respuestas[operacion] = xmlRespuesta;

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
                return Task.FromResult(XDocument.Parse(_respuestas[operacion]));
            }
        }
    }
}
