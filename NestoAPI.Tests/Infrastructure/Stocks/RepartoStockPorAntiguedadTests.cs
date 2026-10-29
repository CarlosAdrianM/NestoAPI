using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Infraestructure.Stocks;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Stocks
{
    /// <summary>
    /// NestoAPI#730 (sugerencia 861, 29/10/26): el reparto del stock por antigüedad de UN pedido es el del picking
    /// (<see cref="GestorReservasStock.Reservar"/>) con la misma lectura (<see cref="ILectorRepartoStock"/>), y la fecha de entrega
    /// a la agencia y los avisos de pedidos en riesgo lo usan en vez de «stock − todos los pendientes de los demás».
    /// </summary>
    [TestClass]
    public class RepartoStockPorAntiguedadTests
    {
        // Martes 13/10/2026 a las 10:30 (antes del corte de las 11:00).
        private static readonly DateTime AHORA = new DateTime(2026, 10, 13, 10, 30, 0);
        private static readonly DateTime T1 = new DateTime(2026, 10, 28, 9, 0, 0);
        private static readonly DateTime T2 = new DateTime(2026, 10, 28, 10, 0, 0);
        private static readonly DateTime T3 = new DateTime(2026, 10, 28, 11, 0, 0);

        private ILectorRepartoStock lector;
        private List<StockProducto> stocks;
        private List<LineaPedidoPicking> pendientes;

        [TestInitialize]
        public void Preparar()
        {
            lector = A.Fake<ILectorRepartoStock>();
            stocks = new List<StockProducto>();
            pendientes = new List<LineaPedidoPicking>();
            // Como la BD: cada lectura devuelve objetos nuevos (el reparto los consume).
            A.CallTo(() => lector.LeerStocks(A<IEnumerable<string>>._)).ReturnsLazily(() => stocks.Select(s => s.Clonar()).ToList());
            A.CallTo(() => lector.LeerLineasPendientes(A<IEnumerable<string>>._)).ReturnsLazily(() => pendientes
                .Select(l => new LineaPedidoPicking
                {
                    Id = l.Id,
                    Empresa = l.Empresa,
                    NumeroPedido = l.NumeroPedido,
                    TipoLinea = l.TipoLinea,
                    Producto = l.Producto,
                    Almacen = l.Almacen,
                    Cantidad = l.Cantidad,
                    FechaModificacion = l.FechaModificacion
                })
                .OrderBy(l => l.FechaModificacion).ThenBy(l => l.Id)
                .ToList());
        }

        private static LineaPedidoPicking Pendiente(int pedido, int id, string producto, int cantidad, DateTime fecha, string almacen = "ALG", string empresa = "1  ")
            => new LineaPedidoPicking
            {
                Id = id,
                Empresa = empresa,
                NumeroPedido = pedido,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto.PadRight(15),
                Almacen = almacen,
                Cantidad = cantidad,
                FechaModificacion = fecha
            };

        /// <summary>El caso real del 29/10/26: 43818, 4 uds en Algete, pedidos de 1, 2 y 2 por orden de modificación.</summary>
        private void Caso43818()
        {
            stocks.Add(new StockProducto { Producto = "43818          ", StockDisponible = 4 });
            pendientes.Add(Pendiente(929498, 101, "43818", 1, T1));
            pendientes.Add(Pendiente(929440, 102, "43818", 2, T2));
            pendientes.Add(Pendiente(929547, 103, "43818", 2, T3));
        }

        private int[] Repartir(int pedido, int id, int cantidad, DateTime fecha)
        {
            var reparto = new RepartoStockPorAntiguedad(lector);
            return reparto.UnidadesReservadas("1", pedido, new List<LineaRepartoStock>
            {
                new LineaRepartoStock { Id = id, Producto = "43818", Almacen = "ALG", Cantidad = cantidad, FechaModificacion = fecha }
            }, AHORA);
        }

        [TestMethod]
        public void UnidadesReservadas_Caso43818_LosDosPrimerosPorAntiguedadTienenTodoYSoloAlTerceroLeFaltaUna()
        {
            Caso43818();

            Assert.AreEqual(1, Repartir(929498, 101, 1, T1).Single(), "929498 (el más antiguo): 1 de 1");
            Assert.AreEqual(2, Repartir(929440, 102, 2, T2).Single(), "929440: 2 de 2 (antes, 4 − 3 de los demás = 1 y salía en riesgo)");
            Assert.AreEqual(1, Repartir(929547, 103, 2, T3).Single(), "929547 (el último): 1 de 2, le falta 1");
        }

        [TestMethod]
        public void UnidadesReservadas_EsElMismoRepartoQueElPicking()
        {
            // Lo que el picking reservaría con las mismas líneas y el mismo stock, línea a línea.
            Caso43818();
            List<LineaPedidoPicking> todas = lector.LeerLineasPendientes(new[] { "43818" });
            GestorReservasStock.Reservar(lector.LeerStocks(new[] { "43818          " }), new List<PedidoPicking>(), todas);

            foreach (LineaPedidoPicking linea in todas)
            {
                Assert.AreEqual(linea.CantidadReservada, Repartir(linea.NumeroPedido, linea.Id, linea.Cantidad, linea.FechaModificacion).Single(),
                    $"Pedido {linea.NumeroPedido}");
            }
        }

        [TestMethod]
        public void UnidadesReservadas_ApartaLasLineasGrabadasDelPropioPedido()
        {
            // El pedido 929440 tiene grabadas 2 uds, pero en la plantilla se están cambiando a 3 (cantidad distinta: va al final).
            Caso43818();
            var reparto = new RepartoStockPorAntiguedad(lector);

            int[] reservadas = reparto.UnidadesReservadas("1", 929440, new List<LineaRepartoStock>
            {
                new LineaRepartoStock { Id = 102, Producto = "43818", Almacen = "ALG", Cantidad = 3 }
            }, AHORA.AddDays(30));

            Assert.AreEqual(1, reservadas.Single(), "4 − 1 (929498) − 2 (929547, ahora por delante) = 1; sus 2 grabadas no cuentan dos veces");
        }

        [TestMethod]
        public void UnidadesReservadas_LineaDeLaPlantillaSinCambiarDeCantidad_ConservaSuTurno()
        {
            Caso43818();
            var reparto = new RepartoStockPorAntiguedad(lector);

            int[] reservadas = reparto.UnidadesReservadas("1", 929440, new List<LineaRepartoStock>
            {
                new LineaRepartoStock { Id = 102, Producto = "43818", Almacen = "ALG", Cantidad = 2 }
            }, AHORA.AddDays(30));

            Assert.AreEqual(2, reservadas.Single(), "Sin fecha y con la misma cantidad que en la BD: su Fecha_Modificación de la BD");
        }

        [TestMethod]
        public void UnidadesReservadas_PedidoNuevo_VaDetrasDeTodos()
        {
            Caso43818();
            var reparto = new RepartoStockPorAntiguedad(lector);

            int[] reservadas = reparto.UnidadesReservadas("1", null, new List<LineaRepartoStock>
            {
                new LineaRepartoStock { Producto = "43818", Almacen = "ALG", Cantidad = 1 },
                new LineaRepartoStock { Producto = "SINSTOCK", Almacen = "ALG", Cantidad = 1 },
                new LineaRepartoStock { Producto = "43818", Almacen = "ALG", Cantidad = 0 }
            }, AHORA.AddDays(30));

            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, reservadas, "Los 5 pendientes se comen las 4 uds; sin stock leído, 0");
        }

        [TestMethod]
        public void UnidadesReservadas_LineaDeTienda_TiraPrimeroDeLaTiendaComoElPicking()
        {
            stocks.Add(new StockProducto { Producto = "43818", StockDisponible = 4, StockTienda = 1 });
            pendientes.Add(Pendiente(929498, 101, "43818", 1, T1));
            var reparto = new RepartoStockPorAntiguedad(lector);

            int[] reservadas = reparto.UnidadesReservadas("1", null, new List<LineaRepartoStock>
            {
                new LineaRepartoStock { Producto = "43818", Almacen = "REI", Cantidad = 2 },
                new LineaRepartoStock { Producto = "43818", Almacen = "ALG", Cantidad = 3 }
            }, AHORA.AddDays(30));

            // REI: 1 de la tienda + 1 de Algete (quedan 3 − 1 = 2 en Algete); ALG: las 2 que quedan.
            CollectionAssert.AreEqual(new[] { 2, 2 }, reservadas);
        }

        [TestMethod]
        public void UnidadesReservadas_SinLineasDeProducto_NoLeeNada()
        {
            var reparto = new RepartoStockPorAntiguedad(lector);

            int[] reservadas = reparto.UnidadesReservadas("1", 1, new List<LineaRepartoStock>(), AHORA);

            Assert.AreEqual(0, reservadas.Length);
            A.CallTo(lector).MustNotHaveHappened();
        }

        // ---- La fecha de entrega a la agencia (y por tanto el aviso de pedidos en riesgo) con el reparto por antigüedad ----

        private static bool EsFestivo(DateTime dia, string almacen) => dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday;

        private ServicioFechaEntregaAgencia ServicioCaso43818()
        {
            Caso43818();
            IRepositorioFechaEntregaAgencia repositorio = A.Fake<IRepositorioFechaEntregaAgencia>();
            A.CallTo(() => repositorio.LeerCalendario(A<string>._)).Returns(Task.FromResult(new List<ReposicionCalendario>()));
            A.CallTo(() => repositorio.LeerHoraCorte(A<string>._)).Returns(new TimeSpan(11, 0, 0));
            foreach (var pedido in new[] { (929498, 101, 1, T1), (929440, 102, 2, T2), (929547, 103, 2, T3) })
            {
                PedidoFechaEntregaAgencia leido = new PedidoFechaEntregaAgencia
                {
                    Empresa = "1",
                    Numero = pedido.Item1,
                    Ruta = "FW",
                    ModoServicio = Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                    ServirJunto = true,
                    Lineas = new List<LineaPedidoFechaEntregaAgencia>
                    {
                        new LineaPedidoFechaEntregaAgencia { Id = pedido.Item2, Producto = "43818", Almacen = "ALG", Cantidad = pedido.Item3, BaseImponible = 10, FechaModificacion = pedido.Item4 }
                    }
                };
                A.CallTo(() => repositorio.LeerPedido("1", pedido.Item1)).Returns(Task.FromResult(leido));
                // La sombra, como en la BD: 4 en Algete y los pendientes de los demás (5 − los suyos), sin stock en ningún otro sitio.
                var datos = new DatosSombraModoServicio();
                int demas = 5 - pedido.Item3;
                ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave("43818", "ALG"), 4);
                ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave("43818"), 4);
                ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave("43818", "ALG"), demas);
                ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave("43818"), demas);
                A.CallTo(() => repositorio.LeerStock("1", pedido.Item1, A<IEnumerable<string>>._)).Returns(datos);
            }
            return new ServicioFechaEntregaAgencia(repositorio, () => AHORA, EsFestivo, _ => { },
                avisadorProveedorVencido: null, anotarPedidoGrabado: (e, p, d, c) => { }, repartoStock: new RepartoStockPorAntiguedad(lector));
        }

        [TestMethod]
        public async Task FechaEntregaAgencia_Caso43818_SoloElTerceroSeQuedaSinStock()
        {
            ServicioFechaEntregaAgencia servicio = ServicioCaso43818();

            FechaEntregaAgenciaDTO primero = await servicio.CalcularPedido("1", 929498);
            FechaEntregaAgenciaDTO segundo = await servicio.CalcularPedido("1", 929440);
            FechaEntregaAgenciaDTO tercero = await servicio.CalcularPedido("1", 929547);

            Assert.AreEqual(AHORA.Date, primero.EntregaCompleta, "929498 sale completo hoy");
            Assert.AreEqual(AHORA.Date, segundo.EntregaCompleta, "929440 sale completo hoy (antes: «sin stock», y salió en el picking de las 9)");
            Assert.AreNotEqual(AHORA.Date, tercero.EntregaCompleta, "929547 es el único al que le falta 1 ud");
            Assert.IsFalse(segundo.Desglose.Any(d => d.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK));
            Assert.IsTrue(tercero.Desglose.Any(d => d.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK));
        }

        // ---- RepartidorStockFechaEntregaAgencia con las unidades del reparto por antigüedad ----

        [TestMethod]
        public void Repartir_ConElRepartoPorAntiguedad_LasLibresDeAlgeteSonLasQueLeTocan()
        {
            var datos = new DatosSombraModoServicio();
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave("A", "ALG"), 4);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave("A"), 4);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave("A", "ALG"), 3);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave("A"), 3);
            var linea = new LineaPedidoFechaEntregaAgencia { Producto = "A", Almacen = "ALG", Cantidad = 2, BaseImponible = 10 };

            LineaFechaEntregaAgencia antes = RepartidorStockFechaEntregaAgencia.Repartir(new[] { linea }, datos, null, AHORA.Date).Single();
            LineaFechaEntregaAgencia ahora = RepartidorStockFechaEntregaAgencia.Repartir(new[] { linea }, datos, null, AHORA.Date,
                new Dictionary<LineaPedidoFechaEntregaAgencia, int> { [linea] = 2 }).Single();

            Assert.AreEqual(1, antes.EnAlgete, "Sin el reparto: 4 − 3 de los demás");
            Assert.AreEqual(2, ahora.EnAlgete, "Con el reparto: las 2 que le da el picking por antigüedad");
        }
    }
}
