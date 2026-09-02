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
using System.Web;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Infraestructure.Pagos
{
    public class RedsysService : IRedsysService
    {
        private readonly string _secretKeyP2F;
        private readonly string _secretKeyTPVVirtual;
        private readonly string _merchantCode;
        private readonly bool _modoPruebas;

        public RedsysService()
            : this(
                ConfigurationManager.AppSettings["RedsysSHA256"],
                ConfigurationManager.AppSettings["RedsysSHA256Terminal1"],
                Redsys.MERCHANT_CODE,
                false)
        {
        }

        internal RedsysService(string secretKeyP2F, string secretKeyTPVVirtual, string merchantCode, bool modoPruebas)
        {
            _secretKeyP2F = secretKeyP2F;
            _secretKeyTPVVirtual = secretKeyTPVVirtual;
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
            string firma = r.createMerchantSignature(_secretKeyP2F);

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
            string correo, string cliente, string urlNotificacion, string urlOk, string urlKo,
            string metodoPago = null, string numeroOrdenExistente = null, bool solicitarToken = false)
        {
            string numeroOrden = !string.IsNullOrWhiteSpace(numeroOrdenExistente)
                ? numeroOrdenExistente
                : GenerarNumeroPedido(string.IsNullOrWhiteSpace(cliente) ? null : "C" + cliente.Trim());

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

            // Issue #140: DS_MERCHANT_PAYMETHODS controla qué método muestra Redsys.
            // "C" = solo tarjeta, "z" = solo Bizum. No se pueden combinar.
            if (!string.IsNullOrWhiteSpace(metodoPago))
            {
                r.SetParameter("DS_MERCHANT_PAYMETHODS", metodoPago);
            }

            // NestoAPI#178: pedir a Redsys que tokenice la tarjeta. La notificación del pago
            // autorizado devuelve Ds_Merchant_Identifier, con el que se puede cobrar al cliente
            // en el futuro sin que vuelva a meter la tarjeta. COF_INI=S marca este pago como el
            // inicial de una credencial en fichero (PSD2): la autenticación fuerte de ESTE pago
            // ampara los cobros con token posteriores.
            if (solicitarToken)
            {
                r.SetParameter("DS_MERCHANT_IDENTIFIER", "REQUIRED");
                r.SetParameter("DS_MERCHANT_COF_INI", "S");
                r.SetParameter("DS_MERCHANT_COF_TYPE", "C");
            }

            string parametros = r.createMerchantParameters();
            string firma = r.createMerchantSignature(_secretKeyTPVVirtual);

            return new ParametrosRedsysFirmados
            {
                Ds_SignatureVersion = "HMAC_SHA256_V1",
                Ds_MerchantParameters = parametros,
                Ds_Signature = firma,
                UrlRedsys = new Uri(UrlFormularioRedsys),
                NumeroOrden = numeroOrden
            };
        }

        /// <summary>
        /// NestoAPI#178/#181: cobro directo con una tarjeta guardada (token de Redsys), sin que
        /// el cliente meta la tarjeta. Va por el canal REST (trataPeticionREST) y la respuesta es
        /// síncrona: el que llama sabe en el momento si el cobro se ha autorizado o no.
        ///
        /// <para>COF_INI=N + COF_TXNID enlazan el cobro con el pago inicial que dio de alta la
        /// credencial (donde el cliente sí se autenticó), y EXCEP_SCA=MIT es la exención PSD2
        /// para cobros sobre credencial en fichero.</para>
        /// </summary>
        public ParametrosRedsysFirmados CrearParametrosCobroConToken(decimal importe,
            string descripcion, string cliente, string tokenTarjeta, string cofTxnId)
        {
            string numeroOrden = GenerarNumeroPedido(
                string.IsNullOrWhiteSpace(cliente) ? null : "C" + cliente.Trim());

            RedsysAPI r = new RedsysAPI();
            r.SetParameter("DS_MERCHANT_AMOUNT", ((int)(importe * 100)).ToString());
            r.SetParameter("DS_MERCHANT_ORDER", numeroOrden);
            r.SetParameter("DS_MERCHANT_MERCHANTCODE", _merchantCode);
            r.SetParameter("DS_MERCHANT_CURRENCY", "978");
            r.SetParameter("DS_MERCHANT_TRANSACTIONTYPE", "0");
            r.SetParameter("DS_MERCHANT_TERMINAL", Redsys.TERMINAL_TPV_VIRTUAL);
            r.SetParameter("DS_MERCHANT_IDENTIFIER", tokenTarjeta);
            r.SetParameter("DS_MERCHANT_DIRECTPAYMENT", "true");
            r.SetParameter("DS_MERCHANT_COF_INI", "N");
            r.SetParameter("DS_MERCHANT_COF_TYPE", "C");
            r.SetParameter("DS_MERCHANT_EXCEP_SCA", "MIT");

            if (!string.IsNullOrWhiteSpace(cofTxnId))
            {
                r.SetParameter("DS_MERCHANT_COF_TXNID", cofTxnId);
            }
            if (!string.IsNullOrWhiteSpace(descripcion))
            {
                r.SetParameter("DS_MERCHANT_PRODUCTDESCRIPTION", descripcion);
            }

            string parametros = r.createMerchantParameters();
            string firma = r.createMerchantSignature(_secretKeyTPVVirtual);

            return new ParametrosRedsysFirmados
            {
                Ds_SignatureVersion = "HMAC_SHA256_V1",
                Ds_MerchantParameters = parametros,
                Ds_Signature = firma,
                UrlRedsys = UrlRedsysREST,
                NumeroOrden = numeroOrden
            };
        }

        /// <summary>
        /// NestoAPI#178: devolución de un cobro hecho por REST (mismo número de orden). Se usa
        /// para deshacer el cobro con tarjeta guardada si el pedido no se llega a crear: dinero
        /// cobrado sin pedido es justo lo que este flujo promete que no puede pasar.
        /// </summary>
        public ParametrosRedsysFirmados CrearParametrosDevolucion(decimal importe, string numeroOrden)
        {
            RedsysAPI r = new RedsysAPI();
            r.SetParameter("DS_MERCHANT_AMOUNT", ((int)(importe * 100)).ToString());
            r.SetParameter("DS_MERCHANT_ORDER", numeroOrden);
            r.SetParameter("DS_MERCHANT_MERCHANTCODE", _merchantCode);
            r.SetParameter("DS_MERCHANT_CURRENCY", "978");
            r.SetParameter("DS_MERCHANT_TRANSACTIONTYPE", "3");
            r.SetParameter("DS_MERCHANT_TERMINAL", Redsys.TERMINAL_TPV_VIRTUAL);

            string parametros = r.createMerchantParameters();
            string firma = r.createMerchantSignature(_secretKeyTPVVirtual);

            return new ParametrosRedsysFirmados
            {
                Ds_SignatureVersion = "HMAC_SHA256_V1",
                Ds_MerchantParameters = parametros,
                Ds_Signature = firma,
                UrlRedsys = UrlRedsysREST,
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
            string decoded = DecodificarParametrosInterno(notificacion.Ds_MerchantParameters);
            RespuestaRedsys respuesta = JsonConvert.DeserializeObject<RespuestaRedsys>(decoded);

            // Redsys devuelve terminal con ceros (ej: "001"), comparar como entero
            int.TryParse(respuesta.Ds_Terminal?.Trim(), out int terminalRecibido);
            int.TryParse(Redsys.TERMINAL_TPV_VIRTUAL, out int terminalTPV);
            string secretKey = terminalRecibido == terminalTPV
                ? _secretKeyTPVVirtual
                : _secretKeyP2F;

            RedsysAPI r = new RedsysAPI();
            string expectedSignature = r.createMerchantSignatureNotif(secretKey, notificacion.Ds_MerchantParameters);

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
                NumeroOrden = respuesta.Ds_Order,
                // NestoAPI#178: si el pago se hizo con tokenización, aquí viene el token con el
                // que se podrá cobrar al cliente sin que vuelva a meter la tarjeta
                TokenTarjeta = respuesta.Ds_Merchant_Identifier,
                CofTxnId = respuesta.Ds_Merchant_Cof_Txnid,
                UltimosDigitosTarjeta = UltimosDigitosDe(respuesta),
                FechaCaducidadTarjeta = ParsearCaducidadRedsys(respuesta.Ds_ExpiryDate),
                MarcaTarjeta = NombreMarcaTarjeta(respuesta.Ds_Card_Brand),
                TipoTarjeta = respuesta.Ds_Card_Type?.Trim(),
                CamposRecibidos = NombresDeCampos(decoded)
            };
        }

        /// <summary>
        /// NestoAPI#178: los últimos dígitos con lo que haya mandado Redsys. Ds_Card_Last4 va
        /// primero (su documentación lo prefiere cuando viene); si no, el número enmascarado.
        /// Null si no viene ninguno — que es lo normal en nuestro terminal, porque el envío de
        /// datos de tarjeta lo activa el banco y no lo tenemos.
        /// </summary>
        internal static string UltimosDigitosDe(RespuestaRedsys respuesta)
        {
            if (respuesta == null)
            {
                return null;
            }
            return ExtraerUltimosDigitos(respuesta.Ds_Card_Last4)
                ?? ExtraerUltimosDigitos(respuesta.Ds_Card_Number);
        }

        /// <summary>
        /// Solo los NOMBRES de los campos del JSON de la notificación, separados por comas.
        /// Nunca los valores: ahí van el token y el PAN enmascarado.
        /// </summary>
        internal static string NombresDeCampos(string jsonNotificacion)
        {
            if (string.IsNullOrWhiteSpace(jsonNotificacion))
            {
                return null;
            }
            try
            {
                return string.Join(", ", JObject.Parse(jsonNotificacion).Properties().Select(p => p.Name));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// NestoAPI#178: los últimos dígitos del número enmascarado que manda Redsys
        /// (p.ej. "454881******04" -> "04"). Null si no hay ninguno.
        /// </summary>
        internal static string ExtraerUltimosDigitos(string numeroEnmascarado)
        {
            if (string.IsNullOrWhiteSpace(numeroEnmascarado))
            {
                return null;
            }
            string digitosFinales = new string(numeroEnmascarado.Trim()
                .Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            if (digitosFinales.Length == 0)
            {
                return null;
            }
            return digitosFinales.Length > 4
                ? digitosFinales.Substring(digitosFinales.Length - 4)
                : digitosFinales;
        }

        /// <summary>
        /// NestoAPI#178: la caducidad AAMM de Redsys ("2712" = diciembre 2027) como último día
        /// de ese mes, que es hasta cuándo vale la tarjeta. Null si no se puede interpretar.
        /// </summary>
        internal static DateTime? ParsearCaducidadRedsys(string expiryDate)
        {
            if (string.IsNullOrWhiteSpace(expiryDate) || expiryDate.Trim().Length != 4)
            {
                return null;
            }
            string limpio = expiryDate.Trim();
            if (!int.TryParse(limpio.Substring(0, 2), out int anno)
                || !int.TryParse(limpio.Substring(2, 2), out int mes)
                || mes < 1 || mes > 12)
            {
                return null;
            }
            return new DateTime(2000 + anno, mes, DateTime.DaysInMonth(2000 + anno, mes));
        }

        internal static string NombreMarcaTarjeta(string dsCardBrand)
        {
            switch (dsCardBrand?.Trim())
            {
                case "1": return "Visa";
                case "2": return "Mastercard";
                case "6": return "Diners";
                case "8": return "Amex";
                case "9": return "JCB";
                case "22": return "UPI";
                default: return string.IsNullOrWhiteSpace(dsCardBrand) ? null : dsCardBrand.Trim();
            }
        }

        private string DecodificarParametrosInterno(string parametros)
        {
            RedsysAPI r = new RedsysAPI();
            return r.decodeMerchantParameters(parametros);
        }
    }
}
