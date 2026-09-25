using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.PedidosVenta;
using static NestoAPI.Models.Constantes.Pedidos;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// NestoAPI#542 (corte 1): el modo de facturación del pedido y su convivencia con MantenerJunto, con las
    /// mismas reglas que ModoServicio/ServirJunto (#482): el bit es la autoridad de «al completar» porque es lo
    /// único que saben decir el Nesto viejo, NestoApp y los triggers de plazos; el modo solo refina el «no».
    /// </summary>
    [TestClass]
    public class ModosFacturacionTests
    {
        [TestMethod]
        public void Efectivo_SinModoInformado_MandaMantenerJunto()
        {
            Assert.AreEqual(ModosFacturacion.AL_COMPLETAR, ModosFacturacion.Efectivo(null, mantenerJunto: true));
            Assert.AreEqual(ModosFacturacion.POR_ENTREGAS, ModosFacturacion.Efectivo(null, mantenerJunto: false));
        }

        [TestMethod]
        public void Efectivo_MantenerJuntoMarcado_SiempreEsAlCompletar_AunqueElModoDigaOtraCosa()
        {
            Assert.AreEqual(ModosFacturacion.AL_COMPLETAR, ModosFacturacion.Efectivo(1, mantenerJunto: true));
            Assert.AreEqual(ModosFacturacion.AL_COMPLETAR, ModosFacturacion.Efectivo(3, mantenerJunto: true));
            Assert.AreEqual(ModosFacturacion.AL_COMPLETAR, ModosFacturacion.Efectivo(9, mantenerJunto: true));
        }

        [TestMethod]
        public void Efectivo_MantenerJuntoDesmarcado_MandaElTresGuardadoOElUno()
        {
            Assert.AreEqual(ModosFacturacion.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, ModosFacturacion.Efectivo(3, mantenerJunto: false));
            Assert.AreEqual(ModosFacturacion.POR_ENTREGAS, ModosFacturacion.Efectivo(1, mantenerJunto: false));
            // Modo 2 con el bit desmarcado = el Nesto viejo lo desmarcó: por entregas
            Assert.AreEqual(ModosFacturacion.POR_ENTREGAS, ModosFacturacion.Efectivo(2, mantenerJunto: false));
            // Un valor fuera de rango en BD no puede tumbar nada
            Assert.AreEqual(ModosFacturacion.POR_ENTREGAS, ModosFacturacion.Efectivo(9, mantenerJunto: false));
        }

        [TestMethod]
        public void Normalizar_ClienteQueNoMandaModo_LoDerivaDeMantenerJuntoYNoCambiaNada()
        {
            var marcado = new PedidoVentaDTO { mantenerJunto = true };
            var sinMarcar = new PedidoVentaDTO { mantenerJunto = false };

            Assert.IsNull(ModosFacturacion.Normalizar(marcado));
            Assert.IsNull(ModosFacturacion.Normalizar(sinMarcar));

            Assert.AreEqual(ModosFacturacion.AL_COMPLETAR, marcado.modoFacturacion);
            Assert.IsTrue(marcado.mantenerJunto);
            Assert.AreEqual(ModosFacturacion.POR_ENTREGAS, sinMarcar.modoFacturacion);
            Assert.IsFalse(sinMarcar.mantenerJunto);
        }

        [TestMethod]
        public void Normalizar_ClienteQueNoMandaModo_ConservaElTresGuardado()
        {
            // NestoApp (o Nesto antes del corte 3) modifica un pedido que está en modo 3: el PUT llega sin
            // modo y con mantenerJunto=false. No se pisa con un 1.
            var sinModo = new PedidoVentaDTO { mantenerJunto = false };
            Assert.IsNull(ModosFacturacion.Normalizar(sinModo, modoAlmacenado: 3));
            Assert.AreEqual(ModosFacturacion.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, sinModo.modoFacturacion);

            // Si marca mantenerJunto, es que quiere una sola factura al final: gana el bit
            var marcado = new PedidoVentaDTO { mantenerJunto = true };
            Assert.IsNull(ModosFacturacion.Normalizar(marcado, modoAlmacenado: 3));
            Assert.AreEqual(ModosFacturacion.AL_COMPLETAR, marcado.modoFacturacion);
        }

        [TestMethod]
        public void Normalizar_ConModo_MantenerJuntoPasaASerSuDerivado()
        {
            // El cliente manda modo 3 con la casilla marcada (incoherente): gana el modo
            var modo3 = new PedidoVentaDTO { mantenerJunto = true, modoFacturacion = 3 };
            var modo2 = new PedidoVentaDTO { mantenerJunto = false, modoFacturacion = 2 };
            var modo1 = new PedidoVentaDTO { mantenerJunto = true, modoFacturacion = 1 };

            Assert.IsNull(ModosFacturacion.Normalizar(modo3));
            Assert.IsNull(ModosFacturacion.Normalizar(modo2));
            Assert.IsNull(ModosFacturacion.Normalizar(modo1));

            Assert.IsFalse(modo3.mantenerJunto, "PuedeFacturarPedido y prdCrearFacturaVta leen MantenerJunto: solo el 2 es 'al completar'");
            Assert.IsTrue(modo2.mantenerJunto);
            Assert.IsFalse(modo1.mantenerJunto);
        }

        [TestMethod]
        public void Normalizar_ModoInexistente_DevuelveElError()
        {
            var pedido = new PedidoVentaDTO { modoFacturacion = 4 };

            string error = ModosFacturacion.Normalizar(pedido);

            StringAssert.Contains(error, "no existe");
        }

        [TestMethod]
        public void Nombre_LosTresModos()
        {
            Assert.AreEqual("Por entregas", ModosFacturacion.Nombre(1));
            Assert.AreEqual("Al completar el pedido", ModosFacturacion.Nombre(2));
            Assert.AreEqual("Todo ahora, lo pendiente se entrega después", ModosFacturacion.Nombre(3));
        }
    }
}
