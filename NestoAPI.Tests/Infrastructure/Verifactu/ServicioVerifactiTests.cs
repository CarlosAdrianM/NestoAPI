using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Infraestructure.Verifactu.Verifacti;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    [TestClass]
    public class ServicioVerifactiTests
    {
        #region Tests de DTOs

        [TestMethod]
        public void VerifactuFacturaRequest_CrearFacturaNormal_TieneDatosCorrectos()
        {
            var request = new VerifactuFacturaRequest
            {
                Serie = "NV",
                Numero = "12345",
                FechaExpedicion = new DateTime(2025, 12, 1),
                TipoFactura = "F1",
                Descripcion = "Venta de productos",
                NifDestinatario = "B12345678",
                NombreDestinatario = "Cliente Test S.L.",
                ImporteTotal = 121.00m,
                DesgloseIva = new List<VerifactuDesgloseIva>
                {
                    new VerifactuDesgloseIva
                    {
                        BaseImponible = 100.00m,
                        TipoIva = 21,
                        CuotaIva = 21.00m
                    }
                }
            };

            Assert.AreEqual("NV", request.Serie);
            Assert.AreEqual("12345", request.Numero);
            Assert.AreEqual("F1", request.TipoFactura);
            Assert.AreEqual(121.00m, request.ImporteTotal);
            Assert.AreEqual(1, request.DesgloseIva.Count);
            Assert.AreEqual("S", request.TipoRectificacion); // Default
        }

        [TestMethod]
        public void VerifactuFacturaRequest_CrearFacturaRectificativa_TieneDatosCorrectos()
        {
            var request = new VerifactuFacturaRequest
            {
                Serie = "RV",
                Numero = "00001",
                FechaExpedicion = new DateTime(2025, 12, 1),
                TipoFactura = "R1",
                Descripcion = "Rectificación por devolución",
                NifDestinatario = "B12345678",
                NombreDestinatario = "Cliente Test S.L.",
                ImporteTotal = -121.00m,
                TipoRectificacion = "S",
                FacturasRectificadas = new List<VerifactuFacturaRectificada>
                {
                    new VerifactuFacturaRectificada
                    {
                        Serie = "NV",
                        Numero = "12345",
                        FechaExpedicion = new DateTime(2025, 11, 15)
                    }
                },
                DesgloseIva = new List<VerifactuDesgloseIva>
                {
                    new VerifactuDesgloseIva
                    {
                        BaseImponible = -100.00m,
                        TipoIva = 21,
                        CuotaIva = -21.00m
                    }
                }
            };

            Assert.AreEqual("RV", request.Serie);
            Assert.AreEqual("R1", request.TipoFactura);
            Assert.AreEqual("S", request.TipoRectificacion);
            Assert.AreEqual(1, request.FacturasRectificadas.Count);
            Assert.AreEqual("NV", request.FacturasRectificadas[0].Serie);
        }

        [TestMethod]
        public void VerifactuDesgloseIva_ConRecargoEquivalencia_TieneDatosCorrectos()
        {
            var desglose = new VerifactuDesgloseIva
            {
                BaseImponible = 100.00m,
                TipoIva = 21,
                CuotaIva = 21.00m,
                TipoRecargoEquivalencia = 5.2m,
                CuotaRecargoEquivalencia = 5.20m
            };

            Assert.AreEqual(100.00m, desglose.BaseImponible);
            Assert.AreEqual(21, desglose.TipoIva);
            Assert.AreEqual(5.2m, desglose.TipoRecargoEquivalencia);
            Assert.AreEqual(5.20m, desglose.CuotaRecargoEquivalencia);
        }

        [TestMethod]
        public void VerifactuResponse_RespuestaExitosa_TieneDatosCorrectos()
        {
            var response = new VerifactuResponse
            {
                Exitoso = true,
                Uuid = "abc-123-def-456",
                Estado = "Aceptada",
                Url = "https://verifacti.com/factura/abc-123",
                QrBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
                Huella = "a1b2c3d4e5f6..."
            };

            Assert.IsTrue(response.Exitoso);
            Assert.AreEqual("abc-123-def-456", response.Uuid);
            Assert.AreEqual("Aceptada", response.Estado);
            Assert.IsNull(response.MensajeError);
        }

        [TestMethod]
        public void VerifactuResponse_RespuestaError_TieneDatosCorrectos()
        {
            var response = new VerifactuResponse
            {
                Exitoso = false,
                MensajeError = "NIF no válido",
                CodigoError = "INVALID_NIF"
            };

            Assert.IsFalse(response.Exitoso);
            Assert.AreEqual("NIF no válido", response.MensajeError);
            Assert.AreEqual("INVALID_NIF", response.CodigoError);
            Assert.IsNull(response.Uuid);
        }

        #endregion

        #region Tests de validación de datos

        [TestMethod]
        public void VerifactuFacturaRequest_DescripcionMayorA500Caracteres_SeTruncaEnServicio()
        {
            // Este test documenta que la descripción se trunca en el servicio al mapear
            var descripcionLarga = new string('X', 600);
            var request = new VerifactuFacturaRequest
            {
                Descripcion = descripcionLarga
            };

            // El DTO acepta cualquier longitud
            Assert.AreEqual(600, request.Descripcion.Length);
            // El truncamiento se hace en ServicioVerifacti.MapearAVerifactiRequest()
        }

        [TestMethod]
        public void VerifactuFacturaRequest_ListasInicializadasPorDefecto()
        {
            var request = new VerifactuFacturaRequest();

            Assert.IsNotNull(request.DesgloseIva);
            Assert.AreEqual(0, request.DesgloseIva.Count);
            Assert.IsNotNull(request.FacturasRectificadas);
            Assert.AreEqual(0, request.FacturasRectificadas.Count);
        }

        [TestMethod]
        public void VerifactuFacturaRequest_TipoRectificacionPorDefectoEsSustitucion()
        {
            var request = new VerifactuFacturaRequest();

            Assert.AreEqual("S", request.TipoRectificacion);
        }

        #endregion

        #region Tests de múltiples tipos de IVA

        [TestMethod]
        public void VerifactuFacturaRequest_MultiplesDesgloseIva_SoportaVariosTipos()
        {
            var request = new VerifactuFacturaRequest
            {
                Serie = "NV",
                Numero = "12346",
                FechaExpedicion = DateTime.Today,
                TipoFactura = "F1",
                ImporteTotal = 234.50m,
                DesgloseIva = new List<VerifactuDesgloseIva>
                {
                    new VerifactuDesgloseIva { BaseImponible = 100.00m, TipoIva = 21, CuotaIva = 21.00m },
                    new VerifactuDesgloseIva { BaseImponible = 50.00m, TipoIva = 10, CuotaIva = 5.00m },
                    new VerifactuDesgloseIva { BaseImponible = 50.00m, TipoIva = 4, CuotaIva = 2.00m },
                    new VerifactuDesgloseIva { BaseImponible = 50.00m, TipoIva = 0, CuotaIva = 0.00m }
                }
            };

            Assert.AreEqual(4, request.DesgloseIva.Count);
            Assert.AreEqual(21, request.DesgloseIva[0].TipoIva);
            Assert.AreEqual(10, request.DesgloseIva[1].TipoIva);
            Assert.AreEqual(4, request.DesgloseIva[2].TipoIva);
            Assert.AreEqual(0, request.DesgloseIva[3].TipoIva);
        }

        #endregion

        #region Tests de tipos de rectificativa

        [TestMethod]
        public void VerifactuFacturaRequest_TipoRectificativaR1_EsDevolucion()
        {
            var request = new VerifactuFacturaRequest
            {
                TipoFactura = "R1",
                TipoRectificacion = "S"
            };

            Assert.AreEqual("R1", request.TipoFactura);
        }

        [TestMethod]
        public void VerifactuFacturaRequest_TipoRectificativaR3_EsDeudaIncobrable()
        {
            var request = new VerifactuFacturaRequest
            {
                TipoFactura = "R3",
                TipoRectificacion = "S"
            };

            Assert.AreEqual("R3", request.TipoFactura);
        }

        [TestMethod]
        public void VerifactuFacturaRequest_TipoRectificativaR4_EsError()
        {
            var request = new VerifactuFacturaRequest
            {
                TipoFactura = "R4",
                TipoRectificacion = "I" // Por diferencia
            };

            Assert.AreEqual("R4", request.TipoFactura);
            Assert.AreEqual("I", request.TipoRectificacion);
        }

        #endregion
    }
}
