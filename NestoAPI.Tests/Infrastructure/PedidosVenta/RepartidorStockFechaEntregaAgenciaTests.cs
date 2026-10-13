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

        // ----- NestoAPI#606 (kits, Carlos 13/10/26): lo que falta en Algete puede salir desmontando un kit -----

        private static void Kit(DatosSombraModoServicio datos, string kit, int stockAlgete, int pendientesAlgete, params (string Componente, int Cantidad)[] componentes)
        {
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave(kit, "ALG"), stockAlgete);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave(kit, "ALG"), pendientesAlgete);
            foreach (var c in componentes)
            {
                datos.Kits.Add(new ComponenteKitFechaEntregaAgencia { Kit = kit, Componente = c.Componente, Cantidad = c.Cantidad });
            }
        }

        /// <summary>
        /// Pedido 928288 (13/10/26): 70 × 43192 VEGAN GUANTES INDIVIDUAL, 64 en Algete. El correo decía «Reposición Reina →
        /// Algete… el jueves», pero el kit 43191 (70 × 43192) tenía 11 en Algete: desmontando uno sale hoy.
        /// </summary>
        private static DatosSombraModoServicio Datos928288()
        {
            DatosSombraModoServicio datos = Datos();
            Stock(datos, "43192", "ALG", 64);
            Stock(datos, "43192", "REI", 10);
            Kit(datos, "43191", 11, 0, ("43192", 70));
            Kit(datos, "44491", 0, 0, ("43192", 5), ("37126", 1));
            return datos;
        }

        [TestMethod]
        public void Kits_Pedido928288_LoQueFaltaSaleDesmontandoElKitAntesQueDeReina()
        {
            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("43192", 70) }, Datos928288(), REINA_PRIMERO, HOY).Single();

            Assert.AreEqual(64, linea.EnAlgete);
            Assert.AreEqual(6, linea.DeKits["43191"]);
            Assert.IsFalse(linea.DeKits.ContainsKey("44491"), "Sin stock de ese kit");
            Assert.AreEqual(0, linea.EnTiendas.Count, "Ya no hay que traer nada de Reina");
        }

        [TestMethod]
        public void Kits_ElKitQueDebenOtrosPedidos_NoSeDesmonta()
        {
            DatosSombraModoServicio datos = Datos();
            Stock(datos, "43192", "ALG", 64);
            Stock(datos, "43192", "REI", 10);
            Kit(datos, "43191", 1, 1, ("43192", 70)); // el único kit lo debe otro pedido

            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("43192", 70) }, datos, REINA_PRIMERO, HOY).Single();

            Assert.AreEqual(0, linea.DeKits.Count);
            Assert.AreEqual(6, linea.EnTiendas["REI"]);
        }

        [TestMethod]
        public void Kits_ElKitQuePideElPropioPedido_NoSeDesmonta()
        {
            DatosSombraModoServicio datos = Datos();
            Stock(datos, "43192", "ALG", 64);
            Kit(datos, "43191", 1, 0, ("43192", 70));

            List<LineaFechaEntregaAgencia> lineas = RepartidorStockFechaEntregaAgencia.Repartir(
                new[] { Linea("43192", 70), Linea("43191", 1) }, datos, REINA_PRIMERO, HOY);

            Assert.AreEqual(0, lineas[0].DeKits.Count, "El kit se lo lleva el propio pedido entero");
            Assert.AreEqual(1, lineas[1].EnAlgete);
        }

        [TestMethod]
        public void Kits_LoQueYaDebenOtrosPedidosDelComponente_SeCubreAntesConLosKits()
        {
            DatosSombraModoServicio datos = Datos();
            Stock(datos, "43192", "ALG", 0, pendientes: 70); // otros pedidos esperan 70 que no hay
            Stock(datos, "43192", "REI", 100);
            Kit(datos, "43191", 1, 0, ("43192", 70));

            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("43192", 6) }, datos, REINA_PRIMERO, HOY).Single();

            Assert.AreEqual(0, linea.DeKits.Count, "El único kit es para los que esperaban antes");
            Assert.AreEqual(6, linea.EnTiendas["REI"]);
        }

        [TestMethod]
        public void Kits_DosProductosDelMismoKit_DesmontanUnoSolo()
        {
            DatosSombraModoServicio datos = Datos();
            Kit(datos, "45494", 1, 0, ("44371", 1), ("44372", 1), ("44373", 1));

            List<LineaFechaEntregaAgencia> lineas = RepartidorStockFechaEntregaAgencia.Repartir(
                new[] { Linea("44372", 1), Linea("44373", 1) }, datos, REINA_PRIMERO, HOY);

            Assert.AreEqual(1, lineas[0].DeKits["45494"]);
            Assert.AreEqual(1, lineas[1].DeKits["45494"], "Lo suelto del kit ya desmontado");
        }

        [TestMethod]
        public void Kits_VariosKitsPequenos_SeDesmontanLosQueHaganFaltaYElRestoSigueSuCamino()
        {
            DatosSombraModoServicio datos = Datos();
            Stock(datos, "43192", "REI", 10);
            Kit(datos, "44491", 3, 0, ("43192", 5), ("37126", 1));

            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("43192", 17) }, datos, REINA_PRIMERO, HOY).Single();

            Assert.AreEqual(15, linea.DeKits["44491"], "Los 3 kits");
            Assert.AreEqual(2, linea.EnTiendas["REI"]);
        }

        [TestMethod]
        public void Kits_EntreVariosKits_PrimeroElQueMasLleva()
        {
            DatosSombraModoServicio datos = Datos928288();
            Kit(datos, "44492", 5, 0, ("43192", 5));

            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("43192", 70) }, datos, REINA_PRIMERO, HOY).Single();

            Assert.AreEqual(6, linea.DeKits["43191"]);
            Assert.AreEqual(1, linea.DeKits.Count);
        }
    }
}
