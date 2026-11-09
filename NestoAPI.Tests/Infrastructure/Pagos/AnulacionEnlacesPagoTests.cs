using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>Enlaces de PagosTPV en memoria para la anulación a mano.</summary>
    internal class RepositorioAnulacionEnlacesPagoEnMemoria : IRepositorioAnulacionEnlacesPago
    {
        public Dictionary<int, EnlaceAAnular> Enlaces { get; } = new Dictionary<int, EnlaceAAnular>();
        public List<(int id, string usuario, string motivo, string estadoAnterior)> Anulaciones { get; } = new List<(int, string, string, string)>();
        /// <summary>Simula que entre leer y anular otro lo ha cambiado (lo ha pagado el cliente, por ejemplo).</summary>
        public string CambiarAntesDeAnular { get; set; }
        public DateTime Ahora { get; set; } = new DateTime(2026, 11, 9, 13, 0, 0);

        public EnlaceAAnular Enlace(int id, string estado = EstadosPagoTPV.PENDIENTE, string tipo = TiposPagoTPV.TPV_VIRTUAL)
        {
            var enlace = new EnlaceAAnular
            {
                Id = id, Empresa = "1", Tipo = tipo, Estado = estado, Cliente = "38515", Importe = 428.92m,
                Descripcion = "Pago pedido 928286 de Nueva Visión", NumeroOrden = "A" + id
            };
            Enlaces[id] = enlace;
            return enlace;
        }

        public Task<EnlaceAAnular> Leer(int id)
            => Task.FromResult(Enlaces.TryGetValue(id, out EnlaceAAnular e) ? Copia(e) : null);

        public Task<DateTime?> Anular(EnlaceAAnular enlace, string usuario, string motivo)
        {
            EnlaceAAnular guardado = Enlaces[enlace.Id];
            if (CambiarAntesDeAnular != null)
            {
                guardado.Estado = CambiarAntesDeAnular;
            }
            if (guardado.Estado != EstadosPagoTPV.PENDIENTE || guardado.Tipo != TiposPagoTPV.TPV_VIRTUAL)
            {
                return Task.FromResult((DateTime?)null);
            }
            Anulaciones.Add((enlace.Id, usuario, motivo, guardado.Estado));
            guardado.Estado = EstadosPagoTPV.CANCELADO;
            return Task.FromResult((DateTime?)Ahora);
        }

        private static EnlaceAAnular Copia(EnlaceAAnular e) => new EnlaceAAnular
        {
            Id = e.Id, Empresa = e.Empresa, Tipo = e.Tipo, Estado = e.Estado, Cliente = e.Cliente, Importe = e.Importe,
            Descripcion = e.Descripcion, NumeroOrden = e.NumeroOrden
        };
    }

    /// <summary>
    /// Carlos (09/11/26, sugerencia 1140 de Alfredo): Administración anula ella misma un enlace de pago pendiente (el 867 del
    /// 928286, ya cobrado por reembolso, bloqueaba la etiqueta del 930167 y hubo que anularlo con un script).
    /// </summary>
    [TestClass]
    public class AnulacionEnlacesPagoTests
    {
        private const string LAURA = "NUEVAVISION\\LauraM";
        private const string MOTIVO = "Pedido ya cobrado por reembolso";

        private RepositorioAnulacionEnlacesPagoEnMemoria repo;
        private ServicioAnulacionEnlacesPago servicio;

        [TestInitialize]
        public void Preparar()
        {
            repo = new RepositorioAnulacionEnlacesPagoEnMemoria();
            servicio = new ServicioAnulacionEnlacesPago(repo);
        }

        [TestMethod]
        public async Task Anular_EnlacePendiente_LoCancelaConElUsuarioYElMotivo()
        {
            _ = repo.Enlace(867);

            ResultadoAnulacionEnlace r = await servicio.Anular(867, "  " + MOTIVO + " ", LAURA);

            Assert.IsNull(r.Error);
            Assert.AreEqual(EstadosPagoTPV.CANCELADO, r.Respuesta.Estado);
            Assert.AreEqual(867, r.Respuesta.Id);
            Assert.AreEqual(repo.Ahora, r.Respuesta.FechaActualizacion);
            StringAssert.Contains(r.Respuesta.Mensaje, "anulado");
            Assert.AreEqual(1, repo.Anulaciones.Count);
            Assert.AreEqual(LAURA, repo.Anulaciones[0].usuario);
            Assert.AreEqual(MOTIVO, repo.Anulaciones[0].motivo, "El motivo se guarda recortado");
            Assert.AreEqual(EstadosPagoTPV.CANCELADO, repo.Enlaces[867].Estado);
        }

        [TestMethod]
        public async Task Anular_SinMotivo_Devuelve400YNoToca()
        {
            _ = repo.Enlace(867);

            ResultadoAnulacionEnlace r = await servicio.Anular(867, "   ", LAURA);

            Assert.IsFalse(r.Conflicto);
            Assert.IsNotNull(r.Error);
            StringAssert.Contains(r.Error, "motivo");
            Assert.AreEqual(0, repo.Anulaciones.Count);
        }

        [TestMethod]
        public async Task Anular_MotivoDemasiadoLargo_Devuelve400()
        {
            _ = repo.Enlace(867);

            ResultadoAnulacionEnlace r = await servicio.Anular(867, new string('x', ServicioAnulacionEnlacesPago.LONGITUD_MAXIMA_MOTIVO + 1), LAURA);

            Assert.IsFalse(r.Conflicto);
            Assert.IsNotNull(r.Error);
            Assert.AreEqual(0, repo.Anulaciones.Count);
        }

        [TestMethod]
        public async Task Anular_NoExiste_NoEncontrado()
        {
            ResultadoAnulacionEnlace r = await servicio.Anular(1, MOTIVO, LAURA);

            Assert.IsTrue(r.NoEncontrado);
        }

        [TestMethod]
        public async Task Anular_YaPagado_Conflicto()
        {
            _ = repo.Enlace(867, EstadosPagoTPV.AUTORIZADO);

            ResultadoAnulacionEnlace r = await servicio.Anular(867, MOTIVO, LAURA);

            Assert.IsTrue(r.Conflicto);
            StringAssert.Contains(r.Error, "ya está pagado");
            Assert.AreEqual(0, repo.Anulaciones.Count);
        }

        [TestMethod]
        public async Task Anular_Denegado_Conflicto()
        {
            _ = repo.Enlace(867, EstadosPagoTPV.DENEGADO);

            ResultadoAnulacionEnlace r = await servicio.Anular(867, MOTIVO, LAURA);

            Assert.IsTrue(r.Conflicto);
            StringAssert.Contains(r.Error, "denegado");
        }

        [TestMethod]
        public async Task Anular_DosVeces_LaSegundaEsConflictoYNoVuelveAEscribir()
        {
            _ = repo.Enlace(867);

            _ = await servicio.Anular(867, MOTIVO, LAURA);
            ResultadoAnulacionEnlace segunda = await servicio.Anular(867, MOTIVO, LAURA);

            Assert.IsTrue(segunda.Conflicto);
            StringAssert.Contains(segunda.Error, "ya está anulado");
            Assert.AreEqual(1, repo.Anulaciones.Count, "Una sola fila de auditoría");
        }

        [TestMethod]
        public async Task Anular_NoEsTpvVirtual_Conflicto()
        {
            _ = repo.Enlace(5, EstadosPagoTPV.PENDIENTE, TiposPagoTPV.CARRITO_APLAZAME);
            _ = repo.Enlace(6, "Enviado", "P2F");
            _ = repo.Enlace(7, EstadosPagoTPV.PENDIENTE, TiposPagoTPV.PEDIDO_APP);

            foreach (int id in new[] { 5, 6, 7 })
            {
                ResultadoAnulacionEnlace r = await servicio.Anular(id, MOTIVO, LAURA);
                Assert.IsTrue(r.Conflicto, "Id " + id);
                StringAssert.Contains(r.Error, "solo se pueden anular los enlaces de pago", "Id " + id);
            }
            Assert.AreEqual(0, repo.Anulaciones.Count);
        }

        [TestMethod]
        public async Task Anular_SiLoPaganMientras_ConflictoConElEstadoNuevo()
        {
            _ = repo.Enlace(867);
            repo.CambiarAntesDeAnular = EstadosPagoTPV.AUTORIZADO;

            ResultadoAnulacionEnlace r = await servicio.Anular(867, MOTIVO, LAURA);

            Assert.IsTrue(r.Conflicto);
            StringAssert.Contains(r.Error, "ya no está pendiente");
            Assert.AreEqual(0, repo.Anulaciones.Count);
        }

        [TestMethod]
        public void TextoModificacion_LlevaElEnlaceElMotivoYElEstado()
        {
            EnlaceAAnular enlace = new RepositorioAnulacionEnlacesPagoEnMemoria().Enlace(867);

            (string anterior, string nuevo) = AnulacionEnlacesPago.TextosModificacion(enlace, MOTIVO);

            StringAssert.Contains(anterior, "867");
            StringAssert.Contains(anterior, "Pendiente");
            StringAssert.Contains(nuevo, "Cancelado");
            StringAssert.Contains(nuevo, MOTIVO);
        }

        // ------------------------------------------------------------------ controlador

        private static PagosController Controlador(IServicioAnulacionEnlacesPago anulacion, params string[] grupos)
        {
            return new PagosController(A.Fake<IServicioPagos>(), anulacionEnlaces: anulacion)
            {
                User = new GenericPrincipal(new GenericIdentity(LAURA), grupos.Select(g => "NUEVAVISION\\" + g).ToArray())
            };
        }

        [TestMethod]
        public async Task Controlador_Anular_SinSerAdministracionDireccionNiInformatica_Devuelve403()
        {
            var anulacion = A.Fake<IServicioAnulacionEnlacesPago>();
            PagosController controller = Controlador(anulacion, "Almacén", "Tiendas");

            var r = await controller.AnularEnlace(867, new SolicitudAnularEnlacePago { Motivo = MOTIVO }) as StatusCodeResult;

            Assert.IsNotNull(r);
            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            A.CallTo(() => anulacion.Anular(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_Anular_AdministracionDireccionEInformatica_Pueden()
        {
            foreach (string grupo in new[] { GruposSeguridad.ADMINISTRACION, GruposSeguridad.DIRECCION, GruposSeguridad.INFORMATICA })
            {
                var anulacion = A.Fake<IServicioAnulacionEnlacesPago>();
                _ = A.CallTo(() => anulacion.Anular(867, MOTIVO, A<string>._)).Returns(ResultadoAnulacionEnlace.Exito(
                    new RespuestaAnularEnlacePago { Id = 867, Estado = EstadosPagoTPV.CANCELADO, Mensaje = "ok" }));
                PagosController controller = Controlador(anulacion, grupo);

                var r = await controller.AnularEnlace(867, new SolicitudAnularEnlacePago { Motivo = MOTIVO })
                    as OkNegotiatedContentResult<RespuestaAnularEnlacePago>;

                Assert.IsNotNull(r, grupo);
                Assert.AreEqual(EstadosPagoTPV.CANCELADO, r.Content.Estado);
            }
        }

        [TestMethod]
        public async Task Controlador_Anular_Conflicto_Devuelve409ConElMensaje()
        {
            var anulacion = A.Fake<IServicioAnulacionEnlacesPago>();
            _ = A.CallTo(() => anulacion.Anular(867, MOTIVO, A<string>._)).Returns(ResultadoAnulacionEnlace.EnConflicto("ya está pagado"));
            PagosController controller = Controlador(anulacion, GruposSeguridad.ADMINISTRACION);

            var r = await controller.AnularEnlace(867, new SolicitudAnularEnlacePago { Motivo = MOTIVO })
                as NegotiatedContentResult<string>;

            Assert.IsNotNull(r);
            Assert.AreEqual(HttpStatusCode.Conflict, r.StatusCode);
            Assert.AreEqual("ya está pagado", r.Content);
        }

        [TestMethod]
        public async Task Controlador_Anular_SinCuerpo_Devuelve400()
        {
            var anulacion = A.Fake<IServicioAnulacionEnlacesPago>();
            _ = A.CallTo(() => anulacion.Anular(867, null, A<string>._)).Returns(ResultadoAnulacionEnlace.Fallo("Indique el motivo"));
            PagosController controller = Controlador(anulacion, GruposSeguridad.ADMINISTRACION);

            var r = await controller.AnularEnlace(867, null) as NegotiatedContentResult<string>;

            Assert.IsNotNull(r);
            Assert.AreEqual(HttpStatusCode.BadRequest, r.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_Anular_NoExiste_Devuelve404()
        {
            var anulacion = A.Fake<IServicioAnulacionEnlacesPago>();
            _ = A.CallTo(() => anulacion.Anular(1, MOTIVO, A<string>._)).Returns(ResultadoAnulacionEnlace.SinEnlace());
            PagosController controller = Controlador(anulacion, GruposSeguridad.DIRECCION);

            var r = await controller.AnularEnlace(1, new SolicitudAnularEnlacePago { Motivo = MOTIVO });

            Assert.IsInstanceOfType(r, typeof(NotFoundResult));
        }

        // ------------------------------------------------------------------ notificación de Redsys de un enlace anulado

        [TestMethod]
        public void NotificacionDeAnulado_Pagado_SeApuntaElCobroYSeAvisa()
        {
            DecisionNotificacionAnulado d = AnulacionEnlacesPago.DecidirNotificacion(EstadosPagoTPV.CANCELADO, TiposPagoTPV.TPV_VIRTUAL, pagoAutorizado: true);

            Assert.AreEqual(DecisionNotificacionAnulado.CobrarYAvisar, d);
        }

        [TestMethod]
        public void NotificacionDeAnulado_Denegado_NoSeRegeneraNiSeCambiaElEstado()
        {
            DecisionNotificacionAnulado d = AnulacionEnlacesPago.DecidirNotificacion(EstadosPagoTPV.CANCELADO, TiposPagoTPV.TPV_VIRTUAL, pagoAutorizado: false);

            Assert.AreEqual(DecisionNotificacionAnulado.DejarAnulado, d);
        }

        [TestMethod]
        public void NotificacionDeNoAnulado_ComoSiempre()
        {
            Assert.AreEqual(DecisionNotificacionAnulado.ComoSiempre,
                AnulacionEnlacesPago.DecidirNotificacion(EstadosPagoTPV.PENDIENTE, TiposPagoTPV.TPV_VIRTUAL, pagoAutorizado: true));
            Assert.AreEqual(DecisionNotificacionAnulado.ComoSiempre,
                AnulacionEnlacesPago.DecidirNotificacion(EstadosPagoTPV.CANCELADO, TiposPagoTPV.CARRITO_APLAZAME, pagoAutorizado: false),
                "Aplázame tiene su propio Cancelado y no pasa por aquí");
        }
    }
}
