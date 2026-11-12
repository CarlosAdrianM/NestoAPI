using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

namespace NestoAPI.Tests.Infraestructure.Informes
{
    /// <summary>NestoAPI#719: «Facturado en tienda» acumulado del año y comparativo con años anteriores (cálculo y Excel).</summary>
    [TestClass]
    public class GestorFacturacionTiendaAnualTests
    {
        private static readonly DateTime HASTA = new DateTime(2026, 3, 15);

        private static FilaMesFacturacionTiendaDTO Fila(int anio, int mes, decimal baseImponible, int facturas = 1, decimal cursos = 0)
            => new FilaMesFacturacionTiendaDTO { Anio = anio, Mes = mes, BaseImponible = baseImponible, NumeroFacturas = facturas, BaseImponibleCursos = cursos };

        [TestMethod]
        public void NormalizarTipo_VacioEsSemanaYSinMirarMayusculas()
        {
            Assert.AreEqual(GestorFacturacionTienda.TIPO_SEMANA, GestorFacturacionTienda.NormalizarTipo(null));
            Assert.AreEqual(GestorFacturacionTienda.TIPO_SEMANA, GestorFacturacionTienda.NormalizarTipo(" "));
            Assert.AreEqual(GestorFacturacionTienda.TIPO_ACUMULADO, GestorFacturacionTienda.NormalizarTipo("acumulado"));
            Assert.AreEqual(GestorFacturacionTienda.TIPO_COMPARATIVO, GestorFacturacionTienda.NormalizarTipo(" COMPARATIVO "));
            Assert.IsNull(GestorFacturacionTienda.NormalizarTipo("Mensual"));
        }

        [TestMethod]
        public void ResolverAniosAnteriores_AcumuladoCeroComparativoDosPorDefectoYTopes()
        {
            Assert.IsNull(GestorFacturacionTienda.ResolverAniosAnteriores(GestorFacturacionTienda.TIPO_ACUMULADO, 5, out int acumulado));
            Assert.AreEqual(0, acumulado);
            Assert.IsNull(GestorFacturacionTienda.ResolverAniosAnteriores(GestorFacturacionTienda.TIPO_COMPARATIVO, null, out int porDefecto));
            Assert.AreEqual(2, porDefecto);
            Assert.IsNull(GestorFacturacionTienda.ResolverAniosAnteriores(GestorFacturacionTienda.TIPO_COMPARATIVO, 10, out _));
            Assert.IsNotNull(GestorFacturacionTienda.ResolverAniosAnteriores(GestorFacturacionTienda.TIPO_COMPARATIVO, 0, out _));
            Assert.IsNotNull(GestorFacturacionTienda.ResolverAniosAnteriores(GestorFacturacionTienda.TIPO_COMPARATIVO, 11, out _));
        }

        [TestMethod]
        public void ParametrosAnual_AcotaDesdeElUnoDeEneroDelAnioMasAntiguoHastaElDiaSiguiente()
        {
            Dictionary<string, object> p = GestorFacturacionTienda.ParametrosAnual("REI", HASTA, 2)
                .Cast<SqlParameter>().ToDictionary(x => x.ParameterName, x => x.Value);

            Assert.AreEqual(new DateTime(2024, 1, 1), p["@p2"]);
            Assert.AreEqual(new DateTime(2026, 3, 16), p["@p3"]);
            Assert.AreEqual("REI", p["@p4"]);
            Assert.AreEqual(3, p["@p6"]);
            Assert.AreEqual(15, p["@p7"]);
        }

        [TestMethod]
        public void SqlAnual_AgregaEnSqlYAcotaPorFecha()
        {
            string sql = GestorFacturacionTienda.SQL_FACTURACION_TIENDA_ANUAL;
            StringAssert.Contains(sql, "GROUP BY YEAR(f.Fecha), MONTH(f.Fecha)");
            StringAssert.Contains(sql, "f.Fecha >= @p2 AND f.Fecha < @p3");
            StringAssert.Contains(sql, "l.[Almacén] = @p4");
        }

        [TestMethod]
        public void ConstruirAnual_Acumulado_TodosLosMesesConSuAcumuladoYElUltimoHastaElDia()
        {
            FacturacionTiendaAnualDTO informe = GestorFacturacionTienda.ConstruirAnual(new[]
            {
                Fila(2026, 1, 100.004m, 3, 20m),
                Fila(2026, 3, -10m, 1) // una rectificativa resta
            }, "REI", HASTA, 0);

            Assert.AreEqual(GestorFacturacionTienda.TIPO_ACUMULADO, informe.Tipo);
            Assert.AreEqual(2026, informe.Anio);
            Assert.AreEqual(new DateTime(2026, 1, 1), informe.Desde);
            Assert.AreEqual(3, informe.Meses.Count);
            Assert.AreEqual("Febrero", informe.Meses[1].NombreMes);
            Assert.AreEqual(0m, informe.Meses[1].BaseImponible);
            Assert.AreEqual(new DateTime(2026, 2, 28), informe.Meses[1].Hasta);
            Assert.AreEqual(HASTA, informe.Meses[2].Hasta);
            Assert.AreEqual(100m, informe.Meses[0].BaseImponible);
            Assert.AreEqual(100m, informe.Meses[1].Acumulado);
            Assert.AreEqual(90m, informe.Meses[2].Acumulado);
            Assert.AreEqual(90m, informe.Total.BaseImponible);
            Assert.AreEqual(4, informe.Total.NumeroFacturas);
            Assert.AreEqual(20m, informe.Total.BaseImponibleCursos);
            Assert.AreEqual(0, informe.AniosAnteriores.Count);
            Assert.AreEqual(0, informe.Meses[0].Anteriores.Count);
        }

