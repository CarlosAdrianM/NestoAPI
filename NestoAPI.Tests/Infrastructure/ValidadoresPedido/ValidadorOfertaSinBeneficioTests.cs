using System.Collections.Generic;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.ValidadoresPedido
{
    [TestClass]
    public class ValidadorOfertaSinBeneficioTests
    {
        private const string PRODUCTO = "40583";

        private IServicioPrecios _servicio;
        private ValidadorOfertaSinBeneficio _validador;

        [TestInitialize]
        public void Init()
        {
            _servicio = A.Fake<IServicioPrecios>();
            _validador = new ValidadorOfertaSinBeneficio();
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precio, int? oferta, decimal descuentoLinea = 0m)
        {
            return new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = precio,
                DescuentoLinea = descuentoLinea,
                AplicarDescuento = false,
                oferta = oferta
            };
        }

        private static PedidoVentaDTO PedidoCon(params LineaPedidoVentaDTO[] lineas)
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.Lineas = new List<LineaPedidoVentaDTO>(lineas);
            return pedido;
        }

        [TestMethod]
        public void SeisMasSeis_AmbasLineasPrecioCompleto_NoEsValido()
        {
            // El bug: 6+6 con los 12 a precio completo (oferta sin descuento).
            PedidoVentaDTO pedido = PedidoCon(
                Linea(PRODUCTO, 6, 20.40m, oferta: 1),
                Linea(PRODUCTO, 6, 20.40m, oferta: 1));

            RespuestaValidacion r = _validador.EsPedidoValido(pedido, _servicio);

            Assert.IsFalse(r.ValidacionSuperada);
            Assert.IsTrue(r.AutorizadaDenegadaExpresamente, "Debe ser un rechazo duro (no anulable por aceptación)");
        }

        [TestMethod]
        public void SeisMasSeis_RegaloGratis_EsValido()
        {
            // Oferta real: 6 pagadas + 6 gratis.
            PedidoVentaDTO pedido = PedidoCon(
                Linea(PRODUCTO, 6, 20.40m, oferta: 1),
                Linea(PRODUCTO, 6, 0m, oferta: 1));

            Assert.IsTrue(_validador.EsPedidoValido(pedido, _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void SegundaUnidadMitadPrecio_EsValido()
        {
            // Oferta real: 2ª unidad al 50 %.
            PedidoVentaDTO pedido = PedidoCon(
                Linea(PRODUCTO, 1, 20.40m, oferta: 1),
                Linea(PRODUCTO, 1, 20.40m, oferta: 1, descuentoLinea: 0.5m));

            Assert.IsTrue(_validador.EsPedidoValido(pedido, _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void LineaSuelta_ConOferta_EsValido()
        {
            // Preocupación del usuario: una única línea con precio especial (p. ej. DescuentosProducto)
            // que coja nº de oferta NO se debe rechazar (regla: solo grupos con >= 2 líneas).
            PedidoVentaDTO pedido = PedidoCon(
                Linea(PRODUCTO, 6, 18.00m, oferta: 1));

            Assert.IsTrue(_validador.EsPedidoValido(pedido, _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void LineasSinOferta_EsValido()
        {
            PedidoVentaDTO pedido = PedidoCon(
                Linea(PRODUCTO, 6, 20.40m, oferta: null),
                Linea(PRODUCTO, 6, 20.40m, oferta: null));

            Assert.IsTrue(_validador.EsPedidoValido(pedido, _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void DoceTodasAlMismoPrecioDescontado_NoEsValido()
        {
            // Aunque haya descuento, si las 12 van al MISMO precio deben ir sin oferta (no como 6+6).
            PedidoVentaDTO pedido = PedidoCon(
                Linea(PRODUCTO, 6, 20.40m, oferta: 1, descuentoLinea: 0.5m),
                Linea(PRODUCTO, 6, 20.40m, oferta: 1, descuentoLinea: 0.5m));

            Assert.IsFalse(_validador.EsPedidoValido(pedido, _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void DosProductosDistintosMismaOferta_EsValido()
        {
            // Oferta combinada (varios productos): fuera del alcance de este validador.
            PedidoVentaDTO pedido = PedidoCon(
                Linea(PRODUCTO, 1, 20.40m, oferta: 1),
                Linea("OTRO", 1, 20.40m, oferta: 1));

            Assert.IsTrue(_validador.EsPedidoValido(pedido, _servicio).ValidacionSuperada);
        }
    }
}
