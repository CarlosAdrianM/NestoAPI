using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using Newtonsoft.Json.Linq;
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
    /// PUT api/EnviosAgencias/{id} sobre un envío ya registrado en la agencia.
    /// <list type="bullet">
    /// <item>NestoAPI#512 (22/09/26, envío 248984 de CTT): el botón «Modificar Envío» de En curso cambiaba el reembolso solo
    /// en nuestra BD (sin historial, sin contabilidad) mientras CTT seguía con los 500 € del manifiesto.</item>
    /// <item>Envío 249990 (Aida, 28/10/26): lo mismo con el retorno; CTT no recogió el bulto.</item>
    /// <item>NestoAPI#725 (Carlos, 28/10/26): nunca una cosa en nuestra BD y otra en la de la agencia. Si cambia un dato que
    /// viajó, 409 CAMBIO_SIN_AGENCIA; solo con la confirmación de la agencia (y con quién se habló) se guarda, marcado en
    /// Modificaciones y en la historia, y el reembolso se contabiliza como en «Modificar datos».</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class PutEnviosAgenciaReembolsoTests
    {
        private const string ALBARAN_CTT = "0082800082809772552452";
        private const string CONFIRMACION = "Marta de CTT Alcobendas, 28/10 a las 12:30";
        private static readonly DateTime HOY = new DateTime(2026, 10, 28);

        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private readonly List<EnviosAgencia> envios = new List<EnviosAgencia>();
        private readonly List<EnvioHistoria> historia = new List<EnvioHistoria>();
        private readonly List<Modificacion> modificaciones = new List<Modificacion>();
        private IContabilidadService contabilidad;
        private List<PreContabilidad> apuntes;
        private ControladorSinEntry controller;

        /// <summary>db.Entry no es fakeable: se cuenta cuántas veces se marcaría el envío como modificado.</summary>
        private class ControladorSinEntry : EnviosAgenciasController
        {
            public ControladorSinEntry(NVEntities db, ITramitacionEnviosService servicio) : base(db, servicio) { }
            public List<EnviosAgencia> Marcados { get; } = new List<EnviosAgencia>();
            internal override void MarcarEnvioModificado(EnviosAgencia enviosAgencia) => Marcados.Add(enviosAgencia);
        }

        private class TransaccionDirecta : IAmbitoTransaccion
        {
            public Task<T> EjecutarAsync<T>(NVEntities db, Func<Task<T>> cuerpo) => cuerpo();
        }

        [TestInitialize]
        public void Setup()
        {
            envios.Clear();
            historia.Clear();
            modificaciones.Clear();
            apuntes = null;

            db = A.Fake<NVEntities>();
            fakeEnvios = FakeDbSet(envios);
            A.CallTo(() => fakeEnvios.AsNoTracking()).Returns(fakeEnvios);
            A.CallTo(() => fakeEnvios.Include(A<string>.Ignored)).Returns(fakeEnvios);
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.ExtractosCliente).Returns(FakeDbSet(new List<ExtractoCliente>()));
            DbSet<EnvioHistoria> fakeHistoria = A.Fake<DbSet<EnvioHistoria>>();
            A.CallTo(() => fakeHistoria.Add(A<EnvioHistoria>.Ignored)).Invokes((EnvioHistoria h) => historia.Add(h)).ReturnsLazily((EnvioHistoria h) => h);
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistoria);
            DbSet<Modificacion> fakeModificaciones = A.Fake<DbSet<Modificacion>>();
            A.CallTo(() => fakeModificaciones.Add(A<Modificacion>.Ignored)).Invokes((Modificacion m) => modificaciones.Add(m)).ReturnsLazily((Modificacion m) => m);
            A.CallTo(() => db.Modificaciones).Returns(fakeModificaciones);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            contabilidad = A.Fake<IContabilidadService>();
            A.CallTo(() => contabilidad.CrearLineas(db, A<List<PreContabilidad>>.Ignored))
                .Invokes((NVEntities d, List<PreContabilidad> l) => apuntes = l).Returns(Task.FromResult(2));
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", "_Reembolso", A<string>.Ignored)).Returns(Task.FromResult(88140));

            var servicio = new TramitacionEnviosService(db, contabilidad, () => HOY, A.Fake<IProcedimientosExtractoCliente>(), new TransaccionDirecta());
            controller = new ControladorSinEntry(db, servicio)
            {
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/249990") },
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Alfredo") }, "Test"))
            };
            controller.Configuration = new HttpConfiguration();
        }

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            IQueryable<T> data = datos.AsQueryable();
            DbSet<T> fakeSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider).Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            return fakeSet;
        }

        private static EnviosAgencia Envio(int agencia = Constantes.Agencias.AGENCIA_CTT, string codigoBarras = ALBARAN_CTT,
            short estado = (short)Constantes.Agencias.ESTADO_EN_CURSO, decimal reembolso = 500M, short retorno = 0) => new EnviosAgencia
            {
                Numero = 249990,
                Empresa = "1  ",
                Cliente = "15191     ",
                Contacto = "0  ",
                Pedido = 929000,
                Vendedor = "NV ",
                Fecha = new DateTime(2026, 10, 27),
                Agencia = agencia,
                Estado = estado,
                CodigoBarras = codigoBarras,
                Servicio = agencia == Constantes.Agencias.AGENCIA_CTT ? (short)48 : (short)96,
                Horario = agencia == Constantes.Agencias.AGENCIA_CTT ? (short)0 : (short)18,
                Retorno = retorno,
                Reembolso = reembolso,
                Nombre = "AIDA ESTETICA",
                Direccion = "Calle Mayor 1",
                CodPostal = "28001",
                Poblacion = "MADRID",
                Pais = 34,
                Bultos = 1,
                Peso = 2M,
                Observaciones = "Entregar por la mañana",
                Empresa1 = new Empresa { Número = "1  ", FormaPagoEfectivo = "EFC", DelegaciónVarios = "ALG", FormaVentaVarios = "DIR" },
                AgenciasTransporte = new AgenciaTransporte { Numero = (short)agencia, Nombre = "CTT", CuentaReembolsos = "5720000013 " }
            };

        private EnviosAgencia Registrado(int agencia = Constantes.Agencias.AGENCIA_CTT, string codigoBarras = ALBARAN_CTT,
            short estado = (short)Constantes.Agencias.ESTADO_EN_CURSO, decimal reembolso = 500M, short retorno = 0)
        {
            EnviosAgencia envio = Envio(agencia, codigoBarras, estado, reembolso, retorno);
            envios.Add(envio);
            return envio;
        }

        private static async Task<HttpResponseMessage> Respuesta(IHttpActionResult resultado)
            => await resultado.ExecuteAsync(System.Threading.CancellationToken.None);

        // ---- Sin confirmación: 409 y nada guardado ----

        [TestMethod]
        public async Task RegistradoEnCTT_CambiarReembolsoSinConfirmacion_409YNoGuarda()
        {
            _ = Registrado(reembolso: 500M);
            EnviosAgencia enviado = Envio(reembolso: 0M);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, enviado);

            HttpResponseMessage respuesta = await Respuesta(resultado);
            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            JObject error = JObject.Parse(await respuesta.Content.ReadAsStringAsync());
            Assert.AreEqual(EnviosAgenciasController.CODIGO_CAMBIO_SIN_AGENCIA, (string)error["Codigo"]);
            StringAssert.Contains(error["Campos"].ToString(), "Reembolso");
            StringAssert.Contains((string)error["Mensaje"], "NO en el de la agencia");
            Assert.AreEqual(0, controller.Marcados.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
            Assert.AreEqual(0, historia.Count);
            Assert.AreEqual(0, modificaciones.Count);
        }

        [TestMethod]
        public async Task RegistradoEnCTT_CambiarRetornoSinConfirmacion_409()
        {
            // El envío 249990: registrado sin retorno y marcado «Con retorno» por el PUT.
            _ = Registrado(retorno: 0, reembolso: 0M);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, Envio(retorno: 1, reembolso: 0M));

            Assert.AreEqual(HttpStatusCode.Conflict, (await Respuesta(resultado)).StatusCode);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RegistradoEnInnovatrans_CambiarDireccionSinConfirmacion_409()
        {
            _ = Registrado(agencia: Constantes.Agencias.AGENCIA_INNOVATRANS, reembolso: 0M);
            EnviosAgencia enviado = Envio(agencia: Constantes.Agencias.AGENCIA_INNOVATRANS, reembolso: 0M);
            enviado.Direccion = "Calle Menor 2";

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, enviado);

            HttpResponseMessage respuesta = await Respuesta(resultado);
            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            StringAssert.Contains(JObject.Parse(await respuesta.Content.ReadAsStringAsync())["Campos"].ToString(), "Dirección");
        }

        // ---- Sin cambios en lo que viajó, pendientes y GLS: como siempre ----

        [TestMethod]
        public async Task RegistradoEnCTT_CambiarSoloLoNuestro_GuardaSinPreguntar()
        {
            _ = Registrado();
            EnviosAgencia enviado = Envio();
            enviado.Atencion = "Aida";
            enviado.Estado = Constantes.Agencias.ESTADO_TRAMITADO;

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, enviado);

            Assert.IsNotInstanceOfType(resultado, typeof(ResponseMessageResult));
            Assert.AreEqual(1, controller.Marcados.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, historia.Count);
            Assert.AreEqual(0, modificaciones.Count);
        }

        [TestMethod]
        public async Task PendienteSinAlbaran_CambiarReembolso_GuardaSinPreguntar()
        {
            // Antes de registrar en la agencia el reembolso se puede corregir libremente.
            _ = Registrado(codigoBarras: null, estado: (short)Constantes.Agencias.ESTADO_PENDIENTE);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, Envio(codigoBarras: null, estado: (short)Constantes.Agencias.ESTADO_PENDIENTE, reembolso: 0M));

            Assert.IsNotInstanceOfType(resultado, typeof(ResponseMessageResult));
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.IsNull(apuntes, "un pendiente no ha contabilizado nada");
        }

        [TestMethod]
        public async Task Gls_CambiarReembolsoYRetorno_GuardaSinPreguntar()
        {
            // GLS manda los datos al cierre del día: cambiarlos antes es lo normal.
            _ = Registrado(agencia: Constantes.Agencias.AGENCIA_GLS, codigoBarras: "61197140248079", reembolso: 500M, retorno: 0);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990,
                Envio(agencia: Constantes.Agencias.AGENCIA_GLS, codigoBarras: "61197140248079", reembolso: 0M, retorno: 1));

            Assert.IsNotInstanceOfType(resultado, typeof(ResponseMessageResult));
            Assert.IsNotInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, modificaciones.Count);
        }

        // ---- Con confirmación ----

        [TestMethod]
        public async Task Confirmado_TextoCorto_400YNoGuarda()
        {
            _ = Registrado(reembolso: 0M);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, Envio(reembolso: 0M, retorno: 1),
                confirmadoConLaAgencia: true, confirmacionAgencia: " CTT ");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "con quién ha hablado");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Confirmado_SinTexto_400()
        {
            _ = Registrado(reembolso: 0M);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, Envio(reembolso: 0M, retorno: 1),
                confirmadoConLaAgencia: true, confirmacionAgencia: null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Confirmado_CambioDeRetorno_GuardaYQuedaMarcadoEnHistoriaYModificaciones()
        {
            _ = Registrado(retorno: 0, reembolso: 0M);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, Envio(retorno: 1, reembolso: 0M),
                confirmadoConLaAgencia: true, confirmacionAgencia: CONFIRMACION);

            Assert.IsNotInstanceOfType(resultado, typeof(ResponseMessageResult));
            Assert.IsNotInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.AreEqual(1, controller.Marcados.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();

            EnvioHistoria fila = historia.Single();
            Assert.AreEqual(249990, fila.NumeroEnvio);
            Assert.AreEqual("Retorno", fila.Campo);
            Assert.AreEqual("0", fila.ValorAnterior);
            StringAssert.Contains(fila.Observaciones, TramitacionEnviosService.PREFIJO_CAMBIO_CONFIRMADO_AGENCIA + CONFIRMACION);
            StringAssert.Contains(fila.Usuario, "Alfredo");

            Modificacion modificacion = modificaciones.Single();
            Assert.AreEqual("EnviosAgencia", modificacion.Tabla);
            StringAssert.Contains(modificacion.Anterior, "Retorno=0");
            StringAssert.Contains(modificacion.Nuevo, "Retorno=1");
            StringAssert.Contains(modificacion.Nuevo, "confirmado por la agencia por teléfono");
            StringAssert.Contains(modificacion.Nuevo, CONFIRMACION);
            StringAssert.Contains(modificacion.Nuevo, "Alfredo");

            Assert.IsNull(apuntes, "sin cambio de reembolso no se contabiliza nada");
        }

        [TestMethod]
        public async Task Confirmado_CambioDeReembolso_ContabilizaComoModificarDatos()
        {
            _ = Registrado(reembolso: 500M);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, Envio(reembolso: 0M),
                confirmadoConLaAgencia: true, confirmacionAgencia: CONFIRMACION);

            Assert.IsNotInstanceOfType(resultado, typeof(ResponseMessageResult));
            Assert.AreEqual("Reembolso", historia.Single().Campo);
            StringAssert.StartsWith(historia.Single().ValorAnterior, "500,00");
            // Deshago de los 500 € en _Reembolso (el nuevo es 0: no hay rehago), como «Modificar datos».
            Assert.IsNotNull(apuntes);
            Assert.AreEqual(1, apuntes.Count);
            Assert.AreEqual(500M, apuntes[0].Debe);
            Assert.AreEqual("_Reembolso", apuntes[0].Diario);
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", "_Reembolso", A<string>.Ignored)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Confirmado_ReembolsoYaCobrado_400()
        {
            EnviosAgencia registrado = Registrado(reembolso: 500M);
            registrado.FechaPagoReembolso = new DateTime(2026, 10, 20);

            IHttpActionResult resultado = await controller.PutEnviosAgencia(249990, Envio(reembolso: 0M),
                confirmadoConLaAgencia: true, confirmacionAgencia: CONFIRMACION);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }
    }
}
