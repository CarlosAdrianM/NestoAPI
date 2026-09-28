using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Issue #547, corte (b): clasificación de la sombra (igual, distinto, pendiente, empate) y la pasada
    /// (qué se registra, idempotencia, tabla que no existe, errores por producto, límite de tiempo).
    /// </summary>
    [TestClass]
    public class ServicioSombraPreciosMediosTests
    {
        private static readonly DateTime F1 = new DateTime(2025, 1, 10, 10, 0, 0);
        private static readonly DateTime F2 = new DateTime(2025, 2, 10, 12, 30, 0);
        private static readonly DateTime CORTE = new DateTime(2026, 9, 27, 0, 30, 0);
        private static readonly DateTime FECHA_PASADA = new DateTime(2026, 9, 27, 6, 30, 0);

        #region Escenarios

        /// <summary>Una compra de 10 uds a 100 € (primera compra: 10,0000) y el SP dejó exactamente eso.</summary>
        internal static DatosProductoPrecioMedio ProductoIgual(string producto = "IGUAL", decimal precioMedioBD = 10m)
        {
            DatosProductoPrecioMedio datos = ConstructorHistorialPrecioMedioTests.Datos();
            datos.Producto = producto;
            datos.Ficha.PrecioMedio = precioMedioBD;
            datos.Compras.Add(ConstructorHistorialPrecioMedioTests.Compra(1, 100, F1, 4, CORTE.AddDays(-100), 10, 100m, coste: 10m));
            datos.Movimientos.Add(new MovimientoStockPrecioMedio { Fecha = F1, Cantidad = 10, EsRecepcion = true });
            return datos;
        }

        /// <summary>
        /// Segunda compra (10 uds a 200 €) facturada DESPUÉS de la pasada del SP: el SP dejó la media de la primera
        /// (10) y la línea nueva a 0; con ella facturada la media sería (10 × 10 + 200) / 20 = 15.
        /// </summary>
        internal static DatosProductoPrecioMedio ProductoConFacturaPosterior()
        {
            DatosProductoPrecioMedio datos = ProductoIgual("PENDIENTE");
            datos.Compras.Add(ConstructorHistorialPrecioMedioTests.Compra(2, 200, F2, 4, CORTE.AddHours(30), 10, 200m, coste: 0m));
            datos.Movimientos.Add(new MovimientoStockPrecioMedio { Fecha = F2, Cantidad = 10, EsRecepcion = true });
            return datos;
        }

        /// <summary>
        /// Dos facturas a la misma hora (10 uds a 100 € y 10 uds a 200 €): el C# procesa la 200 la última (media 20);
        /// el SP, en su orden indeterminado, dejó la de la 100 (media 10).
        /// </summary>
        internal static DatosProductoPrecioMedio ProductoConEmpate()
        {
            DatosProductoPrecioMedio datos = ConstructorHistorialPrecioMedioTests.Datos();
            datos.Producto = "EMPATE";
            datos.Ficha.PrecioMedio = 10m;
            datos.Compras.Add(ConstructorHistorialPrecioMedioTests.Compra(1, 100, F1, 4, CORTE.AddDays(-100), 10, 100m, coste: 10m));
            datos.Compras.Add(ConstructorHistorialPrecioMedioTests.Compra(2, 200, F1, 4, CORTE.AddDays(-100), 10, 200m, coste: 20m));
            datos.Movimientos.Add(new MovimientoStockPrecioMedio { Fecha = F1, Cantidad = 20, EsRecepcion = true });
            return datos;
        }

        #endregion

        #region Evaluar

        [TestMethod]
        public void Evaluar_TodoIgual_Igual()
        {
            ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(ProductoIgual(), CORTE);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.Igual, r.Clasificacion);
            Assert.AreEqual(10m, r.Calculo.PrecioMedioFinal);
            Assert.AreEqual(1, r.Diferencia.LineasComparadas);
            Assert.IsFalse(r.DebeRegistrarse, "los iguales sin avisos no se registran");
            Assert.IsFalse(r.EsNoEsperada);
        }

        [TestMethod]
        public void Evaluar_MediaDistinta_Distinto()
        {
            ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(ProductoIgual(precioMedioBD: 10.0001m), CORTE);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.Distinto, r.Clasificacion);
            Assert.IsTrue(r.Diferencia.MediaDistinta);
            Assert.IsTrue(r.DebeRegistrarse);
            Assert.IsTrue(r.EsNoEsperada);
        }

        [TestMethod]
        public void Evaluar_FacturaPosteriorALaPasadaDelSP_Pendiente()
        {
            ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(ProductoConFacturaPosterior(), CORTE);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.Pendiente, r.Clasificacion);
            Assert.AreEqual(1, r.LineasPendientes);
            Assert.AreEqual(15m, r.Calculo.PrecioMedioFinal, "el cálculo devuelto es el actual: lo que dejará el SP el domingo");
            Assert.AreEqual(10m, r.Diferencia.PrecioMedioCalculado, "la diferencia registrada es contra lo que vio el SP");
            Assert.IsFalse(r.Diferencia.HayDiferencia);
            Assert.IsTrue(r.DebeRegistrarse);
            Assert.IsFalse(r.EsNoEsperada);
        }

        [TestMethod]
        public void Evaluar_FacturaPosteriorPeroElSPTampocoCuadra_Distinto()
        {
            DatosProductoPrecioMedio datos = ProductoConFacturaPosterior();
            datos.Ficha.PrecioMedio = 11m;

            ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(datos, CORTE);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.Distinto, r.Clasificacion);
            Assert.AreEqual(1, r.LineasPendientes);
        }

        [TestMethod]
        public void Evaluar_FacturaPosteriorYaVistaPorElSP_Igual()
        {
            // Si la BD ya tiene lo que sale con la factura (p. ej. se tocó la cabecera después), es igual sin más.
            DatosProductoPrecioMedio datos = ProductoConFacturaPosterior();
            datos.Ficha.PrecioMedio = 15m;
            datos.Compras.Single(c => c.NumeroOrden == 2).Coste = 15m;

            Assert.AreEqual(ClasificacionSombraPrecioMedio.Igual, ServicioSombraPreciosMedios.Evaluar(datos, CORTE).Clasificacion);
        }

        [TestMethod]
        public void Evaluar_DiferenciaConEmpateDeFacturas_Empate()
        {
            ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(ProductoConEmpate(), CORTE);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.Empate, r.Clasificacion);
            Assert.AreEqual(20m, r.Calculo.PrecioMedioFinal, "el C# desempata por nº de factura: decide la mayor");
            Assert.IsTrue(r.Calculo.Avisos.Any(a => a.Tipo == TipoAvisoPrecioMedio.EmpateFacturasMismaFecha));
            Assert.IsTrue(r.DebeRegistrarse);
            Assert.IsFalse(r.EsNoEsperada, "un empate es una diferencia esperada");
        }

        [TestMethod]
        public void Evaluar_EmpateQueCuadra_IgualConAvisos()
        {
            DatosProductoPrecioMedio datos = ProductoConEmpate();
            datos.Ficha.PrecioMedio = 20m;

            ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(datos, CORTE);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.IgualConAvisos, r.Clasificacion);
            Assert.IsTrue(r.DebeRegistrarse, "los avisos se registran aunque cuadre");
        }

        [TestMethod]
        public void Evaluar_SinFicha_NoSeCalculaNiSeRegistra()
        {
            DatosProductoPrecioMedio datos = ProductoIgual();
            datos.Ficha = null;

            ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(datos, CORTE);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.SinFicha, r.Clasificacion);
            Assert.IsNull(r.Calculo);
            Assert.IsFalse(r.DebeRegistrarse);
        }

        [TestMethod]
        public void Evaluar_Ficticio_NoProcesadoYNoSeRegistra()
        {
            DatosProductoPrecioMedio datos = ProductoIgual(precioMedioBD: 99m);
            datos.Ficha.Ficticio = true;

            ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(datos, CORTE);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.NoProcesado, r.Clasificacion);
            Assert.AreEqual(MotivoPrecioMedioNoProcesado.Ficticio, r.Calculo.Motivo);
            Assert.IsFalse(r.DebeRegistrarse);
        }

        [TestMethod]
        public void Evaluar_VentaConCosteDistinto_Distinto()
        {
            DatosProductoPrecioMedio datos = ProductoIgual();
            datos.Ventas = new List<LineaVentaPrecioMedio>
            {
                new LineaVentaPrecioMedio { NumeroOrden = 1, Estado = 4, FechaAlbaran = F2, Coste = 9m, FechaModificacion = CORTE.AddDays(-1) }
            };

            ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(datos, CORTE);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.Distinto, r.Clasificacion);
            Assert.AreEqual(1, r.Diferencia.VentasDistintas);
        }

        /// <summary>
        /// Los 18 productos reales del §3 (estado del 27/09/26) leídos «como de la BD»: la sombra no puede ver
        /// ninguna diferencia (si la viera, sería un fallo del constructor del historial o del comparador).
        /// </summary>
        [TestMethod]
        public void Evaluar_LosProductosRealesDelPlan_NingunaDiferencia()
        {
            string[] productos = { "45934", "38896", "41281", "44904", "34248", "40985", "45396", "17877", "16137", "45188",
                "44243", "43859", "45039", "34929", "37688", "40179", "45668", "47074" };
            List<string> fallos = new List<string>();
            foreach (string producto in productos)
            {
                FixturePrecioMedio fixture = FixturePrecioMedio.Cargar(producto);
                ResultadoSombraPrecioMedio r = ServicioSombraPreciosMedios.Evaluar(DatosDesdeFixture(fixture), CORTE);
                ClasificacionSombraPrecioMedio[] esperadas = fixture.MotivoEsperado == MotivoPrecioMedioNoProcesado.Ninguno
                    ? new[] { ClasificacionSombraPrecioMedio.Igual, ClasificacionSombraPrecioMedio.IgualConAvisos }
                    : new[] { ClasificacionSombraPrecioMedio.NoProcesado };
                if (!esperadas.Contains(r.Clasificacion))
                {
                    fallos.Add(producto + ": " + r.Clasificacion + " (primera línea distinta " + r.Diferencia?.PrimeraLineaDistinta + ")");
                }
            }
            Assert.AreEqual(0, fallos.Count, string.Join("; ", fallos));
        }

        /// <summary>
        /// Una fixture del corte (a) como si viniera de la BD: los costes y la media son los que dejó el SP, y el
        /// stock a cada fecha se convierte en movimientos (diferencias entre fechas consecutivas).
        /// </summary>
        internal static DatosProductoPrecioMedio DatosDesdeFixture(FixturePrecioMedio fixture)
        {
            DatosProductoPrecioMedio datos = new DatosProductoPrecioMedio
            {
                Empresa = fixture.Empresa,
                EmpresaEspejo = fixture.EmpresaEspejo,
                Producto = fixture.Producto,
                Ficha = new FichaProductoPrecioMedio { PrecioMedio = fixture.PrecioMedioSP, Ficticio = fixture.Ficticio },
                Compras = fixture.Compras.Select(l => new LineaCompraBDPrecioMedio
                {
                    Empresa = l.Empresa,
                    NumeroFactura = l.NumeroFactura,
                    FechaAlbaran = l.FechaAlbaran,
                    NumeroAlbaran = l.NumeroAlbaran,
                    NumeroOrden = l.NumeroOrden,
                    Cantidad = l.Cantidad,
                    BaseImponible = l.BaseImponible,
                    Coste = l.Coste,
                    Estado = l.Estado,
                    FechaModificacionFactura = CORTE.AddDays(-1)
                }).ToList()
            };
            int anterior = 0;
            foreach (FixturePrecioMedio.StockFecha stock in (fixture.StockHasta ?? new List<FixturePrecioMedio.StockFecha>()).OrderBy(s => s.Fecha))
            {
                datos.Movimientos.Add(new MovimientoStockPrecioMedio { Fecha = stock.Fecha, Cantidad = stock.Stock - anterior });
                anterior = stock.Stock;
            }
            if (fixture.FechaUltimoMontaje.HasValue)
            {
                datos.Movimientos.Add(new MovimientoStockPrecioMedio { Fecha = fixture.FechaUltimoMontaje.Value, EsMontaje = true });
            }
            if (fixture.FechaUltimaRecepcion.HasValue)
            {
                datos.Movimientos.Add(new MovimientoStockPrecioMedio { Fecha = fixture.FechaUltimaRecepcion.Value, EsRecepcion = true });
            }
            return datos;
        }

        #endregion

        #region Pasada

        private IRepositorioPreciosMedios repositorio;
        private ServicioSombraPreciosMedios servicio;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioPreciosMedios>();
            A.CallTo(() => repositorio.ExisteTablaSombra()).Returns(true);
            A.CallTo(() => repositorio.EmpresaEspejo("1")).Returns("3");
            A.CallTo(() => repositorio.ProductosConCompras("1", "3")).Returns(new List<string> { "IGUAL", "DISTINTO", "ERROR", "PENDIENTE" });
            A.CallTo(() => repositorio.LeerDatos("1", "3", "IGUAL", A<bool>._)).Returns(ProductoIgual());
            A.CallTo(() => repositorio.LeerDatos("1", "3", "DISTINTO", A<bool>._)).Returns(ProductoIgual("DISTINTO", 9m));
            A.CallTo(() => repositorio.LeerDatos("1", "3", "ERROR", A<bool>._)).Throws(new InvalidOperationException("timeout"));
            A.CallTo(() => repositorio.LeerDatos("1", "3", "PENDIENTE", A<bool>._)).Returns(ProductoConFacturaPosterior());
            servicio = new ServicioSombraPreciosMedios(repositorio);
        }

        [TestMethod]
        public void Pasada_SinLaTabla_NoLeeNadaYDevuelveElError()
        {
            A.CallTo(() => repositorio.ExisteTablaSombra()).Returns(false);

            ResumenPasadaSombraPrecioMedio resumen = servicio.EjecutarPasada(new[] { "1" }, FECHA_PASADA, CORTE, true, TimeSpan.FromHours(1));

            StringAssert.Contains(resumen.Error, "Issue547_PreciosMediosSombra.sql");
            A.CallTo(() => repositorio.ProductosConCompras(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarFila(A<FilaPreciosMediosSombra>._)).MustNotHaveHappened();
            StringAssert.Contains(resumen.ToString(), "NO ejecutada");
        }

        [TestMethod]
        public void Pasada_SiFallaLaComprobacionDeLaTabla_NoRompe()
        {
            A.CallTo(() => repositorio.ExisteTablaSombra()).Throws(new Exception("sin conexión"));

            ResumenPasadaSombraPrecioMedio resumen = servicio.EjecutarPasada(new[] { "1" }, FECHA_PASADA, CORTE, true, TimeSpan.FromHours(1));

            StringAssert.Contains(resumen.Error, "sin conexión");
            A.CallTo(() => repositorio.ProductosConCompras(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Pasada_RegistraSoloLoQueDifiereMasElResumen_YBorraAntesLaDelDia()
        {
            List<FilaPreciosMediosSombra> filas = new List<FilaPreciosMediosSombra>();
            A.CallTo(() => repositorio.GuardarFila(A<FilaPreciosMediosSombra>._)).Invokes((FilaPreciosMediosSombra f) => filas.Add(f));

            ResumenPasadaSombraPrecioMedio resumen = servicio.EjecutarPasada(new[] { "1" }, FECHA_PASADA, CORTE, true, TimeSpan.FromHours(1));

            Assert.IsNull(resumen.Error);
            A.CallTo(() => repositorio.BorrarPasada(FECHA_PASADA.Date, "1")).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEquivalent(new[] { "DISTINTO", "ERROR", "PENDIENTE", FilaPreciosMediosSombra.PRODUCTO_RESUMEN },
                filas.Select(f => f.Producto).ToList());
            Assert.IsTrue(filas.All(f => f.FechaPasada == FECHA_PASADA.Date && f.Empresa == "1"));

            FilaPreciosMediosSombra distinto = filas.Single(f => f.Producto == "DISTINTO");
            Assert.AreEqual("Distinto", distinto.Clasificacion);
            Assert.AreEqual(9m, distinto.PrecioMedioBD);
            Assert.AreEqual(10m, distinto.PrecioMedioCalculado);
            Assert.AreEqual("Pendiente", filas.Single(f => f.Producto == "PENDIENTE").Clasificacion);
            Assert.AreEqual(1, filas.Single(f => f.Producto == "PENDIENTE").LineasPendientes);
            StringAssert.Contains(filas.Single(f => f.Producto == "ERROR").Detalle, "timeout");

            FilaPreciosMediosSombra fila = filas.Single(f => f.EsResumen);
            Assert.AreEqual("Distinto", fila.Clasificacion, "hay diferencias no esperadas");

            ResumenEmpresaSombraPrecioMedio e = resumen.Empresas.Single();
            Assert.AreEqual(4, e.Productos);
            Assert.AreEqual(4, e.Revisados);
            Assert.AreEqual(1, e.Iguales);
            Assert.AreEqual(1, e.Distintos);
            Assert.AreEqual(1, e.Pendientes);
            Assert.AreEqual(1, e.Errores);
            Assert.AreEqual(2, resumen.NoEsperados);
            CollectionAssert.AreEquivalent(new[] { "DISTINTO", "ERROR" }, e.PrimerosNoEsperados.Select(p => p.Producto).ToList());
        }

        [TestMethod]
        public void Pasada_SinRegistrar_NoTocaLaTabla()
        {
            ResumenPasadaSombraPrecioMedio resumen = servicio.EjecutarPasada(new[] { "1" }, FECHA_PASADA, CORTE, false, TimeSpan.FromHours(1));

            Assert.AreEqual(4, resumen.Empresas.Single().Revisados);
            A.CallTo(() => repositorio.ExisteTablaSombra()).MustNotHaveHappened();
            A.CallTo(() => repositorio.BorrarPasada(A<DateTime>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarFila(A<FilaPreciosMediosSombra>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Pasada_UnFalloAlRegistrarUnProducto_NoAbortaLaPasada()
        {
            A.CallTo(() => repositorio.GuardarFila(A<FilaPreciosMediosSombra>.That.Matches(f => f.Producto == "DISTINTO")))
                .Throws(new Exception("permiso denegado"));

            ResumenPasadaSombraPrecioMedio resumen = servicio.EjecutarPasada(new[] { "1" }, FECHA_PASADA, CORTE, true, TimeSpan.FromHours(1));

            Assert.AreEqual(4, resumen.Empresas.Single().Revisados);
            A.CallTo(() => repositorio.GuardarFila(A<FilaPreciosMediosSombra>.That.Matches(f => f.EsResumen))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Pasada_AgotadoElTiempo_SeInterrumpeSinLeerMas()
        {
            ResumenPasadaSombraPrecioMedio resumen = servicio.EjecutarPasada(new[] { "1", "4" }, FECHA_PASADA, CORTE, true, TimeSpan.Zero);

            ResumenEmpresaSombraPrecioMedio e = resumen.Empresas.Single();
            Assert.IsTrue(e.Interrumpida);
            Assert.AreEqual(0, e.Revisados);
            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
            StringAssert.Contains(resumen.ToString(), "INTERRUMPIDA");
        }

        [TestMethod]
        public void Pasada_MuestreaLasVentasDeUnosPocosProductos()
        {
            servicio.EjecutarPasada(new[] { "1" }, FECHA_PASADA, CORTE, false, TimeSpan.FromHours(1), muestraVentasPorEmpresa: 1);

            A.CallTo(() => repositorio.LeerDatos("1", "3", A<string>._, true)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.LeerDatos("1", "3", A<string>._, false)).MustHaveHappened(3, Times.Exactly);
        }

        [TestMethod]
        public void ElegirMuestraVentas_EsEstableParaLaMismaFechaYDelTamanoPedido()
        {
            List<string> productos = Enumerable.Range(1, 1000).Select(i => i.ToString()).ToList();

            HashSet<string> a = ServicioSombraPreciosMedios.ElegirMuestraVentas(productos, 200, FECHA_PASADA);
            HashSet<string> b = ServicioSombraPreciosMedios.ElegirMuestraVentas(productos, 200, FECHA_PASADA.Date);
            HashSet<string> otraSemana = ServicioSombraPreciosMedios.ElegirMuestraVentas(productos, 200, FECHA_PASADA.AddDays(7));

            Assert.AreEqual(200, a.Count);
            Assert.IsTrue(a.SetEquals(b));
            Assert.IsFalse(a.SetEquals(otraSemana), "cada semana, otra muestra");
            Assert.AreEqual(0, ServicioSombraPreciosMedios.ElegirMuestraVentas(productos, 0, FECHA_PASADA).Count);
            Assert.AreEqual(3, ServicioSombraPreciosMedios.ElegirMuestraVentas(productos.Take(3).ToList(), 200, FECHA_PASADA).Count);
        }

        [TestMethod]
        public void CompararProducto_UsaLaEmpresaEspejoYNoRegistra()
        {
            ResultadoSombraPrecioMedio r = servicio.CompararProducto("1", "DISTINTO", CORTE, conVentas: true);

            Assert.AreEqual(ClasificacionSombraPrecioMedio.Distinto, r.Clasificacion);
            A.CallTo(() => repositorio.LeerDatos("1", "3", "DISTINTO", true)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.GuardarFila(A<FilaPreciosMediosSombra>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void CorteSP_SiNoSePuedeLeerMsdb_UsaElSupuesto()
        {
            A.CallTo(() => repositorio.UltimaEjecucionSP()).Throws(new Exception("SELECT permission denied on msdb"));
            Assert.AreEqual(CORTE, servicio.CorteSP(CORTE));
            Assert.IsNull(servicio.UltimaEjecucionSP());

            DateTime inicioReal = new DateTime(2026, 9, 27, 0, 30, 5);
            A.CallTo(() => repositorio.UltimaEjecucionSP()).Returns(new EjecucionSPPreciosMedios { Inicio = inicioReal, Fin = inicioReal.AddHours(3) });
            Assert.AreEqual(inicioReal, servicio.CorteSP(CORTE));
        }

        #endregion
    }
}
