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
