using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.ServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Tests del endpoint canónico de "Servir junto" (NestoAPI#161).
    /// Se instancia el servicio real (ServicioValidarServirJunto) con un NVEntities
    /// y IProductoService mockeados, para cubrir el flujo end-to-end del controller
    /// más el pipeline de validadores.
    /// </summary>
    [TestClass]
    public class ServirJuntoControllerTests
    {
        private NVEntities db;
        private IProductoService fakeProductoService;
        private DbSet<Producto> fakeProductos;
        private DbSet<Ganavision> fakeGanavisiones;
        private ServirJuntoController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeProductoService = A.Fake<IProductoService>();
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            fakeGanavisiones = A.Fake<DbSet<Ganavision>>(o => o.Implements<IQueryable<Ganavision>>().Implements<IDbAsyncEnumerable<Ganavision>>());

            A.CallTo(() => db.Productos).Returns(fakeProductos);
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>().AsQueryable());

            A.CallTo(() => db.Ganavisiones).Returns(fakeGanavisiones);
            ConfigurarFakeDbSet(fakeGanavisiones, new List<Ganavision>().AsQueryable());

            A.CallTo(() => fakeProductoService.CalcularStockProducto(A<string>._, A<string>._))
                .Returns(Task.FromResult(new ProductoDTO.StockProducto()));

            var servicio = new ServicioValidarServirJunto(db, fakeProductoService);
            controller = new ServirJuntoController(servicio);
        }

        private void ConfigurarGanavisiones(params string[] productosIds)
        {
            var lista = productosIds
                .Select(id => new Ganavision
                {
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    ProductoId = id,
                    Ganavisiones = 1
                })
                .AsQueryable();
            ConfigurarFakeDbSet(fakeGanavisiones, lista);
        }

        [TestMethod]
        public async Task ValidarServirJunto_RequestNulo_RetornaBadRequest()
        {
            var resultado = await controller.ValidarServirJunto(null);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task ValidarServirJunto_SinAlmacen_RetornaBadRequest()
        {
            var request = new ValidarServirJuntoRequest { Almacen = null };
            var resultado = await controller.ValidarServirJunto(request);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task ValidarServirJunto_SinBonificadosNiLineas_PuedeDesmarcar()
        {
            var request = new ValidarServirJuntoRequest { Almacen = "ALG" };
            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
        }

        [TestMethod]
        public async Task ValidarServirJunto_LineaMMPSinStock_BloqueaConMensajeEspecifico()
        {
            // Issue #161: línea del pedido con subgrupo MMP sin stock en el almacén
            // → bloquea con mensaje "borre primero el producto".
            MockStock("MMP1", "ALG", 0);

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "MMP1", Nombre = "Muestra Crema", SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "MMP1", Cantidad = 1 }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsFalse(okResult.Content.PuedeDesmarcar);
            Assert.AreEqual("MMP1", okResult.Content.ProductosProblematicos[0].ProductoId.Trim());
            Assert.IsTrue(okResult.Content.Mensaje.Contains("material promocional"));
            Assert.IsTrue(okResult.Content.Mensaje.Contains("Borre primero"));
            Assert.IsTrue(okResult.Content.Mensaje.Contains("Muestra Crema"));
        }

        [TestMethod]
        public async Task ValidarServirJunto_LineaMMPConStock_PuedeDesmarcar()
        {
            MockStock("MMP1", "ALG", 10);

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "MMP1", Nombre = "Muestra Crema", SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "MMP1", Cantidad = 3 }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
        }

        [TestMethod]
        public async Task ValidarServirJunto_LineaNoMMP_NoLaValidaElValidadorMMP()
        {
            // Una línea con subgrupo distinto de MMP y sin stock no debe activar el
            // validador MMP (delega al resto del pipeline, que en este caso deja pasar
            // porque no hay bonificados).
            MockStock("PROD1", "ALG", 0);

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "PROD1", Nombre = "Normal", SubGrupo = "OTR" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "PROD1", Cantidad = 1 }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
        }

        [TestMethod]
        public async Task ValidarServirJunto_LineaMMPSinStockYBonificadoSinStock_PriorizaMensajeMMP()
        {
            MockStock("MMP1", "REI", 10);   // MMP sin stock en ALG (en otro almacén no sirve)
            MockStock("PROD2", "REI", 10);  // Bonificado normal, sin stock en ALG

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "MMP1", Nombre = "Muestra Crema", SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS },
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "PROD2", Nombre = "Regalo Normal", SubGrupo = "OTR" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "MMP1", Cantidad = 1 }
                },
                ProductosBonificadosConCantidad = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "PROD2", Cantidad = 1 }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsFalse(okResult.Content.PuedeDesmarcar);
            Assert.IsTrue(okResult.Content.Mensaje.Contains("material promocional"));
        }

        [TestMethod]
        public async Task ValidarServirJunto_LineaMMPConStockYBonificadoSinStock_DelegaAValidadorRegalos()
        {
            MockStock("MMP1", "ALG", 10);
            MockStock("PROD2", "REI", 10);

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "MMP1", Nombre = "Muestra Crema", SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS },
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "PROD2", Nombre = "Regalo Normal", SubGrupo = "OTR" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "MMP1", Cantidad = 3 }
                },
                ProductosBonificadosConCantidad = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "PROD2", Cantidad = 1 }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsFalse(okResult.Content.PuedeDesmarcar);
            Assert.IsFalse(okResult.Content.Mensaje.Contains("material promocional"),
                "El MMP tiene stock → debe usar el mensaje genérico de regalos");
            Assert.AreEqual("PROD2", okResult.Content.ProductosProblematicos[0].ProductoId.Trim());
        }

        [TestMethod]
        public async Task ValidarServirJunto_FormatoAntiguoSinLineas_SigueFuncionando()
        {
            // Retrocompatibilidad: un cliente antiguo que solo manda bonificados en
            // formato string[] debe funcionar igual que antes del refactor.
            MockStock("PROD1", "ALG", 10);

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "PROD1", Nombre = "Regalo Normal", SubGrupo = "OTR" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                ProductosBonificados = new List<string> { "PROD1" }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
        }

        // NestoAPI#175: bonificado de Ganavisiones enviado como línea del pedido
        // (escenario DetallePedido: ProductosBonificadosConCantidad viene vacío y
        // los bonificados llegan marcados dentro de LineasPedido).

        [TestMethod]
        public async Task ValidarServirJunto_LineaBonificadoGanavisionesSinStock_Bloquea()
        {
            MockStock("BONIF1", "ALG", 0);
            ConfigurarGanavisiones("BONIF1");

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "BONIF1", Nombre = "Regalo Ganavisiones", SubGrupo = "OTR" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "BONIF1", Cantidad = 2, EsBonificadoGanavisiones = true }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsFalse(okResult.Content.PuedeDesmarcar);
            Assert.AreEqual("BONIF1", okResult.Content.ProductosProblematicos[0].ProductoId.Trim());
            Assert.AreEqual("Regalo Ganavisiones", okResult.Content.ProductosProblematicos[0].ProductoNombre);
        }

        [TestMethod]
        public async Task ValidarServirJunto_LineaBonificadoGanavisionesConStock_PuedeDesmarcar()
        {
            MockStock("BONIF1", "ALG", 10);
            ConfigurarGanavisiones("BONIF1");

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "BONIF1", Nombre = "Regalo Ganavisiones", SubGrupo = "OTR" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "BONIF1", Cantidad = 2, EsBonificadoGanavisiones = true }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
        }

        [TestMethod]
        public async Task ValidarServirJunto_LineaSinFlagEsBonificado_NoSeValidaComoBonificado()
        {
            // Retrocompatibilidad: las líneas no marcadas siguen sin entrar al validador
            // de regalos aunque no tengan stock (sólo el validador MMP las mira, y ese
            // filtra por subgrupo MUESTRAS).
            MockStock("PROD1", "ALG", 0);

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "PROD1", Nombre = "Producto normal", SubGrupo = "OTR" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "PROD1", Cantidad = 1 }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
        }

        [TestMethod]
        public async Task ValidarServirJunto_MismoProductoEnBonificadosYLineas_NoDuplicaValidacion()
        {
            // El mismo producto puede llegar a la vez como bonificado explícito y como
            // línea del pedido marcada. El validador debe tratarlo como uno solo para no
            // repetirlo en el listado de productos problemáticos.
            MockStock("BONIF1", "ALG", 0);
            ConfigurarGanavisiones("BONIF1");

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "BONIF1", Nombre = "Regalo Ganavisiones", SubGrupo = "OTR" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                ProductosBonificadosConCantidad = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "BONIF1", Cantidad = 1 }
                },
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "BONIF1", Cantidad = 1, EsBonificadoGanavisiones = true }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsFalse(okResult.Content.PuedeDesmarcar);
            Assert.AreEqual(1, okResult.Content.ProductosProblematicos.Count);
        }

        [TestMethod]
        public async Task ValidarServirJunto_LineaMarcadaSinGanavisionesEnBd_SeDescartaDelValidador()
        {
            // Un cliente puede marcar como EsBonificadoGanavisiones cualquier línea a 0€.
            // Si el producto no tiene registro en la tabla Ganavision, el servicio lo
            // desmarca y el validador de regalos no lo ve → no falsea ProductosProblematicos.
            MockStock("NO_BONIF", "ALG", 0);
            // Ganavisiones fake queda vacío: NO_BONIF no tiene registro.

            var productos = new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "NO_BONIF", Nombre = "Regalo por importe", SubGrupo = "OTR" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeProductos, productos);

            var request = new ValidarServirJuntoRequest
            {
                Almacen = "ALG",
                LineasPedido = new List<ProductoBonificadoConCantidadRequest>
                {
                    new ProductoBonificadoConCantidadRequest { ProductoId = "NO_BONIF", Cantidad = 1, EsBonificadoGanavisiones = true }
                }
            };

            var resultado = await controller.ValidarServirJunto(request);

            var okResult = (OkNegotiatedContentResult<ValidarServirJuntoResponse>)resultado;
            Assert.IsTrue(okResult.Content.PuedeDesmarcar);
        }

        #region Helpers (copiados de GanavisionesControllerTests)

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private void MockStock(string productoId, string almacen, int stock, int pendienteEntregar = 0)
        {
            A.CallTo(() => fakeProductoService.CalcularStockProducto(productoId, almacen))
                .Returns(Task.FromResult(new ProductoDTO.StockProducto
                {
                    Almacen = almacen,
                    Stock = stock,
                    PendienteEntregar = pendienteEntregar
                }));
        }

        #endregion
    }
}
