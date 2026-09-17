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
            A.CallTo(() => productoService.CalcularStockProducto("MMP1", "ALG", A<int?>._))
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
        public async Task Validar_MMP_ExcluyeElPropioPedidoDelStock_PermiteSiHayStockLibre()
        {
            // NestoAPI#262: regresión del caso real (pedido 920796, muestra 45171: stock 2, 1 unidad
            // pendiente de OTRO pedido). Antes la propia línea del pedido se contaba contra sí misma
            // (PendienteEntregar incluía su reserva) y se denegaba aunque hubiera 1 unidad libre. Ahora se
            // excluye el pedido del cálculo de stock; con el pedido excluido hay disponibilidad suficiente
            // (2 - 1 = 1 >= 1) y se permite desmarcar.
            //
            // Es rojo sin el fix: si el código no pasara el nº de pedido, el fake configurado para
            // (..., 920796) no casaría, devolvería StockProducto por defecto (Stock 0) y denegaría.
            NVEntities db = A.Fake<NVEntities>();
            Producto productoMMP = new Producto
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Número = "MMP1",
                Nombre = "MUESTRA HYDRO MILK CLEANSER",
                SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS
            };
            A.CallTo(() => db.Productos).Returns(FakeDbSet(new List<Producto> { productoMMP }));

            IProductoService productoService = A.Fake<IProductoService>();
            A.CallTo(() => productoService.CalcularStockProducto("MMP1", "ALG", 920796))
                .Returns(Task.FromResult(new ProductoDTO.StockProducto { Almacen = "ALG", Stock = 2, PendienteEntregar = 1 }));

            ILogService logService = A.Fake<ILogService>();
            ServicioValidarServirJunto servicio = new ServicioValidarServirJunto(db, productoService, logService);

            ValidarServirJuntoRequest request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                Pedido = 920796,
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "MMP1", Cantidad = 1, EsBonificadoGanavisiones = false }
                }
            };

            ValidarServirJuntoResponse resultado = await servicio.Validar(request);

            Assert.IsTrue(resultado.PuedeDesmarcar, "Con stock libre suficiente (excluyendo el propio pedido) debe permitir desmarcar");
            A.CallTo(() => productoService.CalcularStockProducto("MMP1", "ALG", 920796)).MustHaveHappened();
            A.CallTo(() => logService.LogError(A<string>._, A<Exception>._)).MustNotHaveHappened();
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

        // ===== NestoAPI#394: el mensaje de denegación tiene que entenderse =====
        // Una comercial reportó como fallo lo que era la guarda funcionando, porque con DOS muestras
        // el mensaje interpolaba la lista dos veces y se leía como un solo nombre larguísimo.

        [TestMethod]
        public async Task Validar_DosMuestrasSinStock_ElMensajeNoRepiteLaListaYUsaElPlural()
        {
            ValidarServirJuntoResponse resultado = await ValidarConMuestrasSinStock(
                ("MMP1", "MUESTRA CREMA FACIAL"),
                ("MMP2", "MUESTRA CREMA REPARADORA SILK SENSITIVE"));

            Assert.IsFalse(resultado.PuedeDesmarcar);

            // Antes: "El producto {lista}, ... Borre primero el producto {lista} ..."
            Assert.AreEqual(1, ContarApariciones(resultado.Mensaje, "MUESTRA CREMA FACIAL"),
                $"La lista de productos debe aparecer UNA sola vez. Mensaje: '{resultado.Mensaje}'");
            Assert.IsFalse(resultado.Mensaje.Contains("El producto "),
                $"Con varias muestras el singular confunde. Mensaje: '{resultado.Mensaje}'");
            Assert.IsTrue(resultado.Mensaje.Contains("estas muestras"),
                $"Se espera el plural. Mensaje: '{resultado.Mensaje}'");
        }

        [TestMethod]
        public async Task Validar_MuestraSinStock_ElMensajeLlevaCodigoYNombreDelProducto()
        {
            ValidarServirJuntoResponse resultado = await ValidarConMuestrasSinStock(
                ("MMP1", "MUESTRA CREMA FACIAL"));

            Assert.IsFalse(resultado.PuedeDesmarcar);
            // Sin el código hay que buscar la línea a ojo entre las del pedido.
            Assert.IsTrue(resultado.Mensaje.Contains("MMP1 MUESTRA CREMA FACIAL"),
                $"Se espera 'codigo nombre'. Mensaje: '{resultado.Mensaje}'");
            Assert.IsTrue(resultado.Mensaje.Contains("esta muestra"),
                $"Con una sola muestra se espera el singular. Mensaje: '{resultado.Mensaje}'");
        }

        [TestMethod]
        public async Task Validar_CuandoSeDeniega_ElLogNoLlevaElPuntoDuplicado()
        {
            ILogService logService = A.Fake<ILogService>();
            await ValidarConMuestrasSinStock(logService, ("MMP1", "MUESTRA CREMA FACIAL"));

            // El mensaje ya acaba en punto: "Motivo: {mensaje}." dejaba un ".." en el log.
            A.CallTo(() => logService.LogError(A<string>.That.Contains(".."), A<Exception>._))
                .MustNotHaveHappened();
            A.CallTo(() => logService.LogError(A<string>.That.Contains("Productos problemáticos"), A<Exception>._))
                .MustHaveHappened();
        }

        // ===== NestoAPI#491: la validación solo aplica a «Según vaya entrando» =====
        // Pedido 926383 (17/09/26): tres denegaciones al intentar pasarlo a «Ahora lo que hay, el
        // resto de una vez» por un regalo sin stock. En los modos 3 y 4 el regalo no sale solo, así
        // que no hay nada que denegar. Rojos sin el fix: el servicio ejecutaba los validadores para
        // cualquier modo distinto de «Todo junto».

        [TestMethod]
        public async Task Validar_AhoraLoQueHayYElRestoDeUnaVez_RegaloSinStock_Permite()
        {
            ILogService logService = A.Fake<ILogService>();
            IProductoService productoService = A.Fake<IProductoService>();

            ValidarServirJuntoResponse resultado = await ValidarRegaloSinStock(
                Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, logService, productoService);

            Assert.IsTrue(resultado.PuedeDesmarcar, $"En modo 4 el regalo va con el resto. Mensaje: '{resultado.Mensaje}'");
            Assert.AreEqual(0, resultado.ProductosProblematicos.Count);
            A.CallTo(() => productoService.CalcularStockProducto(A<string>._, A<string>._, A<int?>._)).MustNotHaveHappened();
            A.CallTo(() => logService.LogError(A<string>._, A<Exception>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Validar_TrasReponerDeTiendas_MuestraSinStock_Permite()
        {
            ILogService logService = A.Fake<ILogService>();
            ValidarServirJuntoResponse resultado = await ValidarConMuestrasSinStock(
                logService, Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, ("MMP1", "MUESTRA CREMA FACIAL"));

            Assert.IsTrue(resultado.PuedeDesmarcar, $"En modo 3 se espera a la reposición. Mensaje: '{resultado.Mensaje}'");
            A.CallTo(() => logService.LogError(A<string>._, A<Exception>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Validar_SegunVayaEntrando_RegaloSinStock_SigueDenegandoYOfreceLosOtrosModos()
        {
            ILogService logService = A.Fake<ILogService>();
            ValidarServirJuntoResponse resultado = await ValidarRegaloSinStock(
                Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, logService, A.Fake<IProductoService>());

            Assert.IsFalse(resultado.PuedeDesmarcar);
            Assert.IsTrue(resultado.Mensaje.Contains("«Según vaya entrando»"), $"Mensaje: '{resultado.Mensaje}'");
            Assert.IsTrue(resultado.Mensaje.Contains("«Ahora lo que hay, el resto de una vez»"),
                $"Debe ofrecer los modos que sí se permiten. Mensaje: '{resultado.Mensaje}'");
            A.CallTo(() => logService.LogError(A<string>.That.Contains("REG1"), A<Exception>._)).MustHaveHappened();
        }

        [TestMethod]
        public async Task Validar_SinModoInformado_MuestraSinStock_SigueDenegando()
        {
            // NestoApp solo manda el bool: sin modo se asume «Según vaya entrando», como hasta ahora.
            ValidarServirJuntoResponse resultado = await ValidarConMuestrasSinStock(
                A.Fake<ILogService>(), null, ("MMP1", "MUESTRA CREMA FACIAL"));

            Assert.IsFalse(resultado.PuedeDesmarcar);
            Assert.IsTrue(resultado.Mensaje.Contains("«Según vaya entrando»"), $"Mensaje: '{resultado.Mensaje}'");
        }

        /// <summary>Regalo Ganavisiones (confirmado contra la tabla Ganavision) sin stock en ALG.</summary>
        private static async Task<ValidarServirJuntoResponse> ValidarRegaloSinStock(
            byte? modoServicio, ILogService logService, IProductoService productoService)
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.Productos).Returns(FakeDbSet(new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "REG1", Nombre = "ALTA FRECUENCIA PORTATIL", SubGrupo = "ACP" }
            }));
            A.CallTo(() => db.Ganavisiones).Returns(FakeDbSet(new List<Ganavision>
            {
                new Ganavision { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, ProductoId = "REG1", Ganavisiones = 100 }
            }));
            A.CallTo(() => productoService.CalcularStockProducto("REG1", A<string>._, A<int?>._))
                .Returns(Task.FromResult(new ProductoDTO.StockProducto { Almacen = "ALG", Stock = 0 }));

            ServicioValidarServirJunto servicio = new ServicioValidarServirJunto(db, productoService, logService);
            return await servicio.Validar(new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                Pedido = 926383,
                ModoServicio = modoServicio,
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "REG1", Cantidad = 1, EsBonificadoGanavisiones = true }
                }
            });
        }

        private static Task<ValidarServirJuntoResponse> ValidarConMuestrasSinStock(
            params (string Id, string Nombre)[] muestras) =>
            ValidarConMuestrasSinStock(A.Fake<ILogService>(), null, muestras);

        private static Task<ValidarServirJuntoResponse> ValidarConMuestrasSinStock(
            ILogService logService, params (string Id, string Nombre)[] muestras) =>
            ValidarConMuestrasSinStock(logService, null, muestras);

        private static async Task<ValidarServirJuntoResponse> ValidarConMuestrasSinStock(
            ILogService logService, byte? modoServicio, params (string Id, string Nombre)[] muestras)
        {
            NVEntities db = A.Fake<NVEntities>();
            List<Producto> productos = muestras.Select(m => new Producto
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Número = m.Id,
                Nombre = m.Nombre,
                SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS
            }).ToList();
            A.CallTo(() => db.Productos).Returns(FakeDbSet(productos));

            IProductoService productoService = A.Fake<IProductoService>();
            foreach ((string Id, string Nombre) muestra in muestras)
            {
                A.CallTo(() => productoService.CalcularStockProducto(muestra.Id, "ALG", A<int?>._))
                    .Returns(Task.FromResult(new ProductoDTO.StockProducto { Almacen = "ALG", Stock = 0 }));
            }

            ServicioValidarServirJunto servicio = new ServicioValidarServirJunto(db, productoService, logService);
            return await servicio.Validar(new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                ModoServicio = modoServicio,
                LineasPedido = muestras.Select(m => new ProductoBonificadoConCantidadRequest
                {
                    ProductoId = m.Id,
                    Cantidad = 1,
                    EsBonificadoGanavisiones = false
                }).ToList()
            });
        }

        private static int ContarApariciones(string texto, string busqueda)
        {
            int total = 0;
            for (int i = texto.IndexOf(busqueda, StringComparison.Ordinal); i >= 0;
                 i = texto.IndexOf(busqueda, i + busqueda.Length, StringComparison.Ordinal))
            {
                total++;
            }
            return total;
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
