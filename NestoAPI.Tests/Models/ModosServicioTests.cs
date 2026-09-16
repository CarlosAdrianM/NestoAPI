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

        // Regla (Carlos, 16/09/26): ServirJunto marcado SIEMPRE es todo junto; desmarcado, manda el modo
        // parcial guardado (3 o 4) o el 2. El Nesto viejo (VB6) escribe ServirJunto en CabPedidoVta sin
        // conocer el modo (pedidos 926269 y 926309 el 16/09: modo 1 y ServirJunto false), y NestoApp
        // solo manda el bool.

        [TestMethod]
        public void Efectivo_ServirJuntoMarcado_SiempreEsTodoJunto_AunqueElModoDigaOtraCosa()
        {
            Assert.AreEqual(ModosServicio.TODO_JUNTO, ModosServicio.Efectivo(4, servirJunto: true));
            Assert.AreEqual(ModosServicio.TODO_JUNTO, ModosServicio.Efectivo(3, servirJunto: true));
            Assert.AreEqual(ModosServicio.TODO_JUNTO, ModosServicio.Efectivo(2, servirJunto: true));
            Assert.AreEqual(ModosServicio.TODO_JUNTO, ModosServicio.Efectivo(9, servirJunto: true));
        }

        [TestMethod]
        public void Efectivo_ServirJuntoDesmarcado_MandaElModoParcialGuardadoOElDos()
        {
            Assert.AreEqual(ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, ModosServicio.Efectivo(4, servirJunto: false));
            Assert.AreEqual(ModosServicio.TRAS_REPONER_DE_TIENDAS, ModosServicio.Efectivo(3, servirJunto: false));
            Assert.AreEqual(ModosServicio.SEGUN_VAYA_ENTRANDO, ModosServicio.Efectivo(2, servirJunto: false));
            // Modo 1 con ServirJunto desmarcado = el Nesto viejo lo desmarcó: se sirve según entre
            Assert.AreEqual(ModosServicio.SEGUN_VAYA_ENTRANDO, ModosServicio.Efectivo(1, servirJunto: false));
            // Un valor fuera de rango en BD no puede tumbar el picking
            Assert.AreEqual(ModosServicio.SEGUN_VAYA_ENTRANDO, ModosServicio.Efectivo(9, servirJunto: false));
        }

        [TestMethod]
        public void Normalizar_ClienteQueNoMandaModo_ConservaElModoParcialGuardado()
        {
            // NestoApp modifica un pedido que Nesto puso en modo 4: el PUT llega sin modo y con
            // servirJunto=false. Antes se pisaba con un 2; ahora se conserva el 4.
            var sinModo = new PedidoVentaDTO { servirJunto = false };
            Assert.IsNull(ModosServicio.Normalizar(sinModo, modoAlmacenado: 4));
            Assert.AreEqual(ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, sinModo.modoServicio);

            var enModo3 = new PedidoVentaDTO { servirJunto = false };
            Assert.IsNull(ModosServicio.Normalizar(enModo3, modoAlmacenado: 3));
            Assert.AreEqual(ModosServicio.TRAS_REPONER_DE_TIENDAS, enModo3.modoServicio);

            // Si marca servirJunto, es que quiere todo junto: gana el bool
            var marcado = new PedidoVentaDTO { servirJunto = true };
            Assert.IsNull(ModosServicio.Normalizar(marcado, modoAlmacenado: 4));
            Assert.AreEqual(ModosServicio.TODO_JUNTO, marcado.modoServicio);

            // Con 1 o 2 guardados no hay nada que conservar: el bool lo dice todo
            var guardadoEn1 = new PedidoVentaDTO { servirJunto = false };
            Assert.IsNull(ModosServicio.Normalizar(guardadoEn1, modoAlmacenado: 1));
            Assert.AreEqual(ModosServicio.SEGUN_VAYA_ENTRANDO, guardadoEn1.modoServicio);
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
