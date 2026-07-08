using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del "EXTRACTO CONTABLE" (migración RDLC -> backend, Nesto#340 Fase 2).
    /// Verifican que se produce un PDF válido (firma %PDF) con movimientos, con lista vacía y con null.
    /// La carga del logo se inyecta a null para no depender de la red.
    /// </summary>
    [TestClass]
    public class GeneradorPdfExtractoContableTests
    {
        private GeneradorPdfExtractoContable _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfExtractoContable(() => null);
        }

        [TestMethod]
        public void GenerarPdf_ConMovimientos_DevuelvePdfValido()
        {
            var movimientos = new List<ExtractoContableDTO>
            {
                new ExtractoContableDTO
                {
                    Fecha = new DateTime(2026, 6, 1), Documento = "FV26/001",
                    Concepto = "Factura de venta", Debe = 121.00m, Haber = 0m, Saldo = 121.00m
                },
                new ExtractoContableDTO
                {
                    Fecha = new DateTime(2026, 6, 15), Documento = "COBRO",
                    Concepto = "Cobro transferencia", Debe = 0m, Haber = 121.00m, Saldo = 0m
                }
            };

            var resultado = _generador.GenerarPdf("430000001", new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), movimientos);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaVacia_DevuelvePdfValido()
        {
            var resultado = _generador.GenerarPdf("430000001", new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new List<ExtractoContableDTO>());

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "Una lista vacía también debe generar un PDF válido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_MovimientosNull_NoLanzaYDevuelvePdf()
        {
            var resultado = _generador.GenerarPdf("430000001", new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), null);

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
