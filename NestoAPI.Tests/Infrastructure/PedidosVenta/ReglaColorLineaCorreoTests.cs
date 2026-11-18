using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Pedido 925901 (18/11/26, decisión de Carlos): el color de cada línea del correo del pedido sale del MISMO motor que el pie
    /// «Entrega a la agencia… Por qué:» (NestoAPI#606). <see cref="GestorStocks.ColorStock(string, string, int)"/> queda de respaldo.
    /// </summary>
    [TestClass]
    public class ReglaColorLineaCorreoTests
    {
        private static readonly DateTime JUEVES_26 = new DateTime(2026, 11, 26);

        private static PartidaFechaEntregaAgenciaDTO Partida(string origen, string producto = "44784", int unidades = 4)
            => new PartidaFechaEntregaAgenciaDTO { Origen = origen, Producto = producto, Unidades = unidades, PuedeSalirEl = JUEVES_26 };

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, string almacen = Constantes.Almacenes.ALGETE,
            short estado = Constantes.EstadosLineaVenta.PENDIENTE)
            => new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                Cantidad = cantidad,
                almacen = almacen,
                estado = estado,
                texto = "PRODUCTO " + producto
            };

        // ---------- La regla pura ----------

        [TestMethod]
        public void DePartida_PickingYAlgete_EnAlgete()
        {
            Assert.AreEqual(SituacionStockLineaCorreo.EnAlgete, ReglaColorLineaCorreo.DePartida(Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_PICKING)));
            Assert.AreEqual(SituacionStockLineaCorreo.EnAlgete, ReglaColorLineaCorreo.DePartida(Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE)));
        }

        [TestMethod]
        public void DePartida_KitYPackConLosComponentesEnAlgete_SeMontaEnAlgete()
        {
            Assert.AreEqual(SituacionStockLineaCorreo.SeMontaEnAlgete, ReglaColorLineaCorreo.DePartida(Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT)));
            Assert.AreEqual(SituacionStockLineaCorreo.SeMontaEnAlgete, ReglaColorLineaCorreo.DePartida(Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE)));
        }

        [TestMethod]
        public void DePartida_TiendaYEnCamino_TieneQueVenir()
        {
            Assert.AreEqual(SituacionStockLineaCorreo.TieneQueVenir, ReglaColorLineaCorreo.DePartida(Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA)));
            Assert.AreEqual(SituacionStockLineaCorreo.TieneQueVenir, ReglaColorLineaCorreo.DePartida(Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO)));
        }

        [TestMethod]
        public void DePartida_ProveedorYSinStock_FaltaStock()
        {
            Assert.AreEqual(SituacionStockLineaCorreo.FaltaStock, ReglaColorLineaCorreo.DePartida(Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR)));
            Assert.AreEqual(SituacionStockLineaCorreo.FaltaStock, ReglaColorLineaCorreo.DePartida(Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK)));
        }

        [TestMethod]
        public void DePartida_PackQueEsperaUnComponente_ComoElOrigenDelComponente()
        {
            PartidaFechaEntregaAgenciaDTO Pack(string origenComponente) => new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE,
                Producto = "44376",
                Unidades = 1,
                ComponenteQueFalta = "40010",
                OrigenComponente = origenComponente
            };
            Assert.AreEqual(SituacionStockLineaCorreo.TieneQueVenir, ReglaColorLineaCorreo.DePartida(Pack(PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA)));
            Assert.AreEqual(SituacionStockLineaCorreo.TieneQueVenir, ReglaColorLineaCorreo.DePartida(Pack(PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO)));
            Assert.AreEqual(SituacionStockLineaCorreo.FaltaStock, ReglaColorLineaCorreo.DePartida(Pack(PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR)));
            Assert.AreEqual(SituacionStockLineaCorreo.FaltaStock, ReglaColorLineaCorreo.DePartida(Pack(PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK)));
            Assert.AreEqual(SituacionStockLineaCorreo.FaltaStock, ReglaColorLineaCorreo.DePartida(Pack(null)), "Sin saber de dónde llega: falta");
        }

        [TestMethod]
        public void DePartida_TodosLosOrigenesDelMotorTienenSituacion()
        {
            foreach (string origen in PartidaFechaEntregaAgenciaDTO.ORDEN_ORIGENES)
            {
                Assert.IsTrue(ReglaColorLineaCorreo.DePartida(Partida(origen)).HasValue, $"El origen {origen} se queda sin color");
            }
        }

        [TestMethod]
        public void DePartida_OrigenDesconocido_SinSituacion()
        {
            Assert.IsNull(ReglaColorLineaCorreo.DePartida(Partida("Marte")));
            Assert.IsNull(ReglaColorLineaCorreo.DePartida(null));
        }

        [TestMethod]
        public void DePartidas_UnaLineaRepartida_TomaElPeorCaso()
        {
            var parteEnAlgeteParteDeProveedor = new[]
            {
                Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, unidades: 2),
                Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, unidades: 2)
            };
            var parteEnAlgeteParteDeTienda = new[]
            {
                Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, unidades: 2),
                Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, unidades: 2)
            };
            Assert.AreEqual(SituacionStockLineaCorreo.FaltaStock, ReglaColorLineaCorreo.DePartidas(parteEnAlgeteParteDeProveedor));
            Assert.AreEqual(SituacionStockLineaCorreo.TieneQueVenir, ReglaColorLineaCorreo.DePartidas(parteEnAlgeteParteDeTienda));
        }

        [TestMethod]
        public void DePartidas_SinPartidasOConUnOrigenDesconocido_SinSituacion()
        {
            Assert.IsNull(ReglaColorLineaCorreo.DePartidas(new PartidaFechaEntregaAgenciaDTO[0]));
            Assert.IsNull(ReglaColorLineaCorreo.DePartidas(null));
            Assert.IsNull(ReglaColorLineaCorreo.DePartidas(new[] { Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE), Partida("Marte") }));
        }

        [TestMethod]
        public void Color_LosDeSiempre()
        {
            Assert.AreEqual("green", ReglaColorLineaCorreo.Color(SituacionStockLineaCorreo.EnAlgete));
            Assert.AreEqual("DeepPink", ReglaColorLineaCorreo.Color(SituacionStockLineaCorreo.SeMontaEnAlgete));
            Assert.AreEqual("DeepPink", ReglaColorLineaCorreo.Color(SituacionStockLineaCorreo.TieneQueVenir));
            Assert.AreEqual("red", ReglaColorLineaCorreo.Color(SituacionStockLineaCorreo.FaltaStock));
        }

        [TestMethod]
        public void LaCalculaElMotor_SoloLineasPendientesDeProductoDeAlgeteDeUnPedido()
        {
            Assert.IsTrue(ReglaColorLineaCorreo.LaCalculaElMotor(Linea("44784", 4), false));
            Assert.IsTrue(ReglaColorLineaCorreo.LaCalculaElMotor(Linea("44784", 4, estado: Constantes.EstadosLineaVenta.EN_CURSO), false));
            Assert.IsTrue(ReglaColorLineaCorreo.LaCalculaElMotor(Linea("44784", 4, almacen: "ALG       "), false), "El almacén viene con espacios");
            Assert.IsFalse(ReglaColorLineaCorreo.LaCalculaElMotor(Linea("44784", 4), true), "Presupuesto");
            Assert.IsFalse(ReglaColorLineaCorreo.LaCalculaElMotor(Linea("44784", 4, estado: Constantes.EstadosLineaVenta.PRESUPUESTO), false));
            Assert.IsFalse(ReglaColorLineaCorreo.LaCalculaElMotor(Linea("44784", 4, estado: Constantes.EstadosLineaVenta.NOTA_ENTREGA), false));
            Assert.IsFalse(ReglaColorLineaCorreo.LaCalculaElMotor(Linea("44784", 4, estado: Constantes.EstadosLineaVenta.ALBARAN), false));
            Assert.IsFalse(ReglaColorLineaCorreo.LaCalculaElMotor(Linea("44784", 4, almacen: Constantes.Almacenes.REINA), false));
            Assert.IsFalse(ReglaColorLineaCorreo.LaCalculaElMotor(Linea("44784", 0), false));
            var texto = Linea("", 1);
            texto.tipoLinea = Constantes.TiposLineaVenta.TEXTO;
            Assert.IsFalse(ReglaColorLineaCorreo.LaCalculaElMotor(texto, false));
        }

        [TestMethod]
        public void DeLinea_ProductoQueElMotorNoTrae_SinSituacion()
        {
            var fecha = new FechaEntregaAgenciaDTO { Desglose = { Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, "44784") } };
            Assert.AreEqual(SituacionStockLineaCorreo.EnAlgete, ReglaColorLineaCorreo.DeLinea(Linea("44784  ", 4), fecha, false), "El código viene con espacios");
            Assert.IsNull(ReglaColorLineaCorreo.DeLinea(Linea("99999", 4), fecha, false));
            Assert.IsNull(ReglaColorLineaCorreo.DeLinea(Linea("44784", 4), null, false));
        }

        [TestMethod]
        public void ReservasEnTiendas_SoloLaReposicionDeTiendaAgrupadaPorTienda()
        {
            var partidas = new[]
            {
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Tienda = Constantes.Almacenes.ALCOBENDAS, Unidades = 1 },
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Tienda = Constantes.Almacenes.REINA, Unidades = 2 },
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Tienda = Constantes.Almacenes.REINA, Unidades = 1 },
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO, Unidades = 5 },
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, Unidades = 5 }
            };
            List<KeyValuePair<string, int>> reservas = ReglaColorLineaCorreo.ReservasEnTiendas(partidas);
            Assert.AreEqual(2, reservas.Count);
            Assert.AreEqual(3, reservas.Single(r => r.Key == Constantes.Almacenes.REINA).Value);
            Assert.AreEqual(1, reservas.Single(r => r.Key == Constantes.Almacenes.ALCOBENDAS).Value);
        }

        // ---------- El correo (GestorPresupuestos) ----------

        private static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas) => new PedidoVentaDTO
        {
            empresa = "1",
            numero = 925901,
            cliente = "15191",
            contacto = "0",
            vendedor = "V01",
            EsPresupuesto = false,
            Lineas = lineas.ToList()
        };

        private static GestorPresupuestos Gestor(PedidoVentaDTO pedido, IServicioGestorStocks servicio, FechaEntregaAgenciaDTO fecha)
            => new GestorPresupuestos(pedido, null, A.Fake<NVEntities>(), A.Fake<IServicioVendedores>(), servicio, A.Fake<IServicioCorreoElectronico>())
            {
                FechaEntregaAgencia = fecha
            };

        /// <summary>
        /// El caso 925901: 4 de 44784 (stock ALG 10) y 4 de 44786 (stock ALG 4). El 929726 tiene 7 y 1 a 31/12/2099. ColorStock suma los
        /// dos pedidos (11 &gt; 10, 5 &gt; 4) y pinta rojo; el motor (el pie) los da con stock en Algete para este pedido.
        /// </summary>
        private static IServicioGestorStocks StocksDel925901()
        {
            var servicio = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.Stock("44784", Constantes.Almacenes.ALGETE)).Returns(10);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("44784", Constantes.Almacenes.ALGETE)).Returns(11);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("44784")).Returns(-1);
            A.CallTo(() => servicio.Stock("44786", Constantes.Almacenes.ALGETE)).Returns(4);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("44786", Constantes.Almacenes.ALGETE)).Returns(5);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("44786")).Returns(-1);
            return servicio;
        }

        private static FechaEntregaAgenciaDTO MotorDel925901() => new FechaEntregaAgenciaDTO
        {
            PrimeraEntrega = JUEVES_26,
            EntregaCompleta = JUEVES_26,
            FechaEntregaAgencia = JUEVES_26,
            Aplica = FechaEntregaAgenciaDTO.APLICA_PRIMERA,
            Desglose =
            {
                Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, "44784", 4),
                Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, "44786", 4)
            }
        };

        [TestMethod]
        public void Correo925901_OtroPedidoA2099NoPoneEnRojoUnaLineaConStockParaEstePedido()
        {
            var servicio = StocksDel925901();
            var pedido = Pedido(Linea("44784", 4), Linea("44786", 4));
            var gestorStocks = new GestorStocks(servicio);
            Assert.AreEqual("red", gestorStocks.ColorStock("44784", Constantes.Almacenes.ALGETE), "Así salía: contradecía al pie");

            GestorPresupuestos gestor = Gestor(pedido, servicio, MotorDel925901());
            List<GestorPresupuestos.StockLineaCorreo> lineas = pedido.Lineas.Select(l => gestor.StockDeLinea(l, gestorStocks)).ToList();

            Assert.IsTrue(lineas.All(l => l.Color == "green"));
            Assert.IsTrue(lineas.All(l => l.DelMotor == SituacionStockLineaCorreo.EnAlgete));
            var resumen = GestorPresupuestos.ResumenStock(lineas);
            Assert.IsFalse(resumen.faltaStockDeAlgo);
            Assert.IsFalse(resumen.tieneQueVenirAlgunProducto);

            string pie = GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(MotorDel925901(), false, false, pedido.Lineas, 6);
            StringAssert.Contains(pie, "Todo el pedido: con stock en Algete");
        }

        [TestMethod]
        public void Correo925901_NiFinanciacionARevisarNiAtencionPorUnRojoFalso()
        {
            // NestoAPI#507: en «Tras reponer de tiendas» solo trocea si falta stock de algo. Con el color de antes (rojo) troceaba.
            var servicio = StocksDel925901();
            var pedido = Pedido(Linea("44784", 4), Linea("44786", 4));
            GestorPresupuestos gestor = Gestor(pedido, servicio, MotorDel925901());
            var resumen = GestorPresupuestos.ResumenStock(pedido.Lineas.Select(l => gestor.StockDeLinea(l, new GestorStocks(servicio))));

            Assert.IsFalse(GestorPresupuestos.SeTroceaLaFacturacion(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, resumen.faltaStockDeAlgo));
            var plazo = new PlazoPago { Nº_Plazos = 2, Financiacion = 45 };
            Assert.IsFalse(GestorPresupuestos.EsFinanciacionARevisar(1000m, plazo, Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
                Constantes.Pedidos.ModosFacturacion.Efectivo(null, false), false, resumen.faltaStockDeAlgo, false));
        }

        [TestMethod]
        public void LineaQueDeVerdadNoTieneStock_SigueRojaYElPieLoDice()
        {
            var servicio = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.Stock("40010", Constantes.Almacenes.ALGETE)).Returns(10); // ColorStock diría verde: manda el motor
            var pedido = Pedido(Linea("44784", 4), Linea("40010", 2));
            var motor = new FechaEntregaAgenciaDTO
            {
                Aplica = FechaEntregaAgenciaDTO.APLICA_PRIMERA,
                FechaEntregaAgencia = JUEVES_26,
                Desglose =
                {
                    Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, "44784", 4),
                    new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, Producto = "40010", Unidades = 2, MotivoSinFecha = "no hay stock ni fecha de llegada" }
                }
            };
            GestorPresupuestos gestor = Gestor(pedido, servicio, motor);
            var lineas = pedido.Lineas.Select(l => gestor.StockDeLinea(l, new GestorStocks(servicio))).ToList();

            Assert.AreEqual("green", lineas[0].Color);
            Assert.AreEqual("red", lineas[1].Color);
            Assert.IsTrue(GestorPresupuestos.ResumenStock(lineas).faltaStockDeAlgo);
            Assert.IsTrue(GestorPresupuestos.SeTroceaLaFacturacion(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, GestorPresupuestos.ResumenStock(lineas).faltaStockDeAlgo));
            StringAssert.Contains(GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(motor, false, false, pedido.Lineas, 6), "Sin fecha");
        }

        [TestMethod]
        public void LineaRepartidaParteEnAlgeteParteDeProveedor_Roja()
        {
            var servicio = A.Fake<IServicioGestorStocks>();
            var pedido = Pedido(Linea("44784", 6));
            var motor = new FechaEntregaAgenciaDTO
            {
                Desglose =
                {
                    Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, "44784", 4),
                    new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, Producto = "44784", Unidades = 2, PedidoProveedor = 220923 }
                }
            };
            var linea = Gestor(pedido, servicio, motor).StockDeLinea(pedido.Lineas.Single(), new GestorStocks(servicio));
            Assert.AreEqual("red", linea.Color);
            Assert.IsTrue(linea.FaltaStock);
        }

        [TestMethod]
        public void SinMotor_RespaldoColorStock()
        {
            var servicio = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.Stock("44784", Constantes.Almacenes.ALGETE)).Returns(3);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("44784", Constantes.Almacenes.ALGETE)).Returns(4);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("44784")).Returns(5);
            var pedido = Pedido(Linea("44784", 4));

            var linea = Gestor(pedido, servicio, null).StockDeLinea(pedido.Lineas.Single(), new GestorStocks(servicio));

            Assert.AreEqual("DeepPink", linea.Color);
            Assert.IsNull(linea.DelMotor);
            Assert.IsTrue(linea.TieneQueVenir);
            Assert.IsFalse(linea.FaltaStock);
        }

        [TestMethod]
        public void MotorSinEseProducto_RespaldoColorStockSoloParaEsaLinea()
        {
            var servicio = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.Stock("99999", Constantes.Almacenes.ALGETE)).Returns(0);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("99999", Constantes.Almacenes.ALGETE)).Returns(1);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("99999")).Returns(-1);
            var pedido = Pedido(Linea("44784", 4), Linea("99999", 1));
            GestorPresupuestos gestor = Gestor(pedido, servicio, MotorDel925901());

            var lineas = pedido.Lineas.Select(l => gestor.StockDeLinea(l, new GestorStocks(servicio))).ToList();

            Assert.AreEqual(SituacionStockLineaCorreo.EnAlgete, lineas[0].DelMotor);
            Assert.IsNull(lineas[1].DelMotor);
            Assert.AreEqual("red", lineas[1].Color);
        }

        [TestMethod]
        public void LineaDeTienda_RespaldoColorStock()
        {
            var servicio = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.Stock("44784", Constantes.Almacenes.REINA)).Returns(10);
            var pedido = Pedido(Linea("44784", 4, almacen: Constantes.Almacenes.REINA));
            var linea = Gestor(pedido, servicio, MotorDel925901()).StockDeLinea(pedido.Lineas.Single(), new GestorStocks(servicio));
            Assert.IsNull(linea.DelMotor);
            Assert.AreEqual("green", linea.Color);
        }

        [TestMethod]
        public void Presupuesto_RespaldoColorStockConSuCantidad()
        {
            // Presupuesto 927312: las unidades de un presupuesto no están en pendientes; aunque llegara un cálculo, no lo hay para él
            var servicio = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.Stock("34100", Constantes.Almacenes.ALGETE)).Returns(0);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("34100", Constantes.Almacenes.ALGETE)).Returns(0);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("34100")).Returns(0);
            var pedido = Pedido(Linea("34100", 1, estado: Constantes.EstadosLineaVenta.PRESUPUESTO));
            pedido.EsPresupuesto = true;
            var motor = new FechaEntregaAgenciaDTO { Desglose = { Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, "34100", 1) } };

            var linea = Gestor(pedido, servicio, motor).StockDeLinea(pedido.Lineas.Single(), new GestorStocks(servicio));

            Assert.IsNull(linea.DelMotor);
            Assert.AreEqual("red", linea.Color);
            Assert.IsTrue(GestorPresupuestos.ResumenStock(new[] { linea }).faltaStockDeAlgo);
        }

        [TestMethod]
        public void LineaDeTexto_Negra()
        {
            var texto = new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.TEXTO, Producto = "", texto = "PORTES" };
            var pedido = Pedido(texto);
            var linea = Gestor(pedido, A.Fake<IServicioGestorStocks>(), MotorDel925901()).StockDeLinea(texto, new GestorStocks(A.Fake<IServicioGestorStocks>()));
            Assert.AreEqual("black", linea.Color);
            Assert.IsFalse(linea.TieneQueVenir || linea.FaltaStock);
        }

        [TestMethod]
        public void DeUnaTienda_TieneQueVenirYAtencionEnSegunVayaEntrando()
        {
            var servicio = A.Fake<IServicioGestorStocks>();
            var pedido = Pedido(Linea("44784", 4));
            pedido.modoServicio = Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO;
            var motor = new FechaEntregaAgenciaDTO
            {
                Desglose = { new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Tienda = Constantes.Almacenes.REINA, Producto = "44784", Unidades = 4 } }
            };
            var lineas = pedido.Lineas.Select(l => Gestor(pedido, servicio, motor).StockDeLinea(l, new GestorStocks(servicio))).ToList();
            var resumen = GestorPresupuestos.ResumenStock(lineas);

            Assert.AreEqual("DeepPink", lineas.Single().Color);
            Assert.IsTrue(resumen.tieneQueVenirAlgunProducto);
            StringAssert.Contains(GestorPresupuestos.GenerarHtmlModoServicio(pedido, resumen.faltaStockDeAlgo, resumen.tieneQueVenirAlgunProducto, 6), "¡¡¡ ATENCIÓN !!!");
        }

        [TestMethod]
        public void KitQueSeDesmontaEnAlgete_RosaPeroNiTieneQueVenirNiAtencionNiReserva()
        {
            var servicio = A.Fake<IServicioGestorStocks>();
            var pedido = Pedido(Linea("44784", 4));
            pedido.modoServicio = Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO;
            var motor = new FechaEntregaAgenciaDTO
            {
                Desglose = { new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT, Kit = "44700", Producto = "44784", Unidades = 4 } }
            };
            GestorPresupuestos gestor = Gestor(pedido, servicio, motor);
            var lineas = pedido.Lineas.Select(l => gestor.StockDeLinea(l, new GestorStocks(servicio))).ToList();
            var resumen = GestorPresupuestos.ResumenStock(lineas);

            Assert.AreEqual("DeepPink", lineas.Single().Color);
            Assert.IsFalse(resumen.tieneQueVenirAlgunProducto);
            Assert.IsFalse(resumen.faltaStockDeAlgo);
            Assert.IsFalse(GestorPresupuestos.GenerarHtmlModoServicio(pedido, resumen.faltaStockDeAlgo, resumen.tieneQueVenirAlgunProducto, 6).Contains("ATENCIÓN"));
            Assert.AreEqual(0, ObtenerAlmacenesConReservas(gestor).Count);
        }

        [TestMethod]
        public void ReservasDesdeMotor_LoQueTraeCadaTienda()
        {
            // ColorStock diría verde y CalcularReservasAlmacenes no reservaría nada: manda lo que dice el pie
            var servicio = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.Stock("44784", Constantes.Almacenes.ALGETE)).Returns(100);
            var linea = Linea("44784", 4);
            var pedido = Pedido(linea);
            var motor = new FechaEntregaAgenciaDTO
            {
                Desglose =
                {
                    Partida(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, "44784", 1),
                    new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Tienda = Constantes.Almacenes.REINA, Producto = "44784", Unidades = 3 }
                }
            };
            GestorPresupuestos gestor = Gestor(pedido, servicio, motor);
            var almacenesConReservas = new Dictionary<string, HashSet<string>>();

            string texto = gestor.CalcularReservasDesdeMotor(linea, new GestorStocks(servicio), servicio, almacenesConReservas);

            Assert.AreEqual("<strong>REI: 3 uds</strong>", texto);
            Assert.IsTrue(almacenesConReservas.ContainsKey(Constantes.Almacenes.REINA));
            CollectionAssert.AreEquivalent(new[] { Constantes.Almacenes.REINA }, ObtenerAlmacenesConReservas(gestor).ToArray());
        }

        [TestMethod]
        public void ReservasDesdeMotor_YaDeCamino_SinReserva()
        {
            var servicio = A.Fake<IServicioGestorStocks>();
            var linea = Linea("44784", 4);
            var pedido = Pedido(linea);
            var motor = new FechaEntregaAgenciaDTO
            {
                Desglose = { new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO, Producto = "44784", Unidades = 4 } }
            };
            GestorPresupuestos gestor = Gestor(pedido, servicio, motor);

            Assert.AreEqual(string.Empty, gestor.CalcularReservasDesdeMotor(linea, new GestorStocks(servicio), servicio, new Dictionary<string, HashSet<string>>()));
            Assert.AreEqual(0, ObtenerAlmacenesConReservas(gestor).Count);
        }

        [TestMethod]
        public void ObtenerAlmacenesConReservas_925901_SinCopiaATiendasPorUnRosaFalso()
        {
            // ColorStock (sin cantidad) daría rosa y metería a Reina en copia; el motor dice que sale de Algete
            var servicio = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.Stock("44784", Constantes.Almacenes.ALGETE)).Returns(10);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("44784", Constantes.Almacenes.ALGETE)).Returns(11);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("44784")).Returns(5);
            A.CallTo(() => servicio.Stock("44784", Constantes.Almacenes.REINA)).Returns(5);
            var pedido = Pedido(Linea("44784", 4));

            Assert.AreEqual(1, ObtenerAlmacenesConReservas(Gestor(pedido, servicio, null)).Count, "Sin motor, como siempre");
            Assert.AreEqual(0, ObtenerAlmacenesConReservas(Gestor(pedido, servicio, MotorDel925901())).Count);
        }

        private static HashSet<string> ObtenerAlmacenesConReservas(GestorPresupuestos gestor)
        {
            var metodo = typeof(GestorPresupuestos).GetMethod("ObtenerAlmacenesConReservas",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (HashSet<string>)metodo.Invoke(gestor, null);
        }
    }
}
