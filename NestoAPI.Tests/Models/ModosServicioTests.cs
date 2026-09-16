using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Infraestructure.ValidadoresServirJunto;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using System.Collections.Generic;
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

        // Valores por defecto (Carlos, 16/09/26): un pedido que no informa el modo NACE en el 3 («tras
        // reponer de tiendas»), no en el ServirJunto de la ficha del cliente: una referencia agotada o
        // anulada dejaba pedidos «todo junto» sin servir nunca. El parámetro permite excepciones.

        [TestMethod]
        public void NormalizarAlCrear_SinModo_NaceEnElPorDefecto_AunqueServirJuntoVengaMarcado()
        {
            var marcado = new PedidoVentaDTO { servirJunto = true };
            var sinMarcar = new PedidoVentaDTO { servirJunto = false };

            Assert.IsNull(ModosServicio.NormalizarAlCrear(marcado, ModosServicio.POR_DEFECTO));
            Assert.IsNull(ModosServicio.NormalizarAlCrear(sinMarcar, ModosServicio.POR_DEFECTO));

            Assert.AreEqual(ModosServicio.TRAS_REPONER_DE_TIENDAS, marcado.modoServicio);
            Assert.IsFalse(marcado.servirJunto, "El ServirJunto de la ficha no se arrastra al pedido");
            Assert.AreEqual(ModosServicio.TRAS_REPONER_DE_TIENDAS, sinMarcar.modoServicio);
        }

        [TestMethod]
        public void NormalizarAlCrear_ConModoInformado_SeRespetaElDelCliente()
        {
            var modo1 = new PedidoVentaDTO { servirJunto = false, modoServicio = 1 };
            var modo4 = new PedidoVentaDTO { servirJunto = true, modoServicio = 4 };

            Assert.IsNull(ModosServicio.NormalizarAlCrear(modo1, ModosServicio.POR_DEFECTO));
            Assert.IsNull(ModosServicio.NormalizarAlCrear(modo4, ModosServicio.POR_DEFECTO));

            Assert.AreEqual(ModosServicio.TODO_JUNTO, modo1.modoServicio);
            Assert.IsTrue(modo1.servirJunto);
            Assert.AreEqual(ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, modo4.modoServicio);
            Assert.IsFalse(modo4.servirJunto);
        }

        [TestMethod]
        public void NormalizarAlCrear_ElParametroPermiteOtroPorDefecto()
        {
            var pedido = new PedidoVentaDTO { servirJunto = false };

            Assert.IsNull(ModosServicio.NormalizarAlCrear(pedido, ModosServicio.TODO_JUNTO));

            Assert.AreEqual(ModosServicio.TODO_JUNTO, pedido.modoServicio);
            Assert.IsTrue(pedido.servirJunto);
        }

        [TestMethod]
        public void ParsearPorDefecto_ValorDelParametro_OElTresSiFaltaONoVale()
        {
            Assert.AreEqual((byte)2, ModosServicio.ParsearPorDefecto("2"));
            Assert.AreEqual((byte)4, ModosServicio.ParsearPorDefecto(" 4 "));
            Assert.AreEqual(ModosServicio.POR_DEFECTO, ModosServicio.ParsearPorDefecto(null));
            Assert.AreEqual(ModosServicio.POR_DEFECTO, ModosServicio.ParsearPorDefecto(""));
            Assert.AreEqual(ModosServicio.POR_DEFECTO, ModosServicio.ParsearPorDefecto("9"));
            Assert.AreEqual(ModosServicio.POR_DEFECTO, ModosServicio.ParsearPorDefecto("tres"));
        }

        // NestoAPI#220/#470 (16/09/26): los mensajes de denegación nombran el modo elegido, no la casilla.

        [TestMethod]
        public void NombreDestino_NombraElModoElegido_YSinModoAsumeSegunVayaEntrando()
        {
            Assert.AreEqual("Ahora lo que hay, el resto de una vez", ModosServicio.NombreDestino(4));
            Assert.AreEqual("Tras reponer de tiendas", ModosServicio.NombreDestino(3));
            Assert.AreEqual("Según vaya entrando", ModosServicio.NombreDestino(2));
            Assert.AreEqual("Según vaya entrando", ModosServicio.NombreDestino(null), "NestoApp solo manda el bool");
            Assert.AreEqual("Según vaya entrando", ModosServicio.NombreDestino(1), "Pasar a todo junto nunca se deniega: no es un destino");
            Assert.AreEqual("Según vaya entrando", ModosServicio.NombreDestino(9));
        }

        [TestMethod]
        public void MensajeDeMaterialPromocional_NombraElModoYNoLaCasilla()
        {
            var problematicos = new List<ProductoSinStockDTO>
            {
                new ProductoSinStockDTO { ProductoId = "45461", ProductoNombre = "SENSITIVE MUESTRA" }
            };

            string mensaje = ValidadorMaterialPromocional.ConstruirMensaje(problematicos, 4);

            StringAssert.Contains(mensaje, "pasar el pedido a «Ahora lo que hay, el resto de una vez»");
            StringAssert.Contains(mensaje, "45461");
            Assert.IsFalse(mensaje.Contains("Servir junto"), "La casilla ya no existe en Nesto");
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
