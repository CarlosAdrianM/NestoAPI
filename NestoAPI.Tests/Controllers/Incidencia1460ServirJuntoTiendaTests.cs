using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.ServirJunto;
using NestoAPI.Infraestructure.ValidadoresServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.PedidosVenta.ServirJunto;
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
    /// Incidencia 1460 (Sancho, 25/11/26): en un pedido de tienda con «Todo junto» y muestras sin stock, la API corregía el modo a
    /// «Según vaya entrando» en silencio y luego lo rechazaba diciendo «elija otro modo de servicio («Todo junto»…)», que en tienda nunca
    /// se acepta: callejón sin salida (tres usuarios, 13 veces). Solo cambia el texto; las reglas no.
    /// </summary>
    [TestClass]
    public class Incidencia1460ServirJuntoTiendaTests
    {
        private static List<ProductoSinStockDTO> Muestras(params string[] ids)
            => ids.Select(id => new ProductoSinStockDTO { ProductoId = id, ProductoNombre = "MUESTRA " + id }).ToList();

        // ---- Material promocional ----

        [TestMethod]
        public void MaterialPromocional_EnTienda_NoMandaAElegirOtroModo_SinoAAlgete()
        {
            string mensaje = ValidadorMaterialPromocional.ConstruirMensaje(Muestras("39918", "40001"), M.SEGUN_VAYA_ENTRANDO, "ALC");

            Assert.IsFalse(mensaje.Contains("elija otro modo"), mensaje);
            Assert.IsFalse(mensaje.Contains("«Todo junto»"), mensaje);
            Assert.AreEqual(
                "No se puede pasar el pedido a «Según vaya entrando»: no hay stock en ALC de estas muestras (material promocional) y no pueden " +
                "quedarse pendientes. Bórrelas del pedido: 39918 MUESTRA 39918, 40001 MUESTRA 40001. En Alcobendas el pedido solo puede salir " +
                "«Según vaya entrando»: bórrelas del pedido o páselo desde Algete (ALG).", mensaje);
        }

        [TestMethod]
        public void MaterialPromocional_EnReina_UnaMuestra_Singular()
        {
            string mensaje = ValidadorMaterialPromocional.ConstruirMensaje(Muestras("39918"), M.SEGUN_VAYA_ENTRANDO, "REI ");

            StringAssert.EndsWith(mensaje, "En Reina el pedido solo puede salir «Según vaya entrando»: bórrela del pedido o páselo desde Algete (ALG).");
            Assert.IsFalse(mensaje.Contains("elija otro modo"), mensaje);
        }

        [TestMethod]
        public void MaterialPromocional_EnAlgete_ElTextoDeSiempre()
        {
            string mensaje = ValidadorMaterialPromocional.ConstruirMensaje(Muestras("39918"), M.SEGUN_VAYA_ENTRANDO, "ALG");

            StringAssert.EndsWith(mensaje, $" O elija otro modo de servicio ({M.AlternativasSinValidacion()}).");
            Assert.IsFalse(mensaje.Contains("páselo desde"), mensaje);
            // Con el flag de corrección (no puede darse en Algete), igual: solo cuenta en tienda.
            Assert.AreEqual(mensaje, ValidadorMaterialPromocional.ConstruirMensaje(Muestras("39918"), M.SEGUN_VAYA_ENTRANDO, "ALG", true));
        }

        [TestMethod]
        public void MaterialPromocional_ModoCorregidoPorTienda_DiceQueLoHaCambiadoLaApi()
        {
            string mensaje = ValidadorMaterialPromocional.ConstruirMensaje(Muestras("39918", "40001"), M.SEGUN_VAYA_ENTRANDO, "ALC", modoCorregidoPorTienda: true);

            Assert.AreEqual(
                "El pedido es de Alcobendas y allí solo puede salir «Según vaya entrando». No hay stock en Alcobendas de estas muestras " +
                "(material promocional) y no pueden quedarse pendientes: bórrelas del pedido (39918 MUESTRA 39918, 40001 MUESTRA 40001) " +
                "o páselo desde Algete (ALG).", mensaje);
        }

        [TestMethod]
        public void MaterialPromocional_ModoCorregidoPorTienda_UnaMuestra_Singular()
        {
            string mensaje = ValidadorMaterialPromocional.ConstruirMensaje(Muestras("39918"), M.SEGUN_VAYA_ENTRANDO, "REI", modoCorregidoPorTienda: true);

            Assert.AreEqual(
                "El pedido es de Reina y allí solo puede salir «Según vaya entrando». No hay stock en Reina de esta muestra " +
                "(material promocional) y no puede quedarse pendiente: bórrela del pedido (39918 MUESTRA 39918) o páselo desde Algete (ALG).", mensaje);
        }

        // ---- Bonificados (mismo patrón) ----

        [TestMethod]
        public void Regalos_EnTienda_NoMandaAElegirOtroModo()
        {
            var regalos = new List<ProductoSinStockDTO> { new ProductoSinStockDTO { ProductoId = "REG1", ProductoNombre = "ALTA FRECUENCIA", AlmacenConStock = "ALG" } };

            string mensaje = ValidadorDisponibilidadRegalos.ConstruirMensaje(regalos, M.SEGUN_VAYA_ENTRANDO, "ALC");

            Assert.IsFalse(mensaje.Contains("elija otro modo"), mensaje);
            Assert.AreEqual(
                "No se puede pasar el pedido a «Según vaya entrando» porque los siguientes productos bonificados no tienen stock suficiente en ALC: " +
                "ALTA FRECUENCIA (stock en ALG). En Alcobendas el pedido solo puede salir «Según vaya entrando»: cambie los productos bonificados " +
                "por otros con stock en ALC o pase el pedido desde Algete (ALG).", mensaje);
        }

        [TestMethod]
        public void Regalos_ModoCorregidoPorTienda_DiceQueLoHaCambiadoLaApi()
        {
            var regalos = new List<ProductoSinStockDTO> { new ProductoSinStockDTO { ProductoId = "REG1", ProductoNombre = "ALTA FRECUENCIA" } };

            string mensaje = ValidadorDisponibilidadRegalos.ConstruirMensaje(regalos, M.SEGUN_VAYA_ENTRANDO, "ALC", modoCorregidoPorTienda: true);

            Assert.AreEqual(
                "El pedido es de Alcobendas y allí solo puede salir «Según vaya entrando». Estos productos bonificados no tienen stock suficiente " +
                "en Alcobendas: ALTA FRECUENCIA. Cámbielos por otros con stock en Alcobendas o pase el pedido desde Algete (ALG).", mensaje);
        }

        [TestMethod]
        public void Regalos_EnAlgete_ElTextoDeSiempre()
        {
            var regalos = new List<ProductoSinStockDTO> { new ProductoSinStockDTO { ProductoId = "REG1", ProductoNombre = "ALTA FRECUENCIA", AlmacenConStock = "REI" } };

            string mensaje = ValidadorDisponibilidadRegalos.ConstruirMensaje(regalos, M.SEGUN_VAYA_ENTRANDO, "ALG");

            Assert.AreEqual(
                "No se puede pasar el pedido a «Según vaya entrando» porque los siguientes productos bonificados no tienen stock suficiente en ALG: " +
                $"ALTA FRECUENCIA (stock en REI). Cambie los productos bonificados por otros con stock en ALG o elija otro modo de servicio ({M.AlternativasSinValidacion()}).",
                mensaje);
        }

        [TestMethod]
        public void ClasificarAlmacen_LaMismaReglaQueLasLineas()
        {
            Assert.AreEqual(ModosServicioPermitidos.TipoAlmacen.Tienda, ModosServicioPermitidos.ClasificarAlmacen("ALC"));
            Assert.AreEqual(ModosServicioPermitidos.TipoAlmacen.Tienda, ModosServicioPermitidos.ClasificarAlmacen("rei "));
            Assert.AreEqual(ModosServicioPermitidos.TipoAlmacen.Central, ModosServicioPermitidos.ClasificarAlmacen("ALG"));
            Assert.AreEqual(ModosServicioPermitidos.TipoAlmacen.Otro, ModosServicioPermitidos.ClasificarAlmacen("AMZ"));
            Assert.AreEqual(ModosServicioPermitidos.TipoAlmacen.Otro, ModosServicioPermitidos.ClasificarAlmacen(null));
        }

        // ---- El camino del POST: corrección del modo + validación de «Servir junto» ----

        [TestMethod]
        public async Task Post_PedidoDeTiendaConTodoJuntoYMuestraSinStock_ElMensajeDiceQueElModoLoHaCambiadoLaApi()
        {
            // Los mismos pasos que CrearPedidoVenta (ComprobarModoServicioAlCrear → NormalizarAlCrear → ValidarServirJuntoDesdePedidoAsync)
            // con el servicio de validación de verdad sobre la BD falsa.
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.Productos).Returns(FakeDbSet(new List<Producto>
            {
                new Producto { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = "39918", Nombre = "MUESTRA SERUM", SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS }
            }));
            A.CallTo(() => db.Ganavisiones).Returns(FakeDbSet(new List<Ganavision>()));
            IProductoService productoService = A.Fake<IProductoService>();
            A.CallTo(() => productoService.CalcularStockProducto("39918", "ALC", A<int?>._))
                .Returns(Task.FromResult(new ProductoDTO.StockProducto { Almacen = "ALC", Stock = 0 }));
            var servicio = new ServicioValidarServirJunto(db, productoService, A.Fake<ILogService>());

            IGestorStocks stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._, A<int>._)).Returns(SugeridorModoServicio.ROJO);
            var controller = new PedidosVentaController(db, servicio) { Stocks = () => stocks };

            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 0,
                cliente = "15191",
                contacto = "0",
                modoServicio = M.TODO_JUNTO,
                servirJunto = true,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        id = 1, Producto = "39918", Cantidad = 1, PrecioUnitario = 10,
                        almacen = "ALC", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, estado = (short)Constantes.EstadosLineaVenta.PENDIENTE
                    }
                }
            };

            controller.ComprobarModoServicioAlCrear(pedido);
            Assert.IsNull(M.NormalizarAlCrear(pedido, M.SEGUN_VAYA_ENTRANDO));
            ValidarServirJuntoResponse falla = await controller.ValidarServirJuntoDesdePedidoAsync(pedido);

            Assert.AreEqual((byte?)M.SEGUN_VAYA_ENTRANDO, pedido.modoServicio, "La regla no cambia: en tienda se corrige al 2");
            Assert.IsFalse(pedido.servirJunto);
            Assert.IsTrue(pedido.ModoCorregidoPorTienda);
            Assert.IsNotNull(falla, "La regla no cambia: la muestra sin stock frena el 2");
            Assert.AreEqual("39918", falla.ProductosProblematicos.Single().ProductoId);
            Assert.AreEqual(
                "El pedido es de Alcobendas y allí solo puede salir «Según vaya entrando». No hay stock en Alcobendas de esta muestra " +
                "(material promocional) y no puede quedarse pendiente: bórrela del pedido (39918 MUESTRA SERUM) o páselo desde Algete (ALG).",
                falla.Mensaje);
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
