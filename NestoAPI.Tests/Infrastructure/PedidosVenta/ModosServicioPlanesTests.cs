using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using M = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Carlos 19/11/26 («mejor verlo como casos individuales»): en Algete los modos se ofrecen comparando lo que haría cada uno con el pedido
    /// (su plan de entregas). De cada grupo de modos con el mismo plan se ofrece uno (1 &gt; 4 &gt; 3 &gt; 2); el 3 necesita algo de las tiendas,
    /// el 4 algo que servir ahora; #561 sigue (nada ahora ni en tiendas → solo «Todo junto»); el sugerido siempre es uno de los ofrecidos.
    /// </summary>
    [TestClass]
    public class ModosServicioPlanesTests
    {
        private static ResumenStocksProductos Resumen(params (string Producto, string Almacen, int Stock)[] stocks)
        {
            var r = new ResumenStocksProductos();
            foreach (var s in stocks)
            {
                ResumenStocksProductos.Sumar(r.StockAlmacen, ResumenStocksProductos.Clave(s.Producto, s.Almacen), s.Stock);
                ResumenStocksProductos.Sumar(r.StockSedes, ResumenStocksProductos.Clave(s.Producto), s.Stock);
            }
            return r;
        }

        private static PedidoVentaDTO Pedido(string almacen, params (string Producto, int Cantidad)[] lineas)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "15191", contacto = "0", Lineas = new List<LineaPedidoVentaDTO>() };
            int id = 1;
            foreach (var l in lineas)
            {
                pedido.Lineas.Add(new LineaPedidoVentaDTO { id = id++, Producto = l.Producto, Cantidad = l.Cantidad, PrecioUnitario = 10, almacen = almacen, tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });
            }
            return pedido;
        }

        private static SugeridorModoServicio.Sugerencia Sugerir(PedidoVentaDTO pedido, ResumenStocksProductos resumen)
            => SugeridorModoServicio.Sugerir(pedido, new GestorStocksSombra(resumen));

        // ================================================================ los casos de Carlos

        [TestMethod]
        public void CasoA_UnaVerdeYOtraEnteraEnReina_NoSeOfreceTrasReponer_YSeSugiereTodoJunto()
        {
            var resumen = Resumen(("V", "ALG", 5), ("R", "REI", 1));

            SugeridorModoServicio.Sugerencia s = Sugerir(Pedido("ALG", ("V", 1), ("R", 1)), resumen);

            CollectionAssert.DoesNotContain(s.ModosPermitidos, M.TRAS_REPONER_DE_TIENDAS);
            Assert.AreEqual("Todo lo que falta viene de las tiendas: es lo mismo que «Todo junto».", s.Modos.Single(m => m.Modo == M.TRAS_REPONER_DE_TIENDAS).Motivo);
            Assert.AreEqual(M.TODO_JUNTO, s.Modo, "Antes sugería «Tras reponer de tiendas»");
            CollectionAssert.Contains(s.ModosPermitidos, s.Modo);
            CollectionAssert.AreEquivalent(new List<byte> { M.TODO_JUNTO, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ }, s.ModosPermitidos,
                "«Según vaya entrando» hace lo mismo que el 4 (lo de ahora y lo de Reina)");
        }

        [TestMethod]
        public void CasoB_DosUnidadesUnaEnReinaYNadaMas_SiSeOfreceTrasReponer()
        {
            var resumen = Resumen(("B", "REI", 1));

            SugeridorModoServicio.Sugerencia s = Sugerir(Pedido("ALG", ("B", 2)), resumen);

            Assert.AreEqual(1, s.LineasRojas, "La línea es roja (parcialmente cubrible)");
            CollectionAssert.Contains(s.ModosPermitidos, M.TRAS_REPONER_DE_TIENDAS, "Hay algo que traer y traerlo no completa el pedido");
            CollectionAssert.Contains(s.ModosPermitidos, M.TODO_JUNTO);
            CollectionAssert.DoesNotContain(s.ModosPermitidos, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, "No hay nada que servir ahora");
            Assert.AreEqual(ModosServicioPermitidos.MOTIVO_NADA_AHORA, s.Modos.Single(m => m.Modo == M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ).Motivo);
            CollectionAssert.DoesNotContain(s.ModosPermitidos, M.SEGUN_VAYA_ENTRANDO, "Hace lo mismo que el 3: la de Reina y luego la otra");
            CollectionAssert.Contains(s.ModosPermitidos, s.Modo);
        }

        [TestMethod]
        public void TodoEnAlgete_SoloTodoJunto()
        {
            SugeridorModoServicio.Sugerencia s = Sugerir(Pedido("ALG", ("V", 2), ("W", 1)), Resumen(("V", "ALG", 2), ("W", "ALG", 3)));

            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO }, s.ModosPermitidos);
        }

        [TestMethod]
        public void Tienda_SoloSegunVayaEntrando_NoCambia()
        {
            SugeridorModoServicio.Sugerencia s = Sugerir(Pedido("REI", ("V", 2)), Resumen());

            CollectionAssert.AreEqual(new List<byte> { M.SEGUN_VAYA_ENTRANDO }, s.ModosPermitidos);
        }

        [TestMethod]
        public void FaltaUnaSolaUnidad_NoSeOfrecenALaVezElDosYElCuatro()
        {
            SugeridorModoServicio.Sugerencia s = Sugerir(Pedido("ALG", ("V", 1), ("X", 2)), Resumen(("V", "ALG", 1), ("X", "ALG", 1)));

            Assert.IsFalse(s.ModosPermitidos.Contains(M.SEGUN_VAYA_ENTRANDO) && s.ModosPermitidos.Contains(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ));
            CollectionAssert.AreEquivalent(new List<byte> { M.TODO_JUNTO, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ }, s.ModosPermitidos);
            StringAssert.StartsWith(s.Modos.Single(m => m.Modo == M.SEGUN_VAYA_ENTRANDO).Motivo, "Solo falta una unidad");
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, s.Modo);
        }

        [TestMethod]
        public void FaltanVariasUnidades_SeOfrecenElDosYElCuatro()
        {
            SugeridorModoServicio.Sugerencia s = Sugerir(Pedido("ALG", ("V", 1), ("X", 3)), Resumen(("V", "ALG", 1), ("X", "ALG", 1)));

            CollectionAssert.AreEquivalent(new List<byte> { M.TODO_JUNTO, M.SEGUN_VAYA_ENTRANDO, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ }, s.ModosPermitidos);
        }

        [TestMethod]
        public void NadaEnNingunSitio_VariasLineas_TodoJuntoYSegunVayaEntrando()
        {
            // Carlos 19/11/26: cada línea puede llegar un día distinto; el 3 y el 4 siguen sin sentido
            SugeridorModoServicio.Sugerencia s = Sugerir(Pedido("ALG", ("X", 1), ("Y", 2)), Resumen());

            CollectionAssert.AreEquivalent(new List<byte> { M.TODO_JUNTO, M.SEGUN_VAYA_ENTRANDO }, s.ModosPermitidos);
            Assert.AreEqual(M.TODO_JUNTO, s.Modo, "El sugerido no cambia");
            Assert.AreEqual(ModosServicioPermitidos.MOTIVO_SIN_ROSAS, s.Modos.Single(m => m.Modo == M.TRAS_REPONER_DE_TIENDAS).Motivo);
            Assert.AreEqual(ModosServicioPermitidos.MOTIVO_NADA_CON_STOCK, s.Modos.Single(m => m.Modo == M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ).Motivo);
        }

        [TestMethod]
        public void Regla561_NadaEnNingunSitio_UnaSolaLinea_SoloTodoJunto_AunqueSeanVariasUnidades()
        {
            SugeridorModoServicio.Sugerencia s = Sugerir(Pedido("ALG", ("Y", 3)), Resumen());

            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO }, s.ModosPermitidos);
            Assert.AreEqual(ModosServicioPermitidos.MOTIVO_NADA_CON_STOCK, s.Modos.Single(m => m.Modo == M.SEGUN_VAYA_ENTRANDO).Motivo);
        }

        [TestMethod]
        public void ReglaSegunVayaEntrandoSinNadaDisponible_SoloConVariasLineasSinCubrirYNadaAhoraNiEnTiendas()
        {
            HechosModoServicio Hechos(int ahora, int tiendas, params int[] sinCubrir)
            {
                var h = new HechosModoServicio { HayAlgoAhora = ahora > 0 };
                h.Lineas.Add(new HechosModoServicio.Linea { Producto = "A", Cantidad = ahora + tiendas, Ahora = ahora, DeTiendas = tiendas });
                for (int i = 0; i < sinCubrir.Length; i++)
                {
                    h.Lineas.Add(new HechosModoServicio.Linea { Producto = "P" + i, Cantidad = sinCubrir[i] });
                }
                return h;
            }

            Assert.IsTrue(ModosServicioPermitidos.OfreceSegunVayaEntrandoSinNadaDisponible(Hechos(0, 0, 1, 1)));
            Assert.IsFalse(ModosServicioPermitidos.OfreceSegunVayaEntrandoSinNadaDisponible(Hechos(0, 0, 5)));
            Assert.IsFalse(ModosServicioPermitidos.OfreceSegunVayaEntrandoSinNadaDisponible(Hechos(1, 0, 1, 1)), "Con algo ahora manda la comparación de planes");
            Assert.IsFalse(ModosServicioPermitidos.OfreceSegunVayaEntrandoSinNadaDisponible(Hechos(0, 1, 1, 1)));
            Assert.IsFalse(ModosServicioPermitidos.OfreceSegunVayaEntrandoSinNadaDisponible(null));
        }

        [TestMethod]
        public void VerdeRosaYRoja_LosCuatroHacenAlgoDistinto()
        {
            SugeridorModoServicio.Sugerencia s = Sugerir(Pedido("ALG", ("V", 1), ("R", 1), ("X", 1)), Resumen(("V", "ALG", 1), ("R", "REI", 1)));

            Assert.AreEqual(4, s.ModosPermitidos.Count);
            Assert.AreEqual(M.TRAS_REPONER_DE_TIENDAS, s.Modo);
        }

        // ================================================================ la tabla entera

        [TestMethod]
        public void Tabla_TodasLasCombinaciones_SiempreHayUnoYElSugeridoEsUnoDeLosOfrecidos()
        {
            var sinCubrir = new List<int[]> { new int[0], new[] { 1 }, new[] { 2 }, new[] { 1, 1 }, new[] { 1, 3 } };
            foreach (int ahora in new[] { 0, 1, 2 })
            foreach (int tiendas in new[] { 0, 1, 2 })
            foreach (int[] p in sinCubrir)
            {
                var hechos = new HechosModoServicio { HayAlgoAhora = ahora > 0 };
                hechos.Lineas.Add(new HechosModoServicio.Linea { Producto = "A", Cantidad = ahora + tiendas, Ahora = ahora, DeTiendas = tiendas });
                for (int i = 0; i < p.Length; i++)
                {
                    hechos.Lineas.Add(new HechosModoServicio.Linea { Producto = "P" + i, Cantidad = p[i] });
                }
                string caso = $"ahora {ahora}, tiendas {tiendas}, sin cubrir [{string.Join(",", p)}]";
                List<ModoServicioPermitidoDTO> modos = ModosServicioPermitidos.CalcularPorPlanes(ModosServicioPermitidos.TipoAlmacen.Central, hechos, true);
                List<byte> ofrecidos = ModosServicioPermitidos.Permitidos(modos);

                CollectionAssert.Contains(ofrecidos, M.TODO_JUNTO, caso);
                Assert.IsTrue(modos.Where(m => !m.Permitido).All(m => !string.IsNullOrWhiteSpace(m.Motivo)), caso);
                Assert.AreEqual(ofrecidos.Count, ofrecidos.Select(m => PlanEntregasModoServicio.Plan(m, hechos)).Distinct().Count(), "Dos ofrecidos iguales: " + caso);
                if (ofrecidos.Contains(M.TRAS_REPONER_DE_TIENDAS))
                {
                    Assert.IsTrue(tiendas > 0, "El 3 sin nada que traer: " + caso);
                }
                if (ofrecidos.Contains(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ))
                {
                    Assert.IsTrue(ahora > 0, "El 4 sin nada que servir ahora: " + caso);
                }
                if (ahora == 0 && tiendas == 0)
                {
                    // #561, con el matiz de Carlos del 19/11/26: varias líneas sin cubrir → también «Según vaya entrando»
                    CollectionAssert.AreEquivalent(p.Length >= 2 ? new List<byte> { M.TODO_JUNTO, M.SEGUN_VAYA_ENTRANDO } : new List<byte> { M.TODO_JUNTO },
                        ofrecidos, "#561: " + caso);
                }
                if (ahora > 0 && tiendas > 0 && p.Length == 0)
                {
                    CollectionAssert.DoesNotContain(ofrecidos, M.TRAS_REPONER_DE_TIENDAS, "Caso A: " + caso);
                }
                if (ahora == 0 && tiendas > 0 && p.Length > 0)
                {
                    CollectionAssert.Contains(ofrecidos, M.TRAS_REPONER_DE_TIENDAS, "Caso B: " + caso);
                }
                if (p.Length == 1 && p[0] == 1 && tiendas == 0 && ahora > 0)
                {
                    Assert.IsFalse(ofrecidos.Contains(M.SEGUN_VAYA_ENTRANDO) && ofrecidos.Contains(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ), "Una unidad: " + caso);
                }
                foreach (byte sugerido in new[] { M.TODO_JUNTO, M.SEGUN_VAYA_ENTRANDO, M.TRAS_REPONER_DE_TIENDAS, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ })
                {
                    CollectionAssert.Contains(ofrecidos, ModosServicioPermitidos.Ofrecido(sugerido, modos, hechos), $"Sugerido {sugerido}: {caso}");
                }
            }
        }

        // ================================================================ al guardar

        [TestMethod]
        public void CasoA_PedidoSinModo_NaceEnTodoJunto_SinRechazo()
        {
            var resumen = Resumen(("V", "ALG", 5), ("R", "REI", 1));
            PedidoVentaDTO pedido = Pedido("ALG", ("V", 1), ("R", 1));

            Assert.IsNull(ValidadorModoServicio.ComprobarAlCrear(pedido, new GestorStocksSombra(resumen)), "Sin modo (NestoApp, tienda) no se mira");
            Assert.AreEqual(M.TODO_JUNTO, SugeridorModoServicio.Sugerir(pedido, new GestorStocksSombra(resumen)).Modo);
        }

        [TestMethod]
        public void CasoA_ElegidoTrasReponer_SeTrataComoTodoVerde_400ConElQueVale()
        {
            var resumen = Resumen(("V", "ALG", 5), ("R", "REI", 1));
            PedidoVentaDTO pedido = Pedido("ALG", ("V", 1), ("R", 1));
            pedido.modoServicio = M.TRAS_REPONER_DE_TIENDAS;

            var ex = Assert.ThrowsException<ModoServicioNoPermitidoException>(() => ValidadorModoServicio.ComprobarAlCrear(pedido, new GestorStocksSombra(resumen)));
            Assert.AreEqual(M.TODO_JUNTO, ex.ModoSugerido);
        }

        [TestMethod]
        public void CasoA_PedidoYaGrabadoEnTrasReponer_SeGuardaOtraVezSinError()
        {
            var resumen = Resumen(("V", "ALG", 5), ("R", "REI", 1));
            PedidoVentaDTO pedido = Pedido("ALG", ("V", 1), ("R", 1));
            pedido.numero = 931000;
            pedido.modoServicio = M.TRAS_REPONER_DE_TIENDAS;

            Assert.IsNull(ValidadorModoServicio.ComprobarAlModificar(pedido, M.TRAS_REPONER_DE_TIENDAS, M.TRAS_REPONER_DE_TIENDAS, new GestorStocksSombra(resumen)));
        }
    }
}
