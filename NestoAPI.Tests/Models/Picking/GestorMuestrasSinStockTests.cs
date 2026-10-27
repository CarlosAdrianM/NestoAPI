using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Servicio = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// 27/10/26 (Carlos, caso David): las muestras (MMP) sin stock no retienen el pedido ni se quedan pendientes. Si lo
    /// demás sale, se quitan del pedido y el pedido sale. Generaliza NestoAPI#529 solo para las muestras; los demás regalos
    /// (base 0 que no son MMP) siguen esperando.
    /// </summary>
    [TestClass]
    public class GestorMuestrasSinStockTests
    {
        private string valorInterruptor = "true";

        [TestInitialize]
        public void Preparar()
        {
            valorInterruptor = "true";
            GestorMuestrasSinStock.LeerAjuste = clave => clave == GestorMuestrasSinStock.CLAVE ? valorInterruptor : null;
        }

        [TestCleanup]
        public void Limpiar()
        {
            GestorMuestrasSinStock.LeerAjuste = null;
        }

        private static PedidoPicking Pedido(byte modoServicio, params LineaPedidoPicking[] lineas)
        {
            return new PedidoPicking(A.Fake<IRellenadorPrepagosService>())
            {
                Id = 925825,
                Empresa = "1",
                Cliente = "29548",
                Usuario = "NUEVAVISION\\Javier",
                Ruta = "FW",
                Iva = "G",
                PlazosPago = "CONTADO",
                ModoServicio = modoServicio,
                ServirJunto = modoServicio == Servicio.TODO_JUNTO,
                Lineas = lineas.ToList()
            };
        }

        private static LineaPedidoPicking DePago(int id, int cantidad, int reservada, decimal baseImponible = 10)
        {
            return new LineaPedidoPicking
            {
                Id = id,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "P" + id,
                Almacen = Constantes.Almacenes.ALGETE,
                Cantidad = cantidad,
                CantidadReservada = reservada,
                BaseImponible = baseImponible,
                Total = baseImponible * 1.21M,
                FechaEntrega = DateTime.Today
            };
        }

        /// <summary>Muestra: subgrupo MMP, base 0 (100 % de descuento).</summary>
        private static LineaPedidoPicking Muestra(int id, int cantidad, int reservada)
        {
            LineaPedidoPicking linea = DePago(id, cantidad, reservada, 0);
            linea.Producto = "M" + id;
            linea.EsMuestra = true;
            return linea;
        }

        /// <summary>Regalo que NO es muestra (Ganavisión, regalo por importe): base 0 sin MMP.</summary>
        private static LineaPedidoPicking Regalo(int id, int cantidad, int reservada)
        {
            LineaPedidoPicking linea = DePago(id, cantidad, reservada, 0);
            linea.Producto = "R" + id;
            return linea;
        }

        private static List<MuestraQuitadaPicking> Aplicar(PedidoPicking pedido)
        {
            return GestorMuestrasSinStock.Aplicar(pedido, GestorPicking.Decidir);
        }

        // ---------- Todo junto ----------

        [TestMethod]
        public void TodoJunto_RestoConStock_MuestraSinStock_SeQuitaYElPedidoSale()
        {
            LineaPedidoPicking muestra = Muestra(3, cantidad: 1, reservada: 0);
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6), DePago(2, 2, 2), muestra);

            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido), "Antes de la regla, la muestra retenía el pedido");
            Assert.AreEqual(MotivoNoSalePicking.TodoJuntoSinStock, pedido.MotivoNoSale);

            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(1, quitadas.Count);
            Assert.AreEqual(3, quitadas[0].IdLinea);
            Assert.AreEqual("M3", quitadas[0].Producto);
            Assert.AreEqual(1, quitadas[0].UnidadesQuitadas);
            Assert.AreEqual(0, quitadas[0].UnidadesQueSalen, "La línea se borra entera");
            Assert.IsFalse(pedido.Lineas.Contains(muestra), "La muestra ya no está en el pedido");
            Assert.AreEqual(2, pedido.Lineas.Count);
            Assert.AreSame(quitadas[0], pedido.MuestrasQuitadas.Single(), "Queda apuntado en el pedido para el rastro y los avisos");
            Assert.IsTrue(GestorPicking.DecidirSiSale(pedido), "Sin la muestra, el todo junto sale");
            Assert.AreEqual(MotivoNoSalePicking.Ninguno, pedido.MotivoNoSale);
        }

        [TestMethod]
        public void TodoJunto_MuestraConStock_NoSeToca()
        {
            LineaPedidoPicking muestra = Muestra(3, cantidad: 2, reservada: 2);
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6), muestra);

            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(0, quitadas.Count);
            Assert.IsTrue(pedido.Lineas.Contains(muestra));
            Assert.AreEqual(2, muestra.Cantidad);
            Assert.AreEqual(0, pedido.MuestrasQuitadas.Count);
            Assert.IsTrue(GestorPicking.DecidirSiSale(pedido));
        }

        [TestMethod]
        public void TodoJunto_TambienFaltaAlgoDePago_LaMuestraSeQuedaYElPedidoEsperaComoAntes()
        {
            LineaPedidoPicking muestra = Muestra(3, cantidad: 1, reservada: 0);
            LineaPedidoPicking sinStock = DePago(2, 2, 0);
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6), sinStock, muestra);
            List<LineaPedidoPicking> lineasAntes = pedido.Lineas.ToList();

            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(0, quitadas.Count, "Quitar la muestra no haría salir el pedido: se deja todo como estaba");
            CollectionAssert.AreEqual(lineasAntes, pedido.Lineas, "Mismas líneas y en el mismo orden");
            Assert.AreEqual(1, muestra.Cantidad);
            Assert.AreEqual(0, pedido.MuestrasQuitadas.Count);
            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.TodoJuntoSinStock, pedido.MotivoNoSale);
            CollectionAssert.AreEquivalent(new[] { sinStock, muestra }, pedido.LineasQueFaltanAlDecidir, "El motivo sigue nombrando la muestra");
        }

        [TestMethod]
        public void TodoJunto_SoloMuestrasSinStockYNadaMas_SeComportaComoAntes()
        {
            LineaPedidoPicking muestra = Muestra(1, cantidad: 1, reservada: 0);
            LineaPedidoPicking otraMuestra = Muestra(2, cantidad: 3, reservada: 3);
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, muestra, otraMuestra);

            Assert.IsFalse(GestorMuestrasSinStock.SeAplica(pedido), "No hay «resto» que servir: solo lleva muestras");
            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(0, quitadas.Count);
            Assert.AreEqual(2, pedido.Lineas.Count);
            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.TodoJuntoSinStock, pedido.MotivoNoSale);
        }

        [TestMethod]
        public void MuestraConParteDelStock_SeReduceALoQueHay()
        {
            LineaPedidoPicking muestra = Muestra(3, cantidad: 5, reservada: 2);
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6), muestra);

            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(1, quitadas.Count);
            Assert.AreEqual(3, quitadas[0].UnidadesQuitadas);
            Assert.AreEqual(2, quitadas[0].UnidadesQueSalen);
            Assert.IsTrue(pedido.Lineas.Contains(muestra), "La línea sigue, con lo que sí hay");
            Assert.AreEqual(2, muestra.Cantidad);
            Assert.AreEqual(2, muestra.CantidadReservada);
            Assert.IsTrue(GestorPicking.DecidirSiSale(pedido));
        }

        // ---------- Interruptor ----------

        [TestMethod]
        public void InterruptorApagado_ComoAntes_LaMuestraRetieneElTodoJunto()
        {
            valorInterruptor = "false";
            LineaPedidoPicking muestra = Muestra(3, cantidad: 1, reservada: 0);
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6), muestra);

            GestorMuestrasSinStock.Aplicar(new List<PedidoPicking> { pedido }, GestorPicking.Decidir);

            Assert.IsTrue(pedido.Lineas.Contains(muestra));
            Assert.AreEqual(0, pedido.MuestrasQuitadas.Count);
            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.TodoJuntoSinStock, pedido.MotivoNoSale);
        }

        [TestMethod]
        public void Interruptor_SinClaveEncendido_FalseOCeroApagado()
        {
            Assert.IsTrue(GestorMuestrasSinStock.Interpretar(null));
            Assert.IsTrue(GestorMuestrasSinStock.Interpretar(""));
            Assert.IsTrue(GestorMuestrasSinStock.Interpretar("true"));
            Assert.IsTrue(GestorMuestrasSinStock.Interpretar("True"));
            Assert.IsFalse(GestorMuestrasSinStock.Interpretar("false"));
            Assert.IsFalse(GestorMuestrasSinStock.Interpretar(" FALSE "));
            Assert.IsFalse(GestorMuestrasSinStock.Interpretar("0"));

            valorInterruptor = null;
            Assert.IsTrue(GestorMuestrasSinStock.Activo, "Sin la clave, encendido");
            valorInterruptor = "false";
            Assert.IsFalse(GestorMuestrasSinStock.Activo);
        }

        // ---------- Modos parciales: tampoco se quedan pendientes ----------

        [TestMethod]
        public void ModosParciales_RestoConStock_LaMuestraSinStockSeQuitaEnVezDeEsperarla()
        {
            foreach (byte modo in new[] { Servicio.SEGUN_VAYA_ENTRANDO, Servicio.TRAS_REPONER_DE_TIENDAS, Servicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ })
            {
                LineaPedidoPicking muestra = Muestra(3, cantidad: 1, reservada: 0);
                PedidoPicking pedido = Pedido(modo, DePago(1, 6, 6), muestra);
                Assert.IsFalse(GestorPicking.DecidirSiSale(pedido), "modo " + modo + ": antes, #529 esperaba la muestra como un regalo");
                Assert.AreEqual(MotivoNoSalePicking.EsperaSoloRegalos, pedido.MotivoNoSale, "modo " + modo);

                List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

                Assert.AreEqual(1, quitadas.Count, "modo " + modo);
                Assert.IsFalse(pedido.Lineas.Contains(muestra), "modo " + modo);
                Assert.IsTrue(GestorPicking.DecidirSiSale(pedido), "modo " + modo + ": sin la muestra, sale");
            }
        }

        [TestMethod]
        public void ModosParciales_FaltaTambienAlgoDePago_SaleLoQueHayYLaMuestraNoSeQuedaPendiente()
        {
            foreach (byte modo in new[] { Servicio.SEGUN_VAYA_ENTRANDO, Servicio.TRAS_REPONER_DE_TIENDAS, Servicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ })
            {
                LineaPedidoPicking muestra = Muestra(3, cantidad: 2, reservada: 0);
                LineaPedidoPicking sinStock = DePago(2, 2, 0);
                PedidoPicking pedido = Pedido(modo, DePago(1, 6, 6), sinStock, muestra);
                Assert.IsTrue(GestorPicking.DecidirSiSale(pedido), "modo " + modo + ": ya salía lo que hay (la muestra se quedaba pendiente)");

                List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

                Assert.AreEqual(1, quitadas.Count, "modo " + modo + ": la muestra tampoco se queda pendiente");
                Assert.AreEqual(2, quitadas[0].UnidadesQuitadas, "modo " + modo);
                Assert.IsFalse(pedido.Lineas.Contains(muestra), "modo " + modo);
                Assert.IsTrue(pedido.Lineas.Contains(sinStock), "modo " + modo + ": lo de pago sin stock sí se queda pendiente, como siempre");
                Assert.IsTrue(GestorPicking.DecidirSiSale(pedido), "modo " + modo);
            }
        }

        [TestMethod]
        public void ModosParciales_NadaDePagoConStock_ElPedidoNoSaleYLaMuestraSeQueda()
        {
            LineaPedidoPicking muestra = Muestra(3, cantidad: 1, reservada: 0);
            PedidoPicking pedido = Pedido(Servicio.SEGUN_VAYA_ENTRANDO, DePago(1, 6, 0), muestra);

            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(0, quitadas.Count, "Sin la muestra el pedido tampoco sale: no se toca");
            Assert.IsTrue(pedido.Lineas.Contains(muestra));
            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.SinStockDePago, pedido.MotivoNoSale);
        }

        [TestMethod]
        public void Modo4_SegundaEntrega_TodoONada_LaMuestraSinStockSeQuitaYSaleElResto()
        {
            LineaPedidoPicking muestra = Muestra(3, cantidad: 1, reservada: 0);
            PedidoPicking pedido = Pedido(Servicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, DePago(1, 6, 6), muestra);
            pedido.TieneLineasServidas = true;
            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.RestoDeUnaVezSinStock, pedido.MotivoNoSale);

            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(1, quitadas.Count);
            Assert.IsTrue(GestorPicking.DecidirSiSale(pedido));
        }

        // ---------- Los regalos que no son muestras no cambian (#529) ----------

        [TestMethod]
        public void RegaloQueNoEsMuestra_SinStock_SigueEsperandoComoEn529()
        {
            LineaPedidoPicking regalo = Regalo(3, cantidad: 1, reservada: 0);
            PedidoPicking pedido = Pedido(Servicio.SEGUN_VAYA_ENTRANDO, DePago(1, 6, 6), regalo);

            Assert.IsFalse(GestorMuestrasSinStock.SeAplica(pedido), "Base 0 sin MMP no es muestra");
            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(0, quitadas.Count);
            Assert.IsTrue(pedido.Lineas.Contains(regalo));
            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.EsperaSoloRegalos, pedido.MotivoNoSale);
        }

        [TestMethod]
        public void RegaloYMuestraSinStock_ElRegaloSigueReteniendo_YLaMuestraNoSeQuita()
        {
            LineaPedidoPicking regalo = Regalo(3, cantidad: 1, reservada: 0);
            LineaPedidoPicking muestra = Muestra(4, cantidad: 1, reservada: 0);
            PedidoPicking pedido = Pedido(Servicio.SEGUN_VAYA_ENTRANDO, DePago(1, 6, 6), regalo, muestra);

            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(0, quitadas.Count, "Sin la muestra, el regalo seguiría reteniendo: no se quita nada todavía");
            Assert.IsTrue(pedido.Lineas.Contains(muestra));
            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.EsperaSoloRegalos, pedido.MotivoNoSale);
        }

        [TestMethod]
        public void RegaloConStockYMuestraSinStock_SeQuitaLaMuestraYSaleTodoConElRegalo()
        {
            LineaPedidoPicking regalo = Regalo(3, cantidad: 1, reservada: 1);
            LineaPedidoPicking muestra = Muestra(4, cantidad: 1, reservada: 0);
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6), regalo, muestra);

            List<MuestraQuitadaPicking> quitadas = Aplicar(pedido);

            Assert.AreEqual(1, quitadas.Count);
            Assert.AreEqual("M4", quitadas[0].Producto);
            Assert.IsTrue(pedido.Lineas.Contains(regalo));
            Assert.IsTrue(GestorPicking.DecidirSiSale(pedido));
        }

        // ---------- Casos que no entran ----------

        [TestMethod]
        public void NotaDeEntrega_RetiradoOSinLineas_NoSeAplica()
        {
            PedidoPicking nota = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6), Muestra(3, 1, 0));
            nota.EsNotaEntrega = true;
            Assert.IsFalse(GestorMuestrasSinStock.SeAplica(nota));

            PedidoPicking retirado = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6), Muestra(3, 1, 0));
            retirado.Borrar = true;
            Assert.IsFalse(GestorMuestrasSinStock.SeAplica(retirado));

            PedidoPicking sinLineas = Pedido(Servicio.TODO_JUNTO);
            Assert.IsFalse(GestorMuestrasSinStock.SeAplica(sinLineas));
            Assert.IsFalse(GestorMuestrasSinStock.SeAplica(null));
        }

        [TestMethod]
        public void Aplicar_ListaDeCandidatos_SoloTocaLosQueLesSobraLaMuestra()
        {
            LineaPedidoPicking muestraQueSobra = Muestra(3, 1, 0);
            PedidoPicking sale = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6), muestraQueSobra);
            LineaPedidoPicking muestraQueSeQueda = Muestra(13, 1, 0);
            PedidoPicking espera = Pedido(Servicio.TODO_JUNTO, DePago(11, 6, 0), muestraQueSeQueda);
            espera.Id = 925826;

            GestorMuestrasSinStock.Aplicar(new List<PedidoPicking> { sale, espera }, GestorPicking.Decidir);

            Assert.IsFalse(sale.Lineas.Contains(muestraQueSobra));
            Assert.AreEqual(1, sale.MuestrasQuitadas.Count);
            Assert.IsTrue(espera.Lineas.Contains(muestraQueSeQueda));
            Assert.AreEqual(0, espera.MuestrasQuitadas.Count);
        }

        // ---------- Modo 3: la muestra no hace esperar la reposición de tiendas ----------

        [TestMethod]
        public void Modo3_MuestraConStockEnTienda_NoHaceEsperarAlPedido()
        {
            LineaPedidoPicking muestra = Muestra(3, cantidad: 1, reservada: 0);
            muestra.FechaModificacion = DateTime.Today;
            List<StockProducto> stocks = new List<StockProducto> { new StockProducto { Producto = "M3", StockDisponible = 0, StockTienda = 5, EnCamino = 0 } };

            Assert.IsFalse(GestorReposicionTiendas.LineaEsperaReposicion(muestra, stocks, new List<LineaPedidoPicking> { muestra }),
                "Encendido: la muestra no hace esperar (se quitará del pedido si lo demás sale)");

            valorInterruptor = "false";
            Assert.IsTrue(GestorReposicionTiendas.LineaEsperaReposicion(muestra, stocks, new List<LineaPedidoPicking> { muestra }),
                "Apagado: como antes, le va a llegar stock de la tienda y espera");
        }

        // ---------- Rastro y avisos ----------

        [TestMethod]
        public void Describir_UnidadesYLineasReducidas()
        {
            string texto = GestorMuestrasSinStock.Describir(new[]
            {
                new MuestraQuitadaPicking { Producto = "44729", UnidadesQuitadas = 1, UnidadesQueSalen = 0 },
                new MuestraQuitadaPicking { Producto = "43767", UnidadesQuitadas = 2, UnidadesQueSalen = 3 }
            });

            Assert.AreEqual("44729 (1 ud), 43767 (2 uds de 5)", texto);
        }

        [TestMethod]
        public void Modificacion_LlevaElUsuarioDeAuditoriaYElAntesYDespuesDeLaLinea()
        {
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6));
            MuestraQuitadaPicking quitada = new MuestraQuitadaPicking { IdLinea = 7001, Producto = "44729", UnidadesQuitadas = 3, UnidadesQueSalen = 2 };
            DateTime fecha = new DateTime(2026, 10, 27, 11, 0, 0);

            Modificacion rastro = EscritorMuestrasSinStock.Modificacion(pedido, quitada, fecha);

            Assert.AreEqual(GestorMuestrasSinStock.USUARIO_AUDITORIA, rastro.Usuario);
            Assert.AreEqual("Picking (muestras sin stock)", rastro.Usuario);
            Assert.AreEqual(EscritorMuestrasSinStock.TABLA_MODIFICACIONES, rastro.Tabla);
            Assert.AreEqual(fecha, rastro.Fecha);
            JObject antes = JObject.Parse(rastro.Anterior);
            Assert.AreEqual(925825, (int)antes["Pedido"]);
            Assert.AreEqual(7001, (int)antes["NumeroOrden"]);
            Assert.AreEqual("44729", (string)antes["Producto"]);
            Assert.AreEqual(5, (int)antes["Cantidad"]);
            JObject despues = JObject.Parse(rastro.Nuevo);
            Assert.AreEqual(EscritorMuestrasSinStock.OPERACION, (string)despues["Operacion"]);
            Assert.AreEqual(2, (int)despues["Cantidad"]);
            Assert.IsFalse((bool)despues["LineaBorrada"]);
            StringAssert.Contains((string)despues["Motivo"], "27/10/26");
            StringAssert.Contains((string)despues["Motivo"], GestorMuestrasSinStock.CLAVE);

            quitada.UnidadesQueSalen = 0;
            Assert.IsTrue((bool)JObject.Parse(EscritorMuestrasSinStock.Modificacion(pedido, quitada, fecha).Nuevo)["LineaBorrada"]);
        }

        [TestMethod]
        public void ComponerNotificacion_DiceQueSeQuitoYQueElRestoSalio_DeUsted()
        {
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6));
            pedido.MuestrasQuitadas.Add(new MuestraQuitadaPicking { IdLinea = 7001, Producto = "44729", UnidadesQuitadas = 1, UnidadesQueSalen = 0 });

            NotificacionPushDTO aviso = GestorMuestrasSinStock.ComponerNotificacion(pedido);

            Assert.IsNotNull(aviso);
            Assert.AreEqual("Pedido 925825: muestra quitada por falta de stock", aviso.Titulo);
            StringAssert.Contains(aviso.Cuerpo, "Cliente 29548");
            StringAssert.Contains(aviso.Cuerpo, "44729 (1 ud)");
            StringAssert.Contains(aviso.Cuerpo, "el resto ha cogido picking");
            StringAssert.Contains(aviso.Cuerpo, "avise al cliente");
            Assert.IsFalse(aviso.Cuerpo.Contains(" tu "), "De usted, nunca de tú");
            Assert.AreEqual(GestorMuestrasSinStock.TIPO_NOTIFICACION, aviso.Tipo);
            Assert.AreEqual("925825", aviso.Datos["pedido"]);
            Assert.AreEqual("1", aviso.Datos["empresa"]);

            Assert.IsNull(GestorMuestrasSinStock.ComponerNotificacion(Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6))), "Sin muestras quitadas, nada que avisar");
        }

        [TestMethod]
        public async System.Threading.Tasks.Task Avisar_CampanaDeNestoYPushDeNestoAppAlUsuarioDelPedido_NuncaLanza()
        {
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6));
            pedido.MuestrasQuitadas.Add(new MuestraQuitadaPicking { IdLinea = 7001, Producto = "44729", UnidadesQuitadas = 1 });
            PedidoPicking sinQuitadas = Pedido(Servicio.TODO_JUNTO, DePago(1, 6, 6));
            IServicioNotificacionesPush notificaciones = A.Fake<IServicioNotificacionesPush>();

            await GestorMuestrasSinStock.Avisar(new[] { pedido, sinQuitadas }, notificaciones);

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Javier", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.That.Matches(n => n.Tipo == GestorMuestrasSinStock.TIPO_NOTIFICACION)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.EnviarAUsuario("Javier", Constantes.Aplicaciones.NESTO_APP, A<NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).Throws(new Exception("SignalR caído"));
            await GestorMuestrasSinStock.Avisar(new[] { pedido }, notificaciones); // no lanza
        }
    }
}
