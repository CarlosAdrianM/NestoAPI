using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Linq;
using static NestoAPI.Tests.Infrastructure.Stocks.Tendencias.SeriesTendencia;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#630: backtest de calibración (cómo estaban 6, 9 y 12 meses antes los que acabaron mal).</summary>
    [TestClass]
    public class CalibracionTendenciasTests
    {
        /// <summary>Vende 30 al mes hasta hace 19 meses y luego nada; 100 uds en stock; entró en EXT en septiembre de 2025.</summary>
        private static DatosProductoTendencia AcaboEnExt()
        {
            int[] ventas = Constante(30);
            for (int i = 41; i < N; i++)
            {
                ventas[i] = 0;
            }
            DatosProductoTendencia d = Producto("EXT1", ventas: ventas, stock: 100);
            d.StockExt = 100;
            d.UltimaEntradaExt = new DateTime(2025, 9, 15); // posición 47
            return d;
        }

        [TestMethod]
        public void Grupo_EXT_FechaDelEventoEsLaEntradaEnExt()
        {
            string grupo = CalibracionTendencias.Grupo(AcaboEnExt(), MesReferencia, out int evento);

            Assert.AreEqual(GruposCalibracion.EXT, grupo);
            Assert.AreEqual(47, evento);
        }

        [TestMethod]
        public void Grupo_SinVenta12Meses_EventoEsElUltimoMesConVenta()
        {
            int[] ventas = Constante(10);
            for (int i = 40; i < N; i++)
            {
                ventas[i] = 0;
            }

            string grupo = CalibracionTendencias.Grupo(Producto("SV1", ventas: ventas), MesReferencia, out int evento);

            Assert.AreEqual(GruposCalibracion.SIN_VENTA, grupo);
            Assert.AreEqual(39, evento);
        }

        [TestMethod]
        public void Grupo_VivoYEstado4()
        {
            Assert.AreEqual(GruposCalibracion.VIVO, CalibracionTendencias.Grupo(Producto("V1"), MesReferencia, out _));
            Assert.AreEqual(GruposCalibracion.ESTADO_4, CalibracionTendencias.Grupo(Producto("E4", estado: 4), MesReferencia, out _));
            // Muerto hace más de 3 años: no informa
            int[] antiguo = Ultimos(0);
            antiguo[2] = 5;
            Assert.IsNull(CalibracionTendencias.Grupo(Producto("X", ventas: antiguo), MesReferencia, out _));
        }

        [TestMethod]
        public void Calcular_CoberturaYPendienteAntesDelEvento()
        {
            ResultadoCalibracion r = CalibracionTendencias.Calcular(new[] { AcaboEnExt(), Producto("V1") }, f => null, MesReferencia, new ParametrosTendencias());

            FilaCalibracion ext = r.Detalle.Single(f => f.Producto == "EXT1");
            Assert.AreEqual(new DateTime(2025, 9, 1), ext.MesEvento);
            // 6 meses antes (posición 41): la media de 6 meses es 5 × 30 ÷ 6 = 25 → 100 ÷ 25 = 4
            Assert.AreEqual(4.0m, ext.Cobertura[6]);
            // 9 meses antes (38): aún vendía 30 → 3,3
            Assert.AreEqual(3.3m, ext.Cobertura[9]);
            Assert.AreEqual(0m, ext.Pendiente[12]);

            ResumenCalibracion resumen = r.Resumen.Single(x => x.Grupo == GruposCalibracion.EXT && x.MesesAntes == 6);
            Assert.AreEqual(1, resumen.Productos);
            Assert.AreEqual(4.0m, resumen.MedianaCobertura);

            UmbralCalibracion regla = r.Umbrales.Single(u => u.Regla == "Cobertura > 6 meses" && u.MesesAntes == 6);
            Assert.AreEqual(1, regla.Muertos);
            Assert.AreEqual(1, regla.Vivos);
            Assert.AreEqual(0m, regla.PorcentajeMuertosMarcados);
            Assert.AreEqual(0m, regla.PorcentajeVivosMarcados);
        }

        [TestMethod]
        public void Mediana()
        {
            Assert.AreEqual(2m, CalibracionTendencias.Mediana(new decimal?[] { 3m, 1m, null, 2m }));
            Assert.AreEqual(2.5m, CalibracionTendencias.Mediana(new decimal?[] { 1m, 2m, 3m, 4m }));
            Assert.IsNull(CalibracionTendencias.Mediana(new decimal?[] { null }));
        }
    }
}
