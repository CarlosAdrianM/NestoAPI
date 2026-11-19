using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#865 (corte 3, temporal): cada correo que capta el job «compras-correos-seguimiento» se reenvía a Compras por el SMTP de
    /// siempre con una cabecera de lo que se ha hecho y el cuerpo original debajo. Ni el histórico ni el reanálisis reenvían.
    /// </summary>
    [TestClass]
    public class ReenvioCorreosSeguimientoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 12, 21, 30, 0);
        private static readonly DateTime DIA_UPS = new DateTime(2026, 11, 16);

        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private ILectorBuzonCompras lector;
        private IServicioCorreoElectronico smtp;
        private List<MailMessage> enviados;
        private List<Exception> errores;
        private bool aplicar;
        private bool reenviar;

        [TestInitialize]
        public void Preparar()
        {
            ReenviadorCorreosSeguimiento.OlvidarFalloApuntado();
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            enviados = new List<MailMessage>();
            errores = new List<Exception>();
            aplicar = true;
            reenviar = true;
            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns("buzon@ejemplo.test");
            smtp = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => smtp.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) =>
            {
                enviados.Add(new MailMessage { Subject = m.Subject, Body = m.Body, IsBodyHtml = m.IsBodyHtml, From = m.From });
                enviados.Last().To.Add(m.To.Single());
                return true;
            });
        }

        private ServicioCorreosSeguimientoCompras Servicio()
        {
            var aplicacion = new ServicioAplicacionCorreosCompras(repositorio, () => aplicar, A.Fake<IAvisadorCorreosPorAsignar>(),
                (e, p, d, c) => { }, () => AHORA, errores.Add);
            var reenviador = new ReenviadorCorreosSeguimiento(smtp, () => reenviar, errores.Add, () => AHORA);
            return new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add, horaCorte: () => new TimeSpan(11, 0, 0),
                esFestivo: _ => false, aplicacion: aplicacion, reenviador: reenviador);
        }

        private async Task Pasada(params CorreoCompras[] correos)
        {
            var lectura = new LecturaBuzonCompras { Correos = correos.ToList() };
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).Returns(Task.FromResult(lectura));
            _ = await Servicio().Pasada(AHORA);
        }

        private static CorreoCompras Ups(string id = "ups1", string codigo = "1Z21E7X76890915153", bool conPedido = true) => CorreosSeguimientoFase3Tests.Correo(
            string.Format(CorreosSeguimientoFase3Tests.UPS_TEXTO, codigo) + (conPedido ? "\nReferencia del pedido: 912001" : string.Empty), "pkginfo@ups.com", new DateTime(2026, 11, 12, 20, 15, 39), "Tu paquete está de camino", id);

        private void UnPedidoDeWeelko() =>
            repositorio.AnadirPedido(912001, "9100", "WEELKO BARCELONA SLU", new DateTime(2026, 11, 11, 7, 39, 0), new DateTime(2026, 11, 20), null);

        private static string Texto(MailMessage m) => WebUtility.HtmlDecode(m.Body);

        #region De punta a punta (job)

        [TestMethod]
        public async Task Job_FechaCambiada_ReenviaACompras_ConLaCabeceraYElCuerpoOriginal()
        {
            UnPedidoDeWeelko();
            CorreoCompras ups = Ups();

            await Pasada(ups);

            MailMessage reenvio = enviados.Single();
            Assert.AreEqual(Constantes.Correos.COMPRAS, reenvio.To.Single().Address);
            Assert.AreEqual("[Nesto: correo de seguimiento] Tu paquete está de camino", reenvio.Subject);
            string texto = Texto(reenvio);
            StringAssert.Contains(texto, "Fecha del pedido de compra 912001: sin fecha y 20/11/2026 → 16/11/2026 (2 líneas).");
            StringAssert.Contains(texto, "Correo original de pkginfo@ups.com, recibido el 12/11/2026 20:15.");
            StringAssert.Contains(texto, "Entrega que anuncia (la agencia): 16/11/2026"); // NestoAPI#857f: dice de quién es
            StringAssert.Contains(texto, "Lunes 16/11/2026", "El cuerpo original va debajo");
            Assert.IsTrue(texto.IndexOf("Fecha del pedido", StringComparison.Ordinal) < texto.IndexOf("Lunes 16/11/2026", StringComparison.Ordinal));
            Assert.AreEqual(0, errores.Count, string.Join(" | ", errores.Select(e => e.Message)));
        }

        [TestMethod]
        public async Task Job_ElMismoCorreoOtraVez_NoSeReenvia()
        {
            UnPedidoDeWeelko();

            await Pasada(Ups());
            await Pasada(Ups());

            Assert.AreEqual(1, enviados.Count);
        }

        [TestMethod]
        public async Task Job_SinPedidoReconocible_PorAsignar()
        {
            UnPedidoDeWeelko();

            await Pasada(Ups(conPedido: false));

            StringAssert.Contains(Texto(enviados.Single()), "Por asignar: no se ha reconocido de qué pedido de compra es.");
        }

        [TestMethod]
        public async Task Job_AplicarApagado_LoDice()
        {
            UnPedidoDeWeelko();
            aplicar = false;

            await Pasada(Ups());

            StringAssert.Contains(Texto(enviados.Single()), "Es del pedido de compra 912001, pero poner la fecha de los correos está apagado");
        }

        [TestMethod]
        public async Task Job_InterruptorDelReenvioApagado_NoReenvia()
        {
            UnPedidoDeWeelko();
            reenviar = false;

            await Pasada(Ups());

            Assert.AreEqual(0, enviados.Count);
            Assert.AreEqual(912001, repositorio.Correos.Single().PedidoAplicado, "Lo demás, como siempre");
        }

        [TestMethod]
        public async Task Job_SiElSmtpFalla_NoRompeLaPasada_YSeApuntaUnaVezAlDia()
        {
            A.CallTo(() => smtp.EnviarCorreoSMTP(A<MailMessage>._)).Throws(new Exception("SMTP caído"));
            UnPedidoDeWeelko();

            await Pasada(Ups("a", "1Z21E7X76890915153"), Ups("b", "1Z21E7X76890089163"));
            await Pasada(Ups("c", "1Z21E7X76893119777"));

            Assert.AreEqual(3, repositorio.Correos.Count, "Se guardan igual");
            Assert.AreEqual(1, errores.Count(e => e.Message.Contains("#865")), string.Join(" | ", errores.Select(e => e.Message)));
        }

        [TestMethod]
        public async Task Historico_NoReenvia()
        {
            UnPedidoDeWeelko();
            ServicioCorreosSeguimientoCompras servicio = Servicio();

            _ = await servicio.GuardarCorreos(new List<CorreoCompras> { Ups() }, new ResultadoPasadaCorreosSeguimiento(),
                _ => Task.FromResult(repositorio.Pedidos.ToList()));

            Assert.AreEqual(1, repositorio.Correos.Count);
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public async Task Job_NuestroPropioReenvio_NoSeCapta()
        {
            CorreoCompras reenvio = Ups();
            reenvio.Asunto = TextosReenvioCorreoSeguimiento.Asunto(reenvio.Asunto);
            reenvio.RemitenteDireccion = "compras@nuevavision.es";

            await Pasada(reenvio);

            Assert.AreEqual(0, repositorio.Correos.Count);
            Assert.AreEqual(0, enviados.Count);
        }

        #endregion

        #region Textos

        [TestMethod]
        public void Textos_CadaDesenlace()
        {
            Assert.AreEqual("Fecha del pedido de compra 220597: 30/12/2026 → 20/11/2026 (1 línea).", TextosReenvioCorreoSeguimiento.QueSeHaHecho(
                new ResultadoCorreoGuardado
                {
                    Desenlace = DesenlaceCorreoSeguimiento.FechaCambiada, Pedido = 220597, FechasAntes = new List<DateTime?> { new DateTime(2026, 12, 30) },
                    FechaNueva = new DateTime(2026, 11, 20), LineasCambiadas = 1
                }));
            Assert.AreEqual("Es del pedido de compra 220597, que ya tenía la fecha 20/11/2026: misma fecha, nada que cambiar.",
                TextosReenvioCorreoSeguimiento.QueSeHaHecho(new ResultadoCorreoGuardado
                {
                    Desenlace = DesenlaceCorreoSeguimiento.MismaFecha, Pedido = 220597, FechaNueva = new DateTime(2026, 11, 20)
                }));
            StringAssert.StartsWith(TextosReenvioCorreoSeguimiento.QueSeHaHecho(new ResultadoCorreoGuardado
            {
                Desenlace = DesenlaceCorreoSeguimiento.PorAsignar, Candidatos = 3
            }), "Por asignar: puede ser de 3 pedidos de compra distintos.");
            Assert.AreEqual("Es un envío nuestro (sale de Nueva Visión), no de una compra: no se toca.",
                TextosReenvioCorreoSeguimiento.QueSeHaHecho(new ResultadoCorreoGuardado { Desenlace = DesenlaceCorreoSeguimiento.SalidaNuestra }));
            Assert.AreEqual("Es de hace más de 3 días: no se aplica (histórico).",
                TextosReenvioCorreoSeguimiento.QueSeHaHecho(new ResultadoCorreoGuardado { Desenlace = DesenlaceCorreoSeguimiento.Antiguo }));
            foreach (DesenlaceCorreoSeguimiento d in Enum.GetValues(typeof(DesenlaceCorreoSeguimiento)))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(TextosReenvioCorreoSeguimiento.QueSeHaHecho(new ResultadoCorreoGuardado { Desenlace = d })), d.ToString());
            }
        }

        [TestMethod]
        public void Textos_CuerpoOriginal_DentroDelBody_OTextoEscapado()
        {
            Assert.AreEqual("<p>Hola</p>", TextosReenvioCorreoSeguimiento.CuerpoOriginal("<html><head><style>x</style></head><BODY class='a'><p>Hola</p></body></html>", true));
            Assert.AreEqual("<p>suelto</p>", TextosReenvioCorreoSeguimiento.CuerpoOriginal("<p>suelto</p>", true));
            StringAssert.Contains(TextosReenvioCorreoSeguimiento.CuerpoOriginal("a < b", false), "a &lt; b");
        }

        [TestMethod]
        public void Textos_MarcaDelAsunto()
        {
            Assert.IsTrue(TextosReenvioCorreoSeguimiento.EsReenvioNuestro("RV: [Nesto: correo de seguimiento] Tu paquete"));
            Assert.IsFalse(TextosReenvioCorreoSeguimiento.EsReenvioNuestro("Tu paquete está de camino"));
            Assert.AreEqual("[Nesto: correo de seguimiento] a b", TextosReenvioCorreoSeguimiento.Asunto("a\r\nb").Replace("  ", " "));
        }

        [TestMethod]
        public void DesenlaceAplicacion_DelResultadoDeAplicar()
        {
            var pedido = new CandidatoPedidoCompra { Empresa = "1", Pedido = 220597 };
            ResultadoCorreoGuardado cambiada = ServicioAplicacionCorreosCompras.DesenlaceAplicacion(new ResultadoOperacionCorreoCompra
            {
                Codigo = CodigoOperacionCorreoCompra.Ok,
                Respuesta = new RespuestaAsignacionCorreoCompraDTO { LineasCambiadas = 2, FechasAnteriores = new List<DateTime?> { new DateTime(2026, 12, 30) } }
            }, pedido, DIA_UPS);
            ResultadoCorreoGuardado misma = ServicioAplicacionCorreosCompras.DesenlaceAplicacion(new ResultadoOperacionCorreoCompra
            {
                Codigo = CodigoOperacionCorreoCompra.Ok, Respuesta = new RespuestaAsignacionCorreoCompraDTO { LineasCambiadas = 0 }
            }, pedido, DIA_UPS);
            ResultadoCorreoGuardado fuera = ServicioAplicacionCorreosCompras.DesenlaceAplicacion(new ResultadoOperacionCorreoCompra
            {
                Codigo = CodigoOperacionCorreoCompra.NoEnElAire
            }, pedido, DIA_UPS);

            Assert.AreEqual(DesenlaceCorreoSeguimiento.FechaCambiada, cambiada.Desenlace);
            Assert.AreEqual(2, cambiada.LineasCambiadas);
            Assert.AreEqual(220597, cambiada.Pedido);
            Assert.AreEqual(DesenlaceCorreoSeguimiento.MismaFecha, misma.Desenlace);
            Assert.AreEqual(DesenlaceCorreoSeguimiento.NoEnElAire, fuera.Desenlace);
        }

        #endregion
    }
}
