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
        public async Task Reimprimir_PideLaEtiquetaZplDelAlbaran()
        {
            var fake = new FakeClienteSoap();
            fake.Responder("BusquedaEtiquetas", RespEtiquetaZpl());

            await new AgenciaRemotaInnovatrans(new OperacionesEnviosDataTrans(fake), Remitente())
                .ReimprimirAsync("0123456789");

            Assert.AreEqual("BusquedaEtiquetas", fake.Llamadas.Single().Operacion);
            StringAssert.Contains(fake.Llamadas[0].Xml, "<mes:formato>1</mes:formato>");
        }

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
