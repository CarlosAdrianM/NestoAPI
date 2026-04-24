using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NestoAPI.Infraestructure
{
    // NestoAPI#188: token de refresh OAuth2 emitido por /oauth/token (grant password)
    // para el flow de NestoApp. Identity DB (NVIdentity connection). Script de tabla en
    // Scripts/SQL/Issue188_AddRefreshTokens.sql.
    [Table("AspNetRefreshTokens")]
    public class RefreshToken
    {
        // SHA-256 hex del secret enviado al cliente. Nunca guardamos el secret en claro.
        [Key]
        [MaxLength(64)]
        public string Id { get; set; }

        [Required]
        [MaxLength(256)]
        public string UserName { get; set; }

        [Required]
        [MaxLength(50)]
        public string ClientId { get; set; }

        [Required]
        public DateTime IssuedUtc { get; set; }

        [Required]
        public DateTime ExpiresUtc { get; set; }

        public DateTime? RevokedUtc { get; set; }

        [Required]
        public string ProtectedTicket { get; set; }
    }
}
