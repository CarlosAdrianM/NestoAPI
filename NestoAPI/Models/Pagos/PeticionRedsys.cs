namespace NestoAPI.Models.Pagos
{
    public class PeticionRedsys
    {
        public string Ds_MerchantParameters { get; set; }
        public string Ds_SignatureVersion { get; } = "HMAC_SHA256_V1";
        public string Ds_Signature { get; set; }
    }
}
