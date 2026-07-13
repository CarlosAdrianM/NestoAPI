using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del packing list (migración RDLC -> backend, Nesto#340).
    /// Verifican PDF válido con varios pedidos, con líneas pendientes de servir, lista vacía
    /// y null.
    /// </summary>
    [TestClass]
    public class GeneradorPdfPackingTests
    {
        private GeneradorPdfPacking _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfPacking();
        }

        private static PackingDTO Linea(int pedido, string producto, string tipo = "Servir")
        {
            return new PackingDTO
            {
                Número = pedido,
                NºCliente = "15191",
                Contacto = "0",
                Direccion = "C/ Falsa 123",
                CodPostal = "28001",
                Poblacion = "Madrid",
                Telefono = "912345678",
                Ruta = "AT",
                Usuario = @"NUEVAVISION\Carlos",
                Aviso = "Aviso importante",
                Ampliacion = "Ampliación del pedido",
                ComentarioPicking = "Dejar en portería",
                ProveedorProducto = "612",
                NºProducto = producto,
                CodBarras = "8412345678901",
                Descripcion = "Producto de prueba",
                Tamaño = 50,
                UnidadMedida = "ml",
                NombreSubGrupo = "Cosmética",
                Cantidad = 2,
                CantidadCajas = 1,
                Estado = 1,
                Pasillo = "A",
                Fila = "1",
                Columna = "03",
                Tipo = tipo
            };
        }

        [TestMethod]
        public void GenerarPdf_VariosPedidos_DevuelvePdfValido()
        {
            var lineas = new List<PackingDTO>
            {
                Linea(900001, "38697"),
                Linea(900001, "12345"),
                Linea(900002, "45473")
            };

            var resultado = _generador.GenerarPdf(123456, lineas);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ConLineasPendientes_DevuelvePdfValido()
        {
            // Las filas con Tipo = "Pendientes" van en su propia sección dentro del pedido.
            var lineas = new List<PackingDTO>
            {
                Linea(900001, "38697"),
                Linea(900001, "99999", tipo: "Pendientes")
            };

            var resultado = _generador.GenerarPdf(123456, lineas);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaVacia_DevuelvePdfValido()
        {
            var resultado = _generador.GenerarPdf(123456, new List<PackingDTO>());

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "Una lista vacía también debe generar un PDF válido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaNull_NoLanzaYDevuelvePdf()
        {
            var resultado = _generador.GenerarPdf(123456, null);

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
