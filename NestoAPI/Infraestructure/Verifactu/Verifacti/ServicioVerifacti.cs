using System;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Verifactu.Verifacti
{
    /// <summary>
    /// Implementación del servicio Verifactu usando el proveedor Verifacti.
    /// https://api.verifacti.com/
    /// </summary>
    public class ServicioVerifacti : IServicioVerifactu
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly bool _habilitado;
        private readonly bool _esSandbox;

        public string NombreProveedor => "Verifacti";
        public bool EstaHabilitado => _habilitado && !string.IsNullOrEmpty(_apiKey);
        public bool EsSandbox => _esSandbox;

        /// <summary>
        /// Constructor que lee la configuración de Web.config
        /// </summary>
        public ServicioVerifacti() : this(null)
        {
        }

        /// <summary>
        /// Constructor con HttpClient inyectado (para testing)
        /// </summary>
        /// <param name="httpClient">HttpClient a usar, o null para crear uno nuevo</param>
        public ServicioVerifacti(HttpClient httpClient)
            : this(
                  httpClient,
                  ConfigurationManager.AppSettings["Verifacti:ApiKey"] ?? "",
                  ConfigurationManager.AppSettings["Verifacti:BaseUrl"] ?? "https://api.verifacti.com/",
                  bool.TryParse(ConfigurationManager.AppSettings["Verifacti:Habilitado"], out var hab) && hab)
        {
        }

        /// <summary>
        /// Constructor con configuración explícita (para tests, sin depender del Web.config)
        /// </summary>
        internal ServicioVerifacti(HttpClient httpClient, string apiKey, string baseUrl, bool habilitado)
        {
            _apiKey = apiKey ?? "";
            _baseUrl = baseUrl;
            _habilitado = habilitado;
            _esSandbox = _apiKey.StartsWith("vf_test_", StringComparison.OrdinalIgnoreCase);

            _httpClient = httpClient ?? new HttpClient();
            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Envía una factura a Verifacti
        /// </summary>
        public async Task<VerifactuResponse> EnviarFacturaAsync(VerifactuFacturaRequest factura)
        {
            return await EnviarRegistroAsync(factura, rechazoPrevio: null);
        }

        /// <summary>
        /// NestoAPI#326: QR local. El numserie de Verifacti es {prefijo}{Serie}{Numero} (Verifacti
        /// antepone un prefijo por emisor, p.ej. "8f44_"). El NIF con el que registra y el prefijo
        /// dependen del proveedor y del entorno (sandbox/prod), así que se leen de la config de
        /// Verifacti. Sin ellos configurados NO se genera QR local (fallback = comportamiento actual).
        /// </summary>
        public DatosQrLocalVerifactu GenerarQrLocal(VerifactuFacturaRequest factura)
            => GenerarQrLocal(factura,
                ConfigurationManager.AppSettings["Verifacti:NifEmisor"],
                ConfigurationManager.AppSettings["Verifacti:PrefijoNumSerie"],
                EsSandbox);

        // Núcleo testeable sin depender del Web.config: forma el numserie de Verifacti y delega en
        // el generador de QR común (URL de la AEAT + imagen).
        internal static DatosQrLocalVerifactu GenerarQrLocal(VerifactuFacturaRequest factura, string nifEmisor,
            string prefijoNumSerie, bool esSandbox)
        {
            if (factura == null || string.IsNullOrWhiteSpace(nifEmisor) || string.IsNullOrWhiteSpace(prefijoNumSerie))
            {
                return null;
            }
            string numSerie = prefijoNumSerie.Trim()
                + (factura.Serie?.Trim() ?? string.Empty)
                + (factura.Numero?.Trim() ?? string.Empty);
            string url = GeneradorQrVerifactu.ConstruirUrlValidacion(
                nifEmisor, numSerie, factura.FechaExpedicion, factura.ImporteTotal, esSandbox);
            return new DatosQrLocalVerifactu
            {
                Url = url,
                ImagenPngQr = GeneradorQrVerifactu.GenerarPngQr(url)
            };
        }

        /// <summary>
        /// NestoAPI#346: subsana una factura vía PUT verifactu/modify. Es el camino legal para
        /// declarar fuera de plazo: el create de Verifacti exige fecha_expedicion actual, pero el
        /// modify admite fechas pasadas (subsanación, sin plazo máximo según la AEAT).
        /// </summary>
        /// <param name="rechazoPrevio">"N" = subsanar un registro aceptado; "X" = el alta inicial
        /// fue rechazada por la AEAT; "S" = una subsanación anterior fue rechazada.</param>
        public async Task<VerifactuResponse> ModificarFacturaAsync(VerifactuFacturaRequest factura, string rechazoPrevio)
        {
            return await EnviarRegistroAsync(factura, rechazoPrevio ?? "N");
        }

        private async Task<VerifactuResponse> EnviarRegistroAsync(VerifactuFacturaRequest factura, string rechazoPrevio)
        {
            if (!EstaHabilitado)
            {
                return new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = "El servicio Verifacti no está habilitado o no tiene API key configurada",
                    CodigoError = "SERVICIO_NO_HABILITADO"
                };
            }

            bool esSubsanacion = rechazoPrevio != null;
            try
            {
                var request = MapearAVerifactiRequest(factura);
                request.RechazoPrevio = rechazoPrevio;
                var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = esSubsanacion
                    ? await _httpClient.PutAsync("verifactu/modify", content)
                    : await _httpClient.PostAsync("verifactu/create", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonConvert.DeserializeObject<VerifactiApiResponse>(responseBody);
                    return MapearDesdeVerifactiResponse(apiResponse, httpExitoso: true);
                }
                else
                {
                    // Intentar parsear error de la API
                    try
                    {
                        var errorResponse = JsonConvert.DeserializeObject<VerifactiApiResponse>(responseBody);
                        return new VerifactuResponse
                        {
                            Exitoso = false,
                            MensajeError = errorResponse?.Error ?? errorResponse?.Message ?? $"Error HTTP {(int)response.StatusCode}",
                            CodigoError = errorResponse?.ErrorCode ?? response.StatusCode.ToString()
                        };
                    }
                    catch
                    {
                        return new VerifactuResponse
                        {
                            Exitoso = false,
                            MensajeError = $"Error HTTP {(int)response.StatusCode}: {responseBody}",
                            CodigoError = response.StatusCode.ToString()
                        };
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                return new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = $"Error de conexión con Verifacti: {ex.Message}",
                    CodigoError = "ERROR_CONEXION"
                };
            }
            catch (TaskCanceledException ex)
            {
                return new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = $"Timeout al conectar con Verifacti: {ex.Message}",
                    CodigoError = "TIMEOUT"
                };
            }
            catch (Exception ex)
            {
                return new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = $"Error inesperado al enviar factura: {ex.Message}",
                    CodigoError = "ERROR_INESPERADO"
                };
            }
        }

        /// <summary>
        /// Consulta el estado de una factura en Verifacti
        /// </summary>
        public async Task<VerifactuResponse> ConsultarEstadoAsync(string uuid)
        {
            if (!EstaHabilitado)
            {
                return new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = "El servicio Verifacti no está habilitado",
                    CodigoError = "SERVICIO_NO_HABILITADO"
                };
            }

            try
            {
                // La API real es query string (verifactu/status/{uuid} devuelve 404 de ruta;
                // verificado contra el sandbox el 17/07/26)
                var response = await _httpClient.GetAsync($"verifactu/status?uuid={Uri.EscapeDataString(uuid)}");
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonConvert.DeserializeObject<VerifactiApiResponse>(responseBody);
                    return MapearDesdeVerifactiResponse(apiResponse, httpExitoso: true);
                }
                else
                {
                    return new VerifactuResponse
                    {
                        Exitoso = false,
                        MensajeError = $"Error al consultar estado: HTTP {(int)response.StatusCode}",
                        CodigoError = response.StatusCode.ToString()
                    };
                }
            }
            catch (Exception ex)
            {
                return new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = $"Error al consultar estado: {ex.Message}",
                    CodigoError = "ERROR_CONSULTA"
                };
            }
        }

        /// <summary>
        /// Envía un registro de anulación a la AEAT a través de Verifacti
        /// </summary>
        public async Task<VerifactuResponse> AnularFacturaAsync(string serie, string numero, DateTime fechaExpedicion,
            bool rechazoPrevio = false, bool sinRegistroPrevio = false)
        {
            if (!EstaHabilitado)
            {
                return new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = "El servicio Verifacti no está habilitado",
                    CodigoError = "SERVICIO_NO_HABILITADO"
                };
            }

            try
            {
                var request = new
                {
                    serie = serie ?? "",
                    numero = numero,
                    fecha_expedicion = fechaExpedicion.ToString("dd-MM-yyyy"),
                    rechazo_previo = rechazoPrevio ? "S" : "N",
                    sin_registro_previo = sinRegistroPrevio ? "S" : "N"
                };
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("verifactu/cancel", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonConvert.DeserializeObject<VerifactiApiResponse>(responseBody);
                    return MapearDesdeVerifactiResponse(apiResponse, httpExitoso: true);
                }
                else
                {
                    return new VerifactuResponse
                    {
                        Exitoso = false,
                        MensajeError = $"Error al anular factura: HTTP {(int)response.StatusCode}",
                        CodigoError = response.StatusCode.ToString()
                    };
                }
            }
            catch (Exception ex)
            {
                return new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = $"Error al anular factura: {ex.Message}",
                    CodigoError = "ERROR_ANULACION"
                };
            }
        }

        #region Métodos de mapeo privados

        /// <summary>
        /// Mapea del DTO genérico al formato específico de Verifacti
        /// </summary>
        private VerifactiCreateRequest MapearAVerifactiRequest(VerifactuFacturaRequest factura)
        {
            var request = new VerifactiCreateRequest
            {
                Serie = factura.Serie,
                Numero = factura.Numero,
                FechaExpedicion = factura.FechaExpedicion.ToString("dd-MM-yyyy"),
                TipoFactura = factura.TipoFactura,
                Descripcion = factura.Descripcion?.Length > 500
                    ? factura.Descripcion.Substring(0, 500)
                    : factura.Descripcion,
                // NestoAPI#339: con identificación extranjera va id_otro y NUNCA nif
                Nif = factura.IdOtro == null ? factura.NifDestinatario : null,
                IdOtro = factura.IdOtro == null ? null : new VerifactiIdOtroRequest
                {
                    CodigoPais = factura.IdOtro.CodigoPais,
                    IdType = factura.IdOtro.IdType,
                    Id = factura.IdOtro.Id
                },
                Nombre = factura.NombreDestinatario,
                ImporteTotal = factura.ImporteTotal,
                // NestoAPI#347: una línea OSS (CalificacionOperacion=N2) lleva solo base +
                // clave_regimen + calificacion_operacion — informar tipo o cuota es rechazo AEAT
                Lineas = factura.DesgloseIva.Select(d => d.CalificacionOperacion != null
                    ? new VerifactiLineaRequest
                    {
                        Base = d.BaseImponible,
                        ClaveRegimen = d.ClaveRegimen,
                        CalificacionOperacion = d.CalificacionOperacion
                    }
                    : new VerifactiLineaRequest
                    {
                        Base = d.BaseImponible,
                        Tipo = d.TipoIva,
                        Cuota = d.CuotaIva,
                        TipoRe = d.TipoRecargoEquivalencia > 0 ? d.TipoRecargoEquivalencia : (decimal?)null,
                        CuotaRe = d.CuotaRecargoEquivalencia != 0 ? d.CuotaRecargoEquivalencia : (decimal?)null
                    }).ToList()
            };

            // Si es rectificativa, añadir datos específicos
            if (factura.TipoFactura?.StartsWith("R") == true)
            {
                request.TipoRectificativa = factura.TipoRectificacion ?? "S";

                if (factura.FacturasRectificadas?.Any() == true)
                {
                    request.FacturasRectificadas = factura.FacturasRectificadas
                        .Select(f => new VerifactiFacturaRectificadaRequest
                        {
                            Serie = f.Serie,
                            Numero = f.Numero,
                            FechaExpedicion = f.FechaExpedicion.ToString("dd-MM-yyyy")
                        }).ToList();
                }
            }

            return request;
        }

        /// <summary>
        /// Mapea de la respuesta de Verifacti al DTO genérico.
        /// La respuesta real de éxito NO trae campo "success" (verificado contra el sandbox
        /// el 17/07/26): el éxito lo marca el HTTP 200 + ausencia de "error".
        /// </summary>
        private VerifactuResponse MapearDesdeVerifactiResponse(VerifactiApiResponse apiResponse, bool httpExitoso)
        {
            return new VerifactuResponse
            {
                Exitoso = httpExitoso && string.IsNullOrEmpty(apiResponse.Error),
                Uuid = apiResponse.Uuid,
                Estado = apiResponse.Estado,
                Url = apiResponse.Url,
                QrBase64 = apiResponse.Qr,
                Huella = apiResponse.Huella,
                // NestoAPI#329: el status trae el veredicto AEAT en codigo_error/mensaje_error;
                // se prefieren sobre los errores genéricos de la API de Verifacti.
                MensajeError = apiResponse.MensajeErrorAeat ?? apiResponse.Error ?? apiResponse.Message,
                CodigoError = apiResponse.CodigoErrorAeat ?? apiResponse.ErrorCode
            };
        }

        #endregion
    }
}
