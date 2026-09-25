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
    ///   GET  trf/web-tracking/v1.0/shippings?...&amp;shipping_date=A[range]B -> 200 {data:[{shipping_code, shipping_status_code...}], pagination}
    /// CTT no tiene "modificar": se anula y se registra de nuevo (albarán nuevo, etiqueta nueva).
    /// </summary>
    public class AgenciaRemotaCTT : IAgenciaRemota, ISeguimientoPorLotes
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
        internal const string RUTA_SEGUIMIENTO_POR_FECHAS = "trf/web-tracking/v1.0/shippings";
        // Registros por página del listado por fechas (el de la documentación de CTT) y tope de páginas
        // por consulta: 40 x 50 = 2.000 envíos, muy por encima de lo que tenemos en vuelo, y un límite
        // duro para que un fallo de paginación nunca se convierta en una ráfaga contra el cupo.
        internal const int REGISTROS_POR_PAGINA = 50;
        internal const int MAX_PAGINAS = 40;
        internal const string CLAVE_YA_ANULADO = "ALREADY_NULLED_SHIPPING";

        // NestoAPI#494: tipos de retorno de CTT (EnviosAgencia.Retorno), los que ofrece Nesto en su lista.
        public const short RETORNO_NINGUNO = 0;
        /// <summary>Se entrega el envío y el repartidor trae algo de vuelta (adicional RET del manifiesto).</summary>
        public const short RETORNO_CON_RETORNO = 1;
        /// <summary>Solo recogida: CTT va al domicilio del cliente/proveedor y lo trae a nuestro almacén (pickup.has_pickup_asap).</summary>
        public const short RETORNO_RECOGIDA_EN_ORIGEN = 2;
        /// <summary>Clave del interruptor (ParametrosUsuario, empresa 1, «(defecto)»): "1"/"true" = activos.</summary>
        public const string CLAVE_RETORNOS_ACTIVOS = "CTTRetornosActivos";
        // Franja por defecto de la recogida: CTT la ajusta a la primera posible si no encaja.
        internal const string RECOGIDA_HORA_DESDE = "09:00";
        internal const string RECOGIDA_HORA_HASTA = "18:00";

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

            // NestoAPI#494/#505: si el envío nuevo no se puede registrar (servicio o retorno que CTT no
            // tiene, retornos apagados...), no se anula el anterior: nos quedaríamos sin ninguno.
            try
            {
                ConstruirManifiesto(envio);
            }
            catch (ArgumentException ex)
            {
                return new ResultadoTramitacionRemota { Exito = false, Error = ex.Message };
            }

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
            LanzarSiCupoAgotado(respuesta, "Seguimiento");
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

        /// <summary>
        /// 23/09/26: todos los envíos del centro con fecha de envío en el rango, en una o pocas llamadas
        /// (páginas de <see cref="REGISTROS_POR_PAGINA"/>). Es lo que usa el poll: preguntar envío a envío
        /// agotaba el cupo de CTT (429 a partir de la 11ª consulta de la pasada de las 10:00).
        /// </summary>
        public async Task<IReadOnlyDictionary<string, SeguimientoEnvioRemoto>> ConsultarSeguimientosAsync(DateTime desde, DateTime hasta)
        {
            if (string.IsNullOrWhiteSpace(_config.ClientCenterCode))
            {
                throw new AgenciaRemotaException("CTT: falta CTT:ClientCenterCode en la configuración (necesario para el seguimiento por fechas).");
            }

            var resultado = new Dictionary<string, SeguimientoEnvioRemoto>(StringComparer.OrdinalIgnoreCase);
            string rango = $"{desde:yyyy-MM-dd}[range]{hasta:yyyy-MM-dd}";
            int pagina = 1;
            for (int llamadas = 0; llamadas < MAX_PAGINAS; llamadas++)
            {
                string ruta = $"{RUTA_SEGUIMIENTO_POR_FECHAS}?page_limit={REGISTROS_POR_PAGINA}&page_offsets={pagina}" +
                    $"&mapping_table_code=APITRACK&order_by=-shipping_date&client_center_code={_config.ClientCenterCode}&shipping_date={rango}";
                RespuestaCTT respuesta = await _cliente.EnviarAsync(HttpMethod.Get, ruta, null, "SeguimientoPorFechas").ConfigureAwait(false);
                LanzarSiCupoAgotado(respuesta, "SeguimientoPorFechas");
                if (!respuesta.Exito)
                {
                    throw new AgenciaRemotaException($"CTT (SeguimientoPorFechas {rango}, página {pagina}): {respuesta.Error}");
                }

                if (respuesta.Json?["data"] is JArray registros)
                {
                    foreach (JToken registro in registros)
                    {
                        string codigo = registro["shipping_code"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(codigo))
                        {
                            resultado[codigo] = InterpretarRegistroPorFechas(registro);
                        }
                    }
                }

                int? siguiente = LeerEntero(respuesta.Json?["pagination"]?["page_offsets"]?["next"]);
                int? ultima = LeerEntero(respuesta.Json?["pagination"]?["page_offsets"]?["last"]);
                if (!siguiente.HasValue || siguiente.Value <= pagina || (ultima.HasValue && pagina >= ultima.Value))
                {
                    break;
                }
                pagina = siguiente.Value;
            }
            return resultado;
        }

        private static void LanzarSiCupoAgotado(RespuestaCTT respuesta, string operacion)
        {
            if (respuesta != null && respuesta.CupoAgotado)
            {
                string espera = respuesta.ReintentarTras.HasValue
                    ? $" CTT pide esperar {Math.Ceiling(respuesta.ReintentarTras.Value.TotalMinutes)} min."
                    : string.Empty;
                throw new CupoAgenciaAgotadoException($"Cupo de la API de CTT agotado ({operacion}).{espera} {respuesta.Error}", respuesta.ReintentarTras);
            }
        }

        private static int? LeerEntero(JToken token)
            => token != null && token.Type != JTokenType.Null && int.TryParse(token.ToString(), out int n) ? n : (int?)null;

        // ---- Núcleo puro (testeable sin HTTP) ----

        /// <summary>
        /// Manifiesto de CTT para un envío nuestro. Un bulto = un item; el peso declarado del envío se
        /// reparte por igual entre los bultos (CTT exige peso por item). Reembolso = additional REE.
        /// </summary>
        internal JObject ConstruirManifiesto(DatosEnvioRemoto envio)
        {
            ValidarRetorno(envio);
            if (envio.Retorno == RETORNO_RECOGIDA_EN_ORIGEN)
            {
                return ConstruirManifiestoRecogida(envio);
            }

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
                // NestoAPI#505: el servicio elegido (48 h por defecto, 24 h si el usuario lo fuerza).
                ["shipping_type_code"] = MapeadorTipoServicioCTT.TipoServicio(envio.Servicio, cp),
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
            var adicionales = new JArray();
            if (envio.Reembolso > 0)
            {
                adicionales.Add(new JObject
                {
                    ["additional_code"] = "REE",
                    ["additional_value"] = Math.Round(envio.Reembolso, 2, MidpointRounding.AwayFromZero),
                    ["additional_flag"] = true,
                    ["additional_text"] = string.Empty,
                    ["additional_sub_code"] = string.Empty
                });
            }
            if (envio.Retorno == RETORNO_CON_RETORNO)
            {
                // NestoAPI#494: "RETURN SHIPMENT" del manual (Shipping Manifest v2.2, Additionals).
                adicionales.Add(new JObject
                {
                    ["additional_code"] = "RET",
                    ["additional_value"] = 0,
                    ["additional_flag"] = true,
                    ["additional_text"] = string.Empty,
                    ["additional_sub_code"] = string.Empty
                });
            }
            if (adicionales.Count > 0)
            {
                manifiesto["additionals"] = adicionales;
            }
            return manifiesto;
        }

        /// <summary>
        /// NestoAPI#494: un tipo de retorno solo sale hacia CTT si es uno de los suyos y el interruptor
        /// está encendido. Lanza ArgumentException (InsertarYEtiquetar la devuelve como error, sin llamar
        /// a CTT): nunca se manda como envío normal algo que el usuario pidió como recogida.
        /// </summary>
        private void ValidarRetorno(DatosEnvioRemoto envio)
        {
            if (envio.Retorno == RETORNO_NINGUNO) return;
            if (envio.Retorno != RETORNO_CON_RETORNO && envio.Retorno != RETORNO_RECOGIDA_EN_ORIGEN)
            {
                throw new ArgumentException($"CTT no tiene el tipo de retorno {envio.Retorno}. Elige «NO», «Con retorno» o «Recogida en origen».");
            }
            if (!_config.RetornosActivos)
            {
                throw new ArgumentException($"Las recogidas y retornos por CTT todavía no están activados (parámetro {CLAVE_RETORNOS_ACTIVOS}). Tramítalo sin retorno o por otra agencia.");
            }
            if (envio.Retorno == RETORNO_RECOGIDA_EN_ORIGEN && envio.Reembolso > 0)
            {
                throw new ArgumentException("Una recogida en origen no puede llevar reembolso: CTT no cobra al recoger.");
            }
        }

        /// <summary>
        /// NestoAPI#494: recogida en el domicilio de un cliente o proveedor que viene a nuestro almacén.
        /// Se invierte el envío: remitente = el domicilio del envío (EnviosAgencia), destinatario = el
        /// remitente de siempre (CTT:Remitente:*, Algete), y el bloque pickup con has_pickup_asap = true
        /// para que CTT genere la orden de recogida en la dirección del remitente (manual Shipping
        /// Manifest v2.2, "Pickup Information Section"). Si la fecha/franja no encaja, CTT la ajusta a la
        /// primera posible. El servicio sale de la zona del domicilio donde se recoge.
        /// </summary>
        internal JObject ConstruirManifiestoRecogida(DatosEnvioRemoto envio)
        {
            string cpOrigen = (envio.CodigoPostal ?? string.Empty).Trim();
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

            DateTime hoy = _hoy().Date;
            DateTime fechaRecogida = envio.FechaRecogida.HasValue && envio.FechaRecogida.Value.Date > hoy
                ? envio.FechaRecogida.Value.Date
                : hoy;

            var manifiesto = new JObject
            {
                ["client_center_code"] = _config.ClientCenterCode,
                ["shipping_type_code"] = MapeadorTipoServicioCTT.TipoServicio(envio.Servicio, cpOrigen),
                ["client_references"] = new JArray(Acotar(envio.Referencia, 30) ?? string.Empty, string.Empty),
                ["shipping_weight_declared"] = Math.Round(envio.Peso, 2, MidpointRounding.AwayFromZero),
                ["item_count"] = bultos,
                ["sender_name"] = Acotar(envio.Nombre, 60),
                ["sender_country_code"] = MapeadorTipoServicioCTT.PaisDesdeCodigoPostal(cpOrigen),
                ["sender_postal_code"] = cpOrigen,
                ["sender_address"] = Acotar(envio.Direccion, 100),
                ["sender_town"] = Acotar(envio.Poblacion, 60),
                ["sender_phones"] = Telefonos(envio.Telefono, envio.Movil),
                ["recipient_name"] = _config.Remitente.Nombre,
                ["recipient_country_code"] = _config.Remitente.Pais ?? "ES",
                ["recipient_postal_code"] = _config.Remitente.CodigoPostal,
                ["recipient_address"] = _config.Remitente.Direccion,
                ["recipient_town"] = _config.Remitente.Poblacion,
                ["recipient_phones"] = Telefonos(_config.Remitente.Telefono, null),
                ["shipping_date"] = hoy.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["pickup"] = new JObject
                {
                    ["has_pickup_asap"] = true,
                    ["comments"] = Acotar(envio.Observaciones, 200) ?? string.Empty,
                    ["date"] = fechaRecogida.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["time"] = new JArray(new JObject
                    {
                        ["min_hourminute"] = RECOGIDA_HORA_DESDE,
                        ["max_hourminute"] = RECOGIDA_HORA_HASTA
                    })
                },
                ["delivery"] = new JObject
                {
                    ["contact_name"] = _config.Remitente.Nombre,
                    ["comments"] = string.Empty
                },
                ["items"] = items
            };
            // Los avisos de CTT nos llegan a nosotros, que en una recogida somos el destinatario.
            if (!string.IsNullOrWhiteSpace(_config.Remitente.Email))
            {
                manifiesto["recipient_email_notify_address"] = _config.Remitente.Email;
            }
            return manifiesto;
        }

        /// <summary>
        /// Último evento de tipo STATUS de CTT a nuestro estado. Códigos vistos en el sandbox y en el
        /// manual (Get Shipping Tracking v2.0): 0000 manifestado, 0500 recogido (visto en prod 22/09), 0900 en tránsito, 1200 en delegación
        /// de destino, 1500 en reparto, 1600 entrega fallida, 2100 entregado, 3000 anulado. Para los
        /// que no conocemos se mira la descripción; si tampoco dice nada, sigue tramitado.
        /// OJO (23/09/26): "en tránsito/en reparto" es TRAMITADO para nosotros. EstadoEnvioSeguimiento.EnCurso
        /// vale 0 = "En curso" de EnviosAgencia (etiqueta SIN tramitar): el 22/09 el poll devolvió los 20
        /// primeros envíos de CTT a la pestaña En curso al leer "ENVÍO RECOGIDO". El texto va en el detalle.
        /// </summary>
        internal static string[] CODIGOS_CONOCIDOS => ESTADOS_CTT.Keys.ToArray();

        /// <summary>
        /// NestoAPI#493: la tabla OFICIAL de estados de CTT (fichero STATUS_INCIDENTS_MANAGEMENTS que
        /// mandó CTT el 24/09/26, hoja STATUS), código → nuestro estado y su descripción. Es la única
        /// fuente de CODIGOS_CONOCIDOS, DESCRIPCIONES y Traducir.
        /// Finales según CTT: 2100, 2110 (entregado), 2500 (devolución) y 2600 (reexpedición).
        /// Tránsito y esperas normales = Tramitado (NUNCA EnCurso: incidente del 22/09). Lo que pide que
        /// alguien mire el envío (fallidos, estacionados, parciales, mal transitados…) = Incidentado.
        /// </summary>
        internal static readonly Dictionary<string, (EstadoEnvioSeguimiento Estado, string Descripcion)> ESTADOS_CTT =
            new Dictionary<string, (EstadoEnvioSeguimiento, string)>
            {
                ["0000"] = (EstadoEnvioSeguimiento.Tramitado, "MANIFESTADO O GRABADO"),
                ["0010"] = (EstadoEnvioSeguimiento.Tramitado, "RECEPCIÓN PROVISIONAL"),
                ["0020"] = (EstadoEnvioSeguimiento.Tramitado, "PENDIENTE DE DEPOSITAR EN PUNTO CTT"),
                ["0030"] = (EstadoEnvioSeguimiento.Tramitado, "DEPOSITADO EN PUNTO PENDIENTE DE RECOGER"),
                ["0300"] = (EstadoEnvioSeguimiento.Tramitado, "RECOGIDA ASIGNADA"),
                ["0400"] = (EstadoEnvioSeguimiento.Incidentado, "RECOGIDA ANULADA"),
                ["0500"] = (EstadoEnvioSeguimiento.Tramitado, "ENVÍO RECOGIDO"),
                ["0600"] = (EstadoEnvioSeguimiento.Incidentado, "RECOGIDA FALLIDA"),
                ["0700"] = (EstadoEnvioSeguimiento.Tramitado, "DELEGACIÓN DE ORIGEN"),
                ["0701"] = (EstadoEnvioSeguimiento.Incidentado, "MERCANCÍA NO ENLAZADA EN ORIGEN"),
                ["0900"] = (EstadoEnvioSeguimiento.Tramitado, "EN TRÁNSITO"),
                ["1000"] = (EstadoEnvioSeguimiento.Tramitado, "DELEGACIÓN DE TRÁNSITO"),
                ["1001"] = (EstadoEnvioSeguimiento.Incidentado, "MERCANCÍA NO ENLAZADA EN CRUCE"),
                ["1100"] = (EstadoEnvioSeguimiento.Incidentado, "MAL TRANSITADO"),
                ["1200"] = (EstadoEnvioSeguimiento.Tramitado, "DELEGACIÓN DESTINO"),
                ["1500"] = (EstadoEnvioSeguimiento.Tramitado, "EN REPARTO"),
                ["1600"] = (EstadoEnvioSeguimiento.Incidentado, "REPARTO FALLIDO"),
                ["1700"] = (EstadoEnvioSeguimiento.Incidentado, "ENVÍO ESTACIONADO"),
                ["1800"] = (EstadoEnvioSeguimiento.Incidentado, "ESTACIONADO UBICADO"),
                ["1900"] = (EstadoEnvioSeguimiento.Tramitado, "PENDIENTE DE EXTRACCIÓN"),
                ["2100"] = (EstadoEnvioSeguimiento.Entregado, "ENTREGADO"),
                ["2110"] = (EstadoEnvioSeguimiento.Entregado, "ENTREGADO ADMINISTRATIVO"),
                ["2200"] = (EstadoEnvioSeguimiento.Incidentado, "ENTREGA PARCIAL"),
                ["2310"] = (EstadoEnvioSeguimiento.Tramitado, "DISPONIBLE EN PUNTO CTT PARA ENTREGA"),
                // 24/09/26 (CTT): el cliente estaba ausente y sale de nuevo a reparto
                ["2400"] = (EstadoEnvioSeguimiento.Tramitado, "NUEVO REPARTO"),
                ["2500"] = (EstadoEnvioSeguimiento.Devuelto, "DEVOLUCIÓN"),
                // Final para este albarán: sigue con otro código de envío, que hay que mirar
                ["2600"] = (EstadoEnvioSeguimiento.Incidentado, "REEXPEDICIÓN"),
                ["2700"] = (EstadoEnvioSeguimiento.Incidentado, "ENTREGADO ALMACÉN REGULADOR"),
                ["2900"] = (EstadoEnvioSeguimiento.Incidentado, "RECOGER EN DELEGACIÓN"),
                ["3000"] = (EstadoEnvioSeguimiento.Desconocido, "ENVÍO ANULADO"),
                ["3900"] = (EstadoEnvioSeguimiento.Tramitado, "TRÁNSITO INTERNACIONAL"),
                ["3901"] = (EstadoEnvioSeguimiento.Tramitado, "GESTIÓN ADUANERA"),
                ["3902"] = (EstadoEnvioSeguimiento.Tramitado, "DESPACHADO"),
                ["3903"] = (EstadoEnvioSeguimiento.Tramitado, "REVISIÓN ADUANERA"),
                ["3904"] = (EstadoEnvioSeguimiento.Tramitado, "INSPECCIÓN ADUANERA")
            };

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
            return Traducir(codigo, descripcion, incidencia, fecha, albaran);
        }

        /// <summary>
        /// Un registro del listado por fechas (web-tracking). Trae el código del último estado pero no su
        /// descripción: se usa la de <see cref="DESCRIPCIONES"/> para el detalle. La traducción a nuestro
        /// estado es la MISMA que la del seguimiento individual (<see cref="Traducir"/>).
        /// </summary>
        internal static SeguimientoEnvioRemoto InterpretarRegistroPorFechas(JToken registro)
        {
            string codigo = (registro?["shipping_status_code"]?.ToString() ?? string.Empty).Trim();
            string albaran = registro?["shipping_code"]?.ToString()?.Trim();
            if (codigo.Length == 0)
            {
                return new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Desconocido, Detalle = "CTT no devuelve el estado del envío " + albaran };
            }
            string descripcion = (registro["shipping_status_desc"]?.ToString()
                ?? registro["shipping_status_description"]?.ToString()
                ?? (DESCRIPCIONES.TryGetValue(codigo, out string conocida) ? conocida : string.Empty)).Trim();
            string incidencia = registro["incident_type_desc"]?.ToString();
            DateTime? fecha = LeerFecha(registro["shipping_status_datetime"]?.ToString());
            return Traducir(codigo, descripcion, incidencia, fecha, albaran);
        }

        /// <summary>Descripción de los códigos conocidos, para el listado por fechas (que no la trae).</summary>
        internal static readonly Dictionary<string, string> DESCRIPCIONES =
            ESTADOS_CTT.ToDictionary(e => e.Key, e => e.Value.Descripcion);

        /// <summary>
        /// Código de estado de CTT → nuestro estado. ÚNICA tabla para los dos caminos (seguimiento de un
        /// envío y listado por fechas), para que un código signifique siempre lo mismo.
        /// </summary>
        internal static SeguimientoEnvioRemoto Traducir(string codigo, string descripcion, string incidencia, DateTime? fecha, string albaran)
        {
            codigo = (codigo ?? string.Empty).Trim();
            descripcion = (descripcion ?? string.Empty).Trim();
            string detalle = string.IsNullOrWhiteSpace(incidencia) ? descripcion : $"{descripcion}: {incidencia}";

            EstadoEnvioSeguimiento estado;
            if (ESTADOS_CTT.TryGetValue(codigo, out var conocido))
            {
                estado = conocido.Estado;
                if (string.IsNullOrWhiteSpace(descripcion))
                {
                    // El 2400 llegó sin descripción: el detalle sale de la tabla oficial
                    detalle = string.IsNullOrWhiteSpace(incidencia) ? conocido.Descripcion : $"{conocido.Descripcion}: {incidencia}";
                }
                if (codigo == "3000")
                {
                    detalle = "Anulado en CTT";
                }
            }
            else
            {
                estado = EstadoDesdeDescripcion(descripcion);
                LoguearCodigoNoContemplado(codigo, descripcion, albaran);
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
