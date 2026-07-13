using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Nesto#397: inversión PedidoVentaDTO → forma de plantilla. Casos exigidos por la issue:
    /// oferta 6+1, oferta personalizada "2ª unidad al 50 %" (Nesto#371) y regalos Ganavisiones
    /// SIN depender del texto '(BONIFICADO)' (que se trunca y varía por cliente).
    /// </summary>
    [TestClass]
    public class ConvertidorPedidoAPlantillaTests
    {
        private static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas)
        {
            var pedido = new PedidoVentaDTO
            {
                empresa = "1  ",
                cliente = "15191",
                contacto = "0 ",
                numero = 921838,
                formaPago = "RCB",
                plazosPago = "CONTADO"
            };
            foreach (LineaPedidoVentaDTO linea in lineas)
            {
                pedido.Lineas.Add(linea);
            }
            return pedido;
        }

        private static LineaPedidoVentaDTO Linea(int id, string producto, int cantidad, decimal precio,
            decimal descuento = 0, int? oferta = null, bool ganavisiones = false, byte tipoLinea = 1, bool esFicticio = false)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                Producto = producto,
                texto = "PRODUCTO DE PRUEBA CON NOMBRE MUY LARGO QUE SE TRUNCARIA (BONIF",
                Cantidad = cantidad,
                PrecioUnitario = precio,
                DescuentoLinea = descuento,
                oferta = oferta,
                EsBonificadoGanavisiones = ganavisiones,
                tipoLinea = tipoLinea,
                EsFicticio = esFicticio,
                fechaEntrega = new System.DateTime(2026, 7, 14),
                almacen = "ALG"
            };
        }

        [TestMethod]
        public void Convertir_Oferta6Mas1_ColapsaEnUnaLineaConCantidadOferta()
        {
            // 2 líneas enlazadas por oferta=77: 6 de pago a 10€ y 1 gratis (base 0).
            var resultado = ConvertidorPedidoAPlantilla.Convertir(Pedido(
                Linea(101, "38697", 6, 10m, oferta: 77),
                Linea(102, "38697", 1, 0m, oferta: 77)));

            Assert.AreEqual(1, resultado.Lineas.Count);
            LineaParaPlantillaDTO linea = resultado.Lineas.Single();
            Assert.AreEqual(6, linea.Cantidad);
            Assert.AreEqual(10m, linea.Precio);
            Assert.AreEqual(1, linea.CantidadOferta);
            Assert.IsFalse(linea.PersonalizarOferta, "Oferta gratis: no es personalizada");
            Assert.AreEqual(101, linea.IdLineaPago);
            Assert.AreEqual(102, linea.IdLineaOferta);
        }

        [TestMethod]
        public void Convertir_SegundaUnidadAl50_MarcaPersonalizarOferta()
        {
            // Nesto#371: 1 de pago a 20€ + 1 "de oferta" al 50% (base > 0), mismo nº de oferta.
            var resultado = ConvertidorPedidoAPlantilla.Convertir(Pedido(
                Linea(201, "44707", 1, 20m, oferta: 88),
                Linea(202, "44707", 1, 20m, descuento: 0.5m, oferta: 88)));

            LineaParaPlantillaDTO linea = resultado.Lineas.Single();
            Assert.AreEqual(1, linea.Cantidad);
            Assert.AreEqual(20m, linea.Precio);
            Assert.AreEqual(0m, linea.Descuento, "La línea de pago va sin descuento");
            Assert.AreEqual(1, linea.CantidadOferta);
            Assert.IsTrue(linea.PersonalizarOferta);
            Assert.AreEqual(20m, linea.PrecioOferta);
            Assert.AreEqual(0.5m, linea.DescuentoOferta);
        }

        [TestMethod]
        public void Convertir_GanavisionesPorFlag_VaARegalosAunqueElTextoEsteTruncado()
        {
            // La clasificación viene del flag del GET (#279), NUNCA del texto (el helper Linea
            // pone a propósito un texto con '(BONIF' truncado en TODAS las líneas).
            var resultado = ConvertidorPedidoAPlantilla.Convertir(Pedido(
                Linea(301, "12345", 2, 15m),
                Linea(302, "45473", 1, 0m, descuento: 1m, ganavisiones: true)));

            Assert.AreEqual(1, resultado.Lineas.Count);
            Assert.AreEqual(1, resultado.Regalos.Count);
            RegaloParaPlantillaDTO regalo = resultado.Regalos.Single();
            Assert.AreEqual("45473", regalo.Producto);
            Assert.AreEqual(302, regalo.IdLinea);
        }

        [TestMethod]
        public void Convertir_LineaACeroSinFlag_EsLineaNormalNoRegalo()
        {
            // MMP/regalo por importe: base 0 sin flag → línea normal (round-trip-safe), NO regalo.
            var resultado = ConvertidorPedidoAPlantilla.Convertir(Pedido(
                Linea(401, "36031", 1, 10m, descuento: 1m)));

            Assert.AreEqual(1, resultado.Lineas.Count);
            Assert.AreEqual(0, resultado.Regalos.Count);
            Assert.AreEqual(1m, resultado.Lineas.Single().Descuento);
        }

        [TestMethod]
        public void Convertir_LineasDeSistema_SeDescartan()
        {
            // Portes/reembolso (tipoLinea = CUENTA_CONTABLE) y ficticios no van a la plantilla.
            var resultado = ConvertidorPedidoAPlantilla.Convertir(Pedido(
                Linea(501, "38697", 1, 10m),
                Linea(502, "62400003", 1, 4.3m, tipoLinea: (byte)Constantes.TiposLineaVenta.CUENTA_CONTABLE),
                Linea(503, "PORTES", 1, 0m, esFicticio: true)));

            Assert.AreEqual(1, resultado.Lineas.Count);
            Assert.AreEqual("38697", resultado.Lineas.Single().Producto);
        }

        [TestMethod]
        public void Convertir_GrupoDeOfertaConSoloLaLineaGratis_CantidadCeroYCantidadOferta()
        {
            // Caso borde de la issue: del grupo solo queda la parte regalada (base 0).
            var resultado = ConvertidorPedidoAPlantilla.Convertir(Pedido(
                Linea(601, "38697", 1, 0m, oferta: 99)));

            LineaParaPlantillaDTO linea = resultado.Lineas.Single();
            Assert.AreEqual(0, linea.Cantidad);
            Assert.AreEqual(1, linea.CantidadOferta);
            Assert.AreEqual(601, linea.IdLineaOferta);
        }

        [TestMethod]
        public void Convertir_Cabecera_MapeaIdentidadYEntrega()
        {
            var resultado = ConvertidorPedidoAPlantilla.Convertir(Pedido(
                Linea(701, "38697", 1, 10m)));

            Assert.AreEqual("1", resultado.Empresa);
            Assert.AreEqual("15191", resultado.Cliente);
            Assert.AreEqual("0", resultado.Contacto);
            Assert.AreEqual(921838, resultado.NumeroPedido);
            Assert.AreEqual("RCB", resultado.FormaPago);
            Assert.AreEqual(new System.DateTime(2026, 7, 14), resultado.FechaEntrega);
            Assert.AreEqual("ALG", resultado.Almacen);
        }
    }
}
