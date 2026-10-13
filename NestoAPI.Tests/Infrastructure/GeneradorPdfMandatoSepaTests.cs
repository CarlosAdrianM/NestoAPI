using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Nesto#490 / Nesto#340 (RDLC → QuestPDF): mandato SEPA (orden de domiciliación CORE).
    /// Comprueban el mapeo de campos del RDLC MandatoCoreSEPA.rdlc (etiquetas, valores, la fecha-
    /// localidad «dd-MM-yyyy - Población», el IBAN formateado), que sale un PDF válido sin ir a
    /// internet a por el logo y que el motor solo cambia con «QuestPDF».
    /// </summary>
    [TestClass]
    public class GeneradorPdfMandatoSepaTests
    {
        private static Mandato CrearMandato() => new Mandato
        {
            Referencia = "15191/0/1",
            IdentificadorAcreedor = "ES12000B12345678",
            NombreAcreedor = "NUEVA VISION, S.A.",
            DireccionAcreedor = "C/ INDUSTRIA 1",
            CodigoPostalAcreedor = "28110",
            PoblacionAcreedor = "ALGETE",
            ProvinciaAcreedor = "MADRID",
            PaisAcreedor = "ESPAÑA",
            NombreDeudor = "CLIENTE DE PRUEBA SL",
            DireccionDeudor = "C/ MAYOR 1",
            CodigoPostalDeudor = "28001",
            PoblacionDeudor = "MADRID",
            ProvinciaDeudor = "MADRID",
            PaisDeudor = "ESPAÑA",
            SwiftBic = "CAIXESBBXXX",
            Iban = new Iban("ES9121000418450200051332"),
            PersonaFirmante = "JUAN PÉREZ"
        };

        [TestMethod]
        public void FilasAcreedor_MapeanLosCamposDelRdlcEnSuOrden()
        {
            var filas = GeneradorPdfMandatoSepa.FilasAcreedor(CrearMandato());

            CollectionAssert.AreEqual(
                new[] { "Mandate reference", "Creditor Identifier", "Creditor's name", "Address", "Postal Code - City - Town", "Country" },
                filas.Select(f => f.Traduccion).ToArray());
            Assert.AreEqual("15191/0/1", filas[0].Valor);
            Assert.AreEqual("ES12000B12345678", filas[1].Valor);
            Assert.AreEqual("NUEVA VISION, S.A.", filas[2].Valor);
            Assert.AreEqual("28110 ALGETE (MADRID)", filas[4].Valor, "Como PoblacionCompletaAcreedor del RDLC");
            Assert.IsTrue(filas.All(f => f.DosPuntos));
        }

        [TestMethod]
        public void FilasDeudor_FechaLocalidadIbanYNotaDelTitular()
        {
            var filas = GeneradorPdfMandatoSepa.FilasDeudor(CrearMandato(), new DateTime(2026, 10, 13));

            var nombre = filas.Single(f => f.Etiqueta.StartsWith("Nombre del deudor"));
            Assert.AreEqual("CLIENTE DE PRUEBA SL", nombre.Valor);
            Assert.AreEqual("(titular/es de la cuenta de cargo)", nombre.Nota);

            Assert.AreEqual("28001 MADRID (MADRID)", filas.Single(f => f.Etiqueta.StartsWith("Código postal")).Valor);
            Assert.AreEqual("CAIXESBBXXX", filas.Single(f => f.Etiqueta.StartsWith("Swift")).Valor);
            Assert.AreEqual(new Iban("ES9121000418450200051332").Formateado,
                filas.Single(f => f.Traduccion == "Account number - IBAN").Valor, "El RDLC pinta Iban.ToString() (formateado)");
            Assert.AreEqual("13-10-2026 - MADRID", filas.Single(f => f.Etiqueta.StartsWith("Fecha - Localidad")).Valor,
                "El RDLC: Today.ToString(\"dd-MM-yyyy\") & \" - \" & PoblacionDeudor");
            Assert.AreEqual("JUAN PÉREZ", filas.Single(f => f.Traduccion == "Signer's name").Valor);
        }

        [TestMethod]
        public void FilasDeudor_SinIban_NoRevienta()
        {
            var mandato = CrearMandato();
            mandato.Iban = null;

            var filas = GeneradorPdfMandatoSepa.FilasDeudor(mandato, DateTime.Today);

            Assert.IsNull(filas.Single(f => f.Traduccion == "Account number - IBAN").Valor);
        }

        [TestMethod]
        public void GenerarPdf_ConMandato_DevuelvePdfValidoAunqueNoHayaLogo()
        {
            var generador = new GeneradorPdfMandatoSepa(() => throw new Exception("sin red"));

            var resultado = generador.GenerarPdf(new List<Mandato> { CrearMandato() }, new DateTime(2026, 10, 13));

            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 4);
            Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }

        [TestMethod]
        public void GenerarPdf_CamposNulos_DevuelvePdfValido()
        {
            var generador = new GeneradorPdfMandatoSepa(() => null);

            var resultado = generador.GenerarPdf(new List<Mandato> { new Mandato() }, DateTime.Today);

            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerarPdf_SinMandatos_LanzaArgumentException()
        {
            new GeneradorPdfMandatoSepa(() => null).GenerarPdf(new List<Mandato>(), DateTime.Today);
        }

        [TestMethod]
        public void EsMotorQuestPdf_SoloConQuestPdf()
        {
            Assert.IsTrue(GestorClientes.EsMotorQuestPdf("QuestPDF"));
            Assert.IsTrue(GestorClientes.EsMotorQuestPdf(" questpdf "));
            Assert.IsFalse(GestorClientes.EsMotorQuestPdf(null));
            Assert.IsFalse(GestorClientes.EsMotorQuestPdf(""));
            Assert.IsFalse(GestorClientes.EsMotorQuestPdf("RDLC"));
        }
    }
}
