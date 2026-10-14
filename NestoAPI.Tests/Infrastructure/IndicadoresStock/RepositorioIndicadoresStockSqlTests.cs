using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.IndicadoresStock;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Tests.Infrastructure.IndicadoresStock
{
    /// <summary>NestoAPI#629: las consultas del job van todas con NOLOCK y el INSERT casa con sus parámetros.</summary>
    [TestClass]
    public class RepositorioIndicadoresStockSqlTests
    {
        private static readonly string[] LECTURAS =
        {
            RepositorioIndicadoresStockSql.SQL_PRODUCTOS, RepositorioIndicadoresStockSql.SQL_STOCK, RepositorioIndicadoresStockSql.SQL_VENTAS,
            RepositorioIndicadoresStockSql.SQL_SERVICIO, RepositorioIndicadoresStockSql.SQL_MOVIMIENTOS_COMPRADOS
        };

        [TestMethod]
        public void Lecturas_TodasLasTablasConNoLock()
        {
            foreach (string sql in LECTURAS)
            {
                int tablas = Regex.Matches(sql, @"\bdbo\.(ExtractoProducto|LinPedidoVta|Productos)\b").Count;
                int nolock = Regex.Matches(sql, @"\bdbo\.(ExtractoProducto|LinPedidoVta|Productos) \w+ WITH \(NOLOCK\)").Count;
                Assert.IsTrue(tablas > 0);
                Assert.AreEqual(tablas, nolock, sql);
            }
        }

        [TestMethod]
        public void Lecturas_AcotadasAlMes()
        {
            StringAssert.Contains(RepositorioIndicadoresStockSql.SQL_STOCK, "e.Fecha < @p1");
            StringAssert.Contains(RepositorioIndicadoresStockSql.SQL_VENTAS, "l.[Fecha Albarán] >= DATEADD(month, -23, @p0) AND l.[Fecha Albarán] < @p1");
            StringAssert.Contains(RepositorioIndicadoresStockSql.SQL_SERVICIO, "l.[Fecha Albarán] >= @p0 AND l.[Fecha Albarán] < @p1");
            StringAssert.Contains(RepositorioIndicadoresStockSql.SQL_MOVIMIENTOS_COMPRADOS, "e.Fecha >= @p0 AND e.Fecha < @p1");
        }

        [TestMethod]
        public void Insertar_UnParametroPorColumnaYLosNullConTipo()
        {
            var fila = new FilaIndicadoresStock { Almacen = "TOTAL", ValorStock = 12.5m, UnidadesStock = 3 };

            SqlParameter[] parametros = RepositorioIndicadoresStockSql.Parametros(new DateTime(2026, 9, 1), fila).Cast<SqlParameter>().ToArray();

            Assert.AreEqual(RepositorioIndicadoresStockSql.COLUMNAS.Length, parametros.Length);
            StringAssert.Contains(RepositorioIndicadoresStockSql.SQL_INSERTAR, "@p" + (parametros.Length - 1) + ")");
            Assert.AreEqual(SqlDbType.Date, parametros[0].SqlDbType);
            Assert.AreEqual(12.5m, parametros[2].Value);
            Assert.AreEqual(3, parametros[3].Value);
            int productosEstado4 = Array.IndexOf(RepositorioIndicadoresStockSql.COLUMNAS, "ProductosEstado4");
            Assert.AreEqual(DBNull.Value, parametros[productosEstado4].Value);
            Assert.AreEqual(SqlDbType.Int, parametros[productosEstado4].SqlDbType);
            int rotacion = Array.IndexOf(RepositorioIndicadoresStockSql.COLUMNAS, "Rotacion");
            Assert.AreEqual(SqlDbType.Decimal, parametros[rotacion].SqlDbType);
        }

        [TestMethod]
        public void Guardar_BorraElMesAntesDeInsertar()
        {
            Assert.AreEqual("DELETE FROM dbo.IndicadoresStock WHERE Mes = @p0", RepositorioIndicadoresStockSql.SQL_BORRAR_MES);
        }
    }
}
