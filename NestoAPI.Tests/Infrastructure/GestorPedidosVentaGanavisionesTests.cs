using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Controllers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Issue #279: al leer un pedido, las líneas a 0€ sin oferta se confirman contra la tabla
    /// Ganavision y solo las reales se marcan con EsBonificadoGanavisiones. Así los clientes
    /// (Nesto#397) distinguen Ganavisiones de MMP/regalos por importe sin heurísticas de texto
    /// (el "(BONIFICADO)" del texto se trunca a 50 caracteres y no es fiable).
    /// </summary>
    [TestClass]
    public class GestorPedidosVentaGanavisionesTests
    {
        private const string PRODUCTO_GANAVISION = "45473";
        private const string PRODUCTO_MMP = "18365";
        private const string PRODUCTO_NORMAL = "31001";

        private NVEntities _db;

        [TestInitialize]
        public void Setup()
        {
            _db = A.Fake<NVEntities>();
            var ganavisiones = new List<Ganavision>
            {
                new Ganavision { Id = 1, Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, ProductoId = PRODUCTO_GANAVISION, Ganavisiones = 100 }
            }.AsQueryable();
            ConfigurarFakeDbSet(ganavisiones, db => A.CallTo(() => _db.Ganavisiones).Returns(db));
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precio, decimal descuento, int? oferta = null)
        {
            return new LineaPedidoVentaDTO
            {
                Producto = producto,
                Cantidad = (short)cantidad,
                PrecioUnitario = precio,
                DescuentoLinea = descuento,
                AplicarDescuento = true,
                oferta = oferta,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO
            };
        }

        [TestMethod]
        public async Task MarcarBonificados_LineaGanavisionReal_SeMarca()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea(PRODUCTO_GANAVISION, 1, 10m, 1m) };

            await GestorPedidosVenta.MarcarBonificadosGanavisionesAsync(_db, lineas);

            Assert.IsTrue(lineas[0].EsBonificadoGanavisiones, "Una línea a 0€ sin oferta de un producto de la tabla Ganavision es un bonificado real");
        }

        [TestMethod]
        public async Task MarcarBonificados_MuestraACero_NoSeMarca()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea(PRODUCTO_MMP, 1, 3m, 1m) };

            await GestorPedidosVenta.MarcarBonificadosGanavisionesAsync(_db, lineas);

            Assert.IsFalse(lineas[0].EsBonificadoGanavisiones, "Una muestra/MMP al 100 % no está en la tabla Ganavision y no debe marcarse");
        }

        [TestMethod]
        public async Task MarcarBonificados_LineaConPrecio_NoSeMarca()
        {
            // Aunque el producto esté en la tabla Ganavision, si la línea tiene importe no es un regalo.
            var lineas = new List<LineaPedidoVentaDTO> { Linea(PRODUCTO_GANAVISION, 1, 10m, 0m) };

            await GestorPedidosVenta.MarcarBonificadosGanavisionesAsync(_db, lineas);

            Assert.IsFalse(lineas[0].EsBonificadoGanavisiones, "Una línea con base imponible no es candidata a bonificado");
            A.CallTo(() => _db.Ganavisiones).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MarcarBonificados_LineaDeOfertaACero_NoSeMarca()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea(PRODUCTO_GANAVISION, 1, 10m, 1m, oferta: 123) };

            await GestorPedidosVenta.MarcarBonificadosGanavisionesAsync(_db, lineas);

            Assert.IsFalse(lineas[0].EsBonificadoGanavisiones, "Las líneas a 0€ que pertenecen a una oferta no son Ganavisiones");
        }

        [TestMethod]
        public async Task MarcarBonificados_VariasLineas_SoloSeMarcaLaGanavision()
        {
            var lineas = new List<LineaPedidoVentaDTO>
            {
                Linea(PRODUCTO_NORMAL, 5, 10m, 0m),
                Linea(PRODUCTO_GANAVISION, 1, 10m, 1m),
                Linea(PRODUCTO_MMP, 1, 3m, 1m)
            };

            await GestorPedidosVenta.MarcarBonificadosGanavisionesAsync(_db, lineas);

            Assert.IsFalse(lineas[0].EsBonificadoGanavisiones);
            Assert.IsTrue(lineas[1].EsBonificadoGanavisiones);
            Assert.IsFalse(lineas[2].EsBonificadoGanavisiones);
        }

        // Igual que en el resto de tests de controller (FakeItEasy + EF6 async), pero con datos.
        private static void ConfigurarFakeDbSet<T>(IQueryable<T> data, System.Action<DbSet<T>> onCreated) where T : class
        {
            var fakeSet = A.Fake<DbSet<T>>(o => o
                .Implements<IQueryable<T>>()
                .Implements<IDbAsyncEnumerable<T>>());

            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider)
                .Returns(new TestAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).Returns(data.GetEnumerator());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator())
                .Returns(new TestAsyncEnumerator<T>(data.GetEnumerator()));

            onCreated(fakeSet);
        }
    }
}
