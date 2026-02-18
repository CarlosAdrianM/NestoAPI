using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models.Sincronizacion;
using System.Text.Json;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    [TestClass]
    public class SyncTableRouterTests
    {
        private SyncTableRouter _router;

        [TestInitialize]
        public void Setup()
        {
            var handlers = new ISyncTableHandlerBase[]
            {
                new ClientesSyncHandler(),
                new ProductosSyncHandler(),
                new PrestashopProductosSyncHandler()
            };
            _router = new SyncTableRouter(handlers);
        }

        #region GetHandler(string tableName)

        [TestMethod]
        public void GetHandler_PorString_Clientes_DevuelveClientesSyncHandler()
        {
            var handler = _router.GetHandler("Clientes");

            Assert.IsNotNull(handler);
            Assert.AreEqual("Clientes", handler.TableName);
        }

        [TestMethod]
        public void GetHandler_PorString_Productos_DevuelveProductosSyncHandler()
        {
            var handler = _router.GetHandler("Productos");

            Assert.IsNotNull(handler);
            Assert.AreEqual("Productos", handler.TableName);
        }

        [TestMethod]
        public void GetHandler_PorString_PrestashopProductos_DevuelvePrestashopHandler()
        {
            var handler = _router.GetHandler("PrestashopProductos");

            Assert.IsNotNull(handler);
            Assert.AreEqual("PrestashopProductos", handler.TableName);
        }

        [TestMethod]
        public void GetHandler_PorString_TablaDesconocida_DevuelveNull()
        {
            var handler = _router.GetHandler("TablaInexistente");

            Assert.IsNull(handler);
        }

        [TestMethod]
        public void GetHandler_PorString_Null_DevuelveNull()
        {
            var handler = _router.GetHandler((string)null);

            Assert.IsNull(handler);
        }

        [TestMethod]
        public void GetHandler_PorString_Vacio_DevuelveNull()
        {
            var handler = _router.GetHandler("");

            Assert.IsNull(handler);
        }

        [TestMethod]
        public void GetHandler_PorString_CaseInsensitive_DevuelveHandler()
        {
            // El router usa StringComparer.OrdinalIgnoreCase en el diccionario
            var handler = _router.GetHandler("prestashopproductos");

            Assert.IsNotNull(handler);
            Assert.AreEqual("PrestashopProductos", handler.TableName);
        }

        #endregion

        #region Deserialize integration (handler.Deserialize vía router)

        [TestMethod]
        public void Deserialize_PrestashopProductos_ViaRouter_DevuelveTipoCorrecto()
        {
            var json = @"{
                ""Tabla"": ""PrestashopProductos"",
                ""Source"": ""Nesto"",
                ""Producto"": ""17404"",
                ""NombrePersonalizado"": ""Test"",
                ""PVP_IVA_Incluido"": 29.95
            }";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var handler = _router.GetHandler("PrestashopProductos");
            var message = handler.Deserialize(json, options);

            Assert.IsInstanceOfType(message, typeof(PrestashopProductoSyncMessage));
            var typed = (PrestashopProductoSyncMessage)message;
            Assert.AreEqual("17404", typed.Producto);
            Assert.AreEqual(29.95m, typed.PVP_IVA_Incluido);
        }

        [TestMethod]
        public void Deserialize_Productos_ViaRouter_DevuelveTipoCorrecto()
        {
            var json = @"{
                ""Tabla"": ""Productos"",
                ""Source"": ""Odoo"",
                ""Producto"": ""17404"",
                ""PrecioProfesional"": 15.50
            }";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var handler = _router.GetHandler("Productos");
            var message = handler.Deserialize(json, options);

            Assert.IsInstanceOfType(message, typeof(ProductoSyncMessage));
            var typed = (ProductoSyncMessage)message;
            Assert.AreEqual("17404", typed.Producto);
            Assert.AreEqual(15.50m, typed.PrecioProfesional);
        }

        [TestMethod]
        public void Deserialize_Clientes_ViaRouter_DevuelveTipoCorrecto()
        {
            var json = @"{
                ""Tabla"": ""Clientes"",
                ""Source"": ""Odoo"",
                ""Cliente"": ""12345"",
                ""Contacto"": ""0"",
                ""Nombre"": ""Test Cliente""
            }";
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var handler = _router.GetHandler("Clientes");
            var message = handler.Deserialize(json, options);

            Assert.IsInstanceOfType(message, typeof(ClienteSyncMessage));
            var typed = (ClienteSyncMessage)message;
            Assert.AreEqual("12345", typed.Cliente);
            Assert.AreEqual("Test Cliente", typed.Nombre);
        }

        #endregion

        #region GetSupportedTables

        [TestMethod]
        public void GetSupportedTables_IncluyePrestashopProductos()
        {
            var tables = _router.GetSupportedTables();

            CollectionAssert.Contains(new System.Collections.ArrayList(
                new System.Collections.Generic.List<string>(tables)),
                "PrestashopProductos");
        }

        #endregion
    }
}
