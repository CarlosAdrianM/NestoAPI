using System;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>
    /// Cliente del tracking de GLS/ASM (web service MiraEnvios, operación GetExpCli). Solo lectura:
    /// consulta el estado de un albarán. Devuelve el XML crudo (sin namespaces) para que la estrategia
    /// lo interprete. El <c>uid</c> identifica nuestra cuenta GLS (hoy la de BusinessParcel, servicio 96,
    /// que es como salen casi todos los envíos); va en Web.config (GLS:UidSeguimiento).
    /// </summary>
    public interface IClienteTrackingGls
    {
        Task<XDocument> ConsultarAsync(string albaran);
    }

    public class ClienteTrackingGls : IClienteTrackingGls
    {
        private const string URL_BASE = "https://www.asmred.com/WebSrvs/MiraEnvios.asmx/GetExpCli";
        private static readonly HttpClient ClienteCompartido = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly string _uid;
        private readonly HttpClient _http;

        public ClienteTrackingGls(string uid, HttpClient http = null)
        {
            _uid = uid;
            _http = http ?? ClienteCompartido;
        }

        public async Task<XDocument> ConsultarAsync(string albaran)
        {
            string url = $"{URL_BASE}?codigo={Uri.EscapeDataString(albaran)}&uid={Uri.EscapeDataString(_uid ?? string.Empty)}";
            string cuerpo = await _http.GetStringAsync(url).ConfigureAwait(false);
            return XDocument.Parse(cuerpo);
        }
    }

    /// <summary>
    /// Seguimiento de envíos de GLS/ASM (solo lectura). Cumple <see cref="ISeguimientoAgenciaRemota"/>
    /// como Innovatrans, pero su WS es totalmente distinto: aquí se traduce la respuesta de GetExpCli al
    /// modelo común. GLS NO tramita server-side (eso lo hace el cliente de escritorio), por eso solo
    /// implementa seguimiento, no <see cref="IAgenciaRemota"/>.
    ///
    /// Mapa (GetExpCli da estado de cabecera, incidencia y pod):
    ///  - estado contiene DEVUEL/DEVOLUC -> Devuelto (terminal).
    ///  - estado contiene ENTREG         -> Entregado (FechaEntrega = pod, fecha real de entrega).
    ///  - incidencia distinta de "SIN INCIDENCIA" -> Incidentado (estado de paso).
    ///  - resto -> Tramitado.
    /// </summary>
    public class AgenciaRemotaGls : ISeguimientoAgenciaRemota
    {
        private const string SIN_INCIDENCIA = "SIN INCIDENCIA";

        private readonly IClienteTrackingGls _cliente;

        public AgenciaRemotaGls(IClienteTrackingGls cliente)
        {
            _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        }

        // GLS está rodada: sin logging detallado (NestoAPI#259).
        public bool LoggingDetallado => false;

        public async Task<SeguimientoEnvioRemoto> ConsultarSeguimientoAsync(string albaran)
        {
            if (string.IsNullOrWhiteSpace(albaran)) throw new ArgumentNullException(nameof(albaran));

            XDocument doc = await _cliente.ConsultarAsync(albaran).ConfigureAwait(false);
            XElement exp = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "exp");
            if (exp == null)
            {
                // GLS aún no tiene datos del envío: sigue tramitado, sin novedad.
                return new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Tramitado };
            }

            string estado = (Valor(exp, "estado") ?? string.Empty).ToUpperInvariant();
            string incidencia = (Valor(exp, "incidencia") ?? string.Empty).Trim();

            if (estado.Contains("DEVUEL") || estado.Contains("DEVOLUC"))
            {
                return new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Devuelto, Detalle = Valor(exp, "estado") };
            }
            if (estado.Contains("ENTREG"))
            {
                return new SeguimientoEnvioRemoto
                {
                    Estado = EstadoEnvioSeguimiento.Entregado,
                    FechaEntrega = ParsearFecha(Valor(exp, "pod")) ?? ParsearFecha(Valor(exp, "FPEntrega")),
                    Detalle = Valor(exp, "estado")
                };
            }
            if (!string.IsNullOrEmpty(incidencia) && !incidencia.Equals(SIN_INCIDENCIA, StringComparison.OrdinalIgnoreCase))
            {
                return new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Incidentado, Detalle = incidencia };
            }
            return new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Tramitado, Detalle = Valor(exp, "estado") };
        }

        private static string Valor(XElement elemento, string nombreLocal)
            => elemento.Elements().FirstOrDefault(e => e.Name.LocalName == nombreLocal)?.Value?.Trim();

        // Fechas de GLS: "dd/MM/yyyy H:mm:ss" (hora sin cero a la izquierda). es-ES lo parsea flexible.
        private static DateTime? ParsearFecha(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            return DateTime.TryParse(texto, new CultureInfo("es-ES"), DateTimeStyles.None, out DateTime fecha)
                ? fecha
                : (DateTime?)null;
        }
    }
}
