using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#765 (decisión 4 de #755): una nota de ENTREGA de solo texto («PRESTAMO HIDRA 7», «VAPOR DE CAMBIO») sale en el picking
    /// como cualquier pedido, sin reservar stock ni ubicaciones. Desde el 01/09/26, 52 de 64 notas de solo texto tuvieron picking; estas
    /// pruebas fijan ese comportamiento (con cantidad 0 o 1 en la línea de texto).
    /// </summary>
    [TestClass]
    public class NotasEntregaSoloTextoPickingTests
    {
        private static PedidoPicking NotaDeTexto(int cantidad)
        {
            var pedido = new PedidoPicking
            {
                Id = 929179,
                EsNotaEntrega = true,
                EsProductoYaFacturado = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(new LineaPedidoPicking
            {
                Id = 329324600,
                TipoLinea = Constantes.TiposLineaVenta.TEXTO,
                Producto = null,
                Cantidad = cantidad,
                BaseImponible = 0,
                FechaEntrega = new DateTime(2026, 10, 30)
            });
            return pedido;
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void NotaDeSoloTexto_SaleEnElPicking(int cantidad)
        {
            PedidoPicking nota = NotaDeTexto(cantidad);
            var candidatos = new List<PedidoPicking> { nota };

            GestorReservasStock.Reservar(new List<StockProducto>(), candidatos, new List<LineaPedidoPicking>());
            var gestor = new GestorStocksPicking(nota);

            Assert.IsTrue(gestor.HayStockDeTodo(), "Una nota no espera stock");
            Assert.IsTrue(gestor.HayStockDeAlgo(), "Tiene algo que preparar: la línea de texto");
            Assert.AreEqual(cantidad, nota.Lineas[0].CantidadReservada, "La línea de texto cuenta como servida (con cantidad 0, nada que reservar)");
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void NotaDeSoloTexto_NoReservaUbicaciones(int cantidad)
        {
            PedidoPicking nota = NotaDeTexto(cantidad);
            GestorReservasStock.Reservar(new List<StockProducto>(), new List<PedidoPicking> { nota }, new List<LineaPedidoPicking>());

            Assert.IsFalse(AsignadorPicking.NecesitaUbicacion(nota, nota.Lineas[0]));
        }

        [TestMethod]
        public void NecesitaUbicacion_SoloProductosReservadosDePedidosQueNoSonNotas()
        {
            var linea = new LineaPedidoPicking { TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "17404", Cantidad = 2, CantidadReservada = 2 };

            Assert.IsTrue(AsignadorPicking.NecesitaUbicacion(new PedidoPicking { EsNotaEntrega = false }, linea));
            Assert.IsFalse(AsignadorPicking.NecesitaUbicacion(new PedidoPicking { EsNotaEntrega = true }, linea), "Una nota de entrega no toca huecos");
            linea.CantidadReservada = 0;
            Assert.IsFalse(AsignadorPicking.NecesitaUbicacion(new PedidoPicking { EsNotaEntrega = false }, linea));
        }
    }
}
