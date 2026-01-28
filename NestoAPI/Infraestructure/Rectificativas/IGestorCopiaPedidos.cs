using NestoAPI.Models.Rectificativas;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rectificativas
{
    /// <summary>
    /// Interfaz para el gestor de copia de pedidos/facturas.
    /// Permite crear rectificativas y traspasos de facturas entre clientes.
    /// Issue #85
    /// </summary>
    public interface IGestorCopiaPedidos
    {
        /// <summary>
        /// Copia las líneas de una factura a un pedido nuevo o existente.
        /// </summary>
        /// <param name="request">Parámetros de la copia</param>
        /// <param name="usuario">Usuario que realiza la operación</param>
        /// <returns>Resultado de la operación con los números de documentos creados</returns>
        Task<CopiarFacturaResponse> CopiarFactura(CopiarFacturaRequest request, string usuario);
    }
}
