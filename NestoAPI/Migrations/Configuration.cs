namespace NestoAPI.Migrations
{
    using Infraestructure;
    using Microsoft.AspNet.Identity;
    using Microsoft.AspNet.Identity.EntityFramework;
    using System;
    using System.Data.Entity;
    using System.Data.Entity.Migrations;
    using System.Linq;

    internal sealed class Configuration : DbMigrationsConfiguration<NestoAPI.Infraestructure.ApplicationDbContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
        }

        protected override void Seed(NestoAPI.Infraestructure.ApplicationDbContext context)
        {
            //  This method will be called after migrating to the latest version.

            //  You can use the DbSet<T>.AddOrUpdate() helper extension method 
            //  to avoid creating duplicate seed data. E.g.
            //
            //    context.People.AddOrUpdate(
            //      p => p.FullName,
            //      new Person { FullName = "Andrew Peters" },
            //      new Person { FullName = "Brice Lambson" },
            //      new Person { FullName = "Rowan Miller" }
            //    );
            //


            // Carlos 11/07/16: pasos para crear un vendedor
            // Poner los datos en var user = new ApplicationUser() ...
            // Poner la contraseña en manager.Create(user,...)
            // Poner el usuario en var adminUser = manager.FindByName("...");
            // Comprobar que el grupo sea correcto en manager.AddToRoles(adminUser.Id, new string[] { "..."});
            // En la consola del administrador de paquetes ejecutar update-database
            
            var manager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(new ApplicationDbContext()));

            var roleManager = new RoleManager<IdentityRole>(new RoleStore<IdentityRole>(new ApplicationDbContext()));

            var user = new ApplicationUser()
            {
                UserName = "Almudena",
                Email = "almudenagonzalez@nuevavision.es",
                EmailConfirmed = true,
                FirstName = "Almudena",
                LastName = "González",
                Level = 1,
                JoinDate = DateTime.Now
            };

            manager.Create(user, "Algete.2023");

            
            if (roleManager.Roles.Count() == 0)
            {
                roleManager.Create(new IdentityRole { Name = "SuperAdmin" });
                roleManager.Create(new IdentityRole { Name = "Admin" });
                roleManager.Create(new IdentityRole { Name = "Vendedor" });
                roleManager.Create(new IdentityRole { Name = "Cliente" });
                roleManager.Create(new IdentityRole { Name = "VendedorTelefono" });
            }

            var adminUser = manager.FindByName("Almudena");

            manager.AddToRoles(adminUser.Id, new string[] { "VendedorTelefono" });
        }
    }
}
