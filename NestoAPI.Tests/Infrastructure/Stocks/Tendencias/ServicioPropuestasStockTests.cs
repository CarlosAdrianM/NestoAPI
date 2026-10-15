using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Collections.Generic;
using System.Linq;
using static NestoAPI.Tests.Infrastructure.Stocks.Tendencias.SeriesTendencia;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#630: la pasada completa con el repositorio falso (lotes, familias, errores, orden).</summary>
    [TestClass]
    public class ServicioPropuestasStockTests
    {
        private IRepositorioTendencias repositorio;
        private List<Exception> errores;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioTendencias>();
            errores = new List<Exception>();
        }

        private ServicioPropuestasStock Servicio() => new ServicioPropuestasStock(repositorio, errores.Add);

        [TestMethod]
        public void Calcular_OrdenaPorExcesoYQuitaLosQueNoTienenNada()
        {
            DatosProductoTendencia mucho = Producto("1", ventas: Constante(30), stock: 1000, precio: 2m);
            DatosProductoTendencia poco = Producto("2", ventas: Constante(30), stock: 200, precio: 2m);
            DatosProductoTendencia muerto = Producto("3", ventas: new int[N], stock: 0);
            _ = A.CallTo(() => repositorio.LeerProductos()).Returns(new List<DatosProductoTendencia> { poco, muerto, mucho });

            ResultadoPropuestasStock r = Servicio().Calcular(new DateTime(2026, 9, 17), new ParametrosTendencias());

            Assert.AreEqual(MesReferencia, r.Mes);
            CollectionAssert.AreEqual(new[] { "1", "2" }, r.Productos.Select(p => p.Producto).ToArray());
            Assert.AreEqual(3, r.ProductosLeidos);
            Assert.AreEqual(0, errores.Count);
            A.CallTo(() => repositorio.CompletarLote(A<IList<DatosProductoTendencia>>._, MesReferencia, 12)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Calcular_UnLoteQueFalla_SeReintentaProductoAProductoYSoloSePierdeElQueFalla()
        {
            List<DatosProductoTendencia> productos = Enumerable.Range(1, RepositorioTendenciasSql.TAMANO_LOTE + 5)
                .Select(i => Producto(i.ToString("00000"), stock: 10))
                .ToList();
            _ = A.CallTo(() => repositorio.LeerProductos()).Returns(productos);
            _ = A.CallTo(() => repositorio.CompletarLote(A<IList<DatosProductoTendencia>>.That.Matches(l => l.Count == RepositorioTendenciasSql.TAMANO_LOTE), A<DateTime>._, A<int>._))
                .Throws(new TimeoutException("tiempo de espera agotado"));
            _ = A.CallTo(() => repositorio.CompletarLote(A<IList<DatosProductoTendencia>>.That.Matches(l => l.Count == 1 && l[0].Producto == "00007"), A<DateTime>._, A<int>._))
                .Throws(new TimeoutException("tiempo de espera agotado"));

            ResultadoPropuestasStock r = Servicio().Calcular(MesReferencia, new ParametrosTendencias());

            Assert.AreEqual(1, r.LotesConError);
            Assert.AreEqual(1, r.ProductosConError);
            Assert.AreEqual(RepositorioTendenciasSql.TAMANO_LOTE + 4, r.ProductosLeidos);
            Assert.IsFalse(r.Productos.Any(p => p.Producto == "00007"));
            Assert.AreEqual(2, errores.Count);
            StringAssert.Contains(errores[0].Message, "00001 a 00200");
            StringAssert.Contains(errores[1].Message, "00007");
        }

        [TestMethod]
        public void IndicesPorFamilia_SumaLosProductosDeLaFamilia()
        {
            // Cada producto por separado vende poco (no llega al mínimo de la familia); juntos sí.
            DatosProductoTendencia a = Producto("A", ventas: Constante(2));
            DatosProductoTendencia b = Producto("B", ventas: Constante(2));
            ParametrosTendencias p = new ParametrosTendencias { UnidadesMinimasFamiliaEstacionalidad = 100 };

            Dictionary<string, decimal[]> solo = ServicioPropuestasStock.IndicesPorFamilia(new[] { a }, MesReferencia, p);
            Dictionary<string, decimal[]> juntos = ServicioPropuestasStock.IndicesPorFamilia(new[] { a, b }, MesReferencia, p);

            Assert.IsNull(solo["Genéricos"]);
            Assert.IsNotNull(juntos["Genéricos"]);
            Assert.AreEqual(1m, juntos["Genéricos"][0]);
        }
    }

    /// <summary>NestoAPI#630: lo puro del repositorio SQL (sin BD).</summary>
    [TestClass]
    public class RepositorioTendenciasSqlTests
    {
        [TestMethod]
        public void AcumularStock_SaldoInicialMasMovimientos()
        {
            int[] movimientos = new int[N];
            movimientos[0] = 10;
            movimientos[FIN] = -4;

            int[] stock = RepositorioTendenciasSql.AcumularStock(5, movimientos);

            Assert.AreEqual(15, stock[0]);
            Assert.AreEqual(15, stock[FIN - 1]);
            Assert.AreEqual(11, stock[FIN]);
            Assert.AreEqual(7, RepositorioTendenciasSql.AcumularStock(7, null)[FIN]);
        }

        [TestMethod]
        public void Indice_DelMesEnLaSerie()
        {
            DateTime desde = MesReferencia.AddMonths(1 - N); // octubre de 2021

            Assert.AreEqual(0, RepositorioTendenciasSql.Indice(2021, 10, desde));
            Assert.AreEqual(FIN, RepositorioTendenciasSql.Indice(2026, 9, desde));
        }

        [TestMethod]
        public void TablaPropuestas_LasColumnasSonLasDeLaTabla()
        {
            ResultadoTendencia r = MotorTendencias.Calcular(Producto(), null, MesReferencia, new ParametrosTendencias());

            System.Data.DataTable tabla = RepositorioTendenciasSql.TablaPropuestas(MesReferencia, new[] { r }, DateTime.Now);

            Assert.AreEqual(1, tabla.Rows.Count);
            foreach (System.Data.DataColumn columna in tabla.Columns)
            {
                StringAssert.Contains(RepositorioTendenciasSql.SQL_LEER, columna.ColumnName);
            }
            Assert.AreEqual("17404", tabla.Rows[0]["Producto"]);
            Assert.AreEqual(DBNull.Value, tabla.Rows[0]["Velocidad"]);
        }

        [TestMethod]
        public void CopiaMasivaPropuestas_CompruebaRestriccionesYDisparaTriggers_ParaNoNecesitarAlterTable()
        {
            // NestoAPI#637: con las opciones por defecto, SQL Server exige ALTER TABLE para saltarse
            // CK_PropuestasStock_Fase, y el 15/10/26 las propuestas de 2026-09 no se guardaron.
            Assert.IsTrue(RepositorioTendenciasSql.OPCIONES_COPIA_PROPUESTAS.HasFlag(System.Data.SqlClient.SqlBulkCopyOptions.CheckConstraints));
            Assert.IsTrue(RepositorioTendenciasSql.OPCIONES_COPIA_PROPUESTAS.HasFlag(System.Data.SqlClient.SqlBulkCopyOptions.FireTriggers));
            Assert.IsFalse(RepositorioTendenciasSql.OPCIONES_COPIA_PROPUESTAS.HasFlag(System.Data.SqlClient.SqlBulkCopyOptions.KeepIdentity),
                "KeepIdentity también exige ALTER TABLE, y el Id es IDENTITY");
        }

        [TestMethod]
        public void Consultas_ConNolockYConLaDemandaDeLaEpica()
        {
            foreach (string sql in new[] { RepositorioTendenciasSql.SQL_VENTAS_MES, RepositorioTendenciasSql.SQL_PRIMERA_VENTA, RepositorioTendenciasSql.SQL_VENTAS_CLIENTE })
            {
                StringAssert.Contains(sql, "WITH (NOLOCK)");
                StringAssert.Contains(sql, DemandaStock.SQL_FILTRO_DEMANDA);
            }
            StringAssert.Contains(RepositorioTendenciasSql.SQL_STOCK_MES, DemandaStock.SQL_FILTRO_STOCK);
            StringAssert.Contains(RepositorioTendenciasSql.SQL_PRODUCTOS, DemandaStock.SQL_FILTRO_PRODUCTO);
        }
    }

    /// <summary>
    /// NestoAPI#630: el repositorio de verdad contra la BD (categoría «Integracion», solo con NESTO_TEST_BD), acotado a DOS
    /// productos (17404, maduro con bajón puntual; 45958, nuevo con pendientes). Solo SELECT con NOLOCK.
    /// </summary>
    [TestClass]
    public class RepositorioTendenciasSqlIntegracionTests
    {
        [TestMethod]
        [TestCategory("Integracion")]
        public void CompletarLote_DosProductos_SeriesYPropuesta()
        {
            RepositorioTendenciasSql repositorio = new RepositorioTendenciasSql(() => NestoAPI.Tests.Helpers.BaseDeDatosDeIntegracion.AbrirNVEntities());
            List<DatosProductoTendencia> lote = new List<DatosProductoTendencia>
            {
                new DatosProductoTendencia { Producto = "17404", Familia = "Genéricos", Estado = 0, PrecioMedio = 4.25m, DiasEnServir = 10, MultiplosAlg = 144, StockMaximoAlg = 555, StockMinimoAlg = 295 },
                new DatosProductoTendencia { Producto = "45958", Familia = "Genéricos", Estado = 0, PrecioMedio = 7.3m, DiasEnServir = 15, MultiplosAlg = 1, StockMaximoAlg = 54, StockMinimoAlg = 30 }
            };

            repositorio.CompletarLote(lote, MesReferencia, 12);

            DatosProductoTendencia maduro = lote[0];
            Assert.IsTrue(maduro.Ventas.Sum() > 10000, "17404 vende miles al año");
            Assert.IsTrue(maduro.FechaPrimeraVenta < new DateTime(2010, 1, 1));
            ResultadoTendencia r = MotorTendencias.Calcular(maduro, null, MesReferencia, new ParametrosTendencias());
            Assert.AreEqual(FasesProducto.MADURO, r.Fase);
            Assert.IsFalse(string.IsNullOrEmpty(r.Explicacion));
            Console.WriteLine($"17404: stock {maduro.Stock}, ventas 12 m {r.Ventas12Meses}, cobertura {r.CoberturaMeses}, pendiente {r.PendienteRelativa}, " +
                $"trimestre {r.RatioTrimestre}, pico {r.PorcentajeDelPico} % ({r.AnioPico}), tendencia {r.Tendencia}, acción {r.Accion}");
            Console.WriteLine(r.Explicacion);

            DatosProductoTendencia nuevo = lote[1];
            ResultadoTendencia n = MotorTendencias.Calcular(nuevo, null, new DateTime(2026, 10, 1), new ParametrosTendencias());
            Console.WriteLine($"45958 (octubre): pendientes {nuevo.UnidadesPendientes}, compra abierta {nuevo.UnidadesPedidoCompraAbierto}, fase {n.Fase}, acción {n.Accion}");
            Console.WriteLine(n.Explicacion);
            Assert.IsTrue(nuevo.UnidadesPendientes > 0);
        }
    }
}
