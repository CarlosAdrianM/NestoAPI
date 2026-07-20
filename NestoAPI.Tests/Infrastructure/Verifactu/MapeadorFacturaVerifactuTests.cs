using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// Tests del mapeo CabFacturaVta → VerifactuFacturaRequest (issue #34).
    /// El desglose por IVA debe replicar el cálculo de GestorFacturas.LeerFactura
    /// para que lo declarado a la AEAT coincida con lo impreso en la factura.
    /// </summary>
    [TestClass]
    public class MapeadorFacturaVerifactuTests
    {
        private CabFacturaVta CrearFacturaNV()
        {
            return new CabFacturaVta
            {
                Empresa = "1",
                Serie = "NV",
                Número = "NV2600123 ",
                Fecha = new DateTime(2026, 7, 17),
                CifNif = "12345678Z",
                NombreFiscal = "CLIENTE DE PRUEBA SL",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 100.00M, ImporteIVA = 21.00M }
                }
            };
        }

        private CabFacturaVta CrearRectificativaRV()
        {
            // Nuestras rectificativas son abonos: los importes van en NEGATIVO (rectificativa
            // "por diferencias" en Verifactu, issue #36).
            return new CabFacturaVta
            {
                Empresa = "1",
                Serie = "RV",
                Número = "RV2600001 ",
                Fecha = new DateTime(2026, 7, 20),
                CifNif = "12345678Z",
                NombreFiscal = "CLIENTE DE PRUEBA SL",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = -100.00M, ImporteIVA = -21.00M }
                }
            };
        }

        [TestMethod]
        public void Mapear_RectificativaSinTipoPersistido_UsaR1PorDiferenciasConLasFacturasRectificadas()
        {
            var factura = CrearRectificativaRV();
            var rectificadas = new List<VerifactuFacturaRectificada>
            {
                new VerifactuFacturaRectificada { Serie = "NV", Numero = "2600123", FechaExpedicion = new DateTime(2026, 6, 1) }
            };

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura, rectificadas);

            Assert.AreEqual("R1", request.TipoFactura); // defecto de la serie RV
            Assert.AreEqual("I", request.TipoRectificacion); // por diferencias: importes en negativo
            Assert.AreEqual(1, request.FacturasRectificadas.Count);
            Assert.AreEqual("NV", request.FacturasRectificadas[0].Serie);
            Assert.AreEqual("2600123", request.FacturasRectificadas[0].Numero);
        }

        [TestMethod]
        public void Mapear_RectificativaConTipoPersistido_RespetaElTipo()
        {
            // Cuando Nesto#244 permita elegir la causa, el tipo viaja en CabFacturaVta.TipoRectificativa
            var factura = CrearRectificativaRV();
            factura.TipoRectificativa = "r4 ";

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura, new List<VerifactuFacturaRectificada>());

            Assert.AreEqual("R4", request.TipoFactura);
        }

        [TestMethod]
        public void Mapear_RectificativaConImportesNegativos_ImporteTotalNegativo()
        {
            var factura = CrearRectificativaRV();

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura, new List<VerifactuFacturaRectificada>());

            Assert.AreEqual(-121.00M, request.ImporteTotal);
            Assert.AreEqual(-100.00M, request.DesgloseIva.Single().BaseImponible);
            Assert.AreEqual(-21.00M, request.DesgloseIva.Single().CuotaIva);
        }

        [TestMethod]
        public void Mapear_FacturaSerieNV_MapeaDatosGeneralesYTipoF1()
        {
            var factura = CrearFacturaNV();

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            Assert.AreEqual("NV", request.Serie);
            Assert.AreEqual("2600123", request.Numero); // sin el prefijo de la serie
            Assert.AreEqual(new DateTime(2026, 7, 17), request.FechaExpedicion);
            Assert.AreEqual("F1", request.TipoFactura);
            Assert.IsFalse(string.IsNullOrWhiteSpace(request.Descripcion));
            Assert.AreEqual("12345678Z", request.NifDestinatario);
            Assert.AreEqual("CLIENTE DE PRUEBA SL", request.NombreDestinatario);
        }

        [TestMethod]
        public void Mapear_AgrupaLasLineasPorTipoDeIva()
        {
            var factura = CrearFacturaNV();
            factura.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 10.00M, ImporteIVA = 2.10M },
                new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 20.00M, ImporteIVA = 4.20M },
                new LinPedidoVta { PorcentajeIVA = 10, PorcentajeRE = 0, Base_Imponible = 50.00M, ImporteIVA = 5.00M }
            };

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            Assert.AreEqual(2, request.DesgloseIva.Count);
            var desglose21 = request.DesgloseIva.Single(d => d.TipoIva == 21);
            Assert.AreEqual(30.00M, desglose21.BaseImponible);
            Assert.AreEqual(6.30M, desglose21.CuotaIva);
            var desglose10 = request.DesgloseIva.Single(d => d.TipoIva == 10);
            Assert.AreEqual(50.00M, desglose10.BaseImponible);
            Assert.AreEqual(5.00M, desglose10.CuotaIva);
            Assert.AreEqual(91.30M, request.ImporteTotal);
        }

        [TestMethod]
        public void Mapear_ConRecargoDeEquivalencia_InformaTipoYCuota()
        {
            var factura = CrearFacturaNV();
            // PorcentajeRE viene de BD como fracción (0.052 = 5,2%)
            factura.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0.052M, Base_Imponible = 100.00M, ImporteIVA = 21.00M }
            };

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            var desglose = request.DesgloseIva.Single();
            Assert.AreEqual(5.2M, desglose.TipoRecargoEquivalencia);
            Assert.AreEqual(5.20M, desglose.CuotaRecargoEquivalencia);
            Assert.AreEqual(126.20M, request.ImporteTotal);
        }

        [TestMethod]
        public void Mapear_CuotasSeRedondeanAwayFromZero()
        {
            var factura = CrearFacturaNV();
            // Suma de cuotas = 2.105 → AwayFromZero da 2.11 (ToEven daría 2.10)
            factura.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 5.01M, ImporteIVA = 1.0525M },
                new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 5.01M, ImporteIVA = 1.0525M }
            };

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            Assert.AreEqual(2.11M, request.DesgloseIva.Single().CuotaIva);
        }

        [TestMethod]
        public void Mapear_SerieNoRegistradaEnVerifactu_Lanza()
        {
            var factura = CrearFacturaNV();
            factura.Serie = "GB";

            _ = Assert.ThrowsException<InvalidOperationException>(() => MapeadorFacturaVerifactu.Mapear(factura));
        }
    }
}
