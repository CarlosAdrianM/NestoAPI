using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Productos
{
    /// <summary>
    /// NestoAPI#738: las LECTURAS del repositorio del cambio de estado contra una base de datos de verdad (las consultas LINQ
    /// agrupadas y el SQL de EstadosProducto solo se ven fallar al llamar a la API). Solo corre con NESTO_TEST_BD; no escribe
    /// nada (Aplicar no se prueba aquí). El motor de las propuestas va falso: tiene sus propias pruebas.
    /// </summary>
    [TestClass]
    public class RepositorioEstadoProductosIntegracionTests
    {
        private static RepositorioEstadoProductos Repositorio(NVEntities db)
        {
            IServicioPropuestaStockProducto motor = A.Fake<IServicioPropuestaStockProducto>();
            A.CallTo(() => motor.CalcularComoDisponibles(A<IEnumerable<string>>._)).Returns(new Dictionary<string, ResultadoTendencia>());
            return new RepositorioEstadoProductos(db, motor, ex => throw ex);
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task LeerEstados_TraeLaTablaConDisponibleYExtinguir()
        {
            using (NVEntities db = BaseDeDatosDeIntegracion.AbrirNVEntities())
            {
                List<EstadoProductoDTO> estados = await Repositorio(db).LeerEstados("1");

                Assert.IsTrue(estados.Any(e => e.Estado == 0));
                Assert.IsTrue(estados.Any(e => e.AExtinguir));
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public async Task FiltroCargarYSugerir_SeTraducenYSeMaterializan()
        {
            using (NVEntities db = BaseDeDatosDeIntegracion.AbrirNVEntities())
            {
                RepositorioEstadoProductos repositorio = Repositorio(db);
                List<string> productos = await repositorio.ProductosDelFiltro("1", new FiltroEstadoProductos { Productos = new List<string> { "17404", "NOEXISTE" } });
                CollectionAssert.AreEqual(new[] { "17404", "NOEXISTE" }, productos);

                string familia = (await db.Productos.Where(p => p.Empresa == "1" && p.Número == "17404").Select(p => p.Familia).ToListAsync()).Single().Trim();
                List<string> deLaFamilia = await repositorio.ProductosDelFiltro("1", new FiltroEstadoProductos { Familia = familia, Productos = new List<string> { "17404" } });
                CollectionAssert.AreEqual(new[] { "17404" }, deLaFamilia);

                Dictionary<string, DatosEstadoProducto> datos = await repositorio.Cargar("1", productos);
                Assert.IsTrue(datos.ContainsKey("17404"));
                Assert.IsFalse(datos.ContainsKey("NOEXISTE"));

                Dictionary<string, List<ControlStockSugeridoDTO>> sugeridos = await repositorio.SugerirControles("1", new List<string> { "17404" });
                CollectionAssert.AreEqual(new[] { "ALG", "REI", "ALC" }, sugeridos["17404"].Select(s => s.Almacen).ToList());
            }
        }
    }
}
