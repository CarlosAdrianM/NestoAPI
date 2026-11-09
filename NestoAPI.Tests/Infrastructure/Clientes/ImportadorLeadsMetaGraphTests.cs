using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes.Origenes;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#623 corte 5c: los leads de Meta por la API Graph (job meta-leads cada 15 min). Mismo importador que el fichero
    /// (<see cref="ImportadorLeadsMeta"/>); aquí, la fuente Graph. Todos los datos son inventados.
    /// </summary>
    [TestClass]
    public class ImportadorLeadsMetaGraphTests
    {
        private const string ORIGEN = "META-HARMONIA-0926";
        private const string FORMULARIO = "555000111222333";
        private static readonly DateTime AHORA_UTC = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);

        private IClienteGraphMeta http;
        private IRepositorioFormulariosMeta formularios;
        private IRepositorioLeadsMeta repositorio;
        private IAltaClientesLeads alta;
        private IServicioNotificacionesPush notificaciones;
        private List<Exception> elmah;
        private AvisosUnaVezAlDia avisos;
        private ConfiguracionMetaLeads configuracion;
        private List<string> urls;

        [TestInitialize]
        public void Inicializar()
        {
            http = A.Fake<IClienteGraphMeta>();
            formularios = A.Fake<IRepositorioFormulariosMeta>();
            repositorio = A.Fake<IRepositorioLeadsMeta>();
            alta = A.Fake<IAltaClientesLeads>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            elmah = new List<Exception>();
            avisos = new AvisosUnaVezAlDia();
            configuracion = new ConfiguracionMetaLeads { Activo = true, Token = "token-de-prueba", Version = "v21.0", PaginaId = "9001" };
            urls = new List<string>();

            _ = A.CallTo(() => formularios.LeerFormularios()).Returns(Task.FromResult(new List<FormularioMetaOrigen>
            {
                new FormularioMetaOrigen { Origen = ORIGEN, MetaFormId = FORMULARIO, Activo = true, EsLead = true,
                    MetaLeadsLeidosHasta = new DateTime(2026, 11, 9, 9, 0, 0) }
            }));
            _ = A.CallTo(() => alta.DatosCodigoPostal(A<string>._)).Returns(Task.FromResult(new RespuestaDatosGeneralesClientes
            {
                CodigoPostal = "28001", Poblacion = "MADRID", Provincia = "MADRID", Ruta = "AT", VendedorEstetica = "JE"
            }));
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>._)).Returns(Task.FromResult("90001"));
            _ = A.CallTo(() => repositorio.ClienteDelLead(A<string>._, A<string>._)).Returns(Task.FromResult<string>(null));
            _ = A.CallTo(() => repositorio.BuscarParecido(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult<ClienteParecidoLead>(null));
            _ = A.CallTo(() => repositorio.PonerOrigenLead(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._, A<bool>._, A<string>._))
                .Returns(Task.FromResult(ResultadoOrigenLead.Puesto));
        }

        private ImportadorLeadsMetaGraph Crear() => new ImportadorLeadsMetaGraph(http, formularios, repositorio, alta, configuracion,
            avisos, () => notificaciones, grupo => new List<string> { grupo == Constantes.GruposSeguridad.INFORMATICA ? "carlos" : "direccion1" },
            ex => elmah.Add(ex), () => AHORA_UTC);

        private void Responder(params string[] cuerpos)
        {
            var cola = new Queue<string>(cuerpos);
            _ = A.CallTo(() => http.Get(A<string>._, A<string>._)).ReturnsLazily((string url, string token) =>
            {
                if (url.Contains("/9001?fields=access_token"))
                {
                    // Los leads se leen con el token de la página (comprobado con Meta el 09/11/26).
                    return Task.FromResult(new RespuestaGraphMeta { Estado = 200, Cuerpo = "{\"access_token\":\"token-pagina\",\"id\":\"9001\"}" });
                }
                urls.Add(url);
                return Task.FromResult(new RespuestaGraphMeta { Estado = 200, Cuerpo = cola.Count > 0 ? cola.Dequeue() : "{\"data\":[]}" });
            });
        }

        private static string Lead(string id, string fecha = "2026-11-09T09:30:00+0000", string nombre = "Ana Prueba Inventada", string email = "Ana@Ejemplo.com",
            string telefono = "+34600111222", string cp = "28001", bool? consentimiento = false, string plataforma = "ig")
        {
            var campos = new JArray();
            void Campo(string nombreCampo, string valor)
            {
                if (valor != null)
                {
                    campos.Add(new JObject { ["name"] = nombreCampo, ["values"] = new JArray(valor) });
                }
            }
            Campo("¿cuál_es_tu_situación_profesional?_", "tengo_un_centro_de_estética_");
            Campo("¿qué_te_gustaría_solicitar?", "una_demostración_de_harmonia");
            Campo("full_name", nombre);
            Campo("email", email);
            Campo("phone_number", telefono);
            Campo("zip_code", cp);
            var lead = new JObject
            {
                ["id"] = id,
                ["created_time"] = fecha,
                ["ad_id"] = "1",
                ["form_id"] = FORMULARIO,
                ["platform"] = plataforma,
                ["field_data"] = campos
            };
            if (consentimiento.HasValue)
            {
                lead["custom_disclaimer_responses"] = new JArray(
                    new JObject { ["checkbox_key"] = "acepto_la_política_de_privacidad", ["is_checked"] = "1" },
                    new JObject { ["checkbox_key"] = "deseo_recibir_por_correo_electrónico_novedades", ["is_checked"] = consentimiento.Value ? "1" : "" });
            }
            return lead.ToString();
        }

        private static string Pagina(string siguiente, params string[] leads) =>
            "{\"data\":[" + string.Join(",", leads) + "]" + (siguiente == null ? "" : ",\"paging\":{\"next\":\"" + siguiente + "\"}") + "}";

        // ---------- Lector de la respuesta de Graph (mapeo de campos) ----------

        [TestMethod]
        public void LectorGraph_MapeaLosCamposDelLead()
        {
            List<LeadMeta> leads = LectorGraphLeadsMeta.Leer(new[] { JObject.Parse(Lead("1000000000000001", consentimiento: true)) });

            Assert.AreEqual(1, leads.Count);
            LeadMeta lead = leads[0];
            Assert.AreEqual("1000000000000001", lead.Id);
            Assert.AreEqual("ig", lead.Plataforma);
            Assert.AreEqual("Ana Prueba Inventada", lead.Nombre);
            Assert.AreEqual("ana@ejemplo.com", lead.Email);
            Assert.AreEqual("600111222", lead.Telefono);
            Assert.AreEqual("28001", lead.CodigoPostal);
            Assert.AreEqual("tengo un centro de estética", lead.SituacionProfesional);
            Assert.AreEqual("una demostración de harmonia", lead.Solicita);
            // 09:30 UTC (+0000, como lo da Graph) = 10:30 en Madrid (horario de invierno).
            Assert.AreEqual(new DateTime(2026, 11, 9, 10, 30, 0), lead.FechaCreacion);
            Assert.IsTrue(lead.AceptaComunicaciones);
        }

        [TestMethod]
        public void LectorGraph_CasillaDeComunicacionesSinMarcar_NoAcepta()
        {
            // La de privacidad (obligatoria) va marcada: no cuenta como consentimiento.
            LeadMeta lead = LectorGraphLeadsMeta.Leer(new[] { JObject.Parse(Lead("1", consentimiento: false)) }).Single();

            Assert.IsFalse(lead.AceptaComunicaciones);
        }

        [TestMethod]
        public void LectorGraph_SinCasillas_NoAcepta()
        {
            LeadMeta lead = LectorGraphLeadsMeta.Leer(new[] { JObject.Parse(Lead("1", consentimiento: null)) }).Single();

            Assert.IsFalse(lead.AceptaComunicaciones);
        }

        [TestMethod]
        public void LectorGraph_ConsentimientoComoPreguntaDelFormulario_Acepta()
        {
            JObject lead = JObject.Parse(Lead("1"));
            ((JArray)lead["field_data"]).Add(new JObject
            {
                ["name"] = "deseo_recibir_por_correo_electrónico_novedades,_formación",
                ["values"] = new JArray("sí")
            });

            Assert.IsTrue(LectorGraphLeadsMeta.Leer(new[] { lead }).Single().AceptaComunicaciones);
        }

        [TestMethod]
        public void LectorGraph_NombreYApellidosSeparados_LosJunta()
        {
            JObject lead = JObject.Parse(Lead("1", nombre: null));
            ((JArray)lead["field_data"]).Add(new JObject { ["name"] = "first_name", ["values"] = new JArray("Ana") });
            ((JArray)lead["field_data"]).Add(new JObject { ["name"] = "last_name", ["values"] = new JArray("Prueba") });

            Assert.AreEqual("Ana Prueba", LectorGraphLeadsMeta.Leer(new[] { lead }).Single().Nombre);
        }

        [TestMethod]
        public void LectorGraph_PostCodeYTelefonoSinPrefijo_SeNormalizan()
        {
            JObject lead = JObject.Parse(Lead("1", cp: null, telefono: "0034 600 111 222"));
            ((JArray)lead["field_data"]).Add(new JObject { ["name"] = "post_code", ["values"] = new JArray("3330") });

            LeadMeta leido = LectorGraphLeadsMeta.Leer(new[] { lead }).Single();

            Assert.AreEqual("03330", leido.CodigoPostal);
            Assert.AreEqual("600111222", leido.Telefono);
        }

        [TestMethod]
        public void LectorGraph_TablaDeCampos_CubreLosEstandarDeMeta()
        {
            foreach (string campo in new[] { "full_name", "first_name", "last_name", "email", "phone_number", "zip_code", "post_code", "city" })
            {
                Assert.IsTrue(LectorGraphLeadsMeta.CAMPOS.ContainsKey(campo), campo);
            }
        }

        [TestMethod]
        public void LectorGraph_FechaUtc_DevuelveLaUtcDelLead()
        {
            Assert.AreEqual(new DateTime(2026, 11, 9, 9, 30, 0, DateTimeKind.Utc), LectorGraphLeadsMeta.FechaUtc("2026-11-09T09:30:00+0000"));
        }

        // ---------- Interruptor ----------

        [TestMethod]
        public async Task Job_ApagadoONoConfigurado_NoHaceNada()
        {
            configuracion.Activo = false;

            ResultadoImportacionLeadsMetaGraph resultado = await Crear().EjecutarJob();

            Assert.IsNull(resultado);
            A.CallTo(() => http.Get(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => formularios.LeerFormularios()).MustNotHaveHappened();
            Assert.AreEqual(0, elmah.Count);
        }

        [TestMethod]
        public async Task Job_EncendidoSinToken_AvisaUnaVezAlDiaYNoLlamaAMeta()
        {
            configuracion.Token = null;

            _ = await Crear().EjecutarJob();
            _ = await Crear().EjecutarJob();

            A.CallTo(() => http.Get(A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(1, elmah.Count);
            StringAssert.Contains(elmah[0].Message, "MetaLeads:Token");
        }

        // ---------- Petición: desde cuándo, paginación ----------

        [TestMethod]
        public async Task Importar_PideLosLeadsDesdeLaUltimaLecturaMenosElMargen()
        {
            Responder(Pagina(null));

            _ = await Crear().Importar(false, "LeadsMeta");

            // Leídos hasta las 09:00 UTC, menos 2 h de margen = 07:00 UTC.
            long desde = new DateTimeOffset(new DateTime(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
            Assert.AreEqual(1, urls.Count);
            StringAssert.StartsWith(urls[0], "https://graph.facebook.com/v21.0/" + FORMULARIO + "/leads?");
            StringAssert.Contains(Uri.UnescapeDataString(urls[0]), "\"field\":\"time_created\",\"operator\":\"GREATER_THAN\",\"value\":" + desde);
            StringAssert.Contains(urls[0], "custom_disclaimer_responses");
            StringAssert.Contains(urls[0], "limit=100");
        }

        [TestMethod]
        public async Task Importar_SinLecturaPrevia_EmpiezaEnElInicioDeLaCampanaPeroComoMucho30DiasAtras()
        {
            _ = A.CallTo(() => formularios.LeerFormularios()).Returns(Task.FromResult(new List<FormularioMetaOrigen>
            {
                new FormularioMetaOrigen { Origen = ORIGEN, MetaFormId = FORMULARIO, Activo = true, EsLead = true, FechaDesde = new DateTime(2026, 9, 1) }
            }));
            Responder(Pagina(null));

            _ = await Crear().Importar(false, "LeadsMeta");

            long desde = new DateTimeOffset(AHORA_UTC.AddDays(-ReglasLeadsCampana.DIAS_MAXIMOS_LEAD)).ToUnixTimeSeconds();
            StringAssert.Contains(Uri.UnescapeDataString(urls[0]), "\"value\":" + desde);
        }

        [TestMethod]
        public async Task Importar_ConDesdeExplicito_LoRespeta()
        {
            Responder(Pagina(null));

            _ = await Crear().Importar(true, "carlos", desdeUtc: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

            long desde = new DateTimeOffset(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
            StringAssert.Contains(Uri.UnescapeDataString(urls[0]), "\"value\":" + desde);
        }

        [TestMethod]
        public async Task Importar_SiguieLasPaginasDeMeta()
        {
            Responder(Pagina("https://graph.facebook.com/v21.0/" + FORMULARIO + "/leads?after=XYZ", Lead("1", telefono: "+34600000001", email: "a@ejemplo.com")),
                Pagina(null, Lead("2", telefono: "+34600000002", email: "b@ejemplo.com")));

            ResultadoImportacionLeadsMetaGraph resultado = await Crear().Importar(false, "LeadsMeta");

            Assert.AreEqual(2, urls.Count);
            Assert.AreEqual("https://graph.facebook.com/v21.0/" + FORMULARIO + "/leads?after=XYZ", urls[1]);
            Assert.AreEqual(2, resultado.Resultado.Leidos);
            Assert.AreEqual(2, resultado.Resultado.Creados);
            A.CallTo(() => alta.Crear(A<ClienteCrear>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task Importar_ElTokenVaEnLaCabeceraNoEnLaUrl()
        {
            Responder(Pagina(null));

            _ = await Crear().Importar(false, "LeadsMeta");

            Assert.IsFalse(urls[0].Contains("token"));
            // El del usuario del sistema solo pide el de la página; los leads, con el de la página.
            A.CallTo(() => http.Get("https://graph.facebook.com/v21.0/9001?fields=access_token", "token-de-prueba")).MustHaveHappenedOnceExactly();
            A.CallTo(() => http.Get(A<string>.That.Contains("/leads?"), "token-pagina")).MustHaveHappenedOnceExactly();
            A.CallTo(() => http.Get(A<string>.That.Contains("/leads?"), "token-de-prueba")).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Importar_SinPaginaConfigurada_UsaElTokenDeLaPrimeraDeMeAccounts()
        {
            configuracion.PaginaId = null;
            _ = A.CallTo(() => http.Get(A<string>._, A<string>._)).ReturnsLazily((string url, string token) => Task.FromResult(new RespuestaGraphMeta
            {
                Estado = 200,
                Cuerpo = url.Contains("/me/accounts") ? "{\"data\":[{\"id\":\"9001\",\"access_token\":\"token-pagina\"}]}" : Pagina(null)
            }));

            _ = await Crear().Importar(false, "LeadsMeta");

            A.CallTo(() => http.Get(A<string>.That.Contains("/leads?"), "token-pagina")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Job_FormularioSinCasillaDeComunicaciones_ImportaSinConsentimientoYLoDiceUnaVez()
        {
            Responder(Pagina(null, Lead("1", consentimiento: null)), Pagina(null, Lead("1", consentimiento: null)));

            ResultadoImportacionLeadsMetaGraph resultado = await Crear().EjecutarJob();
            _ = await Crear().EjecutarJob();

            Assert.IsFalse(resultado.Resultado.Leads.Single().AceptaComunicaciones);
            Assert.AreEqual(1, elmah.Count);
            StringAssert.Contains(elmah[0].Message, "custom_disclaimer_responses");
        }

        [TestMethod]
        public void LectorGraph_LeadsRealesDeHarmonia_CamposComoLosDaMeta()
        {
            // Los nombres de field_data que da el formulario de Harmonia (comprobado el 09/11/26); datos inventados.
            JObject lead = JObject.Parse("{\"id\":\"1003871096067209\",\"created_time\":\"2026-11-04T18:12:05+0000\",\"field_data\":["
                + "{\"name\":\"¿cuál_es_tu_situación_profesional?_\",\"values\":[\"trabajo_en_un_centro\"]},"
                + "{\"name\":\"¿qué_te_gustaría_solicitar?\",\"values\":[\"más_información\"]},"
                + "{\"name\":\"full_name\",\"values\":[\"Eva Inventada\"]},{\"name\":\"email\",\"values\":[\"eva@ejemplo.com\"]},"
                + "{\"name\":\"phone_number\",\"values\":[\"+34611222333\"]},{\"name\":\"post_code\",\"values\":[\"28100\"]}]}");

            LeadMeta leido = LectorGraphLeadsMeta.Leer(new[] { lead }).Single();

            Assert.AreEqual("1003871096067209", leido.Id);
            Assert.AreEqual("Eva Inventada", leido.Nombre);
            Assert.AreEqual("611222333", leido.Telefono);
            Assert.AreEqual("28100", leido.CodigoPostal);
            Assert.AreEqual("trabajo en un centro", leido.SituacionProfesional);
            Assert.AreEqual("más información", leido.Solicita);
            Assert.AreEqual(new DateTime(2026, 11, 4, 19, 12, 5), leido.FechaCreacion);
            Assert.IsFalse(leido.AceptaComunicaciones);
        }

        // ---------- Mismas reglas que el fichero ----------

        [TestMethod]
        public async Task Importar_CreaElClienteConElOrigenYLaFechaDelLead()
        {
            Responder(Pagina(null, Lead("1000000000000001", consentimiento: true)));

            ResultadoImportacionLeadsMetaGraph resultado = await Crear().Importar(false, "LeadsMeta");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREADO, resultado.Resultado.Leads.Single().Codigo);
            A.CallTo(() => alta.Crear(A<ClienteCrear>.That.Matches(c => c.Estado == Constantes.Clientes.Estados.PRIMERA_VISITA
                && c.VendedorEstetica == "JE" && c.AplicacionAlta == ImportadorLeadsMeta.APLICACION_ALTA))).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.PonerOrigenLead("1", "90001", ORIGEN, new DateTime(2026, 11, 9, 10, 30, 0), "1000000000000001", true, "LeadsMeta"))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => alta.PublicarConOrigen("1", "90001", ORIGEN, new DateTime(2026, 11, 9, 10, 30, 0), true, "LeadsMeta")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.QuitarBoletin(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Importar_SinConsentimiento_QuitaElBoletin()
        {
            Responder(Pagina(null, Lead("1", consentimiento: false)));

            _ = await Crear().Importar(false, "LeadsMeta");

            A.CallTo(() => repositorio.QuitarBoletin("1", "90001")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Importar_LeadYaImportado_NoCreaNadaYAvanzaLaLectura()
        {
            _ = A.CallTo(() => repositorio.ClienteDelLead("1", "1000000000000001")).Returns(Task.FromResult("42113"));
            Responder(Pagina(null, Lead("1000000000000001")));

            ResultadoImportacionLeadsMetaGraph resultado = await Crear().Importar(false, "LeadsMeta");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_YA_EXISTIA, resultado.Resultado.Leads.Single().Codigo);
            A.CallTo(() => alta.Crear(A<ClienteCrear>._)).MustNotHaveHappened();
            A.CallTo(() => formularios.GuardarLeidoHasta(ORIGEN, AHORA_UTC)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Importar_Ensayo_NoEscribeNiAvanzaLaLectura()
        {
            Responder(Pagina(null, Lead("1")));

            ResultadoImportacionLeadsMetaGraph resultado = await Crear().Importar(true, "carlos");

            Assert.IsTrue(resultado.Resultado.Ensayo);
            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREAR, resultado.Resultado.Leads.Single().Codigo);
            A.CallTo(() => alta.Crear(A<ClienteCrear>._)).MustNotHaveHappened();
            A.CallTo(() => formularios.GuardarLeidoHasta(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Importar_LeadQueFalla_NoAvanzaLaLecturaMasAllaDeEl()
        {
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>._)).Throws(new InvalidOperationException("BD caída"));
            Responder(Pagina(null, Lead("1", fecha: "2026-11-09T09:30:00+0000")));

            _ = await Crear().Importar(false, "LeadsMeta");

            A.CallTo(() => formularios.GuardarLeidoHasta(ORIGEN, new DateTime(2026, 11, 9, 9, 29, 59, DateTimeKind.Utc))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Job_LeadQueFallaCadaCuartoDeHora_VaAElmahUnaVezAlDia()
        {
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>._)).Throws(new InvalidOperationException("BD caída"));
            Responder(Pagina(null, Lead("1")), Pagina(null, Lead("1")));

            _ = await Crear().EjecutarJob();
            _ = await Crear().EjecutarJob();

            Assert.AreEqual(1, elmah.Count);
            StringAssert.Contains(elmah[0].Message, "1");
        }

        [TestMethod]
        public async Task Job_LeadSinDatos_VaAElmahConSoloElIdYElMotivo()
        {
            Responder(Pagina(null, Lead("777", telefono: null, email: null)));

            _ = await Crear().EjecutarJob();

            Assert.AreEqual(1, elmah.Count);
            StringAssert.Contains(elmah[0].Message, "777");
            StringAssert.Contains(elmah[0].Message, "No tiene teléfono ni correo.");
            Assert.IsFalse(elmah[0].Message.Contains("Ana"));
        }

        [TestMethod]
        public async Task Importar_SoloOrigenesActivosYDeCampana()
        {
            _ = A.CallTo(() => formularios.LeerFormularios()).Returns(Task.FromResult(new List<FormularioMetaOrigen>
            {
                new FormularioMetaOrigen { Origen = "VIEJO", MetaFormId = "1", Activo = false, EsLead = true },
                new FormularioMetaOrigen { Origen = "CADUCADO", MetaFormId = "2", Activo = true, EsLead = true, FechaHasta = new DateTime(2026, 10, 1) },
                new FormularioMetaOrigen { Origen = "WEB", MetaFormId = "3", Activo = true, EsLead = false },
            }));

            ResultadoImportacionLeadsMetaGraph resultado = await Crear().Importar(false, "LeadsMeta");

            A.CallTo(() => http.Get(A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, resultado.Formularios);
        }

        // ---------- Errores de Meta ----------

        [TestMethod]
        public void ErrorGraph_190EsDeToken()
        {
            ErrorGraphMetaException error = ErrorGraphMetaException.Desde(400,
                "{\"error\":{\"message\":\"Error validating access token: Session has expired\",\"type\":\"OAuthException\",\"code\":190,\"error_subcode\":463}}");

            Assert.AreEqual(TipoErrorGraphMeta.Token, error.Tipo);
            Assert.AreEqual(190, error.Codigo);
        }

        [TestMethod]
        public void ErrorGraph_PermisosYLimites()
        {
            Assert.AreEqual(TipoErrorGraphMeta.Permiso, ErrorGraphMetaException.Desde(403, "{\"error\":{\"message\":\"x\",\"code\":200}}").Tipo);
            Assert.AreEqual(TipoErrorGraphMeta.Permiso, ErrorGraphMetaException.Desde(400, "{\"error\":{\"message\":\"x\",\"code\":10}}").Tipo);
            Assert.AreEqual(TipoErrorGraphMeta.Limite, ErrorGraphMetaException.Desde(400, "{\"error\":{\"message\":\"x\",\"code\":17}}").Tipo);
            Assert.AreEqual(TipoErrorGraphMeta.Otro, ErrorGraphMetaException.Desde(500, "<html>").Tipo);
        }

        [TestMethod]
        public async Task Job_TokenCaducado_ElmahYCampanaAInformaticaYDireccionUnaVezAlDia()
        {
            _ = A.CallTo(() => http.Get(A<string>._, A<string>._)).Returns(Task.FromResult(new RespuestaGraphMeta
            {
                Estado = 400,
                Cuerpo = "{\"error\":{\"message\":\"Error validating access token\",\"type\":\"OAuthException\",\"code\":190}}"
            }));

            _ = await Crear().EjecutarJob();
            _ = await Crear().EjecutarJob();
            ResultadoImportacionLeadsMetaGraph tercero = await Crear().EjecutarJob();

            Assert.AreEqual(1, elmah.Count);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(@"NUEVAVISION\carlos", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(@"NUEVAVISION\direccion1", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => formularios.GuardarLeidoHasta(A<string>._, A<DateTime>._)).MustNotHaveHappened();
            Assert.AreEqual(1, tercero.Errores.Count);
            Assert.IsFalse(elmah[0].Message.Contains("token-de-prueba"));
        }

        [TestMethod]
        public async Task Job_TokenCaducado_AlDiaSiguienteVuelveAAvisar()
        {
            _ = A.CallTo(() => http.Get(A<string>._, A<string>._)).Returns(Task.FromResult(new RespuestaGraphMeta
            {
                Estado = 400, Cuerpo = "{\"error\":{\"message\":\"Error validating access token\",\"code\":190}}"
            }));
            DateTime ahora = AHORA_UTC;
            var importador = new ImportadorLeadsMetaGraph(http, formularios, repositorio, alta, configuracion, avisos, () => notificaciones,
                g => new List<string> { "carlos" }, ex => elmah.Add(ex), () => ahora);

            _ = await importador.EjecutarJob();
            ahora = AHORA_UTC.AddDays(1);
            _ = await importador.EjecutarJob();

            Assert.AreEqual(2, elmah.Count);
        }

        [TestMethod]
        public async Task Job_OtroErrorDeMeta_SoloElmahUnaVezAlDiaSinCampana()
        {
            _ = A.CallTo(() => http.Get(A<string>._, A<string>._)).Returns(Task.FromResult(new RespuestaGraphMeta
            {
                Estado = 500, Cuerpo = "{\"error\":{\"message\":\"An unexpected error has occurred\",\"code\":2}}"
            }));

            _ = await Crear().EjecutarJob();
            _ = await Crear().EjecutarJob();

            Assert.AreEqual(1, elmah.Count);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        // ---------- Formularios de la página ----------

        [TestMethod]
        public async Task Formularios_LeeLasPaginasDelTokenYSusFormulariosConElTokenDeCadaPagina()
        {
            _ = A.CallTo(() => http.Get(A<string>.That.Contains("/me/accounts"), "token-de-prueba")).Returns(Task.FromResult(new RespuestaGraphMeta
            {
                Estado = 200,
                Cuerpo = "{\"data\":[{\"id\":\"9001\",\"name\":\"Nueva Visión\",\"access_token\":\"token-pagina\"}]}"
            }));
            _ = A.CallTo(() => http.Get(A<string>.That.Contains("/9001/leadgen_forms"), "token-pagina")).Returns(Task.FromResult(new RespuestaGraphMeta
            {
                Estado = 200,
                Cuerpo = "{\"data\":[{\"id\":\"" + FORMULARIO + "\",\"name\":\"Harmonia sept 2026\",\"status\":\"ACTIVE\",\"leads_count\":12}]}"
            }));

            List<FormularioMetaDTO> lista = await Crear().LeerFormulariosDeMeta();

            Assert.AreEqual(1, lista.Count);
            Assert.AreEqual(FORMULARIO, lista[0].Id);
            Assert.AreEqual("Harmonia sept 2026", lista[0].Nombre);
            Assert.AreEqual("ACTIVE", lista[0].Estado);
            Assert.AreEqual("9001", lista[0].PaginaId);
            Assert.AreEqual("Nueva Visión", lista[0].Pagina);
            Assert.AreEqual(12, lista[0].Leads);
            Assert.AreEqual(ORIGEN, lista[0].Origen);
        }

        // ---------- Endpoints ----------

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private ClientesController Controlador(string grupo)
        {
            var controlador = new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>())
            {
                ImportadorLeadsGraph = Crear()
            };
            controlador.User = Usuario("Carlos", grupo);
            return controlador;
        }

        [TestMethod]
        public async Task Endpoint_Vendedor_403()
        {
            var respuesta = await Controlador(Constantes.GruposSeguridad.VENDEDORES).PostImportarLeadsMeta() as NegotiatedContentResult<string>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            A.CallTo(() => http.Get(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Endpoint_SinToken_400()
        {
            configuracion.Token = "";

            var respuesta = await Controlador(Constantes.GruposSeguridad.DIRECCION).PostImportarLeadsMeta() as BadRequestErrorMessageResult;

            Assert.IsNotNull(respuesta);
            StringAssert.Contains(respuesta.Message, ConfiguracionMetaLeads.CLAVE_TOKEN);
        }

        [TestMethod]
        public async Task Endpoint_EnsayoConElJobApagado_DevuelveLoQueHaria()
        {
            configuracion.Activo = false;
            Responder(Pagina(null, Lead("1")));

            var respuesta = await Controlador(Constantes.GruposSeguridad.INFORMATICA).PostImportarLeadsMeta(ensayo: true)
                as OkNegotiatedContentResult<ResultadoImportacionLeadsDTO>;

            Assert.IsNotNull(respuesta);
            Assert.IsTrue(respuesta.Content.Ensayo);
            Assert.AreEqual(1, respuesta.Content.Creados);
            Assert.AreEqual(ORIGEN, respuesta.Content.Origen);
            A.CallTo(() => alta.Crear(A<ClienteCrear>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Endpoint_TokenCaducado_502ConElMotivo()
        {
            _ = A.CallTo(() => http.Get(A<string>._, A<string>._)).Returns(Task.FromResult(new RespuestaGraphMeta
            {
                Estado = 400, Cuerpo = "{\"error\":{\"message\":\"Error validating access token\",\"code\":190}}"
            }));

            var respuesta = await Controlador(Constantes.GruposSeguridad.DIRECCION).PostImportarLeadsMeta() as NegotiatedContentResult<string>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(HttpStatusCode.BadGateway, respuesta.StatusCode);
            StringAssert.Contains(respuesta.Content, "190");
        }

        [TestMethod]
        public async Task Endpoint_SinFormularios_400()
        {
            _ = A.CallTo(() => formularios.LeerFormularios()).Returns(Task.FromResult(new List<FormularioMetaOrigen>()));

            var respuesta = await Controlador(Constantes.GruposSeguridad.DIRECCION).PostImportarLeadsMeta() as BadRequestErrorMessageResult;

            Assert.IsNotNull(respuesta);
            StringAssert.Contains(respuesta.Message, "MetaFormId");
        }
    }
}
