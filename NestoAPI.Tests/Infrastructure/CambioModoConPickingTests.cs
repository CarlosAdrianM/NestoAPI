using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Calidad;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#533: pedido 926879 (23/09/26), pasado a «Todo junto» con el picking hecho sin que
    /// almacén se enterase. Con picking o albarán de hoy no se cambia el modo; con entregas de otros
    /// días y sin picking, sí.
    /// </summary>
    [TestClass]
    public class CambioModoConPickingTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 24);

        private static LinPedidoVta Linea(short estado, int? picking = null, DateTime? fechaAlbaran = null) =>
            new LinPedidoVta { Estado = estado, Picking = picking, Fecha_Albarán = fechaAlbaran };

        private static ClaimsPrincipal ConRoles(params string[] roles) => new ClaimsPrincipal(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)).Concat(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Alfredo") }), "test"));

        // Sugerencia 618 (Alfredo, 15/10/26, pedido 928498): el correo pedía a almacén cambiar el modo, pero la regla
        // también le bloqueaba a él. Almacén y Dirección sí pueden; los demás siguen pidiéndoselo a almacén.
        [DataTestMethod]
        [DataRow("NUEVAVISION\\Almacén", true)]
        [DataRow("Dirección", true)]
        [DataRow("NUEVAVISION\\Tiendas", false)]
        [DataRow("NUEVAVISION\\Vendedores", false)]
        public void PuedeCambiarConPicking_SoloAlmacenYDireccion(string rol, bool puede)
        {
            Assert.AreEqual(puede, CambioModoConPicking.PuedeCambiarConPicking(ConRoles(rol)));
        }

        [TestMethod]
        public void PuedeCambiarConPicking_SinUsuario_No()
        {
            Assert.IsFalse(CambioModoConPicking.PuedeCambiarConPicking(null));
        }

        [TestMethod]
        public void ConPicking_NoSePuedeCambiar()
        {
            string motivo = CambioModoConPicking.Motivo(new[] { Linea(Constantes.EstadosLineaVenta.EN_CURSO, picking: 99632) }, HOY);

            StringAssert.Contains(motivo, "picking");
            StringAssert.Contains(motivo, "almacén");
        }

        [TestMethod]
        public void ConAlbaranDeHoy_NoSePuedeCambiar()
        {
            var lineas = new[]
            {
                Linea(Constantes.EstadosLineaVenta.ALBARAN, fechaAlbaran: HOY.AddHours(11)),
                Linea(Constantes.EstadosLineaVenta.PENDIENTE)
            };

            Assert.IsNotNull(CambioModoConPicking.Motivo(lineas, HOY));
        }

        [TestMethod]
        public void EntregaDeOtroDiaYSinPicking_SiSePuede()
        {
            // Que el resto del pedido salga junto: el caso que sí tiene sentido
            var lineas = new[]
            {
                Linea(Constantes.EstadosLineaVenta.ALBARAN, fechaAlbaran: HOY.AddDays(-3)),
                Linea(Constantes.EstadosLineaVenta.PENDIENTE)
            };

            Assert.IsNull(CambioModoConPicking.Motivo(lineas, HOY));
        }

        [TestMethod]
        public void SinPickingNiAlbaran_SiSePuede()
        {
            Assert.IsNull(CambioModoConPicking.Motivo(new[] { Linea(Constantes.EstadosLineaVenta.EN_CURSO, picking: 0) }, HOY));
        }

        [TestMethod]
        public void CorreoSolicitud_VaAAlmacenConElPedidoYLosModos()
        {
            MailMessage correo = CambioModoConPicking.CorreoSolicitud("1", 926879, "40483     ",
                Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                "NUEVAVISION\\dlopez", "El cliente lo quiere todo junto");

            Assert.AreEqual(Constantes.Correos.ALMACEN, correo.To.Single().Address);
            StringAssert.Contains(correo.Subject, "926879");
            StringAssert.Contains(correo.Subject, "Todo junto");
            StringAssert.Contains(correo.Body, "vaya entrando"); // la ú va codificada en HTML
            StringAssert.Contains(correo.Body, "dlopez");
            StringAssert.Contains(correo.Body, "El cliente lo quiere todo junto");
        }

        private static NVEntities DbConElPedido()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<CabPedidoVta> cabeceras = A.Fake<DbSet<CabPedidoVta>>(o => o.Implements<IQueryable<CabPedidoVta>>().Implements<IDbAsyncEnumerable<CabPedidoVta>>());
            IQueryable<CabPedidoVta> datos = new List<CabPedidoVta>
            {
                new CabPedidoVta { Empresa = "1", Número = 926879, Nº_Cliente = "40483", Contacto = "0", ModoServicio = 2, ServirJunto = false }
            }.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<CabPedidoVta>)cabeceras).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<CabPedidoVta>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<CabPedidoVta>)cabeceras).Provider).Returns(new TestDbAsyncQueryProvider<CabPedidoVta>(datos.Provider));
            A.CallTo(() => ((IQueryable<CabPedidoVta>)cabeceras).Expression).Returns(datos.Expression);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)cabeceras).ElementType).Returns(datos.ElementType);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)cabeceras).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => db.CabPedidoVtas).Returns(cabeceras);
            return db;
        }

        private static PedidosVentaSolicitudesController Controlador(IServicioCorreoElectronico correo) =>
            new PedidosVentaSolicitudesController(DbConElPedido(), correo)
            {
                RequestContext = new HttpRequestContext
                {
                    Principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Name, "NUEVAVISION\\dlopez"),
                        new Claim(ClaimTypes.Email, "daniellopez@nuevavision.es")
                    }, "JWT"))
                }
            };

        [TestMethod]
        public async Task PostSolicitudCambioModo_MandaElCorreoConRespuestaAlUsuario()
        {
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);
            var controller = Controlador(correo);

            var resultado = await controller.PostSolicitudCambioModo(new SolicitudCambioModoDTO
            {
                Empresa = "1", Pedido = 926879, ModoDeseado = Constantes.Pedidos.ModosServicio.TODO_JUNTO
            });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<string>));
            Assert.AreEqual("daniellopez@nuevavision.es", enviado.ReplyToList.Single().Address);
            StringAssert.Contains(enviado.Subject, "926879");
        }

        // NestoAPI#914: la petición deja su señal de calidad (antes solo salía por correo y se perdía), aunque el correo no salga
        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task PostSolicitudCambioModo_ApuntaElEventoDeCalidad(bool correoSale)
        {
            Func<IEscritorEventosCalidad> escritorAntes = RegistroCalidad.Escritor;
            Action<Action> segundoPlanoAntes = RegistroCalidad.EnSegundoPlano;
            try
            {
                IEscritorEventosCalidad escritor = A.Fake<IEscritorEventosCalidad>();
                EventoCalidad evento = null;
                A.CallTo(() => escritor.Escribir(A<EventoCalidad>._)).Invokes((EventoCalidad e) => evento = e);
                RegistroCalidad.Escritor = () => escritor;
                RegistroCalidad.EnSegundoPlano = trabajo => trabajo();
                IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
                A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(correoSale);

                _ = await Controlador(correo).PostSolicitudCambioModo(new SolicitudCambioModoDTO
                {
                    Empresa = "1", Pedido = 926879, ModoDeseado = Constantes.Pedidos.ModosServicio.TODO_JUNTO
                });

                Assert.IsNotNull(evento);
                Assert.AreEqual(TiposEventoCalidad.CAMBIO_DE_MODO_CON_PICKING, evento.Tipo);
                Assert.AreEqual("dlopez", evento.Usuario);
                Assert.AreEqual(926879, evento.Pedido);
                Assert.AreEqual("40483", evento.Cliente);
                StringAssert.Contains(evento.Detalle, "\"ModoActual\":2");
                StringAssert.Contains(evento.Detalle, "\"ModoPedido\":1");
                StringAssert.Contains(evento.Detalle, "\"CorreoEnviado\":" + (correoSale ? "true" : "false"));
            }
            finally
            {
                RegistroCalidad.Escritor = escritorAntes;
                RegistroCalidad.EnSegundoPlano = segundoPlanoAntes;
            }
        }

        [TestMethod]
        public async Task PostSolicitudCambioModo_ModoRaro_BadRequest()
        {
            var controller = new PedidosVentaSolicitudesController(A.Fake<NVEntities>(), A.Fake<IServicioCorreoElectronico>());

            var resultado = await controller.PostSolicitudCambioModo(new SolicitudCambioModoDTO { Empresa = "1", Pedido = 1, ModoDeseado = 9 });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
    }
}
