using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Picking;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#482, modo 3 «tras reponer de tiendas»: el pedido espera solo el stock que LE
    /// CORRESPONDE por orden de picking (Fecha_Modificación, luego Nº Orden) y que aún no está en
    /// Algete. Los casos son los de Carlos (16/09/26).
    /// </summary>
    [TestClass]
    public class GestorReposicionTiendasTest
    {
        private const byte MODO_3 = Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS;
        private static readonly DateTime ANTES = new DateTime(2026, 9, 15, 10, 0, 0);
        private static readonly DateTime DESPUES = new DateTime(2026, 9, 16, 10, 0, 0);

        private static LineaPedidoPicking Linea(int id, string producto, int cantidad, DateTime modificacion, string almacen = "ALG", int reservada = 0)
        {
            return new LineaPedidoPicking
            {
                Id = id,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                Cantidad = cantidad,
                CantidadReservada = reservada,
                Almacen = almacen,
                FechaModificacion = modificacion
            };
        }

        private static PedidoPicking PedidoModo3(params LineaPedidoPicking[] lineas)
        {
            return new PedidoPicking { Id = 1, ModoServicio = MODO_3, Lineas = lineas.ToList() };
        }

        private static List<StockProducto> Stocks(int algete, int tiendas, int enCamino = 0)
        {
            return new List<StockProducto> { new StockProducto { Producto = "A", StockDisponible = algete, StockTienda = tiendas, EnCamino = enCamino } };
        }

        [TestMethod]
        public void StockLibreEnLaTienda_ElPedidoEspera()
        {
            LineaPedidoPicking mia = Linea(10, "A", 1, DESPUES);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(algete: 0, tiendas: 5), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { mia });

            Assert.IsTrue(pedido.EsperaReposicionDeTiendas);
            Assert.IsFalse(pedido.saleEnPicking(), "Mientras espera, no sale");
        }

        [TestMethod]
        public void StockDeLaTiendaComprometidoPorUnPedidoAnterior_NoLoEspera()
        {
            // Carlos: 5 en tienda, otro pedido de 6 delante del mío (de 1): esas 5 no son mías.
            LineaPedidoPicking otra = Linea(5, "A", 6, ANTES);
            LineaPedidoPicking mia = Linea(10, "A", 1, DESPUES);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(0, 5), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { otra, mia });

            Assert.IsFalse(pedido.EsperaReposicionDeTiendas);
        }

        [TestMethod]
        public void StockDeLaTiendaQueSobraTrasElPedidoAnterior_SiLoEspera()
        {
            // 5 en tienda, 3 delante: me corresponden 2 y no tengo nada reservado en Algete.
            LineaPedidoPicking otra = Linea(5, "A", 3, ANTES);
            LineaPedidoPicking mia = Linea(10, "A", 1, DESPUES);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(0, 5), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { otra, mia });

            Assert.IsTrue(pedido.EsperaReposicionDeTiendas);
        }

        [TestMethod]
        public void ElOrdenEsElDelPicking_FechaModificacionYLuegoNumeroDeOrden()
        {
            // Mismo día: manda el Nº Orden. El de Nº Orden 20 (posterior a mi 10) NO va delante.
            LineaPedidoPicking posterior = Linea(20, "A", 6, DESPUES);
            LineaPedidoPicking mia = Linea(10, "A", 1, DESPUES);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(0, 5), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { mia, posterior });

            Assert.IsTrue(pedido.EsperaReposicionDeTiendas, "Las 5 son mías: el otro pedido va detrás");
        }

        [TestMethod]
        public void StockEnCaminoHaciaAlgete_HayQueEsperarlo()
        {
            LineaPedidoPicking mia = Linea(10, "A", 2, DESPUES);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(algete: 0, tiendas: 0, enCamino: 2), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { mia });

            Assert.IsTrue(pedido.EsperaReposicionDeTiendas);
        }

        [TestMethod]
        public void StockEnCaminoHaciaLaTienda_QueLaTiendaNoNecesita_Volvera_SeEspera()
        {
            // En camino hacia Reina sin pendientes en Reina: cuenta como stock que volverá.
            LineaPedidoPicking mia = Linea(10, "A", 1, DESPUES);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(0, 0, enCamino: 3), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { mia });

            Assert.IsTrue(pedido.EsperaReposicionDeTiendas);
        }

        [TestMethod]
        public void StockEnCaminoHaciaLaTienda_QueLaTiendaSiNecesita_NoSeEspera()
        {
            // Los pendientes de Reina (anteriores) se lo quedan: no volverá.
            LineaPedidoPicking deReina = Linea(5, "A", 3, ANTES, almacen: "REI");
            LineaPedidoPicking mia = Linea(10, "A", 1, DESPUES);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(0, 0, enCamino: 3), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { deReina, mia });

            Assert.IsFalse(pedido.EsperaReposicionDeTiendas);
        }

        [TestMethod]
        public void LineaYaReservadaEnteraEnAlgete_NoEsperaAunqueHayaStockEnTienda()
        {
            LineaPedidoPicking mia = Linea(10, "A", 2, DESPUES, reservada: 2);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(algete: 2, tiendas: 5), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { mia });

            Assert.IsFalse(pedido.EsperaReposicionDeTiendas);
            Assert.IsTrue(pedido.saleEnPicking());
        }

        [TestMethod]
        public void LineaReservadaAMedias_ConElRestoEnLaTienda_Espera()
        {
            // 1 reservada en Algete de 3; en la tienda hay 2 que son mías: se espera a que lleguen.
            LineaPedidoPicking mia = Linea(10, "A", 3, DESPUES, reservada: 1);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(algete: 1, tiendas: 2), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { mia });

            Assert.IsTrue(pedido.EsperaReposicionDeTiendas);
        }

        [TestMethod]
        public void SinNadaQueTraer_ElPedidoSaleConLoQueHay_ComoElModo2()
        {
            // Dos líneas: una reservada entera, otra sin stock en ningún sitio → nada que esperar.
            LineaPedidoPicking servida = Linea(10, "A", 2, DESPUES, reservada: 2);
            LineaPedidoPicking sinStock = Linea(11, "B", 1, DESPUES);
            PedidoPicking pedido = PedidoModo3(servida, sinStock);
            List<StockProducto> stocks = Stocks(2, 0);
            stocks.Add(new StockProducto { Producto = "B", StockDisponible = 0, StockTienda = 0 });

            GestorReposicionTiendas.MarcarEsperas(stocks, new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { servida, sinStock });

            Assert.IsFalse(pedido.EsperaReposicionDeTiendas);
            Assert.IsTrue(pedido.saleEnPicking(), "El modo 3 no exige stock de todo: sale la línea servida");
        }

        [TestMethod]
        public void SoloSeMarcanLosPedidosEnModo3()
        {
            LineaPedidoPicking mia = Linea(10, "A", 1, DESPUES);
            PedidoPicking modo2 = new PedidoPicking { Id = 2, ModoServicio = Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, Lineas = new List<LineaPedidoPicking> { mia } };
            PedidoPicking sinModoServirJunto = new PedidoPicking { Id = 3, ServirJunto = true, Lineas = new List<LineaPedidoPicking> { Linea(11, "A", 1, DESPUES) } };

            GestorReposicionTiendas.MarcarEsperas(Stocks(0, 5), new List<PedidoPicking> { modo2, sinModoServirJunto }, new List<LineaPedidoPicking> { mia });

            Assert.IsFalse(modo2.EsperaReposicionDeTiendas);
            Assert.IsFalse(sinModoServirJunto.EsperaReposicionDeTiendas);
        }

        [TestMethod]
        public void ProductoSinFilaDeStock_NoRevienta_NoEspera()
        {
            LineaPedidoPicking mia = Linea(10, "Z", 1, DESPUES);
            PedidoPicking pedido = PedidoModo3(mia);

            GestorReposicionTiendas.MarcarEsperas(Stocks(0, 5), new List<PedidoPicking> { pedido }, new List<LineaPedidoPicking> { mia });

            Assert.IsFalse(pedido.EsperaReposicionDeTiendas);
        }

        [TestMethod]
        public void Clonar_ConservaElPoolAntesDeRepartir()
        {
            StockProducto original = new StockProducto { Producto = "A", StockDisponible = 3, StockTienda = 4, EnCamino = 1 };
            StockProducto copia = original.Clonar();
            original.StockDisponible = 0;

            Assert.AreEqual(3, copia.StockDisponible);
            Assert.AreEqual(4, copia.StockTienda);
            Assert.AreEqual(1, copia.EnCamino);
        }
    }
}
