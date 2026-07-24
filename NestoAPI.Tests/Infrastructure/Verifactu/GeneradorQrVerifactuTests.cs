using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Infraestructure.Verifactu.Verifacti;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// NestoAPI#326: el QR tributario se genera EN LOCAL para que ninguna factura salga sin él. La
    /// URL de validación debe reproducir EXACTAMENTE la que ya devuelve el proveedor (verificado
    /// contra una URL real de Verifacti leída de CabFacturaVta.VerifactuURL).
    /// </summary>
    [TestClass]
    public class GeneradorQrVerifactuTests
    {
        [TestMethod]
        public void ConstruirUrlValidacion_Sandbox_ReproduceLaUrlRealDeVerifacti()
        {
            // URL real (sandbox) de la factura NV2612688:
            // https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=B75777847&numserie=8f44_NV2612688&fecha=24-07-2026&importe=324.09
            string url = GeneradorQrVerifactu.ConstruirUrlValidacion(
                "B75777847", "8f44_NV2612688", new DateTime(2026, 7, 24), 324.09m, esSandbox: true);

            Assert.AreEqual(
                "https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=B75777847&numserie=8f44_NV2612688&fecha=24-07-2026&importe=324.09",
                url);
        }

        [TestMethod]
        public void ConstruirUrlValidacion_Produccion_UsaElHostRealYFormatosCorrectos()
        {
            string url = GeneradorQrVerifactu.ConstruirUrlValidacion(
                "A78368255", "PREF_NV1", new DateTime(2027, 1, 2), 10m, esSandbox: false);

            StringAssert.StartsWith(url, "https://www2.agenciatributaria.gob.es/wlpl/TIKE-CONT/ValidarQR?");
            StringAssert.Contains(url, "importe=10.00"); // 2 decimales, punto decimal invariante
            StringAssert.Contains(url, "fecha=02-01-2027");
        }

        [TestMethod]
        public void GenerarPngQr_DevuelveUnPngValido()
        {
            byte[] png = GeneradorQrVerifactu.GenerarPngQr("https://prewww2.aeat.es/x");

            Assert.IsNotNull(png);
            Assert.IsTrue(png.Length > 8, "El PNG debe tener contenido");
            // Firma PNG: 89 50 4E 47
            CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
        }

        [TestMethod]
        public void GenerarPngQr_UrlVacia_DevuelveNull()
        {
            Assert.IsNull(GeneradorQrVerifactu.GenerarPngQr(null));
            Assert.IsNull(GeneradorQrVerifactu.GenerarPngQr("   "));
        }

        [TestMethod]
        public void ServicioVerifacti_GenerarQrLocal_FormaElNumserieConElPrefijoDeVerifacti()
        {
            var factura = new VerifactuFacturaRequest
            {
                Serie = "NV",
                Numero = "2612688", // el número ya viene sin la serie (MapeadorFacturaVerifactu)
                FechaExpedicion = new DateTime(2026, 7, 24),
                ImporteTotal = 324.09m
            };

            DatosQrLocalVerifactu datos = ServicioVerifacti.GenerarQrLocal(
                factura, nifEmisor: "B75777847", prefijoNumSerie: "8f44_", esSandbox: true);

            Assert.IsNotNull(datos);
            StringAssert.Contains(datos.Url, "numserie=8f44_NV2612688");
            StringAssert.Contains(datos.Url, "nif=B75777847");
            Assert.IsNotNull(datos.ImagenPngQr);
        }

        [TestMethod]
        public void ServicioVerifacti_GenerarQrLocal_SinNifOSinPrefijoOSinFactura_DevuelveNull()
        {
            var factura = new VerifactuFacturaRequest { Serie = "NV", Numero = "1", ImporteTotal = 1m };

            Assert.IsNull(ServicioVerifacti.GenerarQrLocal(factura, "", "8f44_", true), "sin NIF emisor no hay QR local");
            Assert.IsNull(ServicioVerifacti.GenerarQrLocal(factura, "B75777847", "   ", true), "sin prefijo no hay QR local");
            Assert.IsNull(ServicioVerifacti.GenerarQrLocal(null, "B75777847", "8f44_", true), "sin factura no hay QR local");
        }
    }
}
