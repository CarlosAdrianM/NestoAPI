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

        // Regresión (Carlos 10/11/26): el PDF que sustituyó al RDLC era una tabla corrida con pasillo/fila/columna en columnas
        // y no valía para contar. Tiene que ser una hoja por ubicación, con la ubicación en el rótulo, en el orden del RDLC
        // (pasillo, columna, fila; dentro, producto) y sin que el relleno de los char parta una ubicación en dos.
        [TestMethod]
        public void Agrupar_UnaHojaPorUbicacion_EnElOrdenDelRdlc()
        {
            var lineas = new List<UbicacionesInventarioDTO>
            {
                new UbicacionesInventarioDTO { Pasillo = "02", Fila = "1", Columna = "01", Producto = "30000" },
                new UbicacionesInventarioDTO { Pasillo = "01", Fila = "2", Columna = "01", Producto = "20000" },
                new UbicacionesInventarioDTO { Pasillo = "01", Fila = "1", Columna = "02", Producto = "10000" },
                new UbicacionesInventarioDTO { Pasillo = "01 ", Fila = "1 ", Columna = "01 ", Producto = "40000  " },
                new UbicacionesInventarioDTO { Pasillo = "01", Fila = "1", Columna = "01", Producto = "12345" }
            };

            List<HojaUbicacionInventario> hojas = GeneradorPdfUbicacionesInventario.Agrupar(lineas);

            Assert.AreEqual(4, hojas.Count, "Una hoja por pasillo, fila y columna (el relleno no cuenta)");
            Assert.AreEqual("Pasillo 01. Fila 1. Columna 01", hojas[0].Titulo);
            Assert.AreEqual("Pasillo 01. Fila 2. Columna 01", hojas[1].Titulo, "Dentro del pasillo, primero la columna y luego la fila");
            Assert.AreEqual("Pasillo 01. Fila 1. Columna 02", hojas[2].Titulo);
            Assert.AreEqual("Pasillo 02. Fila 1. Columna 01", hojas[3].Titulo);
            Assert.AreEqual(2, hojas[0].Lineas.Count);
            Assert.AreEqual("12345", hojas[0].Lineas[0].Producto.Trim(), "Dentro de la hoja, por producto");
            Assert.AreEqual("40000", hojas[0].Lineas[1].Producto.Trim());
        }

        // Una hoja por ubicación pasa de las 250 páginas con las que QuestPDF corta por defecto (Algete: unas 700).
        [TestMethod]
        public void GenerarPdf_MasDe250Ubicaciones_NoCorta()
        {
            var lineas = new List<UbicacionesInventarioDTO>();
            for (int i = 0; i < 300; i++)
            {
                lineas.Add(new UbicacionesInventarioDTO { Pasillo = "01", Fila = "1", Columna = i.ToString("000"), Producto = "12345", Nombre = "Producto" });
            }

            byte[] bytes = _generador.GenerarPdf(lineas).ReadAsByteArrayAsync().Result;

            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void Agrupar_NullOVacia_SinHojas()
        {
            Assert.AreEqual(0, GeneradorPdfUbicacionesInventario.Agrupar(null).Count);
            Assert.AreEqual(0, GeneradorPdfUbicacionesInventario.Agrupar(new List<UbicacionesInventarioDTO>()).Count);
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
