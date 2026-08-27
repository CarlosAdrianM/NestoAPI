using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorProductosTests
    {
        private ISincronizacionEventPublisher _publisher;
        private GestorProductos _gestor;

        [TestInitialize]
        public void Setup()
        {
            _publisher = A.Fake<ISincronizacionEventPublisher>();
            _gestor = new GestorProductos(new SincronizacionEventWrapper(_publisher));
        }

        private async Task<ProductoSyncMessage> PublicarYCapturar(ProductoDTO dto)
        {
            ProductoSyncMessage capturado = null;
            A.CallTo(() => _publisher.PublishEventAsync("sincronizacion-tablas", A<object>.Ignored))
                .Invokes((string _, object message) => capturado = message as ProductoSyncMessage);

            await _gestor.PublicarProductoSincronizar(dto);

            Assert.IsNotNull(capturado, "No se publicó ningún ProductoSyncMessage");
            return capturado;
        }

        [TestMethod]
        public async Task PublicarProductoSincronizar_ConTextosDeTienda_ViajanEnElMensaje()
        {
            // Cutover 26/08/2026: los textos editables de la tienda (antes en el mensaje de tabla
            // PrestashopProductos, retirado) viajan dentro del mensaje de Productos.
            var dto = new ProductoDTO
            {
                Producto = "17404",
                Nombre = "NOMBRE FICHA",
                NombrePersonalizado = "Nombre bonito para la web",
                Descripcion = "Descripción completa",
                DescripcionBreve = "Breve",
                PrecioProfesional = 24.60M,
                PrecioPublicoFinal = 42.52M
            };

            var mensaje = await PublicarYCapturar(dto);

            Assert.AreEqual("Nombre bonito para la web", mensaje.NombrePersonalizado);
            Assert.AreEqual("Descripción completa", mensaje.Descripcion);
            Assert.AreEqual("Breve", mensaje.DescripcionBreve);
            // Y el nombre de la ficha sigue viajando aparte: son dos cosas distintas
            Assert.AreEqual("NOMBRE FICHA", mensaje.Nombre);
        }

        [TestMethod]
        public async Task PublicarProductoSincronizar_SinTextosDeTienda_ViajanComoNull()
        {
            // null significa "sin personalización: NO tocar el texto que tenga la tienda". Si
            // viajara "" o el nombre de la ficha, el consumidor machacaría textos de la web.
            var dto = new ProductoDTO
            {
                Producto = "17404",
                Nombre = "NOMBRE FICHA",
                PrecioProfesional = 24.60M,
                PrecioPublicoFinal = 42.52M
            };

            var mensaje = await PublicarYCapturar(dto);

            Assert.IsNull(mensaje.NombrePersonalizado);
            Assert.IsNull(mensaje.Descripcion);
            Assert.IsNull(mensaje.DescripcionBreve);
        }

        [TestMethod]
        public async Task PublicarProductoSincronizar_ConKit_LaComposicionViajaConCantidades()
        {
            // NestoAPI#412: ProductosKit (lista plana, compatibilidad) tiraba las cantidades, y
            // sin ellas Odoo no puede construir la BoM del kit. ComponentesKit lleva la
            // composición completa.
            var dto = new ProductoDTO
            {
                Producto = "31573",
                Nombre = "KIT LIFTING",
                PrecioProfesional = 60M,
                PrecioPublicoFinal = 103.71M
            };
            dto.ProductosKit.Add(new ProductoKit { ProductoId = "17404", Cantidad = 2 });
            dto.ProductosKit.Add(new ProductoKit { ProductoId = "25000", Cantidad = 1 });

            var mensaje = await PublicarYCapturar(dto);

            CollectionAssert.AreEqual(new[] { "17404", "25000" },
                mensaje.ProductosKit.ToArray(), "La lista plana sigue viajando por compatibilidad");
            Assert.AreEqual(2, mensaje.ComponentesKit.Count);
            Assert.AreEqual("17404", mensaje.ComponentesKit[0].ProductoId);
            Assert.AreEqual(2, mensaje.ComponentesKit[0].Cantidad);
            Assert.AreEqual("25000", mensaje.ComponentesKit[1].ProductoId);
            Assert.AreEqual(1, mensaje.ComponentesKit[1].Cantidad);
        }

        [TestMethod]
        public async Task PublicarProductoSincronizar_ConStocks_LaCantidadMontableViajaDentroDelStock()
        {
            // NestoAPI#412: el montable viaja como campo APARTE dentro de cada stock; la
            // CantidadDisponible es siempre físico real y ningún consumidor debe verla inflada.
            var dto = new ProductoDTO
            {
                Producto = "31573",
                Nombre = "KIT LIFTING",
                PrecioProfesional = 60M,
                PrecioPublicoFinal = 103.71M
            };
            dto.Stocks.Add(new ProductoDTO.StockProducto { Almacen = "ALG", Stock = 3, CantidadMontable = 7 });

            var mensaje = await PublicarYCapturar(dto);

            Assert.AreEqual(3, mensaje.Stocks[0].CantidadDisponible, "El disponible es solo el físico");
            Assert.AreEqual(7, mensaje.Stocks[0].CantidadMontable);
        }
    }

    /// <summary>
    /// NestoAPI#412: la fórmula del stock montable de un kit desde sus componentes — el
    /// min-floor de la consulta legacy de la web (28/03/16), ahora en
    /// <c>ProductoService.CalcularKitsMontables</c>.
    /// </summary>
    [TestClass]
    public class CalcularKitsMontablesTests
    {
        private static int Montables(System.Collections.Generic.List<ProductoKit> componentes,
            System.Collections.Generic.Dictionary<string, int> disponibles)
        {
            return NestoAPI.Infraestructure.Kits.ProductoService.CalcularKitsMontables(componentes, disponibles);
        }

        [TestMethod]
        public void SinComponentes_NoEsUnKit_Cero()
        {
            Assert.AreEqual(0, Montables(new System.Collections.Generic.List<ProductoKit>(),
                new System.Collections.Generic.Dictionary<string, int>()));
            Assert.AreEqual(0, NestoAPI.Infraestructure.Kits.ProductoService.CalcularKitsMontables(null, null));
        }

        [TestMethod]
        public void ElMinimoDeLosFloor_ComoElLegacy()
        {
            // 10 uds del A (2 por kit) dan para 5; 7 del B (1 por kit) dan para 7 → mandan los 5.
            var componentes = new System.Collections.Generic.List<ProductoKit>
            {
                new ProductoKit { ProductoId = "A", Cantidad = 2 },
                new ProductoKit { ProductoId = "B", Cantidad = 1 }
            };
            var disponibles = new System.Collections.Generic.Dictionary<string, int> { { "A", 10 }, { "B", 7 } };

            Assert.AreEqual(5, Montables(componentes, disponibles));
        }

        [TestMethod]
        public void KitSinStockPropio_PeroConComponentes_EsMontable()
        {
            // El caso que motiva todo esto: hoy hay 37 kits vivos sin físico que la web daría por
            // agotados y que se pueden montar (26/08/2026).
            var componentes = new System.Collections.Generic.List<ProductoKit>
            {
                new ProductoKit { ProductoId = "A", Cantidad = 1 }
            };
            var disponibles = new System.Collections.Generic.Dictionary<string, int> { { "A", 4 } };

            Assert.AreEqual(4, Montables(componentes, disponibles));
        }

        [TestMethod]
        public void UnComponenteAgotado_BloqueaElKit()
        {
            var componentes = new System.Collections.Generic.List<ProductoKit>
            {
                new ProductoKit { ProductoId = "A", Cantidad = 1 },
                new ProductoKit { ProductoId = "B", Cantidad = 1 }
            };
            var disponibles = new System.Collections.Generic.Dictionary<string, int> { { "A", 100 }, { "B", 0 } };

            Assert.AreEqual(0, Montables(componentes, disponibles));
        }

        [TestMethod]
        public void ComponenteSinStockRegistrado_CuentaComoCero()
        {
            // Un componente que ni aparece en el diccionario (sin extracto en ese almacén) no es
            // "infinito": es 0 y bloquea, que es lo que pasa en el almacén de verdad.
            var componentes = new System.Collections.Generic.List<ProductoKit>
            {
                new ProductoKit { ProductoId = "A", Cantidad = 1 },
                new ProductoKit { ProductoId = "FANTASMA", Cantidad = 1 }
            };
            var disponibles = new System.Collections.Generic.Dictionary<string, int> { { "A", 100 } };

            Assert.AreEqual(0, Montables(componentes, disponibles));
        }

        [TestMethod]
        public void CantidadPorKitInvalida_NoLimita()
        {
            // Dato corrupto (cantidad 0 o negativa): dividir petaría y tratarlo como bloqueo
            // apagaría el kit por un error de mantenimiento. Se ignora ESE componente.
            var componentes = new System.Collections.Generic.List<ProductoKit>
            {
                new ProductoKit { ProductoId = "A", Cantidad = 0 },
                new ProductoKit { ProductoId = "B", Cantidad = 2 }
            };
            var disponibles = new System.Collections.Generic.Dictionary<string, int> { { "A", 1 }, { "B", 9 } };

            Assert.AreEqual(4, Montables(componentes, disponibles));
        }
    }
}
