using Microsoft.AspNet.Identity.EntityFramework;
using System.Data.Entity;

namespace NestoAPI.Infraestructure
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        static ApplicationDbContext()
        {
            // NestoAPI#191: el esquema de ASP.NET Identity lo gestionamos a mano
            // con Scripts/SQL/*.sql, no con EF Migrations. Sin esta línea, EF
            // compara el modelo con EdmMetadata y rompe TODA consulta tras un
            // cambio del modelo (p. ej. al añadir DbSet<RefreshToken> en #188).
            // El initializer se aplica por tipo y solo una vez por AppDomain,
            // por eso debe ir en un constructor estático.
            Database.SetInitializer<ApplicationDbContext>(null);
        }

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
