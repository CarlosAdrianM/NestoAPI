using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Embudo
{
    /// <summary>
    /// Lo que manda Odoo en GET {url}/nv/embudo/recomendacion (cabecera «X-NV-Clave»). Se lee con tolerancia: los campos que falten
    /// quedan a null y los de más se ignoran.
    /// </summary>
    public class RecomendacionRitmoOdoo
    {
        public DateTime? CalculadoEl { get; set; }
        public string Recomendacion { get; set; }
        public int? EscalonActual { get; set; }
        public int? EscalonRecomendado { get; set; }
        public int? TechoOdoo { get; set; }
        public string Motivo { get; set; }
        /// <summary>Las cifras que manda (tandas, enviados, entregados, rebotes…), para el rastro y el aviso.</summary>
        public Dictionary<string, string> Cifras { get; set; } = new Dictionary<string, string>();

        public static readonly string[] CAMPOS_CIFRAS =
        {
            "ventanaDesde", "ventanaHasta", "escalonActual", "escalonRecomendado", "techoOdoo", "tandas", "enviados", "entregados", "rebotes",
            "rebotesPorPolitica", "rebotesSinClasificar", "fallosEnvio", "bajas", "aperturas", "respuestas"
        };

        /// <summary>Null si el cuerpo no es un objeto JSON.</summary>
        public static RecomendacionRitmoOdoo Leer(string json)
        {
            JObject o;
            try
            {
                o = string.IsNullOrWhiteSpace(json) ? null : JToken.Parse(json) as JObject;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
            if (o == null)
            {
                return null;
            }
            string Texto(string campo) => o.GetValue(campo, StringComparison.OrdinalIgnoreCase)?.Type is JTokenType tipo
                && tipo != JTokenType.Null && tipo != JTokenType.Undefined ? o.GetValue(campo, StringComparison.OrdinalIgnoreCase).ToString() : null;
            int? Entero(string campo) => int.TryParse(Texto(campo), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : (int?)null;
            DateTime? Fecha(string campo)
            {
                JToken t = o.GetValue(campo, StringComparison.OrdinalIgnoreCase);
                if (t == null || t.Type == JTokenType.Null)
                {
                    return null;
                }
                if (t.Type == JTokenType.Date)
                {
                    DateTime d = t.Value<DateTime>();
                    return d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);
                }
                return DateTime.TryParse(t.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime f)
                    ? f : (DateTime?)null;
            }
            var r = new RecomendacionRitmoOdoo
            {
                CalculadoEl = Fecha("calculadoEl"),
                Recomendacion = Texto("recomendacion")?.Trim().ToLowerInvariant(),
                EscalonActual = Entero("escalonActual"),
                EscalonRecomendado = Entero("escalonRecomendado"),
                TechoOdoo = Entero("techoOdoo"),
                Motivo = Texto("motivo")?.Trim()
            };
            foreach (string campo in CAMPOS_CIFRAS)
            {
                string valor = Texto(campo);
                if (valor != null)
                {
                    r.Cifras[campo] = valor;
                }
            }
            return r;
        }

        /// <summary>«enviados 1200, entregados 1150, rebotes 12…» para el rastro y el aviso.</summary>
        public string TextoCifras() => string.Join(", ", Cifras.Select(c => $"{c.Key} {c.Value}"));
    }

    public enum AccionRitmoEmbudo
    {
        /// <summary>«mantener», o no hay nada que hacer (ya en el escalón, ya apagado, antes de 7 días…).</summary>
        Nada,
        Subir,
        Bajar,
        Parar,
        /// <summary>El dato tiene más de 8 días, no trae fecha o la recomendación no se entiende: no se toca nada y se avisa.</summary>
        DatoNoValido
    }

    public class DecisionRitmoEmbudo
    {
        public AccionRitmoEmbudo Accion { get; set; }
        public int TamanoAnterior { get; set; }
        public int TamanoNuevo { get; set; }
        /// <summary>Por qué (de usted, para el registro y el aviso).</summary>
        public string Explicacion { get; set; }
    }

    /// <summary>
    /// Las reglas del ritmo del embudo (Carlos 11/11/26), puras: escalones 50 → 75 → 100, tope 100 (la cuenta de Office 365 de la
    /// empresa no debe pasar de 100 al día). «subir»: un escalón, solo si han pasado 7 días desde el último cambio de tamaño y el dato
    /// tiene menos de 8, sin pasar de escalonRecomendado ni de 100. «bajar»: un escalón en el acto (no baja de 50). «parar»: apaga el
    /// interruptor en el acto. «mantener»: nada. Dato de más de 8 días o ilegible: nada (y aviso).
    /// </summary>
    public static class ReglasRitmoEmbudo
    {
        public static readonly IReadOnlyList<int> ESCALONES = new[] { 50, 75, 100 };
        public const int TOPE = 100;
        public const int DIAS_ENTRE_SUBIDAS = 7;
        public const int DIAS_MAXIMOS_DATO = 8;

        public const string SUBIR = "subir";
        public const string MANTENER = "mantener";
        public const string BAJAR = "bajar";
        public const string PARAR = "parar";

        /// <summary>
        /// Día del primer bloque del embudo (NV-2026-11-01, 10/11/26). Carlos 13/11/26: mientras el tamaño no haya cambiado nunca,
        /// los 7 días entre subidas se cuentan desde aquí (primera subida posible, el 17/11/26). A medianoche UTC para que el
        /// job de las 6:45 del séptimo día ya cuente los 7 días enteros.
        /// </summary>
        public static readonly DateTime PRIMER_BLOQUE_UTC = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>El último cambio de tamaño guardado o, si nunca ha cambiado, el día del primer bloque.</summary>
        public static DateTime UltimoCambioEfectivo(DateTime? guardadoUtc) => guardadoUtc ?? PRIMER_BLOQUE_UTC;

        public static DecisionRitmoEmbudo Decidir(RecomendacionRitmoOdoo r, int tamanoActual, bool encendido, DateTime? ultimoCambioUtc, DateTime ahoraUtc)
        {
            var d = new DecisionRitmoEmbudo { Accion = AccionRitmoEmbudo.Nada, TamanoAnterior = tamanoActual, TamanoNuevo = tamanoActual };
            if (r == null || !r.CalculadoEl.HasValue)
            {
                return NoValido(d, "Odoo no ha mandado la fecha del cálculo.");
            }
            if (ahoraUtc - r.CalculadoEl.Value >= TimeSpan.FromDays(DIAS_MAXIMOS_DATO))
            {
                return NoValido(d, $"La recomendación de Odoo es del {r.CalculadoEl.Value:dd/MM/yyyy}: tiene {DIAS_MAXIMOS_DATO} días o más.");
            }
            switch (r.Recomendacion)
            {
                case MANTENER:
                    d.Explicacion = "Odoo recomienda mantener el ritmo.";
                    return d;
                case PARAR:
                    if (!encendido)
                    {
                        d.Explicacion = "Odoo recomienda parar y el bloque diario ya está apagado.";
                        return d;
                    }
                    d.Accion = AccionRitmoEmbudo.Parar;
                    d.Explicacion = "Odoo recomienda parar el embudo: se apaga el bloque diario.";
                    return d;
                case BAJAR:
                    int anterior = ESCALONES.Where(e => e < tamanoActual).DefaultIfEmpty(ESCALONES[0]).Max();
                    if (anterior >= tamanoActual)
                    {
                        d.Explicacion = $"Odoo recomienda bajar, pero el bloque ya está en el mínimo ({tamanoActual}).";
                        return d;
                    }
                    d.Accion = AccionRitmoEmbudo.Bajar;
                    d.TamanoNuevo = anterior;
                    d.Explicacion = $"Odoo recomienda bajar el ritmo: el bloque diario pasa de {tamanoActual} a {anterior} clientes.";
                    return d;
                case SUBIR:
                    int siguiente = ESCALONES.Where(e => e > tamanoActual).DefaultIfEmpty(TOPE).Min();
                    int techo = Math.Min(TOPE, r.EscalonRecomendado.HasValue && r.EscalonRecomendado.Value > 0 ? r.EscalonRecomendado.Value : TOPE);
                    int nuevo = Math.Min(siguiente, techo);
                    if (nuevo <= tamanoActual)
                    {
                        d.Explicacion = $"Odoo recomienda subir, pero el bloque ya está en {tamanoActual} (tope {techo}).";
                        return d;
                    }
                    if (ultimoCambioUtc.HasValue && ahoraUtc - ultimoCambioUtc.Value < TimeSpan.FromDays(DIAS_ENTRE_SUBIDAS))
                    {
                        d.Explicacion = $"Odoo recomienda subir, pero el tamaño cambió hace menos de {DIAS_ENTRE_SUBIDAS} días ({ultimoCambioUtc.Value.ToLocalTime():dd/MM/yyyy}).";
                        return d;
                    }
                    d.Accion = AccionRitmoEmbudo.Subir;
                    d.TamanoNuevo = nuevo;
                    d.Explicacion = $"Odoo recomienda subir el ritmo: el bloque diario pasa de {tamanoActual} a {nuevo} clientes.";
                    return d;
                default:
                    return NoValido(d, $"Odoo ha mandado una recomendación que no se entiende («{r.Recomendacion}»).");
            }
        }

        private static DecisionRitmoEmbudo NoValido(DecisionRitmoEmbudo d, string explicacion)
        {
            d.Accion = AccionRitmoEmbudo.DatoNoValido;
            d.Explicacion = explicacion;
            return d;
        }
    }

    /// <summary>
    /// Embudo ritmo (Carlos 11/11/26): antes de SUBIR de escalón, el dominio de envío tiene que tener publicado el DKIM de Office 365
    /// (selector1._domainkey.{dominio}). True si el nombre existe (CNAME o TXT); false si «no existe el nombre» o no tiene registros;
    /// lanza si la consulta DNS falla.
    /// </summary>
    public interface IComprobadorDkim
    {
        Task<bool> TienePublicadoDkim(string dominio);
    }

    /// <summary>La consulta DNS de Windows (dnsapi DnsQuery_W): CNAME y, si no, TXT. Si falla, el job no sube.</summary>
    public class ComprobadorDkimDns : IComprobadorDkim
    {
        public const string SELECTOR = "selector1";
        private const short DNS_TYPE_CNAME = 5;
        private const short DNS_TYPE_TEXT = 16;
        private const int DNS_QUERY_STANDARD = 0;
        private const int DNS_ERROR_RCODE_NAME_ERROR = 9003;
        private const int DNS_INFO_NO_RECORDS = 9501;
        private const int DNS_FREE_RECORD_LIST = 1;

        [System.Runtime.InteropServices.DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
        private static extern int DnsQuery(string nombre, short tipo, int opciones, IntPtr servidores, out IntPtr resultados, IntPtr reservado);

        [System.Runtime.InteropServices.DllImport("dnsapi.dll", ExactSpelling = true)]
        private static extern void DnsRecordListFree(IntPtr lista, int tipoLiberacion);

        public static string Nombre(string dominio) => $"{SELECTOR}._domainkey.{dominio?.Trim().TrimEnd('.')}";

        public Task<bool> TienePublicadoDkim(string dominio) => Task.Run(() =>
        {
            string nombre = Nombre(dominio);
            foreach (short tipo in new[] { DNS_TYPE_CNAME, DNS_TYPE_TEXT })
            {
                int codigo = DnsQuery(nombre, tipo, DNS_QUERY_STANDARD, IntPtr.Zero, out IntPtr resultados, IntPtr.Zero);
                if (resultados != IntPtr.Zero)
                {
                    DnsRecordListFree(resultados, DNS_FREE_RECORD_LIST);
                }
                if (codigo == 0)
                {
                    return true;
                }
                if (codigo == DNS_ERROR_RCODE_NAME_ERROR)
                {
                    return false; // «no existe el nombre»
                }
                if (codigo != DNS_INFO_NO_RECORDS)
                {
                    throw new InvalidOperationException($"La consulta DNS de {nombre} ha fallado (código {codigo}).");
                }
            }
            return false;
        });
    }

    public enum ResultadoAjusteRitmoEmbudo
    {
        /// <summary>Odoo recomienda subir, pero el dominio no tiene el DKIM de Office 365 (o no se ha podido comprobar): no sube.</summary>
        PendienteDkim,
        SinConfiguracion,
        /// <summary>404 de Odoo ({"recomendacion": null}): aún no tiene ningún cálculo. No es un error: ni se toca ni se avisa.</summary>
        SinDatos,
        Fallo,
        DatoNoValido,
        SinCambios,
        Cambiado
    }

    /// <summary>
    /// Job «embudo-ajuste-ritmo» (Carlos 11/11/26): todos los días a las 6:45 de Madrid (antes del bloque de las 7:00) lee la
    /// recomendación de Odoo y ajusta SOLO el tamaño del bloque (o apaga el interruptor si Odoo dice «parar»). URL y clave en
    /// secretos.config (<see cref="CLAVE_URL"/>, <see cref="CLAVE_SECRETO"/>): sin ellas no hace nada ni avisa (Odoo aún no lo tiene).
    /// Cada cambio deja rastro (Modificaciones, con el motivo y las cifras de Odoo) y avisa a Dirección en la campana; un fallo o un
    /// dato viejo, una vez al día. Nunca lanza.
    /// </summary>
    public class EmbudoAjusteRitmoJobsService
    {
        public const string JOB_ID = "embudo-ajuste-ritmo";
        /// <summary>Todos los días a las 6:45 (hora de Madrid): Odoo calcula a las 4:30 UTC (las 6:30 de Madrid en verano).</summary>
        public const string CRON = "45 6 * * *";
        /// <summary>secretos.config: la URL base de Odoo (sin la ruta).</summary>
        public const string CLAVE_URL = "OdooEmbudoRitmoUrl";
        /// <summary>secretos.config: la clave que va en la cabecera «X-NV-Clave».</summary>
        public const string CLAVE_SECRETO = "OdooEmbudoRitmoClave";
        public const string RUTA = "/nv/embudo/recomendacion";
        public const string CABECERA = "X-NV-Clave";
        public const string TIPO_NOTIFICACION = "EmbudoAjusteRitmo";

        // Lo que se guarda en ParametrosUsuario «(defecto)», junto al tamaño
        public const string PARAMETRO_FECHA_CAMBIO_TAMANO = "EmbudoTamanoBloqueFecha";
        public const string PARAMETRO_RECOMENDACION_FECHA = "EmbudoRecomendacionFecha";
        public const string PARAMETRO_RECOMENDACION = "EmbudoRecomendacion";
        public const string PARAMETRO_RECOMENDACION_MOTIVO = "EmbudoRecomendacionMotivo";
        public const string PARAMETRO_ULTIMO_AVISO_FALLO = "EmbudoRitmoUltimoAvisoFallo";
        /// <summary>Cuándo se avisó por última vez de que no se sube por el DKIM (como mucho una vez por semana).</summary>
        public const string PARAMETRO_ULTIMO_AVISO_DKIM = "EmbudoRitmoUltimoAvisoDkim";
        /// <summary>El dominio desde el que salen los correos del embudo (Office 365): su DKIM tiene que estar publicado para subir.</summary>
        public const string DOMINIO_ENVIO = "nuevavision.es";
        public const int DIAS_ENTRE_AVISOS_DKIM = 7;

        internal const string USUARIO = "NestoAPI (ritmo del embudo)";
        internal const string DOMINIO = "NUEVAVISION\\";

        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private static DateTime ultimoRegistroSinConfiguracion = DateTime.MinValue;

        private readonly IParametrosEmbudoDiario parametros;
        private readonly Func<string, string> configuracion;
        private readonly Func<string, string, Task<(HttpStatusCode Estado, string Cuerpo)>> pedir;
        private readonly Func<List<string>> usuariosDireccion;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Action<Exception> registrar;
        private readonly IComprobadorDkim dkim;

        public EmbudoAjusteRitmoJobsService() : this(null)
        {
        }

        internal EmbudoAjusteRitmoJobsService(IParametrosEmbudoDiario parametros, Func<string, string> configuracion = null,
            Func<string, string, Task<(HttpStatusCode Estado, string Cuerpo)>> pedir = null, Func<List<string>> usuariosDireccion = null,
            IServicioNotificacionesPush notificaciones = null, Action<Exception> registrar = null, IComprobadorDkim dkim = null)
        {
            this.dkim = dkim ?? new ComprobadorDkimDns();
            this.parametros = parametros ?? new ParametrosEmbudoDiarioEF();
            this.configuracion = configuracion ?? (clave => ConfigurationManager.AppSettings[clave]);
            this.pedir = pedir ?? Pedir;
            this.usuariosDireccion = usuariosDireccion ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.DIRECCION));
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>Punto de entrada de Hangfire.</summary>
        public static async Task Ajustar()
        {
            _ = await new EmbudoAjusteRitmoJobsService().AjustarAsync(DateTime.UtcNow).ConfigureAwait(false);
        }

        private static async Task<(HttpStatusCode, string)> Pedir(string url, string clave)
        {
            using (var peticion = new HttpRequestMessage(HttpMethod.Get, url))
            {
                peticion.Headers.Add(CABECERA, clave);
                using (HttpResponseMessage respuesta = await http.SendAsync(peticion).ConfigureAwait(false))
                {
                    string cuerpo = respuesta.Content == null ? null : await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return (respuesta.StatusCode, cuerpo);
                }
            }
        }

        public async Task<ResultadoAjusteRitmoEmbudo> AjustarAsync(DateTime ahoraUtc)
        {
            string url = configuracion(CLAVE_URL)?.Trim();
            string clave = configuracion(CLAVE_SECRETO)?.Trim();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(clave))
            {
                // Odoo aún no lo tiene: sin aviso; en ELMAH como mucho una vez al día
                if (ultimoRegistroSinConfiguracion.Date != ahoraUtc.Date)
                {
                    ultimoRegistroSinConfiguracion = ahoraUtc;
                    Registrar(new Exception($"[Embudo ritmo] (informativo) Sin {CLAVE_URL} o {CLAVE_SECRETO} en secretos.config: no se lee la recomendación de Odoo."));
                }
                return ResultadoAjusteRitmoEmbudo.SinConfiguracion;
            }

            RecomendacionRitmoOdoo recomendacion;
            try
            {
                (HttpStatusCode estado, string cuerpo) = await pedir(url.TrimEnd('/') + RUTA, clave).ConfigureAwait(false);
                if (estado == HttpStatusCode.NotFound)
                {
                    // Contrato con Odoo: 404 {"recomendacion": null} = aún no ha calculado nada. Ni se toca ni se avisa.
                    return ResultadoAjusteRitmoEmbudo.SinDatos;
                }
                if (estado != HttpStatusCode.OK)
                {
                    await Fallo(ahoraUtc, estado == HttpStatusCode.Forbidden
                        ? "Odoo ha rechazado la clave (403)."
                        : $"Odoo ha contestado {(int)estado}.").ConfigureAwait(false);
                    return ResultadoAjusteRitmoEmbudo.Fallo;
                }
                recomendacion = RecomendacionRitmoOdoo.Leer(cuerpo);
                if (recomendacion == null)
                {
                    await Fallo(ahoraUtc, "La respuesta de Odoo no se puede leer.").ConfigureAwait(false);
                    return ResultadoAjusteRitmoEmbudo.Fallo;
                }
            }
            catch (Exception ex)
            {
                await Fallo(ahoraUtc, $"No se ha podido hablar con Odoo: {ex.Message}", ex).ConfigureAwait(false);
                return ResultadoAjusteRitmoEmbudo.Fallo;
            }

            try
            {
                await parametros.Escribir(PARAMETRO_RECOMENDACION_FECHA, ahoraUtc.ToString("o", CultureInfo.InvariantCulture), USUARIO, dejarRastro: false).ConfigureAwait(false);
                await parametros.Escribir(PARAMETRO_RECOMENDACION, Recortar(recomendacion.Recomendacion ?? "(sin recomendación)", 20), USUARIO, dejarRastro: false).ConfigureAwait(false);
                await parametros.Escribir(PARAMETRO_RECOMENDACION_MOTIVO, Recortar(recomendacion.Motivo ?? string.Empty, 160), USUARIO, dejarRastro: false).ConfigureAwait(false);

                bool encendido = EmbudoBloqueDiarioJobsService.EstaEncendido(parametros.Leer(EmbudoBloqueDiarioJobsService.PARAMETRO_INTERRUPTOR));
                int tamano = EmbudoBloqueDiarioJobsService.TamanoBloque(parametros.Leer(EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO), Registrar);
                DateTime? ultimoCambio = ReglasRitmoEmbudo.UltimoCambioEfectivo(LeerFecha(parametros.Leer(PARAMETRO_FECHA_CAMBIO_TAMANO)));
                DecisionRitmoEmbudo decision = ReglasRitmoEmbudo.Decidir(recomendacion, tamano, encendido, ultimoCambio, ahoraUtc);

                string detalle = $"Motivo de Odoo: {recomendacion.Motivo ?? "(sin motivo)"}. Cifras: {recomendacion.TextoCifras()}. Calculado el {recomendacion.CalculadoEl:o}.";
                if (decision.Accion == AccionRitmoEmbudo.Subir && !await DkimPublicado(decision, ahoraUtc).ConfigureAwait(false))
                {
                    return ResultadoAjusteRitmoEmbudo.PendienteDkim;
                }
                switch (decision.Accion)
                {
                    case AccionRitmoEmbudo.Subir:
                    case AccionRitmoEmbudo.Bajar:
                        await parametros.Escribir(EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO, decision.TamanoNuevo.ToString(CultureInfo.InvariantCulture),
                            USUARIO, $"{decision.Explicacion} {detalle}").ConfigureAwait(false);
                        await parametros.Escribir(PARAMETRO_FECHA_CAMBIO_TAMANO, ahoraUtc.ToString("o", CultureInfo.InvariantCulture), USUARIO, dejarRastro: false).ConfigureAwait(false);
                        await Avisar(decision.Accion == AccionRitmoEmbudo.Subir ? "Sube el ritmo del embudo" : "Baja el ritmo del embudo",
                            $"{decision.Explicacion} Motivo de Odoo: {recomendacion.Motivo ?? "(sin motivo)"}.").ConfigureAwait(false);
                        return ResultadoAjusteRitmoEmbudo.Cambiado;
                    case AccionRitmoEmbudo.Parar:
                        await parametros.Escribir(EmbudoBloqueDiarioJobsService.PARAMETRO_INTERRUPTOR, "0", USUARIO, $"{decision.Explicacion} {detalle}").ConfigureAwait(false);
                        await Avisar("Se ha parado el embudo",
                            $"{decision.Explicacion} Mañana no saldrá ningún bloque. Motivo de Odoo: {recomendacion.Motivo ?? "(sin motivo)"}. " +
                            "Para volver a encenderlo: PUT api/Clientes/Embudo/Configuracion { \"Encendido\": true }.").ConfigureAwait(false);
                        return ResultadoAjusteRitmoEmbudo.Cambiado;
                    case AccionRitmoEmbudo.DatoNoValido:
                        await Fallo(ahoraUtc, decision.Explicacion).ConfigureAwait(false);
                        return ResultadoAjusteRitmoEmbudo.DatoNoValido;
                    default:
                        return ResultadoAjusteRitmoEmbudo.SinCambios;
                }
            }
            catch (Exception ex)
            {
                await Fallo(ahoraUtc, $"No se ha podido aplicar la recomendación: {ex.Message}", ex).ConfigureAwait(false);
                return ResultadoAjusteRitmoEmbudo.Fallo;
            }
        }

        /// <summary>
        /// Carlos (11/11/26): no se sube sin el DKIM de Office 365 publicado (selector1._domainkey del dominio de envío). Si no está o
        /// no se puede comprobar, no sube y lo dice a Dirección como mucho una vez por semana. «bajar» y «parar» no dependen de esto.
        /// </summary>
        private async Task<bool> DkimPublicado(DecisionRitmoEmbudo decision, DateTime ahoraUtc)
        {
            string motivo;
            try
            {
                if (await dkim.TienePublicadoDkim(DOMINIO_ENVIO).ConfigureAwait(false))
                {
                    return true;
                }
                motivo = $"{ComprobadorDkimDns.Nombre(DOMINIO_ENVIO)} no existe";
            }
            catch (Exception ex)
            {
                motivo = $"no se ha podido comprobar {ComprobadorDkimDns.Nombre(DOMINIO_ENVIO)}: {ex.Message}";
            }
            string texto = $"Odoo recomienda subir a {decision.TamanoNuevo}, pero no se sube hasta activar el DKIM de Office 365 del dominio {DOMINIO_ENVIO}.";
            Registrar(new Exception($"[Embudo ritmo] {texto} ({motivo})"));
            try
            {
                DateTime? ultimoAviso = LeerFecha(parametros.Leer(PARAMETRO_ULTIMO_AVISO_DKIM));
                if (!ultimoAviso.HasValue || ahoraUtc - ultimoAviso.Value >= TimeSpan.FromDays(DIAS_ENTRE_AVISOS_DKIM))
                {
                    await parametros.Escribir(PARAMETRO_ULTIMO_AVISO_DKIM, ahoraUtc.ToString("o", CultureInfo.InvariantCulture), USUARIO, dejarRastro: false).ConfigureAwait(false);
                    await Avisar("El embudo no sube de ritmo", $"{texto} El bloque diario sigue en {decision.TamanoAnterior} clientes.").ConfigureAwait(false);
                }
            }
            catch (Exception exAviso)
            {
                Registrar(new Exception($"[Embudo ritmo] No se ha podido avisar del DKIM: {exAviso.Message}", exAviso));
            }
            return false;
        }

        internal static DateTime? LeerFecha(string valor)
            => DateTime.TryParse(valor?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime f)
                ? f : (DateTime?)null;

        /// <summary>Lo deja en ELMAH y avisa a Dirección como mucho una vez al día (mientras dure).</summary>
        private async Task Fallo(DateTime ahoraUtc, string motivo, Exception ex = null)
        {
            string texto = $"[Embudo ritmo] No se ha ajustado el ritmo del embudo: {motivo}";
            Registrar(ex == null ? new Exception(texto) : new Exception(texto, ex));
            try
            {
                string hoy = ahoraUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (parametros.Leer(PARAMETRO_ULTIMO_AVISO_FALLO) == hoy)
                {
                    return;
                }
                await parametros.Escribir(PARAMETRO_ULTIMO_AVISO_FALLO, hoy, USUARIO, dejarRastro: false).ConfigureAwait(false);
                await Avisar("No se ha podido leer el ritmo del embudo",
                    $"{motivo.Trim().TrimEnd('.')}. No se ha cambiado nada: el bloque diario sigue con el mismo tamaño.").ConfigureAwait(false);
            }
            catch (Exception exAviso)
            {
                Registrar(new Exception($"[Embudo ritmo] No se ha podido avisar a Dirección: {exAviso.Message}", exAviso));
            }
        }

        private async Task Avisar(string titulo, string cuerpo)
        {
            var notificacion = new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = cuerpo,
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_NOTIFICACION }
            };
            List<string> usuarios = (usuariosDireccion() ?? new List<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
        }

        private static string Recortar(string texto, int largo) => texto == null || texto.Length <= largo ? texto : texto.Substring(0, largo);

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(ex);
            }
            catch
            {
                // Nunca tumba el job
            }
        }
    }
}
