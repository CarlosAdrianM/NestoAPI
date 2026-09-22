using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.PedidosCompra;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Models.PedidosCompra
{
    [TestClass]
    public class PedidoCompraDTOTests
    {
        // Regresión: el campo Texto de LinPedidoCmp es nvarchar(50). Si la descripción de la línea
        // supera 50 caracteres, EF rechazaba el SaveChangesAsync con DbEntityValidationException y el
        // alta del pedido de compra fallaba (visto en ELMAH, usuario Manuel, 25/06/2026). El mapeo debe
        // truncar a 50, igual que ya hace el pedido de venta.
        [TestMethod]
        public void ToCabPedidoCmp_TextoMayorDe50_LoTruncaA50()
        {
            string textoLargo = new string('A', 60);
            PedidoCompraDTO pedido = CrearPedidoConLinea(textoLargo, cantidad: 5, cantidadRegalo: null);

            CabPedidoCmp cabecera = pedido.ToCabPedidoCmp();

            LinPedidoCmp linea = cabecera.LinPedidoCmps.Single();
            Assert.AreEqual(50, linea.Texto.Length);
            Assert.AreEqual(textoLargo.Substring(0, 50), linea.Texto);
        }

        [TestMethod]
        public void ToCabPedidoCmp_TextoMayorDe50ConRegalo_TruncaProductoYRegalo()
        {
            // El error salía DOS veces porque la línea de producto y su línea de regalo comparten el
            // mismo Texto: ambas deben truncarse.
            string textoLargo = new string('B', 80);
            PedidoCompraDTO pedido = CrearPedidoConLinea(textoLargo, cantidad: 5, cantidadRegalo: 2);

            CabPedidoCmp cabecera = pedido.ToCabPedidoCmp();

            Assert.AreEqual(2, cabecera.LinPedidoCmps.Count, "Debe haber línea de producto y línea de regalo.");
            Assert.IsTrue(cabecera.LinPedidoCmps.All(l => l.Texto.Length == 50));
        }

        [TestMethod]
        public void ToCabPedidoCmp_TextoDe50OMenos_NoLoModifica()
        {
            string texto = "Descripción corta del producto";
            PedidoCompraDTO pedido = CrearPedidoConLinea(texto, cantidad: 5, cantidadRegalo: null);

            CabPedidoCmp cabecera = pedido.ToCabPedidoCmp();

            Assert.AreEqual(texto, cabecera.LinPedidoCmps.Single().Texto);
        }

        // ===== NestoAPI#510: pronto pago en los pedidos de compra (pedido 220319, PP5 con 45 % de producto) =====

        [TestMethod]
        public void LineaPedidoCompra_SumaDescuentos_IncluyeElProntoPagoComoElTrigger()
        {
            // 1 − (1 − 0,45)(1 − 0,05) = 0,4775, que es lo que graba trgLinPedidoCmpUpd.
            var linea = new LineaPedidoCompraDTO { PrecioUnitario = 14.50M, Cantidad = 6, DescuentoProducto = 0.45M, DescuentoPP = 0.05M };

            Assert.AreEqual(0.4775M, linea.SumaDescuentos);
            Assert.AreEqual(45.46M, linea.BaseImponible, "87,00 − ROUND(87,00 × 0,4775) = 87,00 − 41,54");
        }

        [TestMethod]
        public void LineaPedidoCompra_SinAplicarDescuento_ElProntoPagoSigueContando()
        {
            var linea = new LineaPedidoCompraDTO { PrecioUnitario = 10M, Cantidad = 1, DescuentoProducto = 0.45M, DescuentoLinea = 0.10M, DescuentoPP = 0.05M, AplicarDescuento = false };

            Assert.AreEqual(1 - (0.90M * 0.95M), linea.SumaDescuentos, "Sin AplicarDescuento se ignora el de producto, no el pronto pago");
        }

        [TestMethod]
        public void PedidoCompra_DescuentoPPDeCabecera_SePropagaALasLineas()
        {
            PedidoCompraDTO pedido = CrearPedidoConLinea("Producto", cantidad: 5, cantidadRegalo: null);

            pedido.DescuentoPP = 0.05M;

            Assert.IsTrue(pedido.Lineas.All(l => l.DescuentoPP == 0.05M));
        }

        [TestMethod]
        public void ToCabPedidoCmp_EscribeElProntoPagoEnProductoYRegalo()
        {
            // Regresión #510: la línea grabada no llevaba DescuentoPP y el trigger dejaba SumaDescuentos sin él.
            PedidoCompraDTO pedido = CrearPedidoConLinea("Producto con PP", cantidad: 5, cantidadRegalo: 1);
            pedido.Lineas.First().PrecioUnitario = 10M;
            pedido.Lineas.First().DescuentoProducto = 0.45M;
            pedido.DescuentoPP = 0.05M;

            CabPedidoCmp cabecera = pedido.ToCabPedidoCmp();

            Assert.AreEqual(2, cabecera.LinPedidoCmps.Count);
            Assert.IsTrue(cabecera.LinPedidoCmps.All(l => l.DescuentoPP == 0.05M), "Producto y regalo llevan el PP del pedido");
            LinPedidoCmp producto = cabecera.LinPedidoCmps.Single(l => l.Cantidad == 5);
            Assert.AreEqual(26.12M, producto.BaseImponible, "50,00 − ROUND(50,00 × 0,4775 = 23,875 → 23,88): la base grabada ya lleva el PP restado");
        }

        private static PedidoCompraDTO CrearPedidoConLinea(string texto, int cantidad, int? cantidadRegalo)
        {
            return new PedidoCompraDTO
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Proveedor = "1",
                Lineas = new List<LineaPedidoCompraDTO>
                {
                    new LineaPedidoCompraDTO
                    {
                        Producto = "123",
                        TipoLinea = Constantes.TiposLineaCompra.PRODUCTO,
                        Texto = texto,
                        Cantidad = cantidad,
                        CantidadRegalo = cantidadRegalo
                    }
                }
            };
        }
    }
}
