using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>
    /// NestoAPI#849 (corte 1, incidencia 1182): el pedido se guarda en el enlace de pago y, cuando está, manda sobre el concepto.
    /// </summary>
    [TestClass]
    public class PedidoEnlacePago849Tests
    {
        private const string TPV = Constantes.TiposPagoTPV.TPV_VIRTUAL;

        // ---------- Lectura ----------

        [TestMethod]
        public void ConPedidoGuardadoYConceptoLibre_SeReconoce()
        {
            Assert.IsTrue(EnlacesPagoPedido.EsEnlaceDelPedido(TPV, "Enlace de pago a Nueva Visión", 929559, 929559));
            Assert.AreEqual(929559, EnlacesPagoPedido.PedidoDelEnlace(929559, "Enlace de pago a Nueva Visión"));
        }

        [TestMethod]
        public void ElPedidoGuardadoMandaSobreElConcepto()
        {
            Assert.IsFalse(EnlacesPagoPedido.EsEnlaceDelPedido(TPV, "Pago pedido 929559 de Nueva Visión", 929559, 930000));
            Assert.IsTrue(EnlacesPagoPedido.EsEnlaceDelPedido(TPV, "Pago pedido 929559 de Nueva Visión", 930000, 930000));
        }

        [TestMethod]
        public void SinPedidoGuardado_ComoHoy_PorElConcepto()
        {
            Assert.IsTrue(EnlacesPagoPedido.EsEnlaceDelPedido(TPV, "Pago pedido 929559 de Nueva Visión", 929559, null));
            Assert.IsTrue(EnlacesPagoPedido.EsEnlaceDelPedido(TPV, "929559", 929559, null));
            Assert.IsFalse(EnlacesPagoPedido.EsEnlaceDelPedido(TPV, "Enlace de pago a Nueva Visión", 929559, null));
            Assert.AreEqual(EnlacesPagoPedido.EsEnlaceDelPedido(TPV, "Pedido 928521", 928521),
                EnlacesPagoPedido.EsEnlaceDelPedido(TPV, "Pedido 928521", 928521, null));
        }

        [TestMethod]
        public void SoloEnlacesTpvVirtual_AunqueTenganPedidoGuardado()
        {
            Assert.IsFalse(EnlacesPagoPedido.EsEnlaceDelPedido(Constantes.TiposPagoTPV.PEDIDO_APP, "x", 929559, 929559));
        }

        // ---------- Qué se guarda al crear ----------

        [TestMethod]
        public async Task PedidoAGuardar_ElDelConceptoSiEsDelMismoCliente()
        {
            Assert.AreEqual(929559, await EnlacesPagoPedido.PedidoAGuardar(null, "929559", p => Task.FromResult(p == 929559)));
        }

        [TestMethod]
        public async Task PedidoAGuardar_ConceptoDeUnPedidoDeOtroCliente_NoSeGuarda()
        {
            Assert.IsNull(await EnlacesPagoPedido.PedidoAGuardar(null, "Pago pedido 930361", p => Task.FromResult(false)));
        }

        [TestMethod]
        public async Task PedidoAGuardar_ConceptoSinPedido_NoPreguntaNiGuarda()
        {
            bool preguntado = false;
            Assert.IsNull(await EnlacesPagoPedido.PedidoAGuardar(null, "Enlace de pago a Nueva Visión", p => { preguntado = true; return Task.FromResult(true); }));
            Assert.IsFalse(preguntado);
        }

        [TestMethod]
        public async Task PedidoAGuardar_ElDeLaSolicitudPrimero_TambienComprobado()
        {
            Assert.AreEqual(930001, await EnlacesPagoPedido.PedidoAGuardar(930001, "Pago pedido 929559", p => Task.FromResult(true)));
            Assert.IsNull(await EnlacesPagoPedido.PedidoAGuardar(930001, "Pago pedido 929559", p => Task.FromResult(p != 930001)));
        }

        // ---------- ServicioPagos ----------

        private static ServicioPagos Servicio(out List<(int Id, string Empresa, int Pedido)> guardados, bool delCliente = true)
        {
            var servicio = new ServicioPagos(A.Fake<IRedsysService>(), A.Fake<IContabilidadService>(), A.Fake<ILectorParametrosUsuario>(),
                A.Fake<IServicioCorreoElectronico>(), A.Fake<ILogService>(), A.Fake<ITarjetaClienteStore>());
            var lista = new List<(int, string, int)>();
            guardados = lista;
            servicio.PedidoEsDelCliente = (empresa, cliente, pedido) => Task.FromResult(delCliente && empresa == "1" && cliente == "40026");
            servicio.GuardarPedidoEnlace = (id, empresa, pedido) => { lista.Add((id, empresa, pedido)); return Task.FromResult(true); };
            return servicio;
        }

        private static PagoTPV Pago(string tipo = TPV) => new PagoTPV { Id = 1036, Tipo = tipo, Empresa = "1 ", Cliente = "40026  " };

        [TestMethod]
        public async Task Servicio_EnlaceConConceptoDelPedidoDelCliente_LoGuarda()
        {
            ServicioPagos servicio = Servicio(out var guardados);

            int? pedido = await servicio.GuardarPedidoDelEnlace(Pago(), new SolicitudPagoTPV { Descripcion = "929559" });

            Assert.AreEqual(929559, pedido);
            Assert.AreEqual((1036, "1", 929559), guardados.Single());
        }

        [TestMethod]
        public async Task Servicio_PedidoDeOtroCliente_NoLoGuarda()
        {
            ServicioPagos servicio = Servicio(out var guardados, delCliente: false);

            Assert.IsNull(await servicio.GuardarPedidoDelEnlace(Pago(), new SolicitudPagoTPV { Descripcion = "Pago pedido 930361" }));
            Assert.AreEqual(0, guardados.Count);
        }

        [TestMethod]
        public async Task Servicio_SinColumna_NoGuardaYNoFalla()
        {
            ServicioPagos servicio = Servicio(out _);
            servicio.GuardarPedidoEnlace = (id, empresa, pedido) => Task.FromResult(false); // ColumnaPedidoPagosTPV.Guardar sin la columna

            Assert.IsNull(await servicio.GuardarPedidoDelEnlace(Pago(), new SolicitudPagoTPV { Descripcion = "929559" }));
        }

        [TestMethod]
        public async Task Servicio_SiFallaLaBD_ElEnlaceSaleIgual()
        {
            ServicioPagos servicio = Servicio(out _);
            servicio.PedidoEsDelCliente = (e, c, p) => throw new System.InvalidOperationException("BD caída");

            Assert.IsNull(await servicio.GuardarPedidoDelEnlace(Pago(), new SolicitudPagoTPV { Descripcion = "929559" }));
        }

        [TestMethod]
        public async Task Servicio_CarritoDeLaApp_NoGuarda()
        {
            ServicioPagos servicio = Servicio(out var guardados);

            Assert.IsNull(await servicio.GuardarPedidoDelEnlace(Pago(Constantes.TiposPagoTPV.CARRITO_APP), new SolicitudPagoTPV { Descripcion = "929559" }));
            Assert.AreEqual(0, guardados.Count);
        }
    }
}
