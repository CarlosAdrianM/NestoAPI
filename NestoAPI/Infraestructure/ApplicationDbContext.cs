using Microsoft.AspNet.Identity.EntityFramework;
using System.Data.Entity;

namespace NestoAPI.Infraestructure
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext()
            : base("NVIdentity", throwIfV1Schema: false)
        {
            Configuration.ProxyCreationEnabled = false;
            Configuration.LazyLoadingEnabled = false;
        }

        // NestoAPI#188: refresh tokens OAuth2 para el flow de NestoApp.
        // Esquema gestionado manualmente por Scripts/SQL/Issue188_AddRefreshTokens.sql,
        // no por migrations EF.
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        public static ApplicationDbContext Create()
        {
            return new ApplicationDbContext();
        }

    }
}
