using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using NestoAPI.Tests.Infrastructure.ChequesRegalo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#593 (c4), pedido servido por partes: el cheque regalo (línea −1) se descuenta en la entrega con la que lo
    /// entregado del pedido SUPERA el mínimo de la campaña. Antes una línea −1 salía siempre en el primer picking.
    /// </summary>
    [TestClass]
    public class ChequesRegaloPickingTests
    {
        private const string CHEQUE = "CHEQUE50_OCT26";

        private static LineaPedidoPicking Producto(int id, string producto, int cantidad, decimal baseImponible) => new LineaPedidoPicking
        {
            Id = id,
            NumeroPedido = 1,
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Almacen = Constantes.Almacenes.ALGETE,
            Producto = producto,
            Cantidad = cantidad,
            BaseImponible = baseImponible,
            FechaEntrega = new DateTime(2026, 10, 20)
        };

        private static LineaPedidoPicking Cheque(int id = 99) => new LineaPedidoPicking
        {
            Id = id,
            NumeroPedido = 1,
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Almacen = Constantes.Almacenes.ALGETE,
            Producto = CHEQUE + "    ",
            Cantidad = -1,
            BaseImponible = -50,
            FechaEntrega = new DateTime(2026, 10, 20)
        };

        private static PedidoPicking Pedido(params LineaPedidoPicking[] lineas) => new PedidoPicking
        {
            Empresa = "1",
            Id = 1,
            ModoServicio = Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
            Lineas = lineas.ToList()
        };

        private static Dictionary<string, ProductoParaChequeRegalo> Productos(string empresa, List<string> codigos)
            => RepositorioCanjeChequesRegalo.Diccionario(new[]
            {
                new ProductoParaChequeRegalo { Numero = CHEQUE, Nombre = "CHEQUE REGALO", Grupo = "COS", Ficticio = true },
                new ProductoParaChequeRegalo { Numero = "A", Nombre = "CREMA", Grupo = "COS" },
                new ProductoParaChequeRegalo { Numero = "B", Nombre = "SERUM", Grupo = "COS" },
                new ProductoParaChequeRegalo { Numero = "PACK", Nombre = "PACK 26 AINHOA", Grupo = "COS" },
                new ProductoParaChequeRegalo { Numero = "PELU", Nombre = "TINTE", Grupo = "PEL" }
            });

        /// <summary>Lo de la ejecución real, sin BD: reservar el stock, rellenar el cheque y decidir.</summary>
        private static void SacarPicking(PedidoPicking pedido, Dictionary<string, int> stock, params LineaEntregadaChequeRegalo[] yaEntregadas)
        {
            var candidatos = new List<PedidoPicking> { pedido };
            ChequesRegaloPicking.Rellenar(candidatos, new List<CampanaCanjeChequeRegalo> { CanjeChequesRegaloTests.Campana() },
                Productos, p => yaEntregadas.ToList());
            List<StockProducto> stocks = pedido.Lineas.Select(l => l.Producto).Distinct()
                .Select(p => new StockProducto { Producto = p, StockDisponible = stock.TryGetValue(p.Trim(), out int s) ? s : 0 })
                .ToList();
            List<LineaPedidoPicking> todas = pedido.Lineas.Select(l => new LineaPedidoPicking
            {
                Id = l.Id, TipoLinea = l.TipoLinea, Almacen = l.Almacen, Producto = l.Producto, Cantidad = l.Cantidad, BaseImponible = l.BaseImponible
            }).ToList();
            GestorReservasStock.Reservar(stocks, candidatos, todas);
            GestorFacturarTodoAhora.Aplicar(candidatos);
            GestorReservasStock.ReservarChequesRegalo(candidatos);
        }

        [TestMethod]
        public void Rellenar_MarcaElChequeYLoQueSumaParaElMinimo()
        {
            PedidoPicking pedido = Pedido(Producto(1, "A", 1, 100), Producto(2, "PACK", 1, 100), Producto(3, "PELU", 1, 100), Cheque());

            ChequesRegaloPicking.Rellenar(new List<PedidoPicking> { pedido }, new List<CampanaCanjeChequeRegalo> { CanjeChequesRegaloTests.Campana() },
                Productos, p => new List<LineaEntregadaChequeRegalo>
                {
                    new LineaEntregadaChequeRegalo { Producto = "B", BaseImponible = 80 },
                    new LineaEntregadaChequeRegalo { Producto = "PELU", BaseImponible = 500 }
                });

            Assert.IsTrue(pedido.Lineas.Single(l => l.Id == 99).EsChequeRegalo);
            Assert.IsFalse(pedido.Lineas.Single(l => l.Id == 99).ComputaMinimoChequeRegalo);
            Assert.IsTrue(pedido.Lineas.Single(l => l.Id == 1).ComputaMinimoChequeRegalo);
            Assert.IsFalse(pedido.Lineas.Single(l => l.Id == 2).ComputaMinimoChequeRegalo, "PACK 26");
            Assert.IsFalse(pedido.Lineas.Single(l => l.Id == 3).ComputaMinimoChequeRegalo, "peluquería");
            Assert.AreEqual(250M, pedido.ChequeRegalo.MinimoCanje);
            Assert.AreEqual(80M, pedido.ChequeRegalo.BaseComputableYaEntregada, "la peluquería ya entregada tampoco suma");
        }

        [TestMethod]
        public void Rellenar_PedidoSinCheque_NoLeeNada()
        {
            PedidoPicking pedido = Pedido(Producto(1, "A", 1, 100));
            bool leido = false;

            ChequesRegaloPicking.Rellenar(new List<PedidoPicking> { pedido }, new List<CampanaCanjeChequeRegalo> { CanjeChequesRegaloTests.Campana() },
                (e, c) => { leido = true; return null; }, p => { leido = true; return null; });

            Assert.IsNull(pedido.ChequeRegalo);
            Assert.IsFalse(leido);
        }

        [TestMethod]
        public void PrimeraEntregaQueNoSuperaElMinimo_ElChequeNoSale()
        {
            // Pedido de 300 €: hoy solo hay stock de A (150 €)
            PedidoPicking pedido = Pedido(Producto(1, "A", 1, 150), Producto(2, "B", 1, 150), Cheque());

            SacarPicking(pedido, new Dictionary<string, int> { ["A"] = 1 });

            Assert.IsFalse(pedido.Lineas.Any(l => l.EsChequeRegalo), "la línea del cheque se queda en el pedido para otra entrega");
            Assert.IsTrue(pedido.ChequeRegalo.Retenido);
            Assert.AreEqual(150M, pedido.ChequeRegalo.BaseComputableConEstaEntrega);
            Assert.IsTrue(new GestorStocksPicking(pedido).HayStockDeAlgo(), "lo demás sale igual");
        }

        [TestMethod]
        public void SegundaEntregaConLaQueSeSuperaElMinimo_ElChequeSale()
        {
            // Ya se entregaron 150 €; hoy salen los otros 150 €
            PedidoPicking pedido = Pedido(Producto(2, "B", 1, 150), Cheque());

            SacarPicking(pedido, new Dictionary<string, int> { ["B"] = 1 },
                new LineaEntregadaChequeRegalo { Producto = "A", BaseImponible = 150 });

            LineaPedidoPicking cheque = pedido.Lineas.Single(l => l.EsChequeRegalo);
            Assert.AreEqual(-1, cheque.CantidadReservada);
            Assert.IsFalse(pedido.ChequeRegalo.Retenido);
            Assert.AreEqual(300M, pedido.ChequeRegalo.BaseComputableConEstaEntrega);
        }

        [TestMethod]
        public void EntregaPequenaQueCruzaElMinimo_SaleConElChequeYLaFacturaQuedaEnNegativo()
        {
            PedidoPicking pedido = Pedido(Producto(2, "B", 1, 20), Cheque());

            SacarPicking(pedido, new Dictionary<string, int> { ["B"] = 1 },
                new LineaEntregadaChequeRegalo { Producto = "A", BaseImponible = 240 });

            Assert.AreEqual(-1, pedido.Lineas.Single(l => l.EsChequeRegalo).CantidadReservada);
            Assert.AreEqual(-30M, pedido.Lineas.Sum(l => l.BaseImponibleEntrega), "20 € de producto menos 50 € del cheque");
        }

        [TestMethod]
        public void TodoEnUnaEntregaQueSuperaElMinimo_ElChequeSaleEnLaPrimera()
        {
            PedidoPicking pedido = Pedido(Producto(1, "A", 2, 300), Cheque());

            SacarPicking(pedido, new Dictionary<string, int> { ["A"] = 2 });

            Assert.AreEqual(-1, pedido.Lineas.Single(l => l.EsChequeRegalo).CantidadReservada);
        }

        [TestMethod]
        public void LoEntregadoJustoEnElMinimo_NoVale()
        {
            PedidoPicking pedido = Pedido(Producto(1, "A", 1, 250), Cheque());

            SacarPicking(pedido, new Dictionary<string, int> { ["A"] = 1 });

            Assert.IsFalse(pedido.Lineas.Any(l => l.EsChequeRegalo));
        }

        [TestMethod]
        public void LoQueNoSumaEnEstaEntrega_NoCuentaAunqueSalga()
        {
            // 200 € de crema y 200 € de peluquería y PACK 26: salen, pero para el cheque solo cuentan los 200 €
            PedidoPicking pedido = Pedido(Producto(1, "A", 1, 200), Producto(2, "PELU", 1, 100), Producto(3, "PACK", 1, 100), Cheque());

            SacarPicking(pedido, new Dictionary<string, int> { ["A"] = 1, ["PELU"] = 1, ["PACK"] = 1 });

            Assert.IsFalse(pedido.Lineas.Any(l => l.EsChequeRegalo));
            Assert.AreEqual(3, pedido.Lineas.Count, "los productos que no suman salen igual");
        }

        [TestMethod]
        public void SeEntregaParteDeUnaLinea_CuentaLaParteQueSale()
        {
            // 10 unidades de 30 € (300 €), hay stock de 9: salen 270 €
            PedidoPicking pedido = Pedido(Producto(1, "A", 10, 300), Cheque());

            SacarPicking(pedido, new Dictionary<string, int> { ["A"] = 9 });

            Assert.AreEqual(270M, pedido.ChequeRegalo.BaseComputableConEstaEntrega);
            Assert.AreEqual(-1, pedido.Lineas.Single(l => l.EsChequeRegalo).CantidadReservada);
        }

        [TestMethod]
        public void FacturarTodoAhora_LoQuePasaARecogerTambienSeFacturaYCuenta()
        {
            // Modo de facturación 3: lo que falta va a Recoger y se factura ya
            PedidoPicking pedido = Pedido(Producto(1, "A", 1, 150), Producto(2, "B", 1, 150), Cheque());
            pedido.ModoFacturacion = Constantes.Pedidos.ModosFacturacion.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;
            pedido.PlazosPago = "CONTADO";

            SacarPicking(pedido, new Dictionary<string, int> { ["A"] = 1 });

            Assert.AreEqual(1, pedido.Lineas.Single(l => l.Id == 2).CantidadARecoger, "B pasa a recoger");
            Assert.AreEqual(300M, pedido.ChequeRegalo.BaseComputableConEstaEntrega);
            Assert.AreEqual(-1, pedido.Lineas.Single(l => l.EsChequeRegalo).CantidadReservada);
        }

        [TestMethod]
        public void SinPrepararElCheque_SeComportaComoAntes()
        {
            // Sin campañas (o si falla la lectura) la línea −1 se reserva en el primer picking, como hasta ahora
            PedidoPicking pedido = Pedido(Producto(1, "A", 1, 100), Cheque());
            var candidatos = new List<PedidoPicking> { pedido };
            GestorReservasStock.Reservar(new List<StockProducto>
            {
                new StockProducto { Producto = "A", StockDisponible = 1 },
                new StockProducto { Producto = CHEQUE + "    " }
            }, candidatos, pedido.Lineas.ToList());

            GestorReservasStock.ReservarChequesRegalo(candidatos);

            Assert.AreEqual(-1, pedido.Lineas.Single(l => l.Id == 99).CantidadReservada);
        }

        [TestMethod]
        public void SoloQuedaElCheque_ElPedidoNoSaleSoloConEl()
        {
            // Todo lo demás ya se entregó (más de 250 €) pero el cheque se quedó atrás: el cheque solo no hace salir el pedido
            PedidoPicking pedido = Pedido(Cheque());

            SacarPicking(pedido, new Dictionary<string, int>(), new LineaEntregadaChequeRegalo { Producto = "A", BaseImponible = 300 });

            Assert.AreEqual(-1, pedido.Lineas.Single().CantidadReservada);
            Assert.IsFalse(new GestorStocksPicking(pedido).HayStockDeAlgo());
        }

        // ===== Pedido 928840 (19/10/26), primer canje en producción =====
        // El cheque se quedó a −1 y sin picking, pero NO lo retuvo el picking de la API (este test, con los datos reales,
        // pasa): lo sacó Ariadna al terminar la salida del picking 99875, porque el mozo lo dio por «No está». Ver
        // ServicioPreparacionAlmacenTests.Picking99875_ElChequeRegaloFicticio_NoSeLeEnsenaAlMozoNiSeQuitaComoFalta.

        private static string P(string codigo) => codigo.PadRight(15);

        private static LineaPedidoPicking Linea928840(int id, string producto, int cantidad, decimal baseImponible, byte tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            string hora = "14:54:48") => new LineaPedidoPicking
        {
            Id = id,
            NumeroPedido = 928840,
            TipoLinea = tipoLinea,
            Almacen = "ALG",
            Producto = P(producto),
            Cantidad = cantidad,
            BaseImponible = baseImponible,
            FechaEntrega = new DateTime(2026, 10, 20),
            FechaModificacion = DateTime.Parse("2026-10-19 " + hora)
        };

        [TestMethod]
        public void Pedido928840_ElPickingDeLaApiSacaElCheque_252Con50SuperaLos250()
        {
            var pedido = new PedidoPicking
            {
                Empresa = "1  ", Id = 928840, ServirJunto = true, ModoServicio = 1, ModoFacturacion = 1, PlazosPago = "CONTADO", Ruta = "00 ",
                Lineas = new List<LineaPedidoPicking>
                {
                    Linea928840(329147600, "38173", 1, 42.30M), Linea928840(329147700, "37503", 1, 52.96M),
                    Linea928840(329147800, "31881", 1, 22.43M), Linea928840(329147900, "40400", 1, 22.95M),
                    Linea928840(329148000, "35612", 1, 16.45M), Linea928840(329148100, "40558", 1, 38.41M),
                    Linea928840(329148200, "24867", 2, 3.60M), Linea928840(329148300, "25973", 1, 12.20M),
                    Linea928840(329148400, "25964", 1, 1.70M), Linea928840(329148500, "40624", 1, 9.50M),
                    Linea928840(329148600, "24884", 1, 6.80M), Linea928840(329148700, "43759", 1, 24.90M),
                    Linea928840(329148800, "62400003", 1, 3.00M, Constantes.TiposLineaVenta.CUENTA_CONTABLE),
                    Linea928840(329148900, "CHEQUE50_OCT26", -1, -50M),
                    Linea928840(329149600, "41307", 1, 0M, hora: "15:10:51")
                }
            };
            var candidatos = new List<PedidoPicking> { pedido };
            var campana = CanjeChequesRegaloTests.Campana();
            campana.Producto = "CHEQUE50_OCT26";
            var nombres = new Dictionary<string, (string, string, bool)>
            {
                ["38173"] = ("ALR LIPO", "COS", false), ["37503"] = ("ALGINATOS", "COS", false), ["31881"] = ("A.E. QUINTA", "COS", false),
                ["40400"] = ("GEL LIMPIADOR", "COS", false), ["35612"] = ("GEL SILICIO", "COS", false), ["40558"] = ("VITAL ENERGY", "COS", false),
                ["24867"] = ("MANTA TERMICA", "ACC", false), ["25973"] = ("ROLLO FILM", "ACC", false), ["25964"] = ("ROLLO PAPEL", "PEL", false),
                ["40624"] = ("PATUCOS", "ACC", false), ["24884"] = ("TOALLA", "ACC", false), ["43759"] = ("SENSORY", "COS", false),
                ["41307"] = ("RODILLO", "ACC", false), ["CHEQUE50_OCT26"] = ("CHEQUE REGALO", "COS", true)
            };
            ChequesRegaloPicking.Rellenar(candidatos, new List<CampanaCanjeChequeRegalo> { campana },
                (e, codigos) => RepositorioCanjeChequesRegalo.Diccionario(codigos.Where(nombres.ContainsKey).Select(c => new ProductoParaChequeRegalo
                { Numero = c, Nombre = nombres[c].Item1, Grupo = nombres[c].Item2, Ficticio = nombres[c].Item3 })),
                p => new List<LineaEntregadaChequeRegalo>());

            var stockReal = new Dictionary<string, int> { ["24867"] = 9, ["24884"] = 30, ["25964"] = 96, ["25973"] = 2, ["31881"] = 2, ["35612"] = 449,
                ["37503"] = 1, ["38173"] = 2, ["40400"] = 116, ["40558"] = 2, ["40624"] = 3, ["41307"] = 54, ["43759"] = 3 };
            List<StockProducto> stocks = pedido.Lineas.Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO).Select(l => l.Producto).Distinct()
                .Select(p => new StockProducto { Producto = p, StockDisponible = stockReal.TryGetValue(p.Trim(), out int s) ? s : 0 }).ToList();
            List<LineaPedidoPicking> todas = pedido.Lineas.Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO).Select(l => new LineaPedidoPicking
            {
                Id = l.Id, TipoLinea = l.TipoLinea, Almacen = l.Almacen, Producto = l.Producto, Cantidad = l.Cantidad, BaseImponible = l.BaseImponible,
                FechaEntrega = l.FechaEntrega, FechaModificacion = l.FechaModificacion
            }).OrderBy(l => l.FechaModificacion).ThenBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, todas);
            GestorReposicionTiendas.MarcarEsperas(stocks, candidatos, todas);
            GestorReservasStock.BorrarLineasQueNoDebenSalir(candidatos, new DateTime(2026, 10, 20));
            GestorFacturarTodoAhora.Aplicar(candidatos);
            GestorReservasStock.ReservarChequesRegalo(candidatos);

            Assert.AreEqual(252.50M, pedido.ChequeRegalo.BaseComputableConEstaEntrega);
            Assert.IsFalse(pedido.ChequeRegalo.Retenido);
            Assert.AreEqual(-1, pedido.Lineas.Single(l => l.EsChequeRegalo).CantidadReservada, "el cheque entra en el picking con el resto");
            Assert.IsTrue(GestorPicking.DecidirSiSale(pedido));
        }
    }
}
