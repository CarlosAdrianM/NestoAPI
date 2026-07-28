using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.PedidosCompra;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#367: red de seguridad del algoritmo de AmpliarPedidoAlStockMaximo ANTES de
    /// reescribir DatosProductosProcesados (la consulta EF de 69K chars que agota el timeout).
    /// Capturan la semántica actual: Cantidad = StockMax − Stock(ALG) + PendienteEntregar −
    /// PendienteRecibir, redondeo al múltiplo superior, se incluyen líneas con necesidad 0 si
    /// StockMaximo > 0, no se duplican productos del pedido y solo entran productos del
    /// proveedor con estado NO_SOBRE_PEDIDO.
    /// </summary>
    [TestClass]
    public class AmpliarPedidoAlStockMaximoTests
    {
        private const string EMPRESA = "1";
        private const string PROVEEDOR = "691";
        private const string PRODUCTO = "12345";

        private NVEntities db;
        private PedidosCompraController controlador;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            ConfigurarProductos();
            ConfigurarControles();
            ConfigurarExtractos();
            ConfigurarLinPedidoVtas();
            ConfigurarLinPedidoCmps();
            ConfigurarDescuentos();
            ConfigurarOfertas();
            controlador = new PedidosCompraController(db);
        }

        [TestMethod]
        public async Task Ampliar_CalculaLaCantidadConStockYPendientes()
        {
            // StockMax 10 − Stock 3 (2+1 en ALG) + PendEntregar 2 − PendRecibir 1 = 8
            ConfigurarProductos(CrearProducto(PRODUCTO));
            ConfigurarControles(new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = PRODUCTO, StockMáximo = 10, Múltiplos = 1 });
            ConfigurarExtractos(
                new ExtractoProducto { Almacén = "ALG", Número = PRODUCTO, Cantidad = 2 },
                new ExtractoProducto { Almacén = "ALG", Número = PRODUCTO, Cantidad = 1 });
            ConfigurarLinPedidoVtas(new LinPedidoVta { TipoLinea = 1, Estado = -1, Producto = PRODUCTO, Cantidad = 2 });
            ConfigurarLinPedidoCmps(new LinPedidoCmp { TipoLínea = "1", Estado = 1, Producto = PRODUCTO, Cantidad = 1 });

            PedidoCompraDTO resultado = await Ampliar();

            LineaPedidoCompraDTO linea = resultado.Lineas.Single();
            Assert.AreEqual(PRODUCTO, linea.Producto);
            Assert.AreEqual(8, linea.Cantidad);
            Assert.AreEqual(8, linea.CantidadBruta);
            Assert.AreEqual(3, linea.Stock);
            Assert.AreEqual(2, linea.PendienteEntregar);
            Assert.AreEqual(1, linea.PendienteRecibir);
            Assert.AreEqual(10, linea.StockMaximo);
            Assert.AreEqual(new DateTime(2026, 7, 28), linea.FechaRecepcion, "Sin líneas previas usa la fecha del pedido");
        }

        [TestMethod]
        public async Task Ampliar_RedondeaLaCantidadAlMultiploSuperior()
        {
            ConfigurarProductos(CrearProducto(PRODUCTO));
            ConfigurarControles(new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = PRODUCTO, StockMáximo = 10, Múltiplos = 6 });

            PedidoCompraDTO resultado = await Ampliar();

            Assert.AreEqual(12, resultado.Lineas.Single().Cantidad);
            Assert.AreEqual(10, resultado.Lineas.Single().CantidadBruta);
        }

        [TestMethod]
        public async Task Ampliar_MultiplosCeroSeTrataComoUno()
        {
            // Bug latente del código original (#367): con Múltiplos = 0 el módulo petaba con
            // división por cero (la normalización a 1 se hacía DESPUÉS). Comportamiento deseado:
            // tratar 0 como 1.
            ConfigurarProductos(CrearProducto(PRODUCTO));
            ConfigurarControles(new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = PRODUCTO, StockMáximo = 10, Múltiplos = 0 });

            PedidoCompraDTO resultado = await Ampliar();

            Assert.AreEqual(10, resultado.Lineas.Single().Cantidad);
            Assert.AreEqual(1, resultado.Lineas.Single().Multiplos);
        }

        [TestMethod]
        public async Task Ampliar_NoDuplicaProductosQueYaEstanEnElPedido()
        {
            ConfigurarProductos(CrearProducto(PRODUCTO));
            ConfigurarControles(new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = PRODUCTO, StockMáximo = 10, Múltiplos = 1 });
            PedidoCompraDTO pedido = CrearPedido();
            pedido.Lineas = new List<LineaPedidoCompraDTO>
            {
                new LineaPedidoCompraDTO { Producto = PRODUCTO, Cantidad = 5, FechaRecepcion = new DateTime(2026, 8, 1) }
            };

            PedidoCompraDTO resultado = await Ampliar(pedido);

            Assert.AreEqual(1, resultado.Lineas.Count(), "El producto ya estaba en el pedido: no se añade línea nueva");
            Assert.AreEqual(5, resultado.Lineas.Single().Cantidad);
        }

        [TestMethod]
        public async Task Ampliar_SoloEntranProductosDelProveedorYNoSobrePedido()
        {
            Producto otroProveedor = CrearProducto("22222");
            otroProveedor.ProveedoresProductoes.Single().Nº_Proveedor = "999";
            Producto sobrePedido = CrearProducto("33333");
            sobrePedido.Estado = 1;
            ConfigurarProductos(CrearProducto(PRODUCTO), otroProveedor, sobrePedido);
            ConfigurarControles(
                new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = PRODUCTO, StockMáximo = 10, Múltiplos = 1 },
                new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = "22222", StockMáximo = 10, Múltiplos = 1 },
                new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = "33333", StockMáximo = 10, Múltiplos = 1 });

            PedidoCompraDTO resultado = await Ampliar();

            Assert.AreEqual(PRODUCTO, resultado.Lineas.Single().Producto);
        }

        [TestMethod]
        public async Task Ampliar_SinNecesidadPeroConStockMaximo_EntraConCantidadCero()
        {
            // Stock (15) por encima del máximo (10): CantidadBruta negativa, Cantidad 0, pero la
            // línea SÍ se añade (StockMaximo > 0) para que el comprador la vea. Un producto sin
            // control de stock y sin necesidad no entra.
            ConfigurarProductos(CrearProducto(PRODUCTO), CrearProducto("44444"));
            ConfigurarControles(new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = PRODUCTO, StockMáximo = 10, Múltiplos = 1 });
            ConfigurarExtractos(new ExtractoProducto { Almacén = "ALG", Número = PRODUCTO, Cantidad = 15 });

            PedidoCompraDTO resultado = await Ampliar();

            LineaPedidoCompraDTO linea = resultado.Lineas.Single();
            Assert.AreEqual(PRODUCTO, linea.Producto);
            Assert.AreEqual(0, linea.Cantidad);
            Assert.AreEqual(-5, linea.CantidadBruta);
        }

        [TestMethod]
        public async Task Ampliar_ElStockSoloCuentaElAlmacenDeAlgete()
        {
            ConfigurarProductos(CrearProducto(PRODUCTO));
            ConfigurarControles(new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = PRODUCTO, StockMáximo = 10, Múltiplos = 1 });
            ConfigurarExtractos(
                new ExtractoProducto { Almacén = "ALG", Número = PRODUCTO, Cantidad = 2 },
                new ExtractoProducto { Almacén = "REI", Número = PRODUCTO, Cantidad = 5 });

            PedidoCompraDTO resultado = await Ampliar();

            Assert.AreEqual(2, resultado.Lineas.Single().Stock);
            Assert.AreEqual(8, resultado.Lineas.Single().Cantidad);
        }

        [TestMethod]
        public async Task Ampliar_AdjuntaDescuentosYOfertasDelProveedor()
        {
            ConfigurarProductos(CrearProducto(PRODUCTO));
            ConfigurarControles(new ControlStock { Empresa = EMPRESA, Almacén = "ALG", Número = PRODUCTO, StockMáximo = 10, Múltiplos = 1 });
            ConfigurarDescuentos(new DescuentosProducto { Empresa = EMPRESA, NºProveedor = PROVEEDOR, Nº_Producto = PRODUCTO, CantidadMínima = 6, Descuento = 0.1m, Precio = 5m });
            ConfigurarOfertas(new OfertaProveedor { Empresa = EMPRESA, NºProveedor = PROVEEDOR, Producto = PRODUCTO, CantidadOferta = 5, CantidadRegalo = 1 });

            PedidoCompraDTO resultado = await Ampliar();

            LineaPedidoCompraDTO linea = resultado.Lineas.Single();
            Assert.AreEqual(1, linea.Descuentos.Count);
            Assert.AreEqual(5m, linea.Descuentos.Single().Precio);
            Assert.AreEqual(1, linea.Ofertas.Count);
            Assert.AreEqual(5, linea.Ofertas.Single().CantidadCobrada);
            // El setter de Cantidad aplica el descuento por cantidad (10 >= 6): se conserva.
            Assert.AreEqual(5m, linea.PrecioUnitario);
            Assert.AreEqual(0.1m, linea.DescuentoProducto);
        }

        private Producto CrearProducto(string numero)
        {
            var producto = new Producto
            {
                Empresa = EMPRESA,
                Número = numero,
                Nombre = "PRODUCTO PRUEBA",
                Grupo = "COS",
                SubGrupo = "ACP",
                IVA_Soportado = "R21",
                PVP = 7m,
                Estado = 0
            };
            producto.ProveedoresProductoes.Add(new ProveedoresProducto { Empresa = EMPRESA, Nº_Producto = numero, Nº_Proveedor = PROVEEDOR });
            return producto;
        }

        private static PedidoCompraDTO CrearPedido() => new PedidoCompraDTO
        {
            Empresa = EMPRESA,
            Proveedor = PROVEEDOR,
            Fecha = new DateTime(2026, 7, 28)
        };

        private async Task<PedidoCompraDTO> Ampliar(PedidoCompraDTO pedido = null)
        {
            var resultado = await controlador.AmpliarPedidoAlStockMaximo(pedido ?? CrearPedido())
                as OkNegotiatedContentResult<PedidoCompraDTO>;
            Assert.IsNotNull(resultado);
            return resultado.Content;
        }

        #region Fakes de DbSets
        private void ConfigurarProductos(params Producto[] datos) => ConfigurarSet(datos, s => A.CallTo(() => db.Productos).Returns(s));
        private void ConfigurarControles(params ControlStock[] datos) => ConfigurarSet(datos, s => A.CallTo(() => db.ControlesStocks).Returns(s));
        private void ConfigurarExtractos(params ExtractoProducto[] datos) => ConfigurarSet(datos, s => A.CallTo(() => db.ExtractosProducto).Returns(s));
        private void ConfigurarLinPedidoVtas(params LinPedidoVta[] datos) => ConfigurarSet(datos, s => A.CallTo(() => db.LinPedidoVtas).Returns(s));
        private void ConfigurarLinPedidoCmps(params LinPedidoCmp[] datos) => ConfigurarSet(datos, s => A.CallTo(() => db.LinPedidoCmps).Returns(s));
        private void ConfigurarDescuentos(params DescuentosProducto[] datos) => ConfigurarSet(datos, s => A.CallTo(() => db.DescuentosProductoes).Returns(s));
        private void ConfigurarOfertas(params OfertaProveedor[] datos) => ConfigurarSet(datos, s => A.CallTo(() => db.OfertasProveedores).Returns(s));

        private static void ConfigurarSet<T>(T[] datos, Action<DbSet<T>> asignar) where T : class
        {
            var fakeSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            IQueryable<T> queryable = datos.AsQueryable();
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider).Returns(queryable.Provider);
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).Returns(queryable.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(queryable.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).ReturnsLazily(() => queryable.GetEnumerator());
            // Include(...) sobre el fake devolvería null (DbQuery no es fakeable): que se devuelva
            // a sí mismo, las navegaciones ya están pobladas en memoria.
            A.CallTo(() => ((DbQuery<T>)fakeSet).Include(A<string>._)).Returns((DbQuery<T>)fakeSet);
            asignar(fakeSet);
        }
        #endregion
    }
}
