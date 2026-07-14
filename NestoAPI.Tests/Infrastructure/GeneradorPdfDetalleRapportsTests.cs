using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del informe de detalle de rapports (migración RDLC -> backend,
    /// Nesto#340). Verifican que se produce un PDF válido (cabecera %PDF) con datos de varios
    /// usuarios, con lista vacía y con null, y con los campos opcionales a null (estilos
    /// condicionales: sin vendedor, sin gestionar, sin dirección).
    /// </summary>
    [TestClass]
    public class GeneradorPdfDetalleRapportsTests
    {
        private static readonly DateTime FechaDesde = new DateTime(2026, 7, 1);
        private static readonly DateTime FechaHasta = new DateTime(2026, 7, 14);

        private GeneradorPdfDetalleRapports _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfDetalleRapports();
        }

        [TestMethod]
        public void GenerarPdf_ConVariosUsuarios_DevuelvePdfValido()
        {
            var rapports = new List<DetalleRapportsDTO>
            {
                new DetalleRapportsDTO
                {
                    Usuario = "Carolina", Cliente = "15191   ", Direccion = "C/ Mayor, 1",
                    CodigoPostal = "28001", Poblacion = "Madrid", Tipo = "V ", EstadoCliente = 0,
                    Comentarios = "Visita comercial", Pedido = true, Vendedor = "CAR",
                    HoraLlamada = new DateTime(2026, 7, 10, 10, 30, 0), EstadoRapport = 0
                },
                new DetalleRapportsDTO
                {
                    Usuario = "Carolina", Cliente = "22222", Direccion = "C/ Menor, 2",
                    CodigoPostal = "28002", Poblacion = "Madrid", Tipo = "T", EstadoCliente = 1,
                    Comentarios = "Llamada de seguimiento", Pedido = false, Vendedor = "CAR",
                    HoraLlamada = new DateTime(2026, 7, 10, 12, 0, 0), EstadoRapport = 0
                },
                new DetalleRapportsDTO
                {
                    Usuario = "Sara", Cliente = "33333", Direccion = "Av. Sol, 3",
                    CodigoPostal = "28003", Poblacion = "Alcobendas", Tipo = "W", EstadoCliente = 2,
                    Comentarios = "WhatsApp con pedido", Pedido = true, Vendedor = "SAR",
                    HoraLlamada = new DateTime(2026, 7, 11, 9, 15, 0), EstadoRapport = 0
                }
            };

            var resultado = _generador.GenerarPdf(FechaDesde, FechaHasta, rapports);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ConCamposOpcionalesNull_NoLanzaYDevuelvePdf()
        {
            // Sin vendedor (rojo en el RDLC), sin gestionar (cursiva), sin dirección ni hora.
            var rapports = new List<DetalleRapportsDTO>
            {
                new DetalleRapportsDTO
                {
                    Usuario = "Carolina", Cliente = "44444", Direccion = null,
                    CodigoPostal = null, Poblacion = null, Tipo = null, EstadoCliente = null,
                    Comentarios = null, Pedido = null, Vendedor = null,
                    HoraLlamada = null, EstadoRapport = 1
                }
            };

            var resultado = _generador.GenerarPdf(FechaDesde, FechaHasta, rapports);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaVacia_DevuelvePdfValido()
        {
            var resultado = _generador.GenerarPdf(FechaDesde, FechaHasta, new List<DetalleRapportsDTO>());

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "Una lista vacía también debe generar un PDF válido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaNull_NoLanzaYDevuelvePdf()
        {
            var resultado = _generador.GenerarPdf(FechaDesde, FechaHasta, null);

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
