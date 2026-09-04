using System;

namespace NestoAPI.Models.Pagos
{
    public class RespuestaIniciarPago
    {
        public int IdPago { get; set; }
        public string UrlRedsys { get; set; }
        public string Ds_SignatureVersion { get; set; }
        public string Ds_MerchantParameters { get; set; }
        public string Ds_Signature { get; set; }
        public Guid TokenAcceso { get; set; }
        public string UrlPaginaPago { get; set; }

        /// <summary>
        /// NestoAPI#181: página propia que autentica el cobro con EMV 3DS 2 sin enseñar la
        /// pasarela. Solo viene cuando se paga con una tarjeta guardada. Si la app la recibe,
        /// carga esta URL en el WebView en vez de enviar el formulario a Redsys; las versiones
        /// antiguas ignoran el campo y siguen funcionando como siempre.
        /// </summary>
        public string UrlPago3DS { get; set; }
    }
}
