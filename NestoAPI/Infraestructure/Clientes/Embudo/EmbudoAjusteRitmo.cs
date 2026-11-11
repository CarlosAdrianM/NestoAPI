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

    public enum ResultadoAjusteRitmoEmbudo
    {
        SinConfiguracion,
        Fallo,
        DatoNoValido,
        SinCambios,
        Cambiado
    }

    /// <summary>
    /// Job «embudo-ajuste-ritmo» (Carlos 11/11/26): todos los días a las 6:30 de Madrid (antes del bloque de las 7:00) lee la
    /// recomendación de Odoo y ajusta SOLO el tamaño del bloque (o apaga el interruptor si Odoo dice «parar»). URL y clave en
    /// secretos.config (<see cref="CLAVE_URL"/>, <see cref="CLAVE_SECRETO"/>): sin ellas no hace nada ni avisa (Odoo aún no lo tiene).
    /// Cada cambio deja rastro (Modificaciones, con el motivo y las cifras de Odoo) y avisa a Dirección en la campana; un fallo o un
    /// dato viejo, una vez al día. Nunca lanza.
    /// </summary>
    public class EmbudoAjusteRitmoJobsService
    {
        public const string JOB_ID = "embudo-ajuste-ritmo";
        /// <summary>Todos los días a las 6:30 (hora de Madrid).</summary>
        public const string CRON = "30 6 * * *";
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

        public EmbudoAjusteRitmoJobsService() : this(null)
        {
        }

        internal EmbudoAjusteRitmoJobsService(IParametrosEmbudoDiario parametros, Func<string, string> configuracion = null,
            Func<string, string, Task<(HttpStatusCode Estado, string Cuerpo)>> pedir = null, Func<List<string>> usuariosDireccion = null,
            IServicioNotificacionesPush notificaciones = null, Action<Exception> registrar = null)
        {
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
                DateTime? ultimoCambio = LeerFecha(parametros.Leer(PARAMETRO_FECHA_CAMBIO_TAMANO));
                DecisionRitmoEmbudo decision = ReglasRitmoEmbudo.Decidir(recomendacion, tamano, encendido, ultimoCambio, ahoraUtc);

                string detalle = $"Motivo de Odoo: {recomendacion.Motivo ?? "(sin motivo)"}. Cifras: {recomendacion.TextoCifras()}. Calculado el {recomendacion.CalculadoEl:o}.";
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
