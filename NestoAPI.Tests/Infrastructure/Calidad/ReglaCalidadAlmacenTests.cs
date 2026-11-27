using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Almacen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914 corte 2: LA REGLA de los indicadores de Almacén (preparador de Ariadna, paradas, líneas, packing y rectificativas).</summary>
    [TestClass]
    public class ReglaCalidadAlmacenTests
    {
        private static readonly DateTime DIA = new DateTime(2026, 11, 20, 9, 0, 0);

        private static LecturaCalidadAlmacen Lectura(params int[] pickings)
        {
            var lectura = new LecturaCalidadAlmacen();
            foreach (int p in pickings)
            {
                lectura.Pickings.Add(new PickingCalidadAlmacen { Picking = p, Fecha = DIA });
            }
            return lectura;
        }

        private static LecturaPickingCalidadAlmacen Pick(int picking, string producto, string usuario, int cogidas, int faltas = 0, int minuto = 0) =>
            new LecturaPickingCalidadAlmacen { Picking = picking, Producto = producto, Usuario = usuario, Cogidas = cogidas, Faltas = faltas, Ultima = DIA.AddMinutes(minuto) };

        private static LineaPedidoCalidadAlmacen Linea(int picking, int pedido, int linea, string producto, int cantidad, string factura = null, decimal rectificadas = 0) =>
            new LineaPedidoCalidadAlmacen
            {
                Picking = picking, Pedido = pedido, Linea = linea, Producto = producto, Cantidad = cantidad, Estado = 1, Factura = factura,
                Almacen = "ALG", Cliente = "1001", Contacto = "0", UnidadesRectificadas = rectificadas
            };

        private static LecturaPackingCalidadAlmacen Pack(int picking, int pedido, string producto, string usuario, int metidas, int faltas = 0) =>
            new LecturaPackingCalidadAlmacen { Picking = picking, Pedido = pedido, Producto = producto, Usuario = usuario, Metidas = metidas, Faltas = faltas };

        [TestMethod]
        public void Persona_QuitaElDominio()
        {
            Assert.AreEqual("Alfredo", ReglaCalidadAlmacen.Persona(@"NUEVAVISION\Alfredo"));
            Assert.AreEqual("Pedro", ReglaCalidadAlmacen.Persona(" Pedro "));
            Assert.IsNull(ReglaCalidadAlmacen.Persona("  "));
        }

        [TestMethod]
        public void Preparador_ElDeMasUnidadesCogidas_ConYSinDominioEsLaMismaPersona()
        {
            string preparador = ReglaCalidadAlmacen.Preparador(new[]
            {
                Pick(1, "A", @"NUEVAVISION\Alfredo", 2), Pick(1, "A", "Alfredo", 2), Pick(1, "A", "Santiago", 3)
            });

            Assert.AreEqual("Alfredo", preparador);
        }

        [TestMethod]
        public void Preparador_Empate_LaLecturaMasReciente()
        {
            Assert.AreEqual("Santiago", ReglaCalidadAlmacen.Preparador(new[] { Pick(1, "A", "Alfredo", 2, minuto: 1), Pick(1, "A", "Santiago", 2, minuto: 5) }));
        }

        [TestMethod]
        public void Preparador_SinCogidas_QuienDioLaFalta()
        {
            Assert.AreEqual("Pedro", ReglaCalidadAlmacen.Preparador(new[] { Pick(1, "A", "Pedro", 0, faltas: 1) }));
            Assert.IsNull(ReglaCalidadAlmacen.Preparador(new LecturaPickingCalidadAlmacen[0]));
        }

        [TestMethod]
        public void Evaluar_PackingSinFaltas_LineaEvaluadaSinIndicadores()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 2));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 2, factura: "NV26/1"));
            lectura.LecturasPacking.Add(Pack(100, 900, "A", "Santiago", 2));

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            LineaPreparadaAlmacen linea = e.Lineas.Single();
            Assert.AreEqual("Alfredo", linea.Preparador);
            Assert.AreEqual("Santiago", linea.Empaquetador);
            Assert.IsTrue(linea.PackingEvaluado);
            Assert.AreEqual(0, linea.Indicadores.Count);
            // Sin albarán todavía: lo de la reclamación (27/11/26) no se puede mirar; lo demás, sí
            Assert.IsTrue(linea.NoEvaluados.All(InfoIndicadoresAlmacen.EsDeReclamacion), string.Join(", ", linea.NoEvaluados));
            Assert.AreEqual(0, e.Paradas.Single().Indicadores.Count);
        }

        [TestMethod]
        public void Evaluar_FaltaDelPackingSinDeshacer_FaltaEnPackingEsError()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 2));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 2));
            lectura.LecturasPacking.Add(Pack(100, 900, "A", "Santiago", 1, faltas: 1));

            LineaPreparadaAlmacen linea = ReglaCalidadAlmacen.Evaluar(lectura).Lineas.Single();

            CollectionAssert.Contains(linea.Indicadores.ToList(), IndicadorAlmacen.FaltaEnPacking);
            Assert.IsTrue(InfoIndicadoresAlmacen.EsError(IndicadorAlmacen.FaltaEnPacking));
        }

        [TestMethod]
        public void Evaluar_FaltaDelPackingDeshecha_HaAparecido_NoEsFalta()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 2));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 2));
            // +1 falta y −1 «ha aparecido» = 0; metidas 2
            lectura.LecturasPacking.Add(Pack(100, 900, "A", "Santiago", 2, faltas: 0));

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            Assert.IsFalse(e.Lineas.Single().Indicadores.Contains(IndicadorAlmacen.FaltaEnPacking));
            Assert.IsFalse(e.Paradas.Single().Indicadores.Contains(IndicadorAlmacen.DescuadrePickingPacking));
        }

        [TestMethod]
        public void Evaluar_PedidoSinPackingEnAriadna_PackingNoEvaluado()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 2));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 2));

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            CollectionAssert.Contains(e.Lineas.Single().NoEvaluados.ToList(), IndicadorAlmacen.FaltaEnPacking);
            CollectionAssert.Contains(e.Paradas.Single().NoEvaluados.ToList(), IndicadorAlmacen.DescuadrePickingPacking);
        }

        [TestMethod]
        public void Evaluar_PackingDeOtroProductoDelPedido_CuentaComoEmpaquetadoYFaltaLoQueNoSeMetio()
        {
            // El pedido se empaquetó con Ariadna (hay lecturas de B) pero de A no se metió nada ni se dio falta: descuadre, no falta de packing
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 2));
            lectura.LecturasPicking.Add(Pick(100, "B", "Alfredo", 1));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 2));
            lectura.Lineas.Add(Linea(100, 900, 2, "B", 1));
            lectura.LecturasPacking.Add(Pack(100, 900, "B", "Santiago", 1));

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            LineaPreparadaAlmacen a = e.Lineas.Single(l => l.Producto == "A");
            Assert.IsTrue(a.PackingEvaluado);
            Assert.IsFalse(a.Indicadores.Contains(IndicadorAlmacen.FaltaEnPacking));
            CollectionAssert.Contains(e.Paradas.Single(p => p.Producto == "A").Indicadores.ToList(), IndicadorAlmacen.DescuadrePickingPacking);
            Assert.IsFalse(e.Paradas.Single(p => p.Producto == "B").Indicadores.Contains(IndicadorAlmacen.DescuadrePickingPacking));
        }

        [TestMethod]
        public void Evaluar_PickingPorOla_LaParadaSirveADosPedidos_DescuadreSoloSiSeEmpaquetaronLosDos()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 3));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 2));
            lectura.Lineas.Add(Linea(100, 901, 7, "A", 1));
            lectura.LecturasPacking.Add(Pack(100, 900, "A", "Santiago", 2));

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            Assert.AreEqual(2, e.Lineas.Count);
            Assert.IsTrue(e.Lineas.All(l => l.Preparador == "Alfredo"));
            ParadaPreparadaAlmacen parada = e.Paradas.Single();
            Assert.AreEqual(3, parada.CantidadEnLineas);
            CollectionAssert.Contains(parada.NoEvaluados.ToList(), IndicadorAlmacen.DescuadrePickingPacking);

            lectura.LecturasPacking.Add(Pack(100, 901, "A", "Santiago", 1));
            Assert.AreEqual(0, ReglaCalidadAlmacen.Evaluar(lectura).Paradas.Single().Indicadores.Count);
        }

        [TestMethod]
        public void Evaluar_FaltaEnteraEnElRecorrido_LaParadaExisteAunqueLaLineaYaNoEsteEnElPicking()
        {
            // Dada entera por falta: la línea sale del picking (Picking = 0), pero la parada sale de las lecturas
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Pedro", 0, faltas: 2));

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            ParadaPreparadaAlmacen parada = e.Paradas.Single();
            Assert.AreEqual("Pedro", parada.Preparador);
            Assert.AreEqual("Pedro", parada.DioLaFalta);
            CollectionAssert.Contains(parada.Indicadores.ToList(), IndicadorAlmacen.FaltaEnPicking);
            Assert.AreEqual(0, e.Lineas.Count);
        }

        [TestMethod]
        public void Evaluar_FaltaQuitadaPorOtro_FaltaDeshechaPorOtroImputadaAQuienLaDio()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Pedro", 1, faltas: 1, minuto: 1));
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 1, faltas: -1, minuto: 9));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 2));

            ParadaPreparadaAlmacen parada = ReglaCalidadAlmacen.Evaluar(lectura).Paradas.Single();

            Assert.AreEqual(0, parada.Faltas);
            Assert.IsFalse(parada.Indicadores.Contains(IndicadorAlmacen.FaltaEnPicking));
            CollectionAssert.Contains(parada.Indicadores.ToList(), IndicadorAlmacen.FaltaDeshechaPorOtro);
            Assert.AreEqual("Pedro", parada.Imputado(IndicadorAlmacen.FaltaDeshechaPorOtro));
            Assert.AreEqual("Alfredo", parada.Preparador, "Empate a 1 cogida: la lectura más reciente");
        }

        [TestMethod]
        public void Evaluar_FaltaDeshechaPorElMismo_NoEsNada()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Pedro", 2, faltas: 1));
            lectura.LecturasPicking.Add(Pick(100, "A", @"NUEVAVISION\Pedro", 0, faltas: -1));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 2));

            Assert.AreEqual(0, ReglaCalidadAlmacen.Evaluar(lectura).Paradas.Single().Indicadores.Count);
        }

        [TestMethod]
        public void Evaluar_LineaSinLecturasDeSuProducto_SinPreparadorYNadaEvaluado()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 1));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 1));
            lectura.Lineas.Add(Linea(100, 900, 2, "B", 1, factura: "NV26/1", rectificadas: 1));

            LineaPreparadaAlmacen b = ReglaCalidadAlmacen.Evaluar(lectura).Lineas.Single(l => l.Producto == "B");

            Assert.IsNull(b.Preparador);
            Assert.AreEqual(0, b.Indicadores.Count);
            CollectionAssert.Contains(b.NoEvaluados.ToList(), IndicadorAlmacen.FaltaEnPacking);
            CollectionAssert.Contains(b.NoEvaluados.ToList(), IndicadorAlmacen.LineaRectificada);
        }

        [TestMethod]
        public void Evaluar_Rectificativa_SoloLineasFacturadas()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 1));
            lectura.LecturasPicking.Add(Pick(100, "B", "Alfredo", 1));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 1, factura: "NV26/1", rectificadas: 1));
            lectura.Lineas.Add(Linea(100, 901, 2, "B", 1));

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            CollectionAssert.Contains(e.Lineas.Single(l => l.Producto == "A").Indicadores.ToList(), IndicadorAlmacen.LineaRectificada);
            CollectionAssert.Contains(e.Lineas.Single(l => l.Producto == "B").NoEvaluados.ToList(), IndicadorAlmacen.LineaRectificada);
            Assert.IsFalse(InfoIndicadoresAlmacen.EsError(IndicadorAlmacen.LineaRectificada), "No se sabe por qué se rectificó: informativo");
        }

        [TestMethod]
        public void Evaluar_DosLineasDelMismoProductoEnUnPedido_VanJuntas()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 3));
            lectura.Lineas.Add(Linea(100, 900, 5, "A ", 1));
            lectura.Lineas.Add(Linea(100, 900, 3, "a", 2));

            LineaPreparadaAlmacen linea = ReglaCalidadAlmacen.Evaluar(lectura).Lineas.Single();

            Assert.AreEqual(3, linea.Cantidad);
            CollectionAssert.AreEqual(new List<int> { 3, 5 }, linea.LineasPedido);
        }

        [TestMethod]
        public void Evaluar_DosPreparadoresEnUnPicking_PickingCompartido()
        {
            LecturaCalidadAlmacen lectura = Lectura(100, 101);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 1));
            lectura.LecturasPicking.Add(Pick(100, "B", "Santiago", 1));
            lectura.LecturasPicking.Add(Pick(101, "A", "Alfredo", 1));

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            CollectionAssert.AreEquivalent(new[] { 100 }, e.PickingsCompartidos.ToList());
        }

        [TestMethod]
        public void Evaluar_LecturasDeUnPickingQueNoEsDelPeriodo_SeIgnoran()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(555, "A", "Alfredo", 1));
            lectura.Lineas.Add(Linea(555, 900, 1, "A", 1));

            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(lectura);

            Assert.AreEqual(0, e.Paradas.Count);
            Assert.AreEqual(0, e.Lineas.Count);
        }

        // Error de envío reclamado (Carlos 27/11/26)

        private static LecturaCalidadAlmacen ConReenvio(DateTime albaran)
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 1));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 1, factura: "NV26/1"));
            lectura.Reclamaciones.Servidas.Add(new NestoAPI.Infraestructure.Calidad.Reclamaciones.LineaServidaReclamacion
            {
                Pedido = 900, Linea = 1, Producto = "A", Cliente = "1001", FechaAlbaran = albaran, Factura = "NV26/1", Cantidad = 1, BaseImponible = 10m
            });
            lectura.Reclamaciones.MismoProducto.Add(new NestoAPI.Infraestructure.Calidad.Reclamaciones.LineaPosteriorReclamacion
            {
                Cliente = "1001", Pedido = 950, Linea = 1, Fecha = albaran.AddDays(3), Producto = "A", Cantidad = 1, BaseImponible = 0m
            });
            return lectura;
        }

        [TestMethod]
        public void Evaluar_ReenviadoSinCosteHaceMasDeLosDias_ErrorDeEnvioReclamadoDelPreparador()
        {
            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(ConReenvio(DIA.Date.AddDays(-60)), DIA);

            LineaPreparadaAlmacen linea = e.Lineas.Single();
            Assert.AreEqual("Alfredo", linea.Preparador);
            Assert.IsTrue(linea.Indicadores.Contains(IndicadorAlmacen.ErrorDeEnvioReclamado));
            Assert.IsTrue(linea.Indicadores.Contains(IndicadorAlmacen.ReenviadoSinCoste));
            Assert.IsFalse(linea.Indicadores.Contains(IndicadorAlmacen.AbonadoSinReposicion));
            Assert.AreEqual(950, linea.Reclamaciones.Single().Pruebas.Single().Pedido);
        }

        [TestMethod]
        public void Evaluar_ReenvioDeHacePocosDias_NoEvaluadoPeroConLaPrueba()
        {
            EvaluacionCalidadAlmacen e = ReglaCalidadAlmacen.Evaluar(ConReenvio(DIA.Date.AddDays(-10)), DIA);

            LineaPreparadaAlmacen linea = e.Lineas.Single();
            Assert.IsTrue(linea.NoEvaluados.Contains(IndicadorAlmacen.ErrorDeEnvioReclamado));
            Assert.IsFalse(linea.Indicadores.Contains(IndicadorAlmacen.ErrorDeEnvioReclamado));
            Assert.AreEqual(1, linea.Reclamaciones.Single().Pruebas.Count);
        }

        [TestMethod]
        public void Evaluar_SinAlbaran_ReclamacionNoEvaluada()
        {
            LecturaCalidadAlmacen lectura = Lectura(100);
            lectura.LecturasPicking.Add(Pick(100, "A", "Alfredo", 1));
            lectura.Lineas.Add(Linea(100, 900, 1, "A", 1));

            LineaPreparadaAlmacen linea = ReglaCalidadAlmacen.Evaluar(lectura, DIA).Lineas.Single();

            Assert.IsTrue(linea.NoEvaluados.Contains(IndicadorAlmacen.ErrorDeEnvioReclamado));
            Assert.IsTrue(linea.NoEvaluados.Contains(IndicadorAlmacen.AbonadoSinReposicion));
        }

        [TestMethod]
        public void Oficiales_ErrorDeEnvioReclamadoYFaltaEnPacking()
        {
            CollectionAssert.AreEqual(new[] { IndicadorAlmacen.ErrorDeEnvioReclamado, IndicadorAlmacen.FaltaEnPacking }, InfoIndicadoresAlmacen.Oficiales.ToList());
            Assert.IsFalse(InfoIndicadoresAlmacen.EsError(IndicadorAlmacen.DescuadrePickingPacking));
            Assert.IsFalse(InfoIndicadoresAlmacen.EsError(IndicadorAlmacen.FaltaDeshechaPorOtro));
            Assert.IsFalse(InfoIndicadoresAlmacen.EsError(IndicadorAlmacen.AbonadoSinReposicion));
        }
    }
}
