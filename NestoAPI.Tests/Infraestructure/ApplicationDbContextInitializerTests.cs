using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure.DependencyResolution;
using System.Runtime.CompilerServices;

namespace NestoAPI.Tests.Infraestructure
{
    // NestoAPI#191: regresión. Tras añadir DbSet<RefreshToken> en #188, EF
    // detectaba que el modelo no coincidía con EdmMetadata y rompía toda
    // consulta del contexto con InvalidOperationException, tirando /oauth/token.
    // El fix es un constructor estático con Database.SetInitializer<...>(null).
    [TestClass]
    public class ApplicationDbContextInitializerTests
    {
        [TestMethod]
        public void ApplicationDbContext_tiene_constructor_estatico()
        {
            // El initializer EF se aplica por tipo y solo una vez por AppDomain,
            // así que la llamada a SetInitializer(null) DEBE estar en un constructor
            // estático. Si alguien lo mueve a uno de instancia, este tripwire salta.
            Assert.IsNotNull(
                typeof(ApplicationDbContext).TypeInitializer,
                "ApplicationDbContext debe tener un constructor estático que llame a Database.SetInitializer<ApplicationDbContext>(null) (ver NestoAPI#191)."
            );
        }

        [TestMethod]
        public void ApplicationDbContext_initializer_esta_deshabilitado()
        {
            // Forzar la ejecución del constructor estático (idempotente).
            RuntimeHelpers.RunClassConstructor(typeof(ApplicationDbContext).TypeHandle);

            // Resolución vía reflexión para evitar añadir referencia a
            // Microsoft.AspNet.Identity.EntityFramework en el proyecto de tests.
            // SetInitializer(null) registra internamente un NullDatabaseInitializer<TContext>
            // en el resolver de EF, no un null literal.
            Type initializerInterface = typeof(IDatabaseInitializer<>).MakeGenericType(typeof(ApplicationDbContext));
            object initializer = DbConfiguration.DependencyResolver.GetService(initializerInterface, null);

            Assert.IsNotNull(initializer, "EF siempre resuelve un initializer; null aquí indicaría un cambio de API.");
            StringAssert.Contains(
                initializer.GetType().Name,
                "Null",
                "El initializer activo no es NullDatabaseInitializer; el contexto volverá a romper login si el modelo difiere de EdmMetadata."
            );
        }
    }
}
