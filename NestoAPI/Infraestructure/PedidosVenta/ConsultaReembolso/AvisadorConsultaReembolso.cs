using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso
{
    /// <summary>NestoAPI#734: a quién se avisa de una consulta de reembolso y de su respuesta.</summary>
    public interface IAvisadorConsultaReembolso
    {
        /// <summary>Campana de Nesto (buzón + SignalR) a todo el grupo Administración. Devuelve a cuántos ha avisado.</summary>
        Task<int> AvisarAdministracion(NotificacionPushDTO notificacion);

        /// <summary>A quien preguntó: campana de Nesto (con dominio) y Avisos de Ariadna (sin dominio), como #698.</summary>
        Task AvisarA(string usuario, NotificacionPushDTO notificacion);
    }

    /// <summary>
    /// NestoAPI#734: los avisos van al buzón, como los de los retenidos (<see cref="Retencion.AvisadorRetencionAlmacen"/>): en Nesto,
    /// GuardarEnBuzonDeUsuario los deja en la campana y avisa por SignalR (#536); en Ariadna, además, manda la push de datos a la PDA
    /// donde ha entrado ese usuario (#575).
    /// </summary>
    public class AvisadorConsultaReembolso : IAvisadorConsultaReembolso
    {
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<List<string>> usuariosDeAdministracion;

        public AvisadorConsultaReembolso() : this(null, null)
        {
        }

        internal AvisadorConsultaReembolso(IServicioNotificacionesPush notificaciones, Func<List<string>> usuariosDeAdministracion)
        {
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.usuariosDeAdministracion = usuariosDeAdministracion ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ADMINISTRACION));
        }

        /// <summary>El grupo Administración, limpio y sin repetidos (con dominio).</summary>
        internal List<string> Destinatarios()
        {
            return (usuariosDeAdministracion() ?? new List<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(ConDominio)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<int> AvisarAdministracion(NotificacionPushDTO notificacion)
        {
            if (notificacion == null)
            {
                return 0;
            }
            List<string> usuarios = Destinatarios();
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            return usuarios.Count;
        }

        public async Task AvisarA(string usuario, NotificacionPushDTO notificacion)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio) || notificacion == null)
            {
                return;
            }
            await notificaciones.GuardarEnBuzonDeUsuario(ConDominio(limpio), Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            await notificaciones.GuardarEnBuzonDeUsuario(limpio.Substring(limpio.LastIndexOf('\\') + 1), Constantes.Aplicaciones.ARIADNA, notificacion)
                .ConfigureAwait(false);
        }

        private static string ConDominio(string usuario) => usuario.Contains("\\") ? usuario : DOMINIO + usuario;
    }
}
