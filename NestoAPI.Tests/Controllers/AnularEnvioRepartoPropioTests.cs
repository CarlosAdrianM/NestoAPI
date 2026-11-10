using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Novedades 1162 (Alfredo, 10/11/26, pedido 930294): el envío 250772 del reparto propio quedó Tramitado al imprimir la
    /// etiqueta con un reembolso de 53 € que no tocaba. No se podía ni modificar ni anular, y al crear otro (250782, registrando
    /// un pendiente nuevo) el repartidor vio dos paradas.
    /// <list type="bullet">
    /// <item>POST Anular: en la 14 también el Tramitado sin empezar a repartir → etiqueta pendiente, con su historia y aviso a las PDA.</item>
    /// <item>PUT que registra un pendiente de la 14: la regla de #847 (409 con el envío que ya tiene el pedido).</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class AnularEnvioRepartoPropioTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 10);
        private const int PEDIDO = 930294;

        private NVEntities db;
        private readonly List<EnviosAgencia> envios = new List<EnviosAgencia>();
        private readonly List<EnvioHistoria> historia = new List<EnvioHistoria>();
        private IFabricaAgenciasRemotas fabrica;
        private IAgenciaRemota remotaInnovatrans;
        private IDatosAnulacionEnvioRepartoPropio datosAnulacion;
        private DatosAnulacionEnvioRepartoPropio datosLeidos;
        private List<string> avisosReparto;
        private ControladorSinEntry controller;

        /// <summary>db.Entry no es fakeable: se apunta qué envío marcaría el PUT como modificado.</summary>
        private class ControladorSinEntry : EnviosAgenciasController
        {
            public ControladorSinEntry(NVEntities db, IFabricaAgenciasRemotas fabrica) : base(db, fabrica) { }
            public List<EnviosAgencia> Marcados { get; } = new List<EnviosAgencia>();
            internal override void MarcarEnvioModificado(EnviosAgencia enviosAgencia) => Marcados.Add(enviosAgencia);
        }

        [TestInitialize]
        public void Setup()
        {
            envios.Clear();
            historia.Clear();
            db = A.Fake<NVEntities>();
            DbSet<EnviosAgencia> fakeEnvios = FakeDbSet(envios);
            A.CallTo(() => fakeEnvios.AsNoTracking()).Returns(fakeEnvios);
            A.CallTo(() => fakeEnvios.FindAsync(A<object[]>._))
                .ReturnsLazily((object[] claves) => Task.FromResult(envios.FirstOrDefault(e => e.Numero == (int)claves[0])));
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            DbSet<EnvioHistoria> fakeHistoria = A.Fake<DbSet<EnvioHistoria>>();
            A.CallTo(() => fakeHistoria.Add(A<EnvioHistoria>.Ignored)).Invokes((EnvioHistoria h) => historia.Add(h)).ReturnsLazily((EnvioHistoria h) => h);
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistoria);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(A.Fake<DbSet<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            remotaInnovatrans = A.Fake<IAgenciaRemota>();
            A.CallTo(() => remotaInnovatrans.Intercambios).Returns(new List<IntercambioRemoto>());
            A.CallTo(() => remotaInnovatrans.AnularAsync(A<string>._)).Returns(Task.FromResult(new ResultadoOperacionRemota { Exito = true }));
            fabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fabrica.CrearAnulacion(Constantes.Agencias.AGENCIA_REPARTO_PROPIO))
                .Returns(new AgenciaRemotaRepartoPropio(n => Task.FromResult(envios.FirstOrDefault(e => e.Numero == n)), () => HOY));
            A.CallTo(() => fabrica.CrearAnulacion(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(remotaInnovatrans);

            datosLeidos = new DatosAnulacionEnvioRepartoPropio();
            datosAnulacion = A.Fake<IDatosAnulacionEnvioRepartoPropio>();
            A.CallTo(() => datosAnulacion.Leer(A<EnviosAgencia>._)).ReturnsLazily(() => Task.FromResult(datosLeidos));
            avisosReparto = new List<string>();

            controller = new ControladorSinEntry(db, fabrica)
            {
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/250772/Anular") },
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Alfredo") }, "Test")),
                MotivoPedidoRetenido = (e, p, rp) => Task.FromResult<string>(null),
                HoyRepartoPropio = () => HOY,
                DatosAnulacionRepartoPropio = datosAnulacion,
                AvisarRepartoCambiado = usuario => avisosReparto.Add(usuario)
            };
            controller.Configuration = new HttpConfiguration();
        }

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            DbSet<T> fakeSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider).ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            return fakeSet;
        }

        private static EnviosAgencia Envio(int numero, short estado, int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO,
            decimal reembolso = 53m, string codigoBarras = "") => new EnviosAgencia
            {
                Numero = numero,
                Empresa = "1  ",
                Cliente = "1179      ",
                Contacto = "0  ",
                Pedido = PEDIDO,
                Vendedor = "NV ",
                Agencia = agencia,
                Estado = estado,
                Fecha = HOY,
                FechaEntrega = HOY.AddDays(1),
                Servicio = 1,
                Horario = 0,
                Retorno = 0,
                Bultos = 1,
                Nombre = "CLIENTE",
                Direccion = "Calle Mayor 1",
                CodPostal = "28110",
                Poblacion = "ALGETE",
                Pais = 34,
                Reembolso = reembolso,
                FechaPagoReembolso = agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO && reembolso != 0m ? HOY.AddDays(1) : (DateTime?)null,
                CodigoBarras = codigoBarras == "" ? (estado >= Constantes.Agencias.ESTADO_EN_CURSO ? "RP" + numero : null) : codigoBarras
            };

        private EnviosAgencia Con(EnviosAgencia envio)
        {
            envios.Add(envio);
            return envio;
        }

        // ---------------------------------------------------------------- Anular

        [TestMethod]
        public async Task Anular_RepartoPropioTramitadoSinRepartir_VuelveAPendienteConHistoriaYAvisaAlReparto()
        {
            EnviosAgencia envio = Con(Envio(250772, Constantes.Agencias.ESTADO_TRAMITADO));

            IHttpActionResult resultado = await controller.AnularEnvio(250772);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EnvioAgenciaDTO>));
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_PENDIENTE, envio.Estado);
            Assert.AreEqual(string.Empty, envio.CodigoBarras, "sin albarán: al volver a imprimirlo se le da otra vez RP250772");
            Assert.AreEqual(53m, envio.Reembolso, "el reembolso no cambia: se corrige en Pendientes");
            Assert.IsNull(envio.FechaPagoReembolso, "se recalcula al registrarlo de nuevo (#778)");
            EnvioHistoria fila = historia.Single();
            Assert.AreEqual(250772, fila.NumeroEnvio);
            Assert.AreEqual("Estado", fila.Campo);
            Assert.AreEqual("1", fila.ValorAnterior);
            Assert.AreEqual(AnulacionEnvioRepartoPropio.OBSERVACIONES_HISTORIA, fila.Observaciones);
            Assert.AreEqual("NUEVAVISION\\Alfredo", fila.Usuario);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Alfredo" }, avisosReparto);
        }

        [TestMethod]
        public async Task Anular_RepartoPropioTramitadoCargado_409ConElMotivoYNoToca()
        {
            EnviosAgencia envio = Con(Envio(250772, Constantes.Agencias.ESTADO_TRAMITADO));
            datosLeidos.Cargado = true;

            IHttpActionResult resultado = await controller.AnularEnvio(250772);

            var conflicto = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(conflicto);
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            StringAssert.Contains(conflicto.Content, "El envío 250772 ya está cargado en la furgoneta: no se puede anular.");
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
            Assert.AreEqual("RP250772", envio.CodigoBarras);
            Assert.AreEqual(0, historia.Count);
            Assert.AreEqual(0, avisosReparto.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_RepartoPropioEntregado_409()
        {
            Con(Envio(250772, Constantes.Agencias.ESTADO_ENTREGADO));

            var conflicto = await controller.AnularEnvio(250772) as NegotiatedContentResult<string>;

            Assert.IsNotNull(conflicto);
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            StringAssert.Contains(conflicto.Content, "ya está entregado");
        }

        [TestMethod]
        public async Task Anular_RepartoPropioEnCurso_SigueFuncionandoSinAvisarAlReparto()
        {
            // El En curso (el Tramitar del cierre falló) no es parada todavía: se anula como antes.
            EnviosAgencia envio = Con(Envio(250772, (short)Constantes.Agencias.ESTADO_EN_CURSO));

            IHttpActionResult resultado = await controller.AnularEnvio(250772);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EnvioAgenciaDTO>));
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_PENDIENTE, envio.Estado);
            Assert.AreEqual("0", historia.Single().ValorAnterior);
            Assert.AreEqual(0, avisosReparto.Count);
        }

        [TestMethod]
        public async Task Anular_RepartoPropioPendiente_400ComoSiempre()
        {
            Con(Envio(250772, (short)Constantes.Agencias.ESTADO_PENDIENTE));

            IHttpActionResult resultado = await controller.AnularEnvio(250772);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => datosAnulacion.Leer(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_OtraAgenciaTramitada_SigueExigiendoEnCurso()
        {
            EnviosAgencia envio = Con(Envio(250773, Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.AGENCIA_INNOVATRANS,
                reembolso: 0m, codigoBarras: "6522393001"));

            IHttpActionResult resultado = await controller.AnularEnvio(250773);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "Solo se puede anular un envío En curso");
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
            A.CallTo(() => remotaInnovatrans.AnularAsync(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => datosAnulacion.Leer(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_OtraAgenciaEnCurso_ComoSiempreSinHistoriaNiAviso()
        {
            EnviosAgencia envio = Con(Envio(250774, (short)Constantes.Agencias.ESTADO_EN_CURSO, Constantes.Agencias.AGENCIA_INNOVATRANS,
                reembolso: 0m, codigoBarras: "6522393001"));

            IHttpActionResult resultado = await controller.AnularEnvio(250774);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EnvioAgenciaDTO>));
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_PENDIENTE, envio.Estado);
            Assert.AreEqual(string.Empty, envio.CodigoBarras);
            Assert.AreEqual(0, historia.Count);
            Assert.AreEqual(0, avisosReparto.Count);
            A.CallTo(() => datosAnulacion.Leer(A<EnviosAgencia>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- PUT que registra un pendiente (el hueco de #847)

        private static EnviosAgencia Registrando(EnviosAgencia pendiente)
        {
            // Lo que manda Nesto en «Insertar e Imprimir»: el pendiente con Estado En curso.
            EnviosAgencia enviado = Envio(pendiente.Numero, (short)Constantes.Agencias.ESTADO_EN_CURSO, pendiente.Agencia, pendiente.Reembolso, codigoBarras: null);
            enviado.FechaPagoReembolso = null;
            return enviado;
        }

        [TestMethod]
        public async Task Put_RegistrarPendienteConOtroEnvioDeHoyDelPedido_409ConEseEnvioYNoGuarda()
        {
            Con(Envio(250772, Constantes.Agencias.ESTADO_TRAMITADO));
            EnviosAgencia pendiente = Con(Envio(250782, (short)Constantes.Agencias.ESTADO_PENDIENTE, reembolso: 0m));
            pendiente.Reembolso = 10m; // con importe: no entra el recálculo de #785

            IHttpActionResult resultado = await controller.PutEnviosAgencia(250782, Registrando(pendiente));

            var conflicto = resultado as NegotiatedContentResult<EnvioRepartoPropioRepetidoDTO>;
            Assert.IsNotNull(conflicto, "el mismo 409 que el POST");
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreEqual(EnvioRepartoPropioRepetido.CODIGO, conflicto.Content.Codigo);
            Assert.AreEqual(250772, conflicto.Content.Envio);
            Assert.AreEqual("RP250772", conflicto.Content.CodigoBarras);
            Assert.AreEqual(0, controller.Marcados.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_RegistrarPendienteSinOtroEnvio_SeGuarda()
        {
            EnviosAgencia pendiente = Con(Envio(250782, (short)Constantes.Agencias.ESTADO_PENDIENTE, reembolso: 10m));

            IHttpActionResult resultado = await controller.PutEnviosAgencia(250782, Registrando(pendiente));

            Assert.IsNotInstanceOfType(resultado, typeof(NegotiatedContentResult<EnvioRepartoPropioRepetidoDTO>));
            Assert.AreEqual(250782, controller.Marcados.Single().Numero);
        }

        [TestMethod]
        public async Task Put_NiElPropioNiEntregadosNiOtraAgencia_CuentanComoOtro()
        {
            // El que se registra no es «otro»; un entregado o un envío de otra agencia del mismo pedido, tampoco.
            EnviosAgencia pendiente = Con(Envio(250782, (short)Constantes.Agencias.ESTADO_PENDIENTE, reembolso: 10m));
            Con(Envio(250790, Constantes.Agencias.ESTADO_ENTREGADO));
            Con(Envio(250791, Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.AGENCIA_CTT));

            IHttpActionResult resultado = await controller.PutEnviosAgencia(250782, Registrando(pendiente));

            Assert.IsNotInstanceOfType(resultado, typeof(NegotiatedContentResult<EnvioRepartoPropioRepetidoDTO>));
            Assert.AreEqual(1, controller.Marcados.Count);
        }

        [TestMethod]
        public async Task Put_PendienteQueSigueSiendoPendiente_NoSeMira()
        {
            // Corregir el pendiente en Pendientes (sin registrarlo) no choca con nada.
            Con(Envio(250772, Constantes.Agencias.ESTADO_TRAMITADO));
            EnviosAgencia pendiente = Con(Envio(250782, (short)Constantes.Agencias.ESTADO_PENDIENTE, reembolso: 10m));
            EnviosAgencia enviado = Envio(250782, (short)Constantes.Agencias.ESTADO_PENDIENTE, reembolso: 0m, codigoBarras: null);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(250782, enviado);

            Assert.IsNotInstanceOfType(resultado, typeof(NegotiatedContentResult<EnvioRepartoPropioRepetidoDTO>));
            Assert.AreEqual(1, controller.Marcados.Count);
            // Y deja la marca de «cambiado a mano» que el registro respeta (#785).
            Assert.IsTrue(ReembolsoEtiquetaPendiente.HayCambioManual(historia));
        }

        [TestMethod]
        public async Task Put_RegistrarPendienteDeOtraAgencia_NoSeMira()
        {
            Con(Envio(250772, Constantes.Agencias.ESTADO_TRAMITADO));
            EnviosAgencia pendiente = Con(Envio(250782, (short)Constantes.Agencias.ESTADO_PENDIENTE, Constantes.Agencias.AGENCIA_CTT, reembolso: 10m));
            EnviosAgencia enviado = Registrando(pendiente);
            enviado.Servicio = 48;

            IHttpActionResult resultado = await controller.PutEnviosAgencia(250782, enviado);

            Assert.IsNotInstanceOfType(resultado, typeof(NegotiatedContentResult<EnvioRepartoPropioRepetidoDTO>));
        }
    }
}
