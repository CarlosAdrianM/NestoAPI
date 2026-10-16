using System;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>
    /// NestoAPI#552 (fase 2): interruptor de la tramitación de GLS por la API. <c>Agencias:GlsTramitarPorApi</c> en
    /// Web.config. APAGADO por defecto (falta la clave o no dice "true"): POST api/EnviosAgencias/{id}/Tramitar con un
    /// envío de GLS sigue dando el 400 de siempre y GLS se tramita desde Nesto (AgenciaASM). OJO: el valor del Web.config
    /// del repo es el que se publica (encendido a mano en el servidor, la siguiente publicación lo apagaría).
    /// </summary>
    public static class InterruptorTramitacionGls
    {
        public const string CLAVE = "Agencias:GlsTramitarPorApi";

        /// <summary>Lee el Web.config en cada llamada.</summary>
        public static bool EstaActivo() => EstaActivo(ConfigurationManager.AppSettings[CLAVE]);

        /// <summary>Solo "true" (sin distinguir mayúsculas) enciende: mandar envíos a GLS tiene que ser una decisión.</summary>
        public static bool EstaActivo(string valor)
            => string.Equals(valor?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>NestoAPI#552 (fase 2): el transporte de GrabaServicios (el WS b2b de GLS/ASM). Se fakea en los tests.</summary>
    public interface IClienteGrabaServiciosGls
    {
        /// <summary>URL a la que se manda (queda en AgenciasLlamadasWeb.UrlLlamada).</summary>
        string Url { get; }

        /// <summary>
        /// Manda el sobre SOAP y devuelve la respuesta cruda. Lanza <see cref="AgenciaRemotaException"/> si no hay
        /// respuesta que interpretar (sin conexión, timeout, HTTP de error); transitoria si puede arreglarse reintentando.
        /// </summary>
        Task<string> EnviarAsync(string soap);
    }

    /// <summary>
    /// NestoAPI#552 (fase 2): GrabaServicios por HTTP, igual que AgenciaASM.LlamadaWebService de Nesto: mismo URL,
    /// SOAPAction, content-type y 30 s de espera. No lleva credenciales: la cuenta va en el uidcliente de la petición
    /// (AgenciasTransporte.Identificador o el de BusinessParcel, ver <see cref="PeticionGls.Identificador"/>).
    /// </summary>
    public class ClienteGrabaServiciosGls : IClienteGrabaServiciosGls
    {
        private const string SOAP_ACTION = "\"http://www.asmred.com/GrabaServicios\"";
        private static readonly HttpClient ClienteCompartido = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly HttpClient _http;

        public ClienteGrabaServiciosGls(HttpClient http = null)
        {
            _http = http ?? ClienteCompartido;
        }

        public string Url => PeticionGls.URL_GRABA_SERVICIOS;

        public async Task<string> EnviarAsync(string soap)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, Url)
            {
                Content = new StringContent(soap ?? string.Empty, Encoding.UTF8, "text/xml")
            };
            _ = peticion.Headers.TryAddWithoutValidation("SOAPAction", SOAP_ACTION);
            _ = peticion.Headers.TryAddWithoutValidation("Accept", "text/xml");

            HttpResponseMessage respuesta;
            try
            {
                respuesta = await _http.SendAsync(peticion).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new AgenciaRemotaException("GLS (GrabaServicios): no se pudo conectar: " + ex.Message, ex) { EsTransitoria = true };
            }
            catch (TaskCanceledException ex)
            {
                throw new AgenciaRemotaException("GLS (GrabaServicios): la petición agotó el tiempo de espera.", ex) { EsTransitoria = true };
            }

            using (respuesta)
            {
                string cuerpo = respuesta.Content == null ? string.Empty : await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                int codigo = (int)respuesta.StatusCode;
                // Un SOAP Fault llega con HTTP 500 y cuerpo XML: se devuelve para que se audite y se interprete como rechazo
                // (no es «GLS no responde»). Un 5xx sin XML sí es su servidor caído.
                if (!respuesta.IsSuccessStatusCode && !EsXml(cuerpo))
                {
                    throw new AgenciaRemotaException($"GLS (GrabaServicios) respondió HTTP {codigo}: {Acotar(cuerpo)}")
                    {
                        EsTransitoria = codigo >= 500
                    };
                }
                return cuerpo;
            }
        }

        private static bool EsXml(string cuerpo) => cuerpo != null && cuerpo.TrimStart('﻿', ' ', '\r', '\n', '\t').StartsWith("<", StringComparison.Ordinal);

        private static string Acotar(string texto) => texto == null ? string.Empty : texto.Length <= 300 ? texto : texto.Substring(0, 300) + "…";
    }
}
