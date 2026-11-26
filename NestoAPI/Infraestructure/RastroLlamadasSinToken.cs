using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Security.Principal;
using System.Web;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// 26/11/26: endpoints que todavía aceptan llamadas SIN token (<c>GET api/ExtractosCliente/EnviarCorreoDomiciliacionDia</c> y
    /// <c>PUT api/ParametrosUsuario</c>). Antes de cerrarlos hay que saber quién los usa: cada llamada sin token deja UNA línea en ELMAH
    /// («[Sin token] {ruta} desde {IP}, User-Agent {…}») como mucho una vez al día por endpoint e IP (en memoria: tras reiniciar el
    /// sitio se vuelve a apuntar). No cambia la respuesta ni lanza nunca.
    /// </summary>
    public static class RastroLlamadasSinToken
    {
        private static readonly MemoriaRastroSinToken memoria = new MemoriaRastroSinToken();

        /// <summary>Si la llamada viene sin token, la apunta (una vez al día por ruta e IP). Nunca lanza.</summary>
        public static void Apuntar(HttpRequestMessage request, IPrincipal usuario, string ruta)
        {
            try
            {
                if (usuario?.Identity?.IsAuthenticated == true)
                {
                    return;
                }
                string ip = IpDe(request);
                if (!memoria.DebeApuntar(ruta, ip, DateTime.Now))
                {
                    return;
                }
                string userAgent = request?.Headers?.UserAgent?.ToString();
                ElmahHelper.Log(new Exception(Texto(ruta, ip, userAgent)));
            }
            catch
            {
                // El rastro nunca rompe la llamada
            }
        }

        /// <summary>«[Sin token] PUT api/ParametrosUsuario desde 10.0.0.5, User-Agent Nesto/1.0».</summary>
        internal static string Texto(string ruta, string ip, string userAgent)
        {
            string agente = string.IsNullOrWhiteSpace(userAgent) ? "(sin User-Agent)" : userAgent.Trim();
            string desde = string.IsNullOrWhiteSpace(ip) ? "(IP desconocida)" : ip.Trim();
            return $"[Sin token] {ruta} desde {desde}, User-Agent {agente}";
        }

        private static string IpDe(HttpRequestMessage request)
        {
            if (request == null)
            {
                return null;
            }
            if (request.Properties.TryGetValue("MS_HttpContext", out object contexto) && contexto is HttpContextBase http)
            {
                string reenviada = http.Request?.Headers?["X-Forwarded-For"];
                if (!string.IsNullOrWhiteSpace(reenviada))
                {
                    return reenviada.Split(',').First().Trim();
                }
                return http.Request?.UserHostAddress;
            }
            return null;
        }
    }

    /// <summary>LA REGLA de «una vez al día por endpoint e IP» (en memoria; el día es el natural).</summary>
    internal class MemoriaRastroSinToken
    {
        private readonly ConcurrentDictionary<string, DateTime> ultimaVez = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public bool DebeApuntar(string ruta, string ip, DateTime ahora)
        {
            string clave = (ruta ?? string.Empty).Trim() + "|" + (ip ?? string.Empty).Trim();
            bool apuntar = false;
            _ = ultimaVez.AddOrUpdate(clave,
                _ => { apuntar = true; return ahora.Date; },
                (_, dia) =>
                {
                    if (dia < ahora.Date)
                    {
                        apuntar = true;
                        return ahora.Date;
                    }
                    apuntar = false;
                    return dia;
                });
            return apuntar;
        }
    }
}
