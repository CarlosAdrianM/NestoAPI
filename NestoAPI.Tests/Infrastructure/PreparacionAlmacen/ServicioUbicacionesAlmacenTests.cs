using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556/#559: lo recibido pendiente de colocar y la consulta de dónde está un producto
    /// (la parte de lectura de lo que hoy hace Ariadna Vieja).
    /// </summary>
    [TestClass]
    public class ServicioUbicacionesAlmacenTests
    {
        private static FilaPendienteDeUbicar Pendiente(string producto, int cantidad, int? albaran = 125429, string codigo = "8400000000001")
        {
            return new FilaPendienteDeUbicar
            {
                Producto = producto,
                Descripcion = "PRODUCTO " + producto.Trim(),
                CodigoBarras = codigo,
                Cantidad = cantidad,
                PedidoCompra = albaran.HasValue ? 220311 : (int?)null,
                AlbaranCompra = albaran,
                DesdeCuando = new DateTime(2026, 9, 30, 10, 55, 0)
            };
        }

        private static FilaUbicacionProducto Hueco(string producto, string pasillo, string fila, string columna, int cantidad, int estado = 0)
        {
            return new FilaUbicacionProducto { Producto = producto, Pasillo = pasillo, Fila = fila, Columna = columna, Cantidad = cantidad, Estado = estado };
        }

        [TestMethod]
        public void MontarPendiente_UnProductoRecibidoEnDosAlbaranes_UnaFilaConSusDosOrigenes()
        {
            var productos = ServicioUbicacionesAlmacen.MontarPendiente(
                new[] { Pendiente("29712", 39, 125429), Pendiente("29712          ", 5, 125435) },
                new FilaUbicacionProducto[0]);

            ProductoPendienteDeUbicarDTO producto = productos.Single();
            Assert.AreEqual(44, producto.Cantidad);
            Assert.AreEqual(2, producto.Origenes.Count);
        }

        [TestMethod]
        public void MontarPendiente_SugiereLosHuecosDondeYaHayDeEseProducto()
        {
            var productos = ServicioUbicacionesAlmacen.MontarPendiente(
                new[] { Pendiente("29712", 39) },
                new[] { Hueco("29712", "004", "001", "010", 14), Hueco("29712", "004", "001", "009", 22), Hueco("OTRO", "001", "001", "001", 3) });

            List<UbicacionAlmacenDTO> huecos = productos.Single().UbicacionesActuales;
            Assert.AreEqual(2, huecos.Count);
            Assert.AreEqual("004/001/009", huecos[0].Ubicacion, "Dentro del pasillo, por columna");
        }

        [TestMethod]
        public void MontarPendiente_SeOrdenaPorElPrimerHuecoYLoQueNoTieneHuecoVaAlFinal()
        {
            var productos = ServicioUbicacionesAlmacen.MontarPendiente(
                new[] { Pendiente("NUEVO", 1), Pendiente("LEJOS", 1), Pendiente("CERCA", 1) },
                new[] { Hueco("LEJOS", "013", "000", "002", 9), Hueco("CERCA", "001", "005", "004", 7) });

            CollectionAssert.AreEqual(new[] { "CERCA", "LEJOS", "NUEVO" }, productos.Select(p => p.Producto).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, productos.Select(p => p.Orden).ToList());
        }

        [TestMethod]
        public void MontarPendiente_ProductoSinCodigoDeBarras_QuedaMarcado()
        {
            ProductoPendienteDeUbicarDTO producto = ServicioUbicacionesAlmacen.MontarPendiente(
                new[] { Pendiente("LIMA", 10, codigo: "  ") }, null).Single();

            Assert.IsTrue(producto.SinCodigo);
            Assert.IsNull(producto.CodigoBarras);
        }

        [TestMethod]
        public void MontarPendiente_LoQueSumaCero_NoSale()
        {
            var productos = ServicioUbicacionesAlmacen.MontarPendiente(new[] { Pendiente("A", 3), Pendiente("A", -3, 125430) }, null);

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public async Task LeerPendienteDeUbicar_SinNadaPendiente_NoPreguntaPorLosHuecos()
        {
            var repositorio = A.Fake<IRepositorioUbicacionesAlmacen>();
            A.CallTo(() => repositorio.LeerPendienteDeUbicar("1", "ALG")).Returns(new List<FilaPendienteDeUbicar>());

            PendienteDeUbicarDTO pendiente = await new ServicioUbicacionesAlmacen(repositorio).LeerPendienteDeUbicar("1", "ALG");

            Assert.AreEqual(0, pendiente.Productos.Count);
            A.CallTo(() => repositorio.LeerHuecosDeLoPendiente(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task BuscarProducto_SeparaLosHuecosDeLoPendienteDeColocar()
        {
            var repositorio = A.Fake<IRepositorioUbicacionesAlmacen>();
            A.CallTo(() => repositorio.BuscarProductos("1", "8710505999670")).Returns(new List<FilaProductoAlmacen>
            {
                new FilaProductoAlmacen { Producto = "18004", Descripcion = "REJILLAS DESMAQUILLANTES ", CodigoBarras = "8710505999670  " }
            });
            A.CallTo(() => repositorio.LeerUbicacionesDelProducto("1", "ALG", "18004")).Returns(new List<FilaUbicacionProducto>
            {
                Hueco("18004", "009", "003", "008", 40),
                Hueco("18004", null, null, null, 59, estado: 2)
            });

            ProductoAlmacenDTO producto = (await new ServicioUbicacionesAlmacen(repositorio).BuscarProducto("1", "ALG", " 8710505999670 ")).Single();

            Assert.AreEqual("8710505999670", producto.CodigoBarras);
            Assert.AreEqual("009/003/008", producto.Ubicaciones.Single().Ubicacion);
            Assert.AreEqual(59, producto.PendienteDeUbicar);
        }

        [TestMethod]
        public async Task BuscarProducto_CodigoVacioODesproporcionado_ListaVaciaSinIrALaBaseDeDatos()
        {
            var repositorio = A.Fake<IRepositorioUbicacionesAlmacen>();
            var servicio = new ServicioUbicacionesAlmacen(repositorio);

            Assert.AreEqual(0, (await servicio.BuscarProducto("1", "ALG", "  ")).Count);
            Assert.AreEqual(0, (await servicio.BuscarProducto("1", "ALG", new string('9', 40))).Count);
            A.CallTo(() => repositorio.BuscarProductos(A<string>._, A<string>._)).MustNotHaveHappened();
        }
    }
}
