using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// La línea de portes que añade el PUT de un pedido. La regresión (21/10/26): Paloma ponía «Su pedido» en los pedidos
    /// 928974, 928975 y 928977, ya albaranados y con sus portes en estado 2. El PUT tomaba la PRIMERA línea como referencia,
    /// la de portes nacía en estado 2 y la guarda de CrearLineaVta (NestoAPI#580) tumbaba el guardado. Con la referencia
    /// en una línea aún por servir, como el reembolso, no se añaden portes a lo ya servido.
    /// </summary>
    [TestClass]
    public class GestorPortesLineaPortesPutTests
    {
        private static readonly ResultadoPortes PORTES = new ResultadoPortes { ImportePortes = 7, CuentaPortes = "62400005" };

        [TestMethod]
        public void CrearLineaPortesPut_TodoYaAlbaranado_NoAnadePortes()
        {
            var lineas = new List<LineaPedidoVentaDTO>
            {
                Linea("39167", Constantes.EstadosLineaVenta.ALBARAN),
                Linea("24392", Constantes.EstadosLineaVenta.ALBARAN)
            };

            Assert.IsNull(GestorPortes.CrearLineaPortesPut(PORTES, lineas, new List<LinPedidoVta>(), "G21"));
        }

        [TestMethod]
        public void CrearLineaPortesPut_PrimeraFacturadaYOtraPendiente_NaceComoLaPendiente()
        {
            // Forma del 927608: la primera línea ya en factura y otra aún pendiente.
            var lineas = new List<LineaPedidoVentaDTO>
            {
                Linea("12634", Constantes.EstadosLineaVenta.FACTURA, "ALG"),
                Linea("45958", Constantes.EstadosLineaVenta.PENDIENTE, "REI")
            };

            LineaPedidoVentaDTO portes = GestorPortes.CrearLineaPortesPut(PORTES, lineas, new List<LinPedidoVta>(), "G21");

            Assert.IsNotNull(portes);
            Assert.AreEqual((short)Constantes.EstadosLineaVenta.PENDIENTE, portes.estado);
            Assert.AreEqual("REI", portes.almacen);
            Assert.AreEqual("62400005", portes.Producto);
            Assert.AreEqual(7m, portes.PrecioUnitario);
            Assert.AreEqual((byte)Constantes.TiposLineaVenta.CUENTA_CONTABLE, portes.tipoLinea);
        }

        private static LineaPedidoVentaDTO Linea(string producto, short estado, string almacen = "ALC")
        {
            return new LineaPedidoVentaDTO
            {
                Producto = producto,
                estado = estado,
                almacen = almacen,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                fechaEntrega = new DateTime(2026, 10, 21)
            };
        }
    }
}
