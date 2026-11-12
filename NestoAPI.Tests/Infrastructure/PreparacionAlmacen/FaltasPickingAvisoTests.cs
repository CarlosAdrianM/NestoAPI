using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Novedades 1228 (picking 100070, 12/11/26): el mozo dio por «No está» el 44729 (EXPOSITOR LEVEL VACIO, muestra MMP de 0 €)
    /// en los pedidos 930414 y 930462. La línea volvió a pendiente sin que nadie se enterara y, como los pedidos son «al
    /// completar», bloqueaba la factura. Ahora (B) la muestra se quita del pedido con rastro y (A) quien pasó cada pedido recibe
    /// un aviso con lo que falta, si hay en otros almacenes y si hay compra pendiente.
    /// </summary>
    [TestClass]
    public class FaltasPickingTerminarTests
    {
        private const string EMPRESA = "1";
        private IRepositorioPreparacionAlmacen repositorio;
        private RepositorioSalidasFalso escrituras;
        private TransaccionSalidaFalsa tx;
        private IAvisadorFaltasPicking avisador;
        private ServicioSalidas servicio;

        [TestInitialize]
        public void Preparar()
        {
            GestorMuestrasSinStock.LeerAjuste = _ => null;
            repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            escrituras = new RepositorioSalidasFalso();
            tx = escrituras.Transaccion;
            avisador = A.Fake<IAvisadorFaltasPicking>();
            servicio = new ServicioSalidas(new IOrigenSalida[] { new OrigenSalidaPicking(repositorio) }, escrituras, avisadorFaltas: avisador);
            A.CallTo(() => repositorio.LeerAlmacenPicking(EMPRESA, 100070)).Returns("ALG");
        }

        [TestCleanup]
        public void Limpiar()
        {
            GestorMuestrasSinStock.LeerAjuste = null;
        }

        private static IPrincipal Usuario(params string[] grupos)
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Pedro") }, "prueba");
            return new GenericPrincipal(identidad, grupos);
        }

        /// <summary>El picking 100070: el 44729 (1 ud., línea 9001 del pedido 930414) dado por falta y el 17404 (2 uds.) cogido.</summary>
        private void Picking100070(int esperadas = 1, int faltas = 1, bool esMuestra = true, bool notaEntrega = false)
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 100070)).Returns(new List<LineaPickingAlmacenDTO>
            {
                new LineaPickingAlmacenDTO { Producto = "44729", Descripcion = "EXPOSITOR LEVEL VACIO", Cantidad = esperadas, Pasillo = "010", Fila = "002", Columna = "003" },
                new LineaPickingAlmacenDTO { Producto = "17404", Descripcion = "OTRO", Cantidad = 2, Pasillo = "011", Fila = "001", Columna = "001" }
            });
            A.CallTo(() => repositorio.LeerLecturasDelPicking(EMPRESA, 100070)).Returns(new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "44729", Unidades = esperadas - faltas, Faltas = faltas },
                new LecturaPickingAlmacen { Producto = "17404", Unidades = 2 }
            });
            tx.Faltas = new List<FaltaSalida> { new FaltaSalida { Producto = "44729", Hueco = "010/002/003", Cantidad = faltas } };
            tx.PiezasPicking = new List<PiezaSalida>
            {
                new PiezaSalida { Linea = 9001, Pedido = 930414, Ubicacion = 501, Producto = "44729", Hueco = "010002003", Cantidad = esperadas,
                    EsMuestra = esMuestra, EsNotaEntrega = notaEntrega },
                new PiezaSalida { Linea = 9002, Pedido = 930414, Ubicacion = 502, Producto = "17404", Hueco = "011001001", Cantidad = 2 }
            };
            tx.LineasQueSiguen = new List<LineaEnPickingSalida>
            {
                new LineaEnPickingSalida { Linea = 9002, Pedido = 930414, Producto = "17404", TipoLinea = 1, Cantidad = 2 },
                new LineaEnPickingSalida { Linea = 9003, Pedido = 930414, Producto = "62400003", TipoLinea = 2, Cantidad = 1 }
            };
        }

        [TestMethod]
        public async Task Terminar_MuestraConFaltaTotal_SeQuitaDelPedidoConRastroEnModificaciones()
        {
            Picking100070();

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.AreEqual(0, tx.SacadoDePedidos.Count, "No queda pendiente");
            (int linea, int cantidad, Modificacion rastro) = tx.QuitadoDePedidos.Single();
            Assert.AreEqual(9001, linea);
            Assert.AreEqual(1, cantidad);
            Assert.AreEqual(EscritorMuestrasSinStock.TABLA_MODIFICACIONES, rastro.Tabla);
            Assert.AreEqual(GestorMuestrasSinStock.USUARIO_AUDITORIA, rastro.Usuario);
            StringAssert.Contains(rastro.Nuevo, EscritorMuestrasSinStock.OPERACION_FALTA_PICKING);
            StringAssert.Contains(rastro.Nuevo, "\"LineaBorrada\":true");
            StringAssert.Contains(rastro.Nuevo, "100070");
            StringAssert.Contains(rastro.Anterior, "930414");
            Assert.AreEqual(501, tx.QuitadoDeReservas.Single().Pieza.Ubicacion, "La reserva pasa a «pendiente de ubicar» como siempre");
            Assert.IsTrue(resultado.Salida.Cambios.Any(c => c.Contains("Pedido 930414") && c.Contains("se quita del pedido (muestra sin existencias)")));
            FaltaPickingPedido falta = resultado.Salida.FaltasPorAvisar.Single();
            Assert.IsTrue(falta.QuitadaDelPedido);
            Assert.IsTrue(falta.EsMuestra);
            Assert.IsFalse(falta.NoSaleNadaDelPedido);
        }

        [TestMethod]
        public async Task Terminar_MuestraConFaltaParcial_LaLineaSeQuedaConLoCogidoYLoQueFaltaSeQuita()
        {
            Picking100070(esperadas: 3, faltas: 1);

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            (int linea, int cantidad, Modificacion rastro) = tx.QuitadoDePedidos.Single();
            Assert.AreEqual((9001, 1), (linea, cantidad));
            StringAssert.Contains(rastro.Nuevo, "\"LineaBorrada\":false");
            StringAssert.Contains(rastro.Nuevo, "\"Cantidad\":2", "Se queda con las 2 cogidas");
            Assert.AreEqual(0, tx.SacadoDePedidos.Count);
            Assert.AreEqual(1, resultado.Salida.FaltasPorAvisar.Single().Unidades);
        }

        [TestMethod]
        public async Task Terminar_FaltaDeAlgoQueNoEsMuestra_QuedaPendienteComoHoy()
        {
            Picking100070(esMuestra: false);

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            Assert.AreEqual((9001, 1), tx.SacadoDePedidos.Single());
            Assert.AreEqual(0, tx.QuitadoDePedidos.Count);
            Assert.IsTrue(resultado.Salida.Cambios.Any(c => c.Contains("queda pendiente para otra entrega")));
            Assert.IsFalse(resultado.Salida.FaltasPorAvisar.Single().QuitadaDelPedido);
        }

        [TestMethod]
        public async Task Terminar_MuestraDeUnPedidoDelQueNoSaleNadaMas_QuedaPendiente()
        {
            // Como la regla del 27/10/26: sin «resto» que servir, la muestra no se quita (se decide en el siguiente picking)
            Picking100070();
            tx.LineasQueSiguen = new List<LineaEnPickingSalida>
            {
                new LineaEnPickingSalida { Linea = 9003, Pedido = 930414, Producto = "62400003", TipoLinea = 2, Cantidad = 1 },
                new LineaEnPickingSalida { Linea = 9004, Pedido = 930414, Producto = "44730", TipoLinea = 1, Cantidad = 1, EsMuestra = true },
                new LineaEnPickingSalida { Linea = 9005, Pedido = 930414, Producto = "CHEQUE", TipoLinea = 1, Cantidad = 1, Ficticio = true }
            };

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            Assert.AreEqual(0, tx.QuitadoDePedidos.Count);
            CollectionAssert.Contains(tx.SacadoDePedidos, (9001, 1));
            Assert.IsFalse(resultado.Salida.FaltasPorAvisar.Single().QuitadaDelPedido);
            Assert.IsFalse(resultado.Salida.FaltasPorAvisar.Single().NoSaleNadaDelPedido,
                "Sale otra muestra (44730): para los portes (regla del 03/10/26) cuenta como producto, como hasta ahora");
        }

        [TestMethod]
        public async Task Terminar_PedidoAlQueNoLeQuedaNingunProducto_ElAvisoLoDice()
        {
            Picking100070(esMuestra: false);
            tx.LineasQueSiguen = new List<LineaEnPickingSalida>
            {
                new LineaEnPickingSalida { Linea = 9003, Pedido = 930414, Producto = "62400003", TipoLinea = 2, Cantidad = 1 }
            };

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            Assert.IsTrue(resultado.Salida.FaltasPorAvisar.Single().NoSaleNadaDelPedido);
        }

        [TestMethod]
        public async Task Terminar_MuestraDeUnaNotaDeEntrega_QuedaPendiente()
        {
            Picking100070(notaEntrega: true);

            _ = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            Assert.AreEqual(0, tx.QuitadoDePedidos.Count);
            Assert.AreEqual((9001, 1), tx.SacadoDePedidos.Single());
        }

        [TestMethod]
        public async Task Terminar_ConElInterruptorDeMuestrasApagado_QuedaPendiente()
        {
            GestorMuestrasSinStock.LeerAjuste = _ => "false";
            Picking100070();

            _ = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            Assert.AreEqual(0, tx.QuitadoDePedidos.Count);
            Assert.AreEqual((9001, 1), tx.SacadoDePedidos.Single());
        }

        [TestMethod]
        public async Task Terminar_SiNoSePuedeQuitarLaMuestra_ElAvisoDiceQueQuedaPendiente()
        {
            Picking100070();
            tx.QuitarDelPedidoDevuelve = false;

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            Assert.IsFalse(resultado.Salida.FaltasPorAvisar.Single().QuitadaDelPedido);
            Assert.IsTrue(resultado.Salida.Cambios.Any(c => c.Contains("queda pendiente")));
        }

        [TestMethod]
        public async Task Terminar_ConFaltas_AvisaConLasFaltasYElAlmacenDelPicking()
        {
            Picking100070();

            _ = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            A.CallTo(() => avisador.Avisar(EMPRESA, 100070, "ALG",
                A<IReadOnlyList<FaltaPickingPedido>>.That.Matches(f => f.Count == 1 && f[0].Pedido == 930414 && f[0].Producto == "44729")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Terminar_SinFaltas_NoAvisa()
        {
            Picking100070(faltas: 0);
            tx.Faltas = new List<FaltaSalida>();

            _ = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            A.CallTo(() => avisador.Avisar(A<string>._, A<int>._, A<string>._, A<IReadOnlyList<FaltaPickingPedido>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ensayo_NoAvisa()
        {
            Picking100070();

            _ = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Dirección"), ensayo: true);

            A.CallTo(() => avisador.Avisar(A<string>._, A<int>._, A<string>._, A<IReadOnlyList<FaltaPickingPedido>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_SiElAvisoFalla_ElPickingQuedaTerminadoIgual()
        {
            Picking100070();
            A.CallTo(() => avisador.Avisar(A<string>._, A<int>._, A<string>._, A<IReadOnlyList<FaltaPickingPedido>>._))
                .Throws(new InvalidOperationException("Firebase caído"));

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 100070, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.AreEqual(1, tx.Terminadas.Count);
            Assert.AreEqual(1, tx.QuitadoDePedidos.Count);
        }
    }

    /// <summary>Novedades 1228: el texto y los destinatarios del aviso (puro).</summary>
    [TestClass]
    public class AvisoFaltasPickingTests
    {
        private static PedidoConFaltasPicking Pedido930414(params FaltaAvisoPicking[] faltas) => new PedidoConFaltasPicking
        {
            Empresa = "1",
            Pedido = 930414,
            Picking = 100070,
            Cliente = "15191/0 - CENTRO DE ESTÉTICA",
            Almacen = "ALG",
            Faltas = faltas.ToList()
        };

        private static FaltaAvisoPicking Expositor(bool quitada = false) => new FaltaAvisoPicking
        {
            Producto = "44729",
            Nombre = "EXPOSITOR LEVEL VACIO",
            Unidades = 1,
            EsMuestra = true,
            QuitadaDelPedido = quitada,
            StockOtrosAlmacenes = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("Alcobendas", 1) }
        };

        private static FaltaAvisoPicking Crema(CompraPendientePicking compra = null, int unidades = 2) => new FaltaAvisoPicking
        {
            Producto = "17404",
            Nombre = "CREMA",
            Unidades = unidades,
            Compra = compra
        };

        [TestMethod]
        public void Componer_UnaFaltaPendiente_ConStockEnOtroAlmacenYSinCompra()
        {
            NotificacionPushDTO aviso = AvisoFaltasPicking.Componer(Pedido930414(Expositor()));

            Assert.AreEqual("Pedido 930414: no se ha encontrado 44729 en el almacén", aviso.Titulo);
            Assert.AreEqual("Pedido 930414 (cliente 15191/0 - CENTRO DE ESTÉTICA): no se ha encontrado en el almacén de Algete 1 ud. de " +
                "44729 EXPOSITOR LEVEL VACIO; queda pendiente. El resto del pedido sigue su curso. En otros almacenes: Alcobendas 1. " +
                "Sin compra pendiente.", aviso.Cuerpo);
            Assert.AreEqual(AvisoFaltasPicking.TIPO_NOTIFICACION, aviso.Tipo);
            Assert.AreEqual("930414", aviso.Datos["pedido"], "NestoApp abre el pedido por «pedido»");
            Assert.AreEqual("1", aviso.Datos["empresa"]);
            Assert.AreEqual(AvisoFaltasPicking.TIPO_NOTIFICACION, aviso.Datos["tipo"]);
        }

        [TestMethod]
        public void Componer_MuestraQuitada_DiceQueSeHaQuitadoYNoHablaDeCompras()
        {
            NotificacionPushDTO aviso = AvisoFaltasPicking.Componer(Pedido930414(Expositor(quitada: true)));

            StringAssert.Contains(aviso.Cuerpo, "se ha quitado del pedido (muestra sin existencias)");
            Assert.IsFalse(aviso.Cuerpo.Contains("queda pendiente"));
            Assert.IsFalse(aviso.Cuerpo.Contains("compra"));
        }

        [TestMethod]
        public void Componer_ConCompraPendiente_DiceLaFechaYSinStockEnOtros()
        {
            NotificacionPushDTO aviso = AvisoFaltasPicking.Componer(Pedido930414(
                Crema(new CompraPendientePicking { Pedido = 55001, FechaPrevista = new DateTime(2026, 11, 20) })));

            StringAssert.Contains(aviso.Cuerpo, "2 uds. de 17404 CREMA; queda pendiente.");
            StringAssert.Contains(aviso.Cuerpo, "No hay en otros almacenes.");
            StringAssert.Contains(aviso.Cuerpo, "Próxima compra: 20/11.");
        }

        [TestMethod]
        public void Componer_CompraSinFecha_LoDice()
        {
            NotificacionPushDTO aviso = AvisoFaltasPicking.Componer(Pedido930414(Crema(new CompraPendientePicking { Pedido = 55001 })));

            StringAssert.Contains(aviso.Cuerpo, "Hay compra pendiente (pedido 55001) sin fecha prevista.");
        }

        [TestMethod]
        public void Componer_PedidoAlCompletarConLineaPendiente_AvisaDeQueNoSeFactura()
        {
            PedidoConFaltasPicking pedido = Pedido930414(Expositor());
            pedido.MantenerJunto = true;

            StringAssert.Contains(AvisoFaltasPicking.Componer(pedido).Cuerpo,
                "El pedido se sirve «al completar»: no se facturará mientras esa línea siga pendiente.");
        }

        [TestMethod]
        public void Componer_PedidoAlCompletarConLaMuestraQuitada_NoHablaDeLaFactura()
        {
            PedidoConFaltasPicking pedido = Pedido930414(Expositor(quitada: true));
            pedido.MantenerJunto = true;

            Assert.IsFalse(AvisoFaltasPicking.Componer(pedido).Cuerpo.Contains("al completar"));
        }

        [TestMethod]
        public void Componer_VariasFaltasEnUnPedido_UnaLineaPorProducto()
        {
            PedidoConFaltasPicking pedido = Pedido930414(Expositor(quitada: true), Crema());
            pedido.MantenerJunto = true;

            NotificacionPushDTO aviso = AvisoFaltasPicking.Componer(pedido);

            Assert.AreEqual("Pedido 930414: no se han encontrado 2 productos en el almacén", aviso.Titulo);
            StringAssert.Contains(aviso.Cuerpo, "no se ha encontrado en el almacén de Algete:");
            StringAssert.Contains(aviso.Cuerpo, "- 1 ud. de 44729 EXPOSITOR LEVEL VACIO: se ha quitado del pedido (muestra sin existencias). En otros almacenes: Alcobendas 1.");
            StringAssert.Contains(aviso.Cuerpo, "- 2 uds. de 17404 CREMA: queda pendiente. No hay en otros almacenes. Sin compra pendiente.");
            StringAssert.Contains(aviso.Cuerpo, "esa línea siga pendiente", "Solo la crema sigue pendiente");
        }

        [TestMethod]
        public void Componer_PedidoDelQueNoSaleNada_NoDiceQueElRestoSigue()
        {
            PedidoConFaltasPicking pedido = Pedido930414(Crema());
            pedido.NoSaleNada = true;

            string cuerpo = AvisoFaltasPicking.Componer(pedido).Cuerpo;

            StringAssert.Contains(cuerpo, "No sale nada más de este pedido en este picking");
            Assert.IsFalse(cuerpo.Contains("El resto del pedido sigue su curso"));
        }

        [TestMethod]
        public void Componer_SinFaltas_Null()
        {
            Assert.IsNull(AvisoFaltasPicking.Componer(Pedido930414()));
            Assert.IsNull(AvisoFaltasPicking.Componer(null));
        }

        private static ComercialesPedido Comerciales(string usuarioPedido, params ComercialPedido[] comerciales) => new ComercialesPedido
        {
            Empresa = "1",
            Pedido = 930414,
            UsuarioPedido = usuarioPedido,
            Comerciales = comerciales.ToList()
        };

        [TestMethod]
        public void Destinatarios_UsuarioYVendedorDistintos_BuzonYPushAlUsuarioYPushAlVendedor()
        {
            DestinatariosAvisoFaltasPicking d = AvisoFaltasPicking.Destinatarios(Comerciales("TiendaReina",
                new ComercialPedido { Vendedor = "JE", Usuarios = new List<string> { "Jesus" } }));

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\TiendaReina" }, d.BuzonNesto);
            Assert.AreEqual("TiendaReina", d.UsuarioNestoApp);
            CollectionAssert.AreEqual(new[] { "JE" }, d.VendedoresNestoApp);
        }

        [TestMethod]
        public void Destinatarios_ElUsuarioEsSuVendedor_UnSoloAviso()
        {
            DestinatariosAvisoFaltasPicking d = AvisoFaltasPicking.Destinatarios(Comerciales("NUEVAVISION\\Jesus",
                new ComercialPedido { Vendedor = "JE", Usuarios = new List<string> { "Jesus" } }));

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Jesus" }, d.BuzonNesto);
            Assert.AreEqual("Jesus", d.UsuarioNestoApp);
            Assert.AreEqual(0, d.VendedoresNestoApp.Count, "No le llega dos veces");
        }

        [TestMethod]
        public void Destinatarios_ElVendedorGeneralNoRecibePush()
        {
            DestinatariosAvisoFaltasPicking d = AvisoFaltasPicking.Destinatarios(Comerciales("Lidia",
                new ComercialPedido { Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL }));

            Assert.AreEqual(0, d.VendedoresNestoApp.Count);
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Lidia" }, d.BuzonNesto);
        }

        [TestMethod]
        public void AvisosCompras_SoloLoPendienteQueNoEsMuestraNiTieneCompra_DeVariosPedidos()
        {
            PedidoConFaltasPicking uno = Pedido930414(Expositor(), Crema());
            PedidoConFaltasPicking otro = Pedido930414(Crema(new CompraPendientePicking { Pedido = 1 }), new FaltaAvisoPicking { Producto = "20001", Nombre = "LACA", Unidades = 1 });
            otro.Pedido = 930462;

            List<string> avisos = AvisoFaltasPicking.AvisosCompras(new[] { uno, otro });

            Assert.AreEqual(2, avisos.Count);
            Assert.AreEqual("Picking 100070, pedido 930414 (cliente 15191/0 - CENTRO DE ESTÉTICA): no se ha encontrado en el almacén de Algete " +
                "2 uds. de 17404 CREMA y no hay compra pendiente. No hay en otros almacenes.", avisos[0]);
            StringAssert.Contains(avisos[1], "pedido 930462");
            StringAssert.Contains(avisos[1], "20001 LACA");
            Assert.AreEqual("Picking 100070: faltas sin compra pendiente", AvisoFaltasPicking.TituloCompras(100070));
        }

        [TestMethod]
        public void OtrosAlmacenes_NoCuentaElDelPickingNiLosQueNoTienen()
        {
            var stocks = new Dictionary<string, Dictionary<string, int>>
            {
                ["44729"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["ALG"] = 3, ["REI"] = 0, ["ALC"] = 1 }
            };

            List<KeyValuePair<string, int>> otros = LectorDatosFaltasPickingSql.OtrosAlmacenes(stocks, "44729 ", "ALG");

            Assert.AreEqual(1, otros.Count);
            Assert.AreEqual(new KeyValuePair<string, int>("Alcobendas", 1), otros[0]);
        }
    }

    /// <summary>Novedades 1228: el envío del aviso (en segundo plano), que nunca lanza.</summary>
    [TestClass]
    public class AvisadorFaltasPickingTests
    {
        private IAvisadorCompras compras;
        private ILectorDatosFaltasPicking lector;
        private IServicioNotificacionesPush notificaciones;
        private List<Exception> registrados;
        private AvisadorFaltasPicking avisador;

        [TestInitialize]
        public void Preparar()
        {
            compras = A.Fake<IAvisadorCompras>();
            lector = A.Fake<ILectorDatosFaltasPicking>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            registrados = new List<Exception>();
            avisador = new AvisadorFaltasPicking(compras, () => lector, () => notificaciones, ex => registrados.Add(ex));
        }

        private static PedidoAAvisarFaltas Pedido(int numero, string usuario, string vendedor, FaltaAvisoPicking falta) => new PedidoAAvisarFaltas
        {
            Pedido = new PedidoConFaltasPicking { Empresa = "1", Pedido = numero, Picking = 100070, Cliente = "15191/0", Almacen = "ALG", Faltas = new List<FaltaAvisoPicking> { falta } },
            Comerciales = new ComercialesPedido
            {
                Empresa = "1",
                Pedido = numero,
                UsuarioPedido = usuario,
                Comerciales = new List<ComercialPedido> { new ComercialPedido { Vendedor = vendedor, Usuarios = new List<string> { vendedor + "user" } } }
            }
        };

        private static readonly List<FaltaPickingPedido> faltas = new List<FaltaPickingPedido>
        {
            new FaltaPickingPedido { Empresa = "1", Pedido = 930414, Producto = "44729", Unidades = 1 }
        };

        private void DosPedidos()
        {
            A.CallTo(() => lector.Leer("1", 100070, "ALG", A<IReadOnlyList<FaltaPickingPedido>>._)).Returns(new List<PedidoAAvisarFaltas>
            {
                Pedido(930414, "TiendaReina", "JE", new FaltaAvisoPicking { Producto = "44729", Unidades = 1, EsMuestra = true, QuitadaDelPedido = true }),
                Pedido(930462, "Lidia", "PA", new FaltaAvisoPicking { Producto = "17404", Unidades = 2 })
            });
        }

        [TestMethod]
        public async Task AvisarAsync_VariosPedidos_UnAvisoPorPedidoYComprasSoloLoQueNoTieneCompra()
        {
            DosPedidos();

            await avisador.AvisarAsync("1", 100070, "ALG", faltas);

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\TiendaReina", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Datos["pedido"] == "930414"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Lidia", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Datos["pedido"] == "930462"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.EnviarAUsuario("TiendaReina", Constantes.Aplicaciones.NESTO_APP, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.EnviarAVendedor("1", "JE", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.EnviarAVendedor("1", "PA", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => compras.Avisar("Picking 100070: faltas sin compra pendiente",
                A<IEnumerable<string>>.That.Matches(a => a.Count() == 1 && a.First().Contains("17404")), AvisadorCompras.TIPO_PICKING_FALTA))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public async Task AvisarAsync_SiFallaLaPushDeUnPedido_SigueConElOtroYNoLanza()
        {
            DosPedidos();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\TiendaReina", A<string>._, A<NotificacionPushDTO>._))
                .Throws(new InvalidOperationException("Firebase caído"));

            await avisador.AvisarAsync("1", 100070, "ALG", faltas);

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Lidia", A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, registrados.Count);
            StringAssert.Contains(registrados[0].Message, "930414");
        }

        [TestMethod]
        public async Task AvisarAsync_SiFallaLaLectura_NoLanzaYLoRegistra()
        {
            A.CallTo(() => lector.Leer(A<string>._, A<int>._, A<string>._, A<IReadOnlyList<FaltaPickingPedido>>._))
                .Throws(new InvalidOperationException("BD caída"));

            await avisador.AvisarAsync("1", 100070, "ALG", faltas);

            Assert.AreEqual(1, registrados.Count);
            A.CallTo(() => compras.Avisar(A<string>._, A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AvisarAsync_SiFallaCompras_NoLanza()
        {
            DosPedidos();
            A.CallTo(() => compras.Avisar(A<string>._, A<IEnumerable<string>>._, A<string>._)).Throws(new InvalidOperationException("AD caído"));

            await avisador.AvisarAsync("1", 100070, "ALG", faltas);

            Assert.AreEqual(1, registrados.Count);
        }
    }
}
