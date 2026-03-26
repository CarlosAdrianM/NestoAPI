using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Notificaciones
{
    public interface IServicioNotificacionesPush
    {
        Task<DispositivoNotificacion> RegistrarDispositivo(RegistrarDispositivoDTO registro, string usuario);
        Task<bool> DesregistrarDispositivo(string token);
        Task<List<DispositivoNotificacion>> ObtenerDispositivosUsuario(string usuario, string aplicacion);
        Task<List<DispositivoNotificacion>> ObtenerDispositivosVendedor(string empresa, string vendedor, string aplicacion);
        Task<List<DispositivoNotificacion>> ObtenerDispositivosCliente(string empresa, string cliente, string aplicacion);
        Task<int> EnviarAUsuario(string usuario, string aplicacion, NotificacionPushDTO notificacion);
        Task<int> EnviarAVendedor(string empresa, string vendedor, NotificacionPushDTO notificacion);
        Task<int> EnviarACliente(string empresa, string cliente, NotificacionPushDTO notificacion);
        Task<int> EnviarATodosDeAplicacion(string aplicacion, NotificacionPushDTO notificacion);
    }
}
