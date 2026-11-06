using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.CTT;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#722 (06/11/26): aplazar un pedido con picking cuyo envío ya tiene la agencia. Con CTT, el cambio de fecha va por
    /// la MISMA lógica que POST api/EnviosAgencias/{id}/FechaEntrega (aquí con el decorador de producción y un HttpMessageHandler
    /// falso); con otra agencia, el PUT se rechaza con PEDIDO_YA_EN_AGENCIA y Nesto ofrece el correo a logística.
    /// </summary>
    [TestClass]
    public class AplazamientoEnvioEnAgenciaTests
    {
        private const string ALBARAN = "0082800082809800827793";
        private const int ENVIO_CTT = 250655;
        private const int ENVIO_GLS = 250600;
        private const string BASE = "https://api-test.cttexpress.com/integrations/";
        private static readonly DateTime HOY = new DateTime(2026, 11, 6);
        private static readonly DateTime LUNES = new DateTime(2026, 11, 9);

        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private HandlerCtt handler;
        private readonly List<MailMessage> correos = new List<MailMessage>();
        private EnviosAgencia envioCtt;
        private EnviosAgencia envioGls;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            DbSet<EnvioHistoria> fakeHistorias = A.Fake<DbSet<EnvioHistoria>>(o => o.Implements<IQueryable<EnvioHistoria>>().Implements<IDbAsyncEnumerable<EnvioHistoria>>());
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistorias);
            DbSet<AgenciaLlamadaWeb> fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => db.AgenciasTransportes).Returns(CrearSet(new List<AgenciaTransporte>
            {
                new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_CTT, Nombre = "CTT" },
                new AgenciaTransporte { Numero = 8, Nombre = "GLS" }
            }));

            envioCtt = new EnviosAgencia { Numero = ENVIO_CTT, Empresa = "1", Agencia = Constantes.Agencias.AGENCIA_CTT, Pedido = 928083, Cliente = "15191",
                Estado = Constantes.Agencias.ESTADO_TRAMITADO, CodigoBarras = ALBARAN, FechaEntrega = HOY };
            envioGls = new EnviosAgencia { Numero = ENVIO_GLS, Empresa = "1", Agencia = 8, Pedido = 928084, Cliente = "15191",
                Estado = Constantes.Agencias.ESTADO_TRAMITADO, CodigoBarras = "6142" };
            A.CallTo(() => fakeEnvios.FindAsync(ENVIO_CTT)).Returns(Task.FromResult(envioCtt));
            A.CallTo(() => fakeEnvios.FindAsync(ENVIO_GLS)).Returns(Task.FromResult(envioGls));

            // CTT tal como la compone producción (decorador de reintentos) sobre un HttpMessageHandler falso.
            handler = new HandlerCtt();
            var config = new ConfiguracionCTT(BASE, "aplazar722-" + Guid.NewGuid().ToString("N"), "secreto", "8090300001", new RemitenteCTT());
            IAgenciaRemota ctt = new AgenciaRemotaCTTConReintentos(new AgenciaRemotaCTT(new ClienteRestCTT(config, new HttpClient(handler)), config));
            IFabricaAgenciasRemotas fabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fabrica.Crear(Constantes.Agencias.AGENCIA_CTT)).Returns(ctt);
            A.CallTo(() => fabrica.Crear(8)).Returns(null);

            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => correos.Add(m)).Returns(true);

            Cambio = new CambioFechaEnvioAgencia(() => new EnviosAgenciasController(db, fabrica)
            {
                ServicioCorreoGestiones = correo,
                HoyGestiones = () => HOY,
                EsDiaSinReparto = dia => dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday,
                User = new System.Security.Principal.GenericPrincipal(new System.Security.Principal.GenericIdentity("NUEVAVISION\\Alfredo"), new string[0])
            });
        }

        private CambioFechaEnvioAgencia Cambio;

        [TestMethod]
        public async Task EnvioTramitadoDeCtt_AdmiteElCambio_YCttCambiaLaFecha()
        {
            handler.Responder("rpc-change-delivery-options", HttpStatusCode.Accepted, string.Empty);

            int envio = await AplazamientoEnvioEnAgencia.EnvioACambiarOExcepcion(Cambio, "1", 928083, ENVIO_CTT, LUNES, "NUEVAVISION\\Alfredo");
            ResultadoCambioFechaEnAgencia r = await Cambio.CambiarFecha(envio, LUNES);

            Assert.AreEqual(ENVIO_CTT, envio);
            Assert.IsTrue(r.Hecho, r.Mensaje);
            Assert.AreEqual("CTT", r.NombreAgencia);
            int i = handler.Peticiones.FindIndex(p => p.RequestUri.AbsolutePath.EndsWith("rpc-change-delivery-options"));
            Assert.IsTrue(i >= 0, "Se ha llamado a CTT");
            StringAssert.Contains(handler.Cuerpos[i], "\"new_delivery_date\":\"2026-11-09\"");
            Assert.AreEqual(LUNES, envioCtt.FechaEntrega, "Como el endpoint: la fecha nueva en el envío");
            Assert.AreEqual(1, correos.Count, "Y el aviso de la gestión a logística, como el endpoint");
            Assert.AreEqual("La fecha de entrega se ha cambiado también en CTT (envío 250655): la entregará el 09/11/2026.",
                AplazamientoEnvioEnAgencia.MensajeCambiadaEnAgencia(r));
        }

        [TestMethod]
        public async Task EnvioTramitadoDeCtt_CttRechazaLaFecha_NoSeHace_YElPutSeRechazaConSuMotivo()
        {
            handler.Responder("rpc-change-delivery-options", HttpStatusCode.Forbidden,
                @"{""error"":{""error_code"":403,""error_extended_info"":{""message"":""Can not change delivery date in state 2100""}}}");

            ResultadoCambioFechaEnAgencia r = await Cambio.CambiarFecha(ENVIO_CTT, LUNES);
            NestoBusinessException ex = AplazamientoEnvioEnAgencia.ExcepcionAgenciaNoLoHace("1", 928083, r, "NUEVAVISION\\Alfredo");

            Assert.IsTrue(r.Admitida);
            Assert.IsFalse(r.Hecho);
            Assert.AreEqual(HOY, envioCtt.FechaEntrega, "No se toca");
            StringAssert.Contains(ex.Message, "Can not change delivery date");
            StringAssert.Contains(ex.Message, "No se ha guardado el pedido");
            Assert.AreEqual(AplazamientoEnvioEnAgencia.CODIGO_AGENCIA_RECHAZA_FECHA, ex.GetErrorCode());
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task EnvioTramitadoDeCtt_FechaNoLaborable_NoSeLlamaACtt()
        {
            ResultadoCambioFechaEnAgencia r = await Cambio.CambiarFecha(ENVIO_CTT, new DateTime(2026, 11, 8));

            Assert.IsFalse(r.Hecho);
            StringAssert.Contains(r.Mensaje, "no se reparte");
            Assert.IsFalse(handler.Peticiones.Any(p => p.RequestUri.AbsolutePath.Contains("management")));
        }

        [TestMethod]
        public void AgenciaNoResponde_502ConSuCodigo()
        {
            NestoBusinessException ex = AplazamientoEnvioEnAgencia.ExcepcionAgenciaNoLoHace("1", 928083,
                new ResultadoCambioFechaEnAgencia { Envio = ENVIO_CTT, Fecha = LUNES, NombreAgencia = "CTT", AgenciaNoResponde = true, Mensaje = "CTT no responde ahora mismo." }, "Alfredo");

            Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
            Assert.AreEqual(AplazamientoEnvioEnAgencia.CODIGO_AGENCIA_NO_RESPONDE, ex.GetErrorCode());
        }

        [TestMethod]
        public async Task EnvioTramitadoDeOtraAgencia_RechazoConElTextoYElFlagDelCorreo()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                AplazamientoEnvioEnAgencia.EnvioACambiarOExcepcion(Cambio, "1", 928084, ENVIO_GLS, LUNES, "NUEVAVISION\\Alfredo"));

            Assert.AreEqual(AplazamientoEnvioEnAgencia.CODIGO_YA_EN_AGENCIA, ex.GetErrorCode());
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual("El pedido 928084 ya lo tiene la agencia (envío 250600): no llegamos a tiempo para retrasar la entrega. " +
                "La única opción es que el cliente lo rechace. Si quiere, mandamos un correo a logística para que intenten hablar con la agencia.", ex.Message);
            Assert.AreEqual(ENVIO_GLS, ex.Context.AdditionalData["envio"]);
            Assert.AreEqual(true, ex.Context.AdditionalData["puedeAvisarLogistica"]);
            Assert.AreEqual("2026-11-09", ex.Context.AdditionalData["fechaEntrega"]);
            Assert.AreEqual(0, handler.Peticiones.Count, "Sin llamar a nadie");
        }

        [TestMethod]
        public async Task EnvioDeCttYaEntregado_NoAdmiteCambiarLaFecha_Rechazo()
        {
            envioCtt.Estado = Constantes.Agencias.ESTADO_ENTREGADO;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                AplazamientoEnvioEnAgencia.EnvioACambiarOExcepcion(Cambio, "1", 928083, ENVIO_CTT, LUNES, "Alfredo"));

            Assert.AreEqual(AplazamientoEnvioEnAgencia.CODIGO_YA_EN_AGENCIA, ex.GetErrorCode());
        }

        [TestMethod]
        public async Task SiNoSePuedeSaberLoQueAdmite_Rechazo()
        {
            ICambioFechaEnvioAgencia roto = A.Fake<ICambioFechaEnvioAgencia>();
            A.CallTo(() => roto.Gestiones(A<int>._)).ThrowsAsync(new InvalidOperationException("BD caída"));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                AplazamientoEnvioEnAgencia.EnvioACambiarOExcepcion(roto, "1", 928083, ENVIO_CTT, LUNES, "Alfredo"));

            Assert.AreEqual(AplazamientoEnvioEnAgencia.CODIGO_YA_EN_AGENCIA, ex.GetErrorCode());
            A.CallTo(() => roto.CambiarFecha(A<int>._, A<DateTime>._)).MustNotHaveHappened();
        }

        // ---------- POST api/PedidosVenta/{empresa}/{numero}/AvisarLogisticaAplazamiento ----------

        private PedidosVentaController ControllerCorreo(List<MailMessage> enviados, bool correoSale = true)
        {
            NVEntities dbPedidos = A.Fake<NVEntities>();
            A.CallTo(() => dbPedidos.CabPedidoVtas).Returns(CrearSet(new List<CabPedidoVta>
            {
                new CabPedidoVta { Empresa = "1", Número = 928084, Nº_Cliente = "15191", Contacto = "0", Vendedor = "SU" }
            }));
            A.CallTo(() => dbPedidos.EnviosAgencias).Returns(CrearSet(new List<EnviosAgencia> { envioGls }));
            A.CallTo(() => dbPedidos.AgenciasTransportes).Returns(CrearSet(new List<AgenciaTransporte> { new AgenciaTransporte { Numero = 8, Nombre = "GLS" } }));
            A.CallTo(() => dbPedidos.Clientes).Returns(CrearSet(new List<Cliente> { new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0", Nombre = "Isabel Hurtado" } }));
            A.CallTo(() => dbPedidos.Vendedores).Returns(CrearSet(new List<Vendedor> { new Vendedor { Empresa = "1", Número = "SU", Mail = "susana@example.com" } }));
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviados.Add(m)).Returns(correoSale);
            return new PedidosVentaController(dbPedidos)
            {
                ServicioCorreoAplazamiento = correo,
                User = new System.Security.Principal.GenericPrincipal(new System.Security.Principal.GenericIdentity("NUEVAVISION\\Susana"), new string[0])
            };
        }

        [TestMethod]
        public async Task AvisarLogistica_MandaElCorreoALogisticaConCopiaAlVendedor()
        {
            var enviados = new List<MailMessage>();
            PedidosVentaController controller = ControllerCorreo(enviados);

            var r = await controller.PostAvisarLogisticaAplazamiento("1", 928084, new AvisarLogisticaAplazamientoRequest { Fecha = LUNES })
                as OkNegotiatedContentResult<AvisarLogisticaAplazamientoRespuesta>;

            Assert.IsNotNull(r);
            MailMessage m = enviados.Single();
            Assert.AreEqual(Constantes.Correos.LOGISTICA, m.To.Single().Address);
            Assert.AreEqual("susana@example.com", m.CC.Single().Address);
            StringAssert.Contains(m.Subject, "928084");
            StringAssert.Contains(m.Body, "250600");
            StringAssert.Contains(m.Body, "6142");
            StringAssert.Contains(m.Body, "09/11/2026");
            StringAssert.Contains(m.Body, "Susana");
            StringAssert.Contains(m.Body, "Isabel Hurtado");
            StringAssert.Contains(m.Body, "No le hemos prometido nada");
            CollectionAssert.AreEquivalent(new[] { Constantes.Correos.LOGISTICA, "susana@example.com" }, r.Content.Destinatarios);
            StringAssert.Contains(r.Content.Mensaje, "con copia al vendedor");
        }

        [TestMethod]
        public async Task AvisarLogistica_SinFecha_400_YPedidoInexistente_404()
        {
            var enviados = new List<MailMessage>();
            PedidosVentaController controller = ControllerCorreo(enviados);

            Assert.IsInstanceOfType(await controller.PostAvisarLogisticaAplazamiento("1", 928084, new AvisarLogisticaAplazamientoRequest()), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await controller.PostAvisarLogisticaAplazamiento("1", 1, new AvisarLogisticaAplazamientoRequest { Fecha = LUNES }), typeof(NotFoundResult));
            Assert.IsInstanceOfType(await controller.PostAvisarLogisticaAplazamiento("1", 928084, new AvisarLogisticaAplazamientoRequest { Fecha = LUNES, Envio = 1 }), typeof(NotFoundResult));
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public async Task AvisarLogistica_ElCorreoNoSale_502()
        {
            PedidosVentaController controller = ControllerCorreo(new List<MailMessage>(), correoSale: false);

            var r = await controller.PostAvisarLogisticaAplazamiento("1", 928084, new AvisarLogisticaAplazamientoRequest { Fecha = LUNES })
                as NegotiatedContentResult<System.Web.Http.HttpError>;

            Assert.IsNotNull(r);
            Assert.AreEqual(HttpStatusCode.BadGateway, r.StatusCode);
        }

        private static DbSet<T> CrearSet<T>(List<T> datos) where T : class
        {
            DbSet<T> set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            IQueryable<T> data = datos.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)set).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)set).Provider).Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)set).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            A.CallTo(() => set.AsNoTracking()).Returns(set);
            return set;
        }

        private class HandlerCtt : HttpMessageHandler
        {
            private readonly Dictionary<string, (HttpStatusCode codigo, string cuerpo)> respuestas = new Dictionary<string, (HttpStatusCode, string)>
            {
                ["oauth2/token"] = (HttpStatusCode.OK, @"{""access_token"":""TOKEN-722"",""expires_in"":86400,""token_type"":""Bearer""}")
            };
            public readonly List<HttpRequestMessage> Peticiones = new List<HttpRequestMessage>();
            public readonly List<string> Cuerpos = new List<string>();

            public void Responder(string sufijoRuta, HttpStatusCode codigo, string cuerpo) => respuestas[sufijoRuta] = (codigo, cuerpo);

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Peticiones.Add(request);
                Cuerpos.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync());
                KeyValuePair<string, (HttpStatusCode codigo, string cuerpo)> r = respuestas.FirstOrDefault(kv => request.RequestUri.AbsolutePath.EndsWith(kv.Key));
                if (r.Key == null)
                {
                    throw new InvalidOperationException("El test no preparó respuesta para " + request.RequestUri.AbsolutePath);
                }
                return new HttpResponseMessage(r.Value.codigo) { Content = new StringContent(r.Value.cuerpo ?? string.Empty) };
            }
        }
    }
}
