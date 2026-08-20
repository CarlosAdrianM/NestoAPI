using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Nesto#340 (Fase 2, RDLC→QuestPDF): etiquetas de precio de la tienda. La composición
    /// (huecos por hoja empezada, referencia codificada, PVP público) es la misma lógica que
    /// tenía FilaEtiquetasModel en el cliente; el layout replica el RDLC (papel precortado,
    /// se valida contra el papel real con el flag MotorPdfEtiquetasTienda antes de extender).
    /// </summary>
    [TestClass]
    public class GeneradorPdfEtiquetasTiendaTests
    {
        private static EtiquetasTiendaDTO Producto(string id = "31001", string nombre = "CHAMPU ALMENDRAS",
            short tamanno = 500, string unidadMedida = "ml", decimal precio = 4.60M, string familia = "Valquer")
        {
            return new EtiquetasTiendaDTO
            {
                ProductoId = id,
                Nombre = nombre,
                Tamanno = tamanno,
                UnidadMedida = unidadMedida,
                PrecioProfesional = precio,
                Familia = familia
            };
        }

        [TestMethod]
        public void Componer_EtiquetaPrimera_DejaHuecosPorLaHojaEmpezada()
        {
            // etiquetaPrimera = 5: las 4 primeras posiciones de la hoja ya están gastadas
            var posiciones = GeneradorPdfEtiquetasTienda.Componer(
                new List<EtiquetasTiendaDTO> { Producto("31001"), Producto("31002") }, etiquetaPrimera: 5);

            Assert.AreEqual(6, posiciones.Count);
            Assert.IsTrue(posiciones.Take(4).All(p => p == null), "Las posiciones gastadas son huecos");
            Assert.AreEqual("31001", posiciones[4].ProductoId);
            Assert.AreEqual("31002", posiciones[5].ProductoId);
        }

        [TestMethod]
        public void Componer_SinHuecos_UnaEtiquetaPorProductoEnOrden()
        {
            var posiciones = GeneradorPdfEtiquetasTienda.Componer(
                new List<EtiquetasTiendaDTO> { Producto() }, etiquetaPrimera: 1);

            Assert.AreEqual(1, posiciones.Count);
            Assert.AreEqual("CHAMPU ALMENDRAS 500 ml", posiciones[0].NombreConTamanno);
            Assert.AreEqual("Valquer", posiciones[0].Familia);
        }

        [TestMethod]
        public void Componer_SinTamanno_ElNombreVaSolo()
        {
            var posiciones = GeneradorPdfEtiquetasTienda.Componer(
                new List<EtiquetasTiendaDTO> { Producto(tamanno: 0) }, etiquetaPrimera: 1);

            Assert.AreEqual("CHAMPU ALMENDRAS", posiciones[0].NombreConTamanno);
        }

        [TestMethod]
        public void CalcularPrecioPublico_FormulaDeTienda()
        {
            // x2 de margen, -35% de tienda, +21% de IVA (misma fórmula que el cliente RDLC)
            Assert.AreEqual(7.24M, GeneradorPdfEtiquetasTienda.CalcularPrecioPublico(4.60M));
            Assert.AreEqual(0M, GeneradorPdfEtiquetasTienda.CalcularPrecioPublico(0M));
        }

        [TestMethod]
        public void ComponerReferencia_IdMasPrecioEnSieteDigitos()
        {
            // 5 enteros + 2 decimales sin separador, mismo formato que leía la tienda
            Assert.AreEqual("310010000460", GeneradorPdfEtiquetasTienda.ComponerReferencia("31001", 4.60M));
            Assert.AreEqual("310010012345", GeneradorPdfEtiquetasTienda.ComponerReferencia("31001", 123.45M));
            Assert.AreEqual("310010000000", GeneradorPdfEtiquetasTienda.ComponerReferencia("31001", 0M));
        }

        [TestMethod]
        public async Task GenerarPdf_ConProductosYHuecos_DevuelveUnPdf()
        {
            // El resolutor de URL se inyecta para no llamar al PHP de la tienda en tests
            var generador = new GeneradorPdfEtiquetasTienda(p => Task.FromResult("https://tienda/p/" + p));

            var contenido = await generador.GenerarPdf(
                new List<EtiquetasTiendaDTO> { Producto("31001"), Producto("31002"), Producto("31003", precio: 0M) },
                etiquetaPrimera: 3);
            byte[] pdf = await contenido.ReadAsByteArrayAsync();

            Assert.IsTrue(pdf.Length > 1000, "El PDF debe tener contenido");
            Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [TestMethod]
        public async Task GenerarPdf_SinUrlDeTienda_SeGeneraIgualSinQr()
        {
            // El PHP de la tienda es best-effort: si no responde, la etiqueta sale sin QR
            var generador = new GeneradorPdfEtiquetasTienda(p => Task.FromResult<string>(null));

            var contenido = await generador.GenerarPdf(
                new List<EtiquetasTiendaDTO> { Producto() }, etiquetaPrimera: 1);
            byte[] pdf = await contenido.ReadAsByteArrayAsync();

            Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }
}
