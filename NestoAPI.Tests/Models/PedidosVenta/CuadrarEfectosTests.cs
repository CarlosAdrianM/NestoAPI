using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.PedidosVenta;
using System.Linq;

namespace NestoAPI.Tests.Models.PedidosVenta
{
    [TestClass]
    public class CuadrarEfectosTests
    {
        private PedidoVentaDTO CrearPedidoConTotal(decimal totalLinea)
        {
            var pedido = new PedidoVentaDTO
            {
                crearEfectosManualmente = true
            };
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Cantidad = 1,
                PrecioUnitario = totalLinea
            });
            return pedido;
        }

        [TestMethod]
        public void CuadrarEfectos_SumaExcedePorRedondeo_AjustaUltimoEfecto()
        {
            // 100.01 / 2 = 50.005 → redondeado a 50.01 cada uno = 100.02
            var pedido = CrearPedidoConTotal(100.01m);
            pedido.Efectos.Add(new EfectoPedidoVentaDTO { Importe = 50.01m });
            pedido.Efectos.Add(new EfectoPedidoVentaDTO { Importe = 50.01m });

            pedido.CuadrarEfectos();

            Assert.AreEqual(100.01m, pedido.Efectos.Sum(e => e.Importe));
            Assert.AreEqual(50.00m, pedido.Efectos.Last().Importe);
        }

        [TestMethod]
        public void CuadrarEfectos_SumaInferior_AjustaUltimoEfectoArriba()
        {
            var pedido = CrearPedidoConTotal(100.00m);
            pedido.Efectos.Add(new EfectoPedidoVentaDTO { Importe = 33.33m });
            pedido.Efectos.Add(new EfectoPedidoVentaDTO { Importe = 33.33m });
            pedido.Efectos.Add(new EfectoPedidoVentaDTO { Importe = 33.33m });

            pedido.CuadrarEfectos();

            Assert.AreEqual(100.00m, pedido.Efectos.Sum(e => e.Importe));
            Assert.AreEqual(33.34m, pedido.Efectos.Last().Importe);
        }

        [TestMethod]
        public void CuadrarEfectos_YaCuadrados_NoModifica()
        {
            var pedido = CrearPedidoConTotal(100.00m);
            pedido.Efectos.Add(new EfectoPedidoVentaDTO { Importe = 50.00m });
            pedido.Efectos.Add(new EfectoPedidoVentaDTO { Importe = 50.00m });

            pedido.CuadrarEfectos();

            Assert.AreEqual(50.00m, pedido.Efectos.First().Importe);
            Assert.AreEqual(50.00m, pedido.Efectos.Last().Importe);
        }

        [TestMethod]
        public void CuadrarEfectos_NoEsManual_NoModifica()
        {
            var pedido = CrearPedidoConTotal(100.00m);
            pedido.crearEfectosManualmente = false;
            pedido.Efectos.Add(new EfectoPedidoVentaDTO { Importe = 60.00m });
            pedido.Efectos.Add(new EfectoPedidoVentaDTO { Importe = 60.00m });

            pedido.CuadrarEfectos();

            Assert.AreEqual(120.00m, pedido.Efectos.Sum(e => e.Importe));
        }

        [TestMethod]
        public void CuadrarEfectos_SinEfectos_NoFalla()
        {
            var pedido = CrearPedidoConTotal(100.00m);

            pedido.CuadrarEfectos();

            Assert.AreEqual(0, pedido.Efectos.Count);
        }
    }
}
