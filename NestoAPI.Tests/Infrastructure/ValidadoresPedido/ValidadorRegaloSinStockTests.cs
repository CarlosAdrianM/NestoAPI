using System.Collections.Generic;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#529: un regalo (base 0, sin oferta) sin stock no entra. Pedido 926923: la lámpara
    /// de regalo sin stock acabó pendiente sola, para un envío de 0 €.
    /// </summary>
    [TestClass]
    public class ValidadorRegaloSinStockTests
    {
        private const string LAMPARA = "44357";

        private IServicioPrecios _servicio;
        private ValidadorRegaloSinStock _validador;

        [TestInitialize]
        public void Init()
        {
            _servicio = A.Fake<IServicioPrecios>();
            _validador = new ValidadorRegaloSinStock();
        }

        private static LineaPedidoVentaDTO Regalo(string producto, int cantidad, int id = 0, int? oferta = null)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = 87.47M,
                DescuentoLinea = 1M,
                AplicarDescuento = false,
                oferta = oferta
            };
        }

        private static PedidoVentaDTO PedidoCon(params LineaPedidoVentaDTO[] lineas)
        {
            return new PedidoVentaDTO { cliente = "35059", Lineas = new List<LineaPedidoVentaDTO>(lineas) };
        }

        [TestMethod]
        public void RegaloNuevoSinStock_SeRechazaSinQueLoPuedaAceptarOtroValidador()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleTotal(LAMPARA)).Returns(0);

            RespuestaValidacion respuesta = _validador.EsPedidoValido(PedidoCon(Regalo(LAMPARA, 1)), _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.IsTrue(respuesta.AutorizadaDenegadaExpresamente, "rechazo duro: ninguna aceptación lo perdona");
            Assert.AreEqual(LAMPARA, respuesta.ProductoId);
            StringAssert.Contains(respuesta.Motivo, "no hay stock");
        }

        [TestMethod]
        public void RegaloNuevoConStock_Pasa()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleTotal(LAMPARA)).Returns(3);

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(Regalo(LAMPARA, 1)), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void VariasLineasDelMismoRegalo_SeSumanContraElStock()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleTotal(LAMPARA)).Returns(2);

            RespuestaValidacion respuesta = _validador.EsPedidoValido(PedidoCon(Regalo(LAMPARA, 2), Regalo(LAMPARA, 1)), _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, "solo hay 2");
        }

        [TestMethod]
        public void RegaloYaGuardadoSinTocar_SeRespetaAunqueYaNoHayaStock()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleTotal(LAMPARA)).Returns(0);
            LineaPedidoVentaDTO guardada = Regalo(LAMPARA, 1, id: 328400100);
            guardada.CantidadAnterior = 1;
            guardada.ProductoAnterior = LAMPARA;

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(guardada), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void RegaloGuardadoAlQueSeSubeLaCantidad_SoloCuentaLoQueSube()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleTotal(LAMPARA)).Returns(1);
            LineaPedidoVentaDTO guardada = Regalo(LAMPARA, 3, id: 328400100);
            guardada.CantidadAnterior = 1;
            guardada.ProductoAnterior = LAMPARA;

            RespuestaValidacion respuesta = _validador.EsPedidoValido(PedidoCon(guardada), _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "sube 2 y solo hay 1");
        }

        [TestMethod]
        public void LineaGuardadaALaQueSeCambiaElProducto_CuentaEntera()
        {
            // #528: el 926037 pasó de un producto a otro sin stock sin que nada lo mirara
            A.CallTo(() => _servicio.BuscarStockDisponibleTotal("40144")).Returns(0);
            LineaPedidoVentaDTO cambiada = Regalo("40144", 1, id: 327935700);
            cambiada.CantidadAnterior = 1;
            cambiada.ProductoAnterior = "37918";

            Assert.IsFalse(_validador.EsPedidoValido(PedidoCon(cambiada), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void UnidadesGratisDeUnaOferta_NoLasMiraEsteValidador()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleTotal(A<string>._)).Returns(0);

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(Regalo("44724", 2, oferta: 169221)), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void ProductoFicticio_NoSeLeExigeStock()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleTotal("FICT")).Returns(0);
            A.CallTo(() => _servicio.BuscarProducto("FICT")).Returns(new Producto { Número = "FICT", Ficticio = true });

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(Regalo("FICT", 1)), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void LineaDePago_NoEsRegalo()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleTotal(A<string>._)).Returns(0);
            LineaPedidoVentaDTO dePago = Regalo("19828", 6);
            dePago.DescuentoLinea = 0M;

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(dePago), _servicio).ValidacionSuperada);
        }
    }
}
