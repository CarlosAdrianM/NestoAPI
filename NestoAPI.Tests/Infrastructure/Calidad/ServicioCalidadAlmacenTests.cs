using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Almacen;
using System;
using System.Linq;
using System.Runtime.Caching;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914 corte 2: el resumen por preparador, lo no evaluado y el detalle de los indicadores de Almacén.</summary>
    [TestClass]
    public class ServicioCalidadAlmacenTests
    {
        private static readonly DateTime DIA = new DateTime(2026, 11, 20, 9, 0, 0);
        private static readonly DateTime DESDE = new DateTime(2026, 11, 1);
        private static readonly DateTime HASTA = new DateTime(2026, 11, 27);

        /// <summary>
        /// Picking 100 (Alfredo): A para el pedido 900, empaquetado con una falta del packing; B para el 901, sin packing. Picking 101: Pedro da
        /// entera por falta la C y Alfredo coge la D, que está facturada y rectificada. Picking 102: una línea sin lecturas (sin preparador).
        /// </summary>
        private static LecturaCalidadAlmacen Lectura()
        {
            var l = new LecturaCalidadAlmacen
            {
                PrimeraLecturaPicking = new DateTime(2026, 9, 30, 8, 0, 0),
                PrimeraLecturaPacking = new DateTime(2026, 10, 2, 8, 0, 0),
                PrimeraNoCasada = new DateTime(2026, 10, 20, 8, 0, 0)
            };
            l.Pickings.Add(new PickingCalidadAlmacen { Picking = 100, Fecha = DIA });
            l.Pickings.Add(new PickingCalidadAlmacen { Picking = 101, Fecha = DIA.AddHours(1) });
            l.Pickings.Add(new PickingCalidadAlmacen { Picking = 102, Fecha = DIA.AddHours(2) });
            l.LecturasPicking.Add(new LecturaPickingCalidadAlmacen { Picking = 100, Producto = "A", Usuario = @"NUEVAVISION\Alfredo", Cogidas = 2, Ultima = DIA });
            l.LecturasPicking.Add(new LecturaPickingCalidadAlmacen { Picking = 100, Producto = "B", Usuario = "Alfredo", Cogidas = 1, Ultima = DIA });
            l.LecturasPicking.Add(new LecturaPickingCalidadAlmacen { Picking = 101, Producto = "C", Usuario = "Pedro", Faltas = 1, Ultima = DIA });
            l.LecturasPicking.Add(new LecturaPickingCalidadAlmacen { Picking = 101, Producto = "D", Usuario = "Alfredo", Cogidas = 4, Ultima = DIA });
            l.Lineas.Add(new LineaPedidoCalidadAlmacen { Picking = 100, Pedido = 900, Linea = 1, Producto = "A", Cantidad = 2 });
            l.Lineas.Add(new LineaPedidoCalidadAlmacen { Picking = 100, Pedido = 901, Linea = 2, Producto = "B", Cantidad = 1 });
            l.Lineas.Add(new LineaPedidoCalidadAlmacen { Picking = 101, Pedido = 902, Linea = 3, Producto = "D", Cantidad = 4, Factura = "NV26/9", UnidadesRectificadas = 1 });
            l.Lineas.Add(new LineaPedidoCalidadAlmacen { Picking = 102, Pedido = 903, Linea = 4, Producto = "E", Cantidad = 1 });
            l.LecturasPacking.Add(new LecturaPackingCalidadAlmacen { Picking = 100, Pedido = 900, Producto = "A", Usuario = "Santiago", Metidas = 1, Faltas = 1 });
            l.NoCasadas.Add(new NoCasadasCalidadAlmacen { Picking = 100, Usuario = "Alfredo", Pantalla = "Recorrido", Lecturas = 2 });
            l.PickingsSinLecturas.Add(new PickingSinLecturasAlmacen { Picking = 99, Pedidos = 1, Lineas = 3, Almacen = "ALG" });
            return l;
        }

        private static CalculoCalidadAlmacen Calculo(LecturaCalidadAlmacen lectura = null)
        {
            lectura = lectura ?? Lectura();
            return new CalculoCalidadAlmacen { Empresa = "1", Desde = DESDE, Hasta = HASTA, Lectura = lectura, Evaluacion = ReglaCalidadAlmacen.Evaluar(lectura) };
        }

        [TestMethod]
        public void Resumir_PorPreparador_VolumenYErrores()
        {
            ResumenCalidadAlmacenDTO r = ServicioCalidadAlmacen.Resumir(Calculo());

            PreparadorAlmacenDTO alfredo = r.Preparadores.Single(p => p.Preparador == "Alfredo");
            Assert.AreEqual(2, alfredo.Pickings);
            Assert.AreEqual(3, alfredo.Lineas);
            Assert.AreEqual(3, alfredo.Pedidos);
            Assert.AreEqual(3, alfredo.Paradas);
            Assert.AreEqual(7, alfredo.Unidades);
            Assert.AreEqual(1, alfredo.LineasEvaluadasError, "Solo A se empaquetó con Ariadna");
            Assert.AreEqual(1, alfredo.LineasConError);
            Assert.AreEqual(100m, alfredo.PorcentajeError);
            Assert.AreEqual(1, alfredo.PickingsCompartidos, "En el 101 también está Pedro");
            IndicadorPreparadorDTO noCasadas = alfredo.Indicadores.Single(i => i.Indicador == "LecturaNoCasada");
            Assert.AreEqual(2, noCasadas.Con);
            Assert.IsNull(noCasadas.Evaluadas);

            PreparadorAlmacenDTO pedro = r.Preparadores.Single(p => p.Preparador == "Pedro");
            Assert.AreEqual(0, pedro.Lineas);
            Assert.AreEqual(1, pedro.Paradas);
            IndicadorPreparadorDTO faltas = pedro.Indicadores.Single(i => i.Indicador == "FaltaEnPicking");
            Assert.AreEqual(1, faltas.Con);
            Assert.AreEqual(1, faltas.Evaluadas);
            Assert.AreEqual(100m, faltas.Porcentaje);
        }

        [TestMethod]
        public void Resumir_Total_LineasSinPreparadorYPickingsEnPapel()
        {
            ResumenCalidadAlmacenDTO r = ServicioCalidadAlmacen.Resumir(Calculo());

            Assert.IsNull(r.Total.Preparador);
            Assert.AreEqual(3, r.Total.Lineas, "La del 102 no tiene preparador: no cuenta");
            Assert.AreEqual(1, r.LineasSinPreparador);
            Assert.AreEqual(1, r.PickingsSinRastro.Pickings);
            Assert.AreEqual(1, r.PickingsSinRastro.PorAlmacen["ALG"]);
            Assert.AreEqual(new DateTime(2026, 9, 30, 8, 0, 0), r.PreparadorDesde);
            Assert.IsNull(r.Aviso);
        }

        [TestMethod]
        public void Resumir_Indicadores_DesdeCuandoYNoEvaluados()
        {
            ResumenCalidadAlmacenDTO r = ServicioCalidadAlmacen.Resumir(Calculo());

            IndicadorAlmacenDTO falta = r.Indicadores.Single(i => i.Indicador == "FaltaEnPacking");
            Assert.IsTrue(falta.EsError);
            Assert.AreEqual("Linea", falta.Ambito);
            Assert.AreEqual(new DateTime(2026, 10, 2, 8, 0, 0), falta.EvaluadoDesde);
            Assert.AreEqual(1, falta.Evaluadas);
            Assert.AreEqual(1, falta.Con);

            IndicadorNoEvaluadoAlmacenDTO noEvaluado = r.IndicadoresNoEvaluados.Single(i => i.Indicador == "FaltaEnPacking");
            Assert.AreEqual(3, noEvaluado.SinEvaluar, "B y D sin packing en Ariadna y E sin preparador");
            Assert.AreEqual(new DateTime(2026, 10, 20, 8, 0, 0), r.Indicadores.Single(i => i.Indicador == "LecturaNoCasada").EvaluadoDesde);
            Assert.IsTrue(r.SinRastro.Any(s => s.Indicador == "FaltaQueSiEstaba"));
            // Carlos 27/11/26: los errores imputables son la falta del packing y el error de envío reclamado (con sus dos sub-motivos)
            CollectionAssert.AreEquivalent(new[] { "FaltaEnPacking", "ErrorDeEnvioReclamado", "ReenviadoSinCoste", "CambiadoPorOtro" },
                r.Indicadores.Where(i => i.EsError).Select(i => i.Indicador).ToList());
            CollectionAssert.AreEqual(new[] { "ErrorDeEnvioReclamado", "FaltaEnPacking" }, r.IndicadoresOficiales);
            Assert.IsTrue(r.Indicadores.Single(i => i.Indicador == "FaltaEnPacking").Oficial);
            Assert.IsFalse(r.Indicadores.Single(i => i.Indicador == "DescuadrePickingPacking").Oficial);
            Assert.AreEqual(falta.Con, r.Total.FaltaEnPacking.Con);
            Assert.AreEqual("ErrorDeEnvioReclamado", r.Total.ErrorDeEnvioReclamado.Indicador);
        }

        [TestMethod]
        public void Resumir_SinTablaDeNoCasadas_AvisoYNoEvaluado()
        {
            LecturaCalidadAlmacen lectura = Lectura();
            lectura.NoCasadas.Clear();
            lectura.PrimeraNoCasada = null;
            lectura.TablasQueFaltan.Add("dbo.LecturasNoCasadasAlmacen");

            ResumenCalidadAlmacenDTO r = ServicioCalidadAlmacen.Resumir(Calculo(lectura));

            StringAssert.Contains(r.Aviso, "LecturasNoCasadasAlmacen");
            Assert.IsNull(r.Indicadores.Single(i => i.Indicador == "LecturaNoCasada").EvaluadoDesde);
            Assert.IsTrue(r.IndicadoresNoEvaluados.Any(i => i.Indicador == "LecturaNoCasada"));
        }

        [TestMethod]
        public void Resumir_PeriodoVacio_SinErrores()
        {
            ResumenCalidadAlmacenDTO r = ServicioCalidadAlmacen.Resumir(Calculo(new LecturaCalidadAlmacen()));

            Assert.AreEqual(0, r.Total.Lineas);
            Assert.IsNull(r.Total.PorcentajeError);
            Assert.AreEqual(0, r.Preparadores.Count);
        }

        [TestMethod]
        public void Detallar_DelPreparador_SusLineasYParadas()
        {
            DetalleCalidadAlmacenDTO d = ServicioCalidadAlmacen.Detallar(Calculo(), @"NUEVAVISION\alfredo", null, 1, 100);

            Assert.AreEqual("alfredo", d.Preparador);
            Assert.AreEqual(3, d.TotalLineas);
            Assert.AreEqual(3, d.TotalParadas);
            Assert.AreEqual(2, d.NoCasadas.Single().Lecturas);
            LineaCalidadAlmacenDTO a = d.Lineas.Single(l => l.Producto == "A");
            CollectionAssert.Contains(a.Indicadores, "FaltaEnPacking");
            Assert.AreEqual("Santiago", a.Empaquetador);
        }

        [TestMethod]
        public void Detallar_PorIndicadorDeParada_SoloLasImputadas()
        {
            DetalleCalidadAlmacenDTO d = ServicioCalidadAlmacen.Detallar(Calculo(), null, IndicadorAlmacen.FaltaEnPicking, 1, 100);

            Assert.AreEqual(0, d.TotalLineas);
            Assert.AreEqual("C", d.Paradas.Single().Producto);
            Assert.AreEqual("Pedro", d.Paradas.Single().DioLaFalta);
            Assert.AreEqual(0, ServicioCalidadAlmacen.Detallar(Calculo(), "Alfredo", IndicadorAlmacen.FaltaEnPicking, 1, 100).TotalParadas);
        }

        [TestMethod]
        public void Detallar_Pagina()
        {
            DetalleCalidadAlmacenDTO d = ServicioCalidadAlmacen.Detallar(Calculo(), null, null, 2, 2);

            Assert.AreEqual(4, d.TotalLineas);
            Assert.AreEqual(2, d.Lineas.Count);
        }

        [TestMethod]
        public void Calcular_SeCacheaYUnFalloNoSeQueda()
        {
            ILectorCalidadAlmacen lector = A.Fake<ILectorCalidadAlmacen>();
            A.CallTo(() => lector.Leer("1", DESDE, HASTA)).Throws(new InvalidOperationException("caída")).Once().Then.Returns(Lectura());
            var servicio = new ServicioCalidadAlmacen(() => lector, () => DIA, new MemoryCache("pruebas-calidad-almacen"));

            _ = Assert.ThrowsException<InvalidOperationException>(() => servicio.Resumen("1", DESDE, HASTA));
            _ = servicio.Resumen("1", DESDE, HASTA);
            _ = servicio.Detalle("1", DESDE, HASTA, null, null, 1, 10);

            A.CallTo(() => lector.Leer("1", DESDE, HASTA)).MustHaveHappenedTwiceExactly();
        }
    }
}
