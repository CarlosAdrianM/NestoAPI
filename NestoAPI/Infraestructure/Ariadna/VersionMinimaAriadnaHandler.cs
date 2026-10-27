using NestoAPI.Infraestructure.Notificaciones;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Ariadna
{
    /// <summary>
    /// NestoAPI#657: red de seguridad para las PDA que no se actualizan. Si la petición trae <c>X-Ariadna-Version</c>
    /// por debajo de <c>Ariadna:VersionMinima</c>:
    ///   - Sombra (por defecto): se apunta en ELMAH una vez por dispositivo y día, y la petición sigue.
    ///   - Activo: 426 Upgrade Required con el mensaje de usted y el código <see cref="PoliticaVersionMinimaAriadna.CODIGO_ERROR"/>.
    /// Sin cabecera no se hace nada: Nesto y NestoApp nunca la mandan, y las Ariadna anteriores a la issue gemela
    /// tampoco (se quedan en sombra sin saberlo). Va como DelegatingHandler para cubrir todos los endpoints que usa
    /// Ariadna (Almacen, Reposiciones, Recepciones, EnviosAgencias, Productos...) sin tocarlos uno a uno.
    /// </summary>
    public class VersionMinimaAriadnaHandler : DelegatingHandler
    {
        private readonly PoliticaVersionMinimaAriadna politica;
        private readonly Action<Exception> log;
        private readonly Func<DateTime> hoy;
        /// <summary>Dispositivo|versión → día en que ya se apuntó. Se vacía de días pasados al apuntar uno nuevo.</summary>
        private readonly ConcurrentDictionary<string, DateTime> apuntados = new ConcurrentDictionary<string, DateTime>();

        public VersionMinimaAriadnaHandler(PoliticaVersionMinimaAriadna politica, Action<Exception> log = null, Func<DateTime> hoy = null)
        {
            this.politica = politica ?? throw new ArgumentNullException(nameof(politica));
            this.log = log ?? (ex => ElmahHelper.Log(ex, "Sistema (versión mínima de Ariadna)"));
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string cabecera = Cabecera(request, PoliticaVersionMinimaAriadna.CABECERA_VERSION);
            if (cabecera == null || !politica.EstaDesactualizada(cabecera, out Version version))
            {
                return base.SendAsync(request, cancellationToken);
            }

            string dispositivo = Cabecera(request, AvisosAlmacen.CABECERA_DISPOSITIVO);
            string usuario = Usuario(request);
            Apuntar(version, dispositivo, usuario);

            if (!politica.Bloquea)
            {
                return base.SendAsync(request, cancellationToken);
            }
            return Task.FromResult(Rechazo(request, version));
        }

        /// <summary>426 con el mismo envoltorio que GlobalExceptionFilter ({ error: { code, message } }) y la mínima, por si Ariadna quiere enseñarla.</summary>
        internal HttpResponseMessage Rechazo(HttpRequestMessage request, Version version)
        {
            string cuerpo = JsonConvert.SerializeObject(new
            {
                error = new
                {
                    code = PoliticaVersionMinimaAriadna.CODIGO_ERROR,
                    message = PoliticaVersionMinimaAriadna.Mensaje(version),
                    version = version.ToString(),
                    versionMinima = politica.VersionMinima.ToString()
                }
            });
            // Sin request.CreateResponse: no depende de que la petición lleve HttpConfiguration (en los tests no la lleva)
            return new HttpResponseMessage((HttpStatusCode)426)
            {
                ReasonPhrase = "Upgrade Required",
                RequestMessage = request,
                Content = new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json")
            };
        }

        /// <summary>Una vez por dispositivo (o usuario, si no manda X-Dispositivo), versión y día.</summary>
        private void Apuntar(Version version, string dispositivo, string usuario)
        {
            DateTime dia = hoy();
            string clave = (string.IsNullOrWhiteSpace(dispositivo) ? "usuario:" + (usuario ?? "?") : "pda:" + dispositivo.Trim()) + "|" + version;
            if (apuntados.TryGetValue(clave, out DateTime apuntado) && apuntado == dia)
            {
                return;
            }
            apuntados[clave] = dia;
            foreach (string caducada in apuntados.Where(kv => kv.Value != dia).Select(kv => kv.Key).ToList())
            {
                apuntados.TryRemove(caducada, out _);
            }
            try
            {
                log(new Exception(politica.MensajeElmah(version, dispositivo, usuario)));
            }
            catch
            {
                // Apuntarlo nunca debe romper la petición.
            }
        }

        private static string Cabecera(HttpRequestMessage request, string nombre)
        {
            if (request?.Headers == null || !request.Headers.TryGetValues(nombre, out System.Collections.Generic.IEnumerable<string> valores))
            {
                return null;
            }
            string valor = valores.FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(valor) ? null : valor;
        }

        private static string Usuario(HttpRequestMessage request)
        {
            try
            {
                return request.GetRequestContext()?.Principal?.Identity?.Name;
            }
            catch
            {
                return null;
            }
        }
    }
}
