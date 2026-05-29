using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del informe Control de pedidos (migración RDLC -> backend).
    /// Verifican que se produce un PDF válido (cabecera %PDF) tanto con datos como sin ellos.
    /// </summary>
    [TestClass]
    public class GeneradorPdfControlPedidosTests
    {
        private GeneradorPdfControlPedidos _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfControlPedidos();
        }

        [TestMethod]
        public void GenerarPdf_ConLineas_DevuelvePdfValido()
        {
            var lineas = new List<ControlPedidosDTO>
            {
                new ControlPedidosDTO
                {
                    Pedido = 12345, Producto = "38697", Ruta = "MAD", Cliente = "15191",
                    Vendedor = "AM", Nombre = "Crema Hidratante", Familia = "Eva Visnú",
                    CantidadPedido = 2, CantidadTotal = 5
                },
                new ControlPedidosDTO
                {
                    Pedido = 12346, Producto = "12345", Ruta = "BCN", Cliente = "20001",
                    Vendedor = "JG", Nombre = "Champú", Familia = "Lisap",
                    CantidadPedido = 1, CantidadTotal = 1
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
            var resultado = _generador.GenerarPdf(new List<ControlPedidosDTO>());

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
