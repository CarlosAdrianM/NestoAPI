using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Punto 5 (Carlos 19/11/26): la parada de una nota de recogida. «No se ha recogido» (sale del reparto, sin bultos NR), el comentario al
    /// recogerla y «Anular recogida» de una nota ya en reparto (caso NR930553, parada 251104).
    /// </summary>
    [TestClass]
    public class RecogidasParadaRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 20, 10, 0, 0);
        private const string ANDRE = "Andre";
        private const int PEDIDO = 928000; // el de ServicioRepartoPropioTests.Envio

        private ServicioRepartoPropioTests.RepositorioFalso repositorio;
        private IRepositorioNotasParadaReparto notas;
        private IServicioParadasNotaEntrega paradasNota;
        private IRecogidasParadaReparto recogidas;
        private ServicioRepartoPropio servicio;
        private string tipoNota;

        [TestInitialize]
        public void Setup()
        {
            repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            notas = A.Fake<IRepositorioNotasParadaReparto>();
            paradasNota = A.Fake<IServicioParadasNotaEntrega>();
            recogidas = A.Fake<IRecogidasParadaReparto>();
            tipoNota = TiposNotaEntrega.RECOGIDA;
            A.CallTo(() => notas.LeerNotas(A<IReadOnlyCollection<int>>._)).ReturnsLazily((IReadOnlyCollection<int> p) =>
                Task.FromResult(p.Contains(PEDIDO) && tipoNota != null
                    ? new List<NotaParadaReparto> { new NotaParadaReparto { Pedido = PEDIDO, Tipo = tipoNota } }
                    : new List<NotaParadaReparto>()));
            A.CallTo(() => recogidas.LeerSeguimiento(A<int>._)).Returns(Task.FromResult(new SeguimientoNotaRecogida { Existe = true }));
            A.CallTo(() => recogidas.CerrarParada(A<int>._, A<string>._, A<IList<EnvioHistoria>>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(true));
            A.CallTo(() => paradasNota.Resolver("NR" + PEDIDO)).Returns(Task.FromResult(new ResolucionCodigoNota { Pedido = PEDIDO, EsNota = true, EsRecogida = true, EnvioId = 251104 }));
            servicio = new ServicioRepartoPropio(repositorio, () => AHORA, null, new ServicioRepartoPropioTests.ParametrosFalsos(),
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), null,
                new DependenciasParadasReparto { NotasParada = notas, Notas = paradasNota })
            {
                Recogidas = recogidas
            };
        }

        private EnviosAgencia Envio(int numero = 251104)
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(numero, bultos: 1);
            envio.Fecha = AHORA.Date;
            return repositorio.Con(envio);
        }

        private static NoRecogidaRepartoDTO NoRecogida(string motivo = "El cliente no tenía nada preparado", Guid? clave = null)
            => new NoRecogidaRepartoDTO { ClaveIdempotencia = clave ?? Guid.NewGuid(), Motivo = motivo };

        // ================================================================ reglas

        [TestMethod]
        public void Reglas_TextoDetalleYPrueba()
        {
            Assert.AreEqual("El motivo es obligatorio.", ReglasParadaRecogida.ValidarTexto("  ", true, "El motivo"));
            Assert.IsNull(ReglasParadaRecogida.ValidarTexto(null, false, "El comentario"));
            StringAssert.Contains(ReglasParadaRecogida.ValidarTexto(new string('x', 501), false, "El comentario"), "500");
            Assert.AreEqual(100, ReglasParadaRecogida.Detalle(ReglasParadaRecogida.PREFIJO_NO_RECOGIDO, new string('x', 300)).Length);
            Assert.AreEqual("Recogido (reparto propio): sin el manípulo", ReglasParadaRecogida.Detalle(ReglasParadaRecogida.PREFIJO_RECOGIDO, " sin el manípulo "));
            Assert.AreEqual("Llamar antes\nComentario: sin el manípulo", ReglasParadaRecogida.TextoPrueba("Llamar antes", "sin el manípulo"));
            Assert.AreEqual("Comentario: sin el manípulo", ReglasParadaRecogida.TextoPrueba(null, "sin el manípulo"));
            Assert.IsNull(ReglasParadaRecogida.TextoPrueba(" ", null));
        }

        [TestMethod]
        public void Reglas_YaRecogida_PorAbonoOBultosEnFurgoneta()
        {
            Assert.IsNull(ReglasParadaRecogida.MotivoYaRecogida("NR1", null));
            Assert.IsNull(ReglasParadaRecogida.MotivoYaRecogida("NR1", new SeguimientoNotaRecogida()));
            StringAssert.Contains(ReglasParadaRecogida.MotivoYaRecogida("NR1", new SeguimientoNotaRecogida { PedidoAbono = 931000 }), "abonada");
            StringAssert.Contains(ReglasParadaRecogida.MotivoYaRecogida("NR1", new SeguimientoNotaRecogida { BultosRecogidos = true }), "furgoneta");
        }

        // ================================================================ No se ha recogido

        [TestMethod]
        public async Task NoRecogida_SaleDelRepartoComoCerradoAManoConElMotivo_YAvisaALaSolicitud()
        {
            EnviosAgencia envio = Envio();
            NoRecogidaRepartoDTO peticion = NoRecogida();

            var resultado = await servicio.NoRecogida(251104, peticion, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(ReglasParadaRecogida.ESTADO_NO_RECOGIDO, resultado.Contenido.Estado);
            Assert.AreEqual(Constantes.Agencias.ESTADO_CERRADO_A_MANO, envio.Estado);
            Assert.AreEqual("No recogido (reparto propio): El cliente no tenía nada preparado", envio.DetalleEstado);
            ServicioRepartoPropioTests.Registro r = repositorio.Registros.Single();
            Assert.AreEqual(PruebaEntregaReparto.TIPO_INCIDENCIA, r.Prueba.Tipo);
            Assert.AreEqual(IncidenciaRepartoDTO.OTRO, r.Prueba.Motivo, "Un motivo que ya admite la CHECK");
            StringAssert.Contains(r.Historia[0].Observaciones, "no se ha recogido");
            A.CallTo(() => recogidas.AvisarSolicitud(PEDIDO, false, "El cliente no tenía nada preparado", ANDRE)).MustHaveHappenedOnceExactly();

            // El reintento con la misma clave: la misma respuesta, sin otro registro ni otro aviso
            var otraVez = await servicio.NoRecogida(251104, peticion, ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Ok, otraVez.Tipo, otraVez.Mensaje);
            Assert.AreEqual(ReglasParadaRecogida.ESTADO_NO_RECOGIDO, otraVez.Contenido.Estado);
            Assert.AreEqual(1, repositorio.Registros.Count);
            A.CallTo(() => recogidas.AvisarSolicitud(A<int>._, A<bool>._, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task NoRecogida_SinMotivo400_YEnUnaParadaQueNoEsRecogida400()
        {
            Envio();
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.NoRecogida(251104, NoRecogida(" "), ANDRE)).Tipo);

            tipoNota = TiposNotaEntrega.ENTREGA;
            var noEsRecogida = await servicio.NoRecogida(251104, NoRecogida(), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Invalido, noEsRecogida.Tipo);
            StringAssert.Contains(noEsRecogida.Mensaje, "solo vale para las notas de recogida");
            Assert.AreEqual(0, repositorio.Registros.Count);
        }

        [TestMethod]
        public async Task NoRecogida_ConBultosNrEnLaFurgoneta_409_YNadaCambia()
        {
            EnviosAgencia envio = Envio();
            A.CallTo(() => recogidas.LeerSeguimiento(PEDIDO)).Returns(Task.FromResult(new SeguimientoNotaRecogida { BultosRecogidos = true }));

            var resultado = await servicio.NoRecogida(251104, NoRecogida(), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, resultado.Tipo);
            Assert.AreEqual(ReglasParadaRecogida.CODIGO_YA_RECOGIDA, resultado.Codigo);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
        }

        // ================================================================ comentario al recoger

        [TestMethod]
        public async Task EntregarRecogida_ConComentario_HistoriaDetallePruebaYAviso()
        {
            EnviosAgencia envio = Envio();
            var peticion = new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), BultosRecogidos = 2, Comentario = "recogido, pero sin el manípulo" };

            var resultado = await servicio.Entregar(251104, peticion, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
            Assert.AreEqual("Recogido (reparto propio): recogido, pero sin el manípulo", envio.DetalleEstado);
            ServicioRepartoPropioTests.Registro r = repositorio.Registros.Single();
            Assert.AreEqual("Comentario: recogido, pero sin el manípulo", r.Prueba.Texto);
            StringAssert.Contains(r.Historia[0].Observaciones, "Reparto propio: recogido (2 bultos). Comentario: recogido, pero sin el manípulo");
            A.CallTo(() => recogidas.AvisarSolicitud(PEDIDO, true, "recogido, pero sin el manípulo", ANDRE)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EntregarEnvio_ElComentarioSeIgnora_YSinComentarioComoSiempre()
        {
            tipoNota = null; // un envío normal
            EnviosAgencia envio = Envio();

            var resultado = await servicio.Entregar(251104, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Comentario = "esto no va" }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual("Entregado (reparto propio)", envio.DetalleEstado);
            Assert.IsNull(repositorio.Registros.Single().Prueba.Texto);
            A.CallTo(() => recogidas.AvisarSolicitud(A<int>._, A<bool>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Entregar_ComentarioDeMasDe500_400()
        {
            Envio();

            var resultado = await servicio.Entregar(251104, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Comentario = new string('x', 501) }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            Assert.AreEqual(0, repositorio.Registros.Count);
        }

        // ================================================================ anular recogida

        [TestMethod]
        public async Task Anular_CierraLaParadaConRastroYAnulaLaSolicitud()
        {
            Envio();

            var resultado = await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "Ya no hace falta: el cliente lo trae" }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual("NR" + PEDIDO, resultado.Contenido.CodigoDocumento);
            Assert.AreEqual(251104, resultado.Contenido.EnvioId);
            Assert.AreEqual(ReglasParadaRecogida.ESTADO_ANULADA, resultado.Contenido.Estado);
            A.CallTo(() => recogidas.CerrarParada(251104, "Recogida anulada: Ya no hace falta: el cliente lo trae",
                A<IList<EnvioHistoria>>.That.Matches(h => h.Count == 2 && h[0].Observaciones.Contains("anulada")),
                ReglasParadaRecogida.TABLA_MODIFICACIONES_ANULADA, A<string>._, A<string>.That.Contains("Ya no hace falta"), ANDRE)).MustHaveHappenedOnceExactly();
            A.CallTo(() => recogidas.AnularSolicitud(PEDIDO, "Ya no hace falta: el cliente lo trae", ANDRE)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Anular_Abonada_O_ConBultosRecogidos_409SinTocarNada()
        {
            Envio();
            A.CallTo(() => recogidas.LeerSeguimiento(PEDIDO)).Returns(Task.FromResult(new SeguimientoNotaRecogida { PedidoAbono = 931000 }));

            var resultado = await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, resultado.Tipo);
            Assert.AreEqual(ReglasParadaRecogida.CODIGO_YA_RECOGIDA, resultado.Codigo);
            A.CallTo(() => recogidas.CerrarParada(A<int>._, A<string>._, A<IList<EnvioHistoria>>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => recogidas.AnularSolicitud(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_SinMotivo400_NoEsRecogida404_SinParada409_YCarrera409()
        {
            Envio();
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO(), ANDRE)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.AnularRecogida(111, new AnularRecogidaRepartoDTO { Motivo = "x" }, ANDRE)).Tipo);

            A.CallTo(() => paradasNota.Resolver("NR" + PEDIDO)).Returns(Task.FromResult(new ResolucionCodigoNota { Pedido = PEDIDO, EsNota = true, EsRecogida = true }));
            var sinParada = await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" }, ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, sinParada.Tipo);
            StringAssert.Contains(sinParada.Mensaje, "no tiene parada");

            A.CallTo(() => paradasNota.Resolver("NR" + PEDIDO)).Returns(Task.FromResult(new ResolucionCodigoNota { Pedido = PEDIDO, EsNota = true, EsRecogida = true, EnvioId = 251104 }));
            A.CallTo(() => recogidas.CerrarParada(A<int>._, A<string>._, A<IList<EnvioHistoria>>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(false));
            var carrera = await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" }, ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, carrera.Tipo);
            A.CallTo(() => recogidas.AnularSolicitud(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        // ================================================================ aviso a quien pidió la recogida

        [TestMethod]
        public void AvisoParadaRepartidor_AQuienLaPidio_ConElTexto()
        {
            var push = A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>();
            var avisos = new AvisosRecogidasSolicitadas(() => push, n => Task.CompletedTask, (g, u) => { }, trabajo => trabajo());
            var recogida = new RecogidaSolicitadaDTO { Id = 7, Empresa = "1", Cliente = "15191", Contacto = "0", UsuarioSolicita = @"NUEVAVISION\Carlos",
                CodigoDocumento = "NR930553", Estado = EstadosRecogidaSolicitada.CONFIRMADA };

            avisos.ParadaRepartidor(recogida, false, "Cerrado por vacaciones", ANDRE);

            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Carlos", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == AvisosRecogidasSolicitadas.TIPO_NO_RECOGIDA && n.Cuerpo.Contains("Cerrado por vacaciones") && n.Cuerpo.Contains("NR930553"))))
                .MustHaveHappenedOnceExactly();
        }

        // ================================================================ controlador

        private RepartoPropioController Controlador(string grupo)
            => new RepartoPropioController(null, servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(grupo == null
                    ? new[] { new Claim(ClaimTypes.Name, ANDRE) }
                    : new[] { new Claim(ClaimTypes.Name, ANDRE), new Claim(ClaimTypes.Role, grupo) }, "prueba"))
            };

        [TestMethod]
        public async Task Controlador_NoRecogidaYAnular_ConPermiso200_SinPermiso403()
        {
            Envio();

            HttpResponseMessage anular = await (await Controlador(Constantes.GruposSeguridad.ADMINISTRACION)
                .AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" })).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.OK, anular.StatusCode);
            StringAssert.Contains(await anular.Content.ReadAsStringAsync(), "\"Estado\":\"Anulada\"");

            HttpResponseMessage prohibido = await (await Controlador(null).AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" }))
                .ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);

            HttpResponseMessage noRecogida = await (await Controlador(Constantes.GruposSeguridad.ALMACEN).NoRecogida(251104, NoRecogida()))
                .ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.OK, noRecogida.StatusCode);
            StringAssert.Contains(await noRecogida.Content.ReadAsStringAsync(), "\"Estado\":\"NoRecogido\"");
        }

        [TestMethod]
        public async Task Controlador_NoRecogidaYaRecogida_409ConCodigo()
        {
            Envio();
            A.CallTo(() => recogidas.LeerSeguimiento(PEDIDO)).Returns(Task.FromResult(new SeguimientoNotaRecogida { BultosRecogidos = true }));

            HttpResponseMessage respuesta = await (await Controlador(Constantes.GruposSeguridad.ALMACEN).NoRecogida(251104, NoRecogida()))
                .ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "\"Codigo\":\"RECOGIDA_YA_RECOGIDA\"");
        }

        // ================================================================ Carlos 20/11/26: «No se ha recogido» pasa a Almacén, que decide

        private void ConTablaNoRecogidas() => A.CallTo(() => recogidas.PendientesDisponibles()).Returns(true);

        private RecogidaNoRecogidaPendiente Pendiente() => new RecogidaNoRecogidaPendiente
        {
            Id = 3, Pedido = PEDIDO, EnvioId = 251104, Motivo = "Cerrado", Repartidor = ANDRE, ClaveIdempotencia = Guid.NewGuid(),
            Empresa = "1", Cliente = "15191", Contacto = "0", CodPostal = "28110", Ruta = "AT"
        };

        private void ReprogramarDevuelve(bool valor)
            => A.CallTo(() => recogidas.Reprogramar(A<RecogidaNoRecogidaPendiente>._, A<DateTime>._, A<IList<EnvioHistoria>>._, A<IReadOnlyList<MovimientoBultoReparto>>._,
                A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(valor));

        [TestMethod]
        public async Task NoRecogida_ConLaTabla_NoSeCierra_QuedaIncidentadaFueraDeTodosLosDiasYAvisaAAlmacen()
        {
            ConTablaNoRecogidas();
            EnviosAgencia envio = Envio();
            envio.FechaEntrega = AHORA.Date;
            NoRecogidaRepartoDTO peticion = NoRecogida("Cerrado por vacaciones");

            var resultado = await servicio.NoRecogida(251104, peticion, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual("NoRecogido", resultado.Contenido.Estado, "Mismo contrato que hoy");
            Assert.AreEqual(Constantes.Agencias.ESTADO_INCIDENTADO, envio.Estado, "No se cierra");
            Assert.AreEqual(FechaRepartoPropio.FECHA_PENDIENTE_DECIDIR, envio.FechaEntrega, "No vuelve sola al reparto");
            Assert.IsTrue(FechaRepartoPropio.EsDeOtroDia(envio, AHORA), "No sale en la lista de hoy ni se carga");
            StringAssert.Contains(FechaRepartoPropio.MensajeNoSeCargaHoy(envio), "pendiente de que Almacén decida");
            A.CallTo(() => recogidas.ApuntarNoRecogida(A<RecogidaNoRecogidaPendiente>.That.Matches(f => f.Pedido == PEDIDO && f.EnvioId == 251104
                && f.Motivo == "Cerrado por vacaciones" && f.ClaveIdempotencia == peticion.ClaveIdempotencia && f.FechaEntregaAnterior == AHORA.Date)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => recogidas.AvisarAlmacen(A<NotificacionPushDTO>.That.Matches(n => n.Tipo == "RecogidaNoRecogida"
                && n.Cuerpo.Contains("NR" + PEDIDO) && n.Cuerpo.Contains("Cerrado por vacaciones") && n.Cuerpo.Contains("Decida")))).MustHaveHappenedOnceExactly();
            StringAssert.Contains(repositorio.Registros.Single().Historia[0].Observaciones, "Pendiente de que Almacén decida");

            var lista = await servicio.Envios(AHORA, false, ANDRE);
            Assert.IsFalse(lista.Contenido.Any(e => e.EnvioId == 251104), "No sale en el reparto del día");
        }

        [TestMethod]
        public async Task NoRecogidas_SinLaTabla503_ConElla_LaLista()
        {
            Assert.AreEqual(TipoResultadoReparto.NoDisponible, (await servicio.RecogidasNoRecogidas()).Tipo);
            ConTablaNoRecogidas();
            A.CallTo(() => recogidas.LeerNoRecogidas()).Returns(Task.FromResult(new List<RecogidaNoRecogidaDTO> { new RecogidaNoRecogidaDTO { Pedido = PEDIDO } }));

            var resultado = await servicio.RecogidasNoRecogidas();

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo);
            Assert.AreEqual(PEDIDO, resultado.Contenido.Single().Pedido);
        }

        [TestMethod]
        public void FechaReprogramada_SiguienteDiaDeRutaDesdeManana_OSiguienteNoFestivo()
        {
            DateTime viernes = new DateTime(2026, 11, 20);
            Assert.AreEqual(new DateTime(2026, 11, 24), ReglasParadaRecogida.FechaReprogramada(viernes, DayOfWeek.Tuesday, d => false), "Su día de ruta: el martes");
            Assert.AreEqual(new DateTime(2026, 11, 23), ReglasParadaRecogida.FechaReprogramada(viernes, null,
                d => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday), "Sin día de ruta: el siguiente con reparto");
            Assert.AreEqual(new DateTime(2026, 11, 27), ReglasParadaRecogida.FechaReprogramada(viernes, DayOfWeek.Friday, d => false), "Nunca hoy");
            Assert.IsNotNull(ReglasParadaRecogida.ValidarFechaReprogramar(viernes.AddDays(-1), viernes));
            Assert.IsNull(ReglasParadaRecogida.ValidarFechaReprogramar(viernes, viernes));
        }

        [TestMethod]
        public async Task Reprogramar_LaMismaParadaVuelveAlSiguienteDiaDeRuta_SinRepartidor()
        {
            ConTablaNoRecogidas();
            EnviosAgencia envio = Envio();
            envio.Estado = Constantes.Agencias.ESTADO_INCIDENTADO;
            envio.FechaEntrega = FechaRepartoPropio.FECHA_PENDIENTE_DECIDIR;
            A.CallTo(() => recogidas.LeerPendiente(PEDIDO)).Returns(Task.FromResult(Pendiente()));
            ReprogramarDevuelve(true);
            servicio.DiaRutaInternaCliente = (ruta, cp, empresa, cliente, contacto) => ruta == "AT" && cliente == "15191" ? DayOfWeek.Tuesday : (DayOfWeek?)null;

            var resultado = await servicio.ReprogramarRecogida(PEDIDO, null, "Alfredo");

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(new DateTime(2026, 11, 24), resultado.Contenido.FechaEntrega, "El martes siguiente (su día de ruta)");
            Assert.AreEqual("NR" + PEDIDO, resultado.Contenido.CodigoDocumento);
            Assert.AreEqual(251104, resultado.Contenido.EnvioId);
            StringAssert.Contains(resultado.Contenido.Mensaje, "sin repartidor");
            A.CallTo(() => recogidas.Reprogramar(A<RecogidaNoRecogidaPendiente>.That.Matches(p => p.Id == 3), new DateTime(2026, 11, 24),
                A<IList<EnvioHistoria>>.That.Matches(h => h.Any(x => x.Campo == "FechaEntrega") && h[0].Observaciones.Contains("vuelve al reparto")),
                A<IReadOnlyList<MovimientoBultoReparto>>._, A<string>._, A<string>._, "Alfredo")).MustHaveHappenedOnceExactly();

            ReprogramarDevuelve(false);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await servicio.ReprogramarRecogida(PEDIDO, null, "Alfredo")).Tipo, "Carrera: 409");
        }

        [TestMethod]
        public async Task Reprogramar_ConFecha_LaFecha_FechaPasada400_NoPendiente409_SinTabla503()
        {
            Assert.AreEqual(TipoResultadoReparto.NoDisponible, (await servicio.ReprogramarRecogida(PEDIDO, null, ANDRE)).Tipo);
            ConTablaNoRecogidas();
            Envio();
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.ReprogramarRecogida(PEDIDO, new ReprogramarRecogidaDTO { Fecha = AHORA.AddDays(-1) }, ANDRE)).Tipo);
            A.CallTo(() => recogidas.LeerPendiente(PEDIDO)).Returns(Task.FromResult<RecogidaNoRecogidaPendiente>(null));
            var noPendiente = await servicio.ReprogramarRecogida(PEDIDO, null, ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, noPendiente.Tipo);
            StringAssert.Contains(noPendiente.Mensaje, "no está pendiente de decidir");

            A.CallTo(() => recogidas.LeerPendiente(PEDIDO)).Returns(Task.FromResult(Pendiente()));
            ReprogramarDevuelve(true);
            var conFecha = await servicio.ReprogramarRecogida(PEDIDO, new ReprogramarRecogidaDTO { Fecha = new DateTime(2026, 11, 26) }, ANDRE);
            Assert.AreEqual(new DateTime(2026, 11, 26), conFecha.Contenido.FechaEntrega);
        }

        [TestMethod]
        public async Task Anular_UnaPendienteDeDecidir_LaCierraDelTodo()
        {
            ConTablaNoRecogidas();
            EnviosAgencia envio = Envio();
            envio.Estado = Constantes.Agencias.ESTADO_INCIDENTADO;

            var resultado = await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "No hace falta" }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            A.CallTo(() => recogidas.CerrarPendiente(PEDIDO, ANDRE)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controlador_NoRecogidasYReprogramar_SinTabla503_SinPermiso403()
        {
            HttpResponseMessage lista = await (await Controlador(Constantes.GruposSeguridad.ALMACEN).GetRecogidasNoRecogidas()).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, lista.StatusCode);
            HttpResponseMessage reprogramar = await (await Controlador(Constantes.GruposSeguridad.ALMACEN).ReprogramarRecogida(PEDIDO, null)).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, reprogramar.StatusCode);
            HttpResponseMessage prohibido = await (await Controlador(null).GetRecogidasNoRecogidas()).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
        }

        // ================================================================ caso 930553: la recogida marcada «Recogido» por error

        private EnviosAgencia EnvioEntregado()
        {
            EnviosAgencia envio = Envio();
            envio.Estado = Constantes.Agencias.ESTADO_ENTREGADO;
            A.CallTo(() => paradasNota.Resolver("NR" + PEDIDO)).Returns(Task.FromResult(new ResolucionCodigoNota
            {
                Pedido = PEDIDO, EsNota = true, EsRecogida = true, EnvioEntregado = 251104
            }));
            // Marcada «Recogido»: el seguimiento dice que hubo bultos NR (no lo impide); sin abono
            A.CallTo(() => recogidas.LeerSeguimiento(PEDIDO)).Returns(Task.FromResult(new SeguimientoNotaRecogida { Existe = true, BultosRecogidos = true }));
            return envio;
        }

        private void AnularMarcadaDevuelve(AnulacionRecogidaMarcada valor)
            => A.CallTo(() => recogidas.AnularRecogidaMarcada(A<int>._, A<int>._, A<string>._, A<string>._, A<IList<EnvioHistoria>>._,
                A<IReadOnlyList<MovimientoBultoReparto>>._, A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(valor));

        private static MovimientoBultoReparto Nr(int bulto, string tipo, int minuto) => new MovimientoBultoReparto
        {
            Id = bulto * 10 + minuto,
            Codigo = $"NR{PEDIDO}-{bulto}",
            TipoCodigo = CodigoBultoFurgoneta.NR,
            Pedido = PEDIDO,
            Bulto = bulto,
            Tipo = tipo,
            Usuario = ANDRE,
            FechaHora = AHORA.Date.AddHours(9).AddMinutes(minuto),
            ClaveIdempotencia = Guid.NewGuid()
        };

        [TestMethod]
        public void Reglas_AnularMarcada_SoloAlmacenODireccion_YSinAbono()
        {
            StringAssert.Contains(ReglasParadaRecogida.MotivoNoAnulableMarcada("NR930553", false, null), "solo Almacén o Dirección");
            Assert.IsNull(ReglasParadaRecogida.MotivoNoAnulableMarcada("NR930553", true, new SeguimientoNotaRecogida { BultosRecogidos = true }),
                "Los bultos en la furgoneta no lo impiden: se descargan");
            StringAssert.Contains(ReglasParadaRecogida.MotivoNoAnulableMarcada("NR930553", true, new SeguimientoNotaRecogida { PedidoAbono = 931000 }),
                "ya está abonada (abono 931000)");
        }

        [TestMethod]
        public async Task AnularMarcada_Almacen_CierraConDetalle_DescargaLoQueSigueEnLaFurgoneta_YAvisaDeLaSolicitud()
        {
            EnvioEntregado();
            var furgoneta = A.Fake<IRepositorioFurgoneta>();
            A.CallTo(() => furgoneta.ScriptAplicado()).Returns(true);
            A.CallTo(() => furgoneta.LeerHistoria(A<ConsultaMovimientosBulto>.That.Matches(c => c.Pedidos.Contains(PEDIDO)))).Returns(Task.FromResult(new List<MovimientoBultoReparto>
            {
                Nr(1, TiposMovimientoBulto.RECOGIDO, 0),
                Nr(2, TiposMovimientoBulto.RECOGIDO, 0),
                Nr(2, TiposMovimientoBulto.DESCARGADO, 5) // ese ya lo descargó (sin leer el otro)
            }));
            var conFurgoneta = new ServicioRepartoPropio(repositorio, () => AHORA, null, new ServicioRepartoPropioTests.ParametrosFalsos(),
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), null,
                new DependenciasParadasReparto { NotasParada = notas, Notas = paradasNota, Furgoneta = furgoneta })
            {
                Recogidas = recogidas
            };
            AnularMarcadaDevuelve(new AnulacionRecogidaMarcada { SolicitudesAnuladas = new List<int> { 7 }, BultosDescargados = 1 });

            var resultado = await conFurgoneta.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "Lo marcó recogido por error" }, ANDRE,
                puedeAnularMarcada: true);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(ReglasParadaRecogida.ESTADO_ANULADA, resultado.Contenido.Estado);
            StringAssert.Contains(resultado.Contenido.Mensaje, $"NR{PEDIDO}-1");
            A.CallTo(() => recogidas.AnularRecogidaMarcada(251104, PEDIDO, "Recogida anulada (marcada por error): Lo marcó recogido por error",
                "Lo marcó recogido por error", A<IList<EnvioHistoria>>.That.Matches(h => h[0].Observaciones.Contains("por error")),
                A<IReadOnlyList<MovimientoBultoReparto>>.That.Matches(d => d.Count == 1 && d[0].Codigo == $"NR{PEDIDO}-1"
                    && d[0].Tipo == TiposMovimientoBulto.DESCARGADO && d[0].Origen == OrigenesMovimientoBulto.ALMACEN && d[0].EnvioId == 251104),
                A<string>._, A<string>.That.Contains("MarcadaPorError"), ANDRE)).MustHaveHappenedOnceExactly();
            A.CallTo(() => recogidas.AvisarSolicitudAnulada(7, ANDRE)).MustHaveHappenedOnceExactly();
            A.CallTo(() => recogidas.CerrarParada(A<int>._, A<string>._, A<IList<EnvioHistoria>>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => recogidas.AnularSolicitud(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AnularMarcada_SinPermiso403_ConAbono409_SinMotivo400_YCarrera409_SinTocarNada()
        {
            EnvioEntregado();
            AnularMarcadaDevuelve(new AnulacionRecogidaMarcada());

            var sinPermiso = await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" }, ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Prohibido, sinPermiso.Tipo);

            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO(), ANDRE, puedeAnularMarcada: true)).Tipo);

            A.CallTo(() => recogidas.LeerSeguimiento(PEDIDO)).Returns(Task.FromResult(new SeguimientoNotaRecogida { Existe = true, PedidoAbono = 931000 }));
            var abonada = await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" }, ANDRE, puedeAnularMarcada: true);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, abonada.Tipo);
            Assert.AreEqual(ReglasParadaRecogida.CODIGO_ABONADA, abonada.Codigo);
            StringAssert.Contains(abonada.Mensaje, "abono 931000");
            A.CallTo(() => recogidas.AnularRecogidaMarcada(A<int>._, A<int>._, A<string>._, A<string>._, A<IList<EnvioHistoria>>._,
                A<IReadOnlyList<MovimientoBultoReparto>>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();

            A.CallTo(() => recogidas.LeerSeguimiento(PEDIDO)).Returns(Task.FromResult(new SeguimientoNotaRecogida { Existe = true }));
            AnularMarcadaDevuelve(null); // el abono llegó mientras, o la parada cambió
            var carrera = await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" }, ANDRE, puedeAnularMarcada: true);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, carrera.Tipo);
            A.CallTo(() => recogidas.AvisarSolicitudAnulada(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AnularMarcada_SinSeguimiento503()
        {
            EnvioEntregado();
            A.CallTo(() => recogidas.LeerSeguimiento(PEDIDO)).Returns(Task.FromResult<SeguimientoNotaRecogida>(null));

            var resultado = await servicio.AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" }, ANDRE, puedeAnularMarcada: true);

            Assert.AreEqual(TipoResultadoReparto.NoDisponible, resultado.Tipo);
        }

        [TestMethod]
        public async Task Controlador_AnularMarcada_Almacen200_Administracion403()
        {
            EnvioEntregado();
            AnularMarcadaDevuelve(new AnulacionRecogidaMarcada());

            HttpResponseMessage administracion = await (await Controlador(Constantes.GruposSeguridad.ADMINISTRACION)
                .AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" })).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.Forbidden, administracion.StatusCode);
            StringAssert.Contains(await administracion.Content.ReadAsStringAsync(), "Almacén o Dirección");

            HttpResponseMessage almacen = await (await Controlador(Constantes.GruposSeguridad.ALMACEN)
                .AnularRecogida(PEDIDO, new AnularRecogidaRepartoDTO { Motivo = "x" })).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.OK, almacen.StatusCode);
        }
    }
}
