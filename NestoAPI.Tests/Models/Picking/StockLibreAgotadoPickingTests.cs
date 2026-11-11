using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>11/11/26 (producto 45313, pedido 929982): «ubicación descuadrada» cuando en realidad todo lo ubicado estaba reservado.</summary>
    [TestClass]
    public class StockLibreAgotadoPickingTests
    {
        [TestMethod]
        public void TodoReservadoParaOtrosPedidos_MensajeClaroDeUsted()
        {
            string mensaje = StockLibreAgotadoPicking.Mensaje("45313     ", "ALG", 929982, new List<ReservaUbicacionPicking>
            {
                new ReservaUbicacionPicking { Pedido = 929003, Cantidad = 1 },
                new ReservaUbicacionPicking { Pedido = 929001, Cantidad = 1 },
                new ReservaUbicacionPicking { Pedido = 929002, Cantidad = 1 }
            });

            Assert.AreEqual("No queda stock libre del producto 45313 en Algete: las 3 unidades están reservadas para los pedidos 929001, 929002 y 929003. " +
                "Quite el picking de alguno o espere a que entre género.", mensaje);
        }

        [TestMethod]
        public void UnaUnidadDeUnSoloPedido_EnSingular()
        {
            string mensaje = StockLibreAgotadoPicking.Mensaje("45313", "ALG", 929982, new[] { new ReservaUbicacionPicking { Pedido = 929001, Cantidad = 1 } });

            Assert.AreEqual("No queda stock libre del producto 45313 en Algete: la unidad está reservada para el pedido 929001. " +
                "Quite el picking de ese pedido o espere a que entre género.", mensaje);
        }

        [TestMethod]
        public void SinReservasDeOtrosPedidos_Null_SeQuedaElMensajeDeSiempre()
        {
            Assert.IsNull(StockLibreAgotadoPicking.Mensaje("45313", "ALG", 929982, new List<ReservaUbicacionPicking>()));
            Assert.IsNull(StockLibreAgotadoPicking.Mensaje("45313", "ALG", 929982, new[] { new ReservaUbicacionPicking { Pedido = 929982, Cantidad = 3 } }),
                "lo reservado para el propio pedido no cuenta");
        }

        [TestMethod]
        public void EsDescuadrada_SoloElErrorDelGestor()
        {
            Assert.IsTrue(StockLibreAgotadoPicking.EsDescuadrada(new Exception(GestorUbicaciones.MENSAJE_DESCUADRADA + ". Producto: 45313. Pedido: 929982")));
            Assert.IsFalse(StockLibreAgotadoPicking.EsDescuadrada(new Exception("Descuadre en ubicación 5")));
        }

        [TestMethod]
        public void ElGestor_SinUbicacionesLibres_LanzaElErrorQueSeTraduce()
        {
            var linea = new LineaPedidoPicking { Id = 1, NumeroPedido = 929982, Producto = "45313", CantidadReservada = 1, TipoLinea = 1, Almacen = "ALG" };
            var ubicaciones = new List<UbicacionPicking>();

            Exception lanzada = null;
            try
            {
                new GestorUbicaciones(linea, ubicaciones).Ejecutar();
            }
            catch (Exception ex)
            {
                lanzada = ex;
            }

            Assert.IsTrue(StockLibreAgotadoPicking.EsDescuadrada(lanzada));
        }
    }
}
