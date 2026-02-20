using NestoAPI.Models.Pagos;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos
{
    public interface IServicioPagos
    {
        Task<RespuestaIniciarPago> IniciarPago(SolicitudPagoTPV solicitud, string usuario);
        Task<bool> ProcesarNotificacion(NotificacionRedsys notificacion);
        Task<PagoTPVDTO> ConsultarPago(int idPago);
        Task<PagoTPVDTO> ConsultarAuditoria(string numeroOrden);
    }
}
