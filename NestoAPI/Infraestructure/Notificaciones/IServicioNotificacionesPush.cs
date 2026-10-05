using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Notificaciones
{
    public interface IServicioNotificacionesPush
    {
        Task<DispositivoNotificacion> RegistrarDispositivo(RegistrarDispositivoDTO registro, string usuario);
        Task<bool> DesregistrarDispositivo(string token);

        /// <summary>
        /// NestoAPI#575: quita a UN usuario del token (un mozo que se borra de una PDA de Ariadna); los demás que
        /// comparten ese aparato siguen recibiendo.
        /// </summary>
        Task<bool> DesregistrarDispositivoDeUsuario(string token, string usuario);
        Task<List<DispositivoNotificacion>> ObtenerDispositivosUsuario(string usuario, string aplicacion);
        Task<List<DispositivoNotificacion>> ObtenerDispositivosVendedor(string empresa, string vendedor, string aplicacion);
        Task<List<DispositivoNotificacion>> ObtenerDispositivosCliente(string empresa, string cliente, string aplicacion);
        Task<int> EnviarAUsuario(string usuario, string aplicacion, NotificacionPushDTO notificacion);
        Task<int> EnviarAVendedor(string empresa, string vendedor, NotificacionPushDTO notificacion);
        Task<int> EnviarACliente(string empresa, string cliente, NotificacionPushDTO notificacion);
        Task<int> EnviarATodosDeAplicacion(string aplicacion, NotificacionPushDTO notificacion);
        /// <summary>NestoAPI#579: los usuarios (sin repetir) con algún dispositivo activo de esa aplicación.</summary>
        Task<List<string>> UsuariosConDispositivoActivo(string aplicacion);

        /// <summary>
        /// Nesto#477: deja la notificación en el buzón de un usuario SIN mandar push (Nesto de
        /// escritorio no tiene dispositivos registrados; la ve en la campana).
        /// NestoAPI#575: en Ariadna, además, manda una push de datos a las PDA donde ha entrado ese mozo.
        /// </summary>
        Task GuardarEnBuzonDeUsuario(string usuario, string aplicacion, NotificacionPushDTO notificacion);

        // Buzón persistente (#387): la push se pierde si el usuario descarta la notificación del
        // sistema, así que además se guarda para que la app pueda volver a verla.
        Task<List<NotificacionBuzonDTO>> ObtenerBuzon(string usuario, string aplicacion, bool soloNoLeidas, int pagina, int tamanoPagina);
        Task<int> ContarNoLeidas(string usuario, string aplicacion);
        Task<bool> MarcarLeida(int id, string usuario);
        Task<int> MarcarTodasLeidas(string usuario, string aplicacion);
        Task<bool> EliminarDelBuzon(int id, string usuario);
    }
}
