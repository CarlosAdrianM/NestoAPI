using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#734 (pedido 929559, consulta del 04/11/26 08:38): nadie contestó en siete días y no hubo segundo aviso; además, un
    /// usuario de Almacén liberó la retención a mano 30 segundos después y la consulta abierta siguió bloqueando la etiqueta.
    /// </summary>
    [TestClass]
    public class RecordatoriosConsultasReembolsoTests
    {
        private const int PEDIDO = 929559;
        private const string ALMACEN = "NUEVAVISION\\Alfredo";
        private const string ADMINISTRACION = "NUEVAVISION\\Paloma";
        // Miércoles 04/11/2026 08:38
        private static readonly DateTime Consulta929559 = new DateTime(2026, 11, 4, 8, 38, 0);

        private static readonly Func<DateTime, bool> SinFestivos = d => false;

        private static ConsultaReembolsoListadoDTO Abierta(int id, DateTime fecha, int pedido = PEDIDO)
            => new ConsultaReembolsoListadoDTO
            {
                Id = id, Empresa = "1", Pedido = pedido, Usuario = ALMACEN, Fecha = fecha, ImporteEnlaces = 252.94m, NumeroEnlaces = 1,
                Cliente = "40026", NombreCliente = "CLIENTA"
            };

        // ================================================================ núcleo: qué y a quién

        [TestMethod]
        public void Decidir_SoloLasAbiertasDeAlMenosCuatroHoras_UnaVezCadaUna()
        {
            DateTime ahora = new DateTime(2026, 11, 4, 15, 30, 0);
            var consultas = new List<ConsultaReembolsoListadoDTO>
            {
                Abierta(1, ahora.AddHours(-4)),                 // justo 4 h: sí
                Abierta(1, ahora.AddHours(-4)),                 // repetida: una sola vez
                Abierta(2, ahora.AddHours(-3).AddMinutes(-59)), // aún no
                new ConsultaReembolsoListadoDTO { Id = 3, Pedido = 3, Fecha = ahora.AddDays(-2), Respondida = ahora.AddDays(-1) }, // contestada
                null
            };

            List<ConsultaReembolsoARecordar> r = RecordatoriosConsultasReembolso.Decidir(consultas, ahora, SinFestivos);

            Assert.AreEqual(1, r.Count);
            Assert.AreEqual(1, r[0].Consulta.Id);
            Assert.IsFalse(r[0].AvisarDireccion, "el mismo día, solo Administración");
        }

        [TestMethod]
        public void MasDeUnDiaLaborable_MismaHoraDelSiguienteLaborable()
        {
            // Miércoles 08:38 → jueves 08:38
            Assert.IsFalse(RecordatoriosConsultasReembolso.MasDeUnDiaLaborable(Consulta929559, new DateTime(2026, 11, 5, 8, 37, 0), SinFestivos));
            Assert.IsTrue(RecordatoriosConsultasReembolso.MasDeUnDiaLaborable(Consulta929559, new DateTime(2026, 11, 5, 8, 38, 0), SinFestivos));
            // Viernes 12:00 → el fin de semana no cuenta: lunes 12:00
            DateTime viernes = new DateTime(2026, 11, 6, 12, 0, 0);
            Assert.IsFalse(RecordatoriosConsultasReembolso.MasDeUnDiaLaborable(viernes, new DateTime(2026, 11, 9, 11, 59, 0), SinFestivos));
            Assert.IsTrue(RecordatoriosConsultasReembolso.MasDeUnDiaLaborable(viernes, new DateTime(2026, 11, 9, 12, 0, 0), SinFestivos));
            // Lunes festivo en Algete → martes 12:00
            Func<DateTime, bool> lunesFestivo = d => d == new DateTime(2026, 11, 9);
            Assert.IsFalse(RecordatoriosConsultasReembolso.MasDeUnDiaLaborable(viernes, new DateTime(2026, 11, 10, 11, 0, 0), lunesFestivo));
            Assert.IsTrue(RecordatoriosConsultasReembolso.MasDeUnDiaLaborable(viernes, new DateTime(2026, 11, 10, 12, 0, 0), lunesFestivo));
            // Si el calendario falla, como laborable
            Assert.IsTrue(RecordatoriosConsultasReembolso.MasDeUnDiaLaborable(viernes, new DateTime(2026, 11, 9, 12, 0, 0), d => throw new InvalidOperationException()));
        }

        // ================================================================ pasada del job

        private static (RepositorioConsultasReembolsoEnMemoria repo, AvisadorConsultaReembolsoEnMemoria avisador) Preparar(params ConsultaReembolsoDTO[] filas)
        {
            var repo = new RepositorioConsultasReembolsoEnMemoria();
            foreach (ConsultaReembolsoDTO fila in filas)
            {
                _ = repo.Insertar(fila).Result;
            }
            return (repo, new AvisadorConsultaReembolsoEnMemoria());
        }

        private static ConsultaReembolsoDTO Fila(DateTime fecha, int pedido = PEDIDO)
            => new ConsultaReembolsoDTO { Empresa = "1", Pedido = pedido, Usuario = ALMACEN, Fecha = fecha, ImporteEnlaces = 252.94m, NumeroEnlaces = 1 };

        [TestMethod]
        public async Task Procesar_Caso929559_SieteDiasSinContestar_AdministracionYDireccionConElMismoTipoYAccion()
        {
            var (repo, avisador) = Preparar(Fila(Consulta929559));
            DateTime ahora = new DateTime(2026, 11, 11, 10, 30, 0);

            ResultadoRecordatoriosConsultasReembolso r = await RecordatoriosConsultasReembolsoJobsService.Procesar(repo, avisador, ahora, SinFestivos, ex => Assert.Fail(ex.Message));

            Assert.AreEqual(1, r.Recordadas);
            Assert.AreEqual(1, r.Escaladas);
            NotificacionPushDTO aviso = avisador.AAdministracion.Single();
            Assert.AreSame(aviso, avisador.ADireccion.Single());
            Assert.AreEqual(TextosConsultaReembolso.TIPO_NOTIFICACION, aviso.Tipo);
            Assert.AreEqual(TextosConsultaReembolso.ACCION_CONSULTA, aviso.Datos["accion"], "al pulsarlo se abre el diálogo de respuesta");
            Assert.AreEqual(PEDIDO.ToString(), aviso.Datos["pedido"]);
            Assert.AreEqual("1", aviso.Datos["empresa"]);
            StringAssert.Contains(aviso.Titulo, "929559");
            StringAssert.Contains(aviso.Cuerpo, "Alfredo consultó el 04/11/2026 a las 08:38 (hace 7 días)");
            StringAssert.Contains(aviso.Cuerpo, "del cliente 40026 (CLIENTA)");
            StringAssert.Contains(aviso.Cuerpo, "también se ha avisado a Dirección");
            // Urgente (#775), como la pregunta original, con su referencia y marcada como escalada
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(aviso));
            Assert.AreEqual("1/929559", aviso.Datos[NotificacionesUrgentes.DATO_REFERENCIA]);
            Assert.AreEqual("1", aviso.Datos[NotificacionesUrgentes.DATO_ESCALADA]);
        }

        [TestMethod]
        public async Task Procesar_ElMismoDia_SoloAdministracion_YLasRecientesNo()
        {
            DateTime ahora = new DateTime(2026, 11, 4, 15, 30, 0);
            var (repo, avisador) = Preparar(Fila(Consulta929559), Fila(ahora.AddHours(-1), pedido: 930000));

            ResultadoRecordatoriosConsultasReembolso r = await RecordatoriosConsultasReembolsoJobsService.Procesar(repo, avisador, ahora, SinFestivos, ex => Assert.Fail(ex.Message));

            CollectionAssert.AreEqual(new List<int> { PEDIDO }, r.PedidosRecordados);
            Assert.AreEqual(1, avisador.AAdministracion.Count);
            Assert.AreEqual(0, avisador.ADireccion.Count);
            StringAssert.Contains(avisador.AAdministracion[0].Cuerpo, "(hace 6 horas)");
            Assert.IsFalse(avisador.AAdministracion[0].Cuerpo.Contains("Dirección"));
        }

        [TestMethod]
        public async Task Procesar_ContestadaOSinTabla_NoAvisa()
        {
            var (repo, avisador) = Preparar(Fila(Consulta929559));
            repo.Filas.Single().Respondida = Consulta929559.AddDays(1);
            DateTime ahora = new DateTime(2026, 11, 11, 10, 30, 0);

            Assert.AreEqual(0, (await RecordatoriosConsultasReembolsoJobsService.Procesar(repo, avisador, ahora, SinFestivos)).Recordadas);

            repo.Filas.Single().Respondida = null;
            repo.Existe = false;
            Assert.AreEqual(0, (await RecordatoriosConsultasReembolsoJobsService.Procesar(repo, avisador, ahora, SinFestivos)).Recordadas);
            Assert.AreEqual(0, avisador.AAdministracion.Count);
        }

        [TestMethod]
        public async Task Procesar_SiFallaUnAviso_LoRegistraYSigueConLasDemas()
        {
            var repo = new RepositorioConsultasReembolsoEnMemoria();
            _ = await repo.Insertar(Fila(Consulta929559));
            _ = await repo.Insertar(Fila(Consulta929559.AddHours(1), pedido: 930000));
            var avisador = A.Fake<IAvisadorConsultaReembolso>();
            A.CallTo(() => avisador.AvisarAdministracion(A<NotificacionPushDTO>.That.Matches(n => n.Datos["pedido"] == PEDIDO.ToString())))
                .Throws(new InvalidOperationException("buzón caído"));
            A.CallTo(() => avisador.AvisarAdministracion(A<NotificacionPushDTO>.That.Matches(n => n.Datos["pedido"] == "930000"))).Returns(5);
            var registrados = new List<Exception>();

            ResultadoRecordatoriosConsultasReembolso r = await RecordatoriosConsultasReembolsoJobsService.Procesar(repo, avisador,
                new DateTime(2026, 11, 4, 15, 30, 0), SinFestivos, registrados.Add);

            CollectionAssert.AreEqual(new List<int> { 930000 }, r.PedidosRecordados);
            Assert.AreEqual(1, registrados.Count);
            StringAssert.Contains(registrados[0].Message, "929559");
        }

        [TestMethod]
        public async Task Avisador_DireccionSinLosQueYaSonDeAdministracion()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorConsultaReembolso(push, () => new List<string> { "Paloma", "Carlos" },
                () => new List<string> { "carlos", "NUEVAVISION\\Laura", "Laura", "(vacío)" });
            var notificacion = new NotificacionPushDTO { Titulo = "t" };

            int avisados = await avisador.AvisarDireccion(notificacion);

            Assert.AreEqual(1, avisados);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Laura", Constantes.Aplicaciones.NESTO, notificacion)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>.That.Contains("arlos"), A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        // ================================================================ liberar a mano con la consulta abierta

        [TestMethod]
        public async Task Caso929559_ConsultarLiberarAManoRechazado_ResponderLibera()
        {
            DateTime reloj = Consulta929559;
            var repo = new RepositorioConsultasReembolsoEnMemoria();
            repo.Pedido(PEDIDO);
            repo.Enlace(900, 252.94m, Consulta929559.AddDays(-3));
            var retenidos = new RepositorioPedidosRetenidosEnMemoria();
            _ = retenidos.Cabecera(PEDIDO, Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO);
            var avisador = new AvisadorConsultaReembolsoEnMemoria();
            var consultas = new ServicioConsultasReembolso(repo, retenidos, avisador, reloj: () => reloj, registrar: ex => Assert.Fail(ex.Message));
            var retencion = new ServicioRetencionPedidos(retenidos, null, () => reloj, ex => Assert.Fail(ex.Message),
                bloqueoLiberacionAMano: LiberacionConConsultaAbierta.Comprobador(repo, ex => Assert.Fail(ex.Message)));

            Assert.IsNull((await consultas.Consultar("1", PEDIDO, null, ALMACEN)).Error);

            // 30 segundos después, alguien de Almacén pulsa «Liberar»
            reloj = Consulta929559.AddSeconds(30);
            ResultadoRetencion aMano = await retencion.LiberarAMano("1", PEDIDO, "NUEVAVISION\\Andre");
            Assert.IsTrue(aMano.Conflicto);
            StringAssert.Contains(aMano.Error, "No se puede liberar el pedido 929559 a mano");
            StringAssert.Contains(aMano.Error, "Alfredo consultó a Administración el 04/11/2026 a las 08:38");
            StringAssert.Contains(aMano.Error, "se levanta sola cuando Administración conteste");
            StringAssert.Contains(aMano.Error, "no se puede sacar la etiqueta");
            Assert.IsNotNull(await retenidos.LeerActiva("1", PEDIDO), "sigue retenido");
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.RETENIDO, retenidos.Cabeceras[PEDIDO].ModoServicio);

            // Administración contesta: la propia respuesta libera
            reloj = Consulta929559.AddHours(2);
            ResultadoConsultaReembolso respuesta = await consultas.Responder("1", PEDIDO, new ResponderConsultaReembolsoRequest { ConReembolso = true }, ADMINISTRACION);
            Assert.IsNull(respuesta.Error, respuesta.Error);
            Assert.IsTrue(respuesta.Respuesta.Liberado);
            // La ventana urgente se cierra en todos los Nesto
            NotificacionResueltaDTO resuelta = avisador.Resueltas.Single();
            Assert.AreEqual(TextosConsultaReembolso.TIPO_NOTIFICACION, resuelta.Tipo);
            Assert.AreEqual("1/929559", resuelta.Referencia);
            Assert.AreEqual("ConReembolso", resuelta.Resultado);
            Assert.AreEqual("Paloma", resuelta.Respondio);
            Assert.IsNull(await retenidos.LeerActiva("1", PEDIDO));
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, retenidos.Cabeceras[PEDIDO].ModoServicio);

            // Ya sin consulta abierta, el botón vuelve a funcionar (otra retención cualquiera)
            Assert.IsNull((await retencion.Retener("1", PEDIDO, new RetenerPedidoRequest { Motivo = "Lo pide el cliente" }, ALMACEN)).Error);
            ResultadoRetencion otra = await retencion.LiberarAMano("1", PEDIDO, "NUEVAVISION\\Andre");
            Assert.IsFalse(otra.Conflicto);
            Assert.IsNull(otra.Error, otra.Error);
        }

        [TestMethod]
        public async Task Controlador_LiberarConConsultaAbierta_409ConElMensaje()
        {
            var repo = new RepositorioConsultasReembolsoEnMemoria();
            _ = await repo.Insertar(Fila(Consulta929559));
            var retenidos = new RepositorioPedidosRetenidosEnMemoria();
            _ = retenidos.Cabecera(PEDIDO, Constantes.Pedidos.ModosServicio.RETENIDO);
            _ = await retenidos.Insertar(new RetencionPedidoDTO { Empresa = "1", Pedido = PEDIDO, Motivo = "consulta", Usuario = ALMACEN, Fecha = Consulta929559, ModoServicioAnterior = 1 });
            var controller = new PedidosVentaController(A.Fake<NVEntities>()) { Request = new HttpRequestMessage(), Configuration = new HttpConfiguration() };
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Andre") }, "Test"));
            controller.CrearServicioRetencion = () => new ServicioRetencionPedidos(retenidos, null, () => Consulta929559.AddSeconds(30), ex => { },
                bloqueoLiberacionAMano: LiberacionConConsultaAbierta.Comprobador(repo, ex => { }));

            IHttpActionResult resultado = await controller.PostLiberar("1", PEDIDO, null);

            HttpResponseMessage respuesta = ((ResponseMessageResult)resultado).Response;
            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            HttpError error = await respuesta.Content.ReadAsAsync<HttpError>();
            StringAssert.Contains(error.Message, "No se puede liberar el pedido 929559 a mano");
            Assert.IsNull(retenidos.Filas.Single().Liberado);
        }

        // ================================================================ urgente (#775): solo la pregunta

        [TestMethod]
        public async Task Urgente_LaPreguntaSiLaRespuestaNo()
        {
            var repo = new RepositorioConsultasReembolsoEnMemoria();
            repo.Pedido(PEDIDO);
            repo.Enlace(900, 252.94m, Consulta929559.AddDays(-3));
            var retenidos = new RepositorioPedidosRetenidosEnMemoria();
            _ = retenidos.Cabecera(PEDIDO, Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO);
            var avisador = new AvisadorConsultaReembolsoEnMemoria();
            var servicio = new ServicioConsultasReembolso(repo, retenidos, avisador, reloj: () => Consulta929559, registrar: ex => Assert.Fail(ex.Message));

            _ = await servicio.Consultar("1", PEDIDO, null, ALMACEN);
            _ = await servicio.Responder("1", PEDIDO, new ResponderConsultaReembolsoRequest { ConReembolso = true }, ADMINISTRACION);

            NotificacionPushDTO pregunta = avisador.AAdministracion.Single();
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(pregunta));
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(new NotificacionPushDTO { Datos = pregunta.Datos }), "por Datos (el buzón)");
            Assert.AreEqual("1/929559", pregunta.Datos[NotificacionesUrgentes.DATO_REFERENCIA]);
            NotificacionPushDTO respuesta = avisador.AUsuarios.First().notificacion;
            Assert.IsFalse(NotificacionesUrgentes.EsUrgente(respuesta), "la respuesta a quien preguntó es normal");
            Assert.IsFalse(respuesta.Datos.ContainsKey(NotificacionesUrgentes.DATO_REFERENCIA));
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(NotificacionesUrgentes_TipoAutorizacion(), null), "la autorización sigue igual, sin acción");
        }

        private static string NotificacionesUrgentes_TipoAutorizacion() => NestoAPI.Infraestructure.Agencias.RepartoPropio.TextosAutorizacionReembolso.TIPO_NOTIFICACION;

        [TestMethod]
        public async Task Comprobador_PendienteMientrasLaConsultaEstaAbierta()
        {
            var repo = new RepositorioConsultasReembolsoEnMemoria();
            _ = await repo.Insertar(Fila(Consulta929559));
            _ = await repo.Insertar(Fila(Consulta929559, pedido: 930000));
            repo.Filas.Single(f => f.Pedido == 930000).Respondida = Consulta929559.AddHours(1);
            var comprobador = new ComprobadorConsultasReembolsoAbiertas(repo);

            ISet<string> pendientes = await comprobador.Pendientes(new List<string> { "1/929559", "1/930000", "basura", "1/x" });

            Assert.AreEqual(TextosConsultaReembolso.TIPO_NOTIFICACION, comprobador.Tipo);
            CollectionAssert.AreEquivalent(new List<string> { "1/929559" }, pendientes.ToList());
        }

        [TestMethod]
        public async Task GetUrgentes_SaleLaPreguntaAbierta_NoLaRespuesta()
        {
            var repo = new RepositorioConsultasReembolsoEnMemoria();
            _ = await repo.Insertar(Fila(Consulta929559));
            DateTime ahora = Consulta929559.AddHours(2);
            var pregunta = TextosConsultaReembolso.NotificacionRecordatorio(
                (await repo.LeerListado("1", true)).Single(), ahora, escalado: false);
            var respuestaDatos = new Dictionary<string, string> { ["tipo"] = TextosConsultaReembolso.TIPO_NOTIFICACION, ["accion"] = TextosConsultaReembolso.ACCION_RESPUESTA, ["pedido"] = "929559", ["referencia"] = "1/929559" };
            var buzon = A.Fake<IRepositorioUrgentesBuzon>();
            A.CallTo(() => buzon.Leer(A<string>._, A<DateTime>._)).Returns(new List<NotificacionBuzonDTO>
            {
                new NotificacionBuzonDTO { Id = 1, Titulo = pregunta.Titulo, Cuerpo = pregunta.Cuerpo, Datos = pregunta.Datos, FechaCreacion = ahora },
                new NotificacionBuzonDTO { Id = 2, Titulo = "respuesta", Datos = respuestaDatos, FechaCreacion = ahora }
            });
            var servicio = new ServicioNotificacionesUrgentes(buzon, new IComprobadorUrgentesPendientes[] { new ComprobadorConsultasReembolsoAbiertas(repo) }, () => ahora);

            List<NotificacionUrgenteDTO> urgentes = await servicio.Pendientes(ADMINISTRACION);

            NotificacionUrgenteDTO urgente = urgentes.Single();
            Assert.AreEqual(1, urgente.IdBuzon);
            Assert.AreEqual("1/929559", urgente.Referencia);
            Assert.AreEqual("consulta", urgente.Datos["accion"]);
        }

        [TestMethod]
        public async Task Comprobador_SiFallaLaLectura_DejaLiberarYLoRegistra()
        {
            var repo = A.Fake<IRepositorioConsultasReembolso>();
            A.CallTo(() => repo.LeerAbierta(A<string>._, A<int>._)).Throws(new InvalidOperationException("BD caída"));
            var registrados = new List<Exception>();

            string motivo = await LiberacionConConsultaAbierta.Comprobador(repo, registrados.Add)("1", PEDIDO);

            Assert.IsNull(motivo);
            Assert.AreEqual(1, registrados.Count);
        }
    }
}
