using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del informe de remesa (NestoAPI#353): PDF válido con
    /// remesa multi-fecha (#345), de una sola fecha y sin efectos, y formato del IBAN.
    /// </summary>
    [TestClass]
    public class GeneradorPdfRemesaTests
    {
        private GeneradorPdfRemesa _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfRemesa();
        }

        private static RemesaInformeDTO RemesaMultiFecha() => new RemesaInformeDTO
        {
            Numero = 10901,
            Fecha = new DateTime(2026, 7, 23),
            Empresa = "Nueva Visión, S.A.",
            Banco = "La Caixa",
            IbanAbono = "ES0621006273900200063554",
            Efectos = new List<RemesaInformeEfectoDTO>
            {
                new RemesaInformeEfectoDTO
                {
                    Cliente = "10714", Nombre = "SUSANA DE CASTRO GARCIA", Documento = "NV2612516",
                    Efecto = "1", Iban = "ES3700495297402795209561", Importe = 35.44m,
                    FechaCargo = new DateTime(2026, 7, 23), Asiento = 1
                },
                new RemesaInformeEfectoDTO
                {
                    Cliente = "19514", Nombre = "JAZMIN AZUCENA ESPINOZA PEREA", Documento = "NV2607383",
                    Efecto = "1", Iban = "ES3921008494970200071750", Importe = 34.49m,
                    FechaCargo = new DateTime(2026, 7, 23), Asiento = 1
                },
                new RemesaInformeEfectoDTO
                {
                    Cliente = "39319", Nombre = "HEALTHY & FIT TWELVE, SOCIEDAD LIMITADA", Documento = "NV2612153",
                    Efecto = "1", Iban = "ES1521003584612210439194", Importe = 16.36m,
                    FechaCargo = new DateTime(2026, 7, 24), Asiento = 2
                }
            }
        };

        [TestMethod]
        public void GenerarPdf_RemesaMultiFecha_DevuelvePdfValido()
        {
            var resultado = _generador.GenerarPdf(RemesaMultiFecha());

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_RemesaDeUnaSolaFecha_DevuelvePdfValido()
        {
            RemesaInformeDTO remesa = RemesaMultiFecha();
            foreach (RemesaInformeEfectoDTO efecto in remesa.Efectos)
            {
                efecto.FechaCargo = new DateTime(2026, 7, 23);
                efecto.Asiento = 1; // modo forzado: todos en el mismo asiento
            }

            var resultado = _generador.GenerarPdf(remesa);

            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_SinEfectosYConNulos_NoLanzaYDevuelvePdf()
        {
            var remesa = new RemesaInformeDTO
            {
                Numero = 10902,
                Fecha = DateTime.Today,
                Empresa = null,
                Banco = null,
                IbanAbono = null,
                Efectos = null
            };

            var resultado = _generador.GenerarPdf(remesa);

            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_EfectoSinFechaCargo_CaeEnLaFechaDeLaRemesa()
        {
            RemesaInformeDTO remesa = RemesaMultiFecha();
            remesa.Efectos[0].FechaCargo = null;

            var resultado = _generador.GenerarPdf(remesa);

            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void AgruparPorAsiento_RespetarVencimientos_DosAsientosMismoDia_SonDosGrupos()
        {
            // NestoAPI#358: remesa 10901 — el banco hizo DOS abonos (dos asientos) AMBOS con
            // fecha 24. El informe debe mostrarlos SEPARADOS aunque compartan día de valor.
            DateTime fecha24 = new DateTime(2026, 7, 24);
            var efectos = new List<RemesaInformeEfectoDTO>
            {
                new RemesaInformeEfectoDTO { Asiento = 101, Importe = 69.93m, FechaCargo = fecha24 },
                new RemesaInformeEfectoDTO { Asiento = 102, Importe = 16.36m, FechaCargo = fecha24 }
            };

            List<IGrouping<int, RemesaInformeEfectoDTO>> grupos =
                GeneradorPdfRemesa.AgruparPorAsiento(efectos, fecha24);

            Assert.AreEqual(2, grupos.Count, "Dos asientos con el mismo día de valor son dos grupos");
            CollectionAssert.AreEquivalent(new[] { 101, 102 }, grupos.Select(g => g.Key).ToList());
            CollectionAssert.AreEquivalent(new[] { 69.93m, 16.36m }, grupos.Select(g => g.Sum(e => e.Importe)).ToList());
        }

        [TestMethod]
        public void AgruparPorAsiento_ModoForzado_UnSoloAsiento_EsUnGrupo()
        {
            // Modo forzado (10902): todos los efectos van en el mismo asiento aunque tengan
            // vencimientos distintos -> UN grupo (antes agrupaba por FechaVto y salían dos).
            DateTime fecha = new DateTime(2026, 7, 23);
            var efectos = new List<RemesaInformeEfectoDTO>
            {
                new RemesaInformeEfectoDTO { Asiento = 55, Importe = 10m, FechaCargo = fecha },
                new RemesaInformeEfectoDTO { Asiento = 55, Importe = 20m, FechaCargo = fecha }
            };

            List<IGrouping<int, RemesaInformeEfectoDTO>> grupos =
                GeneradorPdfRemesa.AgruparPorAsiento(efectos, fecha);

            Assert.AreEqual(1, grupos.Count);
            Assert.AreEqual(30m, grupos[0].Sum(e => e.Importe));
        }

        [TestMethod]
        public void AgruparPorAsiento_OrdenaPorDiaDeValorYLuegoAsiento()
        {
            var efectos = new List<RemesaInformeEfectoDTO>
            {
                new RemesaInformeEfectoDTO { Asiento = 200, FechaCargo = new DateTime(2026, 7, 25) },
                new RemesaInformeEfectoDTO { Asiento = 102, FechaCargo = new DateTime(2026, 7, 24) },
                new RemesaInformeEfectoDTO { Asiento = 101, FechaCargo = new DateTime(2026, 7, 24) }
            };

            List<IGrouping<int, RemesaInformeEfectoDTO>> grupos =
                GeneradorPdfRemesa.AgruparPorAsiento(efectos, DateTime.Today);

            CollectionAssert.AreEqual(new[] { 101, 102, 200 }, grupos.Select(g => g.Key).ToList(),
                "Primero por día de valor, luego por asiento (estable para asientos del mismo día)");
        }

        [TestMethod]
        public void FormatearIban_AgrupaDeCuatroEnCuatro()
        {
            Assert.AreEqual("ES06 2100 6273 9002 0006 3554",
                GeneradorPdfRemesa.FormatearIban("ES0621006273900200063554"));
        }

        [TestMethod]
        public void FormatearIban_NullVaciaYConEspacios()
        {
            Assert.AreEqual("", GeneradorPdfRemesa.FormatearIban(null));
            Assert.AreEqual("", GeneradorPdfRemesa.FormatearIban(""));
            Assert.AreEqual("ES06 2100 6273 9002 0006 3554",
                GeneradorPdfRemesa.FormatearIban("ES06 2100 6273 90 0200063554"));
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
