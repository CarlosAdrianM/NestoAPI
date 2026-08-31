using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// NestoAPI#435: la forma de venta APP ("Aplicación Móviles") ya existía en la tabla
    /// FormasVenta, pero no aparecía en ninguna regla del código. Estos tests fijan las decisiones
    /// tomadas sobre cómo se comporta un pedido de la app, para que no cambien por descuido.
    /// </summary>
    [TestClass]
    public class FormaVentaAppTests
    {
        private static PedidoVentaDTO PedidoDeLaApp()
        {
            PedidoVentaDTO pedido = new PedidoVentaDTO
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                cliente = "15191",
                contacto = "0",
                comentarios = "TOTAL PEDIDO: 100"
            };
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoLinea = 0.05M,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                formaVenta = Constantes.FormasVenta.APP,
                texto = "PRODUCTO DE PRUEBA"
            });
            return pedido;
        }

        // --- Portes: los calcula el servidor, luego APP no puede ser canal externo ---

        [TestMethod]
        public void FormasVenta_App_NoEsCanalExterno()
        {
            // Si APP fuera canal externo, el servidor respetaría los portes que llegasen en la
            // petición: un cliente manipulándola podría regalarse el envío.
            Assert.IsFalse(Constantes.FormasVenta.EsCanalExterno(Constantes.FormasVenta.APP));
        }

        [TestMethod]
        public void FormasVenta_App_NoEstaEnLaListaDeCanalesExternos()
        {
            CollectionAssert.DoesNotContain(Constantes.FormasVenta.CANALES_EXTERNOS, Constantes.FormasVenta.APP);
        }

        [TestMethod]
        public void FormasVenta_CanalesExternos_SiguenSiendoLosCuatroDeSiempre()
        {
            CollectionAssert.AreEquivalent(
                new[] { "STK", "WEB", "QRU", "BLT" },
                Constantes.FormasVenta.CANALES_EXTERNOS);
        }

        // --- Picking: un pedido de la app se prepara y se envía como uno de la web ---

        [TestMethod]
        public void FormasVenta_App_SePreparaComoTiendaOnline()
        {
            Assert.IsTrue(Constantes.FormasVenta.EsPreparacionTiendaOnline(Constantes.FormasVenta.APP));
            CollectionAssert.Contains(Constantes.FormasVenta.PREPARACION_TIENDA_ONLINE, Constantes.FormasVenta.APP);
        }

        [TestMethod]
        public void FormasVenta_PreparacionTiendaOnline_IncluyeTambienLosCanalesExternos()
        {
            foreach (string canal in Constantes.FormasVenta.CANALES_EXTERNOS)
            {
                CollectionAssert.Contains(Constantes.FormasVenta.PREPARACION_TIENDA_ONLINE, canal);
            }
        }

        // --- Albarán: los clientes de la app son profesionales, no público final ---

        [TestMethod]
        public void FormasVenta_App_NoImprimeAPrecioDePublicoFinal()
        {
            CollectionAssert.DoesNotContain(Constantes.FormasVenta.PRECIO_PUBLICO_FINAL, Constantes.FormasVenta.APP);
        }

        [TestMethod]
        public void FormasVenta_PrecioPublicoFinal_NoIncluyeMiravia()
        {
            // Miravia nunca ha ido a precio de público final: al pasar la lista a Constantes
            // hay que respetar exactamente las tres formas de venta que había.
            CollectionAssert.AreEquivalent(
                new[] { "STK", "WEB", "QRU" },
                Constantes.FormasVenta.PRECIO_PUBLICO_FINAL);
        }

        // --- Validadores de tienda online: la app NO hereda sus excepciones ---

        [TestMethod]
        public void ValidadorDescuentoTiendaOnline_PedidoDeLaApp_NoAutorizaElDescuento()
        {
            // El 5% es el voucher de Prestashop. La app no tiene vouchers: su descuento tiene que
            // pasar por revisión manual como el de cualquier otro pedido.
            PedidoVentaDTO pedido = PedidoDeLaApp();

            RespuestaValidacion respuesta = new ValidadorDescuentoTiendaOnline()
                .EsPedidoValido(pedido, "AA11", A.Fake<IServicioPrecios>());

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void ValidadorRegalosTiendaOnline_PedidoDeLaApp_NoAutorizaElRegalo()
        {
            PedidoVentaDTO pedido = PedidoDeLaApp();

            RespuestaValidacion respuesta = new ValidadorRegalosTiendaOnline()
                .EsPedidoValido(pedido, "AA11", A.Fake<IServicioPrecios>());

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        // El caso contrario (pedido todo WEB, voucher autorizado) esta cubierto en
        // GestorPreciosTests, que es donde se prepara el IServicioPrecios estatico que necesita
        // MontarOfertaPedido para leer los productos.
    }
}
