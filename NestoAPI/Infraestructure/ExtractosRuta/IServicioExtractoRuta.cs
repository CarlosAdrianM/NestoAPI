using NestoAPI.Models;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ExtractosRuta
{
    /// <summary>
    /// Servicio para gestionar inserciones en la tabla ExtractoRuta
    /// </summary>
    public interface IServicioExtractoRuta
    {
        /// <summary>
        /// Inserta un registro en ExtractoRuta copiando datos desde ExtractoCliente (para facturas).
        /// Busca el registro de ExtractoCliente con TipoApunte = 1 (factura) y copia sus datos.
        /// </summary>
        /// <param name="pedido">Pedido que se ha facturado</param>
        /// <param name="numeroFactura">Número de la factura creada</param>
        /// <param name="usuario">Usuario que realiza la operación</param>
        /// <param name="autoSave">Si es true, guarda cambios automáticamente. Si es false, el llamador debe hacer SaveChangesAsync()</param>
        /// <returns>Task</returns>
        Task InsertarDesdeFactura(CabPedidoVta pedido, string numeroFactura, string usuario, bool autoSave = true);

        /// <summary>
        /// Inserta un registro en ExtractoRuta usando datos del pedido (para albaranes).
        /// No existe ExtractoCliente, por lo que usa directamente datos del pedido.
        /// </summary>
        /// <param name="pedido">Pedido del que se ha creado el albarán</param>
        /// <param name="numeroAlbaran">Número del albarán creado</param>
        /// <param name="usuario">Usuario que realiza la operación</param>
        /// <param name="autoSave">Si es true, guarda cambios automáticamente. Si es false, el llamador debe hacer SaveChangesAsync()</param>
        /// <returns>Task</returns>
        Task InsertarDesdeAlbaran(CabPedidoVta pedido, int numeroAlbaran, string usuario, bool autoSave = true);
    }
}
