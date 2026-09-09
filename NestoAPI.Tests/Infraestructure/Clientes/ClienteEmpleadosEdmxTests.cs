using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace NestoAPI.Tests.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#464: Clientes.Empleados y Clientes.EmpleadosFecha se añadieron con
    /// Scripts/Clientes_Empleados.sql y se mapearon A MANO en los tres modelos del EDMX, como
    /// FechaBaja de Videos (VideoFechaBajaTests). Estos tests están para que un "Update Model from
    /// Database" desde Visual Studio no se lleve el mapeo por delante en silencio: si eso pasara,
    /// EF dejaría de leer y escribir las columnas y la combo de empleados saldría siempre vacía.
    /// </summary>
    [TestClass]
    public class ClienteEmpleadosEdmxTests
    {
        [TestMethod]
        public void LaEntidadClienteTieneEmpleadosNulables()
        {
            Cliente cliente = new Cliente();

            Assert.IsNull(cliente.Empleados, "null = no se ha preguntado todavía");
            Assert.IsNull(cliente.EmpleadosFecha);

            cliente.Empleados = 5;
            cliente.EmpleadosFecha = new DateTime(2026, 9, 9);
            Assert.AreEqual((byte)5, cliente.Empleados);
        }

        [TestMethod]
        public void ElEdmxMapeaLasColumnasEnElModeloDeAlmacen()
        {
            string ssdl = EntidadDelEdmx("Clientes");

            StringAssert.Contains(ssdl, "<Property Name=\"Empleados\" Type=\"tinyint\" />");
            StringAssert.Contains(ssdl, "<Property Name=\"EmpleadosFecha\" Type=\"datetime\" />");
        }

        [TestMethod]
        public void ElEdmxMapeaLasPropiedadesEnElModeloConceptualYAdmitenNull()
        {
            string csdl = EntidadDelEdmx("Cliente");

            foreach (string nombre in new[] { "Empleados", "EmpleadosFecha" })
            {
                Match propiedad = Regex.Match(csdl, "<Property Name=\"" + nombre + "\"[^>]*>");
                Assert.IsTrue(propiedad.Success, "Falta " + nombre + " en el modelo conceptual del EDMX");
                Assert.IsFalse(propiedad.Value.Contains("Nullable=\"false\""), nombre + " tiene que admitir NULL: " + propiedad.Value);
            }
        }

        [TestMethod]
        public void ElEdmxMapeaLasColumnasContraLasPropiedades()
        {
            string edmx = File.ReadAllText(LocalizarEdmx());

            Match mapeo = Regex.Match(edmx,
                "<EntityTypeMapping TypeName=\"NVModel.Cliente\">.*?</EntityTypeMapping>",
                RegexOptions.Singleline);

            Assert.IsTrue(mapeo.Success, "No se encuentra el mapeo de la entidad Cliente");
            StringAssert.Contains(mapeo.Value, "<ScalarProperty Name=\"Empleados\" ColumnName=\"Empleados\" />");
            StringAssert.Contains(mapeo.Value, "<ScalarProperty Name=\"EmpleadosFecha\" ColumnName=\"EmpleadosFecha\" />");
        }

        private static string EntidadDelEdmx(string nombre)
        {
            string edmx = File.ReadAllText(LocalizarEdmx());

            Match entidad = Regex.Match(edmx,
                "<EntityType Name=\"" + nombre + "\">.*?</EntityType>", RegexOptions.Singleline);

            Assert.IsTrue(entidad.Success, "No se encuentra la entidad " + nombre + " en el EDMX");
            return entidad.Value;
        }

        private static string LocalizarEdmx()
        {
            DirectoryInfo carpeta = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (carpeta != null)
            {
                string candidato = Path.Combine(carpeta.FullName, "NestoAPI", "Models", "NestoEntities.edmx");
                if (File.Exists(candidato))
                {
                    return candidato;
                }
                carpeta = carpeta.Parent;
            }

            throw new FileNotFoundException("No se encuentra NestoEntities.edmx subiendo desde " +
                AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
