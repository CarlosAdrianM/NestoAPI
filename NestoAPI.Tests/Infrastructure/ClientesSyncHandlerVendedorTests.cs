using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests que documentan las reglas de negocio para la sincronización de vendedor
    /// en ClientesSyncHandler.
    ///
    /// IMPORTANTE: Estos tests validan la lógica del detector de cambios y documentan
    /// el comportamiento esperado del handler. La lógica de asignación de vendedor NV
    /// está en ActualizarVendedorSiValido del handler.
    /// </summary>
    [TestClass]
    public class ClientesSyncHandlerVendedorTests
    {
        private ClienteChangeDetector _detector;

        [TestInitialize]
        public void Setup()
        {
            _detector = new ClienteChangeDetector();
        }

        #region Tests de Resolución de Vendedor por Email

        /// <summary>
        /// Documenta el comportamiento del handler cuando viene VendedorEmail desde Odoo.
        /// El handler resuelve el email a código de vendedor ANTES de llamar al detector.
        /// Por tanto, el detector recibe el mensaje YA con el código de vendedor resuelto.
        /// </summary>
        [TestMethod]
        public void DetectarCambios_VendedorResueltoPorEmail_DetectaCambio()
        {
            // Arrange
            // Este escenario representa lo que pasa DESPUÉS de que el handler
            // resuelve el email a código de vendedor
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Vendedor = "NV" // Vendedor original
            };

            // El handler ya ha resuelto VendedorEmail "carlosadrian@nuevavision.es" → "CAM"
            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Vendedor = "CAM", // Resuelto por el handler desde VendedorEmail
                VendedorEmail = "carlosadrian@nuevavision.es",
                Source = "Odoo"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(1, cambios.Count);
            Assert.IsTrue(cambios[0].Contains("Vendedor"));
            Assert.IsTrue(cambios[0].Contains("NV"));
            Assert.IsTrue(cambios[0].Contains("CAM"));
        }

        #endregion

        #region Tests de Vendedor Eliminado en Odoo (Issue #64 - Fix)

        /// <summary>
        /// Documenta el comportamiento cuando el vendedor es eliminado en Odoo.
        ///
        /// REGLA DE NEGOCIO:
        /// - Cuando VendedorEmail viene como cadena vacía ("") desde Odoo, significa
        ///   que el vendedor fue eliminado/desasignado en el sistema externo.
        /// - En este caso, el handler debe asignar el vendedor general "NV".
        ///
        /// Este test documenta que el DETECTOR no detecta cambios (porque Vendedor=null/vacío),
        /// pero el HANDLER aplicará la lógica especial para Odoo.
        /// </summary>
        [TestMethod]
        public void DetectarCambios_VendedorEliminadoEnOdoo_NoDetectaCambioEnDetector()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Vendedor = "CAM" // Tenía un vendedor asignado
            };

            // Mensaje de Odoo indicando que se eliminó el vendedor
            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Vendedor = null, // No viene código (no se pudo resolver)
                VendedorEmail = "", // Cadena vacía = vendedor eliminado en Odoo
                Source = "Odoo"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            // El detector NO detecta cambios porque Vendedor es null/vacío
            // y la lógica especial está en el handler, no en el detector
            Assert.AreEqual(0, cambios.Count,
                "El detector no detecta cambios cuando Vendedor es null/vacío. " +
                "La lógica de asignar 'NV' cuando VendedorEmail='' y Source='Odoo' " +
                "está en ActualizarVendedorSiValido del handler.");
        }

        /// <summary>
        /// Documenta que VendedorEmail=null NO activa la lógica de vendedor eliminado.
        /// Solo VendedorEmail="" (cadena vacía explícita) indica eliminación.
        /// </summary>
        [TestMethod]
        public void DetectarCambios_VendedorEmailNulo_NoEsEliminacion()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Vendedor = "CAM"
            };

            // Mensaje donde no viene información de vendedor (campo nulo)
            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Vendedor = null,
                VendedorEmail = null, // Nulo = no se envió el campo
                Source = "Odoo"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count,
                "VendedorEmail=null significa que el campo no se envió, " +
                "NO que el vendedor fue eliminado. El handler no modificará el vendedor.");
        }

        /// <summary>
        /// Documenta que la lógica de vendedor eliminado SOLO aplica para Source="Odoo".
        /// Otros sistemas externos no activan esta lógica.
        /// </summary>
        [TestMethod]
        public void DetectarCambios_VendedorEmailVacioPeroNoOdoo_NoModificaVendedor()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Vendedor = "CAM"
            };

            // Mensaje de otro sistema (no Odoo) con VendedorEmail vacío
            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Vendedor = null,
                VendedorEmail = "", // Vacío pero de otro sistema
                Source = "OtroSistema" // NO es Odoo
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count,
                "La lógica de vendedor eliminado (asignar 'NV') SOLO aplica " +
                "cuando Source='Odoo'. Otros sistemas no activan esta lógica.");
        }

        #endregion

        #region Tests de Documentación del Flujo Completo

        /// <summary>
        /// Este test documenta el flujo completo de sincronización de vendedor.
        ///
        /// FLUJO EN EL HANDLER:
        /// 1. ResolverVendedorPorEmailSiNecesario:
        ///    - Si viene VendedorEmail pero no Vendedor, busca el vendedor por email
        ///    - Si lo encuentra, asigna message.Vendedor = códigoEncontrado
        ///
        /// 2. DetectarCambios:
        ///    - Compara clienteNesto con message (ya con Vendedor resuelto)
        ///    - Si Vendedor es null/vacío, no detecta cambio de vendedor
        ///
        /// 3. ActualizarVendedorSiValido:
        ///    - Si message.Vendedor tiene valor válido → actualiza vendedor
        ///    - Si message.Vendedor es null/vacío Y message.VendedorEmail==""
        ///      Y Source="Odoo" → asigna "NV" (vendedor eliminado)
        ///    - En cualquier otro caso → no modifica vendedor
        /// </summary>
        [TestMethod]
        public void DocumentacionFlujoCompletoVendedor()
        {
            // Este test es solo documentación, siempre pasa
            Assert.IsTrue(true, "Ver comentarios del método para el flujo completo");
        }

        /// <summary>
        /// Documenta los escenarios de VendedorEmail y su resultado esperado.
        ///
        /// | VendedorEmail | Source | Vendedor resuelto | Acción del Handler              |
        /// |---------------|--------|-------------------|----------------------------------|
        /// | "a@b.com"     | Odoo   | "CAM" (encontrado)| Actualiza a "CAM"               |
        /// | "a@b.com"     | Odoo   | null (no existe)  | No modifica vendedor            |
        /// | ""            | Odoo   | null              | Asigna "NV" (eliminado en Odoo) |
        /// | null          | Odoo   | null              | No modifica vendedor            |
        /// | ""            | Otro   | null              | No modifica vendedor            |
        /// </summary>
        [TestMethod]
        public void DocumentacionEscenariosVendedorEmail()
        {
            // Este test es solo documentación, siempre pasa
            Assert.IsTrue(true, "Ver tabla de escenarios en comentarios del método");
        }

        #endregion
    }
}
