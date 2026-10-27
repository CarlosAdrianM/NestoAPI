using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Abandono;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Abandono
{
    /// <summary>NestoAPI#714 (corte 1): precisión de la regla por vendedor y mes, como función pura.</summary>
    [TestClass]
    public class InformeRiesgoAbandonoTests
    {
        private static FilaInformeAbandono Alerta(string vendedor, DateTime dia, string resultado = null)
            => new FilaInformeAbandono { Vendedor = vendedor, FechaInstantanea = dia, Tipo = TiposFilaAbandono.ALERTA, Baseline = true, Resuelto = resultado == null ? (DateTime?)null : dia.AddDays(70), Resultado = resultado };

        private static FilaInformeAbandono Instantanea(string vendedor, DateTime dia, bool baseline)
            => new FilaInformeAbandono { Vendedor = vendedor, FechaInstantanea = dia, Tipo = TiposFilaAbandono.INSTANTANEA, Baseline = baseline };

        [TestMethod]
        public void Calcular_PorVendedorYMes_ConPrecisionSinLosDesconocidos()
        {
            var oct = new DateTime(2026, 10, 27);
            var nov = new DateTime(2026, 11, 3);
            var filas = new List<FilaInformeAbandono>
            {
                Alerta("DV", oct, ResultadoAbandono.ABANDONO),
                Alerta("DV", oct, ResultadoAbandono.BAJA),
                Alerta("DV", oct, ResultadoAbandono.COMPRO),
                Alerta("DV", oct, ResultadoAbandono.DESCONOCIDO),
                Alerta("DV", oct), // pendiente
                Alerta("DV", nov, ResultadoAbandono.COMPRO),
                Alerta("IM", oct, ResultadoAbandono.ABANDONO),
                Instantanea("DV", oct.AddDays(-1), true),
                Instantanea("DV", oct.AddDays(-1), false),
                Instantanea("DV", oct.AddDays(-1), false)
            };

            InformeRiesgoAbandonoDTO informe = InformeRiesgoAbandono.Calcular(filas, new DateTime(2026, 10, 1), new DateTime(2026, 12, 1), ModoAbandono.Sombra);

            Assert.AreEqual(new DateTime(2026, 10, 1), informe.Desde);
            Assert.AreEqual(new DateTime(2026, 11, 30), informe.Hasta);
            Assert.AreEqual("Sombra", informe.Modo);
            Assert.AreEqual("baseline-1", informe.VersionRegla);
            Assert.AreEqual(3, informe.PorVendedorYMes.Count);

            FilaInformeRiesgoAbandonoDTO dvOct = informe.PorVendedorYMes.Single(f => f.Vendedor == "DV" && f.Mes == "2026-10");
            Assert.AreEqual(5, dvOct.Alertas);
            Assert.AreEqual(4, dvOct.Resueltas);
            Assert.AreEqual(1, dvOct.Abandonos);
            Assert.AreEqual(1, dvOct.Bajas);
            Assert.AreEqual(1, dvOct.Compraron);
            Assert.AreEqual(1, dvOct.Desconocidos);
            Assert.AreEqual(1, dvOct.Pendientes);
            Assert.AreEqual(2.0 / 3, dvOct.Precision.Value, 0.0001);
            Assert.AreEqual(3, dvOct.RegularesInstantaneas);
            Assert.AreEqual(1, dvOct.RegularesMarcados);

            FilaInformeRiesgoAbandonoDTO dvNov = informe.PorVendedorYMes.Single(f => f.Vendedor == "DV" && f.Mes == "2026-11");
            Assert.AreEqual(0.0, dvNov.Precision);

            Assert.AreEqual(7, informe.Totales.Alertas);
            Assert.AreEqual(6, informe.Totales.Resueltas);
            Assert.AreEqual(3.0 / 5, informe.Totales.Precision.Value, 0.0001);
            Assert.IsNull(informe.Totales.Vendedor);
        }

        [TestMethod]
        public void Calcular_SinResueltas_PrecisionNula()
        {
            var filas = new List<FilaInformeAbandono> { Alerta("DV", new DateTime(2026, 10, 27)) };
            InformeRiesgoAbandonoDTO informe = InformeRiesgoAbandono.Calcular(filas, new DateTime(2026, 10, 1), new DateTime(2026, 11, 1), ModoAbandono.Sombra);
            Assert.IsNull(informe.Totales.Precision);
            Assert.AreEqual(1, informe.Totales.Pendientes);
        }

        [TestMethod]
        public void Calcular_SinFilas_Vacio()
        {
            InformeRiesgoAbandonoDTO informe = InformeRiesgoAbandono.Calcular(null, new DateTime(2026, 10, 1), new DateTime(2026, 11, 1), ModoAbandono.Apagado);
            Assert.AreEqual(0, informe.PorVendedorYMes.Count);
            Assert.AreEqual(0, informe.Totales.Alertas);
            Assert.AreEqual("Apagado", informe.Modo);
        }
    }
}
