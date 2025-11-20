using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class ClienteChangeDetectorTests
    {
        private ClienteChangeDetector _detector;

        [TestInitialize]
        public void Setup()
        {
            _detector = new ClienteChangeDetector();
        }

        [TestMethod]
        public void DetectarCambios_ClienteNulo_RetornaClienteNuevo()
        {
            // Arrange
            Cliente clienteNesto = null;
            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Nuevo Cliente"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(1, cambios.Count);
            Assert.AreEqual("CLIENTE_NUEVO", cambios.First());
        }

        [TestMethod]
        public void DetectarCambios_MismosValores_RetornaListaVacia()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Teléfono = "666111222",
                Dirección = "Calle Test 123",
                Población = "Madrid",
                CodPostal = "28001",
                Provincia = "Madrid",
                CIF_NIF = "B12345678",
                Comentarios = "Sin comentarios"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Telefono = "666111222",
                Direccion = "Calle Test 123",
                Poblacion = "Madrid",
                CodigoPostal = "28001",
                Provincia = "Madrid",
                Nif = "B12345678",
                Comentarios = "Sin comentarios"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "No debería detectar cambios cuando los valores son iguales");
        }

        [TestMethod]
        public void DetectarCambios_TelefonoDiferente_DetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Teléfono = "666111111",
                Dirección = "Calle Test 123",
                Población = "Madrid",
                CodPostal = "28001"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Telefono = "666222222", // Cambio aquí
                Direccion = "Calle Test 123",
                Poblacion = "Madrid",
                CodigoPostal = "28001"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(1, cambios.Count);
            Assert.IsTrue(cambios.First().Contains("Teléfono"));
            Assert.IsTrue(cambios.First().Contains("666111111"));
            Assert.IsTrue(cambios.First().Contains("666222222"));
        }

        [TestMethod]
        public void DetectarCambios_MultiplesValoresDiferentes_DetectaTodosCambios()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Viejo",
                Teléfono = "666111111",
                Dirección = "Calle Vieja 1",
                Población = "Barcelona",
                CodPostal = "08001"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Nuevo",
                Telefono = "666222222",
                Direccion = "Calle Nueva 2",
                Poblacion = "Madrid",
                CodigoPostal = "28001"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(5, cambios.Count, "Deberían detectarse 5 cambios");
            Assert.IsTrue(cambios.Any(c => c.Contains("Nombre")));
            Assert.IsTrue(cambios.Any(c => c.Contains("Teléfono")));
            Assert.IsTrue(cambios.Any(c => c.Contains("Dirección")));
            Assert.IsTrue(cambios.Any(c => c.Contains("Población")));
            Assert.IsTrue(cambios.Any(c => c.Contains("CodPostal")));
        }

        [TestMethod]
        public void DetectarCambios_EspaciosExtra_NormalizaYNoDetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "  Cliente Test  ", // Espacios extra
                Teléfono = "666111222"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test", // Sin espacios
                Telefono = "666111222"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "Los espacios extra deben normalizarse");
        }

        [TestMethod]
        public void DetectarCambios_CaseInsensitive_NoDetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "CLIENTE TEST",
                Población = "MADRID"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "cliente test",
                Poblacion = "madrid"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "La comparación debe ser case-insensitive");
        }

        [TestMethod]
        public void DetectarCambios_ValorNullVsVacio_NoDetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = null // Null
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "" // Vacío
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "Null y vacío deben considerarse iguales");
        }

        [TestMethod]
        public void DetectarCambiosPersonaContacto_PersonaNula_RetornaPersonaNueva()
        {
            // Arrange
            PersonaContactoCliente personaNesto = null;
            var personaExterna = new PersonaContactoSyncDTO
            {
                Nombre = "Juan Pérez"
            };

            // Act
            var cambios = _detector.DetectarCambiosPersonaContacto(personaNesto, personaExterna);

            // Assert
            Assert.AreEqual(1, cambios.Count);
            Assert.AreEqual("PERSONA_CONTACTO_NUEVA", cambios.First());
        }

        [TestMethod]
        public void DetectarCambiosPersonaContacto_MismosValores_RetornaListaVacia()
        {
            // Arrange
            var personaNesto = new PersonaContactoCliente
            {
                Nombre = "Juan Pérez",
                Teléfono = "666333444",
                CorreoElectrónico = "juan@test.com"
            };

            var personaExterna = new PersonaContactoSyncDTO
            {
                Nombre = "Juan Pérez",
                Telefonos = "666333444",
                CorreoElectronico = "juan@test.com"
            };

            // Act
            var cambios = _detector.DetectarCambiosPersonaContacto(personaNesto, personaExterna);

            // Assert
            Assert.AreEqual(0, cambios.Count);
        }

        [TestMethod]
        public void DetectarCambiosPersonaContacto_EmailDiferente_DetectaCambio()
        {
            // Arrange
            var personaNesto = new PersonaContactoCliente
            {
                Nombre = "Juan Pérez",
                CorreoElectrónico = "juan.viejo@test.com"
            };

            var personaExterna = new PersonaContactoSyncDTO
            {
                Nombre = "Juan Pérez",
                CorreoElectronico = "juan.nuevo@test.com"
            };

            // Act
            var cambios = _detector.DetectarCambiosPersonaContacto(personaNesto, personaExterna);

            // Assert
            Assert.AreEqual(1, cambios.Count);
            Assert.IsTrue(cambios.First().Contains("Email"));
        }

        #region Tests de Normalización de Comentarios

        [TestMethod]
        public void DetectarCambios_ComentariosConHTMLYOrdenDiferente_NoDetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = "<p>[Teléfonos extra] 649172403\nA/A Mª JOSÉ: 660101678</p>"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "A/A Mª JOSÉ: 660101678\n[Teléfonos extra] 649172403"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "Los comentarios con HTML y diferente orden deben considerarse iguales");
        }

        [TestMethod]
        public void DetectarCambios_ComentariosConDiferentesSaltosLinea_NoDetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = "Línea 1\r\nLínea 2\r\nLínea 3"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "Línea 1\nLínea 2\nLínea 3"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "Los comentarios con diferentes saltos de línea (\\r\\n vs \\n) deben considerarse iguales");
        }

        [TestMethod]
        public void DetectarCambios_ComentariosHTMLVsTextoPlano_NoDetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = "<p>Este es un comentario importante</p>"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "Este es un comentario importante"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "Los comentarios con HTML vs texto plano deben considerarse iguales si el contenido es el mismo");
        }

        [TestMethod]
        public void DetectarCambios_ComentariosConLineasEnOrdenInverso_NoDetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = "Teléfono: 600111222\nEmail: test@example.com\nHorario: 9-18h"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "Email: test@example.com\nHorario: 9-18h\nTeléfono: 600111222"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "Los comentarios con las mismas líneas en diferente orden deben considerarse iguales");
        }

        [TestMethod]
        public void DetectarCambios_ComentariosConContenidoDiferente_DetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = "Cliente VIP"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "Cliente NORMAL"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(1, cambios.Count, "Los comentarios con contenido diferente deben detectar cambios");
            Assert.IsTrue(cambios.First().Contains("Comentarios"));
        }

        [TestMethod]
        public void DetectarCambios_ComentariosConHTMLComplejoDiferente_DetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = "<p>Teléfono: 600111222</p><p>Email: test@example.com</p>"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "<p>Teléfono: 600333444</p><p>Email: otro@example.com</p>"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(1, cambios.Count, "Los comentarios con contenido diferente deben detectar cambios");
            Assert.IsTrue(cambios.First().Contains("Comentarios"));
        }

        [TestMethod]
        public void DetectarCambios_ComentariosConEspaciosYHTMLExtra_NoDetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = "  <p>  Comentario importante  </p>  "
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "Comentario importante"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "Los espacios extra y HTML deben normalizarse");
        }

        [TestMethod]
        public void DetectarCambios_ComentarioNullVsHTMLVacio_NoDetectaCambio()
        {
            // Arrange
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = null
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "<p></p>"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "Comentario null debe ser igual a HTML vacío");
        }

        [TestMethod]
        public void DetectarCambios_ComentariosRealCasoUsuario_NoDetectaCambio()
        {
            // Arrange - Caso real del usuario
            var clienteNesto = new Cliente
            {
                Nombre = "Cliente Test",
                Comentarios = "<p>[Teléfonos extra] 649172403\nA/A Mª JOSÉ: 660101678</p>"
            };

            var clienteExterno = new ClienteSyncMessage
            {
                Nombre = "Cliente Test",
                Comentarios = "A/A Mª JOSÉ: 660101678\n[Teléfonos extra] 649172403"
            };

            // Act
            var cambios = _detector.DetectarCambios(clienteNesto, clienteExterno);

            // Assert
            Assert.AreEqual(0, cambios.Count, "El caso real del usuario debe funcionar correctamente");
        }

        #endregion
    }
}
