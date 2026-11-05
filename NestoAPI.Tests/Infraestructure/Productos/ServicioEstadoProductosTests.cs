using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Productos
{
    /// <summary>NestoAPI#738 (sugerencia 793, Manuel): el servicio con el repositorio falso.</summary>
    [TestClass]
    public class ServicioEstadoProductosTests
    {
        private IRepositorioEstadoProductos repositorio;
        private ServicioEstadoProductos servicio;
        private Dictionary<string, DatosEstadoProducto> datos;

        [TestInitialize]
        public void Setup()
        {
            repositorio = A.Fake<IRepositorioEstadoProductos>();
            A.CallTo(() => repositorio.LeerEstados(A<string>._)).Returns(Task.FromResult(new List<EstadoProductoDTO>
            {
                new EstadoProductoDTO { Estado = 0, Descripcion = "DISPONIBLE" },
                new EstadoProductoDTO { Estado = 1, Descripcion = "SOBRE PEDIDO" },
                new EstadoProductoDTO { Estado = 3, Descripcion = "EXTINGUIR", AExtinguir = true }
            }));
            datos = new Dictionary<string, DatosEstadoProducto>(StringComparer.OrdinalIgnoreCase);
            A.CallTo(() => repositorio.Cargar(A<string>._, A<IList<string>>._)).ReturnsLazily(() => Task.FromResult(datos));
            A.CallTo(() => repositorio.Aplicar(A<string>._, A<IList<DecisionEstadoProducto>>._, A<string>._))
                .ReturnsLazily((string e, IList<DecisionEstadoProducto> c, string u) => Task.FromResult(new ResultadoAplicacionEstado
                {
                    Cambiados = c.Count,
                    ControlesQuitados = c.Sum(x => x.ControlesAQuitar.Count),
                    ControlesCreados = c.Sum(x => x.ControlesACrear.Count)
                }));
            servicio = new ServicioEstadoProductos(repositorio);
        }

        private void Producto(string referencia, short? estado, params ControlStockActual[] controles)
        {
            datos[referencia] = new DatosEstadoProducto { Producto = referencia, Nombre = "P " + referencia, Estado = estado, Controles = controles.ToList() };
        }

        [TestMethod]
        public async Task Cambiar_EstadoQueNoExiste_400ConElMensaje()
        {
            PeticionEstadoProductosNoValidaException ex = await Assert.ThrowsExceptionAsync<PeticionEstadoProductosNoValidaException>(
                () => servicio.Cambiar(new CambioEstadoProductosPeticion { Productos = new List<string> { "17404" }, EstadoNuevo = 8 }, "yo"));

            Assert.AreEqual("El estado 8 no existe en la tabla de estados de los productos.", ex.Message);
            A.CallTo(() => repositorio.Aplicar(A<string>._, A<IList<DecisionEstadoProducto>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cambiar_SinEstadoOSinProductos_400()
        {
            await Assert.ThrowsExceptionAsync<PeticionEstadoProductosNoValidaException>(
                () => servicio.Cambiar(new CambioEstadoProductosPeticion { Productos = new List<string> { "17404" } }, "yo"));
            await Assert.ThrowsExceptionAsync<PeticionEstadoProductosNoValidaException>(
                () => servicio.Cambiar(new CambioEstadoProductosPeticion { Productos = new List<string> { " " }, EstadoNuevo = 3 }, "yo"));
        }

        [TestMethod]
        public async Task Cambiar_MasDe2000_400()
        {
            List<string> muchos = Enumerable.Range(1, 2001).Select(i => i.ToString()).ToList();

            await Assert.ThrowsExceptionAsync<PeticionEstadoProductosNoValidaException>(
                () => servicio.Cambiar(new CambioEstadoProductosPeticion { Productos = muchos, EstadoNuevo = 3 }, "yo"));
        }

        [TestMethod]
        public async Task Cambiar_ConControlesSinQuitarlos_OmiteEsosYCambiaLosDemas()
        {
            Producto("1", 0, new ControlStockActual { Almacen = "ALG", Maximo = 5 });
            Producto("2", 0);

            ResultadoCambioEstadoProductosDTO r = await servicio.Cambiar(new CambioEstadoProductosPeticion
            {
                Productos = new List<string> { "1", "2" },
                EstadoNuevo = 3,
                QuitarControlesStock = false
            }, "NUEVAVISION\\Manuel");

            Assert.AreEqual(1, r.Cambiados);
            Assert.AreEqual("1", r.Omitidos.Single().Producto);
            Assert.AreEqual("Tiene controles de stock en ALG.", r.Omitidos.Single().Motivo);
            Assert.IsTrue(r.Avisos.Contains("1 producto no ha cambiado de estado porque tiene controles de stock."));
            A.CallTo(() => repositorio.Aplicar("1", A<IList<DecisionEstadoProducto>>.That.Matches(c => c.Count == 1 && c[0].Producto == "2"), "NUEVAVISION\\Manuel"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Cambiar_ConControlesQuitandolos_LosQuitaYCambiaElEstado()
        {
            Producto("1", 0, new ControlStockActual { Almacen = "ALG", Maximo = 5, Minimo = 2 }, new ControlStockActual { Almacen = "REI", Maximo = 1 });

            ResultadoCambioEstadoProductosDTO r = await servicio.Cambiar(new CambioEstadoProductosPeticion
            {
                Productos = new List<string> { "1" },
                EstadoNuevo = 3,
                QuitarControlesStock = true
            }, "yo");

            Assert.AreEqual(1, r.Cambiados);
            Assert.AreEqual(0, r.Omitidos.Count);
            Assert.AreEqual(2, r.ControlesStockQuitados);
        }

        [TestMethod]
        public async Task Cambiar_VuelveADisponible_PideLosSugeridosSoloDeLosQueVuelven()
        {
            Producto("1", 3);
            Producto("2", 0);
            A.CallTo(() => repositorio.SugerirControles(A<string>._, A<IList<string>>._)).Returns(Task.FromResult(
                new Dictionary<string, List<ControlStockSugeridoDTO>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["1"] = new List<ControlStockSugeridoDTO> { new ControlStockSugeridoDTO { Almacen = "ALG", StockMaximo = 8, StockMinimo = 3 } }
                }));

            ResultadoCambioEstadoProductosDTO r = await servicio.Cambiar(new CambioEstadoProductosPeticion
            {
                Productos = new List<string> { "1", "2" },
                EstadoNuevo = 0
            }, "yo");

            Assert.AreEqual(1, r.Cambiados);
            Assert.AreEqual("Ya está en estado 0.", r.Omitidos.Single().Motivo);
            Assert.AreEqual(1, r.ControlesStockCreados);
            A.CallTo(() => repositorio.SugerirControles("1", A<IList<string>>.That.Matches(l => l.Count == 1 && l[0] == "1"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Cambiar_VuelveADisponibleSinSugeridos_AvisaDeRevisarLosMaximos()
        {
            Producto("1", 3);
            A.CallTo(() => repositorio.SugerirControles(A<string>._, A<IList<string>>._))
                .Returns(Task.FromResult(new Dictionary<string, List<ControlStockSugeridoDTO>>()));

            ResultadoCambioEstadoProductosDTO r = await servicio.Cambiar(new CambioEstadoProductosPeticion
            {
                Productos = new List<string> { "1" },
                EstadoNuevo = 0
            }, "yo");

            Assert.AreEqual(1, r.Cambiados);
            Assert.AreEqual(0, r.ControlesStockCreados);
            Assert.IsTrue(r.Avisos.Contains("1 producto ha vuelto a Disponible sin controles de stock: revise los máximos."));
        }

        [TestMethod]
        public async Task Cambiar_TodosOmitidos_NoLlamaAAplicar()
        {
            Producto("1", 3);

            ResultadoCambioEstadoProductosDTO r = await servicio.Cambiar(new CambioEstadoProductosPeticion
            {
                Productos = new List<string> { "1", "99999" },
                EstadoNuevo = 3
            }, "yo");

            Assert.AreEqual(0, r.Cambiados);
            Assert.AreEqual(2, r.Omitidos.Count);
            Assert.AreEqual(ReglasEstadoProducto.MOTIVO_NO_EXISTE, r.Omitidos.Single(o => o.Producto == "99999").Motivo);
            A.CallTo(() => repositorio.Aplicar(A<string>._, A<IList<DecisionEstadoProducto>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task VistaPrevia_SinFiltro_400()
        {
            await Assert.ThrowsExceptionAsync<PeticionEstadoProductosNoValidaException>(
                () => servicio.VistaPrevia(new VistaPreviaEstadoProductosPeticion { EstadoNuevo = 3, Filtro = new FiltroEstadoProductos() }, true));
        }

        [TestMethod]
        public async Task VistaPrevia_UnProducto_TraeStockPendientesControlesYPregunta()
        {
            Producto("17404", 0, new ControlStockActual { Almacen = "REI", Maximo = 2 });
            datos["17404"].Stock["ALG"] = 7;
            datos["17404"].PendienteRecibir = 4;
            datos["17404"].PedidosPendientes.Add(new PedidoPendienteEstadoDTO { Pedido = 900001, Cliente = "15191", Cantidad = 1 });
            A.CallTo(() => repositorio.ProductosDelFiltro(A<string>._, A<FiltroEstadoProductos>._)).Returns(Task.FromResult(new List<string> { "17404" }));

            VistaPreviaEstadoProductosDTO v = await servicio.VistaPrevia(new VistaPreviaEstadoProductosPeticion
            {
                Filtro = new FiltroEstadoProductos { Productos = new List<string> { "17404" } },
                EstadoNuevo = 3
            }, puedeCambiar: false);

            ProductoEstadoVistaPreviaDTO p = v.Productos.Single();
            Assert.AreEqual((short)0, p.EstadoActual);
            Assert.AreEqual(7, p.StockALG);
            Assert.AreEqual(4, p.PendienteRecibirProveedor);
            Assert.AreEqual(900001, p.PedidosPendientes.Single().Pedido);
            Assert.IsTrue(p.TieneControlesStock.REI);
            Assert.IsFalse(p.TieneControlesStock.ALG);
            Assert.IsNull(p.MotivoNoSeCambia);
            Assert.AreEqual(1, v.ControlesStockAfectados);
            Assert.AreEqual(1, v.ProductosConPedidosPendientes);
            Assert.IsNotNull(v.PreguntaControlesStock);
            Assert.IsFalse(v.PuedeCambiar);
            A.CallTo(() => repositorio.Aplicar(A<string>._, A<IList<DecisionEstadoProducto>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task VistaPrevia_VuelveADisponible_EnsenaLosSugeridos()
        {
            Producto("17404", 3);
            A.CallTo(() => repositorio.ProductosDelFiltro(A<string>._, A<FiltroEstadoProductos>._)).Returns(Task.FromResult(new List<string> { "17404" }));
            A.CallTo(() => repositorio.SugerirControles(A<string>._, A<IList<string>>._)).Returns(Task.FromResult(
                new Dictionary<string, List<ControlStockSugeridoDTO>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["17404"] = new List<ControlStockSugeridoDTO> { new ControlStockSugeridoDTO { Almacen = "ALG", StockMaximo = 6, StockMinimo = 2, Explicacion = "Excel" } }
                }));

            VistaPreviaEstadoProductosDTO v = await servicio.VistaPrevia(new VistaPreviaEstadoProductosPeticion
            {
                Filtro = new FiltroEstadoProductos { Familia = "Lisap" },
                EstadoNuevo = 0
            }, true);

            Assert.AreEqual(6, v.Productos.Single().ControlesSugeridos.Single().StockMaximo);
            Assert.IsNull(v.PreguntaControlesStock);
        }
    }
}
