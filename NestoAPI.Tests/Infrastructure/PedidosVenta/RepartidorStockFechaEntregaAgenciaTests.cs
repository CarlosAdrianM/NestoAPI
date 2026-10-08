using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>NestoAPI#606: de dónde sale cada unidad (Algete, tiendas, en camino, proveedor, sin fecha).</summary>
    [TestClass]
    public class RepartidorStockFechaEntregaAgenciaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 13);
        private static readonly string[] REINA_PRIMERO = { "REI", "ALC" };

        private static DatosSombraModoServicio Datos() => new DatosSombraModoServicio();

        private static void Stock(DatosSombraModoServicio datos, string producto, string almacen, int stock, int pendientes = 0)
        {
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave(producto, almacen), stock);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave(producto), stock);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave(producto, almacen), pendientes);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave(producto), pendientes);
        }

        private static LineaPedidoFechaEntregaAgencia Linea(string producto, int cantidad, string almacen = "ALG", bool enPicking = false) =>
            new LineaPedidoFechaEntregaAgencia { Producto = producto, Cantidad = cantidad, Almacen = almacen, BaseImponible = 10, YaEnPicking = enPicking };

        [TestMethod]
        public void TodoEnAlgete()
        {
            DatosSombraModoServicio datos = Datos();
            Stock(datos, "A", "ALG", 5, pendientes: 2);

            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("A", 3) }, datos, REINA_PRIMERO, HOY).Single();

            Assert.AreEqual(3, linea.EnAlgete);
            Assert.AreEqual(0, linea.EnTiendas.Count);
        }

        [TestMethod]
        public void LoQueFaltaEnAlgete_SeCogeDeLasTiendasEnElOrdenDado_YLoQueNoTienenLibreYaVieneDeCamino()
        {
            DatosSombraModoServicio datos = Datos();
            Stock(datos, "A", "ALG", 1);
            Stock(datos, "A", "REI", 2, pendientes: 1);   // 1 libre en Reina
            Stock(datos, "A", "ALC", 1);                  // 1 libre en Alcobendas
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteReposicion, ResumenStocksProductos.Clave("A"), 1); // 1 en camino

            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("A", 4) }, datos, new[] { "ALC", "REI" }, HOY).Single();

            Assert.AreEqual(1, linea.EnAlgete);
            Assert.AreEqual(1, linea.EnTiendas["ALC"]);
            Assert.AreEqual(1, linea.EnTiendas["REI"]);
            Assert.AreEqual(1, linea.EnCaminoDeTiendas);
        }

        [TestMethod]
        public void LoQueLlegaDelProveedor_ConSuFechaPrevista()
        {
            DatosSombraModoServicio datos = Datos();
            datos.PendienteRecibir[ResumenStocksProductos.Clave("A", "ALG")] = 5;
            datos.FechaPrevista[ResumenStocksProductos.Clave("A", "ALG")] = new DateTime(2026, 10, 16);

            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("A", 2) }, datos, REINA_PRIMERO, HOY).Single();

            Assert.AreEqual(2, linea.DelProveedor);
            Assert.AreEqual(new DateTime(2026, 10, 16), linea.FechaProveedor);
        }

        [TestMethod]
        public void SobrePedidoSinPedidoAlProveedor_SinFechaYDiceElMotivo()
        {
            DatosSombraModoServicio datos = Datos();
            datos.Estados[ResumenStocksProductos.Clave("A")] = Constantes.Productos.ESTADO_SOBRE_PEDIDO;

            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("A", 2) }, datos, REINA_PRIMERO, HOY).Single();

            Assert.AreEqual(0, linea.EnAlgete + linea.DelProveedor + linea.EnCaminoDeTiendas + linea.EnTiendas.Values.Sum());
            StringAssert.Contains(linea.MotivoSinFecha, "sobre pedido");
        }

        [TestMethod]
        public void DosLineasDelMismoProducto_LaQueYaTienePickingSeQuedaConLoDeAlgete()
        {
            DatosSombraModoServicio datos = Datos();
            Stock(datos, "A", "ALG", 2);
            Stock(datos, "A", "REI", 5);

            List<LineaFechaEntregaAgencia> lineas = RepartidorStockFechaEntregaAgencia.Repartir(
                new[] { Linea("A", 2), Linea("A", 2, enPicking: true) }, datos, REINA_PRIMERO, HOY);

            Assert.AreEqual(0, lineas[0].EnAlgete, "La normal (en el orden recibido)");
            Assert.AreEqual(2, lineas[0].EnTiendas["REI"]);
            Assert.IsTrue(lineas[1].YaEnPicking);
            Assert.AreEqual(2, lineas[1].EnAlgete);
        }

        [TestMethod]
        public void LasLineasDeTienda_NoSalenPorAgencia()
        {
            DatosSombraModoServicio datos = Datos();
            Stock(datos, "A", "REI", 5);

            List<LineaFechaEntregaAgencia> lineas = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("A", 1, "REI") }, datos, REINA_PRIMERO, HOY);

            Assert.AreEqual(0, lineas.Count);
        }
    }
}
