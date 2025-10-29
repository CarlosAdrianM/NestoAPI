using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorPresupuestosTests
    {
        /// <summary>
        /// Test que verifica que no se cuentan dos veces las unidades pendientes del propio pedido.
        /// Escenario: Pedido de 3 unidades del producto X para ALG
        /// - Stock en ALG: 0
        /// - Stock en REI: 15
        /// - Pendientes totales en ALG: 3 (del propio pedido, después del SaveChanges)
        ///
        /// Resultado esperado: "Faltan 3 uds. Stock disponible: REI: 15 uds"
        /// (No debería decir que faltan más de 3, porque las 3 pendientes son del propio pedido)
        /// </summary>
        [TestMethod]
        public void CalcularReservasAlmacenes_NoDeberiaContarDobleVezPendientesPropioPedido()
        {
            // Arrange
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 123456,
                cliente = "TEST01",
                contacto = "0",
                fecha = DateTime.Now,
                vendedor = "V01",
                EsPresupuesto = false
            };

            var linea = new LineaPedidoVentaDTO
            {
                id = 1, // No es línea nueva
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "PROD123",
                Cantidad = 3,
                almacen = Constantes.Almacenes.ALGETE,
                CantidadAnterior = null // Es nueva en el contexto de modificación
            };

            pedido.Lineas = new List<LineaPedidoVentaDTO> { linea };

            // Mock del servicio de stocks
            var servicioGestorStocks = A.Fake<IServicioGestorStocks>();

            // Stock en ALG: 0 físico, 3 pendientes (del propio pedido)
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD123", Constantes.Almacenes.ALGETE))
                .Returns(3); // Los 3 pendientes son del propio pedido

            // Stock en REI: 15 físico, 0 pendientes
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD123", Constantes.Almacenes.REINA))
                .Returns(0);

            // Stock físico en cada almacén
            A.CallTo(() => servicioGestorStocks.Stock("PROD123", Constantes.Almacenes.ALGETE)).Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("PROD123", Constantes.Almacenes.REINA)).Returns(15);
            A.CallTo(() => servicioGestorStocks.Stock("PROD123", Constantes.Almacenes.ALCOBENDAS)).Returns(0);

            // Crear GestorStocks real (no mock) pasándole el servicio mockeado
            var gestorStocks = new GestorStocks(servicioGestorStocks);

            var db = A.Fake<NVEntities>();
            var servicioVendedores = A.Fake<IServicioVendedores>();
            var servicioCorreo = A.Fake<IServicioCorreoElectronico>();

            var gestor = new GestorPresupuestos(
                pedido,
                null, // respuestaValidacion
                db,
                servicioVendedores,
                servicioGestorStocks,
                servicioCorreo
            );

            var almacenesConReservas = new Dictionary<string, HashSet<string>>();

            // Act
            string resultado = gestor.CalcularReservasAlmacenes(linea, gestorStocks, servicioGestorStocks, almacenesConReservas);

            // Assert
            // Cuando solo hay UN almacén con stock, el formato es: "<strong>REI: X uds</strong>"
            // donde X es Math.Min(cantidadFaltante, disponibleAlmacen)
            // En este caso: Math.Min(3 faltantes, 15 disponibles) = 3 uds
            Assert.IsTrue(resultado.Contains("REI: 3 uds"),
                $"El resultado debería mostrar 'REI: 3 uds' (cantidad correcta a reservar). Resultado actual: {resultado}");

            // Verificar que NO hay doble contabilidad
            // Si contara doble, diría 6 uds o más
            Assert.IsFalse(resultado.Contains("6 uds") || resultado.Contains("9 uds") || resultado.Contains("12 uds"),
                $"No debería haber doble contabilidad de pendientes. Resultado actual: {resultado}");

            // Verificar que el resultado contiene el formato HTML esperado
            Assert.IsTrue(resultado.Contains("<strong>REI:"),
                $"Debería tener el formato HTML correcto. Resultado actual: {resultado}");
        }

        /// <summary>
        /// Test que verifica que NO se muestra "Antes: X uds" cuando el producto anterior NO necesitaba reservas.
        /// Escenario: Modificación de pedido
        /// - Antes: Producto 17404, cantidad 1, stock ALG suficiente (NO necesitaba reservas)
        /// - Ahora: Producto 39547, cantidad 45, stock ALG insuficiente (SÍ necesita reservas)
        ///
        /// Resultado esperado: NO debería mostrar "Antes: 1 uds → Ahora: 45 uds"
        /// porque la cantidad anterior NO estaba reservada (había stock suficiente)
        /// </summary>
        [TestMethod]
        public void CalcularReservasAlmacenes_NoDeberaMostrarAntesEnCambioDeProductoSinReservasAProdcutoConReservas()
        {
            // Arrange
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 123456,
                cliente = "TEST01",
                contacto = "0",
                fecha = DateTime.Now,
                vendedor = "V01",
                EsPresupuesto = false
            };

            var linea = new LineaPedidoVentaDTO
            {
                id = 1, // Línea existente (modificada)
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "39547", // Producto NUEVO
                ProductoAnterior = "17404", // Producto ANTERIOR (al estar definido, CambioProducto será true)
                Cantidad = 45, // Cantidad NUEVA
                CantidadAnterior = 1, // Cantidad ANTERIOR
                almacen = Constantes.Almacenes.ALGETE
            };

            pedido.Lineas = new List<LineaPedidoVentaDTO> { linea };

            // Mock del servicio de stocks
            var servicioGestorStocks = A.Fake<IServicioGestorStocks>();

            // PRODUCTO ANTERIOR (17404) - Tenía stock SUFICIENTE en ALG (NO necesitaba reservas)
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("17404", Constantes.Almacenes.ALGETE))
                .Returns(0); // No había pendientes
            A.CallTo(() => servicioGestorStocks.Stock("17404", Constantes.Almacenes.ALGETE))
                .Returns(50); // Stock abundante

            // PRODUCTO NUEVO (39547) - Stock INSUFICIENTE en ALG (SÍ necesita reservas)
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("39547", Constantes.Almacenes.ALGETE))
                .Returns(45); // Los 45 pendientes son del propio pedido
            A.CallTo(() => servicioGestorStocks.Stock("39547", Constantes.Almacenes.ALGETE))
                .Returns(40); // Solo hay 40 disponibles (faltan 5)

            // Stock del producto nuevo en otros almacenes
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("39547", Constantes.Almacenes.REINA))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("39547", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("39547", Constantes.Almacenes.REINA))
                .Returns(1);
            A.CallTo(() => servicioGestorStocks.Stock("39547", Constantes.Almacenes.ALCOBENDAS))
                .Returns(6);

            // Crear GestorStocks real
            var gestorStocks = new GestorStocks(servicioGestorStocks);

            var db = A.Fake<NVEntities>();
            var servicioVendedores = A.Fake<IServicioVendedores>();
            var servicioCorreo = A.Fake<IServicioCorreoElectronico>();

            var gestor = new GestorPresupuestos(
                pedido,
                null,
                db,
                servicioVendedores,
                servicioGestorStocks,
                servicioCorreo
            );

            var almacenesConReservas = new Dictionary<string, HashSet<string>>();

            // Act
            string resultado = gestor.CalcularReservasAlmacenes(linea, gestorStocks, servicioGestorStocks, almacenesConReservas);

            // Assert
            // El bug está en que muestra "Antes: 1 uds → Ahora: 45 uds" cuando NO debería
            // porque el producto anterior NO necesitaba reservas (había stock suficiente)
            Assert.IsFalse(resultado.Contains("Antes: 1 uds"),
                $"NO debería mostrar 'Antes: 1 uds' porque el producto anterior NO necesitaba reservas (había stock suficiente). Resultado actual: {resultado}");

            // Sin embargo, SÍ debe mostrar las reservas del producto nuevo
            Assert.IsTrue(resultado.Contains("Faltan 5 uds"),
                $"Debería mostrar que faltan 5 uds del producto nuevo. Resultado actual: {resultado}");
            Assert.IsTrue(resultado.Contains("REI:") && resultado.Contains("ALC:"),
                $"Debería mostrar los almacenes con stock disponible (REI y ALC). Resultado actual: {resultado}");
        }

        /// <summary>
        /// Test que verifica el caso donde se muestra columna Reservar vacía.
        /// Escenario: Modificación de pedido donde una línea que YA necesitaba reservas NO se modifica
        /// - La línea tiene color "DeepPink" (necesita reservas)
        /// - Pero NO cambió ni producto, ni cantidad (linea.CantidadAnterior == null)
        /// - Por tanto, NO se llama a CalcularReservasAlmacenes y la columna queda vacía
        ///
        /// Resultado esperado: DEBERÍA mostrar información de reservas incluso si no cambió
        /// </summary>
        [TestMethod]
        public void CalcularReservasAlmacenes_DeberaMostrarReservasParaLineaExistenteSinCambios()
        {
            // Arrange
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 123456,
                cliente = "TEST01",
                contacto = "0",
                fecha = DateTime.Now,
                vendedor = "V01",
                EsPresupuesto = false
            };

            var linea = new LineaPedidoVentaDTO
            {
                id = 1, // Línea existente
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "PROD123",
                Cantidad = 10,
                CantidadAnterior = null, // NO hay cambio de cantidad (línea no fue modificada en esta actualización)
                ProductoAnterior = null, // NO hay cambio de producto
                almacen = Constantes.Almacenes.ALGETE
            };

            pedido.Lineas = new List<LineaPedidoVentaDTO> { linea };

            // Mock del servicio de stocks
            var servicioGestorStocks = A.Fake<IServicioGestorStocks>();

            // La línea necesita reservas (stock insuficiente)
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD123", Constantes.Almacenes.ALGETE))
                .Returns(10); // Los 10 pendientes son del propio pedido
            A.CallTo(() => servicioGestorStocks.Stock("PROD123", Constantes.Almacenes.ALGETE))
                .Returns(5); // Solo hay 5 disponibles (faltan 5)

            // Stock en otros almacenes
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD123", Constantes.Almacenes.REINA))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("PROD123", Constantes.Almacenes.REINA))
                .Returns(8);
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD123", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("PROD123", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0);

            // Crear GestorStocks real
            var gestorStocks = new GestorStocks(servicioGestorStocks);

            var db = A.Fake<NVEntities>();
            var servicioVendedores = A.Fake<IServicioVendedores>();
            var servicioCorreo = A.Fake<IServicioCorreoElectronico>();

            var gestor = new GestorPresupuestos(
                pedido,
                null,
                db,
                servicioVendedores,
                servicioGestorStocks,
                servicioCorreo
            );

            var almacenesConReservas = new Dictionary<string, HashSet<string>>();

            // Act
            string resultado = gestor.CalcularReservasAlmacenes(linea, gestorStocks, servicioGestorStocks, almacenesConReservas);

            // Assert
            // El bug: cuando no hay CantidadAnterior (línea no modificada), no se muestra nada
            // Pero DEBERÍA mostrar las reservas porque la línea SÍ necesita reservas
            Assert.IsFalse(string.IsNullOrWhiteSpace(resultado),
                $"DEBERÍA mostrar información de reservas incluso si la línea no cambió. Resultado actual: '{resultado}'");
            Assert.IsTrue(resultado.Contains("REI:"),
                $"Debería mostrar el almacén REI con stock disponible. Resultado actual: {resultado}");
        }

        /// <summary>
        /// Test que verifica el caso de línea eliminada que tenía reservas.
        /// Escenario: Modificación de pedido donde se elimina una línea (cantidad = 0) que tenía reservas
        /// - Antes: Producto X, cantidad 10, necesitaba reservas
        /// - Ahora: Cantidad = 0 (eliminada)
        ///
        /// Resultado esperado: DEBERÍA mostrar que se debe LIBERAR las reservas
        /// </summary>
        [TestMethod]
        public void GenerarTextoLiberarReservas_DeberaMostrarLiberarParaLineaEliminada()
        {
            // Arrange
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 123456,
                cliente = "TEST01",
                contacto = "0",
                fecha = DateTime.Now,
                vendedor = "V01",
                EsPresupuesto = false
            };

            var linea = new LineaPedidoVentaDTO
            {
                id = 1, // Línea existente
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "PROD456",
                Cantidad = 0, // ELIMINADA
                CantidadAnterior = 10, // Antes tenía 10 unidades
                ProductoAnterior = null, // No cambió el producto
                almacen = Constantes.Almacenes.ALGETE
            };

            pedido.Lineas = new List<LineaPedidoVentaDTO> { linea };

            // Mock del servicio de stocks
            var servicioGestorStocks = A.Fake<IServicioGestorStocks>();

            // La línea ANTES necesitaba reservas
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD456", Constantes.Almacenes.ALGETE))
                .Returns(0); // Ya no hay pendientes (se eliminó)
            A.CallTo(() => servicioGestorStocks.Stock("PROD456", Constantes.Almacenes.ALGETE))
                .Returns(5); // Había stock insuficiente (10 solicitadas - 5 disponibles)

            // Stock en otros almacenes (que tenían reservas)
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD456", Constantes.Almacenes.REINA))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("PROD456", Constantes.Almacenes.REINA))
                .Returns(6);
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD456", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("PROD456", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0);

            // Crear GestorStocks real
            var gestorStocks = new GestorStocks(servicioGestorStocks);

            var db = A.Fake<NVEntities>();
            var servicioVendedores = A.Fake<IServicioVendedores>();
            var servicioCorreo = A.Fake<IServicioCorreoElectronico>();

            var gestor = new GestorPresupuestos(
                pedido,
                null,
                db,
                servicioVendedores,
                servicioGestorStocks,
                servicioCorreo
            );

            // Act
            string resultado = gestor.GenerarTextoLiberarReservas(linea, "PROD456", 10, gestorStocks, servicioGestorStocks);

            // Assert
            Assert.IsFalse(string.IsNullOrWhiteSpace(resultado),
                $"DEBERÍA mostrar información para liberar reservas. Resultado actual: '{resultado}'");
            Assert.IsTrue(resultado.Contains("LIBERAR"),
                $"Debería contener la palabra LIBERAR. Resultado actual: {resultado}");
            Assert.IsTrue(resultado.Contains("ELIMINADA") || resultado.Contains("10 uds"),
                $"Debería indicar que se eliminaron las 10 uds. Resultado actual: {resultado}");
        }

        /// <summary>
        /// Test que verifica que NO se muestran reservas para líneas en estado PRESUPUESTO.
        /// Escenario: Línea con stock insuficiente pero en estado PRESUPUESTO (-3)
        /// - La línea necesitaría reservas (stock insuficiente)
        /// - Pero está en estado PRESUPUESTO (no confirmado)
        ///
        /// Resultado esperado: NO debería calcular reservas (no tiene sentido reservar algo no confirmado)
        /// </summary>
        [TestMethod]
        public void CalcularReservasAlmacenes_NoDeberaMostrarReservasParaLineasPresupuesto()
        {
            // Arrange
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 123456,
                cliente = "TEST01",
                contacto = "0",
                fecha = DateTime.Now,
                vendedor = "V01",
                EsPresupuesto = true
            };

            var linea = new LineaPedidoVentaDTO
            {
                id = 0, // Línea nueva
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "PROD789",
                Cantidad = 10,
                almacen = Constantes.Almacenes.ALGETE,
                estado = Constantes.EstadosLineaVenta.PRESUPUESTO // Estado PRESUPUESTO
            };

            pedido.Lineas = new List<LineaPedidoVentaDTO> { linea };

            // Mock del servicio de stocks
            var servicioGestorStocks = A.Fake<IServicioGestorStocks>();

            // Stock insuficiente (normalmente necesitaría reservas)
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD789", Constantes.Almacenes.ALGETE))
                .Returns(10);
            A.CallTo(() => servicioGestorStocks.Stock("PROD789", Constantes.Almacenes.ALGETE))
                .Returns(3); // Solo hay 3 disponibles (faltarían 7)

            // Stock en otros almacenes
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD789", Constantes.Almacenes.REINA))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("PROD789", Constantes.Almacenes.REINA))
                .Returns(10);
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PROD789", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("PROD789", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0);

            // Crear GestorStocks real
            var gestorStocks = new GestorStocks(servicioGestorStocks);

            var db = A.Fake<NVEntities>();
            var servicioVendedores = A.Fake<IServicioVendedores>();
            var servicioCorreo = A.Fake<IServicioCorreoElectronico>();

            var gestor = new GestorPresupuestos(
                pedido,
                null,
                db,
                servicioVendedores,
                servicioGestorStocks,
                servicioCorreo
            );

            var almacenesConReservas = new Dictionary<string, HashSet<string>>();

            // Act
            string resultado = gestor.CalcularReservasAlmacenes(linea, gestorStocks, servicioGestorStocks, almacenesConReservas);

            // Assert
            // NO debería mostrar reservas porque está en estado PRESUPUESTO
            Assert.IsTrue(string.IsNullOrWhiteSpace(resultado),
                $"NO debería mostrar reservas para líneas en estado PRESUPUESTO. Resultado actual: '{resultado}'");

            // Verificar que no se añadieron almacenes a la lista de reservas
            Assert.AreEqual(0, almacenesConReservas.Count,
                "No debería haber almacenes con reservas para líneas PRESUPUESTO");
        }

        /// <summary>
        /// Test que reproduce el bug de la columna Reservar vacía.
        /// Escenario: Modificación de pedido donde una línea ANTES necesitaba reservas pero AHORA NO
        /// - Antes: Producto 43228, cantidad 2, faltaba stock
        /// - Ahora: Mismo producto, misma cantidad (2), YA NO falta stock
        /// - La línea NO cambió (ni producto ni cantidad)
        ///
        /// BUG ORIGINAL:
        /// - La lógica en GenerarTablaHTML (línea 341) solo verificaba si cambió producto o cantidad
        /// - Si NO cambió nada, no se generaba texto de liberar → columna vacía
        ///
        /// FIX:
        /// - Ahora verifica SIEMPRE si antes necesitaba reservas (línea 342 GestorPresupuestos.cs)
        /// - Genera texto "Stock disponible en almacén principal" cuando no cambió nada (línea 869)
        /// </summary>
        [TestMethod]
        public void GenerarTextoLiberar_DeberaMostrarMensajeCuandoNoHayCambiosEnLinea()
        {
            // Arrange
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 901555,
                cliente = "14375",
                contacto = "1",
                fecha = new DateTime(2025, 10, 27),
                vendedor = "V01",
                EsPresupuesto = false
            };

            // Línea que NO cambió (ni producto ni cantidad)
            var linea = new LineaPedidoVentaDTO
            {
                id = 1,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "43228",
                Cantidad = 2,
                CantidadAnterior = 2, // NO cambió
                ProductoAnterior = null, // NO cambió
                almacen = Constantes.Almacenes.ALGETE,
                estado = Constantes.EstadosLineaVenta.PENDIENTE
            };

            pedido.Lineas = new List<LineaPedidoVentaDTO> { linea };

            // Mock del servicio de stocks
            var servicioGestorStocks = A.Fake<IServicioGestorStocks>();

            // Configurar stocks para que ANTES faltara (faltante > 0)
            // Stock físico = 1, Pendientes = 2 (este pedido)
            // Disponible = Stock(1) - PendientesOtros(0) = 1
            // Faltante = CantidadAnterior(2) - Disponible(1) = 1 > 0 ✓
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("43228", Constantes.Almacenes.ALGETE))
                .Returns(2);
            A.CallTo(() => servicioGestorStocks.Stock("43228", Constantes.Almacenes.ALGETE))
                .Returns(1);

            // Stock en otros almacenes (para que haya almacenes con reservas)
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("43228", Constantes.Almacenes.REINA))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("43228", Constantes.Almacenes.REINA))
                .Returns(2);

            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("43228", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("43228", Constantes.Almacenes.ALCOBENDAS))
                .Returns(1);

            var gestorStocks = new GestorStocks(servicioGestorStocks);
            var db = A.Fake<NVEntities>();
            var servicioVendedores = A.Fake<IServicioVendedores>();
            var servicioCorreo = A.Fake<IServicioCorreoElectronico>();

            var gestor = new GestorPresupuestos(
                pedido,
                null,
                db,
                servicioVendedores,
                servicioGestorStocks,
                servicioCorreo
            );

            // Act - Llamar directamente a GenerarTextoLiberarReservas
            string textoLiberar = gestor.GenerarTextoLiberarReservas(
                linea,
                "43228", // productoAnterior (mismo producto)
                2, // cantidadAnterior (misma cantidad)
                gestorStocks,
                servicioGestorStocks
            );

            // Assert
            Assert.IsFalse(string.IsNullOrWhiteSpace(textoLiberar),
                $"DEBERÍA generar texto de liberar. Resultado: '{textoLiberar}'");

            Assert.IsTrue(textoLiberar.Contains("LIBERAR"),
                $"Debe contener 'LIBERAR'. Resultado: {textoLiberar}");

            Assert.IsTrue(textoLiberar.Contains("Stock disponible en almacén principal"),
                $"Debe indicar que hay stock disponible. Resultado: {textoLiberar}");

            Assert.IsTrue(textoLiberar.Contains("2 uds"),
                $"Debe mostrar la cantidad (2 uds). Resultado: {textoLiberar}");
        }

        /// <summary>
        /// Test que verifica que NO se añaden almacenes para enviar correos si no hay stock disponible.
        /// Escenario: Pedido con línea que necesita reservas PERO no hay stock en otros almacenes
        /// - Línea con stock insuficiente en ALG
        /// - NO hay stock disponible en REI ni ALC
        ///
        /// Resultado esperado: NO debe haber almacenes en la lista (columna no se mostraría)
        /// </summary>
        [TestMethod]
        public void ObtenerAlmacenesConReservas_NoDeberaRetornarAlmacenesSiNoHayStockDisponible()
        {
            // Arrange
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                numero = 123456,
                cliente = "TEST01",
                contacto = "0",
                fecha = DateTime.Now,
                vendedor = "V01",
                EsPresupuesto = false
            };

            var linea = new LineaPedidoVentaDTO
            {
                id = 0,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "PRODSIN",
                Cantidad = 10,
                almacen = Constantes.Almacenes.ALGETE,
                estado = Constantes.EstadosLineaVenta.PENDIENTE
            };

            pedido.Lineas = new List<LineaPedidoVentaDTO> { linea };

            // Mock del servicio de stocks
            var servicioGestorStocks = A.Fake<IServicioGestorStocks>();

            // Stock insuficiente en ALG
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PRODSIN", Constantes.Almacenes.ALGETE))
                .Returns(10);
            A.CallTo(() => servicioGestorStocks.Stock("PRODSIN", Constantes.Almacenes.ALGETE))
                .Returns(3); // Faltan 7 uds

            // NO hay stock en otros almacenes
            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PRODSIN", Constantes.Almacenes.REINA))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("PRODSIN", Constantes.Almacenes.REINA))
                .Returns(0); // Sin stock

            A.CallTo(() => servicioGestorStocks.UnidadesPendientesEntregarAlmacen("PRODSIN", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0);
            A.CallTo(() => servicioGestorStocks.Stock("PRODSIN", Constantes.Almacenes.ALCOBENDAS))
                .Returns(0); // Sin stock

            var db = A.Fake<NVEntities>();
            var servicioVendedores = A.Fake<IServicioVendedores>();
            var servicioCorreo = A.Fake<IServicioCorreoElectronico>();

            var gestor = new GestorPresupuestos(
                pedido,
                null,
                db,
                servicioVendedores,
                servicioGestorStocks,
                servicioCorreo
            );

            // Act - Usar reflexión para acceder al método privado
            var metodo = typeof(GestorPresupuestos).GetMethod("ObtenerAlmacenesConReservas",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var almacenes = (HashSet<string>)metodo.Invoke(gestor, null);

            // Assert
            // NO debe haber almacenes porque no hay stock disponible en ningún lado
            Assert.AreEqual(0, almacenes.Count,
                "NO debería haber almacenes con reservas si no hay stock disponible en otros almacenes");
        }
    }
}