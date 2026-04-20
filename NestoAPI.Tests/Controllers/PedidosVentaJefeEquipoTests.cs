using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#163: en la ficha del cliente, un jefe de equipo debe ver los pedidos
    /// de todos los vendedores de su equipo, no solo los que él ha hecho.
    /// El endpoint <c>GET api/PedidosVenta?vendedor=ASH&amp;cliente=12345</c> debe
    /// resolver el equipo con <c>IServicioVendedores.VendedoresEquipoString</c>.
    /// </summary>
    [TestClass]
    public class PedidosVentaJefeEquipoTests
    {
        private NVEntities _db;
        private IServicioVendedores _servicioVendedores;
        private PedidosVentaController _controller;

        [TestInitialize]
        public void Setup()
        {
            _db = A.Fake<NVEntities>();
            _servicioVendedores = A.Fake<IServicioVendedores>();

            ConfigurarFakeDbSet<CabPedidoVta>(db => A.CallTo(() => _db.CabPedidoVtas).Returns(db));
            ConfigurarFakeDbSet<LinPedidoVta>(db => A.CallTo(() => _db.LinPedidoVtas).Returns(db));
            ConfigurarFakeDbSet<VendedorPedidoGrupoProducto>(db => A.CallTo(() => _db.VendedoresPedidosGruposProductos).Returns(db));
            ConfigurarFakeDbSet<EnviosAgencia>(db => A.CallTo(() => _db.EnviosAgencias).Returns(db));

            _controller = new PedidosVentaController(_db, _servicioVendedores);
        }

        [TestMethod]
        public async Task GetPedidosVenta_JefeEquipoConCliente_ExpandeEquipoPorVendedoresEquipoString()
        {
            // Arrange: jefe ASH con equipo {ASH, VEN1, VEN2}
            A.CallTo(() => _servicioVendedores.VendedoresEquipoString(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, "ASH"))
                .Returns(Task.FromResult(new List<string> { "ASH", "VEN1", "VEN2" }));

            // Act
            await _controller.GetPedidosVenta("ASH", "12345");

            // Assert: debe haber resuelto el equipo (no solo filtrar por "ASH")
            A.CallTo(() => _servicioVendedores.VendedoresEquipoString(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, "ASH"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetPedidosVenta_SinVendedor_NoLlamaVendedoresEquipo()
        {
            await _controller.GetPedidosVenta("", "12345");

            A.CallTo(() => _servicioVendedores.VendedoresEquipoString(
                    A<string>.Ignored, A<string>.Ignored))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetPedidosVenta_VendedorNull_NoLlamaVendedoresEquipo()
        {
            await _controller.GetPedidosVenta(null, "12345");

            A.CallTo(() => _servicioVendedores.VendedoresEquipoString(
                    A<string>.Ignored, A<string>.Ignored))
                .MustNotHaveHappened();
        }

        private static void ConfigurarFakeDbSet<T>(System.Action<DbSet<T>> onCreated) where T : class
        {
            var data = new List<T>().AsQueryable();
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

    /// <summary>Helpers mínimos de EF6 async queries para fakes vacíos.</summary>
    internal class TestAsyncQueryProvider<TEntity> : IDbAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;
        public TestAsyncQueryProvider(IQueryProvider inner) { _inner = inner; }
        public IQueryable CreateQuery(System.Linq.Expressions.Expression expression)
            => new TestAsyncEnumerable<TEntity>(expression);
        public IQueryable<TElement> CreateQuery<TElement>(System.Linq.Expressions.Expression expression)
            => new TestAsyncEnumerable<TElement>(expression);
        public object Execute(System.Linq.Expressions.Expression expression) => _inner.Execute(expression);
        public TResult Execute<TResult>(System.Linq.Expressions.Expression expression) => _inner.Execute<TResult>(expression);
        public Task<object> ExecuteAsync(System.Linq.Expressions.Expression expression, System.Threading.CancellationToken cancellationToken)
            => Task.FromResult(Execute(expression));
        public Task<TResult> ExecuteAsync<TResult>(System.Linq.Expressions.Expression expression, System.Threading.CancellationToken cancellationToken)
            => Task.FromResult(Execute<TResult>(expression));
    }

    internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IDbAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(System.Linq.Expressions.Expression expression) : base(expression) { }
        public TestAsyncEnumerable(System.Collections.Generic.IEnumerable<T> enumerable) : base(enumerable) { }
        public IDbAsyncEnumerator<T> GetAsyncEnumerator() => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
        IDbAsyncEnumerator IDbAsyncEnumerable.GetAsyncEnumerator() => GetAsyncEnumerator();
        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

    internal class TestAsyncEnumerator<T> : IDbAsyncEnumerator<T>
    {
        private readonly System.Collections.Generic.IEnumerator<T> _inner;
        public TestAsyncEnumerator(System.Collections.Generic.IEnumerator<T> inner) { _inner = inner; }
        public void Dispose() => _inner.Dispose();
        public Task<bool> MoveNextAsync(System.Threading.CancellationToken cancellationToken) => Task.FromResult(_inner.MoveNext());
        public T Current => _inner.Current;
        object IDbAsyncEnumerator.Current => Current;
    }
}
