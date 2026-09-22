using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Models.Informes;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#510: el pronto pago del pedido de compra a pie de documento. Los datos son los del
    /// pedido real 220319 (PP5 con un 45 % de producto): en BD cada línea lleva SumaDescuentos 0,4775
    /// y la base ya con el PP restado. El documento debe enseñar el 45 % en la línea y el 5 % al pie.
    /// </summary>
    [TestClass]
    public class ResumenImportesPedidoCompraTests
    {
        private const decimal PP5 = 0.05M;
        private const decimal SUMA_CON_PP = 0.4775M; // 1 − 0,55 × 0,95

        private static LineaPedidoCompraInformeDTO Linea(decimal precio, short cantidad)
        {
            decimal bruto = precio * cantidad;
            decimal importeDto = System.Math.Round(bruto * SUMA_CON_PP, 2, System.MidpointRounding.AwayFromZero);
            return new LineaPedidoCompraInformeDTO
            {
                Cantidad = cantidad,
                PrecioUnitario = precio,
                Bruto = bruto,
                SumaDescuentos = SUMA_CON_PP,
                DescuentoPP = PP5,
                BaseImponible = bruto - importeDto
            };
        }

        /// <summary>Las seis primeras líneas del 220319 tal y como están grabadas.</summary>
        private static List<LineaPedidoCompraInformeDTO> Lineas220319() => new List<LineaPedidoCompraInformeDTO>
        {
            Linea(14.50M, 6), Linea(6.50M, 6), Linea(12.00M, 8), Linea(9.90M, 6), Linea(7.00M, 6), Linea(15.50M, 6)
        };

        [TestMethod]
        public void DescuentoSinPP_DeshaceElFactorDelProntoPago()
        {
            Assert.AreEqual(0.45M, System.Math.Round(ResumenImportesPedidoCompra.DescuentoSinPP(SUMA_CON_PP, PP5), 4));
            Assert.AreEqual(0.30M, ResumenImportesPedidoCompra.DescuentoSinPP(0.30M, 0), "Sin PP el descuento es el grabado");
            Assert.AreEqual(0M, System.Math.Round(ResumenImportesPedidoCompra.DescuentoSinPP(PP5, PP5), 4), "Solo PP: la línea no lleva descuento propio");
        }

        [TestMethod]
        public void ImporteLineaSinPP_EsElBrutoConElDescuentoPropio()
        {
            // 87,00 × (1 − 0,45) = 47,85; en BD la base es 45,46 (con el 5 % ya restado).
            LineaPedidoCompraInformeDTO primera = Lineas220319()[0];
            Assert.AreEqual(45.46M, primera.BaseImponible);
            Assert.AreEqual(47.85M, ResumenImportesPedidoCompra.ImporteLineaSinPP(primera, PP5));
            Assert.AreEqual(45.46M, ResumenImportesPedidoCompra.ImporteLineaSinPP(primera, 0), "Sin PP se enseña la base grabada");
        }

        [TestMethod]
        public void ImporteLineaSinPP_SinBruto_DeshaceElPPSobreLaBase()
        {
            var linea = new LineaPedidoCompraInformeDTO { Bruto = 0, SumaDescuentos = SUMA_CON_PP, BaseImponible = 45.46M };
            Assert.AreEqual(47.85M, ResumenImportesPedidoCompra.ImporteLineaSinPP(linea, PP5));
        }

        [TestMethod]
        public void Resumen_Pedido220319_BaseProntoPagoYTotalCuadran()
        {
            var resumen = new ResumenImportesPedidoCompra(Lineas220319(), PP5);

            Assert.IsTrue(resumen.TienePP);
            Assert.AreEqual(PP5, resumen.DescuentoPP);
            Assert.AreEqual(229.02M, resumen.Subtotal, "Σ bruto × 0,55: 416,40 × 0,55");
            Assert.AreEqual(217.57M, resumen.Total, "Σ bases grabadas: lo que se factura");
            Assert.AreEqual(11.45M, resumen.ImportePP, "Por diferencia, para cuadrar al céntimo con la BD");
            Assert.AreEqual(resumen.Total, resumen.Subtotal - resumen.ImportePP);
        }

        [TestMethod]
        public void Resumen_SinProntoPago_TodoComoAntes()
        {
            var lineas = new List<LineaPedidoCompraInformeDTO>
            {
                new LineaPedidoCompraInformeDTO { Bruto = 100, SumaDescuentos = 0.30M, BaseImponible = 70, DescuentoPP = 0 },
                new LineaPedidoCompraInformeDTO { Bruto = 50, SumaDescuentos = 0, BaseImponible = 50, DescuentoPP = 0 }
            };
            var resumen = new ResumenImportesPedidoCompra(lineas, 0);

            Assert.IsFalse(resumen.TienePP);
            Assert.AreEqual(120M, resumen.Total);
            Assert.AreEqual(120M, resumen.Subtotal);
            Assert.AreEqual(0M, resumen.ImportePP);
        }

        [TestMethod]
        public void Resumen_SinLineasOPPInvalido_NoRevienta()
        {
            Assert.AreEqual(0M, new ResumenImportesPedidoCompra(null, PP5).Total);
            Assert.AreEqual(0M, new ResumenImportesPedidoCompra(new List<LineaPedidoCompraInformeDTO>(), PP5).ImportePP);
            Assert.IsFalse(new ResumenImportesPedidoCompra(Lineas220319(), 1M).TienePP, "Un PP del 100 % no es un pronto pago");
            Assert.IsFalse(new ResumenImportesPedidoCompra(Lineas220319(), -0.05M).TienePP);
        }

        [TestMethod]
        public void DescuentoPPDelPedido_EsElDeSusLineas()
        {
            Assert.AreEqual(PP5, ResumenImportesPedidoCompra.DescuentoPPDelPedido(Lineas220319()));
            Assert.AreEqual(0M, ResumenImportesPedidoCompra.DescuentoPPDelPedido(new List<LineaPedidoCompraInformeDTO>()));
            Assert.AreEqual(0M, ResumenImportesPedidoCompra.DescuentoPPDelPedido(null));
        }
    }
}
