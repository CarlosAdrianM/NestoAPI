using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>
    /// Servicio para procesar notas de entrega.
    /// Las notas de entrega documentan entregas de productos que pueden estar ya facturados o pendientes de facturación.
    /// </summary>
    public interface IServicioNotasEntrega
    {
        /// <summary>
        /// Procesa un pedido como nota de entrega.
        ///
        /// Lógica:
        /// - Si líneas NO facturadas (YaFacturado=false): Solo cambia estado a NOTA_ENTREGA (-2), NO toca stock
        /// - Si líneas YA facturadas (YaFacturado=true): Cambia estado a NOTA_ENTREGA (-2) Y da de baja stock
        /// </summary>
        /// <param name="pedido">Pedido a procesar como nota de entrega</param>
        /// <param name="usuario">Usuario que realiza la operación</param>
        /// <returns>DTO con los datos de la nota de entrega creada</returns>
        Task<NotaEntregaCreadaDTO> ProcesarNotaEntrega(CabPedidoVta pedido, string usuario);
    }
}
