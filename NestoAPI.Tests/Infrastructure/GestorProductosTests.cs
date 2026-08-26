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
    }
}
