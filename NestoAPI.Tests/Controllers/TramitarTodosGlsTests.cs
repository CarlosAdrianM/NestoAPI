using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Results;
using System.Xml.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using NestoAPI.Tests.Infrastructure.Agencias;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#552 (fase 4): POST api/EnviosAgencias/TramitarTodos, el «Tramitar todos» de GLS al cerrar el día desde la API.
    /// GLS es un fake (nada sale hacia GLS) y el cierre (ConfirmarTramitacion) también.
    /// </summary>
    [TestClass]
    public class TramitarTodosGlsTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 27);

        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<AgenciaLlamadaWeb> fakeLlamadas;
        private IClienteGrabaServiciosGls fakeGls;
        private ITramitacionEnviosService fakeCierre;
        private EnviosAgenciasController controller;
        private readonly List<AgenciaLlamadaWeb> auditorias = new List<AgenciaLlamadaWeb>();
        private readonly List<EnviosAgencia> envios = new List<EnviosAgencia>();

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.Ignored))
                .Invokes((AgenciaLlamadaWeb l) => auditorias.Add(l))
                .ReturnsLazily((AgenciaLlamadaWeb l) => l);

            fakeGls = A.Fake<IClienteGrabaServiciosGls>();
            A.CallTo(() => fakeGls.Url).Returns(PeticionGls.URL_GRABA_SERVICIOS);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).Returns(Task.FromResult(TramitadorGlsTests.RESPUESTA_OK));

            fakeCierre = A.Fake<ITramitacionEnviosService>();
            A.CallTo(() => fakeCierre.TramitarAsync(A<int>._, A<string>._)).ReturnsLazily((int numero, string _) =>
            {
                EnviosAgencia envio = envios.Single(e => e.Numero == numero);
                envio.Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO;
                return Task.FromResult(new ResultadoTramitacionEnvio { Numero = numero, Asiento = 0, Mensaje = $"Envío del pedido {envio.Pedido} tramitado correctamente." });
            });

            controller = new EnviosAgenciasController(db, fakeCierre)
            {
                MotivoPedidoRetenido = (e, p) => Task.FromResult<string>(null), // NestoAPI#698: sin BD en los tests
                GlsTramitarPorApi = () => true,
                ClienteGls = fakeGls,
                HoyGls = () => HOY,
                CargarDatosGls = e => Task.FromResult((TramitadorGlsTests.EmpresaNuevaVision(),
                    new AgenciaTransporte { Numero = 1, Nombre = "ASM", Identificador = "9ABAEE33", PrefijoCodigoBarras = "6108352" })),
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/TramitarTodos") }
            };
        }

        private EnviosAgencia ConEnvio(int numero, int pedido, Action<EnviosAgencia> cambiar = null)
        {
            EnviosAgencia envio = TramitadorGlsTests.Envio();
            envio.Numero = numero;
            envio.Pedido = pedido;
            envio.CodigoBarras = PlazaYCodigoBarrasGls.CodigoBarras(envio, "6108352");
            cambiar?.Invoke(envio);
            envios.Add(envio);
            A.CallTo(() => fakeEnvios.FindAsync(numero)).Returns(Task.FromResult(envio));
            return envio;
        }

        private async Task<ResultadoTramitarEnviosDTO> TramitarTodos(params int[] numeros)
        {
            var respuesta = await controller.TramitarTodos(new TramitarEnviosDTO { Agencia = 1, Envios = numeros.ToList() })
                as OkNegotiatedContentResult<ResultadoTramitarEnviosDTO>;
            Assert.IsNotNull(respuesta, "Tramitar todos contesta 200 con el resultado de cada envío");
            return respuesta.Content;
        }

        [TestMethod]
        public async Task InterruptorApagado_400ComoElEnvioSueltoYNoLlamaAGls()
        {
            controller.GlsTramitarPorApi = () => false;
            ConEnvio(249723, 928379);

            var resultado = await controller.TramitarTodos(new TramitarEnviosDTO { Agencia = 1, Envios = new List<int> { 249723 } });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => fakeCierre.TramitarAsync(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task OtraAgencia_400SinLlamarAGls()
        {
            var resultado = await controller.TramitarTodos(new TramitarEnviosDTO { Agencia = Constantes.Agencias.AGENCIA_CTT });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AgenciaDeLaEmpresaEspejo_SeTramitaConSuPropiaCuenta()
        {
            // NestoAPI#552 (fase 5, Carlos 27/10/26): la agencia 5 pasa a la API igual que la 1, con su uid (AgenciasTransporte 5).
            controller.CargarDatosGls = e => Task.FromResult((
                new Empresa { Número = "3  ", Nombre = "Empresa espejo", Dirección = "x", Población = "x", Provincia = "x", CodPostal = "28000", Teléfono = "1", Email = "a@b.c" },
                new AgenciaTransporte { Numero = 5, Nombre = "ASM", Identificador = "0B00UID-ESPEJO", PrefijoCodigoBarras = "6128331" }));
            ConEnvio(249723, 928379, e => { e.Agencia = Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO; e.Empresa = "3  "; e.Servicio = 1; e.CodigoBarras = "61283310249723"; });
            string mandado = null;
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).Invokes((string soap) => mandado = soap).Returns(Task.FromResult(TramitadorGlsTests.RESPUESTA_OK));

            var respuesta = await controller.TramitarTodos(new TramitarEnviosDTO { Agencia = Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO, Envios = new List<int> { 249723 } })
                as OkNegotiatedContentResult<ResultadoTramitarEnviosDTO>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(1, respuesta.Content.Tramitados);
            StringAssert.Contains(mandado, "uidcliente=\"0B00UID-ESPEJO\"");
            StringAssert.Contains(mandado, "codbarras=\"61283310249723\"");
            StringAssert.Contains(mandado, "<Nombre>Nueva Visión</Nombre>", "La empresa espejo sale con Nueva Visión de remitente, como en AgenciaASM");
            A.CallTo(() => fakeCierre.TramitarAsync(249723, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnvioDeLaAgencia5EnElLoteDeLa1_ErrorSinLlamarAGls()
        {
            ConEnvio(249723, 928379, e => e.Agencia = Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO);

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723);

            Assert.AreEqual(1, resultado.Fallidos);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        // ---------- -33 con datos cambiados (fase 5, decisión de Carlos del 27/10/26) ----------

        /// <summary>La petición que GLS aceptó en su día: la de ahora con lo que se le cambie.</summary>
        private static XElement GrabadoConDatos(EnviosAgencia envio, Action<EnviosAgencia> comoEra = null)
        {
            EnviosAgencia antes = TramitadorGlsTests.Envio();
            antes.Numero = envio.Numero;
            antes.Pedido = envio.Pedido;
            antes.CodigoBarras = envio.CodigoBarras;
            comoEra?.Invoke(antes);
            return PeticionGls.ConstruirServicios(antes, TramitadorGlsTests.EmpresaNuevaVision(), "9ABAEE33", new DateTime(2026, 10, 20));
        }

        [TestMethod]
        public async Task GlsYaLoTenia_ConLosMismosDatos_SeCierraSinAviso()
        {
            EnviosAgencia envio = ConEnvio(249723, 928379);
            controller.GrabacionAnteriorGls = e => Task.FromResult(GrabadoConDatos(envio));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).Returns(Task.FromResult(TramitadorGlsTests.Respuesta("-33", "Ya existe el codigo de barras de la expedicion")));

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723);

            Assert.AreEqual(ResultadoTramitarEnvioDTO.TRAMITADO, resultado.Envios.Single().Resultado);
            Assert.IsTrue(resultado.Envios.Single().YaEstabaEnGls);
            Assert.IsNull(resultado.Envios.Single().Aviso, "La fecha distinta (la de tramitación) no cuenta");
            Assert.AreEqual(0, resultado.ConAviso);
        }

        [TestMethod]
        public async Task GlsYaLoTenia_ConOtrosDatos_SeCierraYAvisaDeLoQueHaCambiado()
        {
            EnviosAgencia envio = ConEnvio(249723, 928379, e => { e.Bultos = 3; e.Reembolso = 25.5M; });
            controller.GrabacionAnteriorGls = e => Task.FromResult(GrabadoConDatos(envio, antes => { antes.Bultos = 2; antes.Reembolso = 0M; }));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).Returns(Task.FromResult(TramitadorGlsTests.Respuesta("-33")));

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723);

            ResultadoTramitarEnvioDTO delEnvio = resultado.Envios.Single();
            Assert.AreEqual(ResultadoTramitarEnvioDTO.TRAMITADO, delEnvio.Resultado, "Se da por bueno igual (Carlos, 27/10/26)");
            A.CallTo(() => fakeCierre.TramitarAsync(249723, A<string>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, resultado.ConAviso);
            StringAssert.Contains(delEnvio.Aviso, "GLS ya tenía grabado el envío del pedido 928379, pero con otros datos");
            StringAssert.Contains(delEnvio.Aviso, "Bultos: «2» → «3»");
            StringAssert.Contains(delEnvio.Aviso, "Reembolso: «0» → «25.5»");
            StringAssert.Contains(delEnvio.Aviso, "revíselo con GLS");
        }

        [TestMethod]
        public async Task GlsYaLoTenia_SinGrabacionAnterior_AvisaDeQueNoSeHaPodidoComprobar()
        {
            ConEnvio(249723, 928379);
            controller.GrabacionAnteriorGls = e => Task.FromResult<XElement>(null);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).Returns(Task.FromResult(TramitadorGlsTests.Respuesta("-33")));

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723);

            Assert.AreEqual(ResultadoTramitarEnvioDTO.TRAMITADO, resultado.Envios.Single().Resultado);
            StringAssert.Contains(resultado.Envios.Single().Aviso, "no se encuentra la primera grabación");
        }

        [TestMethod]
        public async Task GlsYaLoTenia_SiFallaLaBusqueda_SeCierraIgualYAvisa()
        {
            ConEnvio(249723, 928379);
            controller.GrabacionAnteriorGls = e => throw new InvalidOperationException("la BD no contesta");
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).Returns(Task.FromResult(TramitadorGlsTests.Respuesta("-33")));

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723);

            Assert.AreEqual(ResultadoTramitarEnvioDTO.TRAMITADO, resultado.Envios.Single().Resultado);
            Assert.IsNotNull(resultado.Envios.Single().Aviso);
        }

        [TestMethod]
        public async Task GrabadoAhora_NoBuscaLaGrabacionAnteriorNiAvisa()
        {
            ConEnvio(249723, 928379);
            bool buscada = false;
            controller.GrabacionAnteriorGls = e => { buscada = true; return Task.FromResult<XElement>(null); };

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723);

            Assert.IsFalse(buscada);
            Assert.IsNull(resultado.Envios.Single().Aviso);
        }

        [TestMethod]
        public async Task Todos_Bien_GrabaEnGlsYCierraCadaUnoYAuditaComoNesto()
        {
            ConEnvio(249723, 928379);
            ConEnvio(249724, 928380);

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723, 249724);

            Assert.AreEqual(2, resultado.Tramitados);
            Assert.AreEqual(0, resultado.Fallidos);
            Assert.IsTrue(resultado.Envios.All(e => e.Resultado == ResultadoTramitarEnvioDTO.TRAMITADO));
            Assert.AreEqual("Envío del pedido 928380 tramitado correctamente.", resultado.Envios[1].Mensaje);
            Assert.AreEqual("1365123979", resultado.Envios[0].Expedicion);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustHaveHappenedTwiceExactly();
            A.CallTo(() => fakeCierre.TramitarAsync(249723, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => fakeCierre.TramitarAsync(249724, A<string>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(2, auditorias.Count);
            Assert.IsTrue(auditorias.All(a => a.Agencia == SombraPeticionesGls.AGENCIA_LLAMADAS && a.Exito && a.TextoRespuestaError == "OK"));
        }

        [TestMethod]
        public async Task GlsRechazaUno_LosDemasSiguenYElRechazadoQuedaSinCerrar()
        {
            ConEnvio(249723, 928379);
            ConEnvio(249724, 928380);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>.That.Contains("codbarras=\"61197140249723\"")))
                .Returns(Task.FromResult(TramitadorGlsTests.Respuesta("-70")));

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723, 249724);

            Assert.AreEqual(1, resultado.Tramitados);
            Assert.AreEqual(1, resultado.Fallidos);
            ResultadoTramitarEnvioDTO fallido = resultado.Envios.Single(e => e.Numero == 249723);
            Assert.AreEqual(ResultadoTramitarEnvioDTO.ERROR, fallido.Resultado);
            Assert.AreEqual(928379, fallido.Pedido);
            Assert.AreEqual("GLS no ha aceptado el envío 249723: Ya se ha enviado este pedido para esta fecha y cliente", fallido.Mensaje);
            A.CallTo(() => fakeCierre.TramitarAsync(249723, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => fakeCierre.TramitarAsync(249724, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GlsNoResponde_ElEnvioFallaConElMensajeDeUstedYSigueElResto()
        {
            ConEnvio(249723, 928379);
            ConEnvio(249724, 928380);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>.That.Contains("codbarras=\"61197140249723\"")))
                .Throws(new AgenciaRemotaException("GLS (GrabaServicios) respondió HTTP 503: caído") { EsTransitoria = true });

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723, 249724);

            Assert.AreEqual(1, resultado.Tramitados);
            Assert.AreEqual("GLS no responde ahora mismo (error de su servidor). Vuelva a intentarlo en unos minutos o, si es urgente, sáquelo por otra agencia.",
                resultado.Envios[0].Mensaje);
        }

        [TestMethod]
        public async Task YaTramitado_SeSaltaSinLlamarAGls()
        {
            ConEnvio(249723, 928379, e => e.Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO);
            ConEnvio(249724, 928380);

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723, 249724);

            Assert.AreEqual(1, resultado.YaTramitados);
            Assert.AreEqual(1, resultado.Tramitados);
            Assert.AreEqual(ResultadoTramitarEnvioDTO.YA_TRAMITADO, resultado.Envios[0].Resultado);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>.That.Contains("codbarras=\"61197140249723\""))).MustNotHaveHappened();
            A.CallTo(() => fakeCierre.TramitarAsync(249723, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RepetirTodo_LosQueYaSeCerraronSeSaltan()
        {
            ConEnvio(249723, 928379);

            _ = await TramitarTodos(249723);
            ResultadoTramitarEnviosDTO segundo = await TramitarTodos(249723);

            Assert.AreEqual(1, segundo.YaTramitados);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GlsYaLoTenia_SeDaPorBuenoYSeCierra()
        {
            ConEnvio(249723, 928379);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).Returns(Task.FromResult(TramitadorGlsTests.Respuesta("-33", "Ya existe el codigo de barras de la expedicion")));

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723);

            Assert.AreEqual(ResultadoTramitarEnvioDTO.TRAMITADO, resultado.Envios.Single().Resultado);
            Assert.IsTrue(resultado.Envios.Single().YaEstabaEnGls);
            A.CallTo(() => fakeCierre.TramitarAsync(249723, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GrabadoEnGlsPeroNoSeCierra_AvisaQueSePuedeRepetirSinDuplicarYSigue()
        {
            ConEnvio(249723, 928379);
            ConEnvio(249724, 928380);
            A.CallTo(() => fakeCierre.TramitarAsync(249723, A<string>._)).Throws(new InvalidOperationException("prdContabilizar ha fallado."));

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723, 249724);

            Assert.AreEqual(1, resultado.Fallidos);
            Assert.AreEqual(1, resultado.Tramitados);
            StringAssert.Contains(resultado.Envios[0].Mensaje, "está grabado en GLS, pero no se ha podido dar por tramitado: prdContabilizar ha fallado.");
            StringAssert.Contains(resultado.Envios[0].Mensaje, "no se duplica");
        }

        [TestMethod]
        public async Task EtiquetaPendienteOEnvioDeOtraAgencia_ErrorSinLlamarAGls()
        {
            ConEnvio(249723, 928379, e => e.Estado = Constantes.Agencias.ESTADO_PENDIENTE);
            ConEnvio(249724, 928380, e => e.Agencia = Constantes.Agencias.AGENCIA_CTT);

            ResultadoTramitarEnviosDTO resultado = await TramitarTodos(249723, 249724, 1);

            Assert.AreEqual(3, resultado.Fallidos);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinLista_TramitaLosEnCursoDeGls()
        {
            ConEnvio(249723, 928379);
            ConEnvio(249724, 928380, e => e.Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO);
            ConEnvio(249725, 928381, e => e.Agencia = Constantes.Agencias.AGENCIA_CTT);
            ConfigurarFakeDbSet(fakeEnvios, envios.ToList().AsQueryable());

            var respuesta = await controller.TramitarTodos(null) as OkNegotiatedContentResult<ResultadoTramitarEnviosDTO>;

            Assert.AreEqual(249723, respuesta.Content.Envios.Single().Numero);
            Assert.AreEqual(1, respuesta.Content.Tramitados);
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider).Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }

    /// <summary>
    /// NestoAPI#552 (fase 4): la etiqueta de GLS desde la API (GET {id}/EtiquetaZpl para Nesto y POST {id}/ImprimirEtiqueta para
    /// el packing de Ariadna), sin grabar el envío en GLS. GLS es un fake.
    /// </summary>
    [TestClass]
    public class EtiquetaGlsPorApiTests
    {
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private IClienteGrabaServiciosGls fakeGrabaServicios;
        private IClienteTspGls fakeTsp;
        private IPlazaYCodigoBarrasAgencia fakePlaza;
        private IImpresoraEtiquetas fakeImpresora;
        private IFabricaAgenciasRemotas fakeFabrica;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            fakeGrabaServicios = A.Fake<IClienteGrabaServiciosGls>();
            fakeTsp = A.Fake<IClienteTspGls>();
            A.CallTo(() => fakeTsp.EnviarAsync(A<string>._)).Returns(Task.FromResult(EtiquetaGlsTests.RESPUESTA_TSP));
            fakePlaza = A.Fake<IPlazaYCodigoBarrasAgencia>();
            fakeImpresora = A.Fake<IImpresoraEtiquetas>();
            fakeFabrica = A.Fake<IFabricaAgenciasRemotas>();

            controller = new EnviosAgenciasController(db, fakeFabrica)
            {
                MotivoPedidoRetenido = (e, p) => Task.FromResult<string>(null), // NestoAPI#698: sin BD en los tests
                GlsTramitarPorApi = () => true,
                ClienteGls = fakeGrabaServicios,
                ClienteTspGls = fakeTsp,
                PlazaGls = fakePlaza,
                ImpresoraEtiquetas = fakeImpresora,
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/249723/EtiquetaZpl") }
            };
        }

        private EnviosAgencia ConEnvio(Action<EnviosAgencia> cambiar = null)
        {
            EnviosAgencia envio = EtiquetaGlsTests.EnvioConPlaza();
            cambiar?.Invoke(envio);
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
            return envio;
        }

        [TestMethod]
        public async Task EtiquetaZpl_InterruptorApagado_400()
        {
            controller.GlsTramitarPorApi = () => false;
            ConEnvio();

            Assert.IsInstanceOfType(await controller.GetEtiquetaZpl(249723), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeTsp.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EtiquetaZpl_EmpresaEspejo_TambienLaMontaLaApi()
        {
            // NestoAPI#552 (fase 5, Carlos 27/10/26): la agencia 5 pasa a la API igual que la 1.
            ConEnvio(e => { e.Agencia = Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO; e.CodigoBarras = "61283310249723"; });

            var resultado = await controller.GetEtiquetaZpl(249723) as OkNegotiatedContentResult<EtiquetaEnvioDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("61283310249723", resultado.Content.CodigoBarras);
            A.CallTo(() => fakeGrabaServicios.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EtiquetaZpl_OtraAgencia_400()
        {
            ConEnvio(e => e.Agencia = Constantes.Agencias.AGENCIA_CANTERAS);

            Assert.IsInstanceOfType(await controller.GetEtiquetaZpl(249723), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeTsp.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EtiquetaZpl_GlsContestaSinTsp_502YNoHayEtiqueta()
        {
            // Decisión de Carlos (27/10/26): sin código de ruta GLS no entrega; nunca se imprime sin él.
            ConEnvio();
            A.CallTo(() => fakeTsp.EnviarAsync(A<string>._)).Returns(Task.FromResult(
                @"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body/></soap:Envelope>"));

            var resultado = await controller.GetEtiquetaZpl(249723) as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.Response.StatusCode);
        }

        [TestMethod]
        public async Task ImprimirEtiqueta_EmpresaEspejo_LaImprimeEnElServidor()
        {
            ConEnvio(e => { e.Agencia = Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO; e.CodigoBarras = "61283310249723"; });

            var resultado = await controller.ImprimirEtiqueta(249723, new ImprimirEtiquetaEnvioDTO { Impresora = "ZEBRA PACKING" })
                as OkNegotiatedContentResult<ImprimirEtiquetaEnvioResultadoDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => fakeImpresora.Imprimir("ZEBRA PACKING", A<string>.That.Contains("^FD61283310249723001^FS"), A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => fakeFabrica.Crear(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EtiquetaZpl_DevuelveLaZplSinGrabarEnGls()
        {
            ConEnvio();

            var resultado = await controller.GetEtiquetaZpl(249723) as OkNegotiatedContentResult<EtiquetaEnvioDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("ZPL", resultado.Content.EtiquetaTipo);
            Assert.AreEqual("base64", resultado.Content.EtiquetaCodificacion);
            Assert.AreEqual("61197140249723", resultado.Content.CodigoBarras);
            string zpl = Encoding.GetEncoding(1252).GetString(Convert.FromBase64String(resultado.Content.EtiquetaContenido));
            StringAssert.Contains(zpl, "^FD61197140249723002^FS");
            StringAssert.Contains(zpl, "^FDMAD-28^FS");
            A.CallTo(() => fakeGrabaServicios.EnviarAsync(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => fakePlaza.CalcularPlazaAsync(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EtiquetaZpl_GlsNoRespondeAlTsp_502AgenciaNoResponde()
        {
            ConEnvio();
            A.CallTo(() => fakeTsp.EnviarAsync(A<string>._)).Throws(new AgenciaRemotaException("GLS (GetTSP): no se pudo conectar") { EsTransitoria = true });

            var resultado = await controller.GetEtiquetaZpl(249723) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.Response.StatusCode);
        }

        [TestMethod]
        public async Task ImprimirEtiqueta_Gls_LaImprimeEnElServidorSinGrabarEnGls()
        {
            ConEnvio();

            var resultado = await controller.ImprimirEtiqueta(249723, new ImprimirEtiquetaEnvioDTO { Impresora = "ZEBRA PACKING" })
                as OkNegotiatedContentResult<ImprimirEtiquetaEnvioResultadoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("ZEBRA PACKING", resultado.Content.Impresora);
            Assert.AreEqual("61197140249723", resultado.Content.CodigoBarras);
            A.CallTo(() => fakeImpresora.Imprimir("ZEBRA PACKING", A<string>.That.Contains("^FD61197140249723001^FS"), A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => fakeGrabaServicios.EnviarAsync(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => fakeFabrica.Crear(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ImprimirEtiqueta_GlsConInterruptorApagado_El400DeSiempre()
        {
            controller.GlsTramitarPorApi = () => false;
            ConEnvio();
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_GLS)).Returns(null);

            var resultado = await controller.ImprimirEtiqueta(249723, new ImprimirEtiquetaEnvioDTO { Impresora = "ZEBRA PACKING" });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeImpresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }
    }
}
