using System;

namespace NestoAPI.Models.Pagos
{
    public class ParametrosRedsysFirmados
    {
        public string Ds_SignatureVersion { get; set; }
        public string Ds_MerchantParameters { get; set; }
        public string Ds_Signature { get; set; }
        public Uri UrlRedsys { get; set; }
        public string NumeroOrden { get; set; }
    }
}
