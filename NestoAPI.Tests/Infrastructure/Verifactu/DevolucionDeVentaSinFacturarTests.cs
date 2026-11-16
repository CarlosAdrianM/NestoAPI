using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// NestoAPI#570 (parte 2): caso RV2600067 (29/09/26). El cliente 41235 compró 2 unidades del
    /// 44194 (pedido 926942, de fin de mes, en el albarán 729830 sin facturar) y devolvió 1
    /// (pedido 927377), que se facturó aparte. La rectificativa no podía decir qué factura
    /// rectificaba y no se pudo declarar. Ahora esa devolución no se deja facturar.
    /// </summary>
    [TestClass]
    public class DevolucionDeVentaSinFacturarTests
    {
        private static LinPedidoVta Linea(int orden, string producto, short cantidad,
            short estado = Constantes.EstadosLineaVenta.ALBARAN)
        {
            return new LinPedidoVta { Nº_Orden = orden, Producto = producto, Cantidad = cantidad, Estado = estado };
        }

        private static readonly ServicioFacturas.VentaSinFacturar VENTA_EN_ALBARAN =
            new ServicioFacturas.VentaSinFacturar { Producto = "44194", Pedido = 926942, Albaran = 729830 };

        private static Task<ServicioFacturas.VentaSinFacturar> Buscar(IEnumerable<LinPedidoVta> lineas,
            bool hayFacturas, ServicioFacturas.VentaSinFacturar enAlbaran, ICollection<int> conOrigen = null)
        {
            return ServicioFacturas.BuscarVentaSinFacturar(lineas, conOrigen,
                (producto, cantidad) => Task.FromResult(hayFacturas),
                producto => Task.FromResult(enAlbaran));
        }

        [TestMethod]
        public async Task SinFacturasDeOrigenYConLaVentaEnAlbaran_DevuelveEsaVenta()
        {
            var venta = await Buscar(new[] { Linea(1, "44194", -1) }, hayFacturas: false, enAlbaran: VENTA_EN_ALBARAN);

            Assert.IsNotNull(venta);
            Assert.AreEqual(926942, venta.Pedido);
        }

        [TestMethod]
        public async Task LaVentaYaEstaFacturada_NoImpideNada()
        {
            // Regresión: la devolución de siempre se sigue facturando aunque haya otra venta en albarán
            var venta = await Buscar(new[] { Linea(1, "44194", -1) }, hayFacturas: true, enAlbaran: VENTA_EN_ALBARAN);

            Assert.IsNull(venta);
        }

        [TestMethod]
        public async Task SinFacturasNiAlbaran_NoImpideNada()
        {
            // No hay venta que esperar: se factura como hasta ahora y lo cuenta el aviso de Verifactu
            var venta = await Buscar(new[] { Linea(1, "44194", -1) }, hayFacturas: false, enAlbaran: null);

            Assert.IsNull(venta);
        }

        [TestMethod]
        public async Task LineaCopiadaDeUnaFactura_YaSabeQueFacturaRectifica()
        {
            var venta = await Buscar(new[] { Linea(7, "44194", -1) }, hayFacturas: false, enAlbaran: VENTA_EN_ALBARAN,
                conOrigen: new List<int> { 7 });

            Assert.IsNull(venta);
        }

        [TestMethod]
        public async Task VariasLineasDelMismoProducto_SePreguntaPorLaSuma()
        {
            decimal preguntada = 0;
            _ = await ServicioFacturas.BuscarVentaSinFacturar(
                new[] { Linea(1, "44194", -1), Linea(2, "44194", -2) }, null,
                (producto, cantidad) => { preguntada = cantidad; return Task.FromResult(true); },
                producto => Task.FromResult<ServicioFacturas.VentaSinFacturar>(null));

            Assert.AreEqual(3m, preguntada);
        }

        [TestMethod]
        public async Task LineasPositivasOYaFacturadas_NoCuentan()
        {
            bool preguntado = false;
            _ = await ServicioFacturas.BuscarVentaSinFacturar(
                new[]
                {
                    Linea(1, "44194", 2),
                    Linea(2, "44194", -1, Constantes.EstadosLineaVenta.FACTURA)
                }, null,
                (producto, cantidad) => { preguntado = true; return Task.FromResult(false); },
                producto => Task.FromResult(VENTA_EN_ALBARAN));

            Assert.IsFalse(preguntado);
        }

        // 16/11/26 (RV2600152/153): la devolución de lo que el cliente no ha comprado (o ya ha devuelto) tampoco se factura.

        private static Task<ServicioFacturas.DevolucionNoFacturable> BuscarNoFacturable(IEnumerable<LinPedidoVta> lineas,
            bool hayFacturas, ServicioFacturas.VentaSinFacturar enAlbaran)
        {
            return ServicioFacturas.BuscarDevolucionNoFacturable(lineas, null,
                (producto, cantidad) => Task.FromResult(hayFacturas),
                producto => Task.FromResult(enAlbaran));
        }

        [TestMethod]
        public async Task SinFacturasNiAlbaran_EsUnaDevolucionSinCompra()
        {
            var devolucion = await BuscarNoFacturable(new[] { Linea(1, "44724", -6), Linea(2, "44724", -2) }, hayFacturas: false, enAlbaran: null);

            Assert.IsNotNull(devolucion);
            Assert.IsNull(devolucion.Venta);
            Assert.AreEqual("44724", devolucion.Producto);
            Assert.AreEqual(8m, devolucion.Cantidad);
        }

        [TestMethod]
        public async Task ConFacturasDeOrigen_SeFactura()
        {
            Assert.IsNull(await BuscarNoFacturable(new[] { Linea(1, "44724", -6) }, hayFacturas: true, enAlbaran: null));
        }

        [TestMethod]
        public async Task SiOtroProductoTieneLaVentaEnAlbaran_MandaEseMotivo()
        {
            // El de la venta en albarán dice qué hacer (facturarla antes): va por delante del «no lo ha comprado»
            var devolucion = await ServicioFacturas.BuscarDevolucionNoFacturable(
                new[] { Linea(1, "40586", -1), Linea(2, "44194", -1) }, null,
                (producto, cantidad) => Task.FromResult(false),
                producto => Task.FromResult(producto == "44194" ? VENTA_EN_ALBARAN : null));

            Assert.AreEqual(926942, devolucion.Venta?.Pedido);
        }

        [TestMethod]
        public void MensajeSinCompra_DiceProductoClienteYQueHacer()
        {
            string mensaje = ServicioFacturas.MensajeDevolucionSinCompra(930081, "30976 ",
                new ServicioFacturas.DevolucionNoFacturable { Producto = "44724 ", Cantidad = 8 });

            StringAssert.Contains(mensaje, "El pedido 930081 devuelve 8 de 44724");
            StringAssert.Contains(mensaje, "cliente 30976 con esas unidades sin devolver");
            StringAssert.Contains(mensaje, "No se puede facturar");
        }

        [TestMethod]
        public void Mensaje_DiceElPedidoElAlbaranYQueHacer()
        {
            string mensaje = ServicioFacturas.MensajeDevolucionDeVentaSinFacturar(927377, VENTA_EN_ALBARAN);

            StringAssert.Contains(mensaje, "El pedido 927377 es la devolución de una venta que todavía no está facturada");
            StringAssert.Contains(mensaje, "pedido 926942, albarán 729830");
            StringAssert.Contains(mensaje, "déjelo en albarán");
        }
    }
}
