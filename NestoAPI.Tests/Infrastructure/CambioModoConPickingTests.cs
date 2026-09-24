using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
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

        [TestMethod]
        public async Task PostSolicitudCambioModo_MandaElCorreoConRespuestaAlUsuario()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<CabPedidoVta> cabeceras = A.Fake<DbSet<CabPedidoVta>>(o => o.Implements<IQueryable<CabPedidoVta>>().Implements<IDbAsyncEnumerable<CabPedidoVta>>());
            IQueryable<CabPedidoVta> datos = new List<CabPedidoVta>
            {
                new CabPedidoVta { Empresa = "1", Número = 926879, Nº_Cliente = "40483", ModoServicio = 2, ServirJunto = false }
            }.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<CabPedidoVta>)cabeceras).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<CabPedidoVta>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<CabPedidoVta>)cabeceras).Provider).Returns(new TestDbAsyncQueryProvider<CabPedidoVta>(datos.Provider));
            A.CallTo(() => ((IQueryable<CabPedidoVta>)cabeceras).Expression).Returns(datos.Expression);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)cabeceras).ElementType).Returns(datos.ElementType);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)cabeceras).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => db.CabPedidoVtas).Returns(cabeceras);
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);
            var controller = new PedidosVentaSolicitudesController(db, correo)
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

            var resultado = await controller.PostSolicitudCambioModo(new SolicitudCambioModoDTO
            {
                Empresa = "1", Pedido = 926879, ModoDeseado = Constantes.Pedidos.ModosServicio.TODO_JUNTO
            });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<string>));
            Assert.AreEqual("daniellopez@nuevavision.es", enviado.ReplyToList.Single().Address);
            StringAssert.Contains(enviado.Subject, "926879");
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
