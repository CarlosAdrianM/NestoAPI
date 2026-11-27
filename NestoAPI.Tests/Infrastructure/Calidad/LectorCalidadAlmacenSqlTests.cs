using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Almacen;
using System;
using System.Data;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914 corte 2: el lector de Almacén con resultados simulados (los del lote, en su orden) y la forma del SQL.</summary>
    [TestClass]
    public class LectorCalidadAlmacenSqlTests
    {
        private static DataTable Tabla(string nombre, params (string Nombre, Type Tipo)[] columnas)
        {
            var t = new DataTable(nombre);
            foreach ((string Nombre, Type Tipo) c in columnas)
            {
                _ = t.Columns.Add(c.Nombre, c.Tipo);
            }
            return t;
        }

        private static DataSet Resultados(bool noCasadas)
        {
            var ds = new DataSet();
            DataTable pickings = Tabla("Pickings", ("Picking", typeof(int)), ("Fecha", typeof(DateTime)), ("TerminadoPor", typeof(string)), ("TerminadoEl", typeof(DateTime)));
            _ = pickings.Rows.Add(100, new DateTime(2026, 11, 20, 8, 5, 0), @"NUEVAVISION\Alfredo", new DateTime(2026, 11, 20, 9, 0, 0));
            _ = pickings.Rows.Add(101, new DateTime(2026, 11, 20, 10, 0, 0), DBNull.Value, DBNull.Value);
            ds.Tables.Add(pickings);

            DataTable lecturas = Tabla("Picking", ("Picking", typeof(int)), ("Producto", typeof(string)), ("Usuario", typeof(string)),
                ("Cogidas", typeof(int)), ("Faltas", typeof(int)), ("Ultima", typeof(DateTime)));
            _ = lecturas.Rows.Add(100, "17404", "Alfredo", 2, 0, new DateTime(2026, 11, 20, 8, 30, 0));
            _ = lecturas.Rows.Add(101, "45958", "Pedro", 0, 1, new DateTime(2026, 11, 20, 10, 5, 0));
            ds.Tables.Add(lecturas);

            DataTable packing = Tabla("Packing", ("Picking", typeof(int)), ("Pedido", typeof(int)), ("Producto", typeof(string)), ("Usuario", typeof(string)),
                ("Metidas", typeof(int)), ("Faltas", typeof(int)));
            _ = packing.Rows.Add(100, 931000, "17404", "Santiago", 1, 1);
            ds.Tables.Add(packing);

            DataTable lineas = Tabla("Lineas", ("Picking", typeof(int)), ("Pedido", typeof(int)), ("Linea", typeof(int)), ("Producto", typeof(string)),
                ("Cantidad", typeof(int)), ("Estado", typeof(short)), ("Factura", typeof(string)), ("Almacen", typeof(string)), ("Cliente", typeof(string)),
                ("Contacto", typeof(string)), ("Rectificadas", typeof(decimal)));
            _ = lineas.Rows.Add(100, 931000, 5551, "17404", 2, (short)4, "NV26/12345", "ALG", "15191", "0", 1m);
            ds.Tables.Add(lineas);

            if (noCasadas)
            {
                DataTable nc = Tabla("NoCasadas", ("Picking", typeof(int)), ("Usuario", typeof(string)), ("Pantalla", typeof(string)), ("Lecturas", typeof(int)));
                _ = nc.Rows.Add(100, "Alfredo", "Recorrido", 3);
                ds.Tables.Add(nc);
            }

            DataTable sin = Tabla("SinLecturas", ("Picking", typeof(int)), ("Pedidos", typeof(int)), ("Lineas", typeof(int)), ("Almacen", typeof(string)));
            _ = sin.Rows.Add(99, 2, 5, "REI");
            ds.Tables.Add(sin);
            return ds;
        }

        [TestMethod]
        public void LeerTanda_ConNoCasadas_LeeLosSeisResultadosEnOrden()
        {
            var lectura = new LecturaCalidadAlmacen();
            using (DataTableReader r = Resultados(noCasadas: true).CreateDataReader())
            {
                LectorCalidadAlmacenSql.LeerTanda(r, true, lectura);
            }

            Assert.AreEqual(2, lectura.Pickings.Count);
            Assert.AreEqual(@"NUEVAVISION\Alfredo", lectura.Pickings[0].TerminadoPor);
            Assert.IsNull(lectura.Pickings[1].TerminadoEl);
            Assert.AreEqual(1, lectura.LecturasPicking.Single(l => l.Picking == 101).Faltas);
            Assert.AreEqual(931000, lectura.LecturasPacking.Single().Pedido);
            LineaPedidoCalidadAlmacen linea = lectura.Lineas.Single();
            Assert.AreEqual(5551, linea.Linea);
            Assert.AreEqual(4, linea.Estado);
            Assert.AreEqual("NV26/12345", linea.Factura);
            Assert.AreEqual(1m, linea.UnidadesRectificadas);
            Assert.AreEqual(3, lectura.NoCasadas.Single().Lecturas);
            Assert.AreEqual("REI", lectura.PickingsSinLecturas.Single().Almacen);
        }

        [TestMethod]
        public void LeerTanda_SinTablaDeNoCasadas_SaltaEseResultado()
        {
            var lectura = new LecturaCalidadAlmacen();
            using (DataTableReader r = Resultados(noCasadas: false).CreateDataReader())
            {
                LectorCalidadAlmacenSql.LeerTanda(r, false, lectura);
            }

            Assert.AreEqual(0, lectura.NoCasadas.Count);
            Assert.AreEqual(99, lectura.PickingsSinLecturas.Single().Picking);
        }

        [TestMethod]
        public void LeerTanda_YLaRegla_FaltaDelPackingYParadaDadaPorFalta()
        {
            var lectura = new LecturaCalidadAlmacen();
            using (DataTableReader r = Resultados(noCasadas: true).CreateDataReader())
            {
                LectorCalidadAlmacenSql.LeerTanda(r, true, lectura);
            }

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            CollectionAssert.Contains(e.Lineas.Single().Indicadores.ToList(), IndicadorAlmacen.FaltaEnPacking);
            CollectionAssert.Contains(e.Lineas.Single().Indicadores.ToList(), IndicadorAlmacen.LineaRectificada);
            CollectionAssert.Contains(e.Paradas.Single(p => p.Picking == 101).Indicadores.ToList(), IndicadorAlmacen.FaltaEnPicking);
        }

        [TestMethod]
        public void Lote_SoloLecturaYSinFuncionesQueLaBdNoTiene()
        {
            string lote = LectorCalidadAlmacenSql.Lote(true, true);

            StringAssert.Contains(lote, "READ UNCOMMITTED");
            Assert.IsTrue(lote.Contains("PreparacionEscaneos e WITH (NOLOCK)"));
            Assert.IsTrue(lote.Contains("LinPedidoVta l WITH (NOLOCK)"));
            foreach (string prohibida in new[] { "TRY_CONVERT", "STRING_SPLIT", "STRING_AGG", "OFFSET", "UPDATE ", "INSERT INTO dbo", "DELETE " })
            {
                Assert.IsFalse(lote.ToUpperInvariant().Contains(prohibida), prohibida);
            }
            // El universo entra por fecha en las lecturas del recorrido, nunca por fecha en LinPedidoVta
            StringAssert.Contains(lote, "e.Fase = 'PICK' AND e.FechaEscaneo >= @desde AND e.FechaEscaneo < @hasta");
            Assert.IsFalse(lote.Contains("l.Fecha"));
        }

        [TestMethod]
        public void LeerEtiquetasPackingIncompleto_PorEmpaquetadorYDesdeCuando()
        {
            var ds = new DataSet();
            DataTable t = Tabla("Etiquetas", ("Usuario", typeof(string)), ("Etiquetas", typeof(int)), ("Confirmadas", typeof(int)), ("SinAviso", typeof(int)));
            _ = t.Rows.Add("Santiago", 3, 2, 1);
            ds.Tables.Add(t);
            DataTable primera = Tabla("Primera", ("Primera", typeof(DateTime)));
            _ = primera.Rows.Add(new DateTime(2026, 11, 28, 9, 0, 0));
            ds.Tables.Add(primera);
            var lectura = new LecturaCalidadAlmacen();

            using (DataTableReader r = ds.CreateDataReader())
            {
                LectorCalidadAlmacenSql.LeerEtiquetasPackingIncompleto(r, lectura);
            }

            Assert.AreEqual(3, lectura.EtiquetasPackingIncompleto.Single().Etiquetas);
            Assert.AreEqual(1, lectura.EtiquetasPackingIncompleto.Single().SinAviso);
            Assert.AreEqual(new DateTime(2026, 11, 28, 9, 0, 0), lectura.PrimeraEtiquetaPackingIncompleto);
            StringAssert.Contains(LectorCalidadAlmacenSql.SQL_ETIQUETAS_PACKING_INCOMPLETO, "e.Detalle LIKE N'%\"Modo\":\"Confirmado\"%'");
        }

        [TestMethod]
        public void Lote_SinTablasOpcionales_NoLasNombra()
        {
            string lote = LectorCalidadAlmacenSql.Lote(false, false);

            Assert.IsFalse(lote.Contains("PreparacionSalidasTerminadas"));
            Assert.IsFalse(lote.Contains("LecturasNoCasadasAlmacen"));
            StringAssert.Contains(lote, "CAST(NULL AS varchar(50)) AS TerminadoPor");
        }
    }
}
