using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#456: el usuario de los prepagos era siempre NUEVAVISION\RDS2016$ (la cuenta de
    /// máquina del pool), 43.286 filas desde 2020 y el 100 % de los últimos 30 días.
    ///
    /// <para>La causa no estaba en el código que crea el prepago —los tres sitios ya asignaban
    /// <c>Usuario</c>— sino en el EDMX: la propiedad estaba marcada como
    /// <c>StoreGeneratedPattern="Computed"</c>, así que Entity Framework NUNCA la mandaba en el
    /// INSERT y ganaba el valor por defecto de la columna, <c>suser_sname()</c>. Las asignaciones
    /// de C# se descartaban en silencio.</para>
    /// </summary>
    [TestClass]
    public class PrepagoUsuarioTests
    {
        [TestMethod]
        public void ElEdmxNoDebeMarcarElUsuarioDelPrepagoComoComputed()
        {
            string edmx = File.ReadAllText(LocalizarEdmx());

            foreach (Match entidad in Regex.Matches(edmx,
                "<EntityType Name=\"Prepagos?\">.*?</EntityType>", RegexOptions.Singleline))
            {
                Match usuario = Regex.Match(entidad.Value, "<Property Name=\"Usuario\"[^>]*>");
                Assert.IsTrue(usuario.Success, "No se encuentra la propiedad Usuario del Prepago");
                Assert.IsFalse(usuario.Value.Contains("StoreGeneratedPattern"),
                    "Si Usuario vuelve a ser Computed, EF deja de mandarlo y el prepago se graba " +
                    "otra vez a nombre de la cuenta de máquina: " + usuario.Value);
            }
        }

        [TestMethod]
        public void LaFechaDeModificacionSiLaSigueGenerandoLaBaseDeDatos()
        {
            // La otra columna del mismo par sí es Computed a propósito: la pone getdate().
            // Este test evita "arreglar" de más quitando los dos.
            string edmx = File.ReadAllText(LocalizarEdmx());

            Match entidad = Regex.Match(edmx,
                "<EntityType Name=\"Prepagos\">.*?</EntityType>", RegexOptions.Singleline);
            Match fecha = Regex.Match(entidad.Value, "<Property Name=\"FechaModificacion\"[^>]*>");

            StringAssert.Contains(fecha.Value, "StoreGeneratedPattern");
        }

        [TestMethod]
        public void ParaAuditoria_UsuarioNormal_LoDejaIgual()
        {
            Assert.AreEqual("NUEVAVISION\\Enrique", UsuarioAuditoriaHelper.ParaAuditoria("NUEVAVISION\\Enrique"));
            Assert.AreEqual("APP\\15191", UsuarioAuditoriaHelper.ParaAuditoria("APP\\15191"));
        }

        [TestMethod]
        public void ParaAuditoria_VacioONulo_DevuelveDesconocidoYNoCadenaVacia()
        {
            // La columna es NOT NULL: al dejar de ser Computed, una cadena vacía ya no la tapa
            // el valor por defecto, así que reventaría el INSERT del prepago.
            Assert.AreEqual(UsuarioAuditoriaHelper.DESCONOCIDO, UsuarioAuditoriaHelper.ParaAuditoria(null));
            Assert.AreEqual(UsuarioAuditoriaHelper.DESCONOCIDO, UsuarioAuditoriaHelper.ParaAuditoria(""));
            Assert.AreEqual(UsuarioAuditoriaHelper.DESCONOCIDO, UsuarioAuditoriaHelper.ParaAuditoria("   "));
        }

        [TestMethod]
        public void ParaAuditoria_MasLargoQueLaColumna_LoRecorta()
        {
            string largo = new string('X', 45);

            string resultado = UsuarioAuditoriaHelper.ParaAuditoria(largo);

            Assert.AreEqual(UsuarioAuditoriaHelper.LONGITUD_MAXIMA, resultado.Length);
        }

        [TestMethod]
        public void ParaAuditoria_ConEspacios_LosQuita()
        {
            Assert.AreEqual("NUEVAVISION\\Magan", UsuarioAuditoriaHelper.ParaAuditoria("  NUEVAVISION\\Magan  "));
        }

        /// <summary>El .edmx vive en el proyecto, no en el bin: hay que subir hasta encontrarlo.</summary>
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
