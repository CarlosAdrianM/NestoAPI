using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Collections.Generic;
using System.Linq;
using static NestoAPI.Tests.Infrastructure.Stocks.Tendencias.SeriesTendencia;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#630: indicadores sobre series pequeñas (cálculo puro, sin BD).</summary>
    [TestClass]
    public class CalculadoraSeriesTests
    {
        /// <summary>10 meses a 30 y noviembre y diciembre a 60, todos los años.</summary>
        private static int[] Estacional()
        {
            int[] serie = new int[N];
            for (int i = 0; i < N; i++)
            {
                int mes = CalculadoraSeries.MesDelAnio(MesReferencia, FIN, i);
                serie[i] = mes == 10 || mes == 11 ? 60 : 30;
            }
            return serie;
        }

        [TestMethod]
        public void Media_YPonderada()
        {
            int[] serie = Ultimos(10, 10, 10, 10, 10, 10, 20, 20, 20, 40, 40, 40);

            Assert.AreEqual(40m, CalculadoraSeries.Media(serie, FIN, 3));
            Assert.AreEqual(30m, CalculadoraSeries.Media(serie, FIN, 6));
            Assert.AreEqual(20m, CalculadoraSeries.Media(serie, FIN, 12));
            // (3 × 40 + 2 × 30 + 1 × 20) ÷ 6
            Assert.AreEqual(200m / 6m, CalculadoraSeries.MediaPonderada(serie, FIN));
        }

        [TestMethod]
        public void IndicesEstacionales_FamiliaEstacional_PicoEnNoviembreYDiciembre()
        {
            decimal[] indices = CalculadoraSeries.IndicesEstacionales(Estacional(), FIN, MesReferencia, 120, 0);

            Assert.IsNotNull(indices);
            // media mensual = (10 × 30 + 2 × 60) ÷ 12 = 35
            Assert.AreEqual(Math.Round(60m / 35m, 4), Math.Round(indices[10], 4));
            Assert.AreEqual(Math.Round(60m / 35m, 4), Math.Round(indices[11], 4));
            Assert.AreEqual(Math.Round(30m / 35m, 4), Math.Round(indices[0], 4));
        }

        [TestMethod]
        public void IndicesEstacionales_FamiliaSinTresAnios_SinEstacionalidad()
        {
            int[] serie = Ultimos(Enumerable.Repeat(30, 30).ToArray());

            Assert.IsNull(CalculadoraSeries.IndicesEstacionales(serie, FIN, MesReferencia, 120, CalculadoraSeries.PrimerMesConVenta(serie)));
        }

        [TestMethod]
        public void IndicesEstacionales_FamiliaConPocasUnidades_SinEstacionalidad()
        {
            Assert.IsNull(CalculadoraSeries.IndicesEstacionales(Constante(2), FIN, MesReferencia, 120, 0));
        }

        [TestMethod]
        public void PendienteRelativa_SerieEstacionalDesestacionalizada_NoEsTendencia()
        {
            int[] serie = Estacional();
            decimal[] indices = CalculadoraSeries.IndicesEstacionales(serie, FIN, MesReferencia, 120, 0);

            decimal? conIndices = CalculadoraSeries.PendienteRelativa(serie, FIN, indices, MesReferencia);
            decimal? sinIndices = CalculadoraSeries.PendienteRelativa(serie, FIN, null, MesReferencia);

            Assert.AreEqual(0m, Math.Round(conIndices.Value, 1));
            // Sin desestacionalizar, el pico de noviembre-diciembre al principio de la ventana parece una caída.
            Assert.IsTrue(sinIndices.Value < -1m, $"sin índices: {sinIndices}");
        }

        [TestMethod]
        public void PendienteRelativa_CaidaLineal_EnPorcentajeDeLaMedia()
        {
            int[] serie = Ultimos(120, 110, 100, 90, 80, 70, 60, 50, 40, 30, 20, 10);

            // pendiente −10 uds/mes sobre una media de 65
            Assert.AreEqual(Math.Round(-1000m / 65m, 2), CalculadoraSeries.PendienteRelativa(serie, FIN, null, MesReferencia));
        }

        [TestMethod]
        public void PendienteRelativa_SinVenta_Null()
        {
            Assert.IsNull(CalculadoraSeries.PendienteRelativa(new int[N], FIN, null, MesReferencia));
        }

        [TestMethod]
        public void RatioTrimestre_ContraElMismoTrimestreDelAnioAnterior()
        {
            int[] serie = Constante(100);
            serie[FIN] = 50;
            serie[FIN - 1] = 50;
            serie[FIN - 2] = 50;

            Assert.AreEqual(0.5m, CalculadoraSeries.RatioTrimestre(serie, FIN));
        }

        [TestMethod]
        public void Contexto_PicoHaceDosAnios()
        {
            int[] serie = new int[N];
            int[] porAnio = { 10, 20, 40, 30, 20 }; // del más antiguo al último
            for (int i = 0; i < N; i++)
            {
                serie[i] = porAnio[i / 12];
            }

            ContextoCincoAnios contexto = CalculadoraSeries.Contexto(serie, FIN, MesReferencia);

            Assert.AreEqual(2, contexto.AniosDesdePico);
            Assert.AreEqual(2024, contexto.AnioPico);
            Assert.AreEqual(50m, contexto.PorcentajeDelPico);
        }

        [TestMethod]
        public void TasaRecompra_NuevoSinRecompra_Cero()
        {
            DateTime lanzamiento = new DateTime(2026, 5, 4);
            List<VentaClienteTendencia> ventas = Enumerable.Range(1, 20)
                .Select(c => new VentaClienteTendencia { Cliente = c.ToString(), Fecha = lanzamiento.AddDays(c) })
                .ToList();

            Assert.AreEqual(0m, CalculadoraSeries.TasaRecompra(ventas, lanzamiento, MesReferencia.AddMonths(1)));
        }

        [TestMethod]
        public void TasaRecompra_CuentaSoloLosDeLosPrimeros60DiasYLaRecompraA30()
        {
            DateTime lanzamiento = new DateTime(2026, 5, 4);
            List<VentaClienteTendencia> ventas = new List<VentaClienteTendencia>
            {
                new VentaClienteTendencia { Cliente = "1", Fecha = lanzamiento },
                new VentaClienteTendencia { Cliente = "1", Fecha = lanzamiento.AddDays(40) },  // repite
                new VentaClienteTendencia { Cliente = "2", Fecha = lanzamiento.AddDays(5) },
                new VentaClienteTendencia { Cliente = "2", Fecha = lanzamiento.AddDays(20) },  // a los 15 días: no cuenta
                new VentaClienteTendencia { Cliente = "3", Fecha = lanzamiento.AddDays(10) },
                new VentaClienteTendencia { Cliente = "3", Fecha = lanzamiento.AddDays(70) },  // repite
                new VentaClienteTendencia { Cliente = "4", Fecha = lanzamiento.AddDays(90) },  // llegó tarde: fuera
                new VentaClienteTendencia { Cliente = "5", Fecha = lanzamiento.AddDays(30) }
            };

            Assert.AreEqual(50m, CalculadoraSeries.TasaRecompra(ventas, lanzamiento, MesReferencia.AddMonths(1)));
        }

        [TestMethod]
        public void TasaRecompra_DemasiadoPronto_Null()
        {
            DateTime lanzamiento = new DateTime(2026, 8, 20);
            List<VentaClienteTendencia> ventas = new List<VentaClienteTendencia> { new VentaClienteTendencia { Cliente = "1", Fecha = lanzamiento } };

            Assert.IsNull(CalculadoraSeries.TasaRecompra(ventas, lanzamiento, MesReferencia.AddMonths(1)));
        }

        [TestMethod]
        public void Velocidad_DesdeLaPrimeraVenta_ConMinimoDeUnMes()
        {
            int[] serie = Ultimos(10, 20);

            Assert.AreEqual(30m, CalculadoraSeries.Velocidad(serie, FIN, new DateTime(2026, 9, 20), MesReferencia.AddMonths(1)));
            decimal velocidad = CalculadoraSeries.Velocidad(serie, FIN, new DateTime(2026, 8, 1), MesReferencia.AddMonths(1));
            Assert.AreEqual(Math.Round(30m / (61m / 30.44m), 2), velocidad);
        }
    }
}
