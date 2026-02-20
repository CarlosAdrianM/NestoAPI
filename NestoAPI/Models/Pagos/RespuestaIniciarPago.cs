namespace NestoAPI.Models.Pagos
{
    public class RespuestaIniciarPago
    {
        public int IdPago { get; set; }
        public string UrlRedsys { get; set; }
        public string Ds_SignatureVersion { get; set; }
        public string Ds_MerchantParameters { get; set; }
        public string Ds_Signature { get; set; }
    }
}
