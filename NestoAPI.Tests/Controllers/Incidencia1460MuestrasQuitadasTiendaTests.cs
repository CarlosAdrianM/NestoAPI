using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.ServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using NestoAPI.Models.Picking;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using M = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Incidencia 1460 (Sancho; OK de Carlos 25/11/26): en un pedido de TIENDA las muestras sin stock se quitan solas al guardar (con aviso),
    /// con la regla de las faltas del picking, en vez del 400. Solo la denegación de las muestras; los regalos, como siempre.
    /// </summary>
    [TestClass]
    public class Incidencia1460MuestrasQuitadasTiendaTests
    {
        private const string MUESTRA = "39918";
        private const string MUESTRA2 = "42654";
        private const string PRODUCTO = "17404";

        [TestCleanup]
        public void Limpiar()
        {
            GestorMuestrasSinStock.LeerAjuste = null;
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, string almacen = "ALC", int id = 0, short estado = -1, int picking = 0)
            => new LineaPedidoVentaDTO
            {
                id = id, Producto = producto, Cantidad = cantidad, PrecioUnitario = 10, almacen = almacen, picking = picking,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, estado = estado
            };

        private static ProductoSinStockDTO SinStock(string producto, int disponibles, string nombre = null)
            => new ProductoSinStockDTO { ProductoId = producto, ProductoNombre = nombre ?? "MUESTRA " + producto, Disponibles = disponibles };

        private static HashSet<string> Muestras => new HashSet<string> { MUESTRA, MUESTRA2 };

        // ---- LA REGLA (pura) ----

        [TestMethod]
        public void Regla_SinStock_SeQuitaEntera_ConStockParcial_SeDejaEnLoQueHay()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea(PRODUCTO, 2), Linea(MUESTRA, 2), Linea(MUESTRA2, 3) };

            ResultadoMuestrasSinStockTienda r = ReglaMuestrasSinStockTienda.Decidir(lineas,
                new[] { SinStock(MUESTRA, 0), SinStock(MUESTRA2, 1) }, Muestras, _ => true);

            Assert.IsNull(r.Motivo);
            Assert.AreEqual(2, r.Quitadas.Count);
            Assert.AreEqual(0, r.Quitadas.Single(q => q.Producto == MUESTRA).Quedan);
            Assert.AreEqual(1, r.Quitadas.Single(q => q.Producto == MUESTRA2).Quedan);
            Assert.AreEqual(2, r.Quitadas.Single(q => q.Producto == MUESTRA2).Quitadas);

            var pedido = new PedidoVentaDTO { Lineas = lineas };
            ReglaMuestrasSinStockTienda.Aplicar(pedido, r);
            Assert.AreEqual(2, pedido.Lineas.Count);
            Assert.IsFalse(pedido.Lineas.Any(l => l.Producto == MUESTRA));
            Assert.AreEqual(1, pedido.Lineas.Single(l => l.Producto == MUESTRA2).Cantidad);
        }

        [TestMethod]
        public void Regla_DosLineasDeLaMismaMuestra_ElDisponibleSeGastaPorOrden()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea(PRODUCTO, 1), Linea(MUESTRA, 2), Linea(MUESTRA, 2) };

            ResultadoMuestrasSinStockTienda r = ReglaMuestrasSinStockTienda.Decidir(lineas, new[] { SinStock(MUESTRA, 3), SinStock(MUESTRA, 3) }, Muestras, _ => true);

            Assert.IsNull(r.Motivo);
            Assert.AreEqual(1, r.Quitadas.Single().Quedan, "La primera se queda con 2; la segunda, con 1");
            Assert.AreSame(lineas[2], r.Quitadas.Single().Linea);
        }

        [TestMethod]
        public void Regla_LoIntocableVaPrimero_YSiYaNoCabeNoSeQuitaNada()
        {
            LineaPedidoVentaDTO enCurso = Linea(MUESTRA, 2, id: 10, estado: 1, picking: 5);
            var lineas = new List<LineaPedidoVentaDTO> { Linea(PRODUCTO, 1), enCurso, Linea(MUESTRA, 1) };

            ResultadoMuestrasSinStockTienda cabe = ReglaMuestrasSinStockTienda.Decidir(lineas, new[] { SinStock(MUESTRA, 2) }, Muestras, l => l != enCurso);
            Assert.IsNull(cabe.Motivo);
            Assert.AreEqual(0, cabe.Quitadas.Single().Quedan, "La en curso gasta las 2; la nueva se quita");

            ResultadoMuestrasSinStockTienda noCabe = ReglaMuestrasSinStockTienda.Decidir(lineas, new[] { SinStock(MUESTRA, 1) }, Muestras, l => l != enCurso);
            Assert.AreEqual(ReglaMuestrasSinStockTienda.MOTIVO_INTOCABLES, noCabe.Motivo);
            Assert.IsFalse(noCabe.Quitadas.Any());
        }

        [TestMethod]
        public void Regla_SoloMuestras_NoSeQuitan()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea(MUESTRA, 1), Linea(MUESTRA2, 1) };

            ResultadoMuestrasSinStockTienda r = ReglaMuestrasSinStockTienda.Decidir(lineas, new[] { SinStock(MUESTRA, 0) }, Muestras, _ => true);

            Assert.IsTrue(r.SoloMuestras);
            Assert.AreEqual(ReglaMuestrasSinStockTienda.TextoSoloMuestras(), r.Motivo);
            Assert.IsFalse(r.Quitadas.Any());
        }

        [TestMethod]
        public void Regla_SeAplica_SoloEnTiendaConInterruptor_YNiPresupuestoNiNota()
        {
            var pedido = new PedidoVentaDTO();
            Assert.IsTrue(ReglaMuestrasSinStockTienda.SeAplica(pedido, "ALC", true));
            Assert.IsTrue(ReglaMuestrasSinStockTienda.SeAplica(pedido, "REI", true));
            Assert.IsFalse(ReglaMuestrasSinStockTienda.SeAplica(pedido, "ALG", true));
            Assert.IsFalse(ReglaMuestrasSinStockTienda.SeAplica(pedido, "ALC", false));
            Assert.IsFalse(ReglaMuestrasSinStockTienda.SeAplica(new PedidoVentaDTO { EsPresupuesto = true }, "ALC", true));
            Assert.IsFalse(ReglaMuestrasSinStockTienda.SeAplica(new PedidoVentaDTO { notaEntrega = true }, "ALC", true));
        }

        [TestMethod]
        public void UnidadesQueSalen_LaReglaDelPicking()
        {
            Assert.AreEqual(0, GestorMuestrasSinStock.UnidadesQueSalen(3, 0));
            Assert.AreEqual(0, GestorMuestrasSinStock.UnidadesQueSalen(3, -2));
            Assert.AreEqual(1, GestorMuestrasSinStock.UnidadesQueSalen(3, 1));
            Assert.AreEqual(3, GestorMuestrasSinStock.UnidadesQueSalen(3, 7));
        }

        // ---- El aviso ----

        [TestMethod]
        public void Aviso_Varias_Enteras_YUnaReducida()
        {
            var quitadas = new List<MuestraQuitadaTienda>
            {
                new MuestraQuitadaTienda { Producto = MUESTRA, Nombre = "BOLSA ANUBIS PAPEL", Pedidas = 1, Quedan = 0 },
                new MuestraQuitadaTienda { Producto = "40001", Nombre = "SOBRE SERUM", Pedidas = 2, Quedan = 0 },
                new MuestraQuitadaTienda { Producto = MUESTRA2, Nombre = "VIAL CREMA", Pedidas = 3, Quedan = 1 }
            };

            AvisoPedidoDTO aviso = ReglaMuestrasSinStockTienda.Aviso("ALC", quitadas);

            Assert.AreEqual("MuestrasSinStockQuitadas", aviso.Tipo);
            Assert.AreEqual(
                "En Alcobendas no hay stock de estas muestras y se han quitado del pedido: 39918 BOLSA ANUBIS PAPEL, 40001 SOBRE SERUM. " +
                "De 42654 VIAL CREMA solo hay 1 de 3; se ha dejado en lo que hay. Si las necesita, pase el pedido desde Algete (ALG).", aviso.Mensaje);
        }

        [TestMethod]
        public void Aviso_UnaEntera_Singular()
        {
            AvisoPedidoDTO aviso = ReglaMuestrasSinStockTienda.Aviso("REI",
                new List<MuestraQuitadaTienda> { new MuestraQuitadaTienda { Producto = MUESTRA, Nombre = "BOLSA ANUBIS PAPEL", Pedidas = 1, Quedan = 0 } });

            Assert.AreEqual("En Reina no hay stock de esta muestra y se ha quitado del pedido: 39918 BOLSA ANUBIS PAPEL. " +
                "Si la necesita, pase el pedido desde Algete (ALG).", aviso.Mensaje);
        }

        [TestMethod]
        public void Aviso_SoloReducida()
        {
            AvisoPedidoDTO aviso = ReglaMuestrasSinStockTienda.Aviso("ALC",
                new List<MuestraQuitadaTienda> { new MuestraQuitadaTienda { Producto = MUESTRA2, Nombre = "VIAL CREMA", Pedidas = 3, Quedan = 1 } });

            Assert.AreEqual("En Alcobendas no hay stock suficiente de esta muestra: de 42654 VIAL CREMA solo hay 1 de 3; se ha dejado en lo que hay. " +
                "Si necesita más, pase el pedido desde Algete (ALG).", aviso.Mensaje);
            Assert.IsNull(ReglaMuestrasSinStockTienda.Aviso("ALC", new List<MuestraQuitadaTienda>()));
        }

        [TestMethod]
        public void Rastro_MismaTablaQueElPicking_ConElUsuarioQueGuarda()
        {
            var quitada = new MuestraQuitadaTienda { NumeroOrden = 77, Producto = MUESTRA, Pedidas = 2, Quedan = 0 };

            Modificacion m = ReglaMuestrasSinStockTienda.Modificacion("1", 931600, "ALC", "NUEVAVISION\\Sancho", quitada, new System.DateTime(2026, 11, 25));

            Assert.AreEqual(EscritorMuestrasSinStock.TABLA_MODIFICACIONES, m.Tabla);
            Assert.AreEqual("NUEVAVISION\\Sancho", m.Usuario);
            StringAssert.Contains(m.Anterior, "\"Pedido\":931600");
            StringAssert.Contains(m.Anterior, "\"NumeroOrden\":77");
            StringAssert.Contains(m.Nuevo, ReglaMuestrasSinStockTienda.OPERACION);
            StringAssert.Contains(m.Nuevo, "\"LineaBorrada\":true");
        }

        // ---- PUT: qué se puede tocar ----

        [TestMethod]
        public void Put_SoloNuevasOPendientesSinPicking_YNadaYaGuardadoSiElPedidoTienePicking()
        {
            var sinPicking = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 10, Estado = -1, Picking = 0 },
                new LinPedidoVta { Nº_Orden = 11, Estado = 1, Picking = 0 }
            };
            Assert.IsTrue(PedidosVentaController.SePuedeQuitarMuestraAlModificar(Linea(MUESTRA, 1), sinPicking));
            Assert.IsTrue(PedidosVentaController.SePuedeQuitarMuestraAlModificar(Linea(MUESTRA, 1, id: 10), sinPicking));
            Assert.IsFalse(PedidosVentaController.SePuedeQuitarMuestraAlModificar(Linea(MUESTRA, 1, id: 11, estado: 1), sinPicking), "En curso: no");

            var conPicking = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 10, Estado = -1, Picking = 0 },
                new LinPedidoVta { Nº_Orden = 12, Estado = 1, Picking = 100 }
            };
            Assert.IsTrue(PedidosVentaController.SePuedeQuitarMuestraAlModificar(Linea(MUESTRA, 1), conPicking), "Lo que se añade ahora, sí");
            Assert.IsFalse(PedidosVentaController.SePuedeQuitarMuestraAlModificar(Linea(MUESTRA, 1, id: 10), conPicking));
            Assert.IsFalse(PedidosVentaController.SePuedeQuitarMuestraAlModificar(Linea(MUESTRA, 1, id: 12, estado: 1, picking: 100), conPicking));
        }

        // ---- El camino del guardado (servicio de validación de verdad sobre la BD falsa) ----

        private static PedidosVentaController Controlador(int stockMuestra, int stockMuestra2 = 0)
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.Productos).Returns(FakeDbSet(new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = MUESTRA, Nombre = "BOLSA ANUBIS PAPEL", SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS },
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = MUESTRA2, Nombre = "VIAL CREMA", SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS },
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = PRODUCTO, Nombre = "CHAMPU", SubGrupo = "PEL" }
            }));
            A.CallTo(() => db.Ganavisiones).Returns(FakeDbSet(new List<Ganavision>()));
            IProductoService productoService = A.Fake<IProductoService>();
            A.CallTo(() => productoService.CalcularStockProducto(MUESTRA, A<string>._, A<int?>._))
                .Returns(Task.FromResult(new ProductoDTO.StockProducto { Almacen = "ALC", Stock = stockMuestra }));
            A.CallTo(() => productoService.CalcularStockProducto(MUESTRA2, A<string>._, A<int?>._))
                .Returns(Task.FromResult(new ProductoDTO.StockProducto { Almacen = "ALC", Stock = stockMuestra2 }));
            var servicio = new ServicioValidarServirJunto(db, productoService, A.Fake<ILogService>());
            return new PedidosVentaController(db, servicio);
        }

        private static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas)
            => new PedidoVentaDTO
            {
                empresa = "1", numero = 0, cliente = "15191", contacto = "0", modoServicio = M.SEGUN_VAYA_ENTRANDO, servirJunto = false,
                Lineas = lineas.ToList()
            };

        [TestMethod]
        public async Task Post_Tienda_MuestraSinStock_SeQuitaYSeGuarda()
        {
            PedidosVentaController controller = Controlador(stockMuestra: 0);
            PedidoVentaDTO pedido = Pedido(Linea(PRODUCTO, 2), Linea(MUESTRA, 1));
            var quitadas = new List<MuestraQuitadaTienda>();

            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoQuitandoMuestrasDeTiendaAsync(pedido, null, _ => true, quitadas);

            Assert.IsNull(falla, falla?.Mensaje);
            Assert.AreEqual(PRODUCTO, pedido.Lineas.Single().Producto);
            Assert.AreEqual(MUESTRA, quitadas.Single().Producto);
            Assert.AreEqual("BOLSA ANUBIS PAPEL", quitadas.Single().Nombre);
        }

        [TestMethod]
        public async Task Post_Tienda_MuestraConStockParcial_SeDejaEnLoQueHay()
        {
            PedidosVentaController controller = Controlador(stockMuestra: 1);
            PedidoVentaDTO pedido = Pedido(Linea(PRODUCTO, 2), Linea(MUESTRA, 3));
            var quitadas = new List<MuestraQuitadaTienda>();

            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoQuitandoMuestrasDeTiendaAsync(pedido, null, _ => true, quitadas);

            Assert.IsNull(falla, falla?.Mensaje);
            Assert.AreEqual(1, pedido.Lineas.Single(l => l.Producto == MUESTRA).Cantidad);
            Assert.AreEqual(2, quitadas.Single().Quitadas);
        }

        [TestMethod]
        public async Task Put_Tienda_LaMuestraEnCursoNoSeToca_El400DeSiempre()
        {
            PedidosVentaController controller = Controlador(stockMuestra: 0);
            LineaPedidoVentaDTO enCurso = Linea(MUESTRA, 1, id: 11, estado: 1, picking: 100);
            PedidoVentaDTO pedido = Pedido(Linea(PRODUCTO, 2, id: 10), enCurso);
            pedido.numero = 931600;
            var guardadas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 10, Estado = -1, Picking = 0, Producto = PRODUCTO, Cantidad = 2, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO },
                new LinPedidoVta { Nº_Orden = 11, Estado = 1, Picking = 100, Producto = MUESTRA, Cantidad = 1, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO }
            };
            var quitadas = new List<MuestraQuitadaTienda>();

            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoQuitandoMuestrasDeTiendaAsync(pedido, null,
                l => PedidosVentaController.SePuedeQuitarMuestraAlModificar(l, guardadas), quitadas);

            Assert.IsNotNull(falla);
            Assert.AreEqual(2, pedido.Lineas.Count);
            Assert.IsFalse(quitadas.Any());
        }

        [TestMethod]
        public async Task Put_Tienda_MuestraNueva_SeQuita()
        {
            PedidosVentaController controller = Controlador(stockMuestra: 0);
            PedidoVentaDTO pedido = Pedido(Linea(PRODUCTO, 2, id: 10), Linea(MUESTRA, 1));
            pedido.numero = 931600;
            var guardadas = new List<LinPedidoVta> { new LinPedidoVta { Nº_Orden = 10, Estado = -1, Picking = 0 } };
            var quitadas = new List<MuestraQuitadaTienda>();

            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoQuitandoMuestrasDeTiendaAsync(pedido, null,
                l => PedidosVentaController.SePuedeQuitarMuestraAlModificar(l, guardadas), quitadas);

            Assert.IsNull(falla, falla?.Mensaje);
            Assert.AreEqual(PRODUCTO, pedido.Lineas.Single().Producto);
            Assert.IsNull(quitadas.Single().NumeroOrden);
        }

        [TestMethod]
        public async Task Tienda_SoloMuestras_El400DeHoy_DiciendoPorQue()
        {
            PedidosVentaController controller = Controlador(stockMuestra: 0);
            PedidoVentaDTO pedido = Pedido(Linea(MUESTRA, 1));
            var quitadas = new List<MuestraQuitadaTienda>();

            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoQuitandoMuestrasDeTiendaAsync(pedido, null, _ => true, quitadas);

            Assert.IsNotNull(falla);
            StringAssert.EndsWith(falla.Mensaje, ReglaMuestrasSinStockTienda.TextoSoloMuestras());
            Assert.AreEqual(1, pedido.Lineas.Count);
            Assert.IsFalse(quitadas.Any());
        }

        [TestMethod]
        public async Task Tienda_InterruptorApagado_El400DeHoy()
        {
            GestorMuestrasSinStock.LeerAjuste = _ => "false";
            PedidosVentaController controller = Controlador(stockMuestra: 0);
            PedidoVentaDTO pedido = Pedido(Linea(PRODUCTO, 2), Linea(MUESTRA, 1));
            var quitadas = new List<MuestraQuitadaTienda>();

            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoQuitandoMuestrasDeTiendaAsync(pedido, null, _ => true, quitadas);

            Assert.IsNotNull(falla);
            Assert.AreEqual(2, pedido.Lineas.Count);
            Assert.IsFalse(quitadas.Any());
        }

        [TestMethod]
        public async Task Algete_MuestraSinStock_ComoSiempre()
        {
            PedidosVentaController controller = Controlador(stockMuestra: 0);
            PedidoVentaDTO pedido = Pedido(Linea(PRODUCTO, 2, "ALG"), Linea(MUESTRA, 1, "ALG"));
            var quitadas = new List<MuestraQuitadaTienda>();

            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoQuitandoMuestrasDeTiendaAsync(pedido, null, _ => true, quitadas);

            Assert.IsNotNull(falla);
            Assert.AreEqual(2, pedido.Lineas.Count);
        }

        [TestMethod]
        public async Task Tienda_DenegacionDeRegalos_SinCambios()
        {
            var denegacion = new ValidarServirJuntoResponse
            {
                PuedeDesmarcar = false,
                Mensaje = "regalos sin stock",
                ProductosProblematicos = new List<ProductoSinStockDTO> { new ProductoSinStockDTO { ProductoId = MUESTRA, Disponibles = 0 } }
            };
            IServicioValidarServirJunto servicio = A.Fake<IServicioValidarServirJunto>();
            A.CallTo(() => servicio.Validar(A<ValidarServirJuntoRequest>._)).Returns(Task.FromResult(denegacion));
            var controller = new PedidosVentaController(A.Fake<NVEntities>(), servicio);
            PedidoVentaDTO pedido = Pedido(Linea(PRODUCTO, 2), Linea(MUESTRA, 1));
            var quitadas = new List<MuestraQuitadaTienda>();

            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoQuitandoMuestrasDeTiendaAsync(pedido, null, _ => true, quitadas);

            Assert.AreSame(denegacion, falla);
            Assert.AreEqual("regalos sin stock", falla.Mensaje);
            Assert.AreEqual(2, pedido.Lineas.Count);
            Assert.IsFalse(quitadas.Any());
        }

        [TestMethod]
        public async Task Validador_MarcaLaDenegacionDeMuestras_ConSuDisponible_SinQueViajeEnElJson()
        {
            PedidosVentaController controller = Controlador(stockMuestra: 1);
            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoDesdePedidoAsync(Pedido(Linea(PRODUCTO, 2), Linea(MUESTRA, 3)));

            Assert.IsTrue(falla.DenegadoPorMuestras);
            Assert.AreEqual(1, falla.ProductosProblematicos.Single().Disponibles);
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(falla);
            Assert.IsFalse(json.Contains("Disponibles"), json);
            Assert.IsFalse(json.Contains("DenegadoPorMuestras"), json);
        }

        private static DbSet<T> FakeDbSet<T>(List<T> data) where T : class
        {
            IQueryable<T> queryable = data.AsQueryable();
            DbSet<T> fakeDbSet = A.Fake<DbSet<T>>(o => o
                .Implements<IQueryable<T>>()
                .Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(queryable.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(queryable.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(queryable.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(queryable.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => queryable.GetEnumerator());
            return fakeDbSet;
        }
    }
}
