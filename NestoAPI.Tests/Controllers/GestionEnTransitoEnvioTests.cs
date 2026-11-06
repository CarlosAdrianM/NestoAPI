using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.CTT;
using NestoAPI.Models;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#726: gestión en tránsito de un envío que ya tiene la agencia (GET Gestiones, POST PararEnVuelo, Retorno,
    /// FechaEntrega y DireccionEntrega). API primero, BD después; cada operación deja historia y avisa a logística (#722).
    /// </summary>
    [TestClass]
    public class GestionEnTransitoEnvioTests
    {
        private const string ALBARAN = "0082800082809800827793";

        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<EnvioHistoria> fakeHistorias;
        private DbSet<AgenciaLlamadaWeb> fakeLlamadas;
        private IFabricaAgenciasRemotas fakeFabrica;
        private IAgenciaRemota fakeAgencia;
        private IGestionEnTransitoAgenciaRemota gestion;
        private IServicioCorreoElectronico fakeCorreo;
        private EnviosAgenciasController controller;
        private readonly List<EnvioHistoria> historias = new List<EnvioHistoria>();
        private readonly List<EnviosAgencia> anadidos = new List<EnviosAgencia>();
        private readonly List<MailMessage> correos = new List<MailMessage>();

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeHistorias = A.Fake<DbSet<EnvioHistoria>>(o => o.Implements<IQueryable<EnvioHistoria>>().Implements<IDbAsyncEnumerable<EnvioHistoria>>());
            fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistorias);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => fakeHistorias.Add(A<EnvioHistoria>._)).Invokes((EnvioHistoria h) => historias.Add(h)).ReturnsLazily((EnvioHistoria h) => h);
            A.CallTo(() => fakeEnvios.Add(A<EnviosAgencia>._)).Invokes((EnviosAgencia e) => anadidos.Add(e)).ReturnsLazily((EnviosAgencia e) => e);

            fakeAgencia = A.Fake<IAgenciaRemota>(o => o.Implements<IGestionEnTransitoAgenciaRemota>());
            gestion = (IGestionEnTransitoAgenciaRemota)fakeAgencia;
            A.CallTo(() => fakeAgencia.Intercambios).Returns(new List<IntercambioRemoto>());
            fakeFabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);

            fakeCorreo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => fakeCorreo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => correos.Add(m)).Returns(true);

            controller = new EnviosAgenciasController(db, fakeFabrica)
            {
                ServicioCorreoGestiones = fakeCorreo,
                HoyGestiones = () => new DateTime(2026, 11, 5),
                EsDiaSinReparto = dia => dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday || dia == new DateTime(2026, 12, 8),
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/1/Gestiones") },
                User = new System.Security.Principal.GenericPrincipal(new System.Security.Principal.GenericIdentity("NUEVAVISION\\Laura"), new string[0])
            };
        }

        private EnviosAgencia ConEnvio(short estado, string codigoBarras = ALBARAN, int agencia = Constantes.Agencias.AGENCIA_INNOVATRANS)
        {
            var envio = new EnviosAgencia
            {
                Numero = 1,
                Empresa = "1",
                Agencia = agencia,
                Cliente = "15191",
                Contacto = "0",
                Pedido = 929879,
                Estado = estado,
                CodigoBarras = codigoBarras,
                Nombre = "CLIENTE",
                Direccion = "Calle Mayor 1",
                CodPostal = "28001",
                Poblacion = "MADRID",
                Telefono = "600000000",
                Email = "cliente@example.com",
                Bultos = 2,
                Peso = 3m,
                Reembolso = 25m
            };
            A.CallTo(() => fakeEnvios.FindAsync(1)).Returns(Task.FromResult(envio));
            return envio;
        }

        private void SinEnvio() => A.CallTo(() => fakeEnvios.FindAsync(A<object[]>._)).Returns(Task.FromResult<EnviosAgencia>(null));

        // ---------- Reglas (GestionEnTransitoEnvio.Calcular) ----------

        [TestMethod]
        public void Calcular_AgenciaSinGestion_NadaYMotivo()
        {
            var envio = new EnviosAgencia { Estado = Constantes.Agencias.ESTADO_TRAMITADO, CodigoBarras = ALBARAN };
            GestionesEnvioDTO g = GestionEnTransitoEnvio.Calcular(envio, null, "GLS");
            Assert.IsFalse(g.PararEnVuelo || g.Retorno || g.CambiarFecha || g.CambiarDireccion);
            Assert.AreEqual("La agencia GLS no tiene gestión en tránsito.", g.Motivo);
        }

        [TestMethod]
        public void Calcular_SinAlbaran_Nada()
        {
            GestionesEnvioDTO g = GestionEnTransitoEnvio.Calcular(new EnviosAgencia { Estado = Constantes.Agencias.ESTADO_TRAMITADO, CodigoBarras = " " }, gestion, "CTT");
            Assert.IsFalse(g.PararEnVuelo || g.Retorno || g.CambiarFecha || g.CambiarDireccion);
            StringAssert.Contains(g.Motivo, "no tiene albarán");
        }

        [DataTestMethod]
        [DataRow((short)0)]
        [DataRow((short)1)]
        [DataRow((short)3)]
        public void Calcular_EnCursoTramitadoOIncidentado_PararFechaYDireccionSinRetorno(short estado)
        {
            GestionesEnvioDTO g = GestionEnTransitoEnvio.Calcular(new EnviosAgencia { Estado = estado, CodigoBarras = ALBARAN }, gestion, "CTT");
            Assert.IsTrue(g.PararEnVuelo && g.CambiarFecha && g.CambiarDireccion);
            Assert.IsFalse(g.Retorno);
        }

        [TestMethod]
        public void Calcular_Entregado_SoloRetorno()
        {
            GestionesEnvioDTO g = GestionEnTransitoEnvio.Calcular(new EnviosAgencia { Estado = Constantes.Agencias.ESTADO_ENTREGADO, CodigoBarras = ALBARAN }, gestion, "CTT");
            Assert.IsTrue(g.Retorno);
            Assert.IsFalse(g.PararEnVuelo || g.CambiarFecha || g.CambiarDireccion);
            StringAssert.Contains(g.Motivo, "ya está entregado");
        }

        [DataTestMethod]
        [DataRow((short)-1)]
        [DataRow((short)4)]
        [DataRow((short)5)]
        public void Calcular_PendienteDevueltoOCerrado_NadaConMotivo(short estado)
        {
            GestionesEnvioDTO g = GestionEnTransitoEnvio.Calcular(new EnviosAgencia { Estado = estado, CodigoBarras = ALBARAN }, gestion, "CTT");
            Assert.IsFalse(g.PararEnVuelo || g.Retorno || g.CambiarFecha || g.CambiarDireccion);
            Assert.IsFalse(string.IsNullOrWhiteSpace(g.Motivo));
        }

        [TestMethod]
        public void Calcular_RetornoEntregado_NoAdmiteOtroRetorno()
        {
            var envio = new EnviosAgencia { Estado = Constantes.Agencias.ESTADO_ENTREGADO, CodigoBarras = ALBARAN, Retorno = AgenciaRemotaCTT.RETORNO_RECOGIDA_EN_ORIGEN };
            GestionesEnvioDTO g = GestionEnTransitoEnvio.Calcular(envio, gestion, "CTT");
            Assert.IsFalse(g.Retorno);
            StringAssert.Contains(g.Motivo, "retorno de un retorno");
        }

        // ---------- GET Gestiones ----------

        [TestMethod]
        public async Task Gestiones_EnvioInexistente_404()
        {
            SinEnvio();
            Assert.IsInstanceOfType(await controller.GetGestionesEnvio(99), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Gestiones_AgenciaSinInterfaz_TodoFalse()
        {
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(A.Fake<IAgenciaRemota>());
            ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);

            var r = (OkNegotiatedContentResult<GestionesEnvioDTO>)await controller.GetGestionesEnvio(1);

            Assert.IsFalse(r.Content.PararEnVuelo || r.Content.Retorno || r.Content.CambiarFecha || r.Content.CambiarDireccion);
            StringAssert.Contains(r.Content.Motivo, "no tiene gestión en tránsito");
        }

        [TestMethod]
        public async Task Gestiones_Tramitado_DevuelveLasDeTransito()
        {
            ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            var r = (OkNegotiatedContentResult<GestionesEnvioDTO>)await controller.GetGestionesEnvio(1);
            Assert.IsTrue(r.Content.PararEnVuelo && r.Content.CambiarFecha && r.Content.CambiarDireccion);
            Assert.IsFalse(r.Content.Retorno);
        }

        // ---------- POST PararEnVuelo ----------

        [TestMethod]
        public async Task PararEnVuelo_EnvioInexistente_404()
        {
            SinEnvio();
            Assert.IsInstanceOfType(await controller.PararEnVuelo(99), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task PararEnVuelo_AgenciaSinGestion_400SinLlamar()
        {
            ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO, agencia: 999);

            var r = await controller.PararEnVuelo(1) as BadRequestErrorMessageResult;

            Assert.IsNotNull(r);
            StringAssert.Contains(r.Message, "no tiene gestión en tránsito");
            A.CallTo(() => gestion.PararEnVueloAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PararEnVuelo_Entregado_400()
        {
            ConEnvio(Constantes.Agencias.ESTADO_ENTREGADO);
            var r = await controller.PararEnVuelo(1) as BadRequestErrorMessageResult;
            StringAssert.Contains(r.Message, "ya está entregado");
            A.CallTo(() => gestion.PararEnVueloAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PararEnVuelo_Ok_HistoriaYCorreoALogistica()
        {
            ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            A.CallTo(() => gestion.PararEnVueloAsync(ALBARAN)).Returns(new ResultadoOperacionRemota { Exito = true });

            var r = await controller.PararEnVuelo(1) as OkNegotiatedContentResult<ResultadoGestionEnvioDTO>;

            Assert.IsNotNull(r);
            StringAssert.Contains(r.Content.Mensaje, "vuelve a origen");
            EnvioHistoria h = historias.Single();
            Assert.AreEqual(GestionEnTransitoEnvio.OPERACION_PARAR_EN_VUELO, h.Campo);
            Assert.AreEqual(1, h.NumeroEnvio);
            Assert.AreEqual("NUEVAVISION\\Laura", h.Usuario);
            StringAssert.Contains(h.Observaciones, "hecho");
            MailMessage correo = correos.Single();
            Assert.AreEqual(Constantes.Correos.LOGISTICA, correo.To.Single().Address);
            StringAssert.Contains(correo.Subject, "929879");
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.That.Matches(l => l.Exito))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PararEnVuelo_Ok_DejaParadoEnVueloEnElDetalleSinCambiarElEstado()
        {
            EnviosAgencia envio = ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            envio.DetalleEstado = "EN REPARTO";
            A.CallTo(() => gestion.PararEnVueloAsync(ALBARAN)).Returns(new ResultadoOperacionRemota { Exito = true });

            Assert.IsInstanceOfType(await controller.PararEnVuelo(1), typeof(OkNegotiatedContentResult<ResultadoGestionEnvioDTO>));

            Assert.AreEqual("Parado en vuelo", envio.DetalleEstado);
            Assert.IsTrue(GestionEnTransitoEnvio.DETALLE_PARADO_EN_VUELO.Length <= 100, "DetalleEstado es varchar(100)");
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado, "El Estado lo cambia el seguimiento");
            StringAssert.Contains(historias.Single().ValorAnterior, "EN REPARTO");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task PararEnVuelo_LaAgenciaRechaza_NoTocaElDetalle()
        {
            EnviosAgencia envio = ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            envio.DetalleEstado = "EN REPARTO";
            A.CallTo(() => gestion.PararEnVueloAsync(ALBARAN)).ThrowsAsync(new AgenciaRemotaException("CTT respondió 403: Can not perform action"));

            Assert.IsInstanceOfType(await controller.PararEnVuelo(1), typeof(BadRequestErrorMessageResult));

            Assert.AreEqual("EN REPARTO", envio.DetalleEstado);
        }

        [TestMethod]
        public async Task PararEnVuelo_FallaElCorreo_NoTumbaLaOperacion()
        {
            ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            A.CallTo(() => gestion.PararEnVueloAsync(ALBARAN)).Returns(new ResultadoOperacionRemota { Exito = true });
            A.CallTo(() => fakeCorreo.EnviarCorreoSMTP(A<MailMessage>._)).Throws(new SmtpException("caído"));

            Assert.IsInstanceOfType(await controller.PararEnVuelo(1), typeof(OkNegotiatedContentResult<ResultadoGestionEnvioDTO>));
        }

        [TestMethod]
        public async Task PararEnVuelo_LaAgenciaRechaza_400ConSuMotivoEHistoria()
        {
            ConEnvio(Constantes.Agencias.ESTADO_INCIDENTADO);
            A.CallTo(() => gestion.PararEnVueloAsync(ALBARAN)).ThrowsAsync(new AgenciaRemotaException("No se pudo parar en vuelo del envío X en CTT. CTT respondió 403: Can not perform action"));

            var r = await controller.PararEnVuelo(1) as BadRequestErrorMessageResult;

            Assert.IsNotNull(r);
            StringAssert.Contains(r.Message, "Can not perform action");
            StringAssert.Contains(historias.Single().Observaciones, "rechazado");
            Assert.AreEqual(0, correos.Count, "Sin hacerse, no se avisa a logística");
        }

        // ---------- POST Retorno ----------

        [TestMethod]
        public async Task Retorno_EnvioInexistente_404()
        {
            SinEnvio();
            Assert.IsInstanceOfType(await controller.CrearRetornoEnvio(99), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Retorno_NoEntregado_400()
        {
            ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            var r = await controller.CrearRetornoEnvio(1) as BadRequestErrorMessageResult;
            StringAssert.Contains(r.Message, "entregado");
            A.CallTo(() => gestion.CrearRetornoAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Retorno_Ok_GrabaElEnvioDeRetornoYApuntaElCodigo()
        {
            ConEnvio(Constantes.Agencias.ESTADO_ENTREGADO);
            A.CallTo(() => gestion.CrearRetornoAsync(ALBARAN)).Returns("0082800031029800000086");

            var r = await controller.CrearRetornoEnvio(1) as OkNegotiatedContentResult<ResultadoRetornoEnvioDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual("0082800031029800000086", r.Content.CodigoRetorno);
            Assert.IsNotNull(r.Content.EnvioRetornoId);
            EnviosAgencia retorno = anadidos.Single();
            Assert.AreEqual("0082800031029800000086", retorno.CodigoBarras);
            Assert.AreEqual(AgenciaRemotaCTT.RETORNO_RECOGIDA_EN_ORIGEN, retorno.Retorno);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, retorno.Estado);
            Assert.AreEqual(0m, retorno.Reembolso, "El retorno no cobra");
            Assert.AreEqual(929879, retorno.Pedido);
            Assert.AreEqual(new DateTime(2026, 11, 5), retorno.Fecha);
            StringAssert.Contains(historias.Single().Observaciones, "0082800031029800000086");
            Assert.AreEqual(1, correos.Count);
        }

        // ---------- POST FechaEntrega ----------

        [TestMethod]
        public async Task FechaEntrega_EnvioInexistente_404()
        {
            SinEnvio();
            Assert.IsInstanceOfType(await controller.CambiarFechaEntrega(99, new CambiarFechaEntregaEnvioDTO { Fecha = new DateTime(2026, 11, 6) }), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task FechaEntrega_Pasada_400SinLlamar()
        {
            ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            var r = await controller.CambiarFechaEntrega(1, new CambiarFechaEntregaEnvioDTO { Fecha = new DateTime(2026, 11, 4) }) as BadRequestErrorMessageResult;
            StringAssert.Contains(r.Message, "ya ha pasado");
            A.CallTo(() => gestion.CambiarFechaEntregaAsync(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow(2026, 11, 8)]  // domingo
        [DataRow(2026, 12, 8)]  // festivo
        public async Task FechaEntrega_FestivoODomingo_400(int anio, int mes, int dia)
        {
            ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            var r = await controller.CambiarFechaEntrega(1, new CambiarFechaEntregaEnvioDTO { Fecha = new DateTime(anio, mes, dia) }) as BadRequestErrorMessageResult;
            StringAssert.Contains(r.Message, "no se reparte");
            A.CallTo(() => gestion.CambiarFechaEntregaAsync(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FechaEntrega_Ok_LlamaConLaFechaYDejaHistoria()
        {
            ConEnvio(Constantes.Agencias.ESTADO_INCIDENTADO);
            A.CallTo(() => gestion.CambiarFechaEntregaAsync(ALBARAN, new DateTime(2026, 11, 6))).Returns(new ResultadoOperacionRemota { Exito = true });

            var r = await controller.CambiarFechaEntrega(1, new CambiarFechaEntregaEnvioDTO { Fecha = new DateTime(2026, 11, 6) }) as OkNegotiatedContentResult<ResultadoGestionEnvioDTO>;

            Assert.IsNotNull(r);
            StringAssert.Contains(r.Content.Mensaje, "06/11/2026");
            StringAssert.Contains(historias.Single().Observaciones, "06/11/2026");
            Assert.AreEqual(1, correos.Count);
        }

        [TestMethod]
        public async Task FechaEntrega_Ok_GrabaLaFechaNuevaEnElEnvio()
        {
            EnviosAgencia envio = ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            envio.FechaEntrega = new DateTime(2026, 11, 5);
            A.CallTo(() => gestion.CambiarFechaEntregaAsync(ALBARAN, new DateTime(2026, 11, 9))).Returns(new ResultadoOperacionRemota { Exito = true });

            var r = await controller.CambiarFechaEntrega(1, new CambiarFechaEntregaEnvioDTO { Fecha = new DateTime(2026, 11, 9, 10, 30, 0) }) as OkNegotiatedContentResult<ResultadoGestionEnvioDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual(new DateTime(2026, 11, 9), envio.FechaEntrega, "Sin la hora");
            Assert.AreEqual("NUEVAVISION\\Laura", envio.Usuario);
            Assert.AreNotEqual(default(DateTime), envio.FechaModificacion);
            Assert.AreEqual("05/11/2026", historias.Single().ValorAnterior);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task FechaEntrega_LaAgenciaRechaza_NoTocaLaFechaDelEnvio()
        {
            EnviosAgencia envio = ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            envio.FechaEntrega = new DateTime(2026, 11, 5);
            A.CallTo(() => gestion.CambiarFechaEntregaAsync(ALBARAN, A<DateTime>._)).ThrowsAsync(new AgenciaRemotaException("CTT respondió 403: Can not change delivery date"));

            var r = await controller.CambiarFechaEntrega(1, new CambiarFechaEntregaEnvioDTO { Fecha = new DateTime(2026, 11, 9) }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(r);
            Assert.AreEqual(new DateTime(2026, 11, 5), envio.FechaEntrega);
            StringAssert.Contains(historias.Single().Observaciones, "rechazado");
        }

        // ---------- POST DireccionEntrega ----------

        private static CambiarDireccionEntregaEnvioDTO DireccionNueva() => new CambiarDireccionEntregaEnvioDTO
        {
            Nombre = "CLIENTE NUEVO",
            Direccion = "Calle Estafeta 1",
            CodigoPostal = "31001",
            Poblacion = "Pamplona",
            Telefono = "699111222",
            Correo = "nuevo@example.com"
        };

        [TestMethod]
        public async Task DireccionEntrega_EnvioInexistente_404()
        {
            SinEnvio();
            Assert.IsInstanceOfType(await controller.CambiarDireccionEntrega(99, DireccionNueva()), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task DireccionEntrega_SinTelefonoNiCorreoEnCtt_400ClaroSinLlamar()
        {
            var clienteCtt = A.Fake<IClienteRestCTT>();
            var config = new ConfiguracionCTT("https://api-test.cttexpress.com/integrations/", "id", "secreto", "8090300001", new RemitenteCTT());
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(new AgenciaRemotaCTT(clienteCtt, config));
            EnviosAgencia envio = ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            CambiarDireccionEntregaEnvioDTO datos = DireccionNueva();
            datos.Telefono = null;
            datos.Correo = " ";

            var r = await controller.CambiarDireccionEntrega(1, datos) as BadRequestErrorMessageResult;

            Assert.IsNotNull(r);
            StringAssert.Contains(r.Message, "el teléfono y el correo electrónico");
            A.CallTo(() => clienteCtt.EnviarAsync(A<HttpMethod>._, A<string>._, A<object>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual("Calle Mayor 1", envio.Direccion, "El envío no se toca");
        }

        [TestMethod]
        public async Task DireccionEntrega_Ok_ActualizaElEnvioYDejaHistoria()
        {
            EnviosAgencia envio = ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            DireccionEntregaRemota enviada = null;
            A.CallTo(() => gestion.CambiarDireccionEntregaAsync(ALBARAN, A<DireccionEntregaRemota>._))
                .Invokes((string a, DireccionEntregaRemota d) => enviada = d)
                .Returns(new ResultadoOperacionRemota { Exito = true });

            var r = await controller.CambiarDireccionEntrega(1, DireccionNueva()) as OkNegotiatedContentResult<ResultadoGestionEnvioDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual("ES", enviada.Pais, "Sin país, España");
            Assert.AreEqual("CLIENTE NUEVO", envio.Nombre);
            Assert.AreEqual("Calle Estafeta 1", envio.Direccion);
            Assert.AreEqual("31001", envio.CodPostal);
            Assert.AreEqual("Pamplona", envio.Poblacion);
            Assert.AreEqual("699111222", envio.Telefono);
            Assert.AreEqual("nuevo@example.com", envio.Email);
            EnvioHistoria h = historias.Single();
            Assert.AreEqual(GestionEnTransitoEnvio.OPERACION_DIRECCION, h.Campo);
            StringAssert.Contains(h.Observaciones, "Calle Mayor 1");
            StringAssert.Contains(h.Observaciones, "Calle Estafeta 1");
            Assert.IsTrue(h.ValorAnterior.Length <= 50);
            Assert.AreEqual(1, correos.Count);
        }

        [TestMethod]
        public async Task DireccionEntrega_LaAgenciaRechaza_NoTocaElEnvio()
        {
            EnviosAgencia envio = ConEnvio(Constantes.Agencias.ESTADO_TRAMITADO);
            A.CallTo(() => gestion.CambiarDireccionEntregaAsync(ALBARAN, A<DireccionEntregaRemota>._))
                .ThrowsAsync(new AgenciaRemotaException("CTT respondió 400: /new_delivery_address bad"));

            var r = await controller.CambiarDireccionEntrega(1, DireccionNueva()) as BadRequestErrorMessageResult;

            Assert.IsNotNull(r);
            Assert.AreEqual("Calle Mayor 1", envio.Direccion);
            Assert.AreEqual(0, correos.Count);
        }
    }
}
