using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Issue #547, corte (c): selección nocturna (compras modificadas + movimientos con fecha pasada, riesgo 2), marca de
    /// la última pasada, aplazamiento si el SP del domingo sigue corriendo, reintentos, interruptor y encolado al facturar.
    /// Todo con repositorios fake: no se ejecuta nada contra la BD.
    /// </summary>
    [TestClass]
    public class PreciosMediosIncrementalTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 9, 30, 2, 30, 0);
        private static readonly DateTime INICIO_SERVIDOR = new DateTime(2026, 9, 30, 2, 30, 7);
        private static readonly DateTime MARCA = new DateTime(2026, 9, 29, 2, 20, 0);
        private const int NUM_ORDEN_GUARDADO = 4690000;
        private const int NUM_ORDEN_MAXIMO = 4691450;

        private IRepositorioEscrituraPreciosMedios escritura;
        private IRepositorioPreciosMedios lectura;
        private List<string> recalculados;

        [TestInitialize]
        public void Inicializar()
        {
            escritura = A.Fake<IRepositorioEscrituraPreciosMedios>();
            lectura = A.Fake<IRepositorioPreciosMedios>();
            recalculados = new List<string>();
            A.CallTo(() => lectura.EmpresaEspejo(A<string>._)).ReturnsLazily((string e) => e == "1" ? "3" : e);
            A.CallTo(() => lectura.UltimaEjecucionSP()).Returns(null);
            A.CallTo(() => escritura.AhoraServidor()).Returns(INICIO_SERVIDOR);
            A.CallTo(() => escritura.LeerUltimaPasada()).Returns(MARCA);
            A.CallTo(() => escritura.ProductosConComprasModificadas(A<string>._, A<string>._, A<DateTime>._)).Returns(new List<string>());
            A.CallTo(() => escritura.ProductosConMovimientosConFechaPasada(A<string>._, A<string>._, A<int>._, A<int>._)).Returns(new List<string>());
            A.CallTo(() => escritura.LeerUltimoNumOrdenExtracto()).Returns(NUM_ORDEN_GUARDADO);
            A.CallTo(() => escritura.MaximoNumOrdenExtracto(A<string>._)).Returns(null);
            A.CallTo(() => escritura.MaximoNumOrdenExtracto("1")).Returns(NUM_ORDEN_MAXIMO);
            A.CallTo(() => escritura.MaximoNumOrdenExtracto("3")).Returns(4625815);
            A.CallTo(() => escritura.MaximoNumOrdenExtracto("4")).Returns(4100000);
            A.CallTo(() => escritura.RecalcularYEscribir(A<string>._, A<string>._, A<string>._, A<Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio>>._))
                .ReturnsLazily((string e, string esp, string p, Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio> f) =>
                {
                    recalculados.Add(e + "/" + esp + "/" + p);
                    DatosProductoPrecioMedio datos = ServicioSombraPreciosMediosTests.ProductoConFacturaPosterior();
                    return new ResultadoEscrituraPrecioMedio { Empresa = e, Producto = p, Plan = f(datos), FilasProductos = 1, FilasCompras = 1 };
                });
        }

        private ServicioIncrementalPreciosMedios Servicio()
        {
            return new ServicioIncrementalPreciosMedios(escritura, lectura);
        }

        #region Selección

        [TestMethod]
        public void Seleccion_UneComprasYMovimientosConFechaPasada_SinRepetidosNiEspacios()
        {
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "45396 ", "41281", "41281" });
            A.CallTo(() => escritura.ProductosConMovimientosConFechaPasada("1", "3", NUM_ORDEN_GUARDADO, NUM_ORDEN_MAXIMO))
                .Returns(new List<string> { "41281", "17877", " " });

            SeleccionIncrementalPrecioMedio s = Servicio().SeleccionarProductos("1", "3", MARCA, NUM_ORDEN_GUARDADO, NUM_ORDEN_MAXIMO);

            CollectionAssert.AreEqual(new[] { "17877", "41281", "45396" }, s.Productos);
            Assert.AreEqual(2, s.PorCompras);
            Assert.AreEqual(2, s.PorMovimientos);
        }

        [TestMethod]
        public void Seleccion_SoloPorMovimientoConFechaPasada_TambienSeRecalcula()
        {
            // Riesgo 2: una regularización con fecha antigua cambia el stock a la fecha de una compra sin tocar LinPedidoCmp.
            A.CallTo(() => escritura.ProductosConMovimientosConFechaPasada("1", "3", NUM_ORDEN_GUARDADO, NUM_ORDEN_MAXIMO))
                .Returns(new List<string> { "16137" });

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            CollectionAssert.Contains(recalculados, "1/3/16137");
            Assert.AreEqual(1, r.Empresas.Single(e => e.Empresa == "1").PorMovimientos);
        }

        [TestMethod]
        public void Seleccion_SinNumOrdenPrevio_NoMiraMovimientos()
        {
            SeleccionIncrementalPrecioMedio s = Servicio().SeleccionarProductos("1", "3", MARCA, null, NUM_ORDEN_MAXIMO);

            Assert.AreEqual(0, s.PorMovimientos);
            A.CallTo(() => escritura.ProductosConMovimientosConFechaPasada(A<string>._, A<string>._, A<int>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Seleccion_SinApuntesNuevos_NoConsultaExtractoProducto()
        {
            SeleccionIncrementalPrecioMedio s = Servicio().SeleccionarProductos("1", "3", MARCA, NUM_ORDEN_MAXIMO, NUM_ORDEN_MAXIMO);

            Assert.AreEqual(0, s.PorMovimientos);
            A.CallTo(() => escritura.ProductosConMovimientosConFechaPasada(A<string>._, A<string>._, A<int>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void EmpresaPrincipal_LaEspejoSeTraduceYLasQueNoProcesaElSPNo()
        {
            ServicioIncrementalPreciosMedios s = Servicio();

            Assert.AreEqual("1", s.EmpresaPrincipal("1  "));
            Assert.AreEqual("1", s.EmpresaPrincipal("3"));
            Assert.AreEqual("4", s.EmpresaPrincipal("4"));
            Assert.IsNull(s.EmpresaPrincipal("2"));
        }

        #endregion

        #region Pasada nocturna

        [TestMethod]
        public void Nocturna_RecalculaCadaProductoDeCadaEmpresaYGuardaLaMarcaConMargen()
        {
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "A", "B" });
            A.CallTo(() => escritura.ProductosConComprasModificadas("4", "4", MARCA)).Returns(new List<string> { "C" });

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            CollectionAssert.AreEqual(new[] { "1/3/A", "1/3/B", "4/4/C" }, recalculados);
            Assert.AreEqual(MARCA, r.Desde);
            Assert.AreEqual("última pasada", r.OrigenDesde);
            Assert.AreEqual(3, r.Empresas.Count);
            Assert.AreEqual(2, r.Empresas[0].ConCambios);
            Assert.AreEqual(2, r.Empresas[0].FilasProductos);
            DateTime esperada = INICIO_SERVIDOR - ServicioIncrementalPreciosMedios.MARGEN_MARCA;
            A.CallTo(() => escritura.GuardarUltimaPasada(esperada)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(esperada, r.MarcaGuardada);
            StringAssert.Contains(r.ToString(), "empresa 1: 2/2 productos");
        }

        [TestMethod]
        public void Nocturna_MovimientosPorNumOrden_DesdeElGuardadoHastaElMaximoDeTodasLasEmpresasYEspejos_YLoGuarda()
        {
            A.CallTo(() => escritura.MaximoNumOrdenExtracto("3")).Returns(NUM_ORDEN_MAXIMO + 5); // la espejo tiene el último apunte

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            Assert.AreEqual(NUM_ORDEN_GUARDADO, r.DesdeNumOrden);
            Assert.AreEqual(NUM_ORDEN_MAXIMO + 5, r.HastaNumOrden);
            A.CallTo(() => escritura.MaximoNumOrdenExtracto("1")).MustHaveHappenedOnceExactly();
            A.CallTo(() => escritura.MaximoNumOrdenExtracto("3")).MustHaveHappenedOnceExactly();
            A.CallTo(() => escritura.MaximoNumOrdenExtracto("4")).MustHaveHappenedOnceExactly();
            A.CallTo(() => escritura.MaximoNumOrdenExtracto("5")).MustHaveHappenedOnceExactly();
            A.CallTo(() => escritura.ProductosConMovimientosConFechaPasada("1", "3", NUM_ORDEN_GUARDADO, NUM_ORDEN_MAXIMO + 5)).MustHaveHappenedOnceExactly();
            A.CallTo(() => escritura.ProductosConMovimientosConFechaPasada("4", "4", NUM_ORDEN_GUARDADO, NUM_ORDEN_MAXIMO + 5)).MustHaveHappenedOnceExactly();
            A.CallTo(() => escritura.GuardarUltimoNumOrdenExtracto(NUM_ORDEN_MAXIMO + 5)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(NUM_ORDEN_MAXIMO + 5, r.NumOrdenGuardado);
            StringAssert.Contains(r.ToString(), "Nº Orden de ExtractoProducto 4690000 → 4691455; guardado 4691455");
        }

        [TestMethod]
        public void Nocturna_SinNumOrdenGuardado_EmpiezaASeguirDesdeElMaximoSinMirarElAtrasado()
        {
            A.CallTo(() => escritura.LeerUltimoNumOrdenExtracto()).Returns(null);

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            Assert.IsNull(r.DesdeNumOrden);
            A.CallTo(() => escritura.ProductosConMovimientosConFechaPasada(A<string>._, A<string>._, A<int>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => escritura.GuardarUltimoNumOrdenExtracto(NUM_ORDEN_MAXIMO)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(r.ToString(), "SIN marca previa");
            StringAssert.Contains(r.ToString(), "se empieza a seguir desde 4691450");
        }

        [TestMethod]
        public void Nocturna_SinApuntesEnExtracto_NoGuardaNumOrden()
        {
            A.CallTo(() => escritura.MaximoNumOrdenExtracto(A<string>._)).Returns(null);

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            Assert.IsNull(r.HastaNumOrden);
            A.CallTo(() => escritura.GuardarUltimoNumOrdenExtracto(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => escritura.GuardarUltimaPasada(A<DateTime>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Nocturna_SinMarca_ParteDelInicioDelSP()
        {
            DateTime inicioSP = new DateTime(2026, 9, 27, 0, 30, 2);
            A.CallTo(() => escritura.LeerUltimaPasada()).Returns(null);
            A.CallTo(() => lectura.UltimaEjecucionSP()).Returns(new EjecucionSPPreciosMedios { Inicio = inicioSP, Fin = inicioSP.AddHours(3) });

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            Assert.AreEqual(inicioSP, r.Desde);
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", inicioSP)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Nocturna_SinMarcaNiMsdb_ParteDelUltimoDomingoALas0030()
        {
            A.CallTo(() => escritura.LeerUltimaPasada()).Returns(null);
            A.CallTo(() => lectura.UltimaEjecucionSP()).Throws(new InvalidOperationException("sin permiso en msdb"));

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            Assert.AreEqual(new DateTime(2026, 9, 27, 0, 30, 0), r.Desde);
        }

        [TestMethod]
        public void Nocturna_ConElSPDelDomingoEnMarcha_SeAplazaSinTocarNadaNiLaMarca()
        {
            A.CallTo(() => lectura.UltimaEjecucionSP()).Returns(new EjecucionSPPreciosMedios { Inicio = AHORA.AddHours(-2), Fin = null });

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            Assert.IsNotNull(r.Aplazada);
            A.CallTo(() => escritura.AhoraServidor()).MustNotHaveHappened();
            A.CallTo(() => escritura.MaximoNumOrdenExtracto(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => escritura.GuardarUltimoNumOrdenExtracto(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => escritura.RecalcularYEscribir(A<string>._, A<string>._, A<string>._, A<Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio>>._))
                .MustNotHaveHappened();
            A.CallTo(() => escritura.GuardarUltimaPasada(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Nocturna_UnProductoFalla_SeEncolaSuReintentoYLaMarcaAvanza()
        {
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "BLOQUEADO", "BIEN" });
            A.CallTo(() => escritura.RecalcularYEscribir("1", "3", "BLOQUEADO", A<Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio>>._))
                .Throws(new InvalidOperationException("Lock request time out period exceeded"));
            List<string> reintentos = new List<string>();

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1),
                (e, p) => reintentos.Add(e + "/" + p));

            CollectionAssert.AreEqual(new[] { "1/BLOQUEADO" }, reintentos);
            Assert.AreEqual(1, r.Errores);
            Assert.AreEqual(1, r.Empresas[0].ConCambios, "un fallo no para la pasada");
            StringAssert.Contains(r.ToString(), "BLOQUEADO: Lock request time out");
            A.CallTo(() => escritura.GuardarUltimaPasada(A<DateTime>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Nocturna_SinTiempo_NoGuardaLaMarca()
        {
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "A" });

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromTicks(-1), null);

            Assert.IsTrue(r.Interrumpida);
            A.CallTo(() => escritura.GuardarUltimaPasada(A<DateTime>._)).MustNotHaveHappened();
            Assert.IsNull(r.MarcaGuardada);
            A.CallTo(() => escritura.GuardarUltimoNumOrdenExtracto(A<int>._)).MustNotHaveHappened();
            Assert.IsNull(r.NumOrdenGuardado);
            StringAssert.Contains(r.ToString(), "NO guardado");
        }

        [TestMethod]
        public void Nocturna_ProductoQueElSPNoToca_CuentaComoNoProcesado()
        {
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "34929" });
            A.CallTo(() => escritura.RecalcularYEscribir("1", "3", "34929", A<Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio>>._))
                .ReturnsLazily((string e, string esp, string p, Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio> f) =>
                    new ResultadoEscrituraPrecioMedio { Plan = f(ServicioSombraPreciosMediosTests.DatosDesdeFixture(FixturePrecioMedio.Cargar("34929"))) });

            ResumenPasadaIncrementalPrecioMedio r = Servicio().EjecutarPasadaNocturna(AHORA, TimeSpan.FromHours(1), null);

            Assert.AreEqual(1, r.Empresas[0].NoProcesados);
            Assert.AreEqual(0, r.Empresas[0].ConCambios);
        }

        #endregion

        #region Jobs e interruptor

        [TestMethod]
        public void Interruptor_SoloTrueExplicitoLoEnciende()
        {
            Assert.IsTrue(PreciosMediosIncrementalJobsService.EstaActivo("true"));
            Assert.IsTrue(PreciosMediosIncrementalJobsService.EstaActivo(" True "));
            Assert.IsFalse(PreciosMediosIncrementalJobsService.EstaActivo(null));
            Assert.IsFalse(PreciosMediosIncrementalJobsService.EstaActivo(""));
            Assert.IsFalse(PreciosMediosIncrementalJobsService.EstaActivo("false"));
            Assert.IsFalse(PreciosMediosIncrementalJobsService.EstaActivo("1"));
            Assert.AreEqual("PreciosMedios:EscribirIncremental", PreciosMediosIncrementalJobsService.CLAVE_INTERRUPTOR);
        }

        [TestMethod]
        public void Interruptor_EnLosTestsNoHayClave_Apagado()
        {
            Assert.IsFalse(PreciosMediosIncrementalJobsService.EstaActivo());
        }

        [TestMethod]
        public void JobNocturno_Apagado_NiSiquieraCreaElServicio()
        {
            int creados = 0;

            ResumenPasadaIncrementalPrecioMedio r = PreciosMediosIncrementalJobsService.ProcesarNocturna(false,
                () => { creados++; return Servicio(); }, AHORA, null);

            Assert.IsNull(r);
            Assert.AreEqual(0, creados);
        }

        [TestMethod]
        public void JobNocturno_Encendido_HaceLaPasada()
        {
            A.CallTo(() => escritura.ProductosConComprasModificadas("1", "3", MARCA)).Returns(new List<string> { "A" });

            ResumenPasadaIncrementalPrecioMedio r = PreciosMediosIncrementalJobsService.ProcesarNocturna(true, Servicio, AHORA, null);

            Assert.IsNotNull(r);
            CollectionAssert.AreEqual(new[] { "1/3/A" }, recalculados);
        }

        [TestMethod]
        public void JobProducto_Apagado_NoHaceNada()
        {
            Assert.IsNull(PreciosMediosIncrementalJobsService.ProcesarProducto(false, Servicio, "1", "A"));
            Assert.AreEqual(0, recalculados.Count);
        }

        [TestMethod]
        public void JobProducto_EmpresaEspejo_SeRecalculaEnLaPrincipal()
        {
            ResultadoEscrituraPrecioMedio r = PreciosMediosIncrementalJobsService.ProcesarProducto(true, Servicio, "3", "17877");

            Assert.IsNotNull(r);
            CollectionAssert.AreEqual(new[] { "1/3/17877" }, recalculados);
        }

        [TestMethod]
        public void JobProducto_ConElSPEnMarcha_NoHaceNada()
        {
            A.CallTo(() => lectura.UltimaEjecucionSP()).Returns(new EjecucionSPPreciosMedios { Inicio = AHORA, Fin = null });

            Assert.IsNull(PreciosMediosIncrementalJobsService.ProcesarProducto(true, Servicio, "1", "A"));
            Assert.AreEqual(0, recalculados.Count);
        }

        [TestMethod]
        public void JobProducto_SiFalla_RelanzaParaQueHangfireReintente()
        {
            A.CallTo(() => escritura.RecalcularYEscribir("1", "3", "A", A<Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio>>._))
                .Throws(new InvalidOperationException("deadlock"));

            Assert.ThrowsException<InvalidOperationException>(() => PreciosMediosIncrementalJobsService.ProcesarProducto(true, Servicio, "1", "A"));
        }

        [TestMethod]
        public void JobPedido_EncolaUnJobPorProductoDelPedido()
        {
            A.CallTo(() => escritura.ProductosDelPedidoCompra("1", 12345)).Returns(new List<string> { "45396 ", "41281" });
            List<string> encolados = new List<string>();

            IReadOnlyList<string> productos = PreciosMediosIncrementalJobsService.ProcesarPedido(true, Servicio, "1", 12345,
                (e, p) => encolados.Add(e + "/" + p));

            CollectionAssert.AreEqual(new[] { "1/45396", "1/41281" }, encolados);
            Assert.AreEqual(2, productos.Count);
            Assert.AreEqual(0, recalculados.Count, "el job del pedido solo encola: cada producto va en su propio job");
        }

        [TestMethod]
        public void JobPedido_Apagado_NoLeeNada()
        {
            IReadOnlyList<string> productos = PreciosMediosIncrementalJobsService.ProcesarPedido(false, Servicio, "1", 12345, (e, p) => Assert.Fail());

            Assert.AreEqual(0, productos.Count);
            A.CallTo(() => escritura.ProductosDelPedidoCompra(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void EncolarTrasFacturar_Apagado_NoEncola()
        {
            bool encolado = false;

            Assert.IsFalse(PreciosMediosIncrementalJobsService.Encolar(false, "1", 12345, (e, p) => encolado = true));
            Assert.IsFalse(encolado);
        }

        [TestMethod]
        public void EncolarTrasFacturar_Encendido_EncolaElPedidoConLaEmpresaSinRelleno()
        {
            string empresa = null;
            int pedido = 0;

            Assert.IsTrue(PreciosMediosIncrementalJobsService.Encolar(true, "1  ", 12345, (e, p) => { empresa = e; pedido = p; }));

            Assert.AreEqual("1", empresa);
            Assert.AreEqual(12345, pedido);
        }

        [TestMethod]
        public void EncolarTrasFacturar_SiHangfireFalla_NoRompeLaFacturacion()
        {
            Assert.IsFalse(PreciosMediosIncrementalJobsService.Encolar(true, "1", 12345,
                (e, p) => throw new InvalidOperationException("Hangfire caído")));
        }

        [TestMethod]
        public void EncolarTrasFacturar_EnLosTestsElInterruptorEstaApagado_NoTocaHangfire()
        {
            // Sin la clave en el app.config de los tests: si intentara encolar, Hangfire (sin almacenamiento) lanzaría.
            PreciosMediosIncrementalJobsService.EncolarTrasFacturarCompra("1", 12345);
        }

        #endregion
    }
}
