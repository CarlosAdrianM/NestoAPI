using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Comisiones;

namespace NestoAPI.Tests.Infrastructure.Comisiones
{
    /// <summary>
    /// Tests del mapper de prdComisionesPorVendedor (NestoAPI#210).
    ///
    /// Regresión Nesto#340: el SP prdComisionesPorVendedor devuelve 6 columnas
    /// con prefijo % (%ComisionCos, %ComisionFijaCos, %ComisionApa, etc.). En el
    /// EDMX legacy de Nesto esas columnas se mapeaban explícitamente a
    /// propiedades C_Comision* via ScalarProperty. Al migrar la lectura a
    /// NestoAPI con db.Database.SqlQuery&lt;ComisionesAntiguasDTO&gt;(...) ese mapping
    /// se perdió porque SqlQuery&lt;T&gt; empareja columnas a propiedades por nombre
    /// directo y % no es válido en identificadores C#. El panel de comisiones
    /// antiguas mostraba 0,00% en los 6 campos aunque los totales sí eran
    /// correctos.
    ///
    /// Estos tests cubren el mapper extraído que sí entiende esa convención.
    /// </summary>
    [TestClass]
    public class ComisionesLecturaServiceTests
    {
        private static DataTable CrearDataTableConTodasLasColumnas()
        {
            var dt = new DataTable();
            // Cosmética
            dt.Columns.Add("VentaCos", typeof(decimal));
            dt.Columns.Add("VentaFDMCos", typeof(decimal));
            dt.Columns.Add("VentaSabrinaCos", typeof(decimal));
            dt.Columns.Add("TotalCos", typeof(decimal));
            dt.Columns.Add("%ComisionCos", typeof(decimal));
            dt.Columns.Add("TotalComisionCos", typeof(decimal));
            dt.Columns.Add("%ComisionFijaCos", typeof(decimal));
            dt.Columns.Add("TotalComisionFijaCos", typeof(decimal));
            dt.Columns.Add("SiguienteTramoCos", typeof(decimal));
            dt.Columns.Add("DiferenciaTramoCos", typeof(decimal));
            dt.Columns.Add("ImporteRutaCos", typeof(decimal));
            // Aparatos
            dt.Columns.Add("VentaAPA", typeof(decimal));
            dt.Columns.Add("VentaFDMApa", typeof(decimal));
            dt.Columns.Add("VentaSabrinaApa", typeof(decimal));
            dt.Columns.Add("TotalApa", typeof(decimal));
            dt.Columns.Add("%ComisionApa", typeof(decimal));
            dt.Columns.Add("TotalComisionApa", typeof(decimal));
            dt.Columns.Add("%ComisionFijaApa", typeof(decimal));
            dt.Columns.Add("TotalComisionFijaApa", typeof(decimal));
            dt.Columns.Add("SiguienteTramoApa", typeof(decimal));
            dt.Columns.Add("DiferenciaTramoApa", typeof(decimal));
            dt.Columns.Add("ImporteRutaApa", typeof(decimal));
            // Otros Aparatos
            dt.Columns.Add("VentaAcp", typeof(decimal));
            dt.Columns.Add("VentaFDMAcp", typeof(decimal));
            dt.Columns.Add("VentaSabrinaAcp", typeof(decimal));
            dt.Columns.Add("TotalAcp", typeof(decimal));
            dt.Columns.Add("%ComisionAcp", typeof(decimal));
            dt.Columns.Add("TotalComisionAcp", typeof(decimal));
            dt.Columns.Add("%ComisionFijaAcp", typeof(decimal));
            dt.Columns.Add("TotalComisionFijaAcp", typeof(decimal));
            dt.Columns.Add("SiguienteTramoAcp", typeof(decimal));
            dt.Columns.Add("DiferenciaTramoAcp", typeof(decimal));
            dt.Columns.Add("ImporteRutaAcp", typeof(decimal));
            // Totales / extras
            dt.Columns.Add("TotalComision", typeof(decimal));
            dt.Columns.Add("PremioEva", typeof(decimal));
            dt.Columns.Add("EvaFdM", typeof(decimal));
            dt.Columns.Add("CifraRuta", typeof(decimal));
            dt.Columns.Add("VentaCur", typeof(decimal));
            dt.Columns.Add("TotalComisionCur", typeof(decimal));
            dt.Columns.Add("VentaUL", typeof(decimal));
            dt.Columns.Add("TotalComisionUL", typeof(decimal));
            return dt;
        }

        [TestMethod]
        public void MapearComisionesAntiguas_ColumnasPrefijadasConPorcentaje_MapeaAPropertyC_Comision()
        {
            // Arrange: simular el result set del SP con las 6 columnas problemáticas
            // teniendo valores no-cero. Antes del fix, SqlQuery<T> dejaba todas en 0.
            var dt = CrearDataTableConTodasLasColumnas();
            var row = dt.NewRow();
            row["%ComisionCos"] = 3.54m;
            row["%ComisionFijaCos"] = 1.20m;
            row["%ComisionApa"] = 5.00m;
            row["%ComisionFijaApa"] = 2.00m;
            row["%ComisionAcp"] = 7.50m;
            row["%ComisionFijaAcp"] = 3.00m;
            // Cubrir también una de las columnas "normales" para asegurar que el
            // refactor no rompió el mapping directo.
            row["VentaCos"] = 1234.56m;
            row["TotalComision"] = 42.42m;
            dt.Rows.Add(row);

            using (var reader = dt.CreateDataReader())
            {
                Assert.IsTrue(reader.Read(), "El DataReader debe poder leer la fila preparada");

                // Act
                var dto = ComisionesLecturaService.MapearComisionesAntiguas(reader);

                // Assert: las 6 columnas problemáticas llegan a su property
                Assert.AreEqual(3.54m, dto.C_ComisionCos,     "%ComisionCos debe mapear a C_ComisionCos");
                Assert.AreEqual(1.20m, dto.C_ComisionFijaCos, "%ComisionFijaCos debe mapear a C_ComisionFijaCos");
                Assert.AreEqual(5.00m, dto.C_ComisionApa,     "%ComisionApa debe mapear a C_ComisionApa");
                Assert.AreEqual(2.00m, dto.C_ComisionFijaApa, "%ComisionFijaApa debe mapear a C_ComisionFijaApa");
                Assert.AreEqual(7.50m, dto.C_ComisionAcp,     "%ComisionAcp debe mapear a C_ComisionAcp");
                Assert.AreEqual(3.00m, dto.C_ComisionFijaAcp, "%ComisionFijaAcp debe mapear a C_ComisionFijaAcp");

                // Y las columnas que ya iban bien siguen yendo bien
                Assert.AreEqual(1234.56m, dto.VentaCos);
                Assert.AreEqual(42.42m, dto.TotalComision);
            }
        }

        [TestMethod]
        public void MapearComisionesAntiguas_ColumnaNula_DevuelveCero()
        {
            // Arrange: row con DBNull en columnas problemáticas. Sucede si el SP
            // devuelve NULL para un vendedor sin ventas. No debe explotar.
            var dt = CrearDataTableConTodasLasColumnas();
            var row = dt.NewRow();
            // Todo lo dejamos a DBNull (default de DataRow.NewRow)
            dt.Rows.Add(row);

            using (var reader = dt.CreateDataReader())
            {
                Assert.IsTrue(reader.Read());

                var dto = ComisionesLecturaService.MapearComisionesAntiguas(reader);

                Assert.AreEqual(0m, dto.C_ComisionCos);
                Assert.AreEqual(0m, dto.VentaCos);
                Assert.AreEqual(0m, dto.TotalComision);
            }
        }
    }
}
