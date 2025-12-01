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
        {
            _apiKey = ConfigurationManager.AppSettings["Verifacti:ApiKey"] ?? "";
            _baseUrl = ConfigurationManager.AppSettings["Verifacti:BaseUrl"] ?? "https://api.verifacti.com/";
            _habilitado = bool.TryParse(ConfigurationManager.AppSettings["Verifacti:Habilitado"], out var hab) && hab;
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
            if (!EstaHabilitado)
            {
                return new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = "El servicio Verifacti no está habilitado o no tiene API key configurada",
                    CodigoError = "SERVICIO_NO_HABILITADO"
                };
            }

            try
            {
                var request = MapearAVerifactiRequest(factura);
                var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("verifactu/create", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonConvert.DeserializeObject<VerifactiApiResponse>(responseBody);
                    return MapearDesdeVerifactiResponse(apiResponse);
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
                var response = await _httpClient.GetAsync($"verifactu/status/{uuid}");
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonConvert.DeserializeObject<VerifactiApiResponse>(responseBody);
                    return MapearDesdeVerifactiResponse(apiResponse);
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
                    return MapearDesdeVerifactiResponse(apiResponse);
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
                Nif = factura.NifDestinatario,
                Nombre = factura.NombreDestinatario,
                ImporteTotal = factura.ImporteTotal,
                Lineas = factura.DesgloseIva.Select(d => new VerifactiLineaRequest
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
        /// Mapea de la respuesta de Verifacti al DTO genérico
        /// </summary>
        private VerifactuResponse MapearDesdeVerifactiResponse(VerifactiApiResponse apiResponse)
        {
            return new VerifactuResponse
            {
                Exitoso = apiResponse.Success,
                Uuid = apiResponse.Uuid,
                Estado = apiResponse.Estado,
                Url = apiResponse.Url,
                QrBase64 = apiResponse.Qr,
                Huella = apiResponse.Huella,
                MensajeError = apiResponse.Error ?? apiResponse.Message,
                CodigoError = apiResponse.ErrorCode
            };
        }

        #endregion
    }
}
