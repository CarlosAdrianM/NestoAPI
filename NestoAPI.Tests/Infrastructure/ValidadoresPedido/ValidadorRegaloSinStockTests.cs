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
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar(LAMPARA, A<string>._)).Returns(0);

            RespuestaValidacion respuesta = _validador.EsPedidoValido(PedidoCon(Regalo(LAMPARA, 1)), _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.IsTrue(respuesta.AutorizadaDenegadaExpresamente, "rechazo duro: ninguna aceptación lo perdona");
            Assert.AreEqual(LAMPARA, respuesta.ProductoId);
            StringAssert.Contains(respuesta.Motivo, "no hay stock");
        }

        [TestMethod]
        public void RegaloNuevoConStock_Pasa()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar(LAMPARA, A<string>._)).Returns(3);

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(Regalo(LAMPARA, 1)), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void VariasLineasDelMismoRegalo_SeSumanContraElStock()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar(LAMPARA, A<string>._)).Returns(2);

            RespuestaValidacion respuesta = _validador.EsPedidoValido(PedidoCon(Regalo(LAMPARA, 2), Regalo(LAMPARA, 1)), _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, "solo hay 2");
        }

        [TestMethod]
        public void RegaloYaGuardadoSinTocar_SeRespetaAunqueYaNoHayaStock()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar(LAMPARA, A<string>._)).Returns(0);
            LineaPedidoVentaDTO guardada = Regalo(LAMPARA, 1, id: 328400100);
            guardada.CantidadAnterior = 1;
            guardada.ProductoAnterior = LAMPARA;

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(guardada), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void RegaloGuardadoAlQueSeSubeLaCantidad_SoloCuentaLoQueSube()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar(LAMPARA, A<string>._)).Returns(1);
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
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("40144", A<string>._)).Returns(0);
            LineaPedidoVentaDTO cambiada = Regalo("40144", 1, id: 327935700);
            cambiada.CantidadAnterior = 1;
            cambiada.ProductoAnterior = "37918";

            Assert.IsFalse(_validador.EsPedidoValido(PedidoCon(cambiada), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void RegaloDeUnPresupuestoQueSeAcepta_CuentaEntero()
        {
            // NestoAPI#528: el presupuesto se hizo con stock; se acepta cuando ya no queda
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar(LAMPARA, A<string>._)).Returns(0);
            LineaPedidoVentaDTO delPresupuesto = Regalo(LAMPARA, 1, id: 328400100);
            delPresupuesto.CantidadAnterior = 1;
            delPresupuesto.ProductoAnterior = LAMPARA;
            delPresupuesto.VieneDePresupuesto = true;

            Assert.IsFalse(_validador.EsPedidoValido(PedidoCon(delPresupuesto), _servicio).ValidacionSuperada);
        }

        private static PedidoVentaDTO PedidoEnAlg(byte? modo, bool servirJunto, params int[] cantidades)
        {
            PedidoVentaDTO pedido = PedidoCon();
            pedido.modoServicio = modo;
            pedido.servirJunto = servirJunto;
            foreach (int cantidad in cantidades)
            {
                LineaPedidoVentaDTO regalo = Regalo("40144", cantidad);
                regalo.almacen = "ALG";
                pedido.Lineas.Add(regalo);
            }
            return pedido;
        }

        [TestMethod]
        public void SegunVayaEntrando_MiraElStockDeSuAlmacenYNoElTotal()
        {
            // #528: 5 del 40144 en total, 3 en ALG, y entraron 5 regalos en ALG
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("40144", null)).Returns(5);
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("40144", "ALG")).Returns(3);

            RespuestaValidacion respuesta = _validador.EsPedidoValido(
                PedidoEnAlg(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, false, 4), _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, "solo hay 3 disponibles en ALG");
        }

        [TestMethod]
        public void SinModoNiServirJunto_MiraElStockDeSuAlmacen()
        {
            // NestoApp solo manda el bool
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("40144", null)).Returns(5);
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("40144", "ALG")).Returns(0);

            RespuestaValidacion respuesta = _validador.EsPedidoValido(PedidoEnAlg(null, false, 1), _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, "no hay stock en ALG");
        }

        [TestMethod]
        public void TrasReponerDeTiendas_ValeElStockDeTodasLasSedes()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("40144", null)).Returns(5);
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("40144", "ALG")).Returns(0);

            Assert.IsTrue(_validador.EsPedidoValido(
                PedidoEnAlg(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, false, 2), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void SinModoConServirJunto_ValeElStockDeTodasLasSedes()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("40144", null)).Returns(5);
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("40144", "ALG")).Returns(0);

            Assert.IsTrue(_validador.EsPedidoValido(PedidoEnAlg(null, true, 2), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void UnidadesGratisDeUnaOferta_NoLasMiraEsteValidador()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar(A<string>._, A<string>._)).Returns(0);

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(Regalo("44724", 2, oferta: 169221)), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void ProductoFicticio_NoSeLeExigeStock()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar("FICT", A<string>._)).Returns(0);
            A.CallTo(() => _servicio.BuscarProducto("FICT")).Returns(new Producto { Número = "FICT", Ficticio = true });

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(Regalo("FICT", 1)), _servicio).ValidacionSuperada);
        }

        [TestMethod]
        public void LineaDePago_NoEsRegalo()
        {
            A.CallTo(() => _servicio.BuscarStockDisponibleParaRegalar(A<string>._, A<string>._)).Returns(0);
            LineaPedidoVentaDTO dePago = Regalo("19828", 6);
            dePago.DescuentoLinea = 0M;

            Assert.IsTrue(_validador.EsPedidoValido(PedidoCon(dePago), _servicio).ValidacionSuperada);
        }
    }
}
