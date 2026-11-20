using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using System.Collections.Generic;
using System.Net;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>Novedades 1283 (Carlos 20/11/26): «nada puede desaparecer mientras haya mercancía o dinero en marcha».</summary>
    [TestClass]
    public class ReglaBorrarPedidoVentaTests
    {
        private const int PEDIDO = 931007;

        private static string Motivo(DatosBorrarPedidoVenta datos) => ReglaBorrarPedidoVenta.MotivoNoSePuedeBorrar(PEDIDO, datos);

        [TestMethod]
        public void PedidoLimpio_SePuedeBorrar()
        {
            Assert.IsNull(Motivo(new DatosBorrarPedidoVenta()));
            Assert.IsNull(ReglaBorrarPedidoVenta.Error("1", PEDIDO, new DatosBorrarPedidoVenta()));
        }

        [TestMethod]
        public void Picking()
        {
            Assert.AreEqual("El pedido 931007 no se puede borrar: tiene líneas en el picking 100139. Anule antes el picking.",
                Motivo(new DatosBorrarPedidoVenta { Pickings = new List<int> { 100139, 100139 } }));
            StringAssert.Contains(Motivo(new DatosBorrarPedidoVenta { Pickings = new List<int> { 100140, 100139 } }), "los pickings 100139 y 100140");
        }

        [TestMethod]
        public void AlbaranFacturaYNotaProcesada()
        {
            Assert.AreEqual("El pedido 931007 no se puede borrar: tiene líneas en el albarán 733001. Lo que ya ha salido no se borra: si hace falta, haga un abono.",
                Motivo(new DatosBorrarPedidoVenta { Albaranes = new List<int> { 733001 } }));
            StringAssert.Contains(Motivo(new DatosBorrarPedidoVenta { Facturas = new List<string> { "NV26/012345 " } }), "tiene líneas en la factura NV26/012345");
            StringAssert.Contains(Motivo(new DatosBorrarPedidoVenta { Facturas = new List<string> { null } }), "tiene líneas facturadas");
            StringAssert.Contains(Motivo(new DatosBorrarPedidoVenta { Albaranes = new List<int> { 0 } }), "tiene líneas en albarán");
            StringAssert.Contains(Motivo(new DatosBorrarPedidoVenta { LineasNotaProcesada = 2 }), "ya está procesado como nota de entrega");
        }

        [TestMethod]
        public void Envio_EnCursoOMas_YLasEtiquetasPendientesNoImpiden()
        {
            Assert.AreEqual("El pedido 931007 no se puede borrar: tiene el envío 251237 tramitado. Anule antes el envío (o pídaselo a Almacén).",
                Motivo(new DatosBorrarPedidoVenta { Envios = new List<EnvioBorrarPedidoVenta> { new EnvioBorrarPedidoVenta { Numero = 251237, Estado = 1 } } }));
            StringAssert.Contains(Motivo(new DatosBorrarPedidoVenta { Envios = new List<EnvioBorrarPedidoVenta> { new EnvioBorrarPedidoVenta { Numero = 1, Estado = 0 } } }), "en curso");
            Assert.IsNull(Motivo(new DatosBorrarPedidoVenta { Envios = new List<EnvioBorrarPedidoVenta> { new EnvioBorrarPedidoVenta { Numero = 251237, Estado = -1 } } }),
                "una etiqueta pendiente se borra con el pedido, como siempre");
        }

        [TestMethod]
        public void PrepagoYEnlaceCobrado()
        {
            Assert.AreEqual("El pedido 931007 no se puede borrar: tiene un prepago de 84,80 €. Devuélvalo o páselo a otro pedido antes.",
                Motivo(new DatosBorrarPedidoVenta { NumeroPrepagos = 1, ImportePrepagos = 84.80M }));
            Assert.AreEqual("El pedido 931007 no se puede borrar: tiene 50,00 € cobrados con un enlace de pago. Devuélvalo o páselo a otro pedido antes.",
                Motivo(new DatosBorrarPedidoVenta { CobradoPorEnlaces = 50 }));
            Assert.IsNull(Motivo(new DatosBorrarPedidoVenta { NumeroPrepagos = 2, ImportePrepagos = 0 }), "un prepago devuelto (suma 0) ya no es dinero en marcha");
        }

        [TestMethod]
        public void VariosMotivos_TodosEnUnaFrase()
        {
            var datos = new DatosBorrarPedidoVenta
            {
                Pickings = new List<int> { 100139 },
                Envios = new List<EnvioBorrarPedidoVenta> { new EnvioBorrarPedidoVenta { Numero = 251237, Estado = 1 } },
                NumeroPrepagos = 1, ImportePrepagos = 84.80M, CobradoPorEnlaces = 10
            };

            Assert.AreEqual("El pedido 931007 no se puede borrar: tiene líneas en el picking 100139, tiene el envío 251237 tramitado, tiene un prepago de 84,80 € " +
                "y tiene 10,00 € cobrados con un enlace de pago. Anule antes el picking. Anule antes el envío (o pídaselo a Almacén). Devuélvalo o páselo a otro pedido antes.",
                Motivo(datos));
            NestoBusinessException error = ReglaBorrarPedidoVenta.Error("1", PEDIDO, datos);
            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.AreEqual(ReglaBorrarPedidoVenta.ERROR_CODE, error.GetErrorCode());
            Assert.AreEqual(PEDIDO, error.Context.Pedido);
        }
    }
}
