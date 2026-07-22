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

        #region Facturas simplificadas F2 (#325)

        [TestMethod]
        public void Mapear_FacturaDeVentasAmazon_EsF2SinDestinatario()
        {
            // #325: primer hallazgo de la fase en sombra. Las ventas a consumidor final se agrupan
            // en clientes ficticios con NIF que no existe en el censo ("NV"), y la AEAT rechazaba
            // las 22 facturas de Amazon del día. Son simplificadas: F2 y SIN destinatario.
            var factura = CrearFacturaNV();
            factura.Nº_Cliente = "32624"; // ClientesEspeciales.AMAZON
            factura.CifNif = "NV";
            factura.NombreFiscal = "FACT. SIMP. VENTAS AMAZON";

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            Assert.AreEqual("F2", request.TipoFactura);
            Assert.IsNull(request.NifDestinatario, "Una simplificada no lleva NIF de destinatario");
            Assert.IsNull(request.NombreDestinatario, "Una simplificada no lleva nombre de destinatario");
        }

        [TestMethod]
        public void Mapear_FacturaDeTiendaOnlineYPublicoFinal_TambienSonF2()
        {
            var tiendaOnline = CrearFacturaNV();
            tiendaOnline.Nº_Cliente = "31517 "; // con relleno, como viene de BD
            var publicoFinal = CrearFacturaNV();
            publicoFinal.Nº_Cliente = "10458";

            Assert.AreEqual("F2", MapeadorFacturaVerifactu.Mapear(tiendaOnline).TipoFactura);
            Assert.AreEqual("F2", MapeadorFacturaVerifactu.Mapear(publicoFinal).TipoFactura);
        }

        [TestMethod]
        public void Mapear_FacturaDeClienteNormal_SigueSiendoF1ConDestinatario()
        {
            // El caso masivo no cambia: las 33 facturas de clientes reales que ya aceptaba la AEAT
            var factura = CrearFacturaNV();
            factura.Nº_Cliente = "26985";

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            Assert.AreEqual("F1", request.TipoFactura);
            Assert.AreEqual("12345678Z", request.NifDestinatario);
            Assert.AreEqual("CLIENTE DE PRUEBA SL", request.NombreDestinatario);
        }

        [TestMethod]
        public void EsFacturaSimplificada_SinClienteONull_NoLanza()
        {
            Assert.IsFalse(MapeadorFacturaVerifactu.EsFacturaSimplificada(null));
            Assert.IsFalse(MapeadorFacturaVerifactu.EsFacturaSimplificada(new CabFacturaVta()));
        }

        #endregion

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

        #region Ventas OSS con IVA extranjero (#347)

        [TestMethod]
        public void Mapear_LineasConIvaExtranjero_VanComoOssNoSujetasSinTipoNiCuota()
        {
            // #347: caso real NV2612439 (simplificada de Amazon, cliente 32624) con IVA 22%
            // (tipo extranjero OSS de ParametrosIVA). La AEAT rechaza tipos no españoles con
            // impuesto=01: la venta OSS va como no sujeta por localización (N2, clave 17) y el
            // IVA extranjero NO se declara (se liquida por el modelo 369).
            var factura = CrearFacturaNV();
            factura.Nº_Cliente = "32624";
            factura.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { PorcentajeIVA = 22, PorcentajeRE = 0, Base_Imponible = 151.60M, ImporteIVA = 33.35M }
            };

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            var desglose = request.DesgloseIva.Single();
            Assert.AreEqual("17", desglose.ClaveRegimen);
            Assert.AreEqual("N2", desglose.CalificacionOperacion);
            Assert.AreEqual(151.60M, desglose.BaseImponible);
            Assert.AreEqual(0M, desglose.TipoIva, "Con N2 está prohibido informar tipo impositivo");
            Assert.AreEqual(0M, desglose.CuotaIva, "Con N2 está prohibido informar cuota");
            // Ejemplo oficial OSS de Verifacti: el importe_total va SIN la cuota extranjera
            Assert.AreEqual(151.60M, request.ImporteTotal);
        }

        [TestMethod]
        public void Mapear_FacturaMixta_SoloLasLineasConIvaExtranjeroVanComoOss()
        {
            var factura = CrearFacturaNV();
            factura.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 100.00M, ImporteIVA = 21.00M },
                new LinPedidoVta { PorcentajeIVA = 23, PorcentajeRE = 0, Base_Imponible = 50.00M, ImporteIVA = 11.50M }
            };

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            var espanola = request.DesgloseIva.Single(d => d.TipoIva == 21);
            Assert.IsNull(espanola.ClaveRegimen);
            Assert.IsNull(espanola.CalificacionOperacion);
            Assert.AreEqual(21.00M, espanola.CuotaIva);
            var oss = request.DesgloseIva.Single(d => d.CalificacionOperacion == "N2");
            Assert.AreEqual(50.00M, oss.BaseImponible);
            Assert.AreEqual("17", oss.ClaveRegimen);
            // Total = línea española con IVA (121) + base OSS sin cuota extranjera (50)
            Assert.AreEqual(171.00M, request.ImporteTotal);
        }

        [TestMethod]
        public void Mapear_CodigoIvaDePaisConTipoCoincidenteConElEspanol_TambienEsOss()
        {
            // Países Bajos, Bélgica o Chequia también tienen el 21%: el porcentaje NO distingue
            // una venta OSS. La señal es el código de IVA de la cabecera (B21 = Bélgica), que
            // CanalesExternos asigna por país de destino.
            var factura = CrearFacturaNV();
            factura.IVA = "B21";
            factura.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 100.00M, ImporteIVA = 21.00M }
            };

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            var desglose = request.DesgloseIva.Single();
            Assert.AreEqual("17", desglose.ClaveRegimen);
            Assert.AreEqual("N2", desglose.CalificacionOperacion);
            Assert.AreEqual(0M, desglose.CuotaIva);
            Assert.AreEqual(100.00M, request.ImporteTotal, "El IVA belga no viaja a la AEAT");
        }

        [TestMethod]
        public void Mapear_CodigoIvaNacional_NoEsOssAunqueLoParezcan()
        {
            // Los códigos nacionales (G21 general, R10 reducido, E52 recargo...) nunca son OSS
            var factura = CrearFacturaNV();
            factura.IVA = "G21";

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            Assert.IsNull(request.DesgloseIva.Single().CalificacionOperacion);
            Assert.AreEqual(21M, request.DesgloseIva.Single().TipoIva);
        }

        [TestMethod]
        public void Mapear_TiposEspanolesVigentes_NingunoSeMarcaComoOss()
        {
            // La lista blanca de tipos españoles es la que valida la propia AEAT: 0, 2, 4, 5, 7.5, 10 y 21
            // (PorcentajeIVA es byte en LinPedidoVta, así que el 7.5 no puede darse ahí)
            var factura = CrearFacturaNV();
            factura.LinPedidoVtas = new byte[] { 0, 2, 4, 5, 10, 21 }
                .Select(tipo => new LinPedidoVta { PorcentajeIVA = tipo, PorcentajeRE = 0, Base_Imponible = 10M, ImporteIVA = tipo / 10M })
                .ToList();

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            Assert.IsFalse(request.DesgloseIva.Any(d => d.CalificacionOperacion != null),
                "Ningún tipo español puede acabar marcado como OSS");
        }

        #endregion
    }
}
