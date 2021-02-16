using NestoAPI.Models;
using System;
using System.Linq;
using System.Web;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Configuration;
using RedsysAPIPrj;
using Microsoft.VisualBasic;

namespace NestoAPI.Infraestructure
{
    public class ServicioReclamacionDeuda
    {
        public async Task<ReclamacionDeuda> ProcesarReclamacionDeuda(ReclamacionDeuda reclamacion)
        {
            RedsysTPV redsys = new RedsysTPV();
            redsys.ModoPruebasOn = false;
            redsys.KcSHA256 = ConfigurationManager.AppSettings["RedsysSHA256"];

            // Calculamos el pedido con un identificador único
            var ticks = new DateTime(2020, 1, 1).Ticks;
            var ans = DateTime.Now.Ticks - ticks;
            var orderUniqueId = Strings.Right(ans.ToString("X12") +"C"+ reclamacion.Cliente, 12);

            //Parameters 
            // This is a example, use your own parameter
            redsys.DS_MERCHANT_AMOUNT = ((int)(reclamacion.Importe * 100)).ToString();
            redsys.DS_MERCHANT_CURRENCY = "978"; //EURO
            redsys.DS_MERCHANT_CUSTOMER_MAIL = reclamacion.Correo;
            redsys.DS_MERCHANT_CUSTOMER_MOBILE = reclamacion.Movil;
            redsys.DS_MERCHANT_MERCHANTCODE = "329515704";
            redsys.DS_MERCHANT_MERCHANTURL = "http://www.nuevavision.es";
            redsys.DS_MERCHANT_ORDER = orderUniqueId;
            redsys.DS_MERCHANT_P2F_XMLDATA.nombreComprador = reclamacion.Nombre;
            redsys.DS_MERCHANT_P2F_XMLDATA.direccionComprador = reclamacion.Direccion;
            redsys.DS_MERCHANT_P2F_XMLDATA.subjectMailCliente = reclamacion.Asunto;
            redsys.DS_MERCHANT_P2F_XMLDATA.textoLibre1 = "Utilice el siguiente botón para pagar la deuda pendiente con NUEVA VISION";
            redsys.DS_MERCHANT_TERMINAL = "2";
            redsys.DS_MERCHANT_TRANSACTIONTYPE = "F";
            redsys.DS_MERCHANT_URLOK = "";
            redsys.DS_MERCHANT_URLKO = "";
            redsys.DS_MERCHANT_CUSTOMER_SMS_TEXT = reclamacion.TextoSMS;

            redsys.CrearPeticion();
            
            RespuestaRedsys respuesta;

            using (HttpClient client = new HttpClient())
            {
                // Call asynchronous network methods in a try/catch block to handle exceptions
                try
                {
                    string peticionJson = redsys.PeticionJson;
                    HttpContent content = new StringContent(peticionJson, Encoding.UTF8, "application/json");
                    System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                    HttpResponseMessage response = await client.PostAsync(redsys.UrlRedsys, content).ConfigureAwait(false);
                    content.Dispose();

                    if (response.IsSuccessStatusCode)
                    {
                        string resultado = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        PeticionRedsys peticion = JsonConvert.DeserializeObject<PeticionRedsys>(resultado);
                        string resultadoDecodificado = redsys.Decodificar(peticion.Ds_MerchantParameters);                        
                        respuesta = JsonConvert.DeserializeObject<RespuestaRedsys>(resultadoDecodificado);
                        reclamacion.Enlace = respuesta.Ds_UrlPago2Fases;
                        reclamacion.TramitadoOK = true;
                    }
                    else
                    {
                        string textoError = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        JObject requestException = JsonConvert.DeserializeObject<JObject>(textoError);

                        string errorMostrar = "No se ha podido enviar la reclamación de deuda al servidor" + "\n";
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
                catch (HttpRequestException ex)
                {
                    return null;
                }
            }

            return reclamacion;
        }
    }
}

public class RedsysTPV
{
    public RedsysTPV()
    {
        Peticion = new PeticionRedsys();
        DS_MERCHANT_P2F_XMLDATA = new FormatoCorreoReclamacion();
    }
    public string DS_MERCHANT_AMOUNT { get; set; }
    public string DS_MERCHANT_ORDER { get; set; }
    public string DS_MERCHANT_MERCHANTCODE { get; set; }
    public string DS_MERCHANT_CURRENCY { get; set; }
    public string DS_MERCHANT_TRANSACTIONTYPE { get; set; }
    public string DS_MERCHANT_TERMINAL { get; set; }
    public string DS_MERCHANT_MERCHANTURL { get; set; }
    public string DS_MERCHANT_URLOK { get; set; }
    public string DS_MERCHANT_URLKO { get; set; }
    public string DS_MERCHANT_CUSTOMER_MOBILE { get; set; }
    public string DS_MERCHANT_CUSTOMER_MAIL { get; set; }
    public string DS_MERCHANT_P2F_EXPIRIDATE { get; set; } = (60 * 24 * 7).ToString();  // minutos
    public string DS_MERCHANT_CUSTOMER_SMS_TEXT { get; set; }
    public FormatoCorreoReclamacion DS_MERCHANT_P2F_XMLDATA { get; set; }



    private PeticionRedsys Peticion { get; set; }
    public string KcSHA256 { get; set; }
    public bool ModoPruebasOn { get; set; }
    public Uri UrlRedsys
    {
        get
        {
            return ModoPruebasOn ? new Uri("https://sis-t.redsys.es:25443/sis/rest/trataPeticionREST") : new Uri("https://sis.redsys.es/sis/rest/trataPeticionREST");
        }
    }

