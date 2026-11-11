using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Inventarios
{
    /// <summary>
    /// Épica #784 (11/11/26, producto 37215 en ALG): una unidad reservada por el picking del pedido 930324 (Ubicaciones Estado 3) ya
    /// estaba recogida; al inventariar el hueco salió en el teórico (1), se contó 0 y prdContabilizarInventario bajó el stock (solo
    /// regulariza Estado 0): stock 0 y ubicado 1. Desde ahora el teórico de un hueco es solo lo LIBRE (Estado 0).
    /// </summary>
    [TestClass]
    public class InventarioReservasTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 11);

        private static UbicacionAlmacenInventario Hueco(string producto, int cantidad, int estado = 0, int? pedido = null) => new UbicacionAlmacenInventario
        {
            Pasillo = "001", Fila = "002", Columna = "005", Producto = producto, Cantidad = cantidad, Estado = estado, PedidoVta = pedido
        };

        private static FilaInventarioPendiente Fila(int numero, string producto, int calculado, int real, string pasillo = "001") => new FilaInventarioPendiente
        {
            NumeroOrden = numero, Producto = producto, Pasillo = pasillo, Fila = pasillo == null ? null : "002", Columna = pasillo == null ? null : "005",
            StockCalculado = calculado, StockReal = real, Fecha = HOY, Ubicar = true, PrecioMedio = 1, Usuario = "NUEVAVISION\\Andre"
        };

        // ---------- El teórico es solo lo libre ----------

        [TestMethod]
        public void Sql_ElTeoricoDeLaUbicacionYElACero_SoloEstado0()
        {
            StringAssert.Contains(RepositorioInventarioUbicacionesSql.SQL_TEORICO_UBICACION, "u.Estado = 0");
            Assert.IsFalse(RepositorioInventarioUbicacionesSql.SQL_TEORICO_UBICACION.Contains("Estado >= 0"));
            StringAssert.Contains(RepositorioInventarioACeroSql.SQL_UBICACIONES, "u.Estado = 0");
            StringAssert.Contains(RepositorioInventarioUbicacionesSql.SQL_PRODUCTOS_POR_UBICACION, "u.Estado = 0");
            StringAssert.Contains(RepositorioInventarioUbicacionesSql.SQL_RESERVAS_UBICACION, "u.Estado > 0");
        }

        [TestMethod]
        public async Task RellenarUbicacion_CasoReal37215_SinLibreNoHayFila_YAvisaDeLaReserva()
        {
            var repo = A.Fake<IRepositorioInventarioUbicaciones>();
            var unidad = A.Fake<IUnidadInventarioUbicaciones>();
            var insertadas = new List<Inventario>();
            A.CallTo(() => repo.LeerAlmacen("1", "ALG")).Returns(new AlmacenInventario { Nombre = "Algete", ControlUbicaciones = true });
            A.CallTo(() => repo.Empezar(A<string>._)).Returns(unidad);
            A.CallTo(() => unidad.Insertar(A<IList<Inventario>>._)).Invokes((IList<Inventario> f) => insertadas.AddRange(f));
            // Lo que devuelve SQL_TEORICO_UBICACION (Estado 0): otro producto libre; el 37215 solo tiene la reserva.
            A.CallTo(() => unidad.TeoricoUbicacion("1", "ALG", "001", "002", "005"))
                .Returns(new List<StockProductoInventario> { new StockProductoInventario { Numero = "27534", Stock = 2 } });
            A.CallTo(() => unidad.ReservasUbicacion("ALG", "001", "002", "005"))
                .Returns(new List<ReservaUbicacionInventario> { new ReservaUbicacionInventario { Producto = "37215", Pedido = 930324, Cantidad = 1 } });

            var resultado = await new ServicioInventarioUbicaciones(repo).RellenarUbicacion(new RellenarUbicacionInventarioDTO
            {
                Empresa = "1", Almacen = "ALG", Fecha = HOY, Pasillo = "001", Fila = "002", Columna = "005"
            }, "NUEVAVISION\\Andre");

            Assert.AreEqual(EstadoInventarioUbicaciones.Ok, resultado.Estado);
            CollectionAssert.AreEqual(new[] { "27534" }, insertadas.Select(f => f.Número).ToArray(), "el 37215 no tiene nada libre: sin fila, el stock no se toca");
            StringAssert.Contains(resultado.Respuesta.Aviso, "1 unidad del 37215 reservada para el pedido 930324");
            StringAssert.Contains(resultado.Respuesta.Aviso, "NO se cuentan");
        }

        [TestMethod]
        public void AvisoReservas_SinReservas_Null()
        {
            Assert.IsNull(ServicioInventarioUbicaciones.AvisoReservas(new List<ReservaUbicacionInventario>()));
        }

        // ---------- Al contabilizar ----------

        [TestMethod]
        public void TeoricoConLaReserva_CasoReal_Bloquea_YSePuedeBorrar()
        {
            // La fila del 37215 se rellenó con la reserva en el teórico (1) y se contó 0: contabilizarla bajaba el stock.
            ComprobacionesInventarioDTO c = ComprobadorInventario.Comprobar("ALG", HOY, true,
                new[] { Fila(948900, "37215", 1, 0) },
                new[] { Hueco("37215", 1, estado: 3, pedido: 930324) });

            ComprobacionInventarioDTO bloqueante = c.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.TEORICO_CON_RESERVAS);
            Assert.AreEqual(948900, bloqueante.Filas.Single().NºOrden);
            ReglaBorrarFilaInventario.Marcar(c, true, HOY);
            Assert.IsTrue(bloqueante.Filas.Single().SePuedeBorrar);
        }

        [TestMethod]
        public void ReservadaSinRecoger_TeoricoSoloLibre_ContadoSoloLibre_NadaQueDecir()
        {
            // 2 libres + 1 reservada (aún en el hueco); se rellena con 2 y se cuentan 2: el SP deja Estado 0 = 2 y la reserva sigue.
            ComprobacionesInventarioDTO c = ComprobadorInventario.Comprobar("ALG", HOY, true,
                new[] { Fila(948901, "37215", 2, 2) },
                new[] { Hueco("37215", 2), Hueco("37215", 1, estado: 3, pedido: 930324) });

            Assert.IsFalse(c.Bloqueantes.Any(b => b.Codigo == CodigosComprobacionInventario.TEORICO_CON_RESERVAS));
            Assert.IsFalse(c.Avisos.Any(a => a.Codigo == CodigosComprobacionInventario.CONTADO_CON_RESERVAS));
        }

        [TestMethod]
        public void MezclaLibreYReservada_ContadaTambienLaReservada_Aviso()
        {
            ComprobacionesInventarioDTO c = ComprobadorInventario.Comprobar("ALG", HOY, true,
                new[] { Fila(948902, "37215", 2, 3) },
                new[] { Hueco("37215", 2), Hueco("37215", 1, estado: 3, pedido: 930324) });

            Assert.AreEqual(948902, c.Avisos.Single(a => a.Codigo == CodigosComprobacionInventario.CONTADO_CON_RESERVAS).Filas.Single().NºOrden);
            Assert.IsFalse(c.Bloqueantes.Any(b => b.Codigo == CodigosComprobacionInventario.TEORICO_CON_RESERVAS));
        }

        [TestMethod]
        public void SinReservas_ComoSiempre()
        {
            ComprobacionesInventarioDTO c = ComprobadorInventario.Comprobar("ALG", HOY, true,
                new[] { Fila(948903, "37215", 3, 0) },
                new[] { Hueco("37215", 3) });

            Assert.IsFalse(c.Bloqueantes.Any(b => b.Codigo == CodigosComprobacionInventario.TEORICO_CON_RESERVAS));
            Assert.IsFalse(c.Avisos.Any(a => a.Codigo == CodigosComprobacionInventario.CONTADO_CON_RESERVAS));
        }

        [TestMethod]
        public void HuecoSoloConReservas_NoEsUbicacionSinRellenar()
        {
            ComprobacionesInventarioDTO c = ComprobadorInventario.Comprobar("ALG", HOY, true,
                new FilaInventarioPendiente[0],
                new[] { Hueco("37215", 1, estado: 3, pedido: 930324) });

            Assert.IsFalse(c.Avisos.Any(a => a.Codigo == CodigosComprobacionInventario.UBICACION_SIN_RELLENAR));
        }

        // ---------- Tiendas (sin ubicaciones) ----------

        [TestMethod]
        public void Tienda_ContadoMenorConPickingSinAlbaranar_Aviso()
        {
            var fila = Fila(948904, "37215", 3, 2, pasillo: null);
            fila.Ubicar = false;
            ComprobacionesInventarioDTO c = ComprobadorInventario.Comprobar("REI", HOY, false,
                new[] { fila, Fila(948905, "27534", 3, 2, pasillo: null) }, null, null,
                new[] { new PickingSinAlbaranarInventario { Producto = "37215", Pedido = 930324, Cantidad = 1 } });

            ComprobacionInventarioDTO aviso = c.Avisos.Single(a => a.Codigo == CodigosComprobacionInventario.PICKING_SIN_ALBARANAR);
            Assert.AreEqual(948904, aviso.Filas.Single().NºOrden);
            Assert.IsFalse(c.Bloqueantes.Any());
        }
    }
}
