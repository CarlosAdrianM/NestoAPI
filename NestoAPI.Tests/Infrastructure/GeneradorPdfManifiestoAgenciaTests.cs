using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del Manifiesto de agencia (migración RDLC -> backend,
    /// Nesto#340). Verifican que se produce un PDF válido (cabecera %PDF) con datos, lista
    /// vacía y null (día sin envíos tramitados).
    /// </summary>
    [TestClass]
    public class GeneradorPdfManifiestoAgenciaTests
    {
        private GeneradorPdfManifiestoAgencia _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfManifiestoAgencia();
        }

        [TestMethod]
        public void GenerarPdf_ConEnvios_DevuelvePdfValido()
        {
            var envios = new List<ManifiestoAgenciaDTO>
            {
                new ManifiestoAgenciaDTO
                {
                    Cliente = "15191", Contacto = "0", Nombre = "CLIENTE DE PRUEBA SL",
                    Direccion = "C/ MAYOR 1", CodigoPostal = "28001", Poblacion = "MADRID",
                    Provincia = "MADRID", Bultos = 3, Reembolso = 125.50M,
                    TelefonoFijo = "915555555", TelefonoMovil = "615555555",
                    Observaciones = "Entregar por la mañana"
                },
                new ManifiestoAgenciaDTO
                {
                    Cliente = "26985", Contacto = "0", Nombre = "OTRO CLIENTE",
                    Direccion = null, CodigoPostal = null, Poblacion = null,
                    Provincia = null, Bultos = 1, Reembolso = 0,
                    TelefonoFijo = null, TelefonoMovil = null, Observaciones = null
                }
            };

            var resultado = _generador.GenerarPdf(envios, "GLS", new DateTime(2026, 7, 22));

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        // ---- Sugerencia 1257 (Alfredo, 13/11/26): el reparto propio, un manifiesto por ruta

        private static ManifiestoAgenciaDTO Envio(string nombre, string ruta, short bultos = 1)
            => new ManifiestoAgenciaDTO { Nombre = nombre, Ruta = ruta, Bultos = bultos };

        [TestMethod]
        public void Secciones_SinSeparar_UnaSolaConTodo()
        {
            var envios = new List<ManifiestoAgenciaDTO> { Envio("A", "AT"), Envio("B", "16") };

            var secciones = GeneradorPdfManifiestoAgencia.Secciones(envios, separarPorRuta: false);

            Assert.AreEqual(1, secciones.Count);
            Assert.IsNull(secciones[0].Key);
            Assert.AreEqual(2, secciones[0].Value.Count);
        }

        [TestMethod]
        public void Secciones_SeparandoConDosRutas_UnaPorRutaOrdenadasYSinRutaAlFinal()
        {
            var envios = new List<ManifiestoAgenciaDTO> { Envio("A", "AT "), Envio("B", "16"), Envio("C", "at"), Envio("D", null) };

            var secciones = GeneradorPdfManifiestoAgencia.Secciones(envios, separarPorRuta: true);

            CollectionAssert.AreEqual(new[] { "16", "AT", GeneradorPdfManifiestoAgencia.SIN_RUTA }, secciones.ConvertAll(s => s.Key));
            Assert.AreEqual(1, secciones[0].Value.Count);
            Assert.AreEqual(2, secciones[1].Value.Count, "«AT » y «at» son la misma ruta");
            Assert.AreEqual(1, secciones[2].Value.Count);
        }

        [TestMethod]
        public void Secciones_SeparandoConUnaSolaRuta_NoPoneTituloDeRuta()
        {
            var envios = new List<ManifiestoAgenciaDTO> { Envio("A", "AT"), Envio("B", "AT") };

            var secciones = GeneradorPdfManifiestoAgencia.Secciones(envios, separarPorRuta: true);

            Assert.AreEqual(1, secciones.Count);
            Assert.IsNull(secciones[0].Key);
        }

        [TestMethod]
        public void Secciones_ListaNullOVacia_UnaSeccionVacia()
        {
            Assert.AreEqual(0, GeneradorPdfManifiestoAgencia.Secciones(null, true)[0].Value.Count);
            Assert.AreEqual(1, GeneradorPdfManifiestoAgencia.Secciones(new List<ManifiestoAgenciaDTO>(), true).Count);
        }

        [TestMethod]
        public void GenerarPdf_SeparadoPorRuta_DevuelvePdfValido()
        {
            var envios = new List<ManifiestoAgenciaDTO> { Envio("A", "AT", 2), Envio("B", "16", 3) };

            byte[] pdf = _generador.GenerarPdf(envios, "Reparto propio", new DateTime(2026, 11, 13), separarPorRuta: true)
                .ReadAsByteArrayAsync().Result;

            Assert.IsTrue(pdf.Length > 4);
            Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [TestMethod]
        public void GenerarPdf_SinEnvios_DevuelvePdfValido()
        {
            var resultado = _generador.GenerarPdf(new List<ManifiestoAgenciaDTO>(), "Innovatrans", DateTime.Today);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "Un día sin envíos también debe generar un PDF válido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaNullYAgenciaNull_NoLanzaYDevuelvePdf()
        {
            var resultado = _generador.GenerarPdf(null, null, DateTime.Today);

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
