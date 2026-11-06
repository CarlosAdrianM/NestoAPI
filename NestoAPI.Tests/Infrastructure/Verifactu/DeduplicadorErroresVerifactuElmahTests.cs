using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Verifactu;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// NestoAPI#346 (06/11/26): la deduplicación en memoria se perdía con cada reciclaje del pool y del 30/10 al 06/11 cada
    /// factura atascada (CV2600692/693, RV2600129/154/155, «facturas suficientes» del 62400000) dejó un aviso igual cada día.
    /// Lo que va a ELMAH mira también dbo.ELMAH_Error de los últimos 7 días.
    /// </summary>
    [TestClass]
    public class DeduplicadorErroresVerifactuElmahTests
    {
        internal class ElmahFalso : ILectorErroresElmah
        {
            public List<(string Mensaje, DateTime TimeUtc)> Errores { get; } = new List<(string, DateTime)>();
            public int Consultas { get; private set; }
            public bool Fallar { get; set; }

            public bool HayErrorConMensajeDesde(string mensaje, DateTime desdeUtc)
            {
                Consultas++;
                if (Fallar)
                {
                    throw new InvalidOperationException("Timeout expired");
                }
                return Errores.Any(e => e.Mensaje == mensaje && e.TimeUtc >= desdeUtc);
            }
        }

        private static readonly DateTime AHORA = new DateTime(2026, 11, 6, 8, 15, 0, DateTimeKind.Utc);
        private const string CLAVE = "1|CV2600692";
        private const string MENSAJE = "Verifactu: error al enviar la factura CV2600692 (1117): El NIF no está identificado en el censo de la AEAT";

        private ElmahFalso elmah;

        [TestInitialize]
        public void Setup()
        {
            DeduplicadorErroresVerifactu.Reset();
            elmah = new ElmahFalso();
            DeduplicadorErroresVerifactu.AhoraUtc = () => AHORA;
        }

        [TestCleanup]
        public void Cleanup()
        {
            DeduplicadorErroresVerifactu.Reset();
        }

        [TestMethod]
        public void EsNovedadEnElmah_SinLectorDeElmah_SoloMemoriaComoHoy()
        {
            Assert.IsTrue(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE), "El primero se loguea");
            Assert.IsFalse(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE), "El mismo texto, no");
            Assert.IsTrue(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE + " (otro motivo)"), "Si cambia el texto, sí");
        }

        [TestMethod]
        public void EsNovedadEnElmah_EnMemoria_NiSiquieraPreguntaAElmah()
        {
            DeduplicadorErroresVerifactu.UsarElmah(elmah);

            Assert.IsTrue(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE));
            Assert.IsFalse(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE));
            Assert.IsFalse(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE));

            Assert.AreEqual(1, elmah.Consultas, "Solo la primera vez (tras el reciclaje) se mira ELMAH");
        }

        [TestMethod]
        public void EsNovedadEnElmah_ElmahLoTieneDeHaceTresDias_NoLoguea()
        {
            elmah.Errores.Add((MENSAJE, AHORA.AddDays(-3)));
            DeduplicadorErroresVerifactu.UsarElmah(elmah);

            Assert.IsFalse(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE), "Tras el reciclaje, ELMAH ya lo tiene");
            Assert.IsFalse(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE), "Y queda en memoria");
            Assert.AreEqual(1, elmah.Consultas);
        }

        [TestMethod]
        public void EsNovedadEnElmah_ElmahLoTieneDeHaceOchoDias_Loguea()
        {
            elmah.Errores.Add((MENSAJE, AHORA.AddDays(-8)));
            DeduplicadorErroresVerifactu.UsarElmah(elmah);

            Assert.IsTrue(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE), "Más de 7 días: se vuelve a avisar");
        }

        [TestMethod]
        public void EsNovedadEnElmah_ElmahTieneOtroTexto_Loguea()
        {
            elmah.Errores.Add((MENSAJE.Replace("CV2600692", "CV2600693"), AHORA.AddHours(-1)));
            DeduplicadorErroresVerifactu.UsarElmah(elmah);

            Assert.IsTrue(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE));
        }

        [TestMethod]
        public void EsNovedadEnElmah_FallaLaConsultaAElmah_LogueaComoHoy()
        {
            elmah.Errores.Add((MENSAJE, AHORA.AddDays(-1)));
            elmah.Fallar = true;
            DeduplicadorErroresVerifactu.UsarElmah(elmah);

            Assert.IsTrue(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE));
            Assert.IsFalse(DeduplicadorErroresVerifactu.EsNovedadEnElmah(CLAVE, MENSAJE), "La memoria sigue cortando la repetición");
        }

        [TestMethod]
        public void EsNovedad_SoloMemoria_NoMiraElmah()
        {
            // El resumen por correo del job (VerifactuJobsService) no va a ELMAH: sigue solo en memoria
            elmah.Errores.Add((MENSAJE, AHORA.AddDays(-1)));
            DeduplicadorErroresVerifactu.UsarElmah(elmah);

            Assert.IsTrue(DeduplicadorErroresVerifactu.EsNovedad(CLAVE, MENSAJE));
            Assert.AreEqual(0, elmah.Consultas);
        }

        [TestMethod]
        public void ComoLoGuardaElmah_RecortaA500()
        {
            string largo = new string('x', 600);

            Assert.AreEqual(500, LectorErroresElmahSql.ComoLoGuardaElmah(largo).Length);
            Assert.AreEqual("corto", LectorErroresElmahSql.ComoLoGuardaElmah("corto"));
            Assert.AreEqual(string.Empty, LectorErroresElmahSql.ComoLoGuardaElmah(null));
        }
    }
}
