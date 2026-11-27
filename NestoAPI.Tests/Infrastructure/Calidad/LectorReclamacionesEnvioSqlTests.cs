using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Almacen;
using NestoAPI.Infraestructure.Calidad.PedidoPerfecto;
using NestoAPI.Infraestructure.Calidad.Reclamaciones;
using System;
using System.Data;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914 (27/11/26): la lectura común del error de envío reclamado con resultados simulados, y la forma de su SQL.</summary>
    [TestClass]
    public class LectorReclamacionesEnvioSqlTests
    {
        private static DataTable Posteriores(string nombre)
        {
            var t = new DataTable(nombre);
            foreach (var c in new (string, Type)[] { ("EnRectificativa", typeof(bool)), ("FacturaRectificativa", typeof(string)), ("Cliente", typeof(string)),
                ("Pedido", typeof(int)), ("Linea", typeof(int)), ("Fecha", typeof(DateTime)), ("Producto", typeof(string)), ("Cantidad", typeof(int)),
                ("BaseImponible", typeof(decimal)), ("Grupo", typeof(string)), ("SubGrupo", typeof(string)), ("Familia", typeof(string)),
                ("PrecioTarifa", typeof(decimal)), ("Oferta", typeof(int)), ("EsGanavisiones", typeof(bool)), ("EsRegaloPorImporte", typeof(bool)),
                ("NotaEntrega", typeof(bool)), ("PedidoOrigen", typeof(int)) })
            {
                _ = t.Columns.Add(c.Item1, c.Item2);
            }
            return t;
        }

        internal static DataSet Resultados()
        {
            var ds = new DataSet();
            var servidas = new DataTable("Servidas");
            foreach (var c in new (string, Type)[] { ("Pedido", typeof(int)), ("Linea", typeof(int)), ("Producto", typeof(string)), ("Cliente", typeof(string)),
                ("FechaAlbaran", typeof(DateTime)), ("Factura", typeof(string)), ("Cantidad", typeof(int)), ("BaseImponible", typeof(decimal)),
                ("Grupo", typeof(string)), ("SubGrupo", typeof(string)), ("Familia", typeof(string)), ("PrecioTarifa", typeof(decimal)), ("Oferta", typeof(int)) })
            {
                _ = servidas.Columns.Add(c.Item1, c.Item2);
            }
            _ = servidas.Rows.Add(931000, 5551, "17404", "15191", new DateTime(2026, 9, 1), "NV26/12345", 2, 30m, "COS", "COS", "Eva Visnu", 15m, DBNull.Value);
            ds.Tables.Add(servidas);

            var rect = new DataTable("Rectificaciones");
            _ = rect.Columns.Add("Pedido", typeof(int));
            _ = rect.Columns.Add("Linea", typeof(int));
            _ = rect.Columns.Add("FacturaRectificativa", typeof(string));
            _ = rect.Columns.Add("Fecha", typeof(DateTime));
            _ = rect.Columns.Add("Cantidad", typeof(decimal));
            _ = rect.Rows.Add(931000, 5551, "RV26/00010", new DateTime(2026, 9, 10), 1m);
            ds.Tables.Add(rect);

            DataTable mismo = Posteriores("Mismo");
            _ = mismo.Rows.Add(false, DBNull.Value, "15191", 931500, 1, new DateTime(2026, 9, 5), "17404", 1, 0m, "COS", "COS", "Eva Visnu", 15m, DBNull.Value, false, false, true, DBNull.Value);
            ds.Tables.Add(mismo);

            DataTable otros = Posteriores("Otros");
            _ = otros.Rows.Add(true, "RV26/00010", "15191", 931600, 2, new DateTime(2026, 9, 10), "17405", 1, 16m, "COS", "COS", "Eva Visnu", 16m, 3, true, true, false, 931000);
            ds.Tables.Add(otros);
            return ds;
        }

        [TestMethod]
        public void LeerResultados_LeeLosCuatroEnOrden()
        {
            var destino = new LecturaReclamacionesEnvio();
            using (DataTableReader r = Resultados().CreateDataReader())
            {
                LectorReclamacionesEnvioSql.LeerResultados(r, destino);
            }

            LineaServidaReclamacion s = destino.Servidas.Single();
            Assert.AreEqual(5551, s.Linea);
            Assert.AreEqual(30m, s.BaseImponible);
            Assert.IsNull(s.Oferta);
            Assert.AreEqual("RV26/00010", destino.Rectificaciones.Single().FacturaRectificativa);
            LineaPosteriorReclamacion m = destino.MismoProducto.Single();
            Assert.IsTrue(m.NotaEntrega);
            Assert.IsNull(m.PedidoOrigen);
            LineaPosteriorReclamacion o = destino.OtrosProductos.Single();
            Assert.IsTrue(o.EnRectificativa);
            Assert.AreEqual(3, o.Oferta);
            Assert.IsTrue(o.EsGanavisiones && o.EsRegaloPorImporte);
            Assert.AreEqual(931000, o.PedidoOrigen);
        }

        [TestMethod]
        public void Sql_EntraPorClaveYConNolock_SinTryConvertNiOffset()
        {
            string sql = LectorReclamacionesEnvioSql.SQL_RECLAMACIONES;
            StringAssert.Contains(sql, "y.[Nº Cliente] = k.Cliente AND y.Producto = k.Producto");
            StringAssert.Contains(sql, "x.FacturaOriginalNumero = s.Factura AND x.FacturaOriginalLinea = s.Linea");
            StringAssert.Contains(sql, "@diasReclamacion");
            Assert.IsFalse(sql.Contains("TRY_CONVERT") || sql.Contains("OFFSET") || sql.Contains("STRING_AGG"));
            int joins = sql.Split(new[] { "JOIN LinPedidoVta" }, StringSplitOptions.None).Length - 1;
            int conNolock = sql.Split(new[] { "JOIN LinPedidoVta y WITH (NOLOCK)", "JOIN LinPedidoVta l WITH (NOLOCK)" }, StringSplitOptions.None).Length - 1;
            Assert.AreEqual(joins, conNolock);
        }

        [TestMethod]
        public void LosDosLectores_LlevanLaMismaLecturaYSuTablaDeEntrada()
        {
            StringAssert.Contains(LectorCalidadAlmacenSql.Lote(true, true), LectorReclamacionesEnvioSql.SQL_RECLAMACIONES);
            StringAssert.Contains(LectorCalidadAlmacenSql.Lote(true, true), "INTO #s0 FROM #l l");
            string pedidoPerfecto = LectorPedidoPerfectoSql.Lote(true, true, true, true, true);
            StringAssert.Contains(pedidoPerfecto, LectorReclamacionesEnvioSql.SQL_RECLAMACIONES);
            StringAssert.Contains(pedidoPerfecto, "INTO #s0");
            // Antes de soltar las temporales de las que parte
            Assert.IsTrue(pedidoPerfecto.IndexOf(LectorReclamacionesEnvioSql.SQL_RECLAMACIONES, StringComparison.Ordinal)
                < pedidoPerfecto.LastIndexOf("DROP TABLE #p;", StringComparison.Ordinal));
        }
    }
}
