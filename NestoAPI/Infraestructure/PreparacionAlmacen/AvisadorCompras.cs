using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#559: lo que tiene que ver Compras de una recepción (un exceso recibido por almacén, una línea sin
    /// visto bueno) va al buzón de Nesto de cada miembro del grupo Compras del dominio; el buzón ya enciende la
    /// campana por SignalR. Mismo camino que el aviso diario de Verifactu (#522).
    /// </summary>
    public class AvisadorCompras : IAvisadorCompras
    {
        public const string TIPO_NOTIFICACION = "RecepcionCompras";
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<List<string>> usuariosDeCompras;

        public AvisadorCompras(IServicioNotificacionesPush notificaciones) : this(notificaciones, null)
        {
        }

        internal AvisadorCompras(IServicioNotificacionesPush notificaciones, Func<List<string>> usuariosDeCompras)
        {
            this.notificaciones = notificaciones;
            this.usuariosDeCompras = usuariosDeCompras ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.COMPRAS));
        }

        public async Task Avisar(string titulo, IEnumerable<string> avisos)
        {
            List<string> lista = (avisos ?? Enumerable.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (!lista.Any())
            {
                return;
            }
            List<string> usuarios = (usuariosDeCompras() ?? new List<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!usuarios.Any())
            {
                ElmahHelper.Log(new Exception($"[Recepción compras] No hay a quién avisar en Compras: {titulo}. {string.Join(" ", lista)}"));
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
