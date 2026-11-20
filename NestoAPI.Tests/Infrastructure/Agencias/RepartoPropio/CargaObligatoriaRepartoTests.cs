using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
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
    /// Punto 6 (Carlos 20/11/26): una parada solo es de un repartidor cuando ha leído algo suyo. GET Envios con soloCargados (solo las
    /// suyas), POST Carga/Terminar (las de nadie y aviso a Almacén) y Reparto.CargaObligatoria al entregar (409 NO_CARGADA o cargarla con lo leído).
    /// </summary>
    [TestClass]
    public class CargaObligatoriaRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 29, 8, 0, 0);
        private const string ANDRE = "Andre";
        private const string MIGUEL = @"NUEVAVISION\Miguel";

        private ServicioRepartoPropioTests.RepositorioFalso repositorio;
        private CargaFurgonetaTests.CargaFalsa cargas;
        private IAvisosRepartoSinCargar avisos;
        private DateTime ahora;
        private bool interruptor;
        private ServicioRepartoPropio servicio;

        [TestInitialize]
        public void Setup()
        {
            repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            cargas = new CargaFurgonetaTests.CargaFalsa();
            avisos = A.Fake<IAvisosRepartoSinCargar>();
            ahora = AHORA;
            interruptor = true;
            servicio = new ServicioRepartoPropio(repositorio, () => ahora, null, new ServicioRepartoPropioTests.ParametrosFalsos(),
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), cargas,
                new DependenciasParadasReparto { AvisosSinCargar = avisos, MemoriaSinCargar = new MemoriaAvisosSinCargar() })
            {
                CargaObligatoriaEncendida = () => interruptor
            };
        }

        private EnviosAgencia Envio(int numero, short bultos = 2)
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(numero, bultos: bultos);
            envio.Fecha = AHORA.Date;
            envio.Nombre = "CLIENTE " + numero;
            return repositorio.Con(envio);
        }

        private Task Cargar(string codigo, string usuario)
            => servicio.Cargar(new CargaBultoRepartoDTO { Codigo = codigo, ClaveIdempotencia = Guid.NewGuid() }, usuario);

        private static EntregaRepartoDTO Entrega(params string[] leidos)
            => new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), BultosEntregados = leidos.Length == 0 ? null : leidos.ToList() };

        // ================================================================ reglas puras

        [TestMethod]
        public void Visible_ConSoloCargados_SoloLasDelUsuario_SinElLasDeSiempre()
        {
            Assert.IsTrue(NucleoParadasReparto.Visible("Andre", @"NUEVAVISION\andre", true, true));
            Assert.IsFalse(NucleoParadasReparto.Visible(null, ANDRE, true, true), "Las de nadie ya no salen");
            Assert.IsFalse(NucleoParadasReparto.Visible("Miguel", ANDRE, true, true));
            Assert.IsTrue(NucleoParadasReparto.Visible(null, ANDRE, true, false), "Sin soloCargados, como siempre");
            Assert.IsFalse(NucleoParadasReparto.Visible("Miguel", ANDRE, true, false));
            Assert.IsTrue(NucleoParadasReparto.Visible("Miguel", ANDRE, false, true), "Sin soloMios, todas");
        }

        [TestMethod]
        public void Decidir_CargaObligatoria()
        {
            Assert.AreEqual(DecisionCargaEntrega.Seguir, ReglaCargaObligatoria.Decidir(false, true, null, ANDRE, false), "Apagado: como hoy");
            Assert.AreEqual(DecisionCargaEntrega.Seguir, ReglaCargaObligatoria.Decidir(true, false, null, ANDRE, false), "Notas de cobro: no les afecta");
            Assert.AreEqual(DecisionCargaEntrega.Seguir, ReglaCargaObligatoria.Decidir(true, true, @"NUEVAVISION\Andre", ANDRE, false), "La lleva él");
            Assert.AreEqual(DecisionCargaEntrega.Bloquear, ReglaCargaObligatoria.Decidir(true, true, null, ANDRE, false));
            Assert.AreEqual(DecisionCargaEntrega.Bloquear, ReglaCargaObligatoria.Decidir(true, true, "Miguel", ANDRE, false));
            Assert.AreEqual(DecisionCargaEntrega.CargarAlEntregar, ReglaCargaObligatoria.Decidir(true, true, null, ANDRE, true));
            Assert.AreEqual(DecisionCargaEntrega.CargarAlEntregar, ReglaCargaObligatoria.Decidir(true, true, "Miguel", ANDRE, true));
        }

        [TestMethod]
        public void Interruptor_SoloUnoEsEncendido()
        {
            Assert.IsTrue(ReglaCargaObligatoria.Encendido("1"));
            Assert.IsTrue(ReglaCargaObligatoria.Encendido(" 1 "));
            Assert.IsFalse(ReglaCargaObligatoria.Encendido(null));
            Assert.IsFalse(ReglaCargaObligatoria.Encendido("0"));
            Assert.IsFalse(ReglaCargaObligatoria.Encendido("true"));
        }

        [TestMethod]
        public void Mensajes_DeUsted_SingularYPlural()
        {
            Assert.IsNull(ReglaCargaObligatoria.Mensaje(0));
            Assert.AreEqual("Queda 1 parada sin cargar por nadie: no sabemos quién la lleva.", ReglaCargaObligatoria.Mensaje(1));
            Assert.AreEqual("Quedan 3 paradas sin cargar por nadie: no sabemos quién las lleva.", ReglaCargaObligatoria.Mensaje(3));
            StringAssert.Contains(ReglaCargaObligatoria.MensajeNoCargada(MIGUEL), "la lleva Miguel");
            StringAssert.Contains(ReglaCargaObligatoria.MensajeNoCargada(null), "no la ha cargado nadie");
        }

        [TestMethod]
        public void Memoria_ElMismoConjuntoUnaVezCadaMediaHora_OtroConjuntoSiempre()
        {
            var memoria = new MemoriaAvisosSinCargar();
            string clave = ReglaCargaObligatoria.ClaveAviso(AHORA.Date, new[] { 2, 1 });
            Assert.AreEqual(clave, ReglaCargaObligatoria.ClaveAviso(AHORA.Date, new[] { 1, 2, 2 }), "Sin importar el orden");

            Assert.IsTrue(memoria.Reservar(clave, AHORA));
            Assert.IsFalse(memoria.Reservar(clave, AHORA.AddMinutes(29)));
            Assert.IsTrue(memoria.Reservar(ReglaCargaObligatoria.ClaveAviso(AHORA.Date, new[] { 1 }), AHORA.AddMinutes(5)));
            Assert.IsTrue(memoria.Reservar(clave, AHORA.AddMinutes(30)));
        }

        [TestMethod]
        public void Notificacion_ConElRepartidorYLasParadas_SinDominio()
        {
            NotificacionPushDTO n = ReglaCargaObligatoria.Notificacion(@"NUEVAVISION\Andre", new List<ParadaSinCargarDTO>
            {
                new ParadaSinCargarDTO { EnvioId = 251300, Nombre = "LA BONITA", Poblacion = "ALGETE" },
                new ParadaSinCargarDTO { EnvioId = 251301, CodigoDocumento = "NE930001", Nombre = "EL EDÉN" }
            });

            Assert.AreEqual(AvisosRepartoSinCargar.TIPO_NOTIFICACION, n.Tipo);
            Assert.AreEqual("Andre ha terminado de cargar y quedan 2 paradas sin cargar por nadie: RP251300 LA BONITA (ALGETE), NE930001 EL EDÉN. Compruebe quién las lleva.", n.Cuerpo);
        }

        // ================================================================ GET Envios con soloCargados

        [TestMethod]
        public async Task Envios_SoloMiosYSoloCargados_SoloLasSuyas_SinElParametroComoHoy()
        {
            Envio(251300);
            Envio(251301);
            Envio(251302);
            await Cargar("RP251300-1", ANDRE);
            await Cargar("RP251301-1", MIGUEL);

            var cargadas = await servicio.Envios(null, soloMios: true, usuario: ANDRE, soloCargados: true);
            var deSiempre = await servicio.Envios(null, soloMios: true, usuario: ANDRE);

            CollectionAssert.AreEquivalent(new[] { 251300 }, cargadas.Contenido.Select(e => e.EnvioId).ToList());
            CollectionAssert.AreEquivalent(new[] { 251300, 251302 }, deSiempre.Contenido.Select(e => e.EnvioId).ToList());
        }

        // ================================================================ Carga/Terminar

        [TestMethod]
        public async Task TerminarCarga_LasDeNadie_YAvisaUnaVezCadaMediaHora()
        {
            Envio(251300);
            Envio(251301, bultos: 3);
            Envio(251302);
            await Cargar("RP251300-1", ANDRE);          // a medias, pero tiene dueño: no sale
            await Cargar("RP251302-1", MIGUEL);
            await Cargar("RP251302-2", MIGUEL);

            var resultado = await servicio.TerminarCarga(null, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            ParadaSinCargarDTO parada = resultado.Contenido.SinCargar.Single();
            Assert.AreEqual(251301, parada.EnvioId);
            Assert.AreEqual(3, parada.Bultos);
            Assert.AreEqual(3, parada.BultosSinCargar);
            Assert.AreEqual(1, resultado.Contenido.Total);
            Assert.AreEqual("Queda 1 parada sin cargar por nadie: no sabemos quién la lleva.", resultado.Contenido.Mensaje);
            A.CallTo(() => avisos.Avisar(ANDRE, A<IReadOnlyList<ParadaSinCargarDTO>>.That.Matches(l => l.Count == 1))).MustHaveHappenedOnceExactly();

            ahora = AHORA.AddMinutes(10);
            _ = await servicio.TerminarCarga(new TerminarCargaRepartoDTO(), MIGUEL);
            A.CallTo(() => avisos.Avisar(A<string>._, A<IReadOnlyList<ParadaSinCargarDTO>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task TerminarCarga_TodoCargado_SinMensajeNiAviso_YSiElAvisoFallaLaRespuestaSale()
        {
            Envio(251300, bultos: 1);
            await Cargar("RP251300-1", ANDRE);

            var resultado = await servicio.TerminarCarga(null, ANDRE);

            Assert.AreEqual(0, resultado.Contenido.Total);
            Assert.IsNull(resultado.Contenido.Mensaje);
            A.CallTo(() => avisos.Avisar(A<string>._, A<IReadOnlyList<ParadaSinCargarDTO>>._)).MustNotHaveHappened();

            Envio(251301);
            A.CallTo(() => avisos.Avisar(A<string>._, A<IReadOnlyList<ParadaSinCargarDTO>>._)).Throws(new InvalidOperationException("sin red"));
            var conFallo = await servicio.TerminarCarga(null, ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Ok, conFallo.Tipo);
            Assert.AreEqual(1, conFallo.Contenido.Total);
        }

        [TestMethod]
        public async Task AvisosRepartoSinCargar_ALosDeAlmacenConDominio()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var aviso = new AvisosRepartoSinCargar(() => push, g => g == Constantes.GruposSeguridad.ALMACEN ? new List<string> { "Pedro", @"NUEVAVISION\Santi" } : null);

            List<string> avisados = await aviso.Avisar(ANDRE, new List<ParadaSinCargarDTO> { new ParadaSinCargarDTO { EnvioId = 1 } });

            CollectionAssert.AreEqual(new[] { Constantes.GruposSeguridad.ALMACEN }, avisados);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Pedro", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Santi", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        // ================================================================ carga obligatoria al entregar

        [TestMethod]
        public async Task Entregar_Encendido_SinCargarYSinLeerNada_409NoCargada_YNoSeEntrega()
        {
            EnviosAgencia envio = Envio(251300);

            var resultado = await servicio.Entregar(251300, Entrega(), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, resultado.Tipo);
            Assert.AreEqual(ReglaCargaObligatoria.CODIGO_NO_CARGADA, resultado.Codigo);
            StringAssert.Contains(resultado.Mensaje, "no la ha cargado nadie");
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
            Assert.AreEqual(0, repositorio.Registros.Count);
        }

        [TestMethod]
        public async Task Entregar_Encendido_LaLlevaOtro_409ConSuNombre()
        {
            Envio(251300);
            await Cargar("RP251300-1", MIGUEL);

            var resultado = await servicio.Entregar(251300, Entrega(), ANDRE);

            Assert.AreEqual(ReglaCargaObligatoria.CODIGO_NO_CARGADA, resultado.Codigo);
            StringAssert.Contains(resultado.Mensaje, "la lleva Miguel");
        }

        [TestMethod]
        public async Task Entregar_Encendido_LeyendoUnBultoSuyo_SeCargaASuNombreConRastro_YSeEntrega()
        {
            EnviosAgencia envio = Envio(251300);
            EntregaRepartoDTO entrega = Entrega("rp251300-2");

            var resultado = await servicio.Entregar(251300, entrega, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
            CargaBultoReparto fila = cargas.Filas.Single();
            Assert.AreEqual(ANDRE, fila.Usuario);
            Assert.AreEqual(2, fila.Bulto);
            Assert.AreEqual("En reparto (Andre)", cargas.Historias.Single().Observaciones);

            // El reintento (misma clave) ni vuelve a cargar ni da 409
            var otraVez = await servicio.Entregar(251300, entrega, ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Ok, otraVez.Tipo, otraVez.Mensaje);
            Assert.AreEqual(1, cargas.Filas.Count);
        }

        [TestMethod]
        public async Task Entregar_Encendido_LaLlevaQuienEntrega_ComoHoy()
        {
            EnviosAgencia envio = Envio(251300);
            await Cargar("RP251300-1", ANDRE);

            var resultado = await servicio.Entregar(251300, Entrega(), @"NUEVAVISION\andre");

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
        }

        [TestMethod]
        public async Task Entregar_Apagado_ComoHoy_AunqueNoLaHayaCargadoNadie()
        {
            interruptor = false;
            EnviosAgencia envio = Envio(251300);

            var resultado = await servicio.Entregar(251300, Entrega(), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, envio.Estado);
            Assert.AreEqual(0, cargas.Filas.Count);
        }

        [TestMethod]
        public async Task Entregar_ParametroNoLegible_Apagado()
        {
            // Sin el Func de los tests se lee ParametrosUsuario; si falla (aquí ParametrosFalsos lanza), apagado.
            servicio.CargaObligatoriaEncendida = null;
            Envio(251300);

            var resultado = await servicio.Entregar(251300, Entrega(), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
        }

        // ================================================================ controlador

        private RepartoPropioController Controlador()
            => new RepartoPropioController(null, servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, ANDRE), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN)
                }, "prueba"))
            };

        [TestMethod]
        public async Task Controlador_EntregarNoCargada_409ConCodigoArribaYEnError()
        {
            Envio(251300);

            HttpResponseMessage respuesta = await (await Controlador().Entregar(251300, Entrega())).ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            string cuerpo = await respuesta.Content.ReadAsStringAsync();
            StringAssert.Contains(cuerpo, "\"Codigo\":\"NO_CARGADA\"");
            StringAssert.Contains(cuerpo, "\"code\":\"NO_CARGADA\"");
        }

        [TestMethod]
        public async Task Controlador_CargaTerminar_200_SinCuerpo_YEnviosConSoloCargados()
        {
            Envio(251300);

            HttpResponseMessage respuesta = await (await Controlador().TerminarCarga(null)).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "\"Total\":1");

            HttpResponseMessage lista = await (await Controlador().GetEnvios(null, true, true)).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.OK, lista.StatusCode);
            Assert.AreEqual("[]", await lista.Content.ReadAsStringAsync());
        }

        [TestMethod]
        public async Task Controlador_CargaTerminar_SinPermiso403()
        {
            RepartoPropioController controlador = Controlador();
            controlador.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, ANDRE) }, "prueba"));

            HttpResponseMessage respuesta = await (await controlador.TerminarCarga(new TerminarCargaRepartoDTO())).ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }
    }
}
