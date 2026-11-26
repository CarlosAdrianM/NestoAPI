using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#880 corte 2: los bultos que faltan en una entrega incompleta quedan pendientes, se ven con dónde están ahora, se cierran a
    /// mano con motivo (Almacén) y, mientras quede alguno, el seguimiento público dice «Entregado parcialmente».
    /// </summary>
    [TestClass]
    public class BultosPendientesRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 26, 10, 30, 0);

        private static EnviosAgencia Envio(int numero = 251099, short bultos = 5) => new EnviosAgencia
        {
            Numero = numero, Empresa = "1  ", Pedido = 930942, Cliente = "15191 ", Contacto = "0  ", Nombre = "PELUQUERÍA ANA", Bultos = bultos,
            Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Estado = Constantes.Agencias.ESTADO_ENTREGADO
        };

        private static EvaluacionBultosEntrega Incompleta(int envio = 251099)
        {
            var evaluacion = new EvaluacionBultosEntrega { EnvioId = envio, Total = 5, HayDatos = true };
            for (int n = 1; n <= 4; n++)
            {
                evaluacion.Bultos.Add(new BultoEntregaEvaluado { Bulto = n, Codigo = $"RP{envio}-{n}", Situacion = SituacionBultoEntrega.Leido });
            }
            evaluacion.Bultos.Add(new BultoEntregaEvaluado { Bulto = 5, Codigo = $"RP{envio}-5", Situacion = SituacionBultoEntrega.NoSeCargo });
            return evaluacion;
        }

        private static BultoPendienteReparto Fila(string estado = ReglasBultosPendientes.ESTADO_PENDIENTE) => new BultoPendienteReparto
        {
            Id = 17, Empresa = "1", EnvioId = 251099, Pedido = 930942, Cliente = "15191", Contacto = "0", Nombre = "PELUQUERÍA ANA", Codigo = "RP251099-5",
            Bulto = 5, Total = 5, SituacionAlEntregar = "NoSeCargo", Repartidor = @"NUEVAVISION\Andre", FechaEntrega = new DateTime(2026, 11, 25, 12, 0, 0), Estado = estado,
            Cierre = estado == ReglasBultosPendientes.ESTADO_CERRADO ? ReglasBultosPendientes.CIERRE_PERDIDO : null,
            UsuarioCierre = estado == ReglasBultosPendientes.ESTADO_CERRADO ? @"NUEVAVISION\Alfredo" : null
        };

        private static (ServicioBultosPendientesReparto servicio, IRepositorioBultosPendientes repositorio, IRepositorioFurgoneta furgoneta) Servicio(bool disponible = true)
        {
            IRepositorioBultosPendientes repositorio = A.Fake<IRepositorioBultosPendientes>();
            A.CallTo(() => repositorio.Disponible()).Returns(disponible);
            IRepositorioFurgoneta furgoneta = A.Fake<IRepositorioFurgoneta>();
            A.CallTo(() => furgoneta.ScriptAplicado()).Returns(true);
            return (new ServicioBultosPendientesReparto(repositorio, furgoneta, () => AHORA), repositorio, furgoneta);
        }

        // ===================================================================== LA REGLA

        [TestMethod]
        public void DesdeEvaluacion_UnaFilaPorBultoQueFalta_ConElPorQue()
        {
            List<BultoPendienteReparto> filas = ReglasBultosPendientes.DesdeEvaluacion(Envio(), Incompleta(), @"NUEVAVISION\Andre", AHORA);

            Assert.AreEqual(1, filas.Count);
            Assert.AreEqual("RP251099-5", filas[0].Codigo);
            Assert.AreEqual("NoSeCargo", filas[0].SituacionAlEntregar);
            Assert.AreEqual(5, filas[0].Total);
            Assert.AreEqual("1", filas[0].Empresa);
            Assert.AreEqual("15191", filas[0].Cliente);
            Assert.AreEqual(ReglasBultosPendientes.ESTADO_PENDIENTE, filas[0].Estado);
        }

        [TestMethod]
        public void DesdeEvaluacion_EntregaCompletaOSinDatos_Nada()
        {
            EvaluacionBultosEntrega completa = Incompleta();
            completa.Bultos[4].Situacion = SituacionBultoEntrega.EntregadoSinLeer;
            Assert.AreEqual(0, ReglasBultosPendientes.DesdeEvaluacion(Envio(), completa, "Andre", AHORA).Count);

            EvaluacionBultosEntrega sinDatos = Incompleta();
            sinDatos.HayDatos = false;
            Assert.AreEqual(0, ReglasBultosPendientes.DesdeEvaluacion(Envio(), sinDatos, "Andre", AHORA).Count, "sin leer nada al cargar ni al entregar no cuenta (decisión 5)");
        }

        [TestMethod]
        public void MotivoNoSePuedeCerrar_CierreYMotivoObligatorios_YaCerrado409()
        {
            Assert.IsNotNull(ReglasBultosPendientes.MotivoNoSePuedeCerrar(Fila(), new CerrarBultoPendienteDTO { Cierre = "Roto", Motivo = "x" }, out _));
            Assert.IsNotNull(ReglasBultosPendientes.MotivoNoSePuedeCerrar(Fila(), new CerrarBultoPendienteDTO { Cierre = "Perdido", Motivo = " " }, out _));
            Assert.IsNotNull(ReglasBultosPendientes.MotivoNoSePuedeCerrar(Fila(), new CerrarBultoPendienteDTO { Cierre = "Perdido", Motivo = new string('a', 501) }, out _));
            Assert.IsNull(ReglasBultosPendientes.MotivoNoSePuedeCerrar(Fila(), new CerrarBultoPendienteDTO { Cierre = "entregadoenmano", Motivo = "Se lo llevó Alfredo" }, out _));

            string motivo = ReglasBultosPendientes.MotivoNoSePuedeCerrar(Fila(ReglasBultosPendientes.ESTADO_CERRADO),
                new CerrarBultoPendienteDTO { Cierre = "Perdido", Motivo = "x" }, out bool conflicto);
            Assert.IsTrue(conflicto);
            Assert.AreEqual("El bulto RP251099-5 ya está cerrado (perdido, Alfredo).", motivo);
        }

        [TestMethod]
        public void DondeEsta_PorElUltimoMovimiento()
        {
            var cargado = new MovimientoBultoReparto { Codigo = "RP251099-5", Tipo = TiposMovimientoBulto.CARGADO, Usuario = @"NUEVAVISION\Miguel", FechaHora = new DateTime(2026, 11, 26, 9, 42, 0) };
            Assert.AreEqual("En la furgoneta de Miguel desde el 26/11 a las 9:42.", ReglasBultosPendientes.DondeEsta(cargado));
            cargado.Tipo = TiposMovimientoBulto.DESCARGADO;
            Assert.AreEqual("En el almacén: lo descargó Miguel el 26/11 a las 9:42.", ReglasBultosPendientes.DondeEsta(cargado));
            Assert.IsNull(ReglasBultosPendientes.DondeEsta(null));
        }

        [TestMethod]
        public void ADto_TextoConElMotivoDeLaFalta()
        {
            BultoPendienteRepartoDTO dto = ReglasBultosPendientes.ADto(Fila(), null);

            Assert.AreEqual("no se cargó", dto.MotivoFalta);
            Assert.AreEqual("Andre", dto.Repartidor);
            Assert.AreEqual("RP251099-5 (bulto 5 de 5) del envío 251099: no se cargó. Pendiente desde el 25/11/2026.", dto.Texto);
        }

        [TestMethod]
        public void EntregaParcial_SoloEntregadoIncompletoConAlgunoPendiente()
        {
            string incompleta = EntregaIncompletaReparto.PREFIJO_DETALLE + "4 de 5 bultos. Falta RP251099-5";
            Assert.IsTrue(ReglasBultosPendientes.EntregaParcial(Constantes.Agencias.ESTADO_ENTREGADO, incompleta, 1));
            Assert.IsFalse(ReglasBultosPendientes.EntregaParcial(Constantes.Agencias.ESTADO_ENTREGADO, incompleta, 0), "cerrados todos: «Entregado»");
            Assert.IsFalse(ReglasBultosPendientes.EntregaParcial(Constantes.Agencias.ESTADO_ENTREGADO, "Entregado (reparto propio)", 1));
            Assert.IsFalse(ReglasBultosPendientes.EntregaParcial(Constantes.Agencias.ESTADO_TRAMITADO, incompleta, 1));
        }

        [TestMethod]
        public void Recordatorio_ListaLoPendiente_YSinNadaNull()
        {
            Assert.IsNull(ReglasBultosPendientes.Recordatorio(new List<BultoPendienteRepartoDTO>()));
            BultoPendienteRepartoDTO dto = ReglasBultosPendientes.ADto(Fila(), null);

            NotificacionPushDTO n = ReglasBultosPendientes.Recordatorio(new[] { dto });

            Assert.AreEqual("Hay 1 bulto pendiente de entregar", n.Titulo);
            StringAssert.Contains(n.Cuerpo, "RP251099-5 (PELUQUERÍA ANA, desde el 25/11)");
            Assert.AreEqual(ReglasBultosPendientes.TIPO_NOTIFICACION_RECORDATORIO, n.Datos["tipo"]);
        }

        [TestMethod]
        public void MovimientoEntregadoEnMano_OrigenAlmacenYClaveEstable()
        {
            MovimientoBultoReparto m = ReglasBultosPendientes.MovimientoEntregadoEnMano(Fila(), "Se lo llevó Alfredo", @"NUEVAVISION\Alfredo", AHORA);

            Assert.AreEqual(TiposMovimientoBulto.ENTREGADO, m.Tipo);
            Assert.AreEqual(OrigenesMovimientoBulto.ALMACEN, m.Origen);
            Assert.AreEqual("RP251099-5", m.Codigo);
            Assert.AreEqual("Entregado en mano: Se lo llevó Alfredo", m.Descripcion);
            Assert.AreEqual(m.ClaveIdempotencia, ReglasBultosPendientes.MovimientoEntregadoEnMano(Fila(), "otro", "otro", AHORA.AddHours(1)).ClaveIdempotencia);
        }

        // ===================================================================== servicio

        [TestMethod]
        public async Task RegistrarDeEntrega_ApuntaLosQueFaltan_YNuncaLanza()
        {
            var (servicio, repositorio, _) = Servicio();
            A.CallTo(() => repositorio.Registrar(A<IReadOnlyList<BultoPendienteReparto>>._)).Returns(Task.FromResult(1));

            Assert.AreEqual(1, await servicio.RegistrarDeEntrega(Envio(), Incompleta(), "Andre"));
            A.CallTo(() => repositorio.Registrar(A<IReadOnlyList<BultoPendienteReparto>>.That.Matches(l => l.Count == 1 && l[0].Codigo == "RP251099-5"))).MustHaveHappenedOnceExactly();

            A.CallTo(() => repositorio.Registrar(A<IReadOnlyList<BultoPendienteReparto>>._)).Throws(new InvalidOperationException("caída"));
            Assert.AreEqual(0, await servicio.RegistrarDeEntrega(Envio(), Incompleta(), "Andre"));
        }

        [TestMethod]
        public async Task RegistrarDeEntrega_SinTabla_NoApunta()
        {
            var (servicio, repositorio, _) = Servicio(disponible: false);

            Assert.AreEqual(0, await servicio.RegistrarDeEntrega(Envio(), Incompleta(), "Andre"));
            A.CallTo(() => repositorio.Registrar(A<IReadOnlyList<BultoPendienteReparto>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Listar_SinTabla503_YConTablaDiceDondeEsta()
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Servicio(disponible: false).servicio.Listar(new FiltroBultosPendientes())).Estado);

            var (servicio, repositorio, furgoneta) = Servicio();
            A.CallTo(() => repositorio.Leer(A<FiltroBultosPendientes>._, A<DateTime>._)).Returns(Task.FromResult(new List<BultoPendienteReparto> { Fila() }));
            A.CallTo(() => furgoneta.LeerHistoria(A<ConsultaMovimientosBulto>._)).Returns(Task.FromResult(new List<MovimientoBultoReparto>
            {
                new MovimientoBultoReparto { Id = 1, Codigo = "RP251099-5", Tipo = TiposMovimientoBulto.CARGADO, Usuario = "Miguel", FechaHora = new DateTime(2026, 11, 26, 9, 0, 0) },
                new MovimientoBultoReparto { Id = 2, Codigo = "RP251099-5", Tipo = TiposMovimientoBulto.DESCARGADO, Usuario = "Miguel", FechaHora = new DateTime(2026, 11, 26, 9, 30, 0) }
            }));

            ResultadoBultosPendientes<List<BultoPendienteRepartoDTO>> resultado = await servicio.Listar(new FiltroBultosPendientes());

            Assert.AreEqual(HttpStatusCode.OK, resultado.Estado);
            Assert.AreEqual("En el almacén: lo descargó Miguel el 26/11 a las 9:30.", resultado.Contenido.Single().DondeEsta);
        }

        [TestMethod]
        public async Task Cerrar_SinPermiso403_NoExiste404_YaCerrado409()
        {
            var (servicio, repositorio, _) = Servicio();
            var peticion = new CerrarBultoPendienteDTO { Cierre = "Perdido", Motivo = "No aparece" };

            Assert.AreEqual(HttpStatusCode.Forbidden, (await servicio.Cerrar(17, peticion, "Pepe", false)).Estado);
            A.CallTo(() => repositorio.LeerPorId(17)).Returns(Task.FromResult<BultoPendienteReparto>(null));
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Cerrar(17, peticion, "Alfredo", true)).Estado);
            A.CallTo(() => repositorio.LeerPorId(17)).Returns(Task.FromResult(Fila(ReglasBultosPendientes.ESTADO_CERRADO)));
            Assert.AreEqual(HttpStatusCode.Conflict, (await servicio.Cerrar(17, peticion, "Alfredo", true)).Estado);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Cerrar(17, new CerrarBultoPendienteDTO { Cierre = "Perdido" }, "Alfredo", true)).Estado);
            A.CallTo(() => repositorio.Cerrar(A<int>._, A<string>._, A<string>._, A<string>._, A<DateTime>._, A<MovimientoBultoReparto>._, A<string>._, A<string>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cerrar_EntregadoEnMano_DejaElMovimientoEntregado_PerdidoNo()
        {
            var (servicio, repositorio, _) = Servicio();
            A.CallTo(() => repositorio.LeerPorId(17)).ReturnsLazily(() => Task.FromResult(Fila()));
            A.CallTo(() => repositorio.Cerrar(A<int>._, A<string>._, A<string>._, A<string>._, A<DateTime>._, A<MovimientoBultoReparto>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(true));

            ResultadoBultosPendientes<BultoPendienteRepartoDTO> enMano = await servicio.Cerrar(17,
                new CerrarBultoPendienteDTO { Cierre = "entregadoEnMano", Motivo = "Se lo llevó Alfredo" }, @"NUEVAVISION\Alfredo", true);

            Assert.AreEqual(HttpStatusCode.OK, enMano.Estado);
            Assert.AreEqual(ReglasBultosPendientes.CIERRE_ENTREGADO_EN_MANO, enMano.Contenido.Cierre);
            Assert.AreEqual(ReglasBultosPendientes.ESTADO_CERRADO, enMano.Contenido.Estado);
            A.CallTo(() => repositorio.Cerrar(17, ReglasBultosPendientes.CIERRE_ENTREGADO_EN_MANO, "Se lo llevó Alfredo", @"NUEVAVISION\Alfredo", AHORA,
                A<MovimientoBultoReparto>.That.Matches(m => m != null && m.Tipo == TiposMovimientoBulto.ENTREGADO), A<string>._, A<string>._)).MustHaveHappenedOnceExactly();

            _ = await servicio.Cerrar(17, new CerrarBultoPendienteDTO { Cierre = "Perdido", Motivo = "No aparece" }, "Alfredo", true);
            A.CallTo(() => repositorio.Cerrar(17, ReglasBultosPendientes.CIERRE_PERDIDO, A<string>._, A<string>._, A<DateTime>._, null, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Cerrar_OtroLoCerroALaVez_409()
        {
            var (servicio, repositorio, _) = Servicio();
            A.CallTo(() => repositorio.LeerPorId(17)).ReturnsNextFromSequence(Fila(), Fila(ReglasBultosPendientes.ESTADO_CERRADO));
            A.CallTo(() => repositorio.Cerrar(A<int>._, A<string>._, A<string>._, A<string>._, A<DateTime>._, A<MovimientoBultoReparto>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(false));

            ResultadoBultosPendientes<BultoPendienteRepartoDTO> r = await servicio.Cerrar(17, new CerrarBultoPendienteDTO { Cierre = "Abonado", Motivo = "Abonado en la 930950" }, "Alfredo", true);

            Assert.AreEqual(HttpStatusCode.Conflict, r.Estado);
            StringAssert.Contains(r.Mensaje, "ya está cerrado");
        }

        [TestMethod]
        public async Task Recordar_CampanaAAlmacenConLoPendiente()
        {
            var (servicio, repositorio, _) = Servicio();
            A.CallTo(() => repositorio.Leer(A<FiltroBultosPendientes>._, A<DateTime>._)).Returns(Task.FromResult(new List<BultoPendienteReparto> { Fila() }));
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();

            int avisados = await servicio.Recordar(() => push, g => new List<string> { "Alfredo", @"NUEVAVISION\alfredo" });

            Assert.AreEqual(1, avisados);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.That.Matches(n => n.Tipo == ReglasBultosPendientes.TIPO_NOTIFICACION_RECORDATORIO)))
                .MustHaveHappenedOnceExactly();
        }

        // ===================================================================== seguimiento público (decisión 8)

        [TestMethod]
        public void Vista_EntregaParcial_EntregadoParcialmente()
        {
            EnviosAgencia envio = Envio();
            envio.DetalleEstado = EntregaIncompletaReparto.PREFIJO_DETALLE + "4 de 5 bultos. Falta RP251099-5";

            VistaSeguimientoReparto parcial = VistaSeguimientoReparto.Construir(envio, null, entregaParcial: true);
            VistaSeguimientoReparto entera = VistaSeguimientoReparto.Construir(envio, null);

            Assert.AreEqual(VistaSeguimientoReparto.TITULO_ENTREGA_PARCIAL, parcial.TituloEstado);
            Assert.IsTrue(parcial.MostrarContacto);
            Assert.AreEqual("Entregado", entera.TituloEstado);
        }
    }
}
