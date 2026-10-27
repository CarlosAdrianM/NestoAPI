using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#712 (pedido 929057, 21/10/26): todas las líneas se grabaron para el 27/10/2027 en vez de 2026 y el pedido
    /// no entró en ningún picking. Al grabar con alguna línea a más de 60 días se pregunta (aviso, no bloqueo).
    /// </summary>
    [TestClass]
    public class AvisoFechaEntregaLejanaTests
    {
        // Martes 27/10/2026: el 27/10/2027 es miércoles y está a 365 días
        private static readonly DateTime HOY = new DateTime(2026, 10, 27);
        private static readonly DateTime FECHA_929057 = new DateTime(2027, 10, 27);

        private static LineaPedidoVentaDTO Linea(DateTime fechaEntrega, string producto = "12345", int id = 0,
            short estado = Constantes.EstadosLineaVenta.PENDIENTE)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                Cantidad = 1,
                estado = estado,
                fechaEntrega = fechaEntrega
            };
        }

        private static PedidoVentaDTO Pedido(bool? confirmar, params LineaPedidoVentaDTO[] lineas)
        {
            return new PedidoVentaDTO
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                numero = 929057,
                cliente = "6316",
                contacto = "0",
                Usuario = "NUEVAVISION\\david",
                fecha = HOY,
                formaPago = Constantes.FormasPago.TARJETA,
                plazosPago = Constantes.PlazosPago.PREPAGO,
                ConfirmarFechaEntregaLejana = confirmar,
                Lineas = lineas.ToList()
            };
        }

        private static LinPedidoVta Grabada(int nOrden, DateTime fechaEntrega)
        {
            return new LinPedidoVta { Nº_Orden = nOrden, Fecha_Entrega = fechaEntrega, Estado = Constantes.EstadosLineaVenta.PENDIENTE };
        }

        private static FechaEntregaLejanaException Avisa(PedidoVentaDTO pedido, IEnumerable<LinPedidoVta> antes = null)
        {
            return Assert.ThrowsException<FechaEntregaLejanaException>(() => AvisoFechaEntregaLejana.Comprobar(pedido, antes, HOY));
        }

        private static void NoAvisa(PedidoVentaDTO pedido, IEnumerable<LinPedidoVta> antes = null)
        {
            AvisoFechaEntregaLejana.Comprobar(pedido, antes, HOY);
        }

        #region Umbral

        [TestMethod]
        public void Umbral_EsSesentaDias()
        {
            Assert.AreEqual(60, Constantes.Pedidos.DIAS_AVISO_FECHA_ENTREGA_LEJANA);
        }

        [TestMethod]
        public void Comprobar_A59Dias_NoAvisa()
        {
            NoAvisa(Pedido(false, Linea(HOY.AddDays(59))));
        }

        [TestMethod]
        public void Comprobar_A60Dias_NoAvisa()
        {
            NoAvisa(Pedido(false, Linea(HOY.AddDays(60))));
        }

        [TestMethod]
        public void Comprobar_A61Dias_Avisa()
        {
            FechaEntregaLejanaException ex = Avisa(Pedido(false, Linea(HOY.AddDays(61))));

            StringAssert.Contains(ex.Message, "(dentro de 61 días)");
        }

        [TestMethod]
        public void Comprobar_HoraDelDiaNoCuenta_A60DiasYMedioNoAvisa()
        {
            NoAvisa(Pedido(false, Linea(HOY.AddDays(60).AddHours(23))));
        }

        #endregion

        #region Mensaje

        [TestMethod]
        public void Comprobar_Pedido929057_VariasLineasMismaFecha_MensajeDeLaIssue()
        {
            FechaEntregaLejanaException ex = Avisa(Pedido(false,
                Linea(FECHA_929057, "11111"), Linea(FECHA_929057, "22222"), Linea(FECHA_929057, "33333")));

            Assert.AreEqual("La fecha de entrega de las líneas es el miércoles 27/10/2027 (dentro de 365 días). Si lo que quiere es que el pedido no salga hasta nuevo aviso, use «Retener pedido» en vez de cambiar la fecha. ¿Es correcta?", ex.Message);
        }

        [TestMethod]
        public void Comprobar_UnaLineaLejanaEntreVarias_DiceElProducto()
        {
            FechaEntregaLejanaException ex = Avisa(Pedido(false,
                Linea(HOY.AddDays(1), "11111"), Linea(FECHA_929057, "22222"), Linea(HOY.AddDays(3), "33333")));

            Assert.AreEqual("La fecha de entrega del producto 22222 es el miércoles 27/10/2027 (dentro de 365 días). Si lo que quiere es que el pedido no salga hasta nuevo aviso, use «Retener pedido» en vez de cambiar la fecha. ¿Es correcta?", ex.Message);
        }

        [TestMethod]
        public void Comprobar_UnaSolaLinea_DiceLaLinea()
        {
            FechaEntregaLejanaException ex = Avisa(Pedido(false, Linea(FECHA_929057)));

            Assert.AreEqual("La fecha de entrega de la línea es el miércoles 27/10/2027 (dentro de 365 días). Si lo que quiere es que el pedido no salga hasta nuevo aviso, use «Retener pedido» en vez de cambiar la fecha. ¿Es correcta?", ex.Message);
        }

        [TestMethod]
        public void Comprobar_VariasLineasLejanasConFechasDistintas_DiceLaMasLejana()
        {
            FechaEntregaLejanaException ex = Avisa(Pedido(false,
                Linea(HOY.AddDays(90), "11111"), Linea(FECHA_929057, "22222"), Linea(HOY.AddDays(2), "33333")));

            Assert.AreEqual("La fecha de entrega de 2 líneas llega hasta el miércoles 27/10/2027 (dentro de 365 días). Si lo que quiere es que el pedido no salga hasta nuevo aviso, use «Retener pedido» en vez de cambiar la fecha. ¿Es correcta?", ex.Message);
        }

        [TestMethod]
        public void Comprobar_DosDeTresLineasConLaMismaFechaLejana_DiceCuantas()
        {
            FechaEntregaLejanaException ex = Avisa(Pedido(false,
                Linea(FECHA_929057, "11111"), Linea(FECHA_929057, "22222"), Linea(HOY.AddDays(2), "33333")));

            Assert.AreEqual("La fecha de entrega de 2 líneas es el miércoles 27/10/2027 (dentro de 365 días). Si lo que quiere es que el pedido no salga hasta nuevo aviso, use «Retener pedido» en vez de cambiar la fecha. ¿Es correcta?", ex.Message);
        }

        [TestMethod]
        public void Comprobar_ExcepcionLlevaCodigoYDetalles()
        {
            FechaEntregaLejanaException ex = Avisa(Pedido(false, Linea(FECHA_929057, "11111"), Linea(FECHA_929057, "22222")));

            Assert.AreEqual(AvisoFechaEntregaLejana.CODIGO, ex.GetErrorCode());
            Assert.AreEqual("FECHA_ENTREGA_LEJANA", ex.GetErrorCode());
            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.AreEqual(929057, ex.Context.Pedido);
            Assert.AreEqual("2027-10-27", ex.Context.AdditionalData["fechaEntrega"]);
            Assert.AreEqual(365, ex.Context.AdditionalData["dias"]);
            Assert.AreEqual(2, ex.Context.AdditionalData["lineas"]);
        }

        #endregion

        #region Flag de confirmación (y compatibilidad)

        [TestMethod]
        public void Comprobar_ConfirmadoTrue_NoAvisa()
        {
            NoAvisa(Pedido(true, Linea(FECHA_929057)));
        }

        [TestMethod]
        public void Comprobar_ClienteQueNoMandaElFlag_NoAvisa()
        {
            // NestoApp, TNV y las llamadas internas (UnirPedidos) no conocen el flag: graban como siempre
            NoAvisa(Pedido(null, Linea(FECHA_929057)));
        }

        [TestMethod]
        public void Comprobar_PedidoNulo_NoAvisa()
        {
            AvisoFechaEntregaLejana.Comprobar(null, null, HOY);
        }

        #endregion

        #region Líneas que no cuentan

        [TestMethod]
        public void Comprobar_LineaEnAlbaranOFactura_NoCuenta()
        {
            NoAvisa(Pedido(false, Linea(FECHA_929057, estado: Constantes.EstadosLineaVenta.ALBARAN)));
            NoAvisa(Pedido(false, Linea(FECHA_929057, estado: Constantes.EstadosLineaVenta.FACTURA)));
        }

        [TestMethod]
        public void Comprobar_FechaSinInformar_NoCuenta()
        {
            NoAvisa(Pedido(false, Linea(default)));
        }

        [TestMethod]
        public void Comprobar_LineaDeCurso_NoCuenta()
        {
            LineaPedidoVentaDTO curso = Linea(FECHA_929057);
            curso.EventoId = 7;

            NoAvisa(Pedido(false, curso));
        }

        [TestMethod]
        public void Comprobar_Presupuesto_SiAvisa()
        {
            _ = Avisa(Pedido(false, Linea(FECHA_929057, estado: Constantes.EstadosLineaVenta.PRESUPUESTO)));
        }

        #endregion

        #region Modificación (PUT)

        [TestMethod]
        public void Comprobar_ModificacionSinTocarLaFecha_NoVuelveAAvisar()
        {
            // El pedido ya estaba grabado para 2027 (confirmado en su día) y se cambia otra cosa
            var antes = new[] { Grabada(1, FECHA_929057), Grabada(2, FECHA_929057) };

            NoAvisa(Pedido(false, Linea(FECHA_929057, "11111", id: 1), Linea(FECHA_929057, "22222", id: 2)), antes);
        }

        [TestMethod]
        public void Comprobar_ModificacionQueCambiaLaFechaALejana_Avisa()
        {
            var antes = new[] { Grabada(1, HOY.AddDays(1)) };

            FechaEntregaLejanaException ex = Avisa(Pedido(false, Linea(FECHA_929057, id: 1)), antes);

            StringAssert.Contains(ex.Message, "27/10/2027");
        }

        [TestMethod]
        public void Comprobar_ModificacionQueCambiaDeUnaFechaLejanaAOtra_Avisa()
        {
            var antes = new[] { Grabada(1, FECHA_929057) };

            _ = Avisa(Pedido(false, Linea(FECHA_929057.AddDays(1), id: 1)), antes);
        }

        [TestMethod]
        public void Comprobar_ModificacionQueAnadeLineaNuevaLejana_AvisaSoloPorLaNueva()
        {
            var antes = new[] { Grabada(1, FECHA_929057) };

            FechaEntregaLejanaException ex = Avisa(Pedido(false, Linea(FECHA_929057, "11111", id: 1), Linea(HOY.AddDays(100), "22222")), antes);

            Assert.AreEqual(1, ex.Context.AdditionalData["lineas"]);
            StringAssert.Contains(ex.Message, "producto 22222");
        }

        [TestMethod]
        public void Comprobar_ModificacionMismaFechaConOtraHora_NoAvisa()
        {
            var antes = new[] { Grabada(1, FECHA_929057) };

            NoAvisa(Pedido(false, Linea(FECHA_929057.AddHours(10), id: 1)), antes);
        }

        [TestMethod]
        public void Comprobar_LineaConIdQueNoEstabaGrabada_CuentaComoNueva()
        {
            var antes = new[] { Grabada(1, HOY) };

            _ = Avisa(Pedido(false, Linea(FECHA_929057, id: 99)), antes);
        }

        #endregion

        #region POST api/PedidosVenta

        [TestMethod]
        public async Task PostPedidoVenta_FlagFalseYFechaLejana_LanzaElAvisoAntesDeCrear()
        {
            NVEntities db = A.Fake<NVEntities>();
            PedidosVentaController controller = new PedidosVentaController(db);
            PedidoVentaDTO pedido = Pedido(false, Linea(DateTime.Today.AddDays(365)));
            pedido.fecha = DateTime.Today;

            FechaEntregaLejanaException ex = await Assert.ThrowsExceptionAsync<FechaEntregaLejanaException>(
                () => controller.PostPedidoVenta(pedido)).ConfigureAwait(false);

            StringAssert.Contains(ex.Message, "(dentro de 365 días). Si lo que quiere es que el pedido no salga hasta nuevo aviso, use «Retener pedido» en vez de cambiar la fecha. ¿Es correcta?");
            A.CallTo(() => db.SaveChanges()).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostPedidoVenta_SinFlag_NoAvisa()
        {
            // Compatibilidad: sin el flag el POST sigue su camino (con la BD falsa acabará fallando más adelante, pero no por el aviso)
            PedidosVentaController controller = new PedidosVentaController(A.Fake<NVEntities>());
            PedidoVentaDTO pedido = Pedido(null, Linea(DateTime.Today.AddDays(365)));
            pedido.fecha = DateTime.Today;

            try
            {
                _ = await controller.PostPedidoVenta(pedido).ConfigureAwait(false);
            }
            catch (FechaEntregaLejanaException)
            {
                Assert.Fail("Un cliente que no manda ConfirmarFechaEntregaLejana no puede recibir el aviso");
            }
            catch (Exception)
            {
                // Cualquier otro fallo de la BD falsa vale: el aviso no ha saltado
            }
        }

        #endregion

        #region POST api/PedidosVenta/UnirPedidos (ampliación desde la plantilla)

        [TestMethod]
        public async Task UnirPedidos_AmpliacionConFlagFalseYFechaLejana_LanzaElAvisoAntesDeUnir()
        {
            NVEntities db = A.Fake<NVEntities>();
            PedidosVentaController controller = new PedidosVentaController(db);
            PedidoVentaDTO ampliacion = Pedido(false, Linea(DateTime.Today.AddDays(200)));
            ampliacion.numero = 0;
            var parametro = Newtonsoft.Json.Linq.JObject.FromObject(new ParametroStringIntPedido
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                NumeroPedidoOriginal = 929057,
                PedidoAmpliacion = ampliacion
            });

            FechaEntregaLejanaException ex = await Assert.ThrowsExceptionAsync<FechaEntregaLejanaException>(
                () => controller.UnirPedidos(parametro)).ConfigureAwait(false);

            StringAssert.Contains(ex.Message, "(dentro de 200 días)");
            A.CallTo(() => db.SaveChanges()).MustNotHaveHappened();
        }

        #endregion
    }
}
