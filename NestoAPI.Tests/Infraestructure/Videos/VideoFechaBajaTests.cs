using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace NestoAPI.Tests.Infraestructure.Videos
{
    /// <summary>
    /// Equipo de SEO, 08/09/26: el listado de vídeos no marcaba las bajas, así que la tienda online
    /// las deducía por ausencia —recorría el listado entero y daba de baja lo que no aparecía— y
    /// tenía que rodearlo de guardas para que un fallo de la API no despublicara media web.
    ///
    /// <para>La columna <c>Videos.FechaBaja</c> (NULL = vivo) resuelve eso y algo peor: hasta ahora
    /// retirar un vídeo era un DELETE con <c>ON DELETE CASCADE</c> a VideosProductos, que se llevaba
    /// por delante la transcripción, el protocolo y los productos, sin vuelta atrás.</para>
    ///
    /// <para>El EDMX se editó a mano (los tres modelos), como en NestoAPI#456 y #413. Estos tests
    /// están para que un "Update Model from Database" desde Visual Studio no se lleve el mapeo por
    /// delante en silencio: si eso pasara, EF dejaría de leer la columna, el filtro de bajas se
    /// evaluaría siempre a null y volverían a salir los vídeos retirados.</para>
    /// </summary>
    [TestClass]
    public class VideoFechaBajaTests
    {
        [TestMethod]
        public void LaEntidadVideoTieneFechaBajaYEsNulable()
        {
            // NULL es "el vídeo está vivo", así que la propiedad NO puede ser un DateTime a secas.
            Video video = new Video();

            Assert.IsNull(video.FechaBaja, "Un vídeo recién creado tiene que nacer vivo");

            video.FechaBaja = new DateTime(2026, 9, 8);
            Assert.AreEqual(new DateTime(2026, 9, 8), video.FechaBaja);
        }

        [TestMethod]
        public void ElEdmxMapeaFechaBajaEnElModeloDeAlmacen()
        {
            string ssdl = EntidadDelEdmx("Videos");

            StringAssert.Contains(ssdl, "<Property Name=\"FechaBaja\" Type=\"datetime\" />",
                "Sin la columna en el SSDL, EF no la lee de la tabla");
        }

        [TestMethod]
        public void ElEdmxMapeaFechaBajaEnElModeloConceptual()
        {
            string csdl = EntidadDelEdmx("Video");

            Match propiedad = Regex.Match(csdl, "<Property Name=\"FechaBaja\"[^>]*>");
            Assert.IsTrue(propiedad.Success, "Falta FechaBaja en el modelo conceptual del EDMX");
            Assert.IsFalse(propiedad.Value.Contains("Nullable=\"false\""),
                "FechaBaja tiene que admitir NULL: NULL es el vídeo vivo. " + propiedad.Value);
        }

        [TestMethod]
        public void ElEdmxMapeaLaColumnaFechaBajaContraLaPropiedad()
        {
            string edmx = File.ReadAllText(LocalizarEdmx());

            Match mapeo = Regex.Match(edmx,
                "<EntityTypeMapping TypeName=\"NVModel.Video\">.*?</EntityTypeMapping>",
                RegexOptions.Singleline);

            Assert.IsTrue(mapeo.Success, "No se encuentra el mapeo de la entidad Video");
            StringAssert.Contains(mapeo.Value,
                "<ScalarProperty Name=\"FechaBaja\" ColumnName=\"FechaBaja\" />",
                "Sin el mapeo, la propiedad existe pero nunca se rellena");
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
