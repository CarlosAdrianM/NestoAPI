using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    // Misma filosofía que ContabilidadService con prdContabilizar: un servicio dueño de PreExtrProducto y de
    // prdExtrProducto, con la única llamada al procedimiento y las validaciones que el procedimiento aplica.
    [TestClass]
    public class ServicioExtractoProductoTests
    {
        private static string CarpetaDelProyecto()
        {
            for (DirectoryInfo carpeta = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); carpeta != null; carpeta = carpeta.Parent)
            {
                if (File.Exists(Path.Combine(carpeta.FullName, "NestoAPI.sln")))
                {
                    return Path.Combine(carpeta.FullName, "NestoAPI");
                }
            }
            return null;
        }

        private static string[] FicherosQueCumplen(Regex patron)
        {
            string carpeta = CarpetaDelProyecto();
            if (carpeta == null)
            {
                Assert.Inconclusive("No se encuentra el código fuente de NestoAPI.");
            }
            return Directory.GetFiles(carpeta, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(f => patron.IsMatch(File.ReadAllText(f)))
                .Select(Path.GetFileName)
                .ToArray();
        }

        [TestMethod]
        public void SoloElServicio_LanzaPrdExtrProducto()
        {
            string[] ficheros = FicherosQueCumplen(new Regex(@"EXEC\s+(@\w+\s*=\s*)?prdExtrProducto", RegexOptions.IgnoreCase));

            CollectionAssert.AreEqual(new[] { "ServicioExtractoProducto.cs" }, ficheros, string.Join(", ", ficheros));
        }

        [TestMethod]
        public void SoloElServicio_AnadeLineasAPreExtrProducto()
        {
            string[] ficheros = FicherosQueCumplen(new Regex(@"PreExtrProductos\.Add"));

            CollectionAssert.AreEqual(new[] { "ServicioExtractoProducto.cs" }, ficheros, string.Join(", ", ficheros));
        }

        [TestMethod]
        public void Merma_SinCentroDeCoste_ErrorAntesDelProcedimiento()
        {
            // prdExtrProducto: «No puede contabilizar una merma sin centro de coste» (diarios merma% de DiariosProducto)
            var lineas = new[]
            {
                new PreExtrProducto { Diario = "Merma", Número = "29631", CentroCoste = " " },
                new PreExtrProducto { Diario = "Merma", Número = "29632", CentroCoste = "ALG" }
            };

            string error = ServicioExtractoProducto.ErrorAntesDeContabilizar(esDiarioDeMermas: true, lineas);

            StringAssert.Contains(error, "centro de coste");
            StringAssert.Contains(error, "29631");
        }

        [TestMethod]
        public void NoEsDiarioDeMermas_SinCentroDeCoste_NoEsError()
        {
            var lineas = new[] { new PreExtrProducto { Diario = "PendRepo", Número = "29631", CentroCoste = null } };

            Assert.IsNull(ServicioExtractoProducto.ErrorAntesDeContabilizar(esDiarioDeMermas: false, lineas));
        }

        [TestMethod]
        public async Task CrearLineas_ConElDbDelLlamante_SoloLasAnade_ElLlamanteGuarda()
        {
            // Las notas de entrega y los kits guardan PreExtrProducto junto con el resto de cambios de su transacción
            var db = A.Fake<NVEntities>();
            var preExtr = A.Fake<DbSet<PreExtrProducto>>();
            A.CallTo(() => db.PreExtrProductos).Returns(preExtr);
            var linea = new PreExtrProducto { Empresa = "1", Diario = "_EntregFac", Número = "29631" };

            int creadas = await new ServicioExtractoProducto().CrearLineas(db, new List<PreExtrProducto> { linea });

            Assert.AreEqual(1, creadas);
            A.CallTo(() => preExtr.Add(linea)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
            A.CallTo(() => db.SaveChanges()).MustNotHaveHappened();
        }

        [TestMethod]
        public void ErrorDelProcedimiento_SinElRuidoDeTransacciones()
        {
            string mensaje = ServicioExtractoProducto.MensajeDeError(new[]
            {
                new KeyValuePair<int, string>(50000, "Fecha no permitida"),
                new KeyValuePair<int, string>(266, "El recuento de transacciones después de EXECUTE indica...")
            });

            StringAssert.Contains(mensaje, "Fecha no permitida");
            Assert.IsFalse(mensaje.Contains("recuento"));
        }
    }
}
