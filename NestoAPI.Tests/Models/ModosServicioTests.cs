using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using static NestoAPI.Models.Constantes.Pedidos;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// NestoAPI#482 (slice 1): el modo de servicio del pedido y su convivencia con ServirJunto.
    /// </summary>
    [TestClass]
    public class ModosServicioTests
    {
        [TestMethod]
        public void Efectivo_SinModoInformado_MandaServirJunto()
        {
            Assert.AreEqual(ModosServicio.TODO_JUNTO, ModosServicio.Efectivo(null, servirJunto: true));
            Assert.AreEqual(ModosServicio.SEGUN_VAYA_ENTRANDO, ModosServicio.Efectivo(null, servirJunto: false));
        }

        [TestMethod]
        public void Efectivo_ConModoInformado_MandaElModo()
        {
            Assert.AreEqual(ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, ModosServicio.Efectivo(4, servirJunto: true));
            Assert.AreEqual(ModosServicio.TODO_JUNTO, ModosServicio.Efectivo(1, servirJunto: false));
            // Un valor fuera de rango en BD no puede tumbar el picking: se ignora y manda ServirJunto
            Assert.AreEqual(ModosServicio.TODO_JUNTO, ModosServicio.Efectivo(9, servirJunto: true));
        }

        [TestMethod]
        public void EsEntregaUnica_SoloLosModos1Y4()
        {
            Assert.IsTrue(ModosServicio.EsEntregaUnica(1));
            Assert.IsFalse(ModosServicio.EsEntregaUnica(2));
            Assert.IsFalse(ModosServicio.EsEntregaUnica(3));
            Assert.IsTrue(ModosServicio.EsEntregaUnica(4));
        }

        [TestMethod]
        public void Normalizar_ClienteQueNoMandaModo_LoDerivaDeServirJuntoYNoCambiaNada()
        {
            var marcado = new PedidoVentaDTO { servirJunto = true };
            var sinMarcar = new PedidoVentaDTO { servirJunto = false };

            Assert.IsNull(ModosServicio.Normalizar(marcado));
            Assert.IsNull(ModosServicio.Normalizar(sinMarcar));

            Assert.AreEqual(ModosServicio.TODO_JUNTO, marcado.modoServicio);
            Assert.IsTrue(marcado.servirJunto);
            Assert.AreEqual(ModosServicio.SEGUN_VAYA_ENTRANDO, sinMarcar.modoServicio);
            Assert.IsFalse(sinMarcar.servirJunto);
        }

        [TestMethod]
        public void Normalizar_ConModo_ServirJuntoPasaASerSuDerivado()
        {
            // El cliente manda modo 4 con la casilla marcada (incoherente): gana el modo
            var modo4 = new PedidoVentaDTO { servirJunto = true, modoServicio = 4 };
            var modo1 = new PedidoVentaDTO { servirJunto = false, modoServicio = 1 };

            Assert.IsNull(ModosServicio.Normalizar(modo4));
            Assert.IsNull(ModosServicio.Normalizar(modo1));

            Assert.IsFalse(modo4.servirJunto, "Los validadores de #220/#470 y el picking leen servirJunto: solo el modo 1 es 'todo junto'");
            Assert.IsTrue(modo1.servirJunto);
        }

        [TestMethod]
        public void Normalizar_Modo3_DisponibleDesdeElSlice2_NoEsTodoJunto()
        {
            var pedido = new PedidoVentaDTO { servirJunto = true, modoServicio = 3 };

            Assert.IsNull(ModosServicio.Normalizar(pedido));
            Assert.IsFalse(pedido.servirJunto, "El 3 puede servir parcialmente cuando ya no queda nada que traer");
        }

        [TestMethod]
        public void Normalizar_ModoFueraDeRango_Error()
        {
            Assert.IsNotNull(ModosServicio.Normalizar(new PedidoVentaDTO { modoServicio = 0 }));
            Assert.IsNotNull(ModosServicio.Normalizar(new PedidoVentaDTO { modoServicio = 5 }));
            Assert.IsNull(ModosServicio.Normalizar(null));
        }
    }
}
