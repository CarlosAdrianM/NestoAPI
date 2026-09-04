using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// NestoAPI#453: el DTO calculaba la base imponible con un redondeo distinto al que se graba
    /// en LinPedidoVta, y ese céntimo de diferencia se colaba en el correo del pedido, en el
    /// cuadre de vencimientos y en la proforma.
    ///
    /// <para>Caso real: pedido 925368 del 03/09/26. El total del DTO salía 594,81 € y el de la
    /// base de datos 594,80 €. Los tres vencimientos se guardaron por 198,27 € (594,81 / 3) y la
    /// proforma se negaba a generarse con "No cuadran los vencimientos con el total de la
    /// factura". Cada vez que Laura ponía el último en 198,26 €, CuadrarEfectos se lo volvía a
    /// subir a 198,27 €, porque comparaba contra el total malo.</para>
    /// </summary>
    [TestClass]
    public class LineaPedidoBaseTests
    {
        private const decimal IVA_21 = .21M;

        private static LineaPedidoVentaDTO Linea(PedidoVentaDTO pedido, decimal precio,
            short cantidad, decimal descuento)
        {
            var linea = new LineaPedidoVentaDTO
            {
                PrecioUnitario = precio,
                Cantidad = cantidad,
                DescuentoLinea = descuento,
                AplicarDescuento = true,
                PorcentajeIva = IVA_21,
                Pedido = pedido
            };
            pedido.Lineas.Add(linea);
            return linea;
        }

        [TestMethod]
        public void ImporteDescuento_SeRedondeaAntesDeRestar()
        {
            // 63,50 × 15 % = 9,525. Redondeando el descuento primero salen 9,53 y 53,97, que es
            // lo que graba CalcularImportesLinea y lo que exige el asiento del SP. Restando sin
            // redondear salía 53,975 → 53,98, un céntimo de más.
            var pedido = new PedidoVentaDTO { Lineas = new List<LineaPedidoVentaDTO>() };
            var linea = Linea(pedido, 63.50M, 1, .15M);

            Assert.AreEqual(9.53M, linea.ImporteDescuento);
            Assert.AreEqual(53.97M, linea.BaseImponible);
        }

        [TestMethod]
        public void BaseImponible_DescuentoExacto_NoCambia()
        {
            // Donde el descuento ya cae en dos decimales no puede cambiar nada
            var pedido = new PedidoVentaDTO { Lineas = new List<LineaPedidoVentaDTO>() };
            var linea = Linea(pedido, 53.20M, 1, .15M);

            Assert.AreEqual(7.98M, linea.ImporteDescuento);
            Assert.AreEqual(45.22M, linea.BaseImponible);
        }

        [TestMethod]
        public void Total_DelPedido925368_CoincideConLaBaseDeDatos()
        {
            PedidoVentaDTO pedido = CrearPedido925368();

            // La base de datos suma 491,57 € y 594,7997 € → 594,80 €
            Assert.AreEqual(491.57M, pedido.Lineas.Sum(l => l.BaseImponible));
            Assert.AreEqual(594.80M, pedido.Total);
        }

        [TestMethod]
        public void CuadrarEfectos_ConLosVencimientosYaCuadrados_NoLosToca()
        {
            // El bug que veía Laura: mandaba 198,27 / 198,27 / 198,26 (que suman el total bueno)
            // y el servidor le subía el último a 198,27 porque su total era 594,81
            PedidoVentaDTO pedido = CrearPedido925368();
            pedido.crearEfectosManualmente = true;
            pedido.Efectos = new List<EfectoPedidoVentaDTO>
            {
                new EfectoPedidoVentaDTO { Id = 1030, Importe = 198.27M },
                new EfectoPedidoVentaDTO { Id = 1031, Importe = 198.27M },
                new EfectoPedidoVentaDTO { Id = 1032, Importe = 198.26M }
            };

            pedido.CuadrarEfectos();

            Assert.AreEqual(198.26M, pedido.Efectos.Last().Importe);
            Assert.AreEqual(pedido.Total, pedido.Efectos.Sum(e => e.Importe));
        }

        [TestMethod]
        public void CuadrarEfectos_ConLosVencimientosDescuadrados_AjustaElUltimo()
        {
            // Como se guardaron el 03/09: 594,80 / 3 = 198,2666 redondeado arriba tres veces
            PedidoVentaDTO pedido = CrearPedido925368();
            pedido.crearEfectosManualmente = true;
            pedido.Efectos = new List<EfectoPedidoVentaDTO>
            {
                new EfectoPedidoVentaDTO { Id = 1030, Importe = 198.27M },
                new EfectoPedidoVentaDTO { Id = 1031, Importe = 198.27M },
                new EfectoPedidoVentaDTO { Id = 1032, Importe = 198.27M }
            };

            pedido.CuadrarEfectos();

            Assert.AreEqual(198.26M, pedido.Efectos.Last().Importe);
            Assert.AreEqual(594.80M, pedido.Efectos.Sum(e => e.Importe));
        }

        /// <summary>Las doce líneas del pedido 925368 tal y como están en producción.</summary>
        private static PedidoVentaDTO CrearPedido925368()
        {
            var pedido = new PedidoVentaDTO { Lineas = new List<LineaPedidoVentaDTO>() };

            _ = Linea(pedido, 53.20M, 1, .15M);   // 40919
            _ = Linea(pedido, 97.20M, 1, .15M);   // 38167
            _ = Linea(pedido, 77.95M, 1, .15M);   // 42649
            _ = Linea(pedido, 63.50M, 1, .15M);   // 41527  <- la del céntimo
            _ = Linea(pedido, 42.15M, 2, 0M);     // 38093
            _ = Linea(pedido, 0M, 1, 0M);         // 38093 regalo
            _ = Linea(pedido, 29.20M, 2, 0M);     // 41525
            _ = Linea(pedido, 21.35M, 2, 0M);     // 41526
            _ = Linea(pedido, 29.05M, 2, 0M);     // 41524
            _ = Linea(pedido, 2.30M, 8, 1M);      // 45663 foulard
            _ = Linea(pedido, 32.95M, 1, 1M);     // 37918 bonificado
            _ = Linea(pedido, 62.00M, 1, 1M);     // 33253 bonificado

            return pedido;
        }
    }
}
