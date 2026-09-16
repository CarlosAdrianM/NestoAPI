using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// NestoAPI#479: ProductoDTO.AplicarFormatoOracionAlNombre, el paso del constructor de
    /// publicación que pasa el Nombre a formato oración cuando no hay NombrePersonalizado,
    /// leyendo la referencia del proveedor principal (Orden = 1).
    /// </summary>
    [TestClass]
    public class ProductoDTONombreTiendaTests
    {
        private NVEntities db;
        private DbSet<ProveedoresProducto> fakeProveedores;
        private Producto producto;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeProveedores = A.Fake<DbSet<ProveedoresProducto>>(o =>
                o.Implements<IQueryable<ProveedoresProducto>>().Implements<IDbAsyncEnumerable<ProveedoresProducto>>());
            A.CallTo(() => db.ProveedoresProductoes).Returns(fakeProveedores);
            producto = new Producto { Empresa = "1  ", Número = "45700" };
        }

        private void ConProveedores(params ProveedoresProducto[] filas)
        {
            ConfigurarFakeDbSet(fakeProveedores, filas.AsQueryable());
        }

        [TestMethod]
        public async Task SinNombrePersonalizado_ElNombreViajaEnFormatoOracionConElModeloDelFabricante()
        {
            ConProveedores(
                new ProveedoresProducto { Empresa = "1  ", Nº_Producto = "45700", Orden = 2, ReferenciaProv = "OTRO" },
                new ProveedoresProducto { Empresa = "1  ", Nº_Producto = "45700", Orden = 1, ReferenciaProv = "Check GY  " });
            ProductoDTO dto = new ProductoDTO { Producto = "45700", Nombre = "SILLON DE BARBERO CHECK GY" };

            await ProductoDTO.AplicarFormatoOracionAlNombre(dto, db, producto);

            Assert.AreEqual("Sillon de barbero Check GY", dto.Nombre);
        }

        [TestMethod]
        public async Task ConNombrePersonalizado_NoSeTocaNada()
        {
            ConProveedores(new ProveedoresProducto { Empresa = "1  ", Nº_Producto = "45700", Orden = 1, ReferenciaProv = "Dave" });
            ProductoDTO dto = new ProductoDTO
            {
                Producto = "45700",
                Nombre = "SILLON DE BARBERO DAVE",
                NombrePersonalizado = "Sillón de barbero Dave"
            };

            await ProductoDTO.AplicarFormatoOracionAlNombre(dto, db, producto);

            Assert.AreEqual("SILLON DE BARBERO DAVE", dto.Nombre);
            Assert.AreEqual("Sillón de barbero Dave", dto.NombrePersonalizado);
        }

        [TestMethod]
        public async Task SinProveedor_SoloLaPrimeraLetraYLaListaBlanca()
        {
            ConProveedores();
            ProductoDTO dto = new ProductoDTO { Producto = "45700", Nombre = "LAMPARA LED DAVE" };

            await ProductoDTO.AplicarFormatoOracionAlNombre(dto, db, producto);

            Assert.AreEqual("Lampara LED dave", dto.Nombre);
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
