using NestoAPI.Models.Pagos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RedsysAPIPrj;
using System;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Infraestructure.Pagos
{
    public class RedsysService : IRedsysService
    {
        private readonly string _secretKey;
        private readonly string _merchantCode;
        private readonly bool _modoPruebas;

        public RedsysService()
            : this(
                ConfigurationManager.AppSettings["RedsysSHA256"],
                Redsys.MERCHANT_CODE,
                false)
        {
        }

        internal RedsysService(string secretKey, string merchantCode, bool modoPruebas)
        {
            _secretKey = secretKey;
            _merchantCode = merchantCode;
            _modoPruebas = modoPruebas;
        }

        public string UrlFormularioRedsys
        {
            get
            {
                return _modoPruebas
                    ? "https://sis-t.redsys.es/sis/realizarPago"
                    : "https://sis.redsys.es/sis/realizarPago";
            }
        }

        private Uri UrlRedsysREST
        {
            get
            {
                return _modoPruebas
                    ? new Uri("https://sis-t.redsys.es:25443/sis/rest/trataPeticionREST")
                    : new Uri("https://sis.redsys.es/sis/rest/trataPeticionREST");
            }
        }

        public string GenerarNumeroPedido(string sufijo = null)
        {
            var ticks = new DateTime(2020, 1, 1).Ticks;
            var ans = DateTime.Now.Ticks - ticks;

            if (string.IsNullOrEmpty(sufijo))
            {
                return ans.ToString("X12").Substring(0, 12);
            }

            string hexPart = ans.ToString("X12");
            string combined = hexPart + sufijo;
            // Redsys requiere exactamente 12 caracteres
            return combined.Length > 12
                ? combined.Substring(combined.Length - 12)
                : combined.PadLeft(12, '0');
        }

        public ParametrosRedsysFirmados CrearParametrosP2F(decimal importe, string correo,
            string movil, string textoSMS, string cliente, FormatoCorreoReclamacion datosCorreo)
        {
            string numeroOrden = GenerarNumeroPedido("C" + cliente);

            RedsysAPI r = new RedsysAPI();
            r.SetParameter("DS_MERCHANT_AMOUNT", ((int)(importe * 100)).ToString());
            r.SetParameter("DS_MERCHANT_ORDER", numeroOrden);
            r.SetParameter("DS_MERCHANT_MERCHANTCODE", _merchantCode);
            r.SetParameter("DS_MERCHANT_CURRENCY", "978");
            r.SetParameter("DS_MERCHANT_TRANSACTIONTYPE", "F");
            r.SetParameter("DS_MERCHANT_TERMINAL", Redsys.TERMINAL_P2F);
            r.SetParameter("DS_MERCHANT_MERCHANTURL", "http://www.nuevavision.es");
            r.SetParameter("DS_MERCHANT_URLOK", "");
            r.SetParameter("DS_MERCHANT_URLKO", "");
            r.SetParameter("DS_MERCHANT_CUSTOMER_MOBILE", movil);
            r.SetParameter("DS_MERCHANT_CUSTOMER_MAIL", correo);
            r.SetParameter("DS_MERCHANT_P2F_EXPIRIDATE", (60 * 24 * 7).ToString());
            r.SetParameter("DS_MERCHANT_CUSTOMER_SMS_TEXT", textoSMS);

            if (datosCorreo != null)
            {
                r.SetParameter("DS_MERCHANT_P2F_XMLDATA", datosCorreo.ToXML());
            }

            string parametros = r.createMerchantParameters();
            string firma = r.createMerchantSignature(_secretKey);

            return new ParametrosRedsysFirmados
            {
                Ds_SignatureVersion = "HMAC_SHA256_V1",
                Ds_MerchantParameters = parametros,
                Ds_Signature = firma,
                UrlRedsys = UrlRedsysREST,
                NumeroOrden = numeroOrden
            };
        }

        public ParametrosRedsysFirmados CrearParametrosTPVVirtual(decimal importe, string descripcion,
            string correo, string urlNotificacion, string urlOk, string urlKo)
        {
            string numeroOrden = GenerarNumeroPedido();

            RedsysAPI r = new RedsysAPI();
            r.SetParameter("DS_MERCHANT_AMOUNT", ((int)(importe * 100)).ToString());
            r.SetParameter("DS_MERCHANT_ORDER", numeroOrden);
            r.SetParameter("DS_MERCHANT_MERCHANTCODE", _merchantCode);
            r.SetParameter("DS_MERCHANT_CURRENCY", "978");
            r.SetParameter("DS_MERCHANT_TRANSACTIONTYPE", "0");
            r.SetParameter("DS_MERCHANT_TERMINAL", Redsys.TERMINAL_TPV_VIRTUAL);
            r.SetParameter("DS_MERCHANT_MERCHANTURL", urlNotificacion ?? "");
            r.SetParameter("DS_MERCHANT_URLOK", urlOk ?? "");
            r.SetParameter("DS_MERCHANT_URLKO", urlKo ?? "");
            r.SetParameter("DS_MERCHANT_CUSTOMER_MAIL", correo ?? "");

            if (!string.IsNullOrWhiteSpace(descripcion))
            {
                r.SetParameter("DS_MERCHANT_PRODUCTDESCRIPTION", descripcion);
            }

            string parametros = r.createMerchantParameters();
            string firma = r.createMerchantSignature(_secretKey);

            return new ParametrosRedsysFirmados
            {
                Ds_SignatureVersion = "HMAC_SHA256_V1",
                Ds_MerchantParameters = parametros,
                Ds_Signature = firma,
                UrlRedsys = new Uri(UrlFormularioRedsys),
                NumeroOrden = numeroOrden
            };
        }

        public async Task<RespuestaRedsys> EnviarPeticionREST(ParametrosRedsysFirmados parametros)
        {
            var peticion = new PeticionRedsys
            {
                Ds_MerchantParameters = parametros.Ds_MerchantParameters,
                Ds_Signature = parametros.Ds_Signature
            };

            using (HttpClient client = new HttpClient())
            {
                string peticionJson = JsonConvert.SerializeObject(peticion);
                HttpContent content = new StringContent(peticionJson, Encoding.UTF8, "application/json");
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                HttpResponseMessage response = await client.PostAsync(parametros.UrlRedsys, content).ConfigureAwait(false);
                content.Dispose();

                if (response.IsSuccessStatusCode)
                {
                    string resultado = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    PeticionRedsys respuestaPeticion = JsonConvert.DeserializeObject<PeticionRedsys>(resultado);
                    string resultadoDecodificado = DecodificarParametrosInterno(respuestaPeticion.Ds_MerchantParameters);
                    return JsonConvert.DeserializeObject<RespuestaRedsys>(resultadoDecodificado);
                }
                else
                {
                    string textoError = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    JObject requestException = JsonConvert.DeserializeObject<JObject>(textoError);

                    string errorMostrar = "No se ha podido enviar la petición al servidor de Redsys" + "\n";
                    if (requestException["exceptionMessage"] != null)
                    {
                        errorMostrar += requestException["exceptionMessage"] + "\n";
                    }
                    if (requestException["ModelState"] != null)
                    {
                        var firstError = requestException["ModelState"];
                        var nodoError = firstError.LastOrDefault();
                        errorMostrar += nodoError.FirstOrDefault()[0];
                    }
                    var innerException = requestException["InnerException"];
                    while (innerException != null)
                    {
                        errorMostrar += "\n" + innerException["ExceptionMessage"];
                        innerException = innerException["InnerException"];
                    }
                    throw new Exception(errorMostrar);
                }
            }
        }

        public RespuestaRedsys DecodificarParametros(string merchantParametersBase64)
        {
            string decoded = DecodificarParametrosInterno(merchantParametersBase64);
            return JsonConvert.DeserializeObject<RespuestaRedsys>(decoded);
        }

        public ResultadoValidacionNotificacion ValidarNotificacion(NotificacionRedsys notificacion)
        {
            RedsysAPI r = new RedsysAPI();
            string expectedSignature = r.createMerchantSignatureNotif(_secretKey, notificacion.Ds_MerchantParameters);

            bool firmaValida = string.Equals(expectedSignature, notificacion.Ds_Signature, StringComparison.OrdinalIgnoreCase);

            if (!firmaValida)
            {
                return new ResultadoValidacionNotificacion
                {
                    FirmaValida = false,
                    PagoAutorizado = false,
                    MensajeError = "Firma de notificación inválida"
                };
            }

            string decoded = DecodificarParametrosInterno(notificacion.Ds_MerchantParameters);
            RespuestaRedsys respuesta = JsonConvert.DeserializeObject<RespuestaRedsys>(decoded);

            int codigoRespuesta;
            bool pagoAutorizado = int.TryParse(respuesta.Ds_Response, out codigoRespuesta)
                && codigoRespuesta >= 0
                && codigoRespuesta <= 99;

            return new ResultadoValidacionNotificacion
            {
                FirmaValida = true,
                PagoAutorizado = pagoAutorizado,
                CodigoRespuesta = respuesta.Ds_Response,
                CodigoAutorizacion = respuesta.Ds_AuthorisationCode,
                NumeroOrden = respuesta.Ds_Order
            };
        }

        private string DecodificarParametrosInterno(string parametros)
        {
            RedsysAPI r = new RedsysAPI();
            return r.decodeMerchantParameters(parametros);
        }
    }
}
