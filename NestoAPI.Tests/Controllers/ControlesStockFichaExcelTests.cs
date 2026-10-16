using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#628 (ficha = Excel, Carlos 16/10/26): «Nesto y el Excel deben sugerir lo mismo». En ALG, la ficha del producto
    /// (GET api/ControlesStock) sugiere el máximo y el mínimo del motor de las propuestas de stock, con su frase; en las tiendas,
    /// la media de 24 meses de siempre, con la frase de cómo sale.
    /// </summary>
    [TestClass]
    public class ControlesStockFichaExcelTests
    {
        private const string PRODUCTO = "42274";
        private static readonly DateTime HOY = new DateTime(2026, 10, 16);
        private static readonly DateTime MES_REFERENCIA = new DateTime(2026, 9, 1); // el último cerrado

        // 42274 en septiembre de 2026 (EntradasJson real de PropuestasStock)
        private static readonly int[] VENTAS_42274 = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 2, 7, 0, 0, 0, 1, 2, 5, 2, 0, 0, 1, 1, 0, 3, 0, 5, 4, 2,
            2, 0, 3, 1, 2, 1, 0, 0, 1, 2, 0, 1, 1, 0, 0, 0, 0, 5, 0, 0, 1, 0, 2, 3, 1, 2, 0, 2 };
        private static readonly int[] STOCK_ULTIMOS_12 = { 240, 259, 259, 259, 258, 258, 256, 253, 252, 250, 250, 248 };
        private static readonly decimal[] INDICES_APRAISE = { 0.8930936613m, 1.2992746768m, 0.9801324503m, 1.1542100284m, 0.9649952696m,
            0.8325449385m, 1.0343740145m, 0.4213181962m, 1.4985808893m, 1.0444654683m, 1.0305897193m, 0.8464206875m };

        private IRepositorioTendencias repositorio;
        private ParametrosTendencias parametros;
        private List<Exception> errores;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioTendencias>();
            parametros = new ParametrosTendencias();
            errores = new List<Exception>();
            _ = A.CallTo(() => repositorio.LeerProducto(PRODUCTO)).ReturnsLazily(() => Ficha42274());
            _ = A.CallTo(() => repositorio.CompletarLote(A<IList<DatosProductoTendencia>>._, A<DateTime>._, A<int>._, A<int>._))
                .Invokes((IList<DatosProductoTendencia> lote, DateTime mes, int fase, int retraso) => Completar42274(lote[0]));
            _ = A.CallTo(() => repositorio.LeerVentasFamilia("Apraise", MES_REFERENCIA)).ReturnsLazily(() => SerieFamiliaApraise());
        }

        private ServicioPropuestaStockProducto Servicio()
            => new ServicioPropuestaStockProducto(repositorio, () => parametros, () => HOY, new MemoryCache("test-" + Guid.NewGuid()));

        #region Controlador

        [TestMethod]
        public async Task GetControlStock_EnAlg_DevuelveLoMismoQueElMotorDelExcel()
        {
            ControlStockProductoModel r = await Leer(Servicio());

            // Lo que sale en el Excel: el motor con los mismos datos
            DatosProductoTendencia d = Ficha42274();
            Completar42274(d);
            decimal[] indices = ServicioPropuestasStock.IndicesDeSerie(SerieFamiliaApraise(), MES_REFERENCIA, parametros);
            ResultadoTendencia excel = MotorTendencias.Calcular(d, indices, MES_REFERENCIA, parametros);

            ControlStockAlmacenModel alg = r.ControlesStocksAlmacen.Single(c => c.Almacen == Constantes.Almacenes.ALGETE);
            Assert.AreEqual(excel.StockMaximoPropuesto, alg.StockMaximoCalculado);
            Assert.AreEqual(excel.StockMinimoPropuesto, r.StockMinimoCalculado);
            Assert.AreEqual(excel.Explicacion, alg.Explicacion);
            Assert.AreEqual(excel.Formula, alg.Formula);
            Assert.AreEqual(excel.Accion, alg.Accion);
            Assert.IsTrue(alg.CalculadoComoExcel);
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public async Task GetControlStock_Caso42274_MinimoDosMaximoTresEnAlg()
        {
            // Septiembre de 2026: ponderada 1,44 × 1,04 estacional, múltiplo 12, proveedor 20 días + 7 de seguridad.
            // La fórmula de siempre (media de 24 meses) sugería otra cosa; el Excel, mínimo 2 y máximo 3 (sin el múltiplo).
            ControlStockProductoModel r = await Leer(Servicio());

            ControlStockAlmacenModel alg = r.ControlesStocksAlmacen.Single(c => c.Almacen == Constantes.Almacenes.ALGETE);
            Assert.AreEqual(2, r.StockMinimoCalculado);
            Assert.AreEqual(3, alg.StockMaximoCalculado);
            Assert.AreEqual(AccionesPropuesta.AJUSTAR, alg.Accion);
            StringAssert.Contains(alg.Explicacion, "máximo 3, mínimo 2 (hoy 2/1)");
            StringAssert.Contains(alg.Explicacion, "múltiplos de 12 (se respetan al pedir, no en el máximo)");
            StringAssert.Contains(alg.Explicacion, "1,04 estacional");
        }

        [TestMethod]
        public async Task GetControlStock_EnLasTiendas_MediaDe24MesesConSuExplicacion()
        {
            ControlStockProductoModel r = await Leer(Servicio());

            ControlStockAlmacenModel rei = r.ControlesStocksAlmacen.Single(c => c.Almacen == Constantes.Almacenes.REINA);
            Assert.AreEqual(rei.StockMaximoFormulaClasica, rei.StockMaximoCalculado);
            Assert.AreEqual(ControlStockBase.FORMULA_CLASICA, rei.Formula);
            Assert.IsNull(rei.Accion);
            Assert.IsFalse(rei.CalculadoComoExcel);
            // 24 uds en 24 meses en REI: 1 ud/mes; 7 + 2 × 20 = 47 días → 1/30 × 47 = 1,57 → 2
            Assert.AreEqual(2, rei.StockMaximoCalculado);
            Assert.AreEqual("Media de 24 meses en REI: 24 uds, 1,0 uds/mes; seguridad 7 + 2 × 20 días del proveedor = 47 días → máximo 1,0 × 47 ÷ 30 = 1,57, redondeado hacia arriba 2.",
                rei.Explicacion);
        }

        [TestMethod]
        public async Task GetControlStock_SiElMotorFalla_FormulaDeSiempreConAvisoYAElmah()
        {
            IServicioPropuestaStockProducto falla = A.Fake<IServicioPropuestaStockProducto>();
            _ = A.CallTo(() => falla.Calcular(PRODUCTO)).Throws(new TimeoutException("tiempo de espera agotado"));

            ControlStockProductoModel r = await Leer(falla);

            ControlStockAlmacenModel alg = r.ControlesStocksAlmacen.Single(c => c.Almacen == Constantes.Almacenes.ALGETE);
            Assert.AreEqual(alg.StockMaximoFormulaClasica, alg.StockMaximoCalculado);
            Assert.AreEqual(r.StockMinimoFormulaClasica, r.StockMinimoCalculado);
            Assert.IsFalse(alg.CalculadoComoExcel);
            Assert.IsNull(alg.Accion);
            StringAssert.StartsWith(alg.Explicacion, "Fórmula de siempre (no se ha podido calcular como en el Excel de propuestas). Media de");
            StringAssert.Contains(alg.Explicacion, "Mínimo: media de 24 meses de toda la empresa");
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "42274");
        }

        [TestMethod]
        public async Task GetControlStock_ProductoFueraDelExcel_FormulaDeSiempreYLoDice()
        {
            IServicioPropuestaStockProducto fuera = A.Fake<IServicioPropuestaStockProducto>();
            _ = A.CallTo(() => fuera.Calcular(PRODUCTO)).Returns(null);

            ControlStockProductoModel r = await Leer(fuera);

            ControlStockAlmacenModel alg = r.ControlesStocksAlmacen.Single(c => c.Almacen == Constantes.Almacenes.ALGETE);
            Assert.AreEqual(alg.StockMaximoFormulaClasica, alg.StockMaximoCalculado);
            StringAssert.StartsWith(alg.Explicacion, "Fórmula de siempre (este producto no entra en las propuestas de stock).");
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public async Task GetControlStock_MotorSinMaximos_SugiereCeroYExplicaLaAccion()
        {
            IServicioPropuestaStockProducto liquidar = A.Fake<IServicioPropuestaStockProducto>();
            _ = A.CallTo(() => liquidar.Calcular(PRODUCTO)).Returns(new ResultadoTendencia
            {
                Producto = PRODUCTO,
                Accion = AccionesPropuesta.ESTADO_1,
                Formula = "Declive",
                StockMaximoPropuesto = null,
                StockMinimoPropuesto = null,
                Explicacion = "Pasar a estado 1 (sobre pedido, sin máximos): declive confirmado."
            });

            ControlStockProductoModel r = await Leer(liquidar);

            ControlStockAlmacenModel alg = r.ControlesStocksAlmacen.Single(c => c.Almacen == Constantes.Almacenes.ALGETE);
            Assert.AreEqual(0, alg.StockMaximoCalculado);
            Assert.AreEqual(0, r.StockMinimoCalculado);
            Assert.AreEqual(AccionesPropuesta.ESTADO_1, alg.Accion);
            Assert.AreEqual("Pasar a estado 1 (sobre pedido, sin máximos): declive confirmado.", alg.Explicacion);
        }

        #endregion

        #region Servicio

        [TestMethod]
        public void Servicio_LaEstacionalidadDeLaFamiliaSeSumaUnaVezPorMes()
        {
            ServicioPropuestaStockProducto servicio = Servicio();

            _ = servicio.Calcular(PRODUCTO);
            ResultadoTendencia r = servicio.Calcular(PRODUCTO);

            Assert.AreEqual(3, r.StockMaximoPropuesto);
            A.CallTo(() => repositorio.LeerVentasFamilia(A<string>._, A<DateTime>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.CompletarLote(A<IList<DatosProductoTendencia>>.That.Matches(l => l.Count == 1), MES_REFERENCIA, 12, 2))
                .MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public void Servicio_ProductoQueNoEntra_DevuelveNullSinLeerSeries()
        {
            _ = A.CallTo(() => repositorio.LeerProducto("MTP01")).Returns(null);

            Assert.IsNull(Servicio().Calcular("MTP01"));
            A.CallTo(() => repositorio.CompletarLote(A<IList<DatosProductoTendencia>>._, A<DateTime>._, A<int>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Repositorio_SerieDeFamilia_RecortaACeroCadaProductoYMesAntesDeSumar()
        {
            DateTime desde = MES_REFERENCIA.AddMonths(1).AddMonths(-DatosProductoTendencia.MESES_SERIE);
            List<RepositorioTendenciasSql.FilaMes> filas = new List<RepositorioTendenciasSql.FilaMes>
            {
                new RepositorioTendenciasSql.FilaMes { Producto = "1", Anno = 2026, Mes = 9, Cantidad = 10 },
                new RepositorioTendenciasSql.FilaMes { Producto = "2", Anno = 2026, Mes = 9, Cantidad = -4 }, // más devoluciones que ventas: 0
                new RepositorioTendenciasSql.FilaMes { Producto = "2", Anno = 2026, Mes = 8, Cantidad = 3 },
                new RepositorioTendenciasSql.FilaMes { Producto = "3", Anno = 2021, Mes = 9, Cantidad = 99 }  // antes de la serie
            };

            int[] serie = RepositorioTendenciasSql.SumarVentasFamilia(filas, desde);

            Assert.AreEqual(10, serie[DatosProductoTendencia.ULTIMO]);
            Assert.AreEqual(3, serie[DatosProductoTendencia.ULTIMO - 1]);
            Assert.AreEqual(13, serie.Sum());
        }

        [TestMethod]
        public void Repositorio_ConsultaDeUnProducto_EsLaDeTodosConElFiltroDelProducto()
        {
            StringAssert.Contains(RepositorioTendenciasSql.SQL_PRODUCTO, DemandaStock.SQL_FILTRO_PRODUCTO + " AND p.Número = @producto");
            Assert.IsFalse(RepositorioTendenciasSql.SQL_PRODUCTO.Contains("ORDER BY p.Número"));
            StringAssert.EndsWith(RepositorioTendenciasSql.SQL_PRODUCTOS, "ORDER BY p.Número");
            string familia = string.Format(RepositorioTendenciasSql.SQL_VENTAS_MES, RepositorioTendenciasSql.SQL_PRODUCTOS_FAMILIA);
            StringAssert.Contains(familia, "l.Producto IN (SELECT p.Número FROM Productos p WITH (NOLOCK) WHERE " + DemandaStock.SQL_FILTRO_PRODUCTO + " AND p.Familia = @familia)");
        }

        #endregion

        #region Datos

        private async Task<ControlStockProductoModel> Leer(IServicioPropuestaStockProducto servicio)
        {
            NVEntities db = A.Fake<NVEntities>();
            Configurar(db, () => db.ControlesStocks, new List<ControlStock>
            {
                new ControlStock { Empresa = "1", Número = PRODUCTO, Almacén = Constantes.Almacenes.ALGETE, StockMáximo = 2, StockMínimo = 1, Múltiplos = 12 },
                new ControlStock { Empresa = "1", Número = PRODUCTO, Almacén = Constantes.Almacenes.REINA, StockMáximo = 1, StockMínimo = 0, Múltiplos = 1 }
            });
            Configurar(db, () => db.ProveedoresProductoes, new List<ProveedoresProducto>
            {
                new ProveedoresProducto { Empresa = "1", Nº_Producto = PRODUCTO, Orden = 1, Proveedore = new Proveedor { DíasEnServir = 20 } }
            });
            List<LinPedidoVta> lineas = new List<LinPedidoVta>();
            for (int i = 1; i <= 24; i++)
            {
                lineas.Add(new LinPedidoVta { Producto = PRODUCTO, Almacén = Constantes.Almacenes.ALGETE, Cantidad = 2, Fecha_Factura = DateTime.Today.AddMonths(-i).AddDays(5) });
                lineas.Add(new LinPedidoVta { Producto = PRODUCTO, Almacén = Constantes.Almacenes.REINA, Cantidad = 1, Fecha_Factura = DateTime.Today.AddMonths(-i).AddDays(5) });
            }
            Configurar(db, () => db.LinPedidoVtas, lineas);
            Configurar(db, () => db.ExtractosProducto, Constantes.Sedes.ListaSedes
                .Select(a => new ExtractoProducto { Número = PRODUCTO, Almacén = a, Cantidad = 50, Fecha = new DateTime(2022, 1, 10) })
                .ToList());

            ControlesStockController controller = new ControlesStockController(db, servicio, errores.Add);
            var resultado = await controller.GetControlStock(PRODUCTO) as OkNegotiatedContentResult<ControlStockProductoModel>;
            Assert.IsNotNull(resultado);
            return resultado.Content;
        }

        private static void Configurar<T>(NVEntities db, System.Linq.Expressions.Expression<Func<DbSet<T>>> propiedad, List<T> datos) where T : class
        {
            DbSet<T> conjunto = A.Fake<DbSet<T>>(o => o.Implements(typeof(IQueryable<T>)).Implements(typeof(IDbAsyncEnumerable<T>)));
            IQueryable<T> consulta = datos.AsQueryable();
            _ = A.CallTo(() => ((IDbAsyncEnumerable<T>)conjunto).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(consulta.GetEnumerator()));
            _ = A.CallTo(() => ((IQueryable<T>)conjunto).Provider).Returns(new TestDbAsyncQueryProvider<T>(consulta.Provider));
            _ = A.CallTo(() => ((IQueryable<T>)conjunto).Expression).Returns(consulta.Expression);
            _ = A.CallTo(() => ((IQueryable<T>)conjunto).ElementType).Returns(consulta.ElementType);
            _ = A.CallTo(() => ((IQueryable<T>)conjunto).GetEnumerator()).ReturnsLazily(() => consulta.GetEnumerator());
            _ = A.CallTo(propiedad).Returns(conjunto);
        }

        /// <summary>La fila de LeerProducto del 42274 (ficha, proveedor 689 con 20 días, control de stock de ALG 2/1 y múltiplo 12).</summary>
        private static DatosProductoTendencia Ficha42274() => new DatosProductoTendencia
        {
            Producto = PRODUCTO,
            Nombre = "Producto 42274",
            Familia = "Apraise",
            Grupo = "COS",
            Estado = 0,
            PrecioMedio = 2.3468m,
            Proveedor = "689",
            DiasEnServir = 20,
            StockMaximoAlg = 2,
            StockMinimoAlg = 1,
            MultiplosAlg = 12,
            TipoExclusiva = "NAC"
        };

        private static void Completar42274(DatosProductoTendencia d)
        {
            d.Ventas = (int[])VENTAS_42274.Clone();
            int[] stock = Enumerable.Repeat(STOCK_ULTIMOS_12[0], DatosProductoTendencia.MESES_SERIE).ToArray();
            Array.Copy(STOCK_ULTIMOS_12, 0, stock, DatosProductoTendencia.MESES_SERIE - 12, 12);
            d.StockFinMes = stock;
            d.FechaPrimeraVenta = new DateTime(2022, 9, 8);
            d.FechaPrimeraEntrada = new DateTime(2022, 8, 1);
        }

        /// <summary>Una serie de la familia cuya estacionalidad son (casi exactos) los índices reales de Apraise en septiembre.</summary>
        private static int[] SerieFamiliaApraise()
        {
            int[] serie = new int[DatosProductoTendencia.MESES_SERIE];
            for (int i = 0; i < serie.Length; i++)
            {
                serie[i] = (int)Math.Round(INDICES_APRAISE[CalculadoraSeries.MesDelAnio(MES_REFERENCIA, DatosProductoTendencia.ULTIMO, i)] * 1000m);
            }
            return serie;
        }

        #endregion
    }
}
