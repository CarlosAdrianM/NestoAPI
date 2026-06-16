using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del informe Ubicaciones de inventario (migración RDLC -> backend).
    /// Verifican que se produce un PDF válido (cabecera %PDF) con datos, lista vacía y null.
    /// </summary>
    [TestClass]
    public class GeneradorPdfUbicacionesInventarioTests
    {
        private GeneradorPdfUbicacionesInventario _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfUbicacionesInventario();
        }

        [TestMethod]
        public void GenerarPdf_ConLineas_DevuelvePdfValido()
        {
            var lineas = new List<UbicacionesInventarioDTO>
            {
                new UbicacionesInventarioDTO
                {
                    Pasillo = "A", Fila = "1", Columna = "03", Producto = "38697",
                    CodigoBarras = "8412345678901", Nombre = "Crema Hidratante",
                    Tamanno = 50, UnidadMedida = "ml", Familia = "Eva Visnú"
                },
                new UbicacionesInventarioDTO
                {
                    Pasillo = "B", Fila = "2", Columna = "10", Producto = "12345",
                    CodigoBarras = null, Nombre = "Champú", Tamanno = null,
                    UnidadMedida = "ud", Familia = "Lisap"
                }
            };

            var resultado = _generador.GenerarPdf(lineas);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaVacia_DevuelvePdfValido()
        {
            var resultado = _generador.GenerarPdf(new List<UbicacionesInventarioDTO>());

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "Una lista vacía también debe generar un PDF válido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaNull_NoLanzaYDevuelvePdf()
        {
            var resultado = _generador.GenerarPdf(null);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        // Los PDF empiezan por la firma "%PDF" (0x25 0x50 0x44 0x46).
        private static void ComprobarCabeceraPdf(byte[] bytes)
        {
            Assert.IsTrue(bytes.Length >= 4, "El PDF es demasiado corto");
            Assert.AreEqual(0x25, bytes[0]);
            Assert.AreEqual(0x50, bytes[1]);
            Assert.AreEqual(0x44, bytes[2]);
            Assert.AreEqual(0x46, bytes[3]);
        }
    }
}
