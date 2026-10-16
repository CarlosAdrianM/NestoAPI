using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos.Aplazame;
using NestoAPI.Models;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Pagos.Aplazame
{
    /// <summary>
    /// NestoAPI#663: el checkout de Aplázame. <c>order.total_amount</c> tiene que ser EXACTAMENTE lo que se cobra (el total
    /// del pedido) y, además, artículos + envío − descuento tienen que sumarlo, con importes que no se dejan repartir.
    /// </summary>
    [TestClass]
    public class CheckoutAplazameTests
    {
        private static LineaPedidoVentaDTO Producto(string producto, int cantidad, decimal precio, decimal descuento = 0,
            decimal iva = 0.21M, decimal re = 0)
        {
            return new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                texto = "PRODUCTO " + producto,
                Cantidad = cantidad,
                PrecioUnitario = precio,
                DescuentoLinea = descuento,
                AplicarDescuento = true,
                PorcentajeIva = iva,
                PorcentajeRecargoEquivalencia = re
            };
        }

        private static LineaPedidoVentaDTO Portes(decimal importe, decimal iva = 0.21M, decimal re = 0)
        {
            return new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "62400003",
                texto = "PORTES",
                Cantidad = 1,
                PrecioUnitario = importe,
                PorcentajeIva = iva,
                PorcentajeRecargoEquivalencia = re
            };
        }

        /// <summary>El total como lo calcula el pedido (PedidoBase.Total): suma de líneas redondeada a dos decimales.</summary>
        private static decimal TotalDelPedido(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            PedidoVentaDTO pedido = new PedidoVentaDTO();
            foreach (LineaPedidoVentaDTO linea in lineas)
            {
                pedido.Lineas.Add(linea);
            }
            return pedido.Total;
        }

        private static void ComprobarQueCuadra(List<LineaPedidoVentaDTO> lineas)
        {
            decimal total = TotalDelPedido(lineas);
            PedidoAplazame pedido = ConstructorCheckoutAplazame.ConstruirPedido("APP-1", lineas, total, out long portes, out long ivaPortes);

            long esperado = ConstructorCheckoutAplazame.Centimos(total);
            Assert.AreEqual(esperado, pedido.TotalAmount, "total_amount es el total del pedido, al céntimo");
            Assert.IsTrue(pedido.Discount >= 0, "el descuento no puede ser negativo");
            Assert.AreEqual(pedido.TotalAmount,
                ConstructorCheckoutAplazame.Sumar(pedido.Articles, portes, ivaPortes) - pedido.Discount,
                "artículos + envío − descuento = total");
            Assert.IsTrue(pedido.Articles.All(a => a.Price >= 0 && a.Quantity >= 1));
        }

        [TestMethod]
        public void TotalExacto_ImportesIncomodosConCantidadesQueNoSeDejanRepartir()
        {
            // 3 x 10,02 con un 15 % de descuento: base 25,55 (no se divide entre 3), 7 x 3,33 con IVA reducido y portes
            ComprobarQueCuadra(new List<LineaPedidoVentaDTO>
            {
                Producto("17404", 3, 10.02M, descuento: 0.15M),
                Producto("29501", 7, 3.33M, iva: 0.10M),
                Producto("31100", 1, 0.99M),
                Portes(4.95M)
            });
        }

        [TestMethod]
        public void TotalExacto_ConRecargoDeEquivalencia()
        {
            ComprobarQueCuadra(new List<LineaPedidoVentaDTO>
            {
                Producto("17404", 11, 2.37M, descuento: 0.07M, re: 0.052M),
                Producto("29501", 13, 1.11M, iva: 0.10M, re: 0.014M),
                Portes(6.50M, re: 0.052M)
            });
        }

        [TestMethod]
        public void TotalExacto_CuandoLosRedondeosDejanLaSumaCorta()
        {
            // Muchas líneas de 0,05 € + 21 %: cada una vale 0,0605 € (0,06 al redondearla sola) y el total redondeado
            // solo una vez es mayor que la suma de las redondeadas: el descuento saldría negativo sin el ajuste.
            List<LineaPedidoVentaDTO> lineas = Enumerable.Range(0, 30).Select(i => Producto("P" + i, 1, 0.05M)).ToList();
            decimal total = TotalDelPedido(lineas);
            long sumaSinAjuste = lineas.Sum(l => ConstructorCheckoutAplazame.ImporteConImpuestos(5, 2100));
            Assert.IsTrue(sumaSinAjuste < ConstructorCheckoutAplazame.Centimos(total), "el caso es el que se quiere probar");

            ComprobarQueCuadra(lineas);
        }

        [TestMethod]
        public void TotalExacto_ConChequeRegaloYRegaloDeGanavisiones()
        {
            LineaPedidoVentaDTO regalo = Producto("GANA1", 1, 12.40M);
            regalo.AplicarDescuento = false;
            regalo.DescuentoLinea = 1M; // el regalo va a tarifa con el 100 %
            LineaPedidoVentaDTO cheque = Producto("CHEQUE", -1, 20M);
            cheque.AplicarDescuento = false;

            List<LineaPedidoVentaDTO> lineas = new List<LineaPedidoVentaDTO>
            {
                Producto("17404", 4, 19.95M),
                regalo,
                cheque,
                Portes(3.90M)
            };
            ComprobarQueCuadra(lineas);

            PedidoAplazame pedido = ConstructorCheckoutAplazame.ConstruirPedido("APP-1", lineas, TotalDelPedido(lineas), out _, out _);
            Assert.IsFalse(pedido.Articles.Any(a => a.Id == "CHEQUE"), "el cheque va dentro del descuento, no como artículo");
            Assert.AreEqual(0, pedido.Articles.Single(a => a.Id == "GANA1").Price, "el regalo va a cero");
        }

        [TestMethod]
        public void Articulo_LaCantidadQueSeReparte_VaConSuPrecioUnitario()
        {
            List<LineaPedidoVentaDTO> lineas = new List<LineaPedidoVentaDTO> { Producto("17404", 4, 19.95M) };
            PedidoAplazame pedido = ConstructorCheckoutAplazame.ConstruirPedido("APP-1", lineas, TotalDelPedido(lineas), out _, out _);

            ArticuloAplazame articulo = pedido.Articles.Single();
            Assert.AreEqual(4, articulo.Quantity);
            Assert.AreEqual(1995, articulo.Price);
            Assert.AreEqual(2100, articulo.TaxRate);
        }

        [TestMethod]
        public void Articulo_LaQueNoSeReparte_VaComoUnaUnidadConElPrecioDeLaLinea()
        {
            List<LineaPedidoVentaDTO> lineas = new List<LineaPedidoVentaDTO> { Producto("17404", 3, 10.02M, descuento: 0.15M) };
            PedidoAplazame pedido = ConstructorCheckoutAplazame.ConstruirPedido("APP-1", lineas, TotalDelPedido(lineas), out _, out _);

            ArticuloAplazame articulo = pedido.Articles.Single();
            Assert.AreEqual(1, articulo.Quantity);
            Assert.AreEqual(ConstructorCheckoutAplazame.Centimos(lineas[0].BaseImponible), articulo.Price);
            StringAssert.StartsWith(articulo.Name, "3 x ");
        }

        [TestMethod]
        public void Checkout_SerializaConLosNombresDeAplazame()
        {
            List<LineaPedidoVentaDTO> lineas = new List<LineaPedidoVentaDTO> { Producto("17404", 2, 10M), Portes(4.95M) };
            CheckoutAplazame checkout = ConstructorCheckoutAplazame.Construir("APP-5", "a@b.es", "15191",
                new DireccionCheckout { Nombre = "PELUQUERIA MARIA", Direccion = "C/ Mayor 1", CodigoPostal = "28001", Poblacion = "Madrid", Provincia = "Madrid", Pais = "ES" },
                new DireccionCheckout { Nombre = "PELUQUERIA MARIA", Direccion = "C/ Menor 2", CodigoPostal = "28002", Poblacion = "Madrid" },
                "Envío", false, lineas, TotalDelPedido(lineas), "https://api.nuevavision.es/api/Aplazame/Notificacion", "nestotiendas://aplazame/");

            JObject json = JObject.FromObject(checkout);

            Assert.AreEqual("APP-5", (string)json["order"]["id"]);
            Assert.AreEqual(3019L, (long)json["order"]["total_amount"]); // 20 + 4,95 = 24,95 + 21 % = 30,19
            Assert.AreEqual("EUR", (string)json["order"]["currency"]);
            Assert.AreEqual(495L, (long)json["shipping"]["price"]);
            Assert.AreEqual("Envío", (string)json["shipping"]["name"]);
            Assert.AreEqual("C/ Menor 2", (string)json["shipping"]["street"]);
            Assert.AreEqual("Madrid", (string)json["shipping"]["state"], "sin provincia, la población");
            Assert.AreEqual("PELUQUERIA", (string)json["billing"]["first_name"]);
            Assert.AreEqual("MARIA", (string)json["billing"]["last_name"]);
            Assert.AreEqual("a@b.es", (string)json["customer"]["email"]);
            Assert.AreEqual("nestotiendas://aplazame/pending", (string)json["merchant"]["pending_url"]);
            Assert.AreEqual("nestotiendas://aplazame/ko", (string)json["merchant"]["error_url"]);
            Assert.IsNotNull((string)json["order"]["articles"][0]["url"]);
            Assert.IsNotNull((string)json["order"]["articles"][0]["image_url"]);
        }

        [TestMethod]
        public void Pais_SoloCodigosDeDosLetras()
        {
            Assert.AreEqual("PT", ConstructorCheckoutAplazame.Pais("pt"));
            Assert.AreEqual("ES", ConstructorCheckoutAplazame.Pais("ESPAÑA"));
            Assert.AreEqual("ES", ConstructorCheckoutAplazame.Pais(null));
        }
    }
}
