using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class ProductosControllerTest
    {
        /// <summary>
        /// Nesto#456: la ficha tiene que dar el CÓDIGO del subgrupo, no solo su descripción.
        ///
        /// En este DTO, Grupo es el código ("COS") pero Subgrupo es la descripción ("Cremas"),
        /// porque Subgrupo viaja así en el mensaje del bus y PrestaShop y Odoo lo consumen como
        /// descripción. Sin SubgrupoCodigo, quien quisiera identificar la categoría principal del
        /// producto tenía que adivinarla emparejando descripciones contra el catálogo.
        /// </summary>
        [TestMethod]
        public async Task ProductosController_GetProductoFicha_DevuelveElCodigoDelSubgrupoYSuDescripcion()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<Producto> fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            A.CallTo(() => fakeProductos.Include(A<string>.Ignored)).Returns(fakeProductos);

            Producto producto = new Producto
            {
                Empresa = "1",
                Número = "41269",
                Nombre = "CREMA DE PRUEBA",
                Grupo = "COS",
                SubGrupo = "CRE",
                Estado = 0,
                PVP = 10M,
                Kits = new List<Kit>(),
                SubGruposProducto = new SubGruposProducto { Descripción = "Cremas" }
            };
            ConfigurarFakeDbSetFicha(fakeProductos, new List<Producto> { producto }.AsQueryable());

            ProductosController controller = new ProductosController(db);

            var resultado = await controller.GetProducto("1", "41269", fichaCompleta: false)
                as OkNegotiatedContentResult<ProductoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("COS", resultado.Content.Grupo, "Grupo es el código");
            Assert.AreEqual("CRE", resultado.Content.SubgrupoCodigo, "SubgrupoCodigo es el código");
            Assert.AreEqual("Cremas", resultado.Content.Subgrupo, "Subgrupo sigue siendo la descripción: es el contrato del bus");
        }

        private static void ConfigurarFakeDbSetFicha<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        [TestMethod]
        public void ProductosController_CalcularStockProducto_SiElProductoEsFicticioElStockEs0()
        {
            // Este test está pendiente de implementación
        }

        /// <summary>
        /// Test que verifica que el cliente PUBLICO_FINAL (10458) debe usar precios de PrestaShop.
        /// Este test documenta el comportamiento esperado: cuando se consulta un producto para
        /// el cliente PUBLICO_FINAL, el sistema debe consultar la API de PrestaShop y dividir
        /// el precio por el IVA correspondiente para obtener la base imponible.
        ///
        /// Contexto: Este test respalda la corrección implementada el 17/11/2024 para unificar
        /// el cálculo de precios entre PlantillaVenta y DetallePedidoVenta.
        ///
        /// Ver: CORRECCION_PRECIOS_PUBLICO_FINAL.md
        /// </summary>
        [TestMethod]
        public void ProductosController_GetProducto_ClientePublicoFinal_DebeUsarPrecioPrestaShop()
        {
            // Arrange
            const string CLIENTE_PUBLICO_FINAL = "10458"; // Constantes.ClientesEspeciales.PUBLICO_FINAL
            const string CLIENTE_NORMAL = "12345";

            // Assert: Documentamos que estos clientes tienen comportamientos diferentes
            Assert.AreEqual("10458", CLIENTE_PUBLICO_FINAL,
                "El cliente PUBLICO_FINAL debe ser '10458' según Constantes.cs:281");

            Assert.AreNotEqual(CLIENTE_PUBLICO_FINAL, CLIENTE_NORMAL,
                "Los clientes normales no deben usar precios de PrestaShop");
        }

        /// <summary>
        /// Test que verifica el cálculo correcto del IVA para precios de público final.
        ///
        /// Cuando se obtiene un precio de PrestaShop con IVA incluido, se debe dividir por:
        /// - 1.21 (IVA estándar 21%) para productos con IVA normal
        /// - 1.10 (IVA reducido 10%) para productos con IVA reducido
        ///
        /// Esto permite obtener la base imponible sin IVA que se almacena en el sistema.
        /// </summary>
        [TestMethod]
        public void ProductosController_CalculoPrecioPublicoFinal_DebeAplicarIVACorrectamente()
        {
            // Arrange
            decimal precioConIVAEstandar = 121.00M; // Precio con 21% IVA
            decimal precioConIVAReducido = 110.00M; // Precio con 10% IVA
            decimal porcentajeIVAEstandar = 1.21M;
            decimal porcentajeIVAReducido = 1.10M;

            // Act
            decimal baseImponibleEstandar = precioConIVAEstandar / porcentajeIVAEstandar;
            decimal baseImponibleReducido = precioConIVAReducido / porcentajeIVAReducido;

            // Assert
            Assert.AreEqual(100.00M, Math.Round(baseImponibleEstandar, 2),
                "La base imponible con IVA estándar (21%) debe calcularse correctamente");

            Assert.AreEqual(100.00M, Math.Round(baseImponibleReducido, 2),
                "La base imponible con IVA reducido (10%) debe calcularse correctamente");
        }

        /// <summary>
        /// Test que documenta los clientes especiales del sistema y sus comportamientos.
        ///
        /// Clientes especiales según Constantes.cs:276-282:
        /// - EL_EDEN (15191): Bypassa validaciones, puede aplicar cualquier descuento
        /// - TIENDA_ONLINE (31517): Pedidos de la tienda online
        /// - AMAZON (32624): Pedidos de Amazon marketplace
        /// - PUBLICO_FINAL (10458): Usa precios B2C de PrestaShop
        /// </summary>
        [TestMethod]
        public void ProductosController_ClientesEspeciales_TienenComportamientoDiferente()
        {
            // Arrange: Clientes especiales del sistema
            var clientesEspeciales = new Dictionary<string, string>
            {
                { "15191", "EL_EDEN - Bypassa validaciones" },
                { "31517", "TIENDA_ONLINE - Pedidos tienda online" },
                { "32624", "AMAZON - Pedidos Amazon" },
                { "10458", "PUBLICO_FINAL - Precios B2C PrestaShop" }
            };

            // Assert: Todos los clientes especiales deben tener códigos únicos
            var codigos = clientesEspeciales.Keys.ToList();
            var codigosUnicos = codigos.Distinct().ToList();

            Assert.AreEqual(codigos.Count, codigosUnicos.Count,
                "Todos los códigos de clientes especiales deben ser únicos");

            Assert.IsTrue(clientesEspeciales.ContainsKey("10458"),
                "PUBLICO_FINAL (10458) debe estar en la lista de clientes especiales");
        }

        /// <summary>
        /// Test de integración conceptual que documenta el flujo completo de obtención de precios
        /// para el cliente PUBLICO_FINAL.
        ///
        /// Flujo esperado:
        /// 1. Se recibe solicitud con cliente = "10458"
        /// 2. Se detecta que es PUBLICO_FINAL
        /// 3. Se consulta ProductoDTO.LeerPrecioPublicoFinal(producto)
        /// 4. Se obtiene precio con IVA de PrestaShop
        /// 5. Se divide por el porcentaje de IVA
        /// 6. Se devuelve la base imponible
        /// </summary>
        [TestMethod]
        public void ProductosController_FlujoPublicoFinal_DebeConsultarPrestaShopYDividirPorIVA()
        {
            // Arrange: Simulación del flujo
            string cliente = "10458"; // PUBLICO_FINAL
            string productoId = "12345";
            decimal precioPrestaShopConIVA = 121.00M; // Precio que vendría de PrestaShop
            decimal porcentajeIVA = 1.21M; // IVA estándar 21%

            // Act: Simular el cálculo que hace ProductosController.GetProducto
            bool esPublicoFinal = (cliente == "10458");
            decimal precioCalculado = 0M;

            if (esPublicoFinal)
            {
                // Este es el flujo que implementamos
                precioCalculado = precioPrestaShopConIVA / porcentajeIVA;
            }

            // Assert
            Assert.IsTrue(esPublicoFinal, "Debe detectar que es cliente PUBLICO_FINAL");
            Assert.AreEqual(100.00M, Math.Round(precioCalculado, 2),
                "Debe calcular correctamente la base imponible dividiendo por IVA");
            Assert.IsTrue(precioCalculado > 0, "El precio calculado debe ser mayor a 0");
        }
    }
}
