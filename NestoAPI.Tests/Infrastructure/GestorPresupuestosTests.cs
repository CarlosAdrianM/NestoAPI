using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;

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
    }
}