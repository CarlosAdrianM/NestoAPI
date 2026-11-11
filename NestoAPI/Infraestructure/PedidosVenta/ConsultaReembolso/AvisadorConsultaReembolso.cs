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

        /// <summary>
        /// El escalado de una consulta sin contestar: campana de Nesto al grupo Dirección, menos los que también son de Administración
        /// (ya lo tienen), como el escalado de las autorizaciones de reembolso (#775). Devuelve a cuántos ha avisado.
        /// </summary>
        Task<int> AvisarDireccion(NotificacionPushDTO notificacion);

        /// <summary>A quien preguntó: campana de Nesto (con dominio) y Avisos de Ariadna (sin dominio), como #698.</summary>
        Task AvisarA(string usuario, NotificacionPushDTO notificacion);

        /// <summary>
        /// NestoAPI#734: la consulta se ha contestado: «notificacionResuelta» a Administración y Dirección para que se cierre la ventana
        /// urgente en todos los Nesto. Nunca lanza.
        /// </summary>
        void Resuelta(NotificacionResueltaDTO resuelta);
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
        private readonly Func<List<string>> usuariosDeDireccion;
        private readonly IAvisosTiempoReal tiempoReal;

        public AvisadorConsultaReembolso() : this(null, null)
        {
        }

        internal AvisadorConsultaReembolso(IServicioNotificacionesPush notificaciones, Func<List<string>> usuariosDeAdministracion,
            Func<List<string>> usuariosDeDireccion = null, IAvisosTiempoReal tiempoReal = null)
        {
            this.tiempoReal = tiempoReal ?? new AvisosTiempoReal();
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.usuariosDeAdministracion = usuariosDeAdministracion ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ADMINISTRACION));
            this.usuariosDeDireccion = usuariosDeDireccion ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.DIRECCION));
        }

        /// <summary>El grupo Administración, limpio y sin repetidos (con dominio).</summary>
        internal List<string> Destinatarios() => Limpios(usuariosDeAdministracion());

        /// <summary>El grupo Dirección menos los que también son de Administración (con dominio, sin repetidos).</summary>
        internal List<string> DestinatariosDireccion()
        {
            var administracion = new HashSet<string>(Destinatarios(), StringComparer.OrdinalIgnoreCase);
            return Limpios(usuariosDeDireccion()).Where(u => !administracion.Contains(u)).ToList();
        }

        private static List<string> Limpios(List<string> usuarios)
        {
            return (usuarios ?? new List<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(ConDominio)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public Task<int> AvisarAdministracion(NotificacionPushDTO notificacion) => AvisarATodos(Destinatarios, notificacion);

        public Task<int> AvisarDireccion(NotificacionPushDTO notificacion) => AvisarATodos(DestinatariosDireccion, notificacion);

        public void Resuelta(NotificacionResueltaDTO resuelta)
        {
            try
            {
                tiempoReal.NotificacionResuelta(Destinatarios().Concat(DestinatariosDireccion()).ToList(), resuelta);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Consulta reembolso #734] No se ha podido avisar a los Nesto de que la consulta {resuelta?.Referencia} está contestada: {ex.Message}", ex),
                    "Sistema (consulta reembolso)");
            }
        }

        private async Task<int> AvisarATodos(Func<List<string>> destinatarios, NotificacionPushDTO notificacion)
        {
            if (notificacion == null)
            {
                return 0;
            }
            List<string> usuarios = destinatarios();
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
