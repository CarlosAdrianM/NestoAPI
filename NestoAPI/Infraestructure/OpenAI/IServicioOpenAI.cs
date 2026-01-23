using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.OpenAI
{
    public interface IServicioOpenAI
    {
        /// <summary>
        /// Genera contenido usando OpenAI con un prompt de sistema y un mensaje de usuario.
        /// </summary>
        /// <param name="systemPrompt">Instrucciones para el modelo (rol, tono, formato)</param>
        /// <param name="userMessage">Contenido/datos a procesar</param>
        /// <param name="maxTokens">Máximo de tokens en la respuesta (default 500)</param>
        /// <param name="temperature">Creatividad 0-1 (default 0.4)</param>
        /// <param name="modelo">Modelo a usar (default gpt-4o-mini)</param>
        /// <returns>Texto generado por el modelo</returns>
        Task<string> GenerarContenidoAsync(
            string systemPrompt,
            string userMessage,
            int maxTokens = 500,
            double temperature = 0.4,
            string modelo = "gpt-4o-mini");

        /// <summary>
        /// Genera contenido HTML para un correo electrónico.
        /// Usa configuración optimizada para emails (temperatura baja, tokens suficientes).
        /// </summary>
        Task<string> GenerarCorreoHtmlAsync(string systemPrompt, string userMessage);
    }
}
