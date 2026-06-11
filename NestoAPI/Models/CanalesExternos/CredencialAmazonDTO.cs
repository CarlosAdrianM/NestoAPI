using System;

namespace NestoAPI.Models.CanalesExternos
{
    /// <summary>
    /// NestoAPI#225: credencial LWA vigente que Nesto consume vía API en vez de tenerla en su
    /// clavesSecretas.config. Así, cuando el job de rotación cambia el secreto, los clientes lo
    /// reciben sin republicar.
    /// </summary>
    public class CredencialAmazonDTO
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string RefreshToken { get; set; }
        public DateTime? SecretExpiry { get; set; }
    }
}
