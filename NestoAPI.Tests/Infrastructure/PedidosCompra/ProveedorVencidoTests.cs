using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#606 (decisión de Carlos, 08/10): pedidos a proveedor con la fecha prevista vencida. Aviso en la campana de Nesto
    /// (deduplicado por línea y día) y correo diario a Compras.
    /// </summary>
    [TestClass]
    public class ProveedorVencidoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 13, 10, 30, 0);
        private IRepositorioProveedorVencido repositorio;
        private IServicioNotificacionesPush notificaciones;
        private List<Exception> registradas;
        private AvisadorProveedorVencido avisador;
        private List<NotificacionPushDTO> enviadas;
        private List<string> destinatariosEnviados;

        [TestInitialize]
        public void Preparar()
        {
            AvisadorProveedorVencido.OlvidarProductosMirados();
            repositorio = A.Fake<IRepositorioProveedorVencido>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            registradas = new List<Exception>();
            enviadas = new List<NotificacionPushDTO>();
            destinatariosEnviados = new List<string>();
            A.CallTo(() => repositorio.LeerDestinatarios()).Returns(new List<string> { "Santiago", "Manuel" });
            A.CallTo(() => repositorio.YaAvisadaHoy(A<string>._, A<DateTime>._)).Returns(false);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string usuario, string aplicacion, NotificacionPushDTO n) =>
                {
                    destinatariosEnviados.Add(usuario);
                    enviadas.Add(n);
                })
                .Returns(Task.CompletedTask);
            avisador = new AvisadorProveedorVencido(() => repositorio, notificaciones, () => AHORA, registradas.Add);
        }

        private static LineaCompraVencida Linea(int pedido, int orden, string producto, string almacen = "ALG") => new LineaCompraVencida
        {
            Empresa = "1",
            Pedido = pedido,
            Orden = orden,
            Proveedor = "1480",
            NombreProveedor = "Cosmética Ejemplo, S.L.",
            Producto = producto,
            Descripcion = "Crema de manos",
            Almacen = almacen,
            Cantidad = 20,
            FechaPrevista = new DateTime(2026, 10, 7)
        };

        private static List<ProveedorVencidoFechaEntregaAgencia> Vencidos(params string[] productos) => productos
            .Select(p => new ProveedorVencidoFechaEntregaAgencia { Producto = p, FechaPrevista = new DateTime(2026, 10, 7), LlegadaSupuesta = new DateTime(2026, 10, 14) })
            .ToList();

        #region Aviso en la campana

        [TestMethod]
        public async Task Avisar_UnPedidoDeCompraVencido_AvisaASantiagoYAManuelEnLaCampanaDeNesto()
        {
            A.CallTo(() => repositorio.LeerLineasVencidas("1", A<IEnumerable<string>>._, AHORA.Date))
                .Returns(new List<LineaCompraVencida> { Linea(220515, 3, "45945"), Linea(220515, 4, "45946") });

            await avisador.Avisar("1  ", Vencidos("45945", "45946"), "el pedido 928020");

            CollectionAssert.AreEquivalent(new[] { "NUEVAVISION\\Santiago", "NUEVAVISION\\Manuel" }, destinatariosEnviados);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .MustHaveHappenedTwiceExactly();
            NotificacionPushDTO n = enviadas.First();
            StringAssert.Contains(n.Titulo, "220515");
            StringAssert.Contains(n.Titulo, "Cosmética Ejemplo");
            StringAssert.Contains(n.Cuerpo, "45945");
            StringAssert.Contains(n.Cuerpo, "45946");
            StringAssert.Contains(n.Cuerpo, "07/10/2026");
            StringAssert.Contains(n.Cuerpo, "el pedido 928020");
            StringAssert.Contains(n.Cuerpo, "pedidos de clientes esperándolo");
            Assert.AreEqual(AvisadorProveedorVencido.TIPO_NOTIFICACION, n.Tipo);
            StringAssert.Contains(n.Datos["lineas"], AvisadorProveedorVencido.Marca("1/220515/3"));
            StringAssert.Contains(n.Datos["lineas"], AvisadorProveedorVencido.Marca("1/220515/4"));
            Assert.AreEqual(0, registradas.Count);
        }

        [TestMethod]
        public async Task Avisar_DosPedidosDeCompra_UnAvisoPorPedido()
        {
            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._))
                .Returns(new List<LineaCompraVencida> { Linea(220515, 3, "45945"), Linea(220266, 1, "45646") });

            await avisador.Avisar("1", Vencidos("45945", "45646"), "el pedido 928020");

            Assert.AreEqual(4, enviadas.Count, "2 pedidos × 2 destinatarios");
            Assert.AreEqual(2, enviadas.Select(e => e.Datos["pedidoCompra"]).Distinct().Count());
        }

        [TestMethod]
        public async Task Avisar_ElMismoProductoOtraVezElMismoDia_NoVuelveAMirarLaBD()
        {
            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._))
                .Returns(new List<LineaCompraVencida> { Linea(220515, 3, "45945") });

            await avisador.Avisar("1", Vencidos("45945"), "el pedido 928020");
            await avisador.Avisar("1", Vencidos("45945"), "un pedido que se está montando en la plantilla de venta");

            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(2, enviadas.Count);
        }

        [TestMethod]
        public async Task Avisar_AlDiaSiguiente_VuelveAAvisar()
        {
            DateTime ahora = AHORA;
            avisador = new AvisadorProveedorVencido(() => repositorio, notificaciones, () => ahora, registradas.Add);
            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._))
                .Returns(new List<LineaCompraVencida> { Linea(220515, 3, "45945") });

            await avisador.Avisar("1", Vencidos("45945"), "el pedido 928020");
            ahora = AHORA.AddDays(1);
            await avisador.Avisar("1", Vencidos("45945"), "el pedido 928020");

            Assert.AreEqual(4, enviadas.Count);
        }

        [TestMethod]
        public async Task Avisar_LineaYaAvisadaHoyEnElBuzon_NoRepite()
        {
            // Tras reciclar la API la memoria se vacía: manda el buzón.
            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._))
                .Returns(new List<LineaCompraVencida> { Linea(220515, 3, "45945"), Linea(220515, 4, "45946") });
            A.CallTo(() => repositorio.YaAvisadaHoy("1/220515/3", AHORA.Date)).Returns(true);

            await avisador.Avisar("1", Vencidos("45945", "45946"), "el pedido 928020");

            Assert.AreEqual(2, enviadas.Count);
            Assert.IsFalse(enviadas[0].Cuerpo.Contains("45945"), enviadas[0].Cuerpo);
            StringAssert.Contains(enviadas[0].Cuerpo, "45946");
        }

        [TestMethod]
        public async Task Avisar_TodasYaAvisadasHoy_NoManda()
        {
            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._))
                .Returns(new List<LineaCompraVencida> { Linea(220515, 3, "45945") });
            A.CallTo(() => repositorio.YaAvisadaHoy(A<string>._, A<DateTime>._)).Returns(true);

            await avisador.Avisar("1", Vencidos("45945"), "el pedido 928020");

            Assert.AreEqual(0, enviadas.Count);
        }

        [TestMethod]
        public async Task Avisar_LineasDeOtroAlmacen_NoAvisa()
        {
            // La fecha de entrega a la agencia solo mira lo que llega a Algete.
            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._))
                .Returns(new List<LineaCompraVencida> { Linea(220515, 3, "45945", "REI") });

            await avisador.Avisar("1", Vencidos("45945"), "el pedido 928020");

            Assert.AreEqual(0, enviadas.Count);
        }

        [TestMethod]
        public async Task Avisar_SinProductos_NoTocaLaBD()
        {
            await avisador.Avisar("1", new List<ProveedorVencidoFechaEntregaAgencia>(), "el pedido 928020");

            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Avisar_SiLaBDFalla_NoLanzaYLoRegistra()
        {
            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._)).Throws(new Exception("BD caída"));

            await avisador.Avisar("1", Vencidos("45945"), "el pedido 928020");

            Assert.AreEqual(1, registradas.Count);
            StringAssert.Contains(registradas[0].Message, "BD caída");
        }

        [TestMethod]
        public async Task Avisar_SinDestinatarios_NoMandaYLoRegistra()
        {
            A.CallTo(() => repositorio.LeerDestinatarios()).Returns(new List<string>());
            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, A<IEnumerable<string>>._, A<DateTime>._))
                .Returns(new List<LineaCompraVencida> { Linea(220515, 3, "45945") });

            await avisador.Avisar("1", Vencidos("45945"), "el pedido 928020");

            Assert.AreEqual(0, enviadas.Count);
            Assert.AreEqual(1, registradas.Count);
        }

        [TestMethod]
        public void Destinatarios_SinParametro_SantiagoYManuel_ConParametro_LaLista_Vacio_Nadie()
        {
            CollectionAssert.AreEqual(new[] { "Santiago", "Manuel" }, DestinatariosAvisoProveedorVencido.Lista(null));
            CollectionAssert.AreEqual(new[] { "Santiago", "Alfredo" }, DestinatariosAvisoProveedorVencido.Lista("NUEVAVISION\\Santiago; Alfredo, santiago"));
            Assert.AreEqual(0, DestinatariosAvisoProveedorVencido.Lista("  ").Count);
        }

        #endregion

        #region Correo diario a Compras

        private bool ProcesarCorreo(List<LineaCompraVencida> lineas, Dictionary<string, EsperaClientesProducto> esperas, out MailMessage correo)
        {
            MailMessage enviado = null;
            IServicioCorreoElectronico servicioCorreo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) =>
            {
                enviado = new MailMessage { Subject = m.Subject, Body = m.Body };
                foreach (MailAddress a in m.To)
                {
                    enviado.To.Add(a);
                }
            }).Returns(true);
            A.CallTo(() => repositorio.LeerLineasVencidas(A<string>._, null, AHORA.Date)).Returns(lineas);
            A.CallTo(() => repositorio.LeerEsperas(A<IEnumerable<string>>._)).Returns(esperas);
            bool resultado = ProveedoresVencidosJobsService.Procesar(new DependenciasProveedoresVencidos
            {
                Repositorio = repositorio,
                Correo = servicioCorreo,
                Ahora = AHORA
            });
            correo = enviado;
            return resultado;
        }

        [TestMethod]
        public void Correo_SinNadaVencido_NoSeManda()
        {
            Assert.IsFalse(ProcesarCorreo(new List<LineaCompraVencida>(), new Dictionary<string, EsperaClientesProducto>(), out MailMessage correo));
            Assert.IsNull(correo);
        }

        [TestMethod]
        public void Correo_VaAComprasYDestacaLosQueTienenClientesEsperando()
        {
            var lineas = new List<LineaCompraVencida> { Linea(220515, 3, "45945"), Linea(220266, 1, "45646") };
            var esperas = new Dictionary<string, EsperaClientesProducto>
            {
                [RepositorioProveedorVencidoSql.ClaveEspera("45945", "ALG")] = new EsperaClientesProducto { Stock = 2, Pendientes = 5, Pedidos = new List<int> { 928020, 928031 } },
                [RepositorioProveedorVencidoSql.ClaveEspera("45646", "ALG")] = new EsperaClientesProducto { Stock = 10, Pendientes = 3, Pedidos = new List<int> { 927000 } }
            };

            Assert.IsTrue(ProcesarCorreo(lineas, esperas, out MailMessage correo));

            Assert.AreEqual(Constantes.Correos.COMPRAS, correo.To.Single().Address);
            StringAssert.Contains(correo.Subject, "2 pedidos");
            StringAssert.Contains(correo.Subject, "1 línea con clientes esperando");
            string html = correo.Body;
            int seccionEspera = html.IndexOf("Con pedidos de clientes esperando", StringComparison.Ordinal);
            int seccionResto = html.IndexOf("<h2>Resto</h2>", StringComparison.Ordinal);
            int posEspera = html.IndexOf("45945", StringComparison.Ordinal);
            int posResto = html.IndexOf("45646", StringComparison.Ordinal);
            Assert.IsTrue(seccionEspera < posEspera && posEspera < seccionResto, "El que tiene clientes esperando va en la primera tabla");
            Assert.IsTrue(posResto > seccionResto, "El stock cubre lo pendiente: va en el resto");
            StringAssert.Contains(html, "faltan 3 uds.");
            StringAssert.Contains(html, "928020, 928031");
            StringAssert.Contains(html, "<td style='text-align:right'>6</td>", "6 días de retraso (07/10 → 13/10)");
        }

        [TestMethod]
        public void EsperaClientes_FaltanEsLoQueElStockNoCubre()
        {
            Assert.AreEqual(3, new EsperaClientesProducto { Stock = 2, Pendientes = 5 }.Faltan);
            Assert.AreEqual(5, new EsperaClientesProducto { Stock = -4, Pendientes = 5 }.Faltan);
            Assert.AreEqual(0, new EsperaClientesProducto { Stock = 9, Pendientes = 5 }.Faltan);
        }

        #endregion
    }
}
