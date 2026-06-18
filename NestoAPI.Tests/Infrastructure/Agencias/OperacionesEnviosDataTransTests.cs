using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Operaciones de escritura/etiquetado de DataTrans DTX (InsertarEnvios + BusquedaEtiquetas),
    /// probadas con un handler HTTP falso (NO se llama a producción: insertar crea un envío real).
    /// </summary>
    [TestClass]
    public class OperacionesEnviosDataTransTests
    {
        private const string RESP_INSERTAR_OK =
            @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
                <soapenv:Body>
                  <ns4:InsertarEnviosTypeOut xmlns:ns4=""http://messageout.dtx.sw"">
                    <ns4:resultado>
                      <ns2:albaran xmlns:ns2=""http://complexType.dtx.sw"">0123456789</ns2:albaran>
                      <ns2:codError xmlns:ns2=""http://complexType.dtx.sw""/>
                      <ns2:msgError xmlns:ns2=""http://complexType.dtx.sw""/>
                      <ns2:bultos xmlns:ns2=""http://complexType.dtx.sw"">1</ns2:bultos>
                      <ns2:ageDestino xmlns:ns2=""http://complexType.dtx.sw"">AGENCIA MADRID</ns2:ageDestino>
                    </ns4:resultado>
                  </ns4:InsertarEnviosTypeOut>
                </soapenv:Body>
              </soapenv:Envelope>";

        private const string RESP_INSERTAR_ERROR =
            @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
                <soapenv:Body>
                  <ns4:InsertarEnviosTypeOut xmlns:ns4=""http://messageout.dtx.sw"">
                    <ns4:resultado>
                      <ns2:albaran xmlns:ns2=""http://complexType.dtx.sw""/>
                      <ns2:codError xmlns:ns2=""http://complexType.dtx.sw"">500</ns2:codError>
                      <ns2:msgError xmlns:ns2=""http://complexType.dtx.sw"">Tipo de servicio no valido</ns2:msgError>
                    </ns4:resultado>
                  </ns4:InsertarEnviosTypeOut>
                </soapenv:Body>
              </soapenv:Envelope>";

        private const string RESP_ETIQUETA_ZPL =
            @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
                <soapenv:Body>
                  <ns4:BusquedaEtiquetasTypeOut xmlns:ns4=""http://messageout.dtx.sw"">
                    <ns4:return>
                      <ns5:tipo xmlns:ns5=""http://listType.dtx.sw"">application/zpl</ns5:tipo>
                      <ns5:codificacion xmlns:ns5=""http://listType.dtx.sw"">base64</ns5:codificacion>
                      <ns5:tamano xmlns:ns5=""http://listType.dtx.sw"">6031</ns5:tamano>
                      <ns5:contenido xmlns:ns5=""http://listType.dtx.sw"">XlhBfkNJMTUw</ns5:contenido>
                    </ns4:return>
                    <ns4:returnError/>
                  </ns4:BusquedaEtiquetasTypeOut>
                </soapenv:Body>
              </soapenv:Envelope>";

        private static OperacionesEnviosDataTrans CrearOperaciones(HandlerFalso handler)
        {
            var config = new ConfiguracionInnovatrans(
                "http://h001.iatsl.es:8081/dtxSW/services/",
                new CredencialesDataTrans { Identificador = "91253", Empresa = "91253", Email = "x@x.es", Clave = "abc" });
            var control = new ControlTasaDataTrans(intervaloMinimo: System.TimeSpan.Zero, esperar: _ => Task.CompletedTask);
            var cliente = new ClienteSoapDataTrans(config, control, new HttpClient(handler));
            return new OperacionesEnviosDataTrans(cliente);
        }

        private static EnvioDataTrans EnvioDePrueba()
        {
            return new EnvioDataTrans
            {
                Remitente = new DireccionDataTrans
                {
                    Nombre = "NUEVA VISION", Telefono = "916601047", CodigoPostal = "28119",
                    Poblacion = "ALGETE", Direccion = "Poligono"
                },
                Destinatario = new DireccionDataTrans
                {
                    Nombre = "CLIENTE", Telefono = "600000000", CodigoPostal = "28001",
                    Poblacion = "MADRID", Direccion = "Calle Mayor 1"
                },
                Referencia = "PED12345",
                TipoServicio = "0048",
                Largo = 15, Alto = 10, Ancho = 20,
                PesoReal = 1.5m,
                Docs = 0, Paqs = 1
            };
        }

        [TestMethod]
        public async Task InsertarEnvio_ConstruyeLaPeticionConLosCamposEsperados()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESP_INSERTAR_OK);

            await CrearOperaciones(handler).InsertarEnvioAsync(EnvioDePrueba());

            Assert.AreEqual("http://h001.iatsl.es:8081/dtxSW/services/Envios",
                handler.UltimaPeticion.RequestUri.ToString());
            Assert.AreEqual("urn:InsertarEnvios",
                string.Join("", handler.UltimaPeticion.Headers.GetValues("SOAPAction")));

            string cuerpo = handler.UltimoCuerpo;
            StringAssert.Contains(cuerpo, "InsertarEnviosTypeIn");
            StringAssert.Contains(cuerpo, "<com:tipoServ>0048</com:tipoServ>");
            // Provincia derivada del CP (28119 -> 028, 28001 -> 028).
            StringAssert.Contains(cuerpo, "<com:provinciaRem>028</com:provinciaRem>");
            StringAssert.Contains(cuerpo, "<com:provinciaDes>028</com:provinciaDes>");
            // Defaults de negocio.
            StringAssert.Contains(cuerpo, "<com:portesPagados>S</com:portesPagados>");
            StringAssert.Contains(cuerpo, "<com:comReembPag>N</com:comReembPag>");
        }

        [TestMethod]
        public async Task InsertarEnvio_DecimalesViajanConPunto_AunEnCulturaEspanola()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                // El servidor es español (coma decimal): el WS exige punto -> no debe colarse "1,5".
                Thread.CurrentThread.CurrentCulture = new CultureInfo("es-ES");
                var handler = new HandlerFalso(HttpStatusCode.OK, RESP_INSERTAR_OK);

                await CrearOperaciones(handler).InsertarEnvioAsync(EnvioDePrueba());

                StringAssert.Contains(handler.UltimoCuerpo, "<com:pesoReal>1.5</com:pesoReal>");
                Assert.IsFalse(handler.UltimoCuerpo.Contains("1,5"), "Un decimal con coma rompería el WS.");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [TestMethod]
        public async Task InsertarEnvio_RespuestaConAlbaran_EsExito()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESP_INSERTAR_OK);

            ResultadoInsertarEnvio r = await CrearOperaciones(handler).InsertarEnvioAsync(EnvioDePrueba());

            Assert.IsTrue(r.Exito);
            Assert.AreEqual("0123456789", r.Albaran);
            Assert.AreEqual("1", r.Bultos);
            Assert.AreEqual("AGENCIA MADRID", r.AgenciaDestino);
        }

        [TestMethod]
        public async Task InsertarEnvio_SinAlbaran_NoEsExitoYTraeElError()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESP_INSERTAR_ERROR);

            ResultadoInsertarEnvio r = await CrearOperaciones(handler).InsertarEnvioAsync(EnvioDePrueba());

            Assert.IsFalse(r.Exito);
            Assert.AreEqual(500, r.CodError);
            StringAssert.Contains(r.MsgError, "Tipo de servicio");
        }

        [TestMethod]
        public async Task BuscarEtiqueta_Zpl_ConstruyePeticionConFormato1()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESP_ETIQUETA_ZPL);

            await CrearOperaciones(handler).BuscarEtiquetaAsync("0123456789", FormatoEtiquetaDataTrans.Zpl);

            Assert.AreEqual("http://h001.iatsl.es:8081/dtxSW/services/Etiquetas",
                handler.UltimaPeticion.RequestUri.ToString());
            Assert.AreEqual("urn:BusquedaEtiquetas",
                string.Join("", handler.UltimaPeticion.Headers.GetValues("SOAPAction")));

            string cuerpo = handler.UltimoCuerpo;
            StringAssert.Contains(cuerpo, "<mes:albaran>0123456789</mes:albaran>");
            StringAssert.Contains(cuerpo, "<mes:formato>1</mes:formato>");
            // Sin bultos -> no se envían (etiqueta completa).
            Assert.IsFalse(cuerpo.Contains("desdeBulto"));
            Assert.IsFalse(cuerpo.Contains("hastaBulto"));
        }

        [TestMethod]
        public async Task BuscarEtiqueta_ConBultos_IncluyeDesdeYHasta()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESP_ETIQUETA_ZPL);

            await CrearOperaciones(handler).BuscarEtiquetaAsync("0123456789", FormatoEtiquetaDataTrans.Zpl, 1, 2);

            string cuerpo = handler.UltimoCuerpo;
            StringAssert.Contains(cuerpo, "<mes:desdeBulto>1</mes:desdeBulto>");
            StringAssert.Contains(cuerpo, "<mes:hastaBulto>2</mes:hastaBulto>");
        }

        [TestMethod]
        public async Task BuscarEtiqueta_DevuelveContenidoYCodificacion()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESP_ETIQUETA_ZPL);

            EtiquetaDataTrans etiqueta = await CrearOperaciones(handler).BuscarEtiquetaAsync("0123456789");

            Assert.IsTrue(etiqueta.Exito);
            Assert.AreEqual("application/zpl", etiqueta.Tipo);
            Assert.AreEqual("base64", etiqueta.Codificacion);
            Assert.AreEqual(6031, etiqueta.TamanoBytes);
            Assert.AreEqual("XlhBfkNJMTUw", etiqueta.Contenido);
        }

        /// <summary>HttpMessageHandler de prueba: captura la petición y devuelve una respuesta fija.</summary>
        private class HandlerFalso : HttpMessageHandler
        {
            private readonly HttpStatusCode _codigo;
            private readonly string _cuerpoRespuesta;

            public HttpRequestMessage UltimaPeticion { get; private set; }
            public string UltimoCuerpo { get; private set; }

            public HandlerFalso(HttpStatusCode codigo, string cuerpoRespuesta)
            {
                _codigo = codigo;
                _cuerpoRespuesta = cuerpoRespuesta;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                UltimaPeticion = request;
                if (request.Content != null)
                {
                    UltimoCuerpo = await request.Content.ReadAsStringAsync();
                }
                return new HttpResponseMessage(_codigo)
                {
                    Content = new StringContent(_cuerpoRespuesta, System.Text.Encoding.UTF8, "text/xml")
                };
            }
        }
    }
}
