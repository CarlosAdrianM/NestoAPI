using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Avisar de las diferencias de una reposición (buzón y campana de Nesto). Nunca debe romper la entrada.</summary>
    public interface IAvisadorReposiciones
    {
        /// <param name="destinatario">Quien creó el traspaso (con o sin dominio). Null: el grupo Almacén.</param>
        Task Avisar(string destinatario, string titulo, IEnumerable<string> lineas);
    }

    /// <summary>
    /// NestoAPI#553 (Carlos, 04/10/26): cuando lo leído al recibir una reposición no coincide con lo enviado, entra lo leído
    /// y se informa a quien creó el traspaso (o, si no se sabe, al grupo Almacén), al buzón de Nesto: el mismo camino que
    /// los avisos a Compras de las recepciones (<see cref="AvisadorCompras"/>).
    /// </summary>
    public class AvisadorReposiciones : IAvisadorReposiciones
    {
        public const string TIPO_NOTIFICACION = "RecepcionReposicion";
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<List<string>> usuariosDeAlmacen;

        public AvisadorReposiciones(IServicioNotificacionesPush notificaciones) : this(notificaciones, null)
        {
        }

        internal AvisadorReposiciones(IServicioNotificacionesPush notificaciones, Func<List<string>> usuariosDeAlmacen)
        {
            this.notificaciones = notificaciones;
            this.usuariosDeAlmacen = usuariosDeAlmacen ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ALMACEN));
        }

        public async Task Avisar(string destinatario, string titulo, IEnumerable<string> lineas)
        {
            List<string> lista = (lineas ?? Enumerable.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (!lista.Any())
            {
                return;
            }
            List<string> usuarios = (string.IsNullOrWhiteSpace(destinatario) ? usuariosDeAlmacen() ?? new List<string>() : new List<string> { destinatario })
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!usuarios.Any())
            {
                ElmahHelper.Log(new Exception($"[Recepción reposiciones] No hay a quién avisar: {titulo}. {string.Join(" ", lista)}"));
                return;
            }

            var notificacion = new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = string.Join(Environment.NewLine, lista),
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_NOTIFICACION }
            };
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
        }
    }
}
