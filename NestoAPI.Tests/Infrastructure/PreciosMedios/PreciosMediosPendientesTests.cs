using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Issue #547, incremental, corte (a): tope de filas por producto y pasada. Un producto que llega al tope se escribe
    /// hasta él, se confirma y queda «pendiente» (tabla PreciosMediosPendientes) para que la siguiente pasada nocturna lo
    /// continúe; cuando se termina, se desmarca. Sin BD: el ejecutor recibe la vuelta de cada sentencia inyectada y el
    /// servicio trabaja con repositorios fake.
    /// </summary>
    [TestClass]
    public class PreciosMediosPendientesTests
    {
        private const int LOTE = SqlEscrituraPreciosMedios.TAMANO_LOTE_VENTAS;
        private static readonly DateTime AHORA = new DateTime(2026, 9, 30, 2, 30, 0);
        private static readonly DateTime MARCA = new DateTime(2026, 9, 29, 2, 20, 0);

        #region Ejecutor con tope

        private static ComandoEscrituraPrecioMedio Comando(TablaEscrituraPrecioMedio tabla, bool enLotes, string texto)
        {
            return new ComandoEscrituraPrecioMedio { Tabla = tabla, EnLotes = enLotes, Texto = texto };
        }

        /// <summary>Simula la BD: cada sentencia tiene N filas por cambiar; cada vuelta cambia hasta un lote (o todas si no va en lotes).</summary>
        private static Func<ComandoEscrituraPrecioMedio, int> Bd(Dictionary<string, int> porCambiar, List<string> ejecutadas)
        {
            return c =>
            {
                ejecutadas.Add(c.Texto);
                int quedan = porCambiar[c.Texto];
                int filas = c.EnLotes ? Math.Min(LOTE, quedan) : quedan;
                porCambiar[c.Texto] = quedan - filas;
                return filas;
            };
        }

        [TestMethod]
        public void Tope_EsUnNumeroEnteroDeLotesYNoPasaDeDiezMil()
        {
            Assert.AreEqual(0, EjecutorComandosPreciosMedios.TOPE_FILAS_POR_PRODUCTO_Y_PASADA % LOTE);
            Assert.IsTrue(EjecutorComandosPreciosMedios.TOPE_FILAS_POR_PRODUCTO_Y_PASADA <= 10000);
        }

        [TestMethod]
        public void Ejecutor_PorDebajoDelTope_LoEjecutaTodoYNoQuedaPendiente()
        {
            Dictionary<string, int> bd = new Dictionary<string, int> { ["P"] = 1, ["C"] = 3, ["V"] = 2500, ["VP"] = 40 };
            List<string> ejecutadas = new List<string>();
            ResultadoEscrituraPrecioMedio r = new ResultadoEscrituraPrecioMedio();

            EjecutorComandosPreciosMedios.Ejecutar(new[]
            {
                Comando(TablaEscrituraPrecioMedio.Productos, false, "P"),
                Comando(TablaEscrituraPrecioMedio.LinPedidoCmp, false, "C"),
                Comando(TablaEscrituraPrecioMedio.LinPedidoVta, true, "V"),
                Comando(TablaEscrituraPrecioMedio.LinPedidoVta, true, "VP")
            }, EjecutorComandosPreciosMedios.TOPE_FILAS_POR_PRODUCTO_Y_PASADA, Bd(bd, ejecutadas), r);

            Assert.IsFalse(r.QuedaPendiente);
            Assert.AreEqual(1, r.FilasProductos);
            Assert.AreEqual(3, r.FilasCompras);
            Assert.AreEqual(2540, r.FilasVentas);
            CollectionAssert.AreEqual(new[] { "P", "C", "V", "V", "V", "VP" }, ejecutadas, "Los lotes se repiten mientras salgan llenos");
            Assert.IsTrue(bd.Values.All(v => v == 0));
        }

        [TestMethod]
        public void Ejecutor_BestsellerConMuchasVentas_ParaEnElTopeYQuedaPendiente()
        {
            Dictionary<string, int> bd = new Dictionary<string, int> { ["P"] = 1, ["V"] = 45000, ["VP"] = 30 };
            List<string> ejecutadas = new List<string>();
            ResultadoEscrituraPrecioMedio r = new ResultadoEscrituraPrecioMedio();

            EjecutorComandosPreciosMedios.Ejecutar(new[]
            {
                Comando(TablaEscrituraPrecioMedio.Productos, false, "P"),
                Comando(TablaEscrituraPrecioMedio.LinPedidoVta, true, "V"),
                Comando(TablaEscrituraPrecioMedio.LinPedidoVta, true, "VP")
            }, 3 * LOTE, Bd(bd, ejecutadas), r);

            Assert.IsTrue(r.QuedaPendiente);
            Assert.AreEqual(1, r.FilasProductos);
            Assert.AreEqual(3 * LOTE, r.FilasVentas, "Se comprueba entre lotes: 1 + 3.000 ≥ 3.000 tras el tercer lote");
            Assert.IsFalse(ejecutadas.Contains("VP"), "Llegado el tope no se empieza ninguna sentencia más");
            Assert.AreEqual(45000 - 3 * LOTE, bd["V"]);
        }

        [TestMethod]
        public void Ejecutor_SegundaPasada_ContinuaDondeSeQuedoGraciasALoQueCambia()
        {
            // La BD recuerda lo ya escrito (el predicado «solo lo que cambia»): la siguiente pasada sigue donde se quedó.
            Dictionary<string, int> bd = new Dictionary<string, int> { ["V"] = 2 * LOTE + 500, ["VP"] = 10 };
            ComandoEscrituraPrecioMedio[] comandos =
            {
                Comando(TablaEscrituraPrecioMedio.LinPedidoVta, true, "V"),
                Comando(TablaEscrituraPrecioMedio.LinPedidoVta, true, "VP")
            };
            ResultadoEscrituraPrecioMedio primera = new ResultadoEscrituraPrecioMedio();
            ResultadoEscrituraPrecioMedio segunda = new ResultadoEscrituraPrecioMedio();

            EjecutorComandosPreciosMedios.Ejecutar(comandos, 2 * LOTE, Bd(bd, new List<string>()), primera);
            EjecutorComandosPreciosMedios.Ejecutar(comandos, 2 * LOTE, Bd(bd, new List<string>()), segunda);

            Assert.IsTrue(primera.QuedaPendiente);
            Assert.AreEqual(2 * LOTE, primera.FilasVentas);
            Assert.IsFalse(segunda.QuedaPendiente);
            Assert.AreEqual(510, segunda.FilasVentas);
            Assert.IsTrue(bd.Values.All(v => v == 0));
        }

        [TestMethod]
        public void Ejecutor_TopeAlcanzadoConLaUltimaSentenciaTerminada_NoQuedaPendiente()
        {
            Dictionary<string, int> bd = new Dictionary<string, int> { ["C"] = 5 };
            ResultadoEscrituraPrecioMedio r = new ResultadoEscrituraPrecioMedio();

            EjecutorComandosPreciosMedios.Ejecutar(new[] { Comando(TablaEscrituraPrecioMedio.LinPedidoCmp, false, "C") }, 5, Bd(bd, new List<string>()), r);

            Assert.IsFalse(r.QuedaPendiente);
            Assert.AreEqual(5, r.FilasCompras);
        }

        [TestMethod]
        public void Ejecutor_TopeAlcanzadoConSentenciasSinEmpezar_QuedaPendiente()
        {
            Dictionary<string, int> bd = new Dictionary<string, int> { ["C"] = 5, ["VP"] = 3 };
            List<string> ejecutadas = new List<string>();
            ResultadoEscrituraPrecioMedio r = new ResultadoEscrituraPrecioMedio();

            EjecutorComandosPreciosMedios.Ejecutar(new[]
            {
                Comando(TablaEscrituraPrecioMedio.LinPedidoCmp, false, "C"),
                Comando(TablaEscrituraPrecioMedio.LinPedidoVta, true, "VP")
            }, 5, Bd(bd, ejecutadas), r);

            Assert.IsTrue(r.QuedaPendiente);
            CollectionAssert.AreEqual(new[] { "C" }, ejecutadas);
        }

        [TestMethod]
        public void Ejecutor_SinComandos_NoHaceNada()
        {
            ResultadoEscrituraPrecioMedio r = new ResultadoEscrituraPrecioMedio();

            EjecutorComandosPreciosMedios.Ejecutar(new List<ComandoEscrituraPrecioMedio>(), 10, c => throw new InvalidOperationException(), r);

            Assert.IsFalse(r.QuedaPendiente);
            Assert.IsFalse(r.HaCambiadoAlgo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Ejecutor_TopeCero_Lanza()
        {
            EjecutorComandosPreciosMedios.Ejecutar(new List<ComandoEscrituraPrecioMedio>(), 0, c => 0, new ResultadoEscrituraPrecioMedio());
        }

        #endregion

        #region Servicio: marcar, continuar y desmarcar

        private IRepositorioEscrituraPreciosMedios escritura;
        private IRepositorioPreciosMedios lectura;
        private HashSet<string> llegaAlTope;
        private List<string> recalculados;

        [TestInitialize]
        public void Inicializar()
        {
            escritura = A.Fake<IRepositorioEscrituraPreciosMedios>();
            lectura = A.Fake<IRepositorioPreciosMedios>();
            llegaAlTope = new HashSet<string>();
            recalculados = new List<string>();
            A.CallTo(() => lectura.EmpresaEspejo(A<string>._)).ReturnsLazily((string e) => e == "1" ? "3" : e);
            A.CallTo(() => lectura.UltimaEjecucionSP()).Returns(null);
            A.CallTo(() => escritura.AhoraServidor()).Returns(AHORA);
            A.CallTo(() => escritura.LeerUltimaPasada()).Returns(MARCA);
            A.CallTo(() => escritura.LeerUltimoNumOrdenExtracto()).Returns(null);
            A.CallTo(() => escritura.MaximoNumOrdenExtracto(A<string>._)).Returns(null);
            A.CallTo(() => escritura.ProductosConComprasModificadas(A<string>._, A<string>._, A<DateTime>._)).Returns(new List<string>());
            A.CallTo(() => escritura.ProductosConMovimientosConFechaPasada(A<string>._, A<string>._, A<int>._, A<int>._)).Returns(new List<string>());
            A.CallTo(() => escritura.ProductosPendientes(A<string>._)).Returns(new List<string>());
            A.CallTo(() => escritura.RecalcularYEscribir(A<string>._, A<string>._, A<string>._, A<Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio>>._))
                .ReturnsLazily((string e, string esp, string p, Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio> f) =>
                {
                    recalculados.Add(e + "/" + p);
                    DatosProductoPrecioMedio datos = ServicioSombraPreciosMediosTests.ProductoConFacturaPosterior();
                    bool tope = llegaAlTope.Contains(p);
                    return new ResultadoEscrituraPrecioMedio
                    {
                        Empresa = e,
                        Producto = p,
                        Plan = f(datos),
                        FilasProductos = 1,
                        FilasVentas = tope ? EjecutorComandosPreciosMedios.TOPE_FILAS_POR_PRODUCTO_Y_PASADA : 0,
                        QuedaPendiente = tope
                    };
                });
        }

        private ServicioIncrementalPreciosMedios Servicio()
        {
            return new ServicioIncrementalPreciosMedios(escritura, lectura);
        }

        [TestMethod]
        public void Nocturna_ProductoQueLlegaAlTope_SeMarcaPendienteYLaMarcaDeLaPasadaAvanza()
        {
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "41281", "17877" });
            _ = llegaAlTope.Add("41281");

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            A.CallTo(() => escritura.MarcarPendiente("1", "41281", A<string>.That.Contains("Tope de 10000 filas"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => escritura.MarcarPendiente(A<string>._, "17877", A<string>._)).MustNotHaveHappened();
            A.CallTo(() => escritura.QuitarPendiente(A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(1, r.Empresas.Single(e => e.Empresa == "1").QuedanPendientes);
            Assert.AreEqual(0, r.Errores, "Quedar pendiente no es un error");
            Assert.IsNotNull(r.MarcaGuardada, "El pendiente va en su tabla: la marca de la pasada avanza igual");
            StringAssert.Contains(r.ToString(), "1 quedan PENDIENTES");
        }

        [TestMethod]
        public void Nocturna_PendienteDeOtraPasada_SeContinuaAunqueNoTengaCambiosNuevosYAlTerminarSeDesmarca()
        {
            A.CallTo(() => escritura.ProductosPendientes("1")).Returns(new List<string> { "41281 " });

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            CollectionAssert.Contains(recalculados, "1/41281");
            A.CallTo(() => escritura.QuitarPendiente("1", "41281")).MustHaveHappenedOnceExactly();
            ResumenEmpresaIncrementalPrecioMedio e1 = r.Empresas.Single(e => e.Empresa == "1");
            Assert.AreEqual(1, e1.PorPendientes);
            Assert.AreEqual(1, e1.PendientesTerminados);
            Assert.AreEqual(1, e1.Seleccionados);
        }

        [TestMethod]
        public void Nocturna_PendienteQueVuelveALlegarAlTope_SigueMarcadoYNoSeDesmarca()
        {
            A.CallTo(() => escritura.ProductosPendientes("1")).Returns(new List<string> { "41281" });
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "41281" });
            _ = llegaAlTope.Add("41281");

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            Assert.AreEqual(1, recalculados.Count(x => x == "1/41281"), "Pendiente y con compras nuevas: una sola vez");
            A.CallTo(() => escritura.MarcarPendiente("1", "41281", A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => escritura.QuitarPendiente(A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, r.Empresas.Single(e => e.Empresa == "1").PendientesTerminados);
        }

        [TestMethod]
        public void Nocturna_ProductoQueNoEstabaPendienteYTermina_NoTocaLaTablaDePendientes()
        {
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "17877" });

            _ = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            A.CallTo(() => escritura.MarcarPendiente(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => escritura.QuitarPendiente(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Nocturna_SinTablaDePendientes_SigueConLoDemasYLoDiceEnElResumen()
        {
            A.CallTo(() => escritura.ProductosPendientes(A<string>._)).Throws(new InvalidOperationException("Invalid object name 'PreciosMediosPendientes'"));
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "17877" });

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            CollectionAssert.Contains(recalculados, "1/17877");
            Assert.IsNotNull(r.MarcaGuardada);
            StringAssert.Contains(r.ToString(), "NO se han podido leer los pendientes: Invalid object name 'PreciosMediosPendientes'");
        }

        [TestMethod]
        public void Nocturna_SiNoSePuedeMarcarPendiente_CuentaComoErrorYSeReintenta()
        {
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "41281" });
            _ = llegaAlTope.Add("41281");
            A.CallTo(() => escritura.MarcarPendiente(A<string>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("sin tabla"));
            List<string> reintentos = new List<string>();

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), (e, p) => reintentos.Add(e + "/" + p));

            Assert.AreEqual(1, r.Errores);
            CollectionAssert.AreEqual(new[] { "1/41281" }, reintentos, "Sin marca, el reintento de Hangfire continúa el producto: no se pierde");
        }

        [TestMethod]
        public void RecalcularProducto_EncoladoAlFacturarQueLlegaAlTope_SeMarcaEnLaEmpresaPrincipal()
        {
            _ = llegaAlTope.Add("41281");

            ResultadoEscrituraPrecioMedio r = Servicio().RecalcularProducto("3", " 41281 ");

            Assert.IsTrue(r.QuedaPendiente);
            A.CallTo(() => escritura.MarcarPendiente("1", "41281", A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void RecalcularProducto_QueTermina_NoTocaLaTablaDePendientes()
        {
            _ = Servicio().RecalcularProducto("1", "17877");

            A.CallTo(() => escritura.MarcarPendiente(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => escritura.QuitarPendiente(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        #endregion
    }
}
