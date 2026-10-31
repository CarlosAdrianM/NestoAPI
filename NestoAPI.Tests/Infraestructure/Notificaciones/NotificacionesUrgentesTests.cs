using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure.Notificaciones
{
    /// <summary>
    /// NestoAPI#775 (Carlos 31/10/26): notificaciones urgentes de la campana de Nesto (lista blanca por tipo, ventana roja), «hay alguien
    /// conectado» para el repartidor, «notificacionResuelta» a todos y el escalado a Dirección a los 2 minutos.
    /// </summary>
    [TestClass]
    public class NotificacionesUrgentesTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 31, 10, 15, 0);
        private const string REPARTIDOR = @"NUEVAVISION\Repartidor";

        private ServicioRepartoPropioTests.RepositorioFalso reparto;
        private CobroTarjetaRepartoTests.AutorizacionesFalsas autorizaciones;
        private IAvisadorAutorizacionesReembolso avisador;
        private List<int> escalados;
        private ServicioAutorizacionesReembolso servicio;

        [TestInitialize]
        public void Setup()
        {
            UsuariosConectadosNesto.Vaciar();
            reparto = new ServicioRepartoPropioTests.RepositorioFalso();
            autorizaciones = new CobroTarjetaRepartoTests.AutorizacionesFalsas();
            avisador = A.Fake<IAvisadorAutorizacionesReembolso>();
            A.CallTo(() => avisador.ConectadosAdministracion()).Returns(new List<string> { "Laura", "Marta" });
            escalados = new List<int>();
            servicio = new ServicioAutorizacionesReembolso(reparto, autorizaciones, avisador, () => AHORA, id => escalados.Add(id));
            reparto.Con(ServicioRepartoPropioTests.Envio(reembolso: 45.30m));
        }

        [TestCleanup]
        public void Limpiar() => UsuariosConectadosNesto.Vaciar();

        // ================================================================ lista blanca

        [TestMethod]
        public void ListaBlanca_SoloLaAutorizacionDeReembolsoEsUrgente()
        {
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(TextosAutorizacionReembolso.TIPO_NOTIFICACION));
            Assert.IsFalse(NotificacionesUrgentes.EsUrgente(TextosAutorizacionReembolso.TIPO_NOTIFICACION_TELEFONICA), "La revisión de una telefónica no corre prisa");
            Assert.IsFalse(NotificacionesUrgentes.EsUrgente("FurgonetaSinDescargar"), "#772: normal");
            Assert.IsFalse(NotificacionesUrgentes.EsUrgente((string)null));
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(new NotificacionPushDTO { Datos = new Dictionary<string, string> { ["tipo"] = "AutorizacionReembolsoReparto" } }),
                "Vale con Datos[\"tipo\"]");
        }

        [TestMethod]
        public void Solicitud_LlevaLaReferenciaYLosDatosDeLaVentana()
        {
            var a = new AutorizacionReembolsoDTO { Id = 12, EnvioId = 251300, Importe = 45.3m, Repartidor = "Repartidor", Nombre = "Peluquería Lucía", Motivo = "Transferencia", Cliente = "15191" };

            NotificacionPushDTO n = TextosAutorizacionReembolso.Solicitud(a);

            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(n));
            Assert.AreEqual("12", n.Datos["referencia"]);
            Assert.AreEqual("Repartidor", n.Datos["repartidor"]);
            Assert.AreEqual("Peluquería Lucía", n.Datos["nombre"]);
            Assert.AreEqual("Transferencia", n.Datos["motivo"]);
            Assert.IsFalse(n.Datos.ContainsKey("escalada"));
            NotificacionPushDTO escalado = TextosAutorizacionReembolso.Escalado(a);
            Assert.AreEqual("1", escalado.Datos["escalada"]);
            StringAssert.StartsWith(escalado.Titulo, "SIN RESPUESTA");
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(escalado));
        }

        [TestMethod]
        public async Task Buzon_UnaUrgenteDeNesto_AvisaTambienConHayNotificacionUrgente_YUnaNormalNo()
        {
            var db = A.Fake<NVEntities>();
            var buzon = A.Fake<System.Data.Entity.DbSet<NotificacionBuzon>>();
            A.CallTo(() => db.NotificacionesBuzon).Returns(buzon);
            var push = new ServicioNotificacionesPush(() => db);
            var avisos = A.Fake<IAvisosTiempoReal>();
            push.AvisosTiempoReal = avisos;

            await push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Laura", "Nesto", new NotificacionPushDTO { Titulo = "Normal", Tipo = "FurgonetaSinDescargar" });
            A.CallTo(() => avisos.HayNotificacionUrgente(A<IEnumerable<string>>._)).MustNotHaveHappened();

            await push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Laura", "Nesto", TextosAutorizacionReembolso.Solicitud(new AutorizacionReembolsoDTO { Id = 1, EnvioId = 2 }));
            A.CallTo(() => avisos.HayNotificacionUrgente(A<IEnumerable<string>>.That.Matches(u => u.Single() == @"NUEVAVISION\Laura"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.HayNotificacionesNuevas(A<IEnumerable<string>>._)).MustHaveHappenedTwiceExactly();
        }

        // ================================================================ conexiones vivas

        [TestMethod]
        public void Conectados_AltaBajaYReconexion_VariasConexionesDelMismoUsuarioCuentanUnaVez()
        {
            UsuariosConectadosNesto.Registrar("c1", @"NUEVAVISION\Laura");
            UsuariosConectadosNesto.Registrar("c2", @"nuevavision\laura"); // segundo Nesto de Laura
            UsuariosConectadosNesto.Registrar("c3", @"NUEVAVISION\Alfredo");

            CollectionAssert.AreEqual(new[] { "Laura" }, UsuariosConectadosNesto.Conectados(new[] { @"NUEVAVISION\Laura", @"NUEVAVISION\Marta", "laura" }),
                "Con o sin dominio, una persona cuenta una vez; Marta no está");
            Assert.AreEqual(2, UsuariosConectadosNesto.ConexionesDe("Laura"));

            UsuariosConectadosNesto.Quitar("c1");
            CollectionAssert.AreEqual(new[] { "Laura" }, UsuariosConectadosNesto.Conectados(new[] { @"NUEVAVISION\Laura" }), "Le queda el otro Nesto");
            UsuariosConectadosNesto.Quitar("c2");
            Assert.AreEqual(0, UsuariosConectadosNesto.Conectados(new[] { @"NUEVAVISION\Laura" }).Count);

            UsuariosConectadosNesto.Registrar("c2", @"NUEVAVISION\Laura"); // OnReconnected vuelve a registrar
            UsuariosConectadosNesto.Registrar("c2", @"NUEVAVISION\Laura");
            Assert.AreEqual(1, UsuariosConectadosNesto.ConexionesDe(@"NUEVAVISION\Laura"), "Reconectar con la misma conexión no la duplica");
        }

        [TestMethod]
        public void Avisador_ConectadosSoloCuentaAAdministracion()
        {
            UsuariosConectadosNesto.Registrar("c1", @"NUEVAVISION\Laura");
            UsuariosConectadosNesto.Registrar("c2", @"NUEVAVISION\Carlos");
            var real = new AvisadorAutorizacionesReembolso(A.Fake<IServicioNotificacionesPush>(), Miembros, A.Fake<IAvisosCobroReparto>(), A.Fake<IAvisosTiempoReal>());

            CollectionAssert.AreEqual(new[] { "Laura" }, real.ConectadosAdministracion(), "Carlos (solo Dirección) no recibe el primer aviso");
        }

        // ================================================================ Conectados en la respuesta

        [TestMethod]
        public async Task Pedir_DiceCuantosDeAdministracionLoEstanViendo_YProgramaElEscalado()
        {
            var pedida = await servicio.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" }, REPARTIDOR);

            Assert.AreEqual(TipoResultadoReparto.Ok, pedida.Tipo, pedida.Mensaje);
            Assert.AreEqual(2, pedida.Contenido.Conectados);
            CollectionAssert.AreEqual(new[] { "Laura", "Marta" }, pedida.Contenido.ConectadosNombres);
            CollectionAssert.AreEqual(new[] { pedida.Contenido.Id }, escalados);

            A.CallTo(() => avisador.ConectadosAdministracion()).Returns(new List<string>());
            var otraVez = await servicio.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Otra" }, REPARTIDOR);
            Assert.AreEqual(0, otraVez.Contenido.Conectados, "La misma pendiente, con los conectados de ahora");
            Assert.AreEqual(1, escalados.Count, "Sin volver a programar el escalado");
        }

        [TestMethod]
        public async Task Pedir_SiNoSePuedeProgramarElEscalado_SePideIgual()
        {
            servicio = new ServicioAutorizacionesReembolso(reparto, autorizaciones, avisador, () => AHORA, id => throw new InvalidOperationException("Sin Hangfire"));

            var pedida = await servicio.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" }, REPARTIDOR);

            Assert.AreEqual(TipoResultadoReparto.Ok, pedida.Tipo, pedida.Mensaje);
            A.CallTo(() => avisador.AvisarAdministracion(A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controlador_PostAutorizacion_DevuelveConectados()
        {
            var controller = new RepartoPropioCobrosController(null, null, servicio)
            {
                User = new GenericPrincipal(new GenericIdentity(REPARTIDOR), new[] { Constantes.GruposSeguridad.ALMACEN })
            };

            var respuesta = await controller.PostAutorizacion(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" });

            var ok = respuesta as OkNegotiatedContentResult<AutorizacionReembolsoDTO>;
            Assert.IsNotNull(ok, respuesta.GetType().Name);
            Assert.AreEqual(2, ok.Content.Conectados);
            CollectionAssert.AreEqual(new[] { "Laura", "Marta" }, ok.Content.ConectadosNombres);
        }

        // ================================================================ escalado

        [TestMethod]
        public async Task Escalar_SiSiguePendiente_AvisaADireccion()
        {
            var pedida = await servicio.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" }, REPARTIDOR);

            Assert.IsTrue(await servicio.Escalar(pedida.Contenido.Id));

            A.CallTo(() => avisador.AvisarDireccion(A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == TextosAutorizacionReembolso.TIPO_NOTIFICACION && n.Datos["escalada"] == "1" && n.Datos["referencia"] == pedida.Contenido.Id.ToString())))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Escalar_YaContestada_NoHaceNada()
        {
            var pedida = await servicio.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" }, REPARTIDOR);
            await servicio.Responder(pedida.Contenido.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = true }, @"NUEVAVISION\Laura");

            Assert.IsFalse(await servicio.Escalar(pedida.Contenido.Id));
            Assert.IsFalse(await servicio.Escalar(999), "No existe");

            A.CallTo(() => avisador.AvisarDireccion(A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Avisador_Escalado_VaADireccionMenosLosDeAdministracion_YVuelveASonarEnAdministracion()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var tiempoReal = A.Fake<IAvisosTiempoReal>();
            var real = new AvisadorAutorizacionesReembolso(push, Miembros, A.Fake<IAvisosCobroReparto>(), tiempoReal);

            await real.AvisarDireccion(new NotificacionPushDTO { Titulo = "x" });

            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Carlos", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Laura", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Marta", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => tiempoReal.HayNotificacionUrgente(A<IEnumerable<string>>.That.Matches(u => u.Count() == 2 && u.Contains(@"NUEVAVISION\Laura"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Avisador_LaSolicitudVaSoloAAdministracion()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var real = new AvisadorAutorizacionesReembolso(push, Miembros, A.Fake<IAvisosCobroReparto>(), A.Fake<IAvisosTiempoReal>());

            await real.AvisarAdministracion(new NotificacionPushDTO { Titulo = "x" });

            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedTwiceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Carlos", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        // ================================================================ notificacionResuelta

        [TestMethod]
        public async Task Responder_AvisaATodosDeQueEstaResuelta()
        {
            var pedida = await servicio.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" }, REPARTIDOR);

            await servicio.Responder(pedida.Contenido.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = false }, @"NUEVAVISION\Laura");

            A.CallTo(() => avisador.Resuelta(A<NotificacionResueltaDTO>.That.Matches(r => r.Tipo == "AutorizacionReembolsoReparto"
                && r.Referencia == pedida.Contenido.Id.ToString() && r.Respondio == "Laura" && r.Resultado == "Denegada"))).MustHaveHappenedOnceExactly();

            var segunda = await servicio.Responder(pedida.Contenido.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = true }, @"NUEVAVISION\Marta");
            Assert.AreEqual(TipoResultadoReparto.Conflicto, segunda.Tipo, "El segundo recibe el 409");
            StringAssert.Contains(segunda.Mensaje, "Laura");
            A.CallTo(() => avisador.Resuelta(A<NotificacionResueltaDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Avisador_Resuelta_VaAAdministracionYDireccion()
        {
            var tiempoReal = A.Fake<IAvisosTiempoReal>();
            var real = new AvisadorAutorizacionesReembolso(A.Fake<IServicioNotificacionesPush>(), Miembros, A.Fake<IAvisosCobroReparto>(), tiempoReal);
            var resuelta = new NotificacionResueltaDTO { Tipo = "AutorizacionReembolsoReparto", Referencia = "12", Respondio = "Laura", Resultado = "Autorizada" };

            real.Resuelta(resuelta);

            A.CallTo(() => tiempoReal.NotificacionResuelta(A<IEnumerable<string>>.That.Matches(u => u.Count() == 3 && u.Contains(@"NUEVAVISION\Carlos")), resuelta))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Telefonica_ConUnaPendiente_TambienLaDaPorResuelta()
        {
            var pedida = await servicio.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" }, REPARTIDOR);
            var comprobador = new ComprobadorCobroEntrega(new CobroTarjetaRepartoTests.TarjetasFalsas(), autorizaciones, avisador, () => AHORA);
            EnviosAgencia envio = await reparto.LeerEnvio(251300);

            await comprobador.Confirmar(new ComprobacionCobroEntrega { Autorizacion = autorizaciones.Filas.Single(), Telefonica = "Me lo dijo Laura" }, envio, REPARTIDOR);

            A.CallTo(() => avisador.Resuelta(A<NotificacionResueltaDTO>.That.Matches(r => r.Referencia == pedida.Contenido.Id.ToString() && r.Resultado == "Telefonica")))
                .MustHaveHappenedOnceExactly();
        }

        // ================================================================ GET Urgentes (pendientes al abrir o reconectar)

        [TestMethod]
        public async Task Urgentes_SoloLasQueSiguenPendientes_UnaPorReferencia()
        {
            await servicio.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Transferencia" }, REPARTIDOR); // 1, pendiente
            autorizaciones.Filas.Add(new AutorizacionReembolsoReparto { Id = 2, EnvioId = 1, Estado = EstadosAutorizacionReembolso.AUTORIZADA });
            var buzon = new BuzonFalso
            {
                Filas =
                {
                    Fila(10, "1", AHORA.AddMinutes(-3)),
                    Fila(11, "1", AHORA.AddMinutes(-1), escalada: true), // la misma, escalada
                    Fila(12, "2", AHORA.AddMinutes(-2)),                 // ya autorizada
                    new NotificacionBuzonDTO { Id = 13, FechaCreacion = AHORA, Datos = new Dictionary<string, string> { ["tipo"] = "FurgonetaSinDescargar", ["referencia"] = "1" } },
                    new NotificacionBuzonDTO { Id = 14, FechaCreacion = AHORA, Datos = null }
                }
            };
            var urgentes = new ServicioNotificacionesUrgentes(buzon, new IComprobadorUrgentesPendientes[] { new ComprobadorAutorizacionesPendientes(autorizaciones) }, () => AHORA);

            List<NotificacionUrgenteDTO> pendientes = await urgentes.Pendientes(@"NUEVAVISION\Laura");

            Assert.AreEqual(1, pendientes.Count);
            Assert.AreEqual(11, pendientes[0].IdBuzon, "La más reciente de esa referencia");
            Assert.AreEqual("1", pendientes[0].Referencia);
            Assert.IsTrue(pendientes[0].Escalada);
            Assert.AreEqual(AHORA - NotificacionesUrgentes.ANTIGUEDAD_MAXIMA, buzon.Desde);
        }

        [TestMethod]
        public async Task Urgentes_UnTipoSinComprobadorNoSale()
        {
            var buzon = new BuzonFalso { Filas = { Fila(10, "1", AHORA) } };
            var urgentes = new ServicioNotificacionesUrgentes(buzon, null, () => AHORA);

            Assert.AreEqual(0, (await urgentes.Pendientes("x")).Count);
        }

        [TestMethod]
        public async Task Controlador_GetUrgentes_DelUsuarioDelToken()
        {
            string pedido = null;
            var controller = new NotificacionesController(A.Fake<IServicioNotificacionesPush>())
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Laura"), null),
                LeerUrgentes = u =>
                {
                    pedido = u;
                    return Task.FromResult(new List<NotificacionUrgenteDTO> { new NotificacionUrgenteDTO { Referencia = "1" } });
                }
            };

            var respuesta = await controller.ObtenerUrgentes() as OkNegotiatedContentResult<List<NotificacionUrgenteDTO>>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(@"NUEVAVISION\Laura", pedido);
            Assert.AreEqual("1", respuesta.Content.Single().Referencia);
        }

        // ================================================================ ayudas

        private static List<string> Miembros(string grupo)
            => grupo == Constantes.GruposSeguridad.ADMINISTRACION ? new List<string> { "Laura", "Marta" }
                : grupo == Constantes.GruposSeguridad.DIRECCION ? new List<string> { "Carlos", "Laura" }
                : new List<string>();

        private static NotificacionBuzonDTO Fila(int id, string referencia, DateTime fecha, bool escalada = false)
        {
            var datos = new Dictionary<string, string> { ["tipo"] = TextosAutorizacionReembolso.TIPO_NOTIFICACION, ["referencia"] = referencia };
            if (escalada)
            {
                datos["escalada"] = "1";
            }
            return new NotificacionBuzonDTO { Id = id, Titulo = "Autorización " + referencia, FechaCreacion = fecha, Datos = datos };
        }

        private sealed class BuzonFalso : IRepositorioUrgentesBuzon
        {
            public List<NotificacionBuzonDTO> Filas { get; } = new List<NotificacionBuzonDTO>();
            public DateTime Desde { get; private set; }

            public Task<List<NotificacionBuzonDTO>> Leer(string usuario, DateTime desde)
            {
                Desde = desde;
                return Task.FromResult(Filas.ToList());
            }
        }
    }
}
