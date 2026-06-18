using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Cliente SOAP de DataTrans DTX (transporte) probado a través de la operación de lectura
    /// BuscarPoblacion. Verifica la construcción del sobre (autenticación + parámetros), el header
    /// SOAPAction, la URL del servicio y el parseo de las respuestas (200 ok, 300 sin datos, 400 auth).
    /// </summary>
    [TestClass]
    public class ClienteSoapDataTransTests
    {
        private const string RESPUESTA_200 =
            @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
                <soapenv:Body>
                  <ns5:BuscarPoblacionTypeOut xmlns:ns5=""http://messageout.dtx.sw"">
                    <ns5:resultado>
                      <ns2:poblacion xmlns:ns2=""http://complexType.dtx.sw"">MADRID</ns2:poblacion>
                      <ns2:kilometros xmlns:ns2=""http://complexType.dtx.sw"">0.0</ns2:kilometros>
                    </ns5:resultado>
                    <ns5:respuesta>200</ns5:respuesta>
                  </ns5:BuscarPoblacionTypeOut>
                </soapenv:Body>
              </soapenv:Envelope>";

        private const string RESPUESTA_300 =
            @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
                <soapenv:Body>
                  <ns5:BuscarPoblacionTypeOut xmlns:ns5=""http://messageout.dtx.sw"">
                    <ns5:respuesta>300</ns5:respuesta>
                    <ns5:msgError>No existen poblaciones para el codigo postal indicado.</ns5:msgError>
                  </ns5:BuscarPoblacionTypeOut>
                </soapenv:Body>
              </soapenv:Envelope>";

        private const string RESPUESTA_400 =
            @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
                <soapenv:Body>
                  <ns5:BuscarPoblacionTypeOut xmlns:ns5=""http://messageout.dtx.sw"">
                    <ns5:respuesta>400</ns5:respuesta>
                    <ns5:msgError>Datos de autenticacion incorrectos.</ns5:msgError>
                  </ns5:BuscarPoblacionTypeOut>
                </soapenv:Body>
              </soapenv:Envelope>";

        private static ConfiguracionInnovatrans ConfigDePrueba()
        {
            return new ConfiguracionInnovatrans(
                "http://h001.iatsl.es:8081/dtxSW/services/",
                new CredencialesDataTrans
                {
                    Identificador = "91253",
                    Empresa = "91253",
                    Email = "carlosadrian@nuevavision.es",
                    Clave = "900150983cd24fb0d6963f7d28e17f72" // MD5 de ejemplo
                });
        }

        // Control de tasa que no duerme (espera = no-op).
        private static ControlTasaDataTrans ControlSinEspera()
        {
            return new ControlTasaDataTrans(intervaloMinimo: TimeSpan.Zero, esperar: _ => Task.CompletedTask);
        }

        private static OperacionesLecturaDataTrans CrearOperaciones(HandlerFalso handler)
        {
            var cliente = new ClienteSoapDataTrans(ConfigDePrueba(), ControlSinEspera(), new HttpClient(handler));
            return new OperacionesLecturaDataTrans(cliente);
        }

        [TestMethod]
        public async Task BuscarPoblacion_ConstruyeLaPeticionSoapEsperada()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESPUESTA_200);

            await CrearOperaciones(handler).BuscarPoblacionAsync("28001");

            // URL del servicio Poblaciones (sin doble barra).
            Assert.AreEqual("http://h001.iatsl.es:8081/dtxSW/services/Poblaciones",
                handler.UltimaPeticion.RequestUri.ToString());
            // SOAPAction = urn:{operacion}.
            Assert.AreEqual("urn:BuscarPoblacion",
                string.Join("", handler.UltimaPeticion.Headers.GetValues("SOAPAction")));
            // Content-Type text/xml.
            Assert.AreEqual("text/xml", handler.UltimaPeticion.Content.Headers.ContentType.MediaType);

            string cuerpo = handler.UltimoCuerpo;
            StringAssert.Contains(cuerpo, "BuscarPoblacionTypeIn");
            StringAssert.Contains(cuerpo, "<com:clave>900150983cd24fb0d6963f7d28e17f72</com:clave>");
            StringAssert.Contains(cuerpo, "<com:identificador>91253</com:identificador>");
            StringAssert.Contains(cuerpo, "28001"); // el código postal pedido
        }

        [TestMethod]
        public async Task BuscarPoblacion_Respuesta200_DevuelvePoblaciones()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESPUESTA_200);

            ResultadoBuscarPoblacion r = await CrearOperaciones(handler).BuscarPoblacionAsync("28001");

            Assert.AreEqual(200, r.Respuesta);
            Assert.AreEqual(1, r.Poblaciones.Count);
            Assert.AreEqual("MADRID", r.Poblaciones[0].Poblacion);
            Assert.AreEqual(0.0m, r.Poblaciones[0].Kilometros);
        }

        [TestMethod]
        public async Task BuscarPoblacion_Respuesta300_SinPoblacionesPeroAutenticado()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESPUESTA_300);

            ResultadoBuscarPoblacion r = await CrearOperaciones(handler).BuscarPoblacionAsync("00000");

            Assert.AreEqual(300, r.Respuesta);
            Assert.AreEqual(0, r.Poblaciones.Count);
            StringAssert.Contains(r.MensajeError, "No existen poblaciones");
        }

        [TestMethod]
        public async Task BuscarPoblacion_Respuesta400_AutenticacionIncorrecta()
        {
            var handler = new HandlerFalso(HttpStatusCode.OK, RESPUESTA_400);

            ResultadoBuscarPoblacion r = await CrearOperaciones(handler).BuscarPoblacionAsync("28001");

            Assert.AreEqual(400, r.Respuesta);
            StringAssert.Contains(r.MensajeError, "autenticacion");
        }

        [TestMethod]
        [ExpectedException(typeof(DataTransException))]
        public async Task BuscarPoblacion_SiHttpFalla_LanzaDataTransException()
        {
            var handler = new HandlerFalso(HttpStatusCode.InternalServerError, "Boom");

            await CrearOperaciones(handler).BuscarPoblacionAsync("28001");
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
