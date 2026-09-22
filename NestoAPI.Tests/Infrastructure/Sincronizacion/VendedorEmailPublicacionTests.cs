using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#504 / odoo-custom-addons#25: el VendedorEmail que viaja a Odoo. Contrato en los dos
    /// sentidos: propiedad AUSENTE = no tocar el vendedor; "" = sin vendedor. Antes, un vendedor sin
    /// Mail en la tabla Vendedores viajaba como null, y Odoo (que solo distingue "presente" de
    /// "ausente") le quitaba el comercial al cliente en silencio en cada republicación.
    /// </summary>
    [TestClass]
    public class VendedorEmailPublicacionTests
    {
        [TestMethod]
        public void VendedorEmailParaPublicar_VendedorConMail_ViajaElMail()
        {
            Assert.AreEqual("paloma@nuevavision.es", GestorClientes.VendedorEmailParaPublicar("PA ", " paloma@nuevavision.es "));
        }

        [TestMethod]
        public void VendedorEmailParaPublicar_VendedorSinMail_NoViaja()
        {
            // Regresión #504: es un dato mal puesto en Vendedores, no una desasignación.
            Assert.IsNull(GestorClientes.VendedorEmailParaPublicar("XX", null));
            Assert.IsNull(GestorClientes.VendedorEmailParaPublicar("XX", "   "), "Un Mail en blanco tampoco es un email resuelto");
        }

        [TestMethod]
        public void VendedorEmailParaPublicar_SinVendedorOVendedorGeneral_ViajaVacio()
        {
            // "" es la única forma legítima de decirle a Odoo "este cliente no tiene comercial".
            Assert.AreEqual(string.Empty, GestorClientes.VendedorEmailParaPublicar(null, null));
            Assert.AreEqual(string.Empty, GestorClientes.VendedorEmailParaPublicar("  ", null));
            Assert.AreEqual(string.Empty, GestorClientes.VendedorEmailParaPublicar(Constantes.Vendedores.VENDEDOR_GENERAL, "general@nuevavision.es"));
        }

        [TestMethod]
        public void ClienteSyncMessage_VendedorEmailNull_NoSeSerializa()
        {
            // Regresión #504: con el serializador por defecto la propiedad viajaba como null y Odoo la
            // leía como "quitar vendedor". Ahora ausente = no tocar.
            var mensaje = new ClienteSyncMessage { Tabla = "Clientes", Cliente = "12345", Vendedor = "XX", VendedorEmail = null };

            using (JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(mensaje)))
            {
                Assert.IsFalse(json.RootElement.TryGetProperty("VendedorEmail", out _), "VendedorEmail no debe viajar cuando es null");
                Assert.IsTrue(json.RootElement.TryGetProperty("Vendedor", out _), "El resto de propiedades sigue viajando igual");
            }
        }

        [TestMethod]
        public void ClienteSyncMessage_VendedorEmailVacioOInformado_SiViaja()
        {
            var sinVendedor = new ClienteSyncMessage { Tabla = "Clientes", Cliente = "12345", VendedorEmail = string.Empty };
            var conVendedor = new ClienteSyncMessage { Tabla = "Clientes", Cliente = "12345", VendedorEmail = "paloma@nuevavision.es" };

            using (JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(sinVendedor)))
            {
                Assert.AreEqual(string.Empty, json.RootElement.GetProperty("VendedorEmail").GetString());
            }
            using (JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(conVendedor)))
            {
                Assert.AreEqual("paloma@nuevavision.es", json.RootElement.GetProperty("VendedorEmail").GetString());
            }
        }

        [TestMethod]
        public void ClienteSyncMessage_LasDemasPropiedadesNulas_SiguenViajando()
        {
            // El contrato de las fechas (#498) y de los textos parciales se apoya en que las claves
            // null viajan presentes: el cambio de #504 es solo para VendedorEmail.
            var mensaje = new ClienteSyncMessage { Tabla = "Clientes", Cliente = "12345", Nombre = null, FechaUltimoPedido = null };

            using (JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(mensaje)))
            {
                Assert.IsTrue(json.RootElement.TryGetProperty("Nombre", out _));
                Assert.IsTrue(json.RootElement.TryGetProperty("FechaUltimoPedido", out _));
            }
        }
    }
}