        [TestMethod]
        public void ConstruirAnual_Comparativo_VariacionEnEurosYPorcentajeRespectoACadaAnio()
        {
            FacturacionTiendaAnualDTO informe = GestorFacturacionTienda.ConstruirAnual(new[]
            {
                Fila(2026, 1, 150m), Fila(2025, 1, 100m), Fila(2024, 1, 200m),
                Fila(2026, 2, 50m),                         // en 2025 y 2024, nada en febrero
                Fila(2025, 3, 30m, 2)
            }, "ALC", HASTA, 2);

            Assert.AreEqual(GestorFacturacionTienda.TIPO_COMPARATIVO, informe.Tipo);
            CollectionAssert.AreEqual(new[] { 2025, 2024 }, informe.AniosAnteriores);

            ComparativoMesFacturacionTiendaDTO enero2025 = informe.Meses[0].Anteriores[0];
            Assert.AreEqual(2025, enero2025.Anio);
            Assert.AreEqual(50m, enero2025.VariacionImporte);
            Assert.AreEqual(50m, enero2025.VariacionPorcentaje);
            ComparativoMesFacturacionTiendaDTO enero2024 = informe.Meses[0].Anteriores[1];
            Assert.AreEqual(-50m, enero2024.VariacionImporte);
            Assert.AreEqual(-25m, enero2024.VariacionPorcentaje);

            Assert.IsNull(informe.Meses[1].Anteriores[0].VariacionPorcentaje, "sin base el año anterior no hay porcentaje");
            Assert.AreEqual(50m, informe.Meses[1].Anteriores[0].VariacionImporte);

            ComparativoMesFacturacionTiendaDTO total2025 = informe.Total.Anteriores[0];
            Assert.AreEqual(130m, total2025.BaseImponible);
            Assert.AreEqual(3, total2025.NumeroFacturas);
            Assert.AreEqual(70m, total2025.VariacionImporte);
            Assert.AreEqual(53.85m, total2025.VariacionPorcentaje);
        }

        [TestMethod]
        public void ConstruirAnual_IgnoraFilasDeMesesPosteriores()
        {
            FacturacionTiendaAnualDTO informe = GestorFacturacionTienda.ConstruirAnual(new[] { Fila(2025, 7, 999m) }, "REI", HASTA, 1);

            Assert.AreEqual(3, informe.Meses.Count);
            Assert.AreEqual(0m, informe.Total.Anteriores[0].BaseImponible);
        }

        private static (List<string> Hojas, List<string> Textos) LeerExcel(byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(ms, false))
            {
                List<string> hojas = doc.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Select(s => s.Name.Value).ToList();
                List<string> textos = doc.WorkbookPart.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>().Select(i => i.InnerText).ToList();
                return (hojas, textos);
            }
        }

        [TestMethod]
        public void Excel_Acumulado_SoloLaHojaAcumulado()
        {
            FacturacionTiendaAnualDTO informe = GestorFacturacionTienda.ConstruirAnual(new[] { Fila(2026, 1, 100m) }, "REI", HASTA, 0);

            (List<string> hojas, List<string> textos) = LeerExcel(new GeneradorExcelFacturacionTienda().GenerarBytes(informe));

            CollectionAssert.AreEqual(new[] { "Acumulado" }, hojas);
            foreach (string columna in GeneradorExcelFacturacionTienda.COLUMNAS_ACUMULADO)
            {
                CollectionAssert.Contains(textos, columna);
            }
            CollectionAssert.Contains(textos, "Enero");
            CollectionAssert.Contains(textos, "Marzo (hasta el 15)");
            CollectionAssert.Contains(textos, "Total");
            Assert.AreEqual("FacturacionTienda_REI_Acumulado_20260315.xlsx", GeneradorExcelFacturacionTienda.NombreFichero(informe));
        }

        [TestMethod]
        public void Excel_Comparativo_HojasAcumuladoYComparativoConSusColumnas()
        {
            FacturacionTiendaAnualDTO informe = GestorFacturacionTienda.ConstruirAnual(new[] { Fila(2026, 1, 150m), Fila(2025, 1, 100m) }, "ALC", HASTA, 2);

            string[] columnas = GeneradorExcelFacturacionTienda.ColumnasComparativo(informe);
            CollectionAssert.AreEqual(new[] { "Mes", "Base 2026", "Base 2025", "Base 2024", "Var. € vs 2025", "Var. % vs 2025", "Var. € vs 2024", "Var. % vs 2024" },
                columnas);
            (List<string> hojas, List<string> textos) = LeerExcel(new GeneradorExcelFacturacionTienda().GenerarBytes(informe));
            CollectionAssert.AreEqual(new[] { "Acumulado", "Comparativo" }, hojas);
            foreach (string columna in columnas)
            {
                CollectionAssert.Contains(textos, columna);
            }
        }
    }
}
