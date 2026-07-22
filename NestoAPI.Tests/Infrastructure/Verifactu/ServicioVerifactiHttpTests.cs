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
        public async Task ModificarFacturaAsync_HacePutAModifyConRechazoPrevio()
        {
            // NestoAPI#346: la subsanación es el camino legal para declarar fuera de plazo.
            // Contrato del ejemplo oficial de Verifacti: PUT verifactu/modify con rechazo_previo
            // en la raíz y el resto del body igual que el create.
            VerifactuResponse respuesta = await servicio.ModificarFacturaAsync(CrearRequest(), "X");

            Assert.AreEqual(HttpMethod.Put, handler.UltimaPeticion.Method);
            Assert.AreEqual("verifactu/modify", handler.UltimaPeticion.RequestUri.PathAndQuery.TrimStart('/'));
            StringAssert.Contains(handler.UltimoBody, "\"rechazo_previo\":\"X\"");
            StringAssert.Contains(handler.UltimoBody, "\"base_imponible\":100.0");
            Assert.IsTrue(respuesta.Exitoso, $"Debe ser exitosa. Error: {respuesta.MensajeError}");
        }

        [TestMethod]
        public async Task EnviarFacturaAsync_ElCreateNoLlevaRechazoPrevio()
        {
            _ = await servicio.EnviarFacturaAsync(CrearRequest());

            Assert.AreEqual(HttpMethod.Post, handler.UltimaPeticion.Method);
            Assert.IsFalse(handler.UltimoBody.Contains("rechazo_previo"),
                "El alta normal no debe llevar rechazo_previo");
        }

        [TestMethod]
        public async Task EnviarFacturaAsync_LineaOss_SerializaClaveRegimenYCalificacionSinTipoNiCuota()
        {
            // NestoAPI#347: contrato del ejemplo OSS oficial de Verifacti — la línea lleva solo
            // base_imponible + clave_regimen + calificacion_operacion (tipo o cuota = rechazo AEAT)
            var request = CrearRequest();
            request.DesgloseIva = new List<VerifactuDesgloseIva>
            {
                new VerifactuDesgloseIva
                {
                    BaseImponible = 151.60m,
                    ClaveRegimen = "17",
                    CalificacionOperacion = "N2"
                }
            };
            request.ImporteTotal = 151.60m;

            _ = await servicio.EnviarFacturaAsync(request);

            StringAssert.Contains(handler.UltimoBody, "\"clave_regimen\":\"17\"");
            StringAssert.Contains(handler.UltimoBody, "\"calificacion_operacion\":\"N2\"");
            Assert.IsFalse(handler.UltimoBody.Contains("tipo_impositivo"),
                "Una línea N2 no puede llevar tipo_impositivo");
            Assert.IsFalse(handler.UltimoBody.Contains("cuota_repercutida"),
                "Una línea N2 no puede llevar cuota_repercutida");
        }

        [TestMethod]
        public async Task EnviarFacturaAsync_ConIdOtro_SerializaIdOtroYNoNif()
        {
            // NestoAPI#339: pasaporte → id_otro {codigo_pais, id_type, id} en la raíz y SIN nif
            // (contrato del ejemplo B2C intracomunitario de Verifacti)
            var request = CrearRequest();
            request.IdOtro = new VerifactuIdOtro { CodigoPais = "MA", IdType = "03", Id = "AB123456" };
            request.NifDestinatario = null;

            _ = await servicio.EnviarFacturaAsync(request);

            StringAssert.Contains(handler.UltimoBody, "\"id_otro\"");
            StringAssert.Contains(handler.UltimoBody, "\"codigo_pais\":\"MA\"");
            StringAssert.Contains(handler.UltimoBody, "\"id_type\":\"03\"");
            StringAssert.Contains(handler.UltimoBody, "\"id\":\"AB123456\"");
            Assert.IsFalse(handler.UltimoBody.Contains("\"nif\""), "Con id_otro no puede viajar nif");
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
