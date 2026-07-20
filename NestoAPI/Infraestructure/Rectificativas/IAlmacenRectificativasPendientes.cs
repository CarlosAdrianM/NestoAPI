using NestoAPI.Models.Rectificativas;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rectificativas
{
    /// <summary>
    /// Issue #87: persistencia de la metadata de rectificación entre la copia del pedido y su
    /// facturación manual (tabla RectificativaPendiente). Interfaz para poder testear la
    /// orquestación sin BD (la tabla no está en el EDMX: la implementación usa SQL crudo).
    /// </summary>
    public interface IAlmacenRectificativasPendientes
    {
        Task GuardarPendientes(string empresa, int numeroPedido, List<RectificativaPendienteDTO> pendientes);
        Task<List<RectificativaPendienteDTO>> LeerPendientes(string empresa, int numeroPedido);
        Task BorrarPendientes(string empresa, int numeroPedido, List<int> numerosLinea);
    }
}
