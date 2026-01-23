using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.OpenAI;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.OpenAI
{
    [TestClass]
    public class ServicioOpenAITests
    {
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ConApiKeyNull_LanzaExcepcion()
        {
            // Act
            var servicio = new ServicioOpenAI(null);
        }

        [TestMethod]
        public void Constructor_ConApiKeyValida_NoLanzaExcepcion()
        {
            // Arrange & Act
            var servicio = new ServicioOpenAI("test-api-key");

            // Assert
            Assert.IsNotNull(servicio);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task GenerarContenidoAsync_ConSystemPromptNull_LanzaExcepcion()
        {
            // Arrange
            var servicio = new ServicioOpenAI("test-api-key");

            // Act
            await servicio.GenerarContenidoAsync(null, "user message");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task GenerarContenidoAsync_ConSystemPromptVacio_LanzaExcepcion()
        {
            // Arrange
            var servicio = new ServicioOpenAI("test-api-key");

            // Act
            await servicio.GenerarContenidoAsync("", "user message");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task GenerarContenidoAsync_ConUserMessageNull_LanzaExcepcion()
        {
            // Arrange
            var servicio = new ServicioOpenAI("test-api-key");

            // Act
            await servicio.GenerarContenidoAsync("system prompt", null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public async Task GenerarContenidoAsync_ConUserMessageVacio_LanzaExcepcion()
        {
            // Arrange
            var servicio = new ServicioOpenAI("test-api-key");

            // Act
            await servicio.GenerarContenidoAsync("system prompt", "   ");
        }

        // Tests de integración (requieren API key real - descomentar para probar manualmente)
        /*
        [TestMethod]
        [TestCategory("Integration")]
        public async Task GenerarContenidoAsync_ConDatosReales_DevuelveRespuesta()
        {
            // Arrange
            var servicio = new ServicioOpenAI(); // Usa API key de config
            var systemPrompt = "Eres un asistente que responde en una sola palabra.";
            var userMessage = "¿De qué color es el cielo?";

            // Act
            var resultado = await servicio.GenerarContenidoAsync(systemPrompt, userMessage, maxTokens: 10);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Length > 0);
        }
        */
    }
}
