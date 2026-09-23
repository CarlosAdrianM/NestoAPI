using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.Agencias.CTT
{
    /// <summary>
    /// NestoAPI#493: CTT Express por su API REST (manifiesto, etiquetas, anulación, seguimiento).
    /// Mismo contrato que Innovatrans (<see cref="IAgenciaRemota"/>): NestoAPI registra el envío,
    /// devuelve el albarán (shipping_code de CTT, 22 dígitos) y la etiqueta ZPL, y Nesto solo imprime.
    ///
    /// Contrato de la API verificado en el sandbox el 17/09/26:
    ///   POST manifest/v2.0/shippings                       -> 201 {shipping_data:{shipping_code, items[]}}
    ///   GET  trf/labelling/v1.0/shippings/{code}/shipping-labels?label_type_code=ZPL&amp;model_type_code=SINGLE
    ///                                                      -> 200 {data:{thermal_label:[zpl por bulto]}}
    ///   POST manifest/v1.0/rpc-cancel-shipping-by-shipping-code/{code} -> 204; repetido -> 403 ALREADY_NULLED_SHIPPING
    ///   GET  trf/item-history-api/history/{code}?view=APITRACK&amp;showItems=false -> 200 {data:{shipping_history:{events[]}}}
    /// CTT no tiene "modificar": se anula y se registra de nuevo (albarán nuevo, etiqueta nueva).
    /// </summary>
    public class AgenciaRemotaCTT : IAgenciaRemota
    {
        // CTT pide medidas por bulto; no guardamos dimensiones (solo el peso), así que va la caja
        // MEDIANA, la más usada, igual que en Innovatrans. Si DatosEnvioRemoto trae medidas, prevalecen.
        internal const decimal LARGO_POR_DEFECTO = 32m;
        internal const decimal ANCHO_POR_DEFECTO = 23m;
        internal const decimal ALTO_POR_DEFECTO = 29m;

        internal const string RUTA_MANIFIESTO = "manifest/v2.0/shippings";
        internal const string RUTA_ANULAR = "manifest/v1.0/rpc-cancel-shipping-by-shipping-code/";
        internal const string RUTA_ETIQUETAS = "trf/labelling/v1.0/shippings/";
        internal const string RUTA_SEGUIMIENTO = "trf/item-history-api/history/";
        internal const string CLAVE_YA_ANULADO = "ALREADY_NULLED_SHIPPING";

        private readonly IClienteRestCTT _cliente;
        private readonly ConfiguracionCTT _config;
        private readonly RegistroIntercambiosRemotos _registro;
        private readonly Func<DateTime> _hoy;

        public AgenciaRemotaCTT(IClienteRestCTT cliente, ConfiguracionCTT config, RegistroIntercambiosRemotos registro = null, Func<DateTime> hoy = null)
        {
            _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _registro = registro ?? new RegistroIntercambiosRemotos();
            _hoy = hoy ?? (() => DateTime.Today);
        }

        public IReadOnlyList<IntercambioRemoto> Intercambios => _registro.Intercambios;

        // Recién integrada: logging detallado ON para vigilarla de cerca (NestoAPI#259).
        public bool LoggingDetallado => true;

        public async Task<ResultadoTramitacionRemota> InsertarYEtiquetarAsync(DatosEnvioRemoto envio)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));

            if (envio.Peso <= 0)
            {
                return new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Error = "CTT necesita el peso del envío (mayor que 0 kg). Indícalo antes de imprimir la etiqueta."
                };
            }

            JObject manifiesto;
            try
            {
                manifiesto = ConstruirManifiesto(envio);
            }
            catch (ArgumentException ex)
            {
                return new ResultadoTramitacionRemota { Exito = false, Error = ex.Message };
            }

            RespuestaCTT respuesta = await _cliente.EnviarAsync(HttpMethod.Post, RUTA_MANIFIESTO, manifiesto, "Manifiesto").ConfigureAwait(false);
            if (!respuesta.Exito)
            {
                return new ResultadoTramitacionRemota { Exito = false, Error = "No se pudo registrar el envío en CTT. " + respuesta.Error };
            }

            string albaran = respuesta.Json?["shipping_data"]?["shipping_code"]?.ToString();
            if (string.IsNullOrWhiteSpace(albaran))
            {
                throw new AgenciaRemotaException("CTT aceptó el envío pero la respuesta no trae shipping_code: " + respuesta.Cuerpo);
            }

            EtiquetaDataTrans etiqueta = await ObtenerEtiquetaAsync(albaran, null, null).ConfigureAwait(false);
            return new ResultadoTramitacionRemota
            {
                Exito = etiqueta.Exito,
                Albaran = albaran,
                Bultos = etiqueta.Exito ? ContarEtiquetas(etiqueta) : envio.Bultos,
                Etiqueta = etiqueta,
                Error = etiqueta.Exito ? null : $"El envío se registró en CTT (albarán {albaran}) pero no se pudo obtener la etiqueta: {etiqueta.Error}"
            };
        }

        public Task<EtiquetaDataTrans> ReimprimirAsync(string albaran, int? desdeBulto = null, int? hastaBulto = null)
        {
            if (string.IsNullOrWhiteSpace(albaran)) throw new ArgumentNullException(nameof(albaran));
            return ObtenerEtiquetaAsync(albaran.Trim(), desdeBulto, hastaBulto);
        }

        public async Task<ResultadoOperacionRemota> AnularAsync(string albaran)
        {
            if (string.IsNullOrWhiteSpace(albaran)) throw new ArgumentNullException(nameof(albaran));

            RespuestaCTT respuesta = await _cliente.EnviarAsync(HttpMethod.Post, RUTA_ANULAR + albaran.Trim(), new JObject(), "Anular").ConfigureAwait(false);
            if (respuesta.Exito)
            {
                return new ResultadoOperacionRemota { Exito = true };
            }
            // Ya estaba anulado en CTT: el objetivo (que no salga) está cumplido. Idempotente.
            if (respuesta.Codigo == (int)HttpStatusCode.Forbidden && respuesta.ClaveError == CLAVE_YA_ANULADO)
            {
                return new ResultadoOperacionRemota { Exito = true };
            }
            return new ResultadoOperacionRemota
            {
                Exito = false,
                Error = $"CTT no permite anular el envío {albaran.Trim()}. {respuesta.Error}"
            };
        }

        /// <summary>
        /// CTT no modifica un envío ya manifestado: se anula el albarán anterior y se registra uno
        /// nuevo con los datos corregidos. El resultado trae el albarán NUEVO (el controlador lo
        /// guarda en CodigoBarras) y su etiqueta. Si la anulación falla, no se toca nada.
        /// </summary>
        public async Task<ResultadoTramitacionRemota> ModificarYEtiquetarAsync(DatosEnvioRemoto envio, string albaran)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            if (string.IsNullOrWhiteSpace(albaran)) throw new ArgumentNullException(nameof(albaran));

            ResultadoOperacionRemota anulacion = await AnularAsync(albaran).ConfigureAwait(false);
            if (!anulacion.Exito)
            {
                return new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Error = "No se pudo modificar el envío en CTT porque no deja anular el albarán anterior. " + anulacion.Error
                };
            }
            return await InsertarYEtiquetarAsync(envio).ConfigureAwait(false);
        }

        public async Task<SeguimientoEnvioRemoto> ConsultarSeguimientoAsync(string albaran)
        {
            if (string.IsNullOrWhiteSpace(albaran)) throw new ArgumentNullException(nameof(albaran));

            RespuestaCTT respuesta = await _cliente.EnviarAsync(HttpMethod.Get,
                RUTA_SEGUIMIENTO + albaran.Trim() + "?view=APITRACK&showItems=false", null, "Seguimiento").ConfigureAwait(false);
            if (respuesta.Codigo == (int)HttpStatusCode.NotFound)
            {
                // Aún no registrado en CTT o código que no es suyo: no es un estado real (NestoAPI#264).
                return new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Desconocido, Detalle = "CTT no encuentra el envío " + albaran.Trim() };
            }
            if (!respuesta.Exito)
            {
                throw new AgenciaRemotaException($"CTT (Seguimiento) del albarán {albaran.Trim()}: {respuesta.Error}");
            }

            JArray eventos = respuesta.Json?["data"]?["shipping_history"]?["events"] as JArray;
            return InterpretarEventos(eventos, albaran.Trim());
        }

        // ---- Núcleo puro (testeable sin HTTP) ----

        /// <summary>
        /// Manifiesto de CTT para un envío nuestro. Un bulto = un item; el peso declarado del envío se
        /// reparte por igual entre los bultos (CTT exige peso por item). Reembolso = additional REE.
        /// </summary>
        internal JObject ConstruirManifiesto(DatosEnvioRemoto envio)
        {
            string cp = (envio.CodigoPostal ?? string.Empty).Trim();
            int bultos = Math.Max(1, envio.Bultos);
            decimal pesoBulto = Math.Max(0.01m, Math.Round(envio.Peso / bultos, 2, MidpointRounding.AwayFromZero));
            decimal largo = envio.Largo > 0 ? envio.Largo : LARGO_POR_DEFECTO;
            decimal ancho = envio.Ancho > 0 ? envio.Ancho : ANCHO_POR_DEFECTO;
            decimal alto = envio.Alto > 0 ? envio.Alto : ALTO_POR_DEFECTO;

            var items = new JArray();
            for (int i = 0; i < bultos; i++)
            {
                items.Add(new JObject
                {
                    ["item_weight_declared"] = pesoBulto,
                    ["item_length_declared"] = largo,
                    ["item_width_declared"] = ancho,
                    ["item_height_declared"] = alto
                });
            }

            var manifiesto = new JObject
            {
                ["client_center_code"] = _config.ClientCenterCode,
                ["shipping_type_code"] = MapeadorTipoServicioCTT.TipoServicioDesdeCodigoPostal(cp),
                ["client_references"] = new JArray(Acotar(envio.Referencia, 30) ?? string.Empty, string.Empty),
                ["shipping_weight_declared"] = Math.Round(envio.Peso, 2, MidpointRounding.AwayFromZero),
                ["item_count"] = bultos,
                ["sender_name"] = _config.Remitente.Nombre,
                ["sender_country_code"] = _config.Remitente.Pais ?? "ES",
                ["sender_postal_code"] = _config.Remitente.CodigoPostal,
                ["sender_address"] = _config.Remitente.Direccion,
                ["sender_town"] = _config.Remitente.Poblacion,
                ["sender_phones"] = Telefonos(_config.Remitente.Telefono, null),
                ["recipient_name"] = Acotar(envio.Nombre, 60),
                ["recipient_country_code"] = MapeadorTipoServicioCTT.PaisDesdeCodigoPostal(cp),
                ["recipient_postal_code"] = cp,
                ["recipient_address"] = Acotar(envio.Direccion, 100),
                ["recipient_town"] = Acotar(envio.Poblacion, 60),
                ["recipient_phones"] = Telefonos(envio.Telefono, envio.Movil),
                ["shipping_date"] = _hoy().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["delivery"] = new JObject
                {
                    ["contact_name"] = Acotar(envio.Nombre, 60),
                    ["comments"] = Acotar(envio.Observaciones, 200) ?? string.Empty
                },
                ["items"] = items
            };
            if (!string.IsNullOrWhiteSpace(_config.Remitente.Email))
            {
                manifiesto["sender_email_notify_address"] = _config.Remitente.Email;
            }
            // Con email, CTT avisa al cliente (entrega prevista, entrega hoy, ausente) y le deja elegir
            // un punto Collectt Express; sin él, no hay avisos.
            if (!string.IsNullOrWhiteSpace(envio.Email) && envio.Email.Contains("@"))
            {
                manifiesto["recipient_email_notify_address"] = envio.Email.Trim();
            }
            if (envio.Reembolso > 0)
            {
                manifiesto["additionals"] = new JArray(new JObject
                {
                    ["additional_code"] = "REE",
                    ["additional_value"] = Math.Round(envio.Reembolso, 2, MidpointRounding.AwayFromZero),
                    ["additional_flag"] = true,
                    ["additional_text"] = string.Empty,
                    ["additional_sub_code"] = string.Empty
                });
            }
            return manifiesto;
        }

        /// <summary>
        /// Último evento de tipo STATUS de CTT a nuestro estado. Códigos vistos en el sandbox y en el
        /// manual (Get Shipping Tracking v2.0): 0000 manifestado, 0900 en tránsito, 1200 en delegación
        /// de destino, 1500 en reparto, 1600 entrega fallida, 2100 entregado, 3000 anulado. Para los
        /// que no conocemos se mira la descripción; si tampoco dice nada, sigue tramitado.
        /// OJO (23/09/26): "en tránsito/en reparto" es TRAMITADO para nosotros. EstadoEnvioSeguimiento.EnCurso
        /// vale 0 = "En curso" de EnviosAgencia (etiqueta SIN tramitar): el 22/09 el poll devolvió los 20
        /// primeros envíos de CTT a la pestaña En curso al leer "ENVÍO RECOGIDO". El texto va en el detalle.
        /// </summary>
        internal static readonly string[] CODIGOS_CONOCIDOS = { "0000", "0900", "1200", "1500", "1600", "2100", "3000" };

        internal static SeguimientoEnvioRemoto InterpretarEventos(JArray eventos, string albaran = null)
        {
            if (eventos == null || !eventos.Any())
            {
                return new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Desconocido, Detalle = "CTT no devuelve eventos" };
            }

            JToken ultimo = eventos
                .Where(e => string.Equals(e["type"]?.ToString(), "STATUS", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e["event_date"]?.ToString())
                .LastOrDefault() ?? eventos.Last();

            string codigo = (ultimo["code"]?.ToString() ?? string.Empty).Trim();
            string descripcion = (ultimo["description"]?.ToString() ?? string.Empty).Trim();
            string incidencia = ultimo["detail"]?["incident_type_name"]?.ToString();
            DateTime? fecha = LeerFecha(ultimo["event_date"]?.ToString());
            string detalle = string.IsNullOrWhiteSpace(incidencia) ? descripcion : $"{descripcion}: {incidencia}";

            EstadoEnvioSeguimiento estado;
            switch (codigo)
            {
                case "0000":
                case "0900":
                case "1200":
                case "1500": estado = EstadoEnvioSeguimiento.Tramitado; break;
                case "2100": estado = EstadoEnvioSeguimiento.Entregado; break;
                case "1600": estado = EstadoEnvioSeguimiento.Incidentado; break;
                case "3000": estado = EstadoEnvioSeguimiento.Desconocido; detalle = "Anulado en CTT"; break;
                default:
                    estado = EstadoDesdeDescripcion(descripcion);
                    LoguearCodigoNoContemplado(codigo, descripcion, albaran);
                    break;
            }

            return new SeguimientoEnvioRemoto
            {
                Estado = estado,
                FechaEntrega = estado == EstadoEnvioSeguimiento.Entregado ? fecha : null,
                Detalle = detalle
            };
        }

        /// <summary>
        /// Vigilancia como en Innovatrans (NestoAPI#259): un código de estado que no está en la lista se
        /// interpreta por la descripción (o queda en curso) y se deja constancia en ELMAH para escribir
        /// su tratamiento. Nunca lanza: lo dispara el poll de Hangfire.
        /// </summary>
        private static void LoguearCodigoNoContemplado(string codigo, string descripcion, string albaran)
        {
            try
            {
                ElmahHelper.Log(new Exception(
                    $"Estado de CTT no contemplado: código '{codigo}' '{descripcion}' (albarán {albaran}). Revisar si hay que tratarlo (NestoAPI#493)."),
                    "Sistema (seguimiento de envíos)");
            }
            catch
            {
                // El diagnóstico nunca rompe el seguimiento.
            }
        }

        private static EstadoEnvioSeguimiento EstadoDesdeDescripcion(string descripcion)
        {
            string d = (descripcion ?? string.Empty).ToUpperInvariant();
            if (d.Contains("DEVUEL") || d.Contains("DEVOLUC") || d.Contains("RETORN") || d.Contains("RETURN")) return EstadoEnvioSeguimiento.Devuelto;
            if (d.Contains("ENTREGADO") || d.Contains("DELIVERED")) return EstadoEnvioSeguimiento.Entregado;
            if (d.Contains("FALLID") || d.Contains("FAILED") || d.Contains("INCIDEN") || d.Contains("NO ENTREG")) return EstadoEnvioSeguimiento.Incidentado;
            if (d.Contains("ANULA") || d.Contains("CANCEL")) return EstadoEnvioSeguimiento.Desconocido;
            return EstadoEnvioSeguimiento.Tramitado;
        }

        private async Task<EtiquetaDataTrans> ObtenerEtiquetaAsync(string albaran, int? desdeBulto, int? hastaBulto)
        {
            RespuestaCTT respuesta = await _cliente.EnviarAsync(HttpMethod.Get,
                RUTA_ETIQUETAS + albaran + "/shipping-labels?label_type_code=ZPL&model_type_code=SINGLE&label_offset=0",
                null, "Etiqueta").ConfigureAwait(false);
            if (!respuesta.Exito)
            {
                return new EtiquetaDataTrans { Error = respuesta.Error };
            }

            List<string> zpls = ExtraerZpl(respuesta.Json);
            if (zpls.Count == 0)
            {
                return new EtiquetaDataTrans { Error = "CTT no devolvió ninguna etiqueta ZPL para el albarán " + albaran };
            }

            // Reimpresión parcial: bultos 1..n (ambos inclusive).
            int desde = Math.Max(1, desdeBulto ?? 1);
            int hasta = Math.Min(zpls.Count, hastaBulto ?? zpls.Count);
            if (desde > hasta)
            {
                return new EtiquetaDataTrans { Error = $"El albarán {albaran} tiene {zpls.Count} bulto(s); no existe el rango {desde}-{hasta}." };
            }
            string contenido = string.Join("\n", zpls.Skip(desde - 1).Take(hasta - desde + 1).Select(NormalizadorZplCTT.CodificarNoAscii));
            return new EtiquetaDataTrans
            {
                Tipo = "plain/text",
                Codificacion = string.Empty,   // ZPL en crudo, como el de Innovatrans cuando lo activaron
                TamanoBytes = contenido.Length,
                Contenido = contenido
            };
        }

        /// <summary>Una etiqueta ZPL por bulto (thermal_label[]); data puede venir como objeto o como lista.</summary>
        internal static List<string> ExtraerZpl(JToken json)
        {
            var resultado = new List<string>();
            JToken data = json?["data"];
            if (data == null) return resultado;
            IEnumerable<JToken> bloques = data is JArray ? (IEnumerable<JToken>)data : new[] { data };
            foreach (JToken bloque in bloques)
            {
                if (bloque["thermal_label"] is JArray termicas)
                {
                    resultado.AddRange(termicas.Select(t => t.ToString()).Where(t => !string.IsNullOrWhiteSpace(t)));
                }
                else if (!string.IsNullOrWhiteSpace(bloque["label"]?.ToString()))
                {
                    resultado.Add(bloque["label"].ToString());
                }
            }
            return resultado;
        }

        /// <summary>Cada etiqueta de CTT termina en un único ^PQ (cantidad a imprimir): una por bulto.</summary>
        internal static int ContarEtiquetas(EtiquetaDataTrans etiqueta)
            => System.Text.RegularExpressions.Regex.Matches(etiqueta.Contenido ?? string.Empty, @"\^PQ").Count;

        private static JArray Telefonos(string telefono, string movil)
        {
            var lista = new List<string>();
            foreach (string t in new[] { movil, telefono })
            {
                string limpio = (t ?? string.Empty).Trim();
                if (limpio.Length > 0 && !lista.Contains(limpio)) lista.Add(limpio);
            }
            return new JArray(lista.Cast<object>().ToArray());
        }

        /// <summary>
        /// Acota y pasa a ASCII. CTT no conserva los caracteres no ASCII de NUESTROS textos: en el
        /// sandbox (17/09/26) "ESPAÑA Ñ" salió en la etiqueta como "ESPAA" y "impresión" como
        /// "impresin" (elimina la letra, no la translitera). Mejor "ESPANA" y "MOSTOLES" que un
        /// nombre mutilado en la etiqueta y en el seguimiento.
        /// </summary>
        private static string Acotar(string texto, int maximo)
        {
            string t = Transliterar(texto).Trim();
            if (t.Length == 0) return null;
            return t.Length > maximo ? t.Substring(0, maximo) : t;
        }

        /// <summary>Quita tildes y diéresis, ñ→n, ç→c, º/ª→o/a, y descarta cualquier otro carácter no ASCII.</summary>
        internal static string Transliterar(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return string.Empty;
            string descompuesto = texto.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue; // la tilde suelta
                if (c == 'º') { sb.Append('o'); continue; }
                if (c == 'ª') { sb.Append('a'); continue; }
                if (c == '€') { sb.Append("EUR"); continue; }
                if (c < 0x80) sb.Append(c);
            }
            return sb.ToString();
        }

        private static DateTime? LeerFecha(string iso)
        {
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime f)
                ? f.ToLocalTime()
                : (DateTime?)null;
        }
    }
}
