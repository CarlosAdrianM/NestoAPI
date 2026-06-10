using System;

namespace NestoAPI.Models.CanalesExternos
{
    /// <summary>
    /// NestoAPI#225: credenciales LWA de Amazon SP-API almacenadas de forma centralizada
    /// en la tabla dbo.AmazonSpApiCredencial. NO se mapea en el EDMX: se accede por SQL
    /// crudo desde ServicioRotacionCredencialesAmazon (único call site).
    /// </summary>
    public class AmazonSpApiCredencial
    {
        public int Id { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string RefreshToken { get; set; }
        public DateTime? SecretExpiry { get; set; }
        public DateTime? OldSecretExpiry { get; set; }
        public DateTime FechaModificacion { get; set; }
        public string Usuario { get; set; }
    }
}