    public void CrearPeticion()
    {

        /* Calcular el parámetro Ds_MerchantParameters. Para llevar a cabo el cálculo
        de este parámetro, inicialmente se deben añadir todos los parámetros de la
        petición de pago que se desea enviar, tal y como se muestra a continuación: */
        RedsysAPI r = new RedsysAPI();
        r.SetParameter("DS_MERCHANT_AMOUNT", DS_MERCHANT_AMOUNT);
        r.SetParameter("DS_MERCHANT_ORDER", DS_MERCHANT_ORDER);
        r.SetParameter("DS_MERCHANT_MERCHANTCODE", DS_MERCHANT_MERCHANTCODE);
        r.SetParameter("DS_MERCHANT_CURRENCY", DS_MERCHANT_CURRENCY);
        r.SetParameter("DS_MERCHANT_TRANSACTIONTYPE", DS_MERCHANT_TRANSACTIONTYPE);
        r.SetParameter("DS_MERCHANT_TERMINAL", DS_MERCHANT_TERMINAL);
        r.SetParameter("DS_MERCHANT_MERCHANTURL", DS_MERCHANT_MERCHANTURL);
        r.SetParameter("DS_MERCHANT_URLOK", DS_MERCHANT_URLOK);
        r.SetParameter("DS_MERCHANT_URLKO", DS_MERCHANT_URLKO);
        r.SetParameter("DS_MERCHANT_CUSTOMER_MOBILE", DS_MERCHANT_CUSTOMER_MOBILE);
        r.SetParameter("DS_MERCHANT_CUSTOMER_MAIL", DS_MERCHANT_CUSTOMER_MAIL);
        r.SetParameter("DS_MERCHANT_P2F_EXPIRIDATE", DS_MERCHANT_P2F_EXPIRIDATE);
        r.SetParameter("DS_MERCHANT_CUSTOMER_SMS_TEXT", DS_MERCHANT_CUSTOMER_SMS_TEXT);
        string p2f_xmldata = DS_MERCHANT_P2F_XMLDATA.ToXML();
        r.SetParameter("DS_MERCHANT_P2F_XMLDATA", p2f_xmldata);
        /* Por último se debe llamar a la función de la librería
        “createMerchantParameters()” para crear los parámetros y asignar dicho valor a
        la etiqueta Ds_MerchantrParameters, tal y como se muestra a continuación: */
        string parametros = r.createMerchantParameters();
        Peticion.Ds_MerchantParameters = parametros;
        /* Calcular el parámetro Ds_Signature. Para llevar a cabo el cálculo de este
        parámetro, se debe llamar a la función de la librería
        “createMerchantSignature()” con la clave SHA-256 del comercio (obteniendola en
        el panel del módulo de administración), tal y como se muestra a continuación:
        */
        string firma = r.createMerchantSignature(KcSHA256);
        Peticion.Ds_Signature = firma;
        /* Una vez obtenidos los valores de los parámetros Ds_MerchantParameters y
        Ds_Signature , se debe rellenar la petición REST con dichos valores y el parámetro Ds_SignatureVersion */
    }

    public string PeticionJson
    {
        get
        {
            return JsonConvert.SerializeObject(Peticion);
        }
    }

    public string Decodificar(string parametros)
    {
        RedsysAPI redsysAPI = new RedsysAPI();
        return redsysAPI.decodeMerchantParameters(parametros);
    }

}

public class PeticionRedsys
{
    public string Ds_MerchantParameters { get; set; }
    public string Ds_SignatureVersion { get; } = "HMAC_SHA256_V1";
    public string Ds_Signature { get; set; }
}

public class FormatoCorreoReclamacion
{
    public string nombreComprador { get; set; }
    public string direccionComprador { get; set; }
    public string subjectMailCliente { get; set; }
    public string textoLibre1 { get; set; }

    public string ToXML()
    {
        string resultado = "<![CDATA[";

        if (!string.IsNullOrWhiteSpace(nombreComprador))
        {
            resultado += "<nombreComprador>" + HttpUtility.HtmlEncode(nombreComprador) + "</nombreComprador>";
        }

        if (!string.IsNullOrWhiteSpace(direccionComprador))
        {
            resultado += "<direccionComprador>" + HttpUtility.HtmlEncode(direccionComprador) + "</direccionComprador>";
        }

        if (!string.IsNullOrWhiteSpace(subjectMailCliente))
        {
            resultado += "<subjectMailCliente>" + HttpUtility.HtmlEncode(subjectMailCliente) + "</subjectMailCliente>";
        }

        if (!string.IsNullOrWhiteSpace(textoLibre1))
        {
            resultado += "<textoLibre1>" + HttpUtility.HtmlEncode(textoLibre1) + "</textoLibre1>";
        }


        resultado += "]]>";

        return resultado;
    }
}

public class RespuestaRedsys
{
    public string Ds_Amount { get; set; }
    public string Ds_AuthorisationCode { get; set; }
    public string Ds_Currency { get; set; }
    public string Ds_Language { get; set; }
    public string Ds_MerchantCode { get; set; }
    public string Ds_MerchantData { get; set; }
    public string Ds_Order { get; set; }
    public string Ds_Response { get; set; }
    public string Ds_SecurePayment { get; set; }
    public string Ds_Terminal { get; set; }
    public string Ds_TransactionType { get; set; }
    public string Ds_UrlPago2Fases { get; set; }
}