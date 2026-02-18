using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models.Sincronizacion;
using System.Text.Json;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    [TestClass]
    public class PrestashopProductosSyncHandlerTests
    {
        private PrestashopProductosSyncHandler _handler;

        [TestInitialize]
        public void Setup()
        {
            _handler = new PrestashopProductosSyncHandler();
        }

        #region TableName

        [TestMethod]
        public void TableName_DevuelvePrestashopProductos()
        {
            Assert.AreEqual("PrestashopProductos", _handler.TableName);
        }

        #endregion

        #region GetMessageKey

        [TestMethod]
        public void GetMessageKey_ConProductoYSource_DevuelveClaveCorrecta()
        {
            var message = new PrestashopProductoSyncMessage
            {
                Producto = "17404",
                Source = "Nesto"
            };

            var key = _handler.GetMessageKey(message);

            Assert.AreEqual("PRESTASHOPPRODUCTO|17404|Nesto", key);
        }

        [TestMethod]
        public void GetMessageKey_ConEspacios_TrimeaValores()
        {
            var message = new PrestashopProductoSyncMessage
            {
                Producto = "  17404  ",
                Source = "  Nesto  "
            };

            var key = _handler.GetMessageKey(message);

            Assert.AreEqual("PRESTASHOPPRODUCTO|17404|Nesto", key);
        }

        [TestMethod]
        public void GetMessageKey_ConMensajeNulo_DevuelveClaveConNulls()
        {
            var key = _handler.GetMessageKey((PrestashopProductoSyncMessage)null);

            Assert.AreEqual("PRESTASHOPPRODUCTO|NULL|NULL", key);
        }

        #endregion

        #region GetLogInfo

        [TestMethod]
        public void GetLogInfo_ConTodosLosCampos_IncluyeInformacionCompleta()
        {
            var message = new PrestashopProductoSyncMessage
            {
                Producto = "17404",
                NombrePersonalizado = "Sérum Vitamina C",
                Source = "Nesto",
                PVP_IVA_Incluido = 29.95m
            };

            var info = _handler.GetLogInfo(message);

            Assert.IsTrue(info.Contains("17404"));
            Assert.IsTrue(info.Contains("Sérum Vitamina C"));
            Assert.IsTrue(info.Contains("Nesto"));
            Assert.IsTrue(info.Contains("29,95") || info.Contains("29.95"));
        }

        [TestMethod]
        public void GetLogInfo_SinNombre_NoIncluyeParentesis()
        {
            var message = new PrestashopProductoSyncMessage
            {
                Producto = "17404",
                Source = "Nesto"
            };

            var info = _handler.GetLogInfo(message);

            Assert.IsTrue(info.Contains("17404"));
            Assert.IsFalse(info.Contains("("));
        }

        #endregion

        #region Deserialize

        [TestMethod]
        public void Deserialize_ConJsonValido_DevuelvePrestashopProductoSyncMessage()
        {
            var json = @"{
                ""Tabla"": ""PrestashopProductos"",
                ""Source"": ""Nesto"",
                ""Usuario"": ""admin"",
                ""Producto"": ""17404"",
                ""NombrePersonalizado"": ""Sérum Vitamina C"",
                ""Descripcion"": ""Descripción completa del producto"",
                ""DescripcionBreve"": ""Descripción breve"",
                ""PVP_IVA_Incluido"": 29.95
            }";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var result = _handler.Deserialize(json, options);

            Assert.IsInstanceOfType(result, typeof(PrestashopProductoSyncMessage));
            var message = (PrestashopProductoSyncMessage)result;
            Assert.AreEqual("PrestashopProductos", message.Tabla);
            Assert.AreEqual("Nesto", message.Source);
            Assert.AreEqual("17404", message.Producto);
            Assert.AreEqual("Sérum Vitamina C", message.NombrePersonalizado);
            Assert.AreEqual("Descripción completa del producto", message.Descripcion);
            Assert.AreEqual("Descripción breve", message.DescripcionBreve);
            Assert.AreEqual(29.95m, message.PVP_IVA_Incluido);
        }

        [TestMethod]
        public void Deserialize_ConCamposOpcionales_DevuelveMensajeConNulls()
        {
            var json = @"{
                ""Tabla"": ""PrestashopProductos"",
                ""Producto"": ""17404""
            }";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var result = (PrestashopProductoSyncMessage)_handler.Deserialize(json, options);

            Assert.AreEqual("17404", result.Producto);
            Assert.IsNull(result.NombrePersonalizado);
            Assert.IsNull(result.Descripcion);
            Assert.IsNull(result.DescripcionBreve);
            Assert.IsNull(result.PVP_IVA_Incluido);
        }

        #endregion

        #region HandleAsync

        [TestMethod]
        public async Task HandleAsync_ConMensajeNulo_DevuelveFalse()
        {
            var result = await _handler.HandleAsync((PrestashopProductoSyncMessage)null);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task HandleAsync_ConProductoVacio_DevuelveFalse()
        {
            var message = new PrestashopProductoSyncMessage
            {
                Producto = "",
                Source = "Nesto"
            };

            var result = await _handler.HandleAsync(message);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task HandleAsync_ConProductoNulo_DevuelveFalse()
        {
            var message = new PrestashopProductoSyncMessage
            {
                Producto = null,
                Source = "Nesto"
            };

            var result = await _handler.HandleAsync(message);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task HandleAsync_ConMensajeValido_DevuelveTrue()
        {
            var message = new PrestashopProductoSyncMessage
            {
                Tabla = "PrestashopProductos",
                Source = "Nesto",
                Producto = "17404",
                NombrePersonalizado = "Test"
            };

            var result = await _handler.HandleAsync(message);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task HandleAsync_VersionBase_ConMensajeValido_DevuelveTrue()
        {
            // Verifica que la versión polimórfica (ISyncTableHandlerBase) también funciona
            ISyncTableHandlerBase handlerBase = _handler;
            var message = new PrestashopProductoSyncMessage
            {
                Tabla = "PrestashopProductos",
                Source = "Nesto",
                Producto = "17404"
            };

            var result = await handlerBase.HandleAsync(message);

            Assert.IsTrue(result);
        }

        #endregion
    }
}
