using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Ubicaciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Ubicaciones
{
    /// <summary>
    /// NestoAPI#594 (corte 2): el cuadre de Ubicaciones (estados &gt;= 0) contra el stock de ExtractoProducto.
    /// </summary>
    [TestClass]
    public class CuadreUbicacionesTests
    {
        private static StockExtractoProducto Stock(string producto, int stock) => new StockExtractoProducto { Producto = producto, Stock = stock };

        private static UbicacionesPorEstado Ubi(string producto, int estado, int cantidad) =>
            new UbicacionesPorEstado { Producto = producto, Estado = estado, Cantidad = cantidad, Filas = 1 };

        private static ProductoCuadre Ficha(string producto, bool ubicar = true) => new ProductoCuadre { Producto = producto, Nombre = "Producto " + producto, Ubicar = ubicar };

        private static CuadreUbicacionProducto Uno(IEnumerable<StockExtractoProducto> stocks, IEnumerable<UbicacionesPorEstado> ubicaciones, bool ubicar = true)
        {
            List<CuadreUbicacionProducto> cuadre = CuadreUbicaciones.Cuadrar("ALG", stocks, ubicaciones, new[] { Ficha("23101", ubicar) }, soloDescuadrados: false);
            Assert.AreEqual(1, cuadre.Count);
            return cuadre[0];
        }

        [TestMethod]
        public void Cuadra_SumaDeTodosLosEstadosFisicosIgualAlStock()
        {
            CuadreUbicacionProducto c = Uno(new[] { Stock("23101", 49) },
                new[] { Ubi("23101", 0, 40), Ubi("23101", 2, 1), Ubi("23101", 3, 2), Ubi("23101", 4, 5), Ubi("23101", 5, 1) });

            Assert.IsTrue(c.Cuadra);
            Assert.AreEqual(CuadreUbicaciones.CUADRA, c.Situacion);
            Assert.AreEqual(0, c.Diferencia);
            Assert.AreEqual(49, c.Ubicaciones);
            Assert.AreEqual(40, c.EnHueco);
            Assert.AreEqual(1, c.PendienteDeUbicar);
            Assert.AreEqual(2, c.ReservadoPicking);
            Assert.AreEqual(5, c.ReservadoReposicion);
            Assert.AreEqual(1, c.ReservadoKit);
            Assert.AreEqual("Producto 23101", c.Nombre);
            Assert.AreEqual("ALG", c.Almacen);
        }

        [TestMethod]
        public void ReposicionDeAlgeteSinTerminar_LaReservaEn4Cuadra_En4NegativaNo()
        {
            // NestoAPI#651, 37800 el 15/10/26 (traspaso 81016 por recoger): stock 32 en el extracto, 31 en el hueco y 1 reservado.
            // Con la reserva viva (4) cuadra; con la reserva ya en -4 (lo que hacía la API al crearla) faltaba 1.
            CuadreUbicacionProducto bien = Uno(new[] { Stock("23101", 32) }, new[] { Ubi("23101", 0, 31), Ubi("23101", 4, 1) });
            CuadreUbicacionProducto antes = Uno(new[] { Stock("23101", 32) }, new[] { Ubi("23101", 0, 31), Ubi("23101", -4, -1) });

            Assert.IsTrue(bien.Cuadra);
            Assert.AreEqual(1, bien.ReservadoReposicion);
            Assert.IsFalse(antes.Cuadra);
            Assert.AreEqual(-1, antes.Diferencia);
        }

        [TestMethod]
        public void SobraEnUbicacion_LoDevueltoAlProveedorSeQuedoEnElHueco()
        {
            // El caso del 14/10/26: la devolución bajó el extracto pero no el hueco
            CuadreUbicacionProducto c = Uno(new[] { Stock("23101", 388) }, new[] { Ubi("23101", 0, 513) });

            Assert.IsFalse(c.Cuadra);
            Assert.AreEqual(CuadreUbicaciones.SOBRA_EN_UBICACIONES, c.Situacion);
            Assert.AreEqual(125, c.Diferencia);
        }

        [TestMethod]
        public void FaltaEnUbicacion_ElExtractoTieneMasQueLosHuecos()
        {
            CuadreUbicacionProducto c = Uno(new[] { Stock("23101", 10) }, new[] { Ubi("23101", 0, 6), Ubi("23101", 3, 1) });

            Assert.IsFalse(c.Cuadra);
            Assert.AreEqual(CuadreUbicaciones.FALTA_EN_UBICACIONES, c.Situacion);
            Assert.AreEqual(-3, c.Diferencia);
        }

        [TestMethod]
        public void ProductoSinUbicaciones_ConStockNoCuadra()
        {
            CuadreUbicacionProducto c = Uno(new[] { Stock("23101", 7) }, new UbicacionesPorEstado[0]);

            Assert.IsFalse(c.Cuadra);
            Assert.AreEqual(CuadreUbicaciones.SIN_UBICACIONES, c.Situacion);
            Assert.AreEqual(-7, c.Diferencia);
        }

        [TestMethod]
        public void ProductoSinUbicaciones_SinStockCuadra()
        {
            CuadreUbicacionProducto c = Uno(new[] { Stock("23101", 0) }, new UbicacionesPorEstado[0]);

            Assert.IsTrue(c.Cuadra);
        }

        [TestMethod]
        public void ProductoQueNoSeUbica_SinFilasCuadraAunqueTengaStock()
        {
            // Productos.Ubicar = 0: no tiene filas en Ubicaciones, como en prdQuedaDescuadradaUbicación
            CuadreUbicacionProducto c = Uno(new[] { Stock("23101", 7) }, new UbicacionesPorEstado[0], ubicar: false);

            Assert.IsTrue(c.Cuadra);
        }

        [TestMethod]
        public void ProductoQueNoSeUbica_ConFilasSeComparaIgual()
        {
            CuadreUbicacionProducto c = Uno(new[] { Stock("23101", 7) }, new[] { Ubi("23101", 0, 3) }, ubicar: false);

            Assert.IsFalse(c.Cuadra);
            Assert.AreEqual(-4, c.Diferencia);
        }

        [TestMethod]
        public void EstadosNegativos_SonHistoricoYNoCuentan()
        {
            CuadreUbicacionProducto c = Uno(new[] { Stock("23101", 46) }, new[]
            {
                Ubi("23101", 0, 46),
                Ubi("23101", -1, 4982), Ubi("23101", -2, -3), Ubi("23101", -3, -69), Ubi("23101", -4, -12), Ubi("23101", -5, -2719), Ubi("23101", -102, -1709)
            });

            Assert.IsTrue(c.Cuadra);
            Assert.AreEqual(46, c.Ubicaciones);
        }

        [TestMethod]
        public void CuentaComoMercancia_SoloEstadosNoNegativos()
        {
            foreach (int estado in new[] { 0, 2, 3, 4, 5 })
            {
                Assert.IsTrue(CuadreUbicaciones.CuentaComoMercancia(estado), estado.ToString());
            }
            foreach (int estado in new[] { -1, -2, -3, -4, -5, -102 })
            {
                Assert.IsFalse(CuadreUbicaciones.CuentaComoMercancia(estado), estado.ToString());
            }
        }

        [TestMethod]
        public void EstadoPositivoDesconocido_CuentaYSeEnseñaAparte()
        {
            CuadreUbicacionProducto c = Uno(new[] { Stock("23101", 5) }, new[] { Ubi("23101", 0, 4), Ubi("23101", 1, 1) });

            Assert.IsTrue(c.Cuadra);
            Assert.AreEqual(1, c.OtrosEstados);
        }

        [TestMethod]
        public void ProductoSoloEnUbicaciones_SinExtractoSobraTodo()
        {
            List<CuadreUbicacionProducto> cuadre = CuadreUbicaciones.Cuadrar("ALG", new StockExtractoProducto[0], new[] { Ubi("38282", 0, 130) },
                new[] { Ficha("38282") }, soloDescuadrados: false);

            Assert.AreEqual(1, cuadre.Count);
            Assert.AreEqual(0, cuadre[0].Stock);
            Assert.AreEqual(130, cuadre[0].Diferencia);
            Assert.AreEqual(CuadreUbicaciones.SOBRA_EN_UBICACIONES, cuadre[0].Situacion);
        }

        [TestMethod]
        public void SoloDescuadrados_QuitaLosQueCuadranYJuntaLosRellenosDeEspacios()
        {
            List<CuadreUbicacionProducto> cuadre = CuadreUbicaciones.Cuadrar("ALG",
                new[] { Stock("A", 5), Stock("B", 3), Stock("C ", 2) },
                new[] { Ubi("A", 0, 5), Ubi("B", 0, 1), Ubi("C", 0, 2) },
                new[] { Ficha("A"), Ficha("B"), Ficha("C") }, soloDescuadrados: true);

            Assert.AreEqual(1, cuadre.Count);
            Assert.AreEqual("B", cuadre[0].Producto);
        }

        // ---------------------------------------------------------------- servicio

        [TestMethod]
        public async Task Servicio_AlmacenSinControlDeUbicaciones_NoCuadraNada()
        {
            IRepositorioCuadreUbicaciones repo = A.Fake<IRepositorioCuadreUbicaciones>();
            A.CallTo(() => repo.TieneControlUbicaciones("1", "REI")).Returns(Task.FromResult<bool?>(false));

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => new ServicioCuadreUbicaciones(repo).Cuadrar("1", "REI", "23101"));
            A.CallTo(() => repo.LeerStock(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_ConProducto_LeeEmpresaYEspejoYDevuelveAunqueCuadre()
        {
            IRepositorioCuadreUbicaciones repo = A.Fake<IRepositorioCuadreUbicaciones>();
            A.CallTo(() => repo.TieneControlUbicaciones("1", "ALG")).Returns(Task.FromResult<bool?>(true));
            A.CallTo(() => repo.LeerStock("1", "3", "ALG", "23101")).Returns(Task.FromResult(new List<StockExtractoProducto> { Stock("23101", 513) }));
            A.CallTo(() => repo.LeerUbicaciones("1", "3", "ALG", "23101")).Returns(Task.FromResult(new List<UbicacionesPorEstado> { Ubi("23101", 0, 513) }));
            A.CallTo(() => repo.LeerProductos("1", "23101")).Returns(Task.FromResult(new List<ProductoCuadre> { Ficha("23101") }));

            List<CuadreUbicacionProducto> cuadre = await new ServicioCuadreUbicaciones(repo).Cuadrar("1", "alg", " 23101 ");

            Assert.AreEqual(1, cuadre.Count);
            Assert.IsTrue(cuadre.Single().Cuadra);
        }

        [TestMethod]
        public async Task Servicio_SinProducto_SoloDescuadrados()
        {
            IRepositorioCuadreUbicaciones repo = A.Fake<IRepositorioCuadreUbicaciones>();
            A.CallTo(() => repo.TieneControlUbicaciones("1", "ALG")).Returns(Task.FromResult<bool?>(true));
            A.CallTo(() => repo.LeerStock("1", "3", "ALG", null)).Returns(Task.FromResult(new List<StockExtractoProducto> { Stock("A", 1), Stock("B", 2) }));
            A.CallTo(() => repo.LeerUbicaciones("1", "3", "ALG", null)).Returns(Task.FromResult(new List<UbicacionesPorEstado> { Ubi("A", 0, 1), Ubi("B", 0, 5) }));
            A.CallTo(() => repo.LeerProductos("1", null)).Returns(Task.FromResult(new List<ProductoCuadre> { Ficha("A"), Ficha("B") }));

            List<CuadreUbicacionProducto> cuadre = await new ServicioCuadreUbicaciones(repo).Cuadrar("1", "ALG", null);

            Assert.AreEqual("B", cuadre.Single().Producto);
            Assert.AreEqual(3, cuadre.Single().Diferencia);
        }

        // ---------------------------------------------------------------- SQL

        [TestMethod]
        public void Sql_SoloLecturaConNolockYEstadosNoNegativos()
        {
            foreach (string sql in new[] { RepositorioCuadreUbicacionesSql.SQL_STOCK, RepositorioCuadreUbicacionesSql.SQL_UBICACIONES,
                RepositorioCuadreUbicacionesSql.SQL_PRODUCTOS, RepositorioCuadreUbicacionesSql.SQL_CONTROL_UBICACIONES })
            {
                StringAssert.Contains(sql, "WITH (NOLOCK)");
                Assert.IsFalse(sql.Contains("UPDATE") || sql.Contains("INSERT") || sql.Contains("DELETE"));
            }
            StringAssert.Contains(RepositorioCuadreUbicacionesSql.SQL_UBICACIONES, "ISNULL(u.Estado, 0) >= 0");
            StringAssert.Contains(RepositorioCuadreUbicacionesSql.SQL_STOCK, "e.Empresa IN (@p0, @p1) AND e.[Almacén] = @p2");
        }

        [TestMethod]
        public void Sql_ElFiltroDeProductoSoloVaSiHayProducto()
        {
            string conProducto = RepositorioCuadreUbicacionesSql.Sql(RepositorioCuadreUbicacionesSql.SQL_UBICACIONES,
                RepositorioCuadreUbicacionesSql.FILTRO_PRODUCTO_UBICACIONES, "23101");
            string sinProducto = RepositorioCuadreUbicacionesSql.Sql(RepositorioCuadreUbicacionesSql.SQL_UBICACIONES,
                RepositorioCuadreUbicacionesSql.FILTRO_PRODUCTO_UBICACIONES, null);

            StringAssert.Contains(conProducto, "AND u.[Número] = @p3");
            Assert.IsFalse(sinProducto.Contains("@p3"));
        }
    }
}
