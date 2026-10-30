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
    /// NestoAPI#765 (decisión 4 de #755): las notas de ENTREGA de solo texto («PRESTAMO HIDRA 7», «VAPOR DE CAMBIO») se empaquetan y
    /// etiquetan en Ariadna como un pedido. Antes el packing exigía TipoLinea = 1 y GET api/Almacen/Pedidos/{p}/Packing daba 404.
    /// </summary>
    [TestClass]
    public class PackingNotasSoloTextoTests
    {
        private const string EMPRESA = "1";
        private const int PICKING = 99963;
        private const int NOTA = 929179;

        private static FilaPackingAlmacen Texto(int pedido, int linea, string texto)
            => new FilaPackingAlmacen
            {
                Pedido = pedido, LineaPedido = linea, Producto = string.Empty, Descripcion = texto, Cantidad = 1, EsTexto = true,
                Cliente = "41006", Contacto = "0", Nombre = "DOTTI FRIENDS S.L", Ruta = "AT"
            };

        private static FilaPackingAlmacen Producto(int pedido, int linea, string producto, int cantidad)
            => new FilaPackingAlmacen
            {
                Pedido = pedido, LineaPedido = linea, Producto = producto, Descripcion = "PRODUCTO " + producto, CodigoBarras = "8437020512176",
                Cantidad = cantidad, Cliente = "41006", Contacto = "0", Nombre = "DOTTI FRIENDS S.L", Ruta = "AT"
            };

        [TestMethod]
        public void MontarPacking_LineaDeTexto_SinCodigoNiProducto_YaMetida()
        {
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[] { Texto(NOTA, 329324600, " HIDRA 7 REVISADA ") });

            LineaPackingAlmacenDTO linea = packing.Entregas.Single().Pedidos.Single().Lineas.Single();
            Assert.IsTrue(linea.EsTexto);
            Assert.AreEqual(string.Empty, linea.Producto);
            Assert.AreEqual("HIDRA 7 REVISADA", linea.Descripcion);
            Assert.IsNull(linea.CodigoBarras);
            Assert.IsFalse(linea.SinCodigo, "No es un producto sin código: no hay nada que escanear ni que tocar");
            Assert.AreEqual(1, linea.Cantidad);
            Assert.AreEqual(1, linea.Metidas, "Cuenta como metida");
            Assert.AreEqual(0, linea.Faltas);
        }

        [TestMethod]
        public void MontarPacking_LasLecturasNoTocanLasLineasDeTexto()
        {
            // Una lectura sin producto (no debería existir) no puede dejar la línea de texto a 0 metidas
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING,
                new[] { Texto(NOTA, 1, "PRESTAMO HIDRA 7"), Texto(NOTA, 2, "VAPOR DE CAMBIO") },
                new[] { new LecturaPackingAlmacen { Pedido = NOTA, Producto = "", Metidas = 0, Faltas = 1 } });

            List<LineaPackingAlmacenDTO> lineas = packing.Entregas.Single().Pedidos.Single().Lineas;
            Assert.AreEqual(2, lineas.Count);
            Assert.IsTrue(lineas.All(l => l.EsTexto && l.Metidas == 1 && l.Faltas == 0));
        }

        [TestMethod]
        public void MontarPacking_NotaDeTextoYPedidoDelMismoCliente_VanEnLaMismaEntrega()
        {
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[]
            {
                Producto(929200, 1, "17404", 2),
                Texto(NOTA, 1, "PRESTAMO HIDRA 7")
            });

            EntregaPackingAlmacenDTO entrega = packing.Entregas.Single();
            CollectionAssert.AreEqual(new[] { NOTA, 929200 }, entrega.Pedidos.Select(p => p.Pedido).ToList());
            Assert.IsFalse(entrega.Pedidos.Single(p => p.Pedido == 929200).Lineas.Single().EsTexto);
            Assert.AreEqual(0, entrega.Pedidos.Single(p => p.Pedido == 929200).Lineas.Single().Metidas, "El producto sí se escanea");
        }

        [TestMethod]
        public async Task LeerEstadoPedido_NotaDeSoloTexto_EstaCompletaSinEscanear()
        {
            var repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerLineasPacking(EMPRESA, PICKING, NOTA)).Returns(new List<FilaPackingAlmacen> { Texto(NOTA, 1, "PRESTAMO HIDRA 7") });
            A.CallTo(() => repositorio.LeerLecturas(EMPRESA, NOTA, PICKING, "PACK")).Returns(new List<LecturaProductoAlmacen>());
            A.CallTo(() => repositorio.LeerBultos(EMPRESA, NOTA)).Returns(new List<BultoAlmacenDTO>());
            var servicio = new ServicioPreparacionAlmacen(repositorio, A.Fake<IAlmacenFotosBultos>());

            EstadoPreparacionPedidoDTO estado = await servicio.LeerEstadoPedido(EMPRESA, NOTA, PICKING);

            Assert.IsTrue(estado.Completo);
            Assert.AreEqual(0, estado.Productos.Count, "La línea de texto no es un producto que casar");
        }

        [TestMethod]
        public async Task LeerEstadoPedido_NotaDeSoloTexto_ConAlgoLeidoQueNoEsSuyo_NoEstaCompleta()
        {
            var repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerLineasPacking(EMPRESA, PICKING, NOTA)).Returns(new List<FilaPackingAlmacen> { Texto(NOTA, 1, "PRESTAMO HIDRA 7") });
            A.CallTo(() => repositorio.LeerLecturas(EMPRESA, NOTA, PICKING, "PACK")).Returns(new List<LecturaProductoAlmacen>
            {
                new LecturaProductoAlmacen { Producto = "17404", Unidades = 1 }
            });
            A.CallTo(() => repositorio.LeerBultos(EMPRESA, NOTA)).Returns(new List<BultoAlmacenDTO>());
            var servicio = new ServicioPreparacionAlmacen(repositorio, A.Fake<IAlmacenFotosBultos>());

            EstadoPreparacionPedidoDTO estado = await servicio.LeerEstadoPedido(EMPRESA, NOTA, PICKING);

            Assert.IsFalse(estado.Completo);
        }

        [TestMethod]
        public void SqlLineasPacking_TieneLaRamaDeTexto_DeNotasDeEntregaNoDeRecogida()
        {
            string sql = RepositorioPreparacionAlmacen.SQL_LINEAS_PACKING;
            StringAssert.Contains(sql, "UNION ALL");
            StringAssert.Contains(sql, "CAST(1 AS bit) AS EsTexto");
            StringAssert.Contains(sql, "CAST(0 AS bit) AS EsTexto");
            StringAssert.Contains(sql, "1 AS Cantidad");
            StringAssert.Contains(sql, "'' AS Producto");
            StringAssert.Contains(sql, RepositorioPreparacionAlmacen.CONDICION_NOTA_SOLO_TEXTO);
            // La rama de producto sigue igual
            StringAssert.Contains(sql, "l.TipoLinea = 1 AND l.Estado = 1 AND ISNULL(p.Ficticio, 0) = 0");
            StringAssert.Contains(sql, "ORDER BY Cliente, Contacto, Pedido, LineaPedido");
        }

        [TestMethod]
        public void SqlEntregasPorEmpaquetar_TambienCuentaLasNotasDeSoloTexto()
        {
            string sql = RepositorioPreparacionAlmacen.SQL_ENTREGAS_POR_EMPAQUETAR;
            StringAssert.Contains(sql, "UNION ALL");
            StringAssert.Contains(sql, "1 AS Unidades");
            StringAssert.Contains(sql, RepositorioPreparacionAlmacen.CONDICION_NOTA_SOLO_TEXTO);
        }

        [TestMethod]
        public void CondicionNotaSoloTexto_TextoNoVacio_NotaDeEntrega_SinProductosEnElPicking_NoRecogida()
        {
            string condicion = RepositorioPreparacionAlmacen.CONDICION_NOTA_SOLO_TEXTO;
            StringAssert.Contains(condicion, "l.TipoLinea = 0");
            StringAssert.Contains(condicion, "c.NotaEntrega = 1");
            StringAssert.Contains(condicion, "LTRIM(RTRIM(ISNULL(l.Texto, ''))) <> ''");
            StringAssert.Contains(condicion, "RTRIM(ne.Tipo) = 'Recogida'");
            StringAssert.Contains(condicion, "x.Picking = l.Picking AND x.Estado = 1 AND x.TipoLinea = 1");
        }
    }
}
