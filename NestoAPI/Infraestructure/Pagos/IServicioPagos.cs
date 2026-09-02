using NestoAPI.Models.Pagos;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos
{
    public interface IServicioPagos
    {
        Task<RespuestaIniciarPago> IniciarPago(SolicitudPagoTPV solicitud, string usuario);
        Task<bool> ProcesarNotificacion(NotificacionRedsys notificacion);
        Task<PagoTPVDTO> ConsultarPago(int idPago);
        Task<PagoTPVDTO> ConsultarAuditoria(string numeroOrden);
        Task<List<PagoTPVDTO>> ListarPorCliente(string empresa, string cliente, int limite = 20);

        // NestoAPI#178/#181: cobro directo con tarjeta guardada (token Redsys), síncrono
        Task<ResultadoCobroTarjetaGuardada> CobrarConTarjetaGuardada(SolicitudCobroTarjetaGuardada solicitud, string usuario);

        /// <summary>NestoAPI#178: la tarjeta guardada si es del cliente y usable; null si no.</summary>
        TarjetaCliente TarjetaGuardadaDe(string empresa, string cliente, int tarjetaId);
        Task AplicarCobroAlPedido(int idPago, int pedido);
        Task<bool> DevolverCobro(int idPago, string motivo);

        // NestoAPI#178: alta de tarjeta sin cobro (autorización 0 EUR con tokenización)
        Task<RespuestaIniciarPago> IniciarAltaTarjeta(SolicitudAltaTarjeta solicitud, string usuario);
    }
}
