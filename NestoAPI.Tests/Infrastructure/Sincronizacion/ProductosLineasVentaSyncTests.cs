using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#705 (incidencia 784, 39598): grabar, modificar o borrar líneas de pedido de venta pendientes
    /// tiene que volver a publicar el producto, porque cambia lo comprometido (PendienteEntregar) y con ello el
    /// CantidadDisponibleTotal que la tienda usa como stock. Antes solo se republicaba con movimientos en
    /// ExtractoProducto y la web vendía lo ya comprometido.
    /// </summary>
    [TestClass]
    public class ProductosLineasVentaSyncTests
    {
        private static LineaVentaStock L(string producto, short? cantidad = 1, string almacen = "ALG", short estado = -1,
            byte? tipoLinea = 1, string empresa = "1  ")
        {
            return new LineaVentaStock
            {
                Producto = producto,
                Cantidad = cantidad,
                Almacen = almacen,
                Estado = estado,
                TipoLinea = tipoLinea,
                Empresa = empresa
            };
        }

        private static List<string> Encolar(params CambioLineaVenta[] cambios)
        {
            return ProductosLineasVentaSync.ProductosAEncolar(cambios);
        }

        [TestMethod]
        public void LineaNuevaPendiente_EncolaElProductoRecortado()
        {
            var productos = Encolar(CambioLineaVenta.Alta(L("39598          ")));

            CollectionAssert.AreEqual(new[] { "39598" }, productos);
        }

        [TestMethod]
        public void LineaNuevaEnCurso_EncolaElProducto()
        {
            var productos = Encolar(CambioLineaVenta.Alta(L("39598", estado: 1, almacen: "REI")));

            CollectionAssert.AreEqual(new[] { "39598" }, productos);
        }

        [TestMethod]
        public void CambiaLaCantidad_EncolaElProducto()
        {
            var productos = Encolar(new CambioLineaVenta { Antes = L("39598", 2), Despues = L("39598", 6) });

            CollectionAssert.AreEqual(new[] { "39598" }, productos);
        }

        [TestMethod]
        public void SoloCambiaElPrecio_NoEncola()
        {
            // Mismos campos de stock antes y después (el precio no está en la foto): nada que publicar.
            var productos = Encolar(new CambioLineaVenta { Antes = L("39598", 2), Despues = L("39598", 2) });

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public void EfDiceQueNoSeHaTocadoNingunCampoDeStock_NoEncolaAunqueLaFotoDifiera()
        {
            var productos = Encolar(new CambioLineaVenta { Antes = L("39598", 2), Despues = L("39598", 6), CamposDeStockModificados = false });

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public void EntidadMarcadaEnteraComoModificada_EncolaAunqueOriginalesYActualesCoincidan()
        {
            // db.Entry(linea).State = Modified: EF no sabe los originales (son los actuales), pero IsModified viene a true.
            var productos = Encolar(new CambioLineaVenta { Antes = L("39598", 2), Despues = L("39598", 2), CamposDeStockModificados = true });

            CollectionAssert.AreEqual(new[] { "39598" }, productos);
        }

        [TestMethod]
        public void LineaBorrada_EncolaElProductoQueDejaDeEstarComprometido()
        {
            var productos = Encolar(CambioLineaVenta.Borrado(L("39598", 6)));

            CollectionAssert.AreEqual(new[] { "39598" }, productos);
        }

        [TestMethod]
        public void CambiaElProductoDeLaLinea_EncolaElViejoYElNuevo()
        {
            var productos = Encolar(new CambioLineaVenta { Antes = L("39598"), Despues = L("17404") });

            CollectionAssert.AreEquivalent(new[] { "39598", "17404" }, productos);
        }

        [TestMethod]
        public void PasarAPresupuesto_EncolaPorqueLiberaStock()
        {
            var productos = Encolar(new CambioLineaVenta { Antes = L("39598", estado: -1), Despues = L("39598", estado: -3) });

            CollectionAssert.AreEqual(new[] { "39598" }, productos);
        }

        [TestMethod]
        public void PresupuestoAPedido_EncolaPorqueComprometeStock()
        {
            var productos = Encolar(new CambioLineaVenta { Antes = L("39598", estado: -3), Despues = L("39598", estado: -1) });

            CollectionAssert.AreEqual(new[] { "39598" }, productos);
        }

        [TestMethod]
        public void LineasQueNoSonDeProducto_NoEncolan()
        {
            var productos = Encolar(
                CambioLineaVenta.Alta(L("62400003", tipoLinea: 2)),     // cuenta contable (portes)
                CambioLineaVenta.Alta(L("", tipoLinea: 0)),             // texto
                CambioLineaVenta.Alta(L("39598", tipoLinea: null)));

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public void OtrosAlmacenesYEmpresas_NoEncolan()
        {
            var productos = Encolar(
                CambioLineaVenta.Alta(L("39598", almacen: "MAD")),
                CambioLineaVenta.Alta(L("39598", empresa: "2  ")));

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public void EmpresaEspejoYAlmacenAlcobendas_Encolan()
        {
            var productos = Encolar(CambioLineaVenta.Alta(L("39598", almacen: "ALC", empresa: "3  ")));

            CollectionAssert.AreEqual(new[] { "39598" }, productos);
        }

        [TestMethod]
        public void LineasYaServidasOPresupuestos_NoEncolanAlDarlasDeAlta()
        {
            var productos = Encolar(
                CambioLineaVenta.Alta(L("39598", estado: -3)),
                CambioLineaVenta.Alta(L("39598", estado: 2)),
                CambioLineaVenta.Alta(L("39598", estado: 4)));

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public void VariasLineasDelMismoProducto_SeEncolaUnaVez()
        {
            var productos = Encolar(
                CambioLineaVenta.Alta(L("39598", 1)),
                CambioLineaVenta.Alta(L("39598  ", 1, "REI")),
                null);

            CollectionAssert.AreEqual(new[] { "39598" }, productos);
        }

        [TestMethod]
        public void SinCambios_ListaVacia()
        {
            Assert.AreEqual(0, ProductosLineasVentaSync.ProductosAEncolar(null).Count);
        }

        [TestMethod]
        public void Foto_CopiaLosCamposDeStockDeLaLinea()
        {
            var foto = ProductosLineasVentaSync.Foto(new LinPedidoVta
            {
                Empresa = "1  ", Producto = "39598", Almacén = "ALG", Estado = -1, TipoLinea = 1, Cantidad = 2
            });

            Assert.AreEqual("39598", foto.Producto);
            Assert.AreEqual("ALG", foto.Almacen);
            Assert.AreEqual((short)-1, foto.Estado);
            Assert.AreEqual((byte?)1, foto.TipoLinea);
            Assert.AreEqual((short?)2, foto.Cantidad);
            Assert.IsNull(ProductosLineasVentaSync.Foto(null));
        }

        /// <summary>
        /// Regresión de la incidencia 784 (27/10/26): 39598 con stock ALG 4 + REI 2 y 10 unidades ya pendientes.
        /// Entra un pedido nuevo (929392, 1 ud): antes no se encolaba nada y la tienda seguía con 2 unidades de la
        /// última publicación (21/10). Ahora el producto se encola y lo que viaja es CantidadDisponibleTotal = 0.
        /// </summary>
        [TestMethod]
        public void Caso39598_PedidoNuevoPendiente_EncolaElProductoYViajaDisponibleCero()
        {
            var productos = Encolar(CambioLineaVenta.Alta(L("39598", 1, "ALG", -1)));

            CollectionAssert.AreEqual(new[] { "39598" }, productos);

            var stocks = new[]
            {
                new ProductoDTO.StockProducto { Almacen = "ALG", Stock = 4, PendienteEntregar = 10 },
                new ProductoDTO.StockProducto { Almacen = "REI", Stock = 2 },
                new ProductoDTO.StockProducto { Almacen = "ALC", Stock = 0 }
            };
            Assert.AreEqual(0, ProductoDTO.CalcularCantidadDisponibleTotal(stocks));
        }

        [TestMethod]
        public async Task EncolarSinRomper_EncolaConElUsuarioDeLasLineasDeVenta()
        {
            var db = A.Fake<NVEntities>();
            A.CallTo(() => db.EncolarProductosLineasVentaSinRomperAsync(A<List<string>>._)).CallsBaseMethod();
            A.CallTo(() => db.EncolarProductosSync(A<IEnumerable<string>>._, A<string>._)).Returns(Task.FromResult(1));

            await db.EncolarProductosLineasVentaSinRomperAsync(new List<string> { "39598" });

            A.CallTo(() => db.EncolarProductosSync(
                A<IEnumerable<string>>.That.Matches(l => l.SequenceEqual(new[] { "39598" })),
                ProductosLineasVentaSync.USUARIO_ENCOLADO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EncolarSinRomper_SiFallaElEncolado_NoLanza()
        {
            // El pedido ya está grabado: un fallo al encolar se loguea y lo recoge el job nocturno.
            var db = A.Fake<NVEntities>();
            A.CallTo(() => db.EncolarProductosLineasVentaSinRomperAsync(A<List<string>>._)).CallsBaseMethod();
            A.CallTo(() => db.EncolarProductosSync(A<IEnumerable<string>>._, A<string>._))
                .Throws(new InvalidOperationException("Nesto_sync no responde"));

            await db.EncolarProductosLineasVentaSinRomperAsync(new List<string> { "39598" });
        }

        [TestMethod]
        public async Task EncolarSinRomper_SinProductos_NoVaALaBaseDeDatos()
        {
            var db = A.Fake<NVEntities>();
            A.CallTo(() => db.EncolarProductosLineasVentaSinRomperAsync(A<List<string>>._)).CallsBaseMethod();

            await db.EncolarProductosLineasVentaSinRomperAsync(new List<string>());
            await db.EncolarProductosLineasVentaSinRomperAsync(null);

            A.CallTo(() => db.EncolarProductosSync(A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }
    }

    /// <summary>
    /// NestoAPI#705: red de seguridad del job nocturno de stocks. Lo que se cambie fuera de la API (Nesto viejo, SPs)
    /// o cuyo encolado falle se recoge por la [Fecha Modificación] de la línea de venta.
    /// </summary>
    [TestClass]
    public class SincronizacionStocksJobsServiceSqlTests
    {
        private static string Sql => SincronizacionStocksJobsService.SQL_ENCOLAR_PRODUCTOS_CON_MOVIMIENTOS;

        [TestMethod]
        public void EncolaProductosConLineasDeVentaModificadasEnLaVentana()
        {
            StringAssert.Contains(Sql, "FROM LinPedidoVta l");
            StringAssert.Contains(Sql, "l.Empresa IN (@empresa, @empresaEspejo)");
            StringAssert.Contains(Sql, "l.Producto = p.Número");
            StringAssert.Contains(Sql, "l.Almacén IN (@almacen1, @almacen2, @almacen3)");
            StringAssert.Contains(Sql, "l.TipoLinea = 1");
            StringAssert.Contains(Sql, "l.[Fecha Modificación] >= @desde");
        }

        [TestMethod]
        public void LaLineaDeVentaEsUnaAlternativaMasDentroDelOr_NoUnFiltroQueRecorteLosMovimientos()
        {
            int indiceLineas = Sql.IndexOf("FROM LinPedidoVta l", StringComparison.Ordinal);
            string antes = Sql.Substring(0, indiceLineas);
            StringAssert.EndsWith(antes.TrimEnd().Replace("\r", ""), "OR EXISTS (SELECT 1");
        }

        [TestMethod]
        public void SigueSinReencolarLoQueYaEstaPendiente()
        {
            StringAssert.Contains(Sql, "AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns");
            StringAssert.Contains(Sql, "ns.Sincronizado IS NULL");
        }

        [TestMethod]
        public void MantieneLasCondicionesAnteriores()
        {
            StringAssert.Contains(Sql, "FROM ExtractoProducto e");
            StringAssert.Contains(Sql, "FROM Kits k");
            StringAssert.Contains(Sql, "FROM DescuentosProducto d");
        }
    }
}
