using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#559: recibir mercancía de proveedor leyendo los productos y casándolos con el pedido
    /// de compra. Lo importante en el albarán es que cuadren las unidades.
    /// </summary>
    [TestClass]
    public class ServicioRecepcionComprasTests
    {
        private const int PEDIDO = 220438;
        private IRepositorioRecepcionCompras repositorio;
        private ServicioRecepcionCompras servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioRecepcionCompras>();
            servicio = new ServicioRecepcionCompras(repositorio);
            A.CallTo(() => repositorio.LeerLineasPendientes("1", PEDIDO)).Returns(new List<FilaRecepcionCompra>
            {
                Fila(1, "37049", "8436601772282", 1),
                Fila(2, "39875", null, 1),
                Fila(3, "44243", "8436617966903", 3)
            });
        }

        private static FilaRecepcionCompra Fila(int linea, string producto, string codigo, int cantidad)
        {
            return new FilaRecepcionCompra
            {
                LineaPedido = linea,
                Proveedor = "413 ",
                NombreProveedor = "WEELKO BARCELONA SLU ",
                Producto = producto,
                Descripcion = "PRODUCTO " + producto,
                CodigoBarras = codigo,
                Cantidad = cantidad
            };
        }

        private static LecturaRecepcionDTO Lectura(string producto, int cantidad)
        {
            return new LecturaRecepcionDTO { Producto = producto, Cantidad = cantidad };
        }

        [TestMethod]
        public async Task LeerRecepcion_DevuelveLasLineasConSuCodigoYElProveedor()
        {
            RecepcionCompraDTO recepcion = await servicio.LeerRecepcion("1", PEDIDO);

            Assert.AreEqual("413", recepcion.Proveedor);
            Assert.AreEqual("WEELKO BARCELONA SLU", recepcion.NombreProveedor);
            Assert.AreEqual(3, recepcion.Lineas.Count);
            Assert.IsTrue(recepcion.Lineas.Single(l => l.Producto == "39875").SinCodigo);
        }

        [TestMethod]
        public async Task LeerRecepcion_PedidoSinNadaPendiente_Null()
        {
            A.CallTo(() => repositorio.LeerLineasPendientes("1", 1)).Returns(new List<FilaRecepcionCompra>());

            Assert.IsNull(await servicio.LeerRecepcion("1", 1));
        }

        [TestMethod]
        public async Task Casar_LlegaTodoLoPedido_Cuadra()
        {
            ResultadoRecepcionCompraDTO resultado = await servicio.Casar("1", PEDIDO,
                new[] { Lectura("37049", 1), Lectura("39875", 1), Lectura("44243", 3) });

            Assert.IsTrue(resultado.Cuadra);
        }

        [TestMethod]
        public async Task Casar_LlegaDeMenosDeMasYAlgoNoPedido_LoDiceProductoAProducto()
        {
            ResultadoRecepcionCompraDTO resultado = await servicio.Casar("1", PEDIDO,
                new[] { Lectura("37049", 2), Lectura("44243", 1), Lectura("99999", 5) });

            Assert.IsFalse(resultado.Cuadra);
            Assert.AreEqual(1, resultado.Productos.Single(p => p.Producto == "37049").Diferencia, "De más");
            Assert.AreEqual(-2, resultado.Productos.Single(p => p.Producto == "44243").Diferencia, "De menos");
            Assert.AreEqual(-1, resultado.Productos.Single(p => p.Producto == "39875").Diferencia, "No ha llegado");
            Assert.IsTrue(resultado.Productos.Single(p => p.Producto == "99999").Ajeno, "No se había pedido");
        }

        [TestMethod]
        public async Task Casar_SinLecturas_TodoFalta()
        {
            ResultadoRecepcionCompraDTO resultado = await servicio.Casar("1", PEDIDO, null);

            Assert.IsFalse(resultado.Cuadra);
            Assert.IsTrue(resultado.Productos.All(p => p.Leido == 0 && p.Diferencia < 0));
        }

        [TestMethod]
        public async Task Casar_PedidoSinNadaPendiente_Null()
        {
            A.CallTo(() => repositorio.LeerLineasPendientes("1", 1)).Returns(new List<FilaRecepcionCompra>());

            Assert.IsNull(await servicio.Casar("1", 1, new[] { Lectura("37049", 1) }));
        }
    }
}
