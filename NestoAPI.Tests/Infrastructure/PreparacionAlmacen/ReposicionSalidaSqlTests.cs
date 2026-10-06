using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Regresión: el recorrido REPO de Ariadna buscaba el hueco con Ubicaciones.NºOrdenRepo = la línea de SALIDA, pero en
    /// los datos reales (traspaso 80885, 05/10/26: 53 filas en -4) NºOrdenRepo apunta a la línea de ENTRADA, que al cerrar
    /// se va al diario del destino. Resultado: todas las paradas salían sin hueco. Ahora se localiza por NºTraspasoRepo +
    /// producto + almacén de origen, y lo que no tiene registro va en una parada (o pieza) sin hueco.
    /// </summary>
    [TestClass]
    public class ReposicionSalidaSqlTests
    {
        private const string UNION_VIEJA = "u.[NºOrdenRepo] = s.[Nº Orden]";

        [TestMethod]
        public void Recorrido_LocalizaElHuecoPorTraspasoYProducto()
        {
            string sql = RepositorioPreparacionAlmacen.SQL_LINEAS_REPOSICION_SALIDA;

            Assert.IsFalse(sql.Contains(UNION_VIEJA), "NºOrdenRepo es la línea de entrada, no la de salida");
            StringAssert.Contains(sql, "u.[NºTraspasoRepo] = x.[NºTraspaso] AND u.[Número] = x.[Número] AND u.[Almacén] = x.[Almacén]");
            StringAssert.Contains(sql, "u.Estado IN (4, -4)");
        }

        [TestMethod]
        public void Recorrido_LoQueNoTieneRegistroVaSinHueco()
        {
            string sql = RepositorioPreparacionAlmacen.SQL_LINEAS_REPOSICION_SALIDA;

            StringAssert.Contains(sql, "x.Cantidad - ISNULL((SELECT SUM(h.Cantidad) FROM Huecos h WHERE h.[Número] = x.[Número]), 0), NULL, NULL, NULL");
            StringAssert.Contains(sql, "HAVING SUM(t.Cantidad) > 0");
            StringAssert.Contains(sql, "s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0");
        }

        [TestMethod]
        public void Piezas_LocalizanElRegistroPorTraspasoYProductoYElRestoSinHueco()
        {
            string sql = TransaccionSalidaSql.SQL_PIEZAS_REPOSICION;

            Assert.IsFalse(sql.Contains(UNION_VIEJA), "NºOrdenRepo es la línea de entrada, no la de salida");
            StringAssert.Contains(sql, "u.[NºTraspasoRepo] = s.[NºTraspaso] AND u.[Número] = s.[Número] AND u.[Almacén] = s.[Almacén]");
            StringAssert.Contains(sql, "UNION ALL");
            StringAssert.Contains(sql, "-s.Cantidad > ISNULL(r.Cantidad, 0)");
            StringAssert.Contains(sql, "WITH (UPDLOCK)");
        }
    }
}
