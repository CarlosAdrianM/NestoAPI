using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas.Agrupacion;

namespace NestoAPI.Tests.Infrastructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#729: el SQL con el que se mueven las líneas al agrupar (lo mismo que hace prdAgruparAlbaranesVta). El 29/10/26
    /// el motor cambiaba <c>Número</c> en las entidades de EF y la facturación de rutas se caía entera.
    /// </summary>
    [TestClass]
    public class PersistenciaMovimientoLineasSqlTests
    {
        [TestMethod]
        public void SqlMoverLineas_MueveLineasEnAlbaranYRepuntaSatelites()
        {
            string sql = PersistenciaMovimientoLineasSql.SqlMoverLineas(3);

            StringAssert.Contains(sql, "UPDATE LinPedidoVta SET Número = @destino WHERE Empresa = @empresa AND Estado = 2 AND [Nº Orden] IN (@o0, @o1, @o2);");
            StringAssert.Contains(sql, "UPDATE PedidosEspeciales SET NºPedidoVta = @destino WHERE Empresa = @empresa AND NºOrdenVta IN (@o0, @o1, @o2);");
            StringAssert.Contains(sql, "UPDATE Ubicaciones SET PedidoVta = @destino WHERE Empresa = @empresa AND NºOrdenVta IN (@o0, @o1, @o2);");
            Assert.IsFalse(sql.Contains("DELETE"), "no borra cabeceras: eso no es cosa del motor");
        }

        [TestMethod]
        public void SqlMarcarAgrupada_SoloElDestino()
        {
            Assert.AreEqual("UPDATE CabPedidoVta SET Agrupada = 1 WHERE Empresa = @empresa AND Número = @destino;",
                PersistenciaMovimientoLineasSql.SQL_MARCAR_AGRUPADA);
        }

        [TestMethod]
        public void Lotes_QuitaRepetidasYTroceaPorTamano()
        {
            List<int> ordenes = Enumerable.Range(1, PersistenciaMovimientoLineasSql.TAMANO_LOTE + 2).ToList();
            ordenes.Add(1); // repetida

            List<List<int>> lotes = PersistenciaMovimientoLineasSql.Lotes(ordenes).ToList();

            Assert.AreEqual(2, lotes.Count);
            Assert.AreEqual(PersistenciaMovimientoLineasSql.TAMANO_LOTE, lotes[0].Count);
            Assert.AreEqual(2, lotes[1].Count);
            Assert.AreEqual(0, PersistenciaMovimientoLineasSql.Lotes(new List<int>()).Count(), "sin órdenes no hay UPDATE de líneas");
        }
    }
}
