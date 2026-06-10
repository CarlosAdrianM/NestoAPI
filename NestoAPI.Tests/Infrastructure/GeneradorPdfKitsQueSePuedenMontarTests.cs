using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del informe "Kits que se pueden montar o desmontar"
    /// (migración RDLC -> backend). Verifican que se produce un PDF válido (cabecera %PDF)
    /// con datos (montar y desmontar), con lista vacía y con lista null.
    /// </summary>
    [TestClass]
    public class GeneradorPdfKitsQueSePuedenMontarTests
    {
        private GeneradorPdfKitsQueSePuedenMontar _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfKitsQueSePuedenMontar();
        }

        [TestMethod]
        public void GenerarPdf_ConKitsMontarYDesmontar_DevuelvePdfValido()
        {
            var kits = new List<KitsQueSePuedenMontarDTO>
            {
                new KitsQueSePuedenMontarDTO { Tipo = "m", Kit = "30001", Nombre = "Pack regalo", CantidadAMontar = 5, CodigoBarras = "8436566600001" },
                new KitsQueSePuedenMontarDTO { Tipo = "d", Kit = "30002", Nombre = "Lote navidad", CantidadAMontar = 2, CodigoBarras = "8436566600002" }
            };

            var resultado = _generador.GenerarPdf(kits);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaVacia_DevuelvePdfValido()
        {
            var resultado = _generador.GenerarPdf(new List<KitsQueSePuedenMontarDTO>());

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
