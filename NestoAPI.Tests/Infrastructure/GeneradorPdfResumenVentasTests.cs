using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del informe Resumen de ventas migrado a QuestPDF: la transformación a vista comparativa
    /// (Año Actual vs. Año Anterior) y que el generador produce un PDF válido.
    /// </summary>
    [TestClass]
    public class GeneradorPdfResumenVentasTests
    {
        // ----- Transformación de datos (función pura) -----

        [TestMethod]
        public void Comparar_PlieganVisnuYUnionLaserEnAnnoActual_YAnteriorEsCursosVision()
        {
            var origen = new ResumenVentasDTO
            {
                Grupo = "NV", Vendedor = "AM", NombreVendedor = "Ana",
                VtaNV = 100m,   // Año Actual base
                VtaVC = 10m,    // Visnú (se pliega en Año Actual)
                VtaUL = 5m,     // Unión Láser (se pliega en Año Actual)
                VtaCV = 80m,    // Año Anterior
                VtaTotal = 195m // Diferencia (€) = VtaTotal tal cual
            };

            var resultado = GeneradorPdfResumenVentas.Comparar(origen);

            Assert.AreEqual(115m, resultado.AnnoActual, "Año Actual = VtaNV + VtaVC + VtaUL");
            Assert.AreEqual(80m, resultado.AnnoAnterior, "Año Anterior = VtaCV");
            Assert.AreEqual(195m, resultado.DiferenciaEuros, "Diferencia (€) = VtaTotal sin tocar");
            Assert.AreEqual(115m / 80m - 1m, resultado.DiferenciaPorcentaje);
        }

        [TestMethod]
        public void CalcularDiferenciaPorcentaje_ConAnteriorPositivo_DevuelveRatio()
        {
            Assert.AreEqual(0.2m, GeneradorPdfResumenVentas.CalcularDiferenciaPorcentaje(120m, 100m));
        }

        [TestMethod]
        public void CalcularDiferenciaPorcentaje_AnteriorCeroYActualPositivo_Devuelve100PorCiento()
        {
            Assert.AreEqual(1m, GeneradorPdfResumenVentas.CalcularDiferenciaPorcentaje(50m, 0m));
        }

        [TestMethod]
        public void CalcularDiferenciaPorcentaje_AnteriorCeroYActualCero_DevuelveCero()
        {
            Assert.AreEqual(0m, GeneradorPdfResumenVentas.CalcularDiferenciaPorcentaje(0m, 0m));
        }

        [TestMethod]
        public void CalcularDiferenciaPorcentaje_AnteriorCeroYActualNegativo_DevuelveMenos100PorCiento()
        {
            Assert.AreEqual(-1m, GeneradorPdfResumenVentas.CalcularDiferenciaPorcentaje(-30m, 0m));
        }

        [TestMethod]
        public void Transformar_ListaNull_DevuelveListaVacia()
        {
            var resultado = GeneradorPdfResumenVentas.Transformar(null);

            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.Count);
        }

        // ----- Etiqueta de grupo -----

        [TestMethod]
        public void NombreGrupo_MapeaFacYAlb_YDejaElRestoIgual()
        {
            Assert.AreEqual("Facturas", GeneradorPdfResumenVentas.NombreGrupo("FAC"));
            Assert.AreEqual("Albaranes", GeneradorPdfResumenVentas.NombreGrupo("ALB"));
            Assert.AreEqual("Facturas", GeneradorPdfResumenVentas.NombreGrupo(" fac "), "Debe ignorar espacios y mayúsculas");
            Assert.AreEqual("OTRO", GeneradorPdfResumenVentas.NombreGrupo("OTRO"));
            Assert.AreEqual("", GeneradorPdfResumenVentas.NombreGrupo(null));
        }

        // ----- Marcado en rojo (solo facturas y vende menos que el año anterior) -----

        [TestMethod]
        public void DebeMarcarseEnRojo_SoloFacturasYVendeMenos_EsVerdadero()
        {
            Assert.IsTrue(GeneradorPdfResumenVentas.DebeMarcarseEnRojo(true, 50m, 100m));
        }

        [TestMethod]
        public void DebeMarcarseEnRojo_SoloFacturasYVendeMas_EsFalso()
        {
            Assert.IsFalse(GeneradorPdfResumenVentas.DebeMarcarseEnRojo(true, 120m, 100m));
        }

        [TestMethod]
        public void DebeMarcarseEnRojo_SoloFacturasYVendeIgual_EsFalso()
        {
            Assert.IsFalse(GeneradorPdfResumenVentas.DebeMarcarseEnRojo(true, 100m, 100m));
        }

        [TestMethod]
        public void DebeMarcarseEnRojo_NoEsSoloFacturas_EsFalsoAunqueVendaMenos()
        {
            Assert.IsFalse(GeneradorPdfResumenVentas.DebeMarcarseEnRojo(false, 50m, 100m));
        }

        // ----- Generación del PDF -----

        [TestMethod]
        public void GenerarPdf_ConDatos_DevuelvePdfValido()
        {
            var datos = new List<ResumenVentasDTO>
            {
                new ResumenVentasDTO { Grupo = "NV", Vendedor = "AM", NombreVendedor = "Ana", VtaNV = 1500m, VtaCV = 1200m, VtaTotal = 2700m },
                new ResumenVentasDTO { Grupo = "NV", Vendedor = "JG", NombreVendedor = "Juan", VtaNV = 800m, VtaCV = 0m, VtaTotal = 800m },
                new ResumenVentasDTO { Grupo = "CV", Vendedor = "MR", NombreVendedor = "María", VtaNV = 500m, VtaCV = 600m, VtaTotal = 1100m }
            };

            var resultado = new GeneradorPdfResumenVentas()
                .GenerarPdf(datos, new DateTime(2026, 1, 1), new DateTime(2026, 3, 31), true);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaVacia_DevuelvePdfValido()
        {
            var resultado = new GeneradorPdfResumenVentas()
                .GenerarPdf(new List<ResumenVentasDTO>(), new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), false);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        private static void ComprobarCabeceraPdf(byte[] bytes)
        {
            Assert.IsTrue(bytes.Length >= 4, "El PDF es demasiado corto");
            Assert.AreEqual(0x25, bytes[0]); // %
            Assert.AreEqual(0x50, bytes[1]); // P
            Assert.AreEqual(0x44, bytes[2]); // D
            Assert.AreEqual(0x46, bytes[3]); // F
        }
    }
}
