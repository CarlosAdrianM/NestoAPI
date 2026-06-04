using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.ServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.ServirJunto
{
    [TestClass]
    public class ServicioValidarServirJuntoTests
    {
        [TestMethod]
        public async Task Validar_CuandoSeDeniegaPorMaterialPromocional_LoLogueaEnElmah()
        {
            // NestoAPI#220: cuando se deniega desmarcar "servir junto", el motivo debe quedar registrado
            // en ELMAH (ILogService) para poder diagnosticar las quejas (antes no quedaba en ningún sitio).
            NVEntities db = A.Fake<NVEntities>();
            Producto productoMMP = new Producto
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Número = "MMP1",
                Nombre = "MUESTRA NEO-TECH",
                SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS
            };
            A.CallTo(() => db.Productos).Returns(FakeDbSet(new List<Producto> { productoMMP }));

            IProductoService productoService = A.Fake<IProductoService>();
            // Stock 0 => CantidadDisponible 0 < 1 pedido => el MMP se quedaría pendiente => deniega.
            A.CallTo(() => productoService.CalcularStockProducto("MMP1", "ALG"))
                .Returns(Task.FromResult(new ProductoDTO.StockProducto { Almacen = "ALG", Stock = 0 }));

            ILogService logService = A.Fake<ILogService>();
            ServicioValidarServirJunto servicio = new ServicioValidarServirJunto(db, productoService, logService);

            ValidarServirJuntoRequest request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "MMP1", Cantidad = 1, EsBonificadoGanavisiones = false }
                }
            };

            ValidarServirJuntoResponse resultado = await servicio.Validar(request);

            Assert.IsFalse(resultado.PuedeDesmarcar, "El MMP sin stock debe impedir desmarcar servir junto");
            A.CallTo(() => logService.LogError(A<string>.That.Contains("MMP1"), A<Exception>._))
                .MustHaveHappened();
        }

        [TestMethod]
        public async Task Validar_CuandoSePuedeDesmarcar_NoLogueaNada()
        {
            // Si no hay nada que validar, no se ensucia ELMAH.
            NVEntities db = A.Fake<NVEntities>();
            IProductoService productoService = A.Fake<IProductoService>();
            ILogService logService = A.Fake<ILogService>();
            ServicioValidarServirJunto servicio = new ServicioValidarServirJunto(db, productoService, logService);

            ValidarServirJuntoResponse resultado = await servicio.Validar(new ValidarServirJuntoRequest { Almacen = "ALG" });

            Assert.IsTrue(resultado.PuedeDesmarcar);
            A.CallTo(() => logService.LogError(A<string>._, A<Exception>._)).MustNotHaveHappened();
        }

        private static DbSet<T> FakeDbSet<T>(List<T> data) where T : class
        {
            IQueryable<T> queryable = data.AsQueryable();
            DbSet<T> fakeDbSet = A.Fake<DbSet<T>>(o => o
                .Implements<IQueryable<T>>()
                .Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(queryable.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(queryable.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(queryable.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(queryable.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(queryable.GetEnumerator());
            return fakeDbSet;
        }
    }
}
