using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Data.Entity.Core.EntityClient;
using System.Data.Entity.Core.Metadata.Edm;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

namespace NestoAPI.Tests.Helpers
{
    /// <summary>
    /// Conexión a una base de datos de verdad para las pruebas de integración de LECTURA
    /// (categoría «Integracion»). Solo se abre si la variable de entorno NESTO_TEST_BD trae una
    /// cadena de conexión de SQL Server; sin ella, la prueba sale como no concluyente y la suite
    /// normal no se entera.
    ///
    /// <para>El app.config de los tests no declara el proveedor de SQL Server, así que se registra
    /// aquí por código. La configuración de Entity Framework es única por proceso: estas pruebas se
    /// lanzan solas (filtro «Integracion»), no mezcladas con el resto de la suite.</para>
    /// </summary>
    internal static class BaseDeDatosDeIntegracion
    {
        public const string VARIABLE_CONEXION = "NESTO_TEST_BD";

        private static readonly object candado = new object();
        private static bool configurado;
        private static string motivoSinConfigurar;
        private static Assembly ensambladoProveedor;

        private class ConfiguracionSqlServer : DbConfiguration
        {
            public ConfiguracionSqlServer()
            {
                // Por reflexión, para no añadir la referencia a EntityFramework.SqlServer al proyecto
                // de tests solo por esto: se carga el que ya compila NestoAPI
                Type proveedor = ensambladoProveedor.GetType("System.Data.Entity.SqlServer.SqlProviderServices", true);
                SetProviderServices("System.Data.SqlClient",
                    (System.Data.Entity.Core.Common.DbProviderServices)proveedor.GetProperty("Instance").GetValue(null));
            }
        }

        private class ContextoSoloSql : DbContext
        {
            public ContextoSoloSql(string conexion) : base(new SqlConnection(conexion), true)
            {
            }
        }

        /// <summary>Un contexto sin modelo, para lo que solo ejecuta SQL (Database.SqlQuery).</summary>
        public static DbContext AbrirSoloSql()
        {
            string conexion = ConexionOInconclusa();
            Database.SetInitializer<ContextoSoloSql>(null);
            return new ContextoSoloSql(conexion);
        }

        /// <summary>
        /// El contexto de verdad, con el modelo del EDMX: sirve para comprobar que una consulta LINQ
        /// se traduce a SQL y se materializa, que es justo lo que no ven los tests con NVEntities falso.
        /// </summary>
        public static NVEntities AbrirNVEntities()
        {
            string conexion = ConexionOInconclusa();
            var metadatos = new MetadataWorkspace(
                new[] { "res://*/Models.NestoEntities.csdl", "res://*/Models.NestoEntities.ssdl", "res://*/Models.NestoEntities.msl" },
                new[] { typeof(NVEntities).Assembly });
            return new NVEntities(new EntityConnection(metadatos, new SqlConnection(conexion), true), true);
        }

        private static string ConexionOInconclusa()
        {
            string conexion = Environment.GetEnvironmentVariable(VARIABLE_CONEXION);
            if (string.IsNullOrWhiteSpace(conexion))
            {
                Assert.Inconclusive($"Sin {VARIABLE_CONEXION}: no se prueba contra la base de datos.");
            }
            lock (candado)
            {
                if (!configurado && motivoSinConfigurar == null)
                {
                    Configurar();
                }
            }
            if (!configurado)
            {
                Assert.Inconclusive(motivoSinConfigurar);
            }
            return conexion;
        }

        private static void Configurar()
        {
            string carpeta = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string ruta in new[]
            {
                Path.Combine(carpeta, "EntityFramework.SqlServer.dll"),
                Path.GetFullPath(Path.Combine(carpeta, "..", "..", "..", "NestoAPI", "bin", "EntityFramework.SqlServer.dll"))
            })
            {
                if (File.Exists(ruta))
                {
                    ensambladoProveedor = Assembly.LoadFrom(ruta);
                    break;
                }
            }
            if (ensambladoProveedor == null)
            {
                motivoSinConfigurar = "No se encuentra EntityFramework.SqlServer.dll (hay que compilar NestoAPI antes).";
                return;
            }

            try
            {
                DbConfiguration.SetConfiguration(new ConfiguracionSqlServer());
                configurado = true;
            }
            catch (InvalidOperationException ex)
            {
                // Otro test ya ha usado Entity Framework en este proceso con la configuración por defecto
                motivoSinConfigurar = "Las pruebas de integración hay que lanzarlas solas (filtro «Integracion»): " + ex.Message;
            }
        }
    }
}
