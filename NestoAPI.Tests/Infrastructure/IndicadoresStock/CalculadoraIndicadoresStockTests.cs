using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.IndicadoresStock;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.IndicadoresStock
{
    /// <summary>NestoAPI#629: cada indicador sobre un extracto pequeño en memoria (septiembre de 2026).</summary>
    [TestClass]
    public class CalculadoraIndicadoresStockTests
    {
        private static readonly DateTime MES = new DateTime(2026, 9, 1);
        private static readonly DateTime ANTIGUO = new DateTime(2020, 1, 1);
        private RepositorioIndicadoresStockEnMemoria repo;

        [TestInitialize]
        public void Inicializar()
        {
            repo = new RepositorioIndicadoresStockEnMemoria();
        }

        private void Producto(string numero, decimal precio, int estado = 0, string grupo = "ACC", string subgrupo = "002", bool ficticio = false)
        {
            repo.Productos.Add(new ProductoEnMemoria { Producto = numero, PrecioMedio = precio, Estado = estado, Grupo = grupo, SubGrupo = subgrupo, Ficticio = ficticio });
        }

        private void Movimiento(string producto, DateTime fecha, int cantidad, string almacen = "ALG", string diario = "SYS", decimal importe = 0, string empresa = "1")
        {
            repo.Extracto.Add(new MovimientoExtracto
            {
                Producto = producto, Fecha = fecha, Cantidad = cantidad, Almacen = almacen, Diario = diario, Importe = importe, Empresa = empresa,
                Orden = repo.Extracto.Count + 1
            });
        }

        private void Venta(string producto, DateTime albaran, int cantidad, string almacen = "ALG", int estado = 4)
        {
            repo.Lineas.Add(new LineaVenta
            {
                Producto = producto, FechaAlbaran = albaran, FechaEntrega = albaran, Cantidad = cantidad, Almacen = almacen, Estado = estado,
                Pedido = repo.Lineas.Count + 1, Orden = 1, Albaran = repo.Lineas.Count + 1
            });
        }

        private List<FilaIndicadoresStock> Calcular(DateTime? mes = null)
        {
            DateTime m = mes ?? MES;
            return CalculadoraIndicadoresStock.Calcular(new DatosIndicadoresStock
            {
                Mes = m,
                Productos = repo.LeerProductos().Result,
                Stock = repo.LeerStock(m).Result,
                Ventas = repo.LeerVentas(m).Result,
                Servicio = repo.LeerServicio(m).Result,
                MovimientosComprados = repo.LeerMovimientosProductosComprados(m).Result
            });
        }

        private static FilaIndicadoresStock Fila(List<FilaIndicadoresStock> filas, string almacen) => filas.Single(f => f.Almacen == almacen);

        [TestMethod]
        public void ValorStock_SinExtNiFicticiosNiOtrasEmpresasNiMovimientosPosteriores_PorAlmacenYTotal()
        {
            Producto("A", 2m);
            Producto("F", 5m, ficticio: true);
            Movimiento("A", ANTIGUO, 10);
            Movimiento("A", new DateTime(2026, 3, 10), 5, "REI");
            Movimiento("A", new DateTime(2026, 5, 1), 3, "ALC", empresa: "3");
            Movimiento("A", new DateTime(2026, 3, 10), 100, "EXT");
            Movimiento("A", new DateTime(2026, 3, 10), 50, empresa: "2");
            Movimiento("A", new DateTime(2026, 10, 2), 7);
            Movimiento("F", ANTIGUO, 20);

            List<FilaIndicadoresStock> filas = Calcular();

            Assert.AreEqual(FilaIndicadoresStock.TOTAL, filas[0].Almacen, "La TOTAL va primero");
            Assert.AreEqual(36m, Fila(filas, "TOTAL").ValorStock);
            Assert.AreEqual(18, Fila(filas, "TOTAL").UnidadesStock);
            Assert.AreEqual(20m, Fila(filas, "ALG").ValorStock);
            Assert.AreEqual(10m, Fila(filas, "REI").ValorStock);
            Assert.AreEqual(6m, Fila(filas, "ALC").ValorStock);
            Assert.IsFalse(filas.Any(f => f.Almacen == "EXT"));
            Assert.AreEqual(20m, Fila(filas, "TOTAL").ValorStockAlgete);
            Assert.AreEqual(16m, Fila(filas, "TOTAL").ValorStockTiendas);
            Assert.IsNull(Fila(filas, "ALG").ValorStockAlgete, "Algete vs tiendas solo en la TOTAL");
        }

        [TestMethod]
        public void Estados4Y1_SoloConStock()
        {
            Producto("B", 3m, estado: 4);
            Producto("C", 1m, estado: 1);
            Producto("D", 1m, estado: 4);
            Movimiento("B", ANTIGUO, 4);
            Movimiento("C", ANTIGUO, 2);
            Movimiento("D", ANTIGUO, 2);
            Movimiento("D", new DateTime(2026, 2, 1), -2);

            FilaIndicadoresStock total = Fila(Calcular(), "TOTAL");

            Assert.AreEqual(12m, total.ValorEstado4);
            Assert.AreEqual(1, total.ProductosEstado4);
            Assert.AreEqual(2m, total.ValorEstado1);
            Assert.AreEqual(1, total.ProductosEstado1);
        }

        [TestMethod]
        public void SinVenta_6_12_24_ConLaDemandaDeTodaLaEmpresa()
        {
            foreach (string p in new[] { "P1", "P2", "P3", "P4", "P6" })
            {
                Producto(p, 1m);
            }
            Producto("P5", 1m, grupo: "MTP");
            Movimiento("P1", ANTIGUO, 10);
            Venta("P1", new DateTime(2026, 1, 15), 1);               // hace 8 meses: sin venta en 6, no en 12
            Movimiento("P2", ANTIGUO, 10);
            Venta("P2", new DateTime(2024, 6, 15), 1);               // hace más de 24 meses
            Movimiento("P3", new DateTime(2026, 7, 1), 10);          // nuevo: no lleva 6 meses
            Movimiento("P4", ANTIGUO, 10, "REI");
            Venta("P4", new DateTime(2026, 8, 10), 1, "ALG");        // se vende en Algete: la demanda es de toda la empresa
            Movimiento("P5", ANTIGUO, 10);                           // materia prima: no cuenta
            Movimiento("P6", ANTIGUO, 10);
            Venta("P6", new DateTime(2026, 8, 10), 1, estado: -3);   // presupuesto: no es venta
            Venta("P6", new DateTime(2025, 3, 10), 1, estado: 2);    // hace 18 meses

            List<FilaIndicadoresStock> filas = Calcular();
            FilaIndicadoresStock total = Fila(filas, "TOTAL");

            Assert.AreEqual(3, total.ProductosSinVenta6Meses);       // P1, P2, P6
            Assert.AreEqual(30m, total.ValorSinVenta6Meses);
            Assert.AreEqual(2, total.ProductosSinVenta12Meses);      // P2, P6
            Assert.AreEqual(20m, total.ValorSinVenta12Meses);
            Assert.AreEqual(1, total.ProductosSinVenta24Meses);      // P2
            Assert.AreEqual(10m, total.ValorSinVenta24Meses);
            Assert.AreEqual(0, Fila(filas, "REI").ProductosSinVenta6Meses, "P4 se vende en otro almacén");
        }

        [TestMethod]
        public void CosteVentas_StockMedio_Rotacion()
        {
            Producto("R", 2m);
            Movimiento("R", ANTIGUO, 10);
            Venta("R", new DateTime(2025, 10, 5), 12);
            Venta("R", new DateTime(2026, 9, 10), 20, estado: 2);
            Venta("R", new DateTime(2026, 9, 11), -2);                // devolución: resta
            Venta("R", new DateTime(2025, 9, 20), 100);               // hace 13 meses: fuera
            Venta("R", new DateTime(2026, 9, 12), 50, estado: 1);     // en curso: no ha salido
            Venta("R", new DateTime(2026, 9, 12), 50, estado: -2);    // nota de entrega: fuera

            FilaIndicadoresStock total = Fila(Calcular(), "TOTAL");

            Assert.AreEqual(60m, total.CosteVentas12Meses);           // 30 unidades netas × 2
            Assert.AreEqual(20m, total.ValorStockMedio12Meses);
            Assert.AreEqual(3m, total.Rotacion);
        }

        [TestMethod]
        public void StockMedio_EsLaMediaDeLosDoceCierresDeMes()
        {
            Producto("S", 1m);
            Movimiento("S", new DateTime(2025, 9, 15), 12);           // antes de la ventana (oct-25 a sep-26)
            Movimiento("S", new DateTime(2026, 3, 15), 12);           // cierres de mar a sep (7) con 24; oct a feb (5) con 12

            FilaIndicadoresStock total = Fila(Calcular(), "TOTAL");

            Assert.AreEqual(19m, total.ValorStockMedio12Meses);       // (5 × 12 + 7 × 24) / 12
            Assert.AreEqual(0m, total.Rotacion);
        }

        [TestMethod]
        public void StockMedio_SinMateriasPrimasNiMuestras()
        {
            Producto("MP", 1m, grupo: "MTP");
            Producto("MU", 1m, subgrupo: "MMP");
            Movimiento("MP", ANTIGUO, 10);
            Movimiento("MU", ANTIGUO, 10);

            FilaIndicadoresStock total = Fila(Calcular(), "TOTAL");

            Assert.AreEqual(20m, total.ValorStock, "El valor de stock (KPI) sí los lleva");
            Assert.AreEqual(0m, total.ValorStockMedio12Meses);
            Assert.IsNull(total.Rotacion);
        }

        [TestMethod]
        public void Cobertura_PonderadaPorEurosYSinLosQueNoSeVenden()
        {
            Producto("A", 1m);
            Producto("B", 2m);
            Producto("C", 1m);
            Movimiento("A", ANTIGUO, 60);
            Movimiento("B", ANTIGUO, 10);
            Movimiento("C", ANTIGUO, 100);
            Venta("A", new DateTime(2026, 4, 10), 30);                // 60 en 6 meses: 6 meses de cobertura
            Venta("A", new DateTime(2026, 9, 10), 30);
            Venta("B", new DateTime(2026, 6, 10), 60);                // 60 en 6 meses con 10 de stock: 1 mes
            Venta("A", new DateTime(2026, 3, 10), 500);               // hace 7 meses: fuera de los 6

            FilaIndicadoresStock total = Fila(Calcular(), "TOTAL");

            // (60 € × 182,5 días + 20 € × 30,42 días) / 80 €
            Assert.AreEqual(144.5m, total.CoberturaMediaDias);
        }

        [TestMethod]
        public void DiasEnServir_YNivelDeServicio()
        {
            Producto("V", 1m);
            repo.Lineas.Add(new LineaVenta { Pedido = 1, Orden = 1, Producto = "V", Cantidad = 2, FechaEntrega = new DateTime(2026, 9, 1), FechaAlbaran = new DateTime(2026, 9, 3), Albaran = 100 });
            repo.Lineas.Add(new LineaVenta { Pedido = 2, Orden = 1, Producto = "V", Cantidad = 4, FechaEntrega = new DateTime(2026, 9, 10), FechaAlbaran = new DateTime(2026, 9, 10), Albaran = 101 });
            repo.Lineas.Add(new LineaVenta { Pedido = 2, Orden = 2, Producto = "V", Cantidad = 2, FechaEntrega = new DateTime(2026, 9, 10), Estado = -1 }); // lo que quedó al partirla
            repo.Lineas.Add(new LineaVenta { Pedido = 3, Orden = 1, Producto = "V", Cantidad = 1, Almacen = "REI", FechaEntrega = new DateTime(2026, 9, 5), FechaAlbaran = new DateTime(2026, 9, 9), Albaran = 102 });
            repo.Lineas.Add(new LineaVenta { Pedido = 4, Orden = 1, Producto = "V", Cantidad = 1, FechaEntrega = new DateTime(2026, 9, 25), FechaAlbaran = new DateTime(2026, 10, 2), Albaran = 103 });
            repo.Lineas.Add(new LineaVenta { Pedido = 5, Orden = 1, Producto = "V", Cantidad = -1, FechaEntrega = new DateTime(2026, 9, 1), FechaAlbaran = new DateTime(2026, 9, 20), Albaran = 104 });

            List<FilaIndicadoresStock> filas = Calcular();

            Assert.AreEqual(3, Fila(filas, "TOTAL").LineasServidas);
            Assert.AreEqual(2m, Fila(filas, "TOTAL").DiasServirMedia);  // (2 + 0 + 4) / 3
            Assert.AreEqual(66.67m, Fila(filas, "TOTAL").NivelServicio);
            Assert.AreEqual(1m, Fila(filas, "ALG").DiasServirMedia);
            Assert.AreEqual(50m, Fila(filas, "ALG").NivelServicio);
            Assert.AreEqual(4m, Fila(filas, "REI").DiasServirMedia);
            Assert.AreEqual(100m, Fila(filas, "REI").NivelServicio);
        }

        [TestMethod]
        public void ComprasYBajasAPerdidasDelMes()
        {
            Producto("K", 2m);
            Movimiento("K", new DateTime(2026, 8, 20), 40, diario: "_ALBCOMP", importe: 100m);  // agosto: no es de este mes
            Movimiento("K", new DateTime(2026, 9, 5), 100, diario: "_ALBCOMP", importe: 250m);
            Movimiento("K", new DateTime(2026, 9, 20), -10, diario: "_ALBCOMP", importe: 25m);  // devolución a proveedor
            Movimiento("K", new DateTime(2026, 9, 7), -3, diario: "Mermas");

            FilaIndicadoresStock total = Fila(Calcular(), "TOTAL");

            Assert.AreEqual(225m, total.ComprasValor);
            Assert.AreEqual(90, total.ComprasUnidades);
            Assert.AreEqual(3, total.BajasPerdidasUnidades);
            Assert.AreEqual(6m, total.BajasPerdidasValor);
        }

        [TestMethod]
        public void AltasYBajasDeReferencias()
        {
            Producto("N", 1m);
            Producto("M", 1m);
            Producto("Q", 1m);
            Movimiento("N", new DateTime(2026, 9, 3), 5);                         // primer movimiento: alta
            Movimiento("M", new DateTime(2025, 5, 1), 4);
            Movimiento("M", new DateTime(2026, 9, 15), -4);                       // se queda sin stock: baja
            Movimiento("Q", ANTIGUO, 10);
            Movimiento("Q", new DateTime(2026, 9, 10), -1);
            Movimiento("Q", new DateTime(2026, 9, 10), 1, "REI");                 // primera vez en Reina

            List<FilaIndicadoresStock> filas = Calcular();

            Assert.AreEqual(1, Fila(filas, "TOTAL").AltasReferencias);
            Assert.AreEqual(1, Fila(filas, "TOTAL").BajasReferencias);
            Assert.AreEqual(1, Fila(filas, "ALG").AltasReferencias);
            Assert.AreEqual(1, Fila(filas, "ALG").BajasReferencias);
            Assert.AreEqual(1, Fila(filas, "REI").AltasReferencias);
            Assert.AreEqual(0, Fila(filas, "REI").BajasReferencias);
        }

        [TestMethod]
        public void ComprasConCoberturaAlta_ConElStockJustoAntesDeCadaCompra()
        {
            Producto("X", 1m);
            Producto("Y", 1m);
            Producto("Z", 1m);
            Movimiento("X", new DateTime(2026, 1, 1), 100);
            Venta("X", new DateTime(2026, 3, 10), 30);                             // 60 en los 6 meses anteriores (mar-ago): 10 al mes
            Venta("X", new DateTime(2026, 8, 10), 30);
            Venta("X", new DateTime(2026, 9, 10), 500);                            // del propio mes: no cuenta para la media
            Movimiento("X", new DateTime(2026, 9, 10), 50, diario: "_ALBCOMP");   // 100 / 10 = 10 meses: alta

            Movimiento("Y", new DateTime(2026, 1, 1), 5);
            Venta("Y", new DateTime(2026, 5, 10), 60);
            Movimiento("Y", new DateTime(2026, 9, 3), 60, diario: "_ALBCOMP");    // 5 / 10 = 0,5 meses
            Movimiento("Y", new DateTime(2026, 9, 20), 100, diario: "_ALBCOMP");  // 65 / 10 = 6,5 meses: alta

            Movimiento("Z", new DateTime(2026, 9, 4), 10, diario: "_ALBCOMP");    // sin stock ni venta: no

            List<FilaIndicadoresStock> filas = Calcular();

            Assert.AreEqual(220, Fila(filas, "TOTAL").UnidadesCompradasMes);
            Assert.AreEqual(150, Fila(filas, "TOTAL").UnidadesCompradasCoberturaAlta);
            Assert.AreEqual(150, Fila(filas, "ALG").UnidadesCompradasCoberturaAlta);
        }

        [TestMethod]
        public void PorcentajeCompras12Meses_NecesitaLosDoceMeses()
        {
            List<FilaIndicadoresStock> filas = Enumerable.Range(0, 12)
                .Select(i => new FilaIndicadoresStock { Mes = MES.AddMonths(-i), Almacen = "TOTAL", UnidadesCompradasMes = 10, UnidadesCompradasCoberturaAlta = i == 0 ? 10 : 5 })
                .ToList();

            Assert.AreEqual(54.17m, CalculadoraIndicadoresStock.CalcularPorcentajeCompras12Meses(filas, MES)); // 65 / 120
            Assert.IsNull(CalculadoraIndicadoresStock.CalcularPorcentajeCompras12Meses(filas.Skip(1), MES));
        }

        [TestMethod]
        public void SiFallaUnaConsulta_SusIndicadoresQuedanANullYLosDemasSeCalculan()
        {
            Producto("A", 2m);
            Movimiento("A", ANTIGUO, 10);

            List<FilaIndicadoresStock> filas = CalculadoraIndicadoresStock.Calcular(new DatosIndicadoresStock
            {
                Mes = MES,
                Productos = repo.LeerProductos().Result,
                Stock = repo.LeerStock(MES).Result
            });
            FilaIndicadoresStock total = Fila(filas, "TOTAL");

            Assert.AreEqual(20m, total.ValorStock);
            Assert.AreEqual(20m, total.ValorStockMedio12Meses);
            Assert.IsNull(total.CosteVentas12Meses);
            Assert.IsNull(total.Rotacion);
            Assert.IsNull(total.ProductosSinVenta6Meses);
            Assert.IsNull(total.DiasServirMedia);
            Assert.IsNull(total.UnidadesCompradasMes);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SinProductos_NoSePuedeCalcular()
        {
            _ = CalculadoraIndicadoresStock.Calcular(new DatosIndicadoresStock { Mes = MES });
        }
    }
}
