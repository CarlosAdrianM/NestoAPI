using Newtonsoft.Json;
using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.OpenAI
{
    public class ServicioOpenAI : IServicioOpenAI
    {
        private const string ENDPOINT = "https://api.openai.com/v1/chat/completions";
        private const string MODELO_POR_DEFECTO = "gpt-4o-mini";

        private readonly string _apiKey;

        public ServicioOpenAI()
        {
            _apiKey = ConfigurationManager.AppSettings["OpenAIKey"];
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("No se ha configurado la API Key de OpenAI. Añade 'OpenAIKey' a appSettings en Web.config");
            }
        }

        /// <summary>
        /// Constructor para testing con API key inyectada
        /// </summary>
        public ServicioOpenAI(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        public async Task<string> GenerarContenidoAsync(
            string systemPrompt,
            string userMessage,
            int maxTokens = 500,
            double temperature = 0.4,
            string modelo = MODELO_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt))
                throw new ArgumentNullException(nameof(systemPrompt));
            if (string.IsNullOrWhiteSpace(userMessage))
                throw new ArgumentNullException(nameof(userMessage));

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var httpClient = new HttpClient(handler))
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

                var payload = new
                {
                    model = modelo,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userMessage }
                    },
                    max_tokens = maxTokens,
                    temperature = temperature
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json");

                var response = await httpClient.PostAsync(ENDPOINT, content).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    dynamic responseJson = JsonConvert.DeserializeObject<dynamic>(responseString);
                    return responseJson?.choices[0]?.message?.content?.ToString();
                }

                // Log del error para diagnóstico
                var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Error OpenAI: {response.StatusCode} - {errorContent}");

                return null;
            }
        }

        public async Task<string> GenerarCorreoHtmlAsync(string systemPrompt, string userMessage)
        {
            // Configuración optimizada para correos:
            // - 1500 tokens: suficiente para un email con HTML
            // - 0.3 temperature: poco creativo, más consistente
            // - gpt-4o-mini: económico pero capaz
            return await GenerarContenidoAsync(
                systemPrompt,
                userMessage,
                maxTokens: 1500,
                temperature: 0.3,
                modelo: MODELO_POR_DEFECTO
            ).ConfigureAwait(false);
        }
    }
}
