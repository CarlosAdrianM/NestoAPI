using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Origenes
{
    /// <summary>
    /// NestoAPI#623 corte 5c: configuración de la importación de leads por la API Graph. <c>MetaLeads:Activo</c> en Web.config (sin
    /// la clave = apagado; se cambia en el repo, que el despliegue lleva su Web.config) y <c>MetaLeads:Token</c> en secretos.config
    /// (token del usuario del sistema del Business Manager de Nueva Visión, app «NestoAPI Leads»).
    /// </summary>
    public class ConfiguracionMetaLeads
    {
        public const string CLAVE_ACTIVO = "MetaLeads:Activo";
        public const string CLAVE_TOKEN = "MetaLeads:Token";
        public const string CLAVE_VERSION = "MetaLeads:VersionGraph";
        /// <summary>Versión de la API Graph si no se dice otra en Web.config (Meta mantiene cada versión unos dos años).</summary>
        public const string VERSION_POR_DEFECTO = "v21.0";
        public const string CLAVE_PAGINA = "MetaLeads:PaginaId";

        public bool Activo { get; set; }
        public string Token { get; set; }
        public string Version { get; set; } = VERSION_POR_DEFECTO;
        /// <summary>La página de Facebook dueña de los formularios (no es secreto). Sin ella, la primera de /me/accounts.</summary>
        public string PaginaId { get; set; }

        public static ConfiguracionMetaLeads Leer()
        {
            string version = ConfigurationManager.AppSettings[CLAVE_VERSION];
            return new ConfiguracionMetaLeads
            {
                Activo = bool.TryParse(ConfigurationManager.AppSettings[CLAVE_ACTIVO], out bool activo) && activo,
                Token = ConfigurationManager.AppSettings[CLAVE_TOKEN]?.Trim(),
                Version = string.IsNullOrWhiteSpace(version) ? VERSION_POR_DEFECTO : version.Trim(),
                PaginaId = ConfigurationManager.AppSettings[CLAVE_PAGINA]?.Trim()
            };
        }
    }

    /// <summary>NestoAPI#623 corte 5c: un origen del catálogo con su formulario de Meta (columna OrigenesCliente.MetaFormId).</summary>
    public class FormularioMetaOrigen
    {
        public string Origen { get; set; }
        public string MetaFormId { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public bool Activo { get; set; }
        public bool EsLead { get; set; }
        /// <summary>UTC: hasta dónde se leyó en la última pasada buena (null = nunca).</summary>
        public DateTime? MetaLeadsLeidosHasta { get; set; }
    }

    /// <summary>NestoAPI#623 corte 5c: un formulario de una página de Meta (GET api/Clientes/Origenes/MetaFormularios).</summary>
    public class FormularioMetaDTO
    {
        public string PaginaId { get; set; }
        public string Pagina { get; set; }
        public string Id { get; set; }
        public string Nombre { get; set; }
        /// <summary>ACTIVE, ARCHIVED…</summary>
        public string Estado { get; set; }
        public int? Leads { get; set; }
        public DateTime? Creado { get; set; }
        /// <summary>El origen de Nesto que tiene este formulario en MetaFormId (null si ninguno).</summary>
        public string Origen { get; set; }
    }

    /// <summary>NestoAPI#623 corte 5c: el resultado de una pasada (todos los formularios).</summary>
    public class ResultadoImportacionLeadsMetaGraph
    {
        /// <summary>Lo mismo que devuelve POST api/Clientes/Origenes/ImportarLeads, con los leads de todos los formularios.</summary>
        public ResultadoImportacionLeadsDTO Resultado { get; set; }
        /// <summary>Cuántos formularios se han mirado (orígenes activos, de campaña y con MetaFormId).</summary>
        public int Formularios { get; set; }
        /// <summary>Los formularios que no se han podido leer, con el motivo.</summary>
        public List<string> Errores { get; set; } = new List<string>();
        /// <summary>El primer error de token (para que el endpoint lo diga claro).</summary>
        public TipoErrorGraphMeta? TipoError { get; set; }
    }

    /// <summary>NestoAPI#623 corte 5c: el formulario de cada origen y hasta dónde se ha leído.</summary>
    public interface IRepositorioFormulariosMeta
    {
        /// <summary>Todos los orígenes con MetaFormId (activos o no). Vacío si no está la columna (script Issue623c5).</summary>
        Task<List<FormularioMetaOrigen>> LeerFormularios();
        Task GuardarLeidoHasta(string origen, DateTime hastaUtc);
    }

    public class RepositorioFormulariosMetaSql : IRepositorioFormulariosMeta
    {
        internal const string SQL_FORMULARIOS = @"
IF COL_LENGTH(N'dbo.OrigenesCliente', N'MetaFormId') IS NOT NULL AND COL_LENGTH(N'dbo.OrigenesCliente', N'MetaLeadsLeidosHasta') IS NOT NULL
    EXEC sp_executesql N'SELECT RTRIM(Codigo) AS Origen, LTRIM(RTRIM(MetaFormId)) AS MetaFormId, CAST(FechaDesde AS datetime) AS FechaDesde,
        CAST(FechaHasta AS datetime) AS FechaHasta, Activo, EsLead, MetaLeadsLeidosHasta
        FROM dbo.OrigenesCliente WITH (NOLOCK) WHERE MetaFormId IS NOT NULL AND LTRIM(RTRIM(MetaFormId)) <> N''''';
ELSE
    SELECT TOP 0 CAST(NULL AS varchar(30)) AS Origen, CAST(NULL AS varchar(30)) AS MetaFormId, CAST(NULL AS datetime) AS FechaDesde,
        CAST(NULL AS datetime) AS FechaHasta, CAST(0 AS bit) AS Activo, CAST(0 AS bit) AS EsLead, CAST(NULL AS datetime) AS MetaLeadsLeidosHasta;";

        internal const string SQL_LEIDO_HASTA = @"
IF COL_LENGTH(N'dbo.OrigenesCliente', N'MetaLeadsLeidosHasta') IS NOT NULL
    EXEC sp_executesql N'UPDATE dbo.OrigenesCliente SET MetaLeadsLeidosHasta = @hasta WHERE Codigo = @origen',
        N'@origen varchar(30), @hasta datetime', @origen, @hasta;";

        public async Task<List<FormularioMetaOrigen>> LeerFormularios()
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return await db.Database.SqlQuery<FormularioMetaOrigen>(SQL_FORMULARIOS).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task GuardarLeidoHasta(string origen, DateTime hastaUtc)
        {
            using (var db = new NVEntities())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(TransactionalBehavior.DoNotEnsureTransaction, SQL_LEIDO_HASTA,
                    new SqlParameter("@origen", SqlDbType.VarChar, 30) { Value = origen },
                    new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hastaUtc }).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// NestoAPI#623 corte 5c: cada clase de aviso, una vez al día. En memoria (al reciclar el pool puede repetirse uno: mejor eso que
    /// perderlo). El job corre cada 15 minutos y un token caducado no puede llenar ELMAH ni la campana.
    /// </summary>
    public class AvisosUnaVezAlDia
    {
        internal static readonly AvisosUnaVezAlDia Compartido = new AvisosUnaVezAlDia();

        private readonly object candado = new object();
        private readonly Dictionary<string, DateTime> avisados = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>true si hoy no se había avisado de esta clave (y la apunta).</summary>
        public bool Primera(string clave, DateTime hoy)
        {
            lock (candado)
            {
                if (avisados.TryGetValue(clave ?? string.Empty, out DateTime dia) && dia == hoy.Date)
                {
                    return false;
                }
                avisados[clave ?? string.Empty] = hoy.Date;
                // Que no crezca sin fin con claves de días pasados.
                foreach (string vieja in avisados.Where(a => a.Value < hoy.Date.AddDays(-2)).Select(a => a.Key).ToList())
                {
                    _ = avisados.Remove(vieja);
                }
                return true;
            }
        }
    }

    /// <summary>
    /// NestoAPI#623 corte 5c (Carlos 09/11/26: «API de Meta + tarea»): trae los leads de los formularios de Meta por la API Graph y los
    /// pasa al MISMO importador que el fichero (<see cref="ImportadorLeadsMeta"/>: cliente en estado 5, vendedor por CP, origen con la
    /// fecha del lead, consentimiento, sin repetir por LeadExternoId, a Odoo, y luego el control de leads sin llamar).
    /// Núcleo común + dos fuentes: el fichero (<see cref="LectorLeadsMeta"/>) y Graph (<see cref="LectorGraphLeadsMeta"/>).
    ///
    /// Cada origen activo, de campaña y con <c>MetaFormId</c> se lee desde su <c>MetaLeadsLeidosHasta</c> menos
    /// <see cref="MARGEN"/> (sin lectura previa: desde el inicio de la campaña, como mucho <see cref="ReglasLeadsCampana.DIAS_MAXIMOS_LEAD"/>
    /// días atrás). Lo ya importado no se repite (LeadExternoId), así que releer es inocuo. Si un lead falla por una excepción, la
    /// lectura no pasa de él: se reintenta en la pasada siguiente.
    /// </summary>
    public class ImportadorLeadsMetaGraph
    {
        public const string URL_GRAPH = "https://graph.facebook.com";
        public const string CAMPOS_LEAD = "id,created_time,field_data,ad_id,form_id,platform,custom_disclaimer_responses";
        public const int LIMITE_POR_PAGINA = 100;
        public const int MAXIMO_PAGINAS = 100;
        public static readonly TimeSpan MARGEN = TimeSpan.FromHours(2);
        public const string USUARIO_JOB = ImportadorLeadsMeta.APLICACION_ALTA;
        public const string TIPO_NOTIFICACION = "MetaLeadsToken";

        private readonly IClienteGraphMeta http;
        private readonly IRepositorioFormulariosMeta formularios;
        private readonly IRepositorioLeadsMeta repositorio;
        private readonly IAltaClientesLeads alta;
        private readonly ConfiguracionMetaLeads configuracion;
        private readonly AvisosUnaVezAlDia avisos;
        private readonly Func<IServicioNotificacionesPush> notificaciones;
        private readonly Func<string, List<string>> miembros;
        private readonly Action<Exception> elmah;
        private readonly Func<DateTime> ahoraUtc;

        public ImportadorLeadsMetaGraph(IClienteGraphMeta http, IRepositorioFormulariosMeta formularios, IRepositorioLeadsMeta repositorio,
            IAltaClientesLeads alta, ConfiguracionMetaLeads configuracion, AvisosUnaVezAlDia avisos = null,
            Func<IServicioNotificacionesPush> notificaciones = null, Func<string, List<string>> miembros = null,
            Action<Exception> elmah = null, Func<DateTime> ahoraUtc = null)
        {
            this.http = http;
            this.formularios = formularios;
            this.repositorio = repositorio;
            this.alta = alta;
            this.configuracion = configuracion ?? new ConfiguracionMetaLeads();
            this.avisos = avisos ?? AvisosUnaVezAlDia.Compartido;
            this.notificaciones = notificaciones ?? (() => new ServicioNotificacionesPush());
            this.miembros = miembros ?? MiembrosGrupoDominio.Leer;
            this.elmah = elmah ?? (ex => ElmahHelper.Log(ex, USUARIO_JOB));
            this.ahoraUtc = ahoraUtc ?? (() => DateTime.UtcNow);
        }

        /// <summary>Con las piezas de producción (GestorClientes para el alta, como el job de sincronización).</summary>
        public static ImportadorLeadsMetaGraph DeProduccion(IAltaClientesLeads alta = null)
        {
            if (alta == null)
            {
                var gestorClientes = new GestorClientes(new ServicioGestorClientes(), new ServicioAgencias(),
                    new SincronizacionEventWrapper(new GooglePubSubEventPublisher()));
                alta = new AltaClientesLeadsGestor(gestorClientes);
            }
            return new ImportadorLeadsMetaGraph(new ClienteGraphMeta(), new RepositorioFormulariosMetaSql(), new RepositorioLeadsMetaSql(), alta,
                ConfiguracionMetaLeads.Leer());
        }

        public ConfiguracionMetaLeads Configuracion => configuracion;

        /// <summary>
        /// La pasada del job: apagado = nada (ni BD ni Meta). Encendido sin token = aviso (una vez al día) y nada más. Nunca lanza.
        /// </summary>
        public async Task<ResultadoImportacionLeadsMetaGraph> EjecutarJob()
        {
            if (!configuracion.Activo)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(configuracion.Token))
            {
                if (avisos.Primera("sin-token", ahoraUtc()))
                {
                    elmah(new Exception($"[Leads Meta #623] {ConfiguracionMetaLeads.CLAVE_ACTIVO} está encendido pero falta {ConfiguracionMetaLeads.CLAVE_TOKEN} en secretos.config: no se leen los leads."));
                }
                return null;
            }
            try
            {
                return await Importar(false, USUARIO_JOB).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (avisos.Primera("job:" + ex.GetType().Name, ahoraUtc()))
                {
                    elmah(new Exception($"[Leads Meta #623] La pasada ha fallado: {ex.Message}", ex));
                }
                return null;
            }
        }

        /// <summary>
        /// Lee los leads de cada formulario y los importa. En ensayo no escribe nada (ni la marca de lectura). <paramref name="soloOrigen"/>
        /// limita a un origen; <paramref name="desdeUtc"/> sustituye a la marca (para diagnosticar).
        /// </summary>
        public async Task<ResultadoImportacionLeadsMetaGraph> Importar(bool ensayo, string usuario, string soloOrigen = null, DateTime? desdeUtc = null)
        {
            DateTime inicio = ahoraUtc();
            List<FormularioMetaOrigen> todos = await formularios.LeerFormularios().ConfigureAwait(false) ?? new List<FormularioMetaOrigen>();
            List<FormularioMetaOrigen> aLeer = todos
                .Where(f => f != null && f.EsLead && !string.IsNullOrWhiteSpace(f.MetaFormId) && !string.IsNullOrWhiteSpace(f.Origen)
                    && ReglasLeadsCampana.EstaActivo(new OrigenCliente { Activo = f.Activo, FechaHasta = f.FechaHasta }, HoraEspana(inicio))
                    && (soloOrigen == null || string.Equals(f.Origen.Trim(), soloOrigen.Trim(), StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var resultado = new ResultadoImportacionLeadsMetaGraph
            {
                Formularios = aLeer.Count,
                Resultado = new ResultadoImportacionLeadsDTO
                {
                    Ensayo = ensayo,
                    Origen = string.Join(", ", aLeer.Select(f => f.Origen.Trim()))
                }
            };
            int desplazamientoFila = 0;
            string tokenPagina = null;
            foreach (FormularioMetaOrigen formulario in aLeer)
            {
                string origen = formulario.Origen.Trim().ToUpperInvariant();
                List<JObject> crudos;
                try
                {
                    tokenPagina = tokenPagina ?? await TokenDePagina().ConfigureAwait(false);
                    crudos = await LeerLeads(formulario.MetaFormId.Trim(), desdeUtc ?? Desde(formulario, inicio), tokenPagina).ConfigureAwait(false);
                }
                catch (ErrorGraphMetaException ex)
                {
                    resultado.Errores.Add($"{origen}: {ex.Message}");
                    resultado.TipoError = resultado.TipoError ?? ex.Tipo;
                    await AvisarError(origen, formulario.MetaFormId.Trim(), ex).ConfigureAwait(false);
                    continue;
                }

                if (!ensayo && crudos.Any() && !crudos.Any(LectorGraphLeadsMeta.TraeConsentimiento)
                    && avisos.Primera("sin-consentimiento:" + formulario.MetaFormId.Trim(), inicio))
                {
                    elmah(new Exception($"[Leads Meta #623] Los leads del formulario {formulario.MetaFormId.Trim()} ({origen}) no traen la casilla de comunicaciones comerciales (custom_disclaimer_responses): se importan sin aceptar comunicaciones."));
                }

                var fallidos = new List<string>();
                var importador = new ImportadorLeadsMeta(repositorio, alta, ex => RegistrarErrorLead(ex), () => HoraEspana(ahoraUtc()));
                List<LeadMeta> leads = LectorGraphLeadsMeta.Leer(crudos);
                ResultadoImportacionLeadsDTO parcial = await importador.Importar(leads, origen, ensayo, usuario).ConfigureAwait(false);

                for (int i = 0; i < parcial.Leads.Count && i < leads.Count; i++)
                {
                    ResultadoLeadMetaDTO fila = parcial.Leads[i];
                    if (fila.Codigo != ImportadorLeadsMeta.CODIGO_ERROR)
                    {
                        continue;
                    }
                    string motivoInvalido = ImportadorLeadsMeta.MotivoInvalido(leads[i]);
                    if (motivoInvalido == null)
                    {
                        // Ha fallado por una excepción (ya en ELMAH): se reintenta.
                        fallidos.Add(leads[i].Id);
                    }
                    else if (!ensayo && avisos.Primera("invalido:" + leads[i].Id, inicio))
                    {
                        // Solo el id y el motivo: nada de nombre, teléfono ni correo.
                        elmah(new Exception($"[Leads Meta #623] El lead {leads[i].Id} del formulario {formulario.MetaFormId.Trim()} ({origen}) no se puede importar: {motivoInvalido}"));
                    }
                }

                foreach (ResultadoLeadMetaDTO fila in parcial.Leads)
                {
                    fila.Fila += desplazamientoFila;
                }
                desplazamientoFila += parcial.Leads.Count;
                resultado.Resultado.Leidos += parcial.Leidos;
                resultado.Resultado.Creados += parcial.Creados;
                resultado.Resultado.YaExistian += parcial.YaExistian;
                resultado.Resultado.Errores += parcial.Errores;
                resultado.Resultado.Leads.AddRange(parcial.Leads);

                if (!ensayo)
                {
                    DateTime hasta = inicio;
                    List<DateTime> fechasFallidos = crudos.Where(c => fallidos.Contains((string)c["id"]))
                        .Select(c => LectorGraphLeadsMeta.FechaUtc(c["created_time"]))
                        .Where(f => f.HasValue).Select(f => f.Value).ToList();
                    if (fechasFallidos.Any())
                    {
                        hasta = fechasFallidos.Min().AddSeconds(-1);
                    }
                    else if (fallidos.Any())
                    {
                        // Sin fecha del lead fallido: la lectura se queda donde estaba.
                        continue;
                    }
                    await formularios.GuardarLeidoHasta(origen, DateTime.SpecifyKind(hasta, DateTimeKind.Utc)).ConfigureAwait(false);
                }
            }
            return resultado;
        }

        /// <summary>Desde la última lectura menos el margen; sin lectura, el inicio de la campaña, como mucho 30 días atrás.</summary>
        internal static DateTime Desde(FormularioMetaOrigen formulario, DateTime ahoraUtc)
        {
            if (formulario.MetaLeadsLeidosHasta.HasValue)
            {
                return DateTime.SpecifyKind(formulario.MetaLeadsLeidosHasta.Value, DateTimeKind.Utc) - MARGEN;
            }
            DateTime tope = ahoraUtc.AddDays(-ReglasLeadsCampana.DIAS_MAXIMOS_LEAD);
            if (formulario.FechaDesde.HasValue)
            {
                DateTime inicioCampana = UtcDesdeEspana(formulario.FechaDesde.Value.Date);
                return inicioCampana > tope ? inicioCampana : tope;
            }
            return tope;
        }

        /// <summary>
        /// Los leads se leen con el token de la PÁGINA, no con el del usuario del sistema (comprobado el 09/11/26): GET
        /// /{pagina}?fields=access_token con el del sistema (MetaLeads:PaginaId) o, sin página configurada, la primera de /me/accounts.
        /// </summary>
        private async Task<string> TokenDePagina()
        {
            if (!string.IsNullOrWhiteSpace(configuracion.PaginaId))
            {
                JObject pagina = await GetJson($"{URL_GRAPH}/{configuracion.Version}/{Uri.EscapeDataString(configuracion.PaginaId.Trim())}?fields=access_token",
                    configuracion.Token).ConfigureAwait(false);
                string token = (string)pagina["access_token"];
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
                throw new ErrorGraphMetaException(TipoErrorGraphMeta.Permiso, 200, null, null,
                    $"El token de Meta no da el token de la página {configuracion.PaginaId.Trim()}: ¿está la página asignada al usuario del sistema?");
            }
            JObject paginas = await GetJson($"{URL_GRAPH}/{configuracion.Version}/me/accounts?fields=id,access_token&limit=100", configuracion.Token)
                .ConfigureAwait(false);
            string primera = (paginas["data"] as JArray)?.OfType<JObject>().Select(p => (string)p["access_token"])
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (primera == null)
            {
                throw new ErrorGraphMetaException(TipoErrorGraphMeta.Permiso, 200, null, null,
                    "El token de Meta no ve ninguna página: en el Business Manager, asigne la página al usuario del sistema.");
            }
            return primera;
        }

        internal string UrlLeads(string formulario, DateTime desdeUtc)
        {
            long unix = new DateTimeOffset(DateTime.SpecifyKind(desdeUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            string filtro = "[{\"field\":\"time_created\",\"operator\":\"GREATER_THAN\",\"value\":" + unix + "}]";
            return $"{URL_GRAPH}/{configuracion.Version}/{Uri.EscapeDataString(formulario)}/leads?fields={CAMPOS_LEAD}"
                + $"&filtering={Uri.EscapeDataString(filtro)}&limit={LIMITE_POR_PAGINA}";
        }

        private async Task<List<JObject>> LeerLeads(string formulario, DateTime desdeUtc, string tokenPagina)
        {
            var leads = new List<JObject>();
            string url = UrlLeads(formulario, desdeUtc);
            for (int pagina = 0; url != null && pagina < MAXIMO_PAGINAS; pagina++)
            {
                JObject respuesta = await GetJson(url, tokenPagina).ConfigureAwait(false);
                if (respuesta["data"] is JArray datos)
                {
                    leads.AddRange(datos.OfType<JObject>());
                }
                url = (string)respuesta["paging"]?["next"];
            }
            return leads;
        }

        private async Task<JObject> GetJson(string url, string token)
        {
            RespuestaGraphMeta respuesta;
            try
            {
                respuesta = await http.Get(url, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new ErrorGraphMetaException(TipoErrorGraphMeta.Otro, 0, null, null, "No se ha podido llamar a Meta: " + ex.Message, ex);
            }
            if (respuesta == null || respuesta.Estado < 200 || respuesta.Estado > 299)
            {
                throw ErrorGraphMetaException.Desde(respuesta?.Estado ?? 0, respuesta?.Cuerpo);
            }
            try
            {
                // Las fechas, como texto: Json.NET las pasaría a DateTime local.
                JObject json;
                using (var lector = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(respuesta.Cuerpo ?? "{}")) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                {
                    json = JObject.Load(lector);
                }
                if (json["error"] != null)
                {
                    throw ErrorGraphMetaException.Desde(respuesta.Estado, respuesta.Cuerpo);
                }
                return json;
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                throw new ErrorGraphMetaException(TipoErrorGraphMeta.Otro, respuesta.Estado, null, null, "Meta ha contestado algo que no es JSON.", ex);
            }
        }

        private void RegistrarErrorLead(Exception ex)
        {
            // El mensaje del importador lleva el id del lead (nunca datos personales) y el tipo de excepción: una vez al día cada uno.
            if (avisos.Primera("lead:" + ex.Message, ahoraUtc()))
            {
                elmah(ex);
            }
        }

        private async Task AvisarError(string origen, string formulario, ErrorGraphMetaException ex)
        {
            string clave = ex.Tipo == TipoErrorGraphMeta.Token ? "token" : $"graph:{formulario}:{ex.Tipo}:{ex.Codigo}";
            if (!avisos.Primera(clave, ahoraUtc()))
            {
                return;
            }
            string texto = ex.Tipo == TipoErrorGraphMeta.Token
                ? $"El token de Meta ({ConfiguracionMetaLeads.CLAVE_TOKEN}) no vale: {ex.Message}. Los leads de Meta NO están entrando en Nesto. " +
                  "Hay que generar otro en el Business Manager (usuario del sistema, app «NestoAPI Leads») y ponerlo en secretos.config."
                : $"No se han podido leer los leads del formulario {formulario} ({origen}): {ex.Message}";
            elmah(new Exception("[Leads Meta #623] " + texto, ex));
            if (ex.Tipo == TipoErrorGraphMeta.Token)
            {
                await AvisarCampana("Los leads de Meta no entran", texto).ConfigureAwait(false);
            }
        }

        private async Task AvisarCampana(string titulo, string texto)
        {
            var usuarios = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string grupo in new[] { Constantes.GruposSeguridad.INFORMATICA, Constantes.GruposSeguridad.DIRECCION })
            {
                try
                {
                    foreach (string u in (miembros(grupo) ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)))
                    {
                        _ = usuarios.Add(Agencias.RepartoPropio.AvisosFurgonetaSinDescargar.ConDominio(u));
                    }
                }
                catch (Exception ex)
                {
                    elmah(new Exception($"[Leads Meta #623] No se pudieron leer los miembros de {grupo}: {ex.Message}", ex));
                }
            }
            var notificacion = new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = texto,
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_NOTIFICACION }
            };
            IServicioNotificacionesPush servicio = notificaciones();
            foreach (string usuario in usuarios)
            {
                try
                {
                    await servicio.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    elmah(new Exception($"[Leads Meta #623] No se pudo avisar a {usuario}: {ex.Message}", ex));
                }
            }
        }

        /// <summary>
        /// Los formularios de las páginas que ve el token (<c>/me/accounts</c>; cada página con su propio token, que es el que pide
        /// <c>/{page-id}/leadgen_forms</c>), con el origen que los tiene asignados. Para averiguar el MetaFormId de cada origen.
        /// </summary>
        public async Task<List<FormularioMetaDTO>> LeerFormulariosDeMeta()
        {
            List<FormularioMetaOrigen> asignados = await formularios.LeerFormularios().ConfigureAwait(false) ?? new List<FormularioMetaOrigen>();
            var lista = new List<FormularioMetaDTO>();
            string url = $"{URL_GRAPH}/{configuracion.Version}/me/accounts?fields=id,name,access_token&limit=100";
            for (int p = 0; url != null && p < MAXIMO_PAGINAS; p++)
            {
                JObject paginas = await GetJson(url, configuracion.Token).ConfigureAwait(false);
                foreach (JObject pagina in (paginas["data"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    string paginaId = (string)pagina["id"];
                    string tokenPagina = string.IsNullOrWhiteSpace((string)pagina["access_token"]) ? configuracion.Token : (string)pagina["access_token"];
                    string urlFormularios = $"{URL_GRAPH}/{configuracion.Version}/{Uri.EscapeDataString(paginaId)}/leadgen_forms"
                        + "?fields=id,name,status,leads_count,created_time&limit=100";
                    for (int f = 0; urlFormularios != null && f < MAXIMO_PAGINAS; f++)
                    {
                        JObject formulariosPagina = await GetJson(urlFormularios, tokenPagina).ConfigureAwait(false);
                        foreach (JObject formulario in (formulariosPagina["data"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                        {
                            string id = (string)formulario["id"];
                            lista.Add(new FormularioMetaDTO
                            {
                                PaginaId = paginaId,
                                Pagina = (string)pagina["name"],
                                Id = id,
                                Nombre = (string)formulario["name"],
                                Estado = (string)formulario["status"],
                                Leads = (int?)formulario["leads_count"],
                                Creado = LectorGraphLeadsMeta.FechaUtc(formulario["created_time"]),
                                Origen = asignados.FirstOrDefault(a => string.Equals(a.MetaFormId?.Trim(), id, StringComparison.Ordinal))?.Origen?.Trim()
                            });
                        }
                        urlFormularios = (string)formulariosPagina["paging"]?["next"];
                    }
                }
                url = (string)paginas["paging"]?["next"];
            }
            return lista;
        }

        private static TimeZoneInfo ZonaEspana()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(LectorLeadsMeta.ZONA_ESPANA);
            }
            catch (Exception)
            {
                return TimeZoneInfo.Local;
            }
        }

        private static DateTime HoraEspana(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZonaEspana());

        private static DateTime UtcDesdeEspana(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), ZonaEspana());
    }

    /// <summary>
    /// NestoAPI#623 corte 5c: job <c>meta-leads</c>, cada 15 minutos de 8:00 a 22:45 (Carlos 09/11/26: nada de avisos por la noche;
    /// lo que entre de noche se recoge a las 8:00 con el margen de lectura). Solo hace algo con <c>MetaLeads:Activo</c> = true (Web.config del
    /// repo) y el token en secretos.config. Sin reintentos (la siguiente pasada es en 15 min) y sin solaparse.
    /// </summary>
    public static class LeadsMetaJobsService
    {
        public const string JOB_ID = "meta-leads";
        public const string CRON = "*/15 8-22 * * *";

        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        [AutomaticRetry(Attempts = 0)]
        public static async Task Importar()
        {
            if (!ConfiguracionMetaLeads.Leer().Activo)
            {
                return;
            }
            _ = await ImportadorLeadsMetaGraph.DeProduccion().EjecutarJob().ConfigureAwait(false);
        }
    }
}
