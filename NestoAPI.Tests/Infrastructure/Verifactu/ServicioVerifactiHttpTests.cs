using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Infraestructure.Verifactu.Verifacti;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// Tests de la capa HTTP de ServicioVerifacti contra el contrato REAL de la API,
    /// verificado con el humo sandbox del 17/07/26 (Fase A del plan Verifactu):
    /// - Las líneas usan nomenclatura SII: base_imponible / tipo_impositivo / cuota_repercutida
    ///   / tipo_recargo_equivalencia / cuota_recargo_equivalencia (NO base/tipo/cuota).
    /// - La respuesta de éxito NO trae campo "success": trae estado/uuid/url/qr/huella.
    /// - La consulta de estado es GET verifactu/status?uuid=... (NO verifactu/status/{uuid}).
    /// </summary>
    [TestClass]
    public class ServicioVerifactiHttpTests
    {
        private const string RESPUESTA_EXITO_REAL =
            "{\"estado\":\"Pendiente\",\"uuid\":\"45a826e0-949d-4832-a036-f71c690c16d0\"," +
            "\"url\":\"https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=X\",\"qr\":\"QRBASE64\"," +
            "\"huella\":\"BD9ECE8A\"}";

        private FakeHttpHandler handler;
        private ServicioVerifacti servicio;

        [TestInitialize]
        public void Setup()
        {
            handler = new FakeHttpHandler
            {
                Respuesta = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RESPUESTA_EXITO_REAL, Encoding.UTF8, "application/json")
                }
            };
            servicio = new ServicioVerifacti(new HttpClient(handler), "vf_test_clave", "https://api.verifacti.com/", true);
        }

        private static VerifactuFacturaRequest CrearRequest()
        {
            return new VerifactuFacturaRequest
            {
                Serie = "NV",
                Numero = "9999001",
                FechaExpedicion = new DateTime(2026, 7, 17),
                TipoFactura = "F1",
                Descripcion = "Venta de productos",
                NifDestinatario = "A78368255",
                NombreDestinatario = "NUEVA VISION SA",
                ImporteTotal = 126.20m,
                DesgloseIva = new List<VerifactuDesgloseIva>
                {
                    new VerifactuDesgloseIva
                    {
                        BaseImponible = 100.00m,
                        TipoIva = 21,
                        CuotaIva = 21.00m,
                        TipoRecargoEquivalencia = 5.2m,
                        CuotaRecargoEquivalencia = 5.20m
                    }
                }
            };
        }

        [TestMethod]
        public async Task EnviarFacturaAsync_LasLineasSerializanConNomenclaturaSII()
        {
            _ = await servicio.EnviarFacturaAsync(CrearRequest());

            StringAssert.Contains(handler.UltimoBody, "\"base_imponible\":100.0");
            StringAssert.Contains(handler.UltimoBody, "\"tipo_impositivo\":21");
            StringAssert.Contains(handler.UltimoBody, "\"cuota_repercutida\":21.0");
            StringAssert.Contains(handler.UltimoBody, "\"tipo_recargo_equivalencia\":5.2");
            StringAssert.Contains(handler.UltimoBody, "\"cuota_recargo_equivalencia\":5.2");
        }

        [TestMethod]
        public async Task EnviarFacturaAsync_RespuestaRealSinCampoSuccess_EsExitosa()
        {
            VerifactuResponse respuesta = await servicio.EnviarFacturaAsync(CrearRequest());

            Assert.IsTrue(respuesta.Exitoso, $"Debe ser exitosa. Error: {respuesta.MensajeError}");
            Assert.AreEqual("45a826e0-949d-4832-a036-f71c690c16d0", respuesta.Uuid);
            Assert.AreEqual("Pendiente", respuesta.Estado);
            Assert.AreEqual("QRBASE64", respuesta.QrBase64);
            Assert.AreEqual("BD9ECE8A", respuesta.Huella);
        }

        [TestMethod]
        public async Task EnviarFacturaAsync_RespuestaConError_NoEsExitosa()
        {
            handler.Respuesta = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"El campo base_imponible es requerido para cada linea.\"}", Encoding.UTF8, "application/json")
            };

            VerifactuResponse respuesta = await servicio.EnviarFacturaAsync(CrearRequest());

            Assert.IsFalse(respuesta.Exitoso);
            StringAssert.Contains(respuesta.MensajeError, "base_imponible");
        }

        [TestMethod]
        public async Task ConsultarEstadoAsync_UsaQueryStringConElUuid()
        {
            handler.Respuesta = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"nif\":\"X\",\"serie\":\"NV\",\"numero\":\"9999001\",\"operacion\":\"Alta\",\"estado\":\"Correcto\"}",
                    Encoding.UTF8, "application/json")
            };

            VerifactuResponse respuesta = await servicio.ConsultarEstadoAsync("45a826e0-949d-4832-a036-f71c690c16d0");

            Assert.AreEqual("verifactu/status?uuid=45a826e0-949d-4832-a036-f71c690c16d0",
                handler.UltimaPeticion.RequestUri.PathAndQuery.TrimStart('/'));
            Assert.IsTrue(respuesta.Exitoso, $"Debe ser exitosa. Error: {respuesta.MensajeError}");
            Assert.AreEqual("Correcto", respuesta.Estado);
        }

        private class FakeHttpHandler : HttpMessageHandler
        {
            public HttpRequestMessage UltimaPeticion { get; private set; }
            public string UltimoBody { get; private set; }
            public HttpResponseMessage Respuesta { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                UltimaPeticion = request;
                UltimoBody = request.Content != null ? await request.Content.ReadAsStringAsync() : null;
                return Respuesta;
            }
        }
    }
}
