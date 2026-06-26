using NestoAPI.Models.PedidosVenta.ServirJunto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ValidadoresServirJunto
{
    /// <summary>
    /// Interfaz para validadores que determinan si se puede desmarcar ServirJunto.
    /// Issue #141: Patrón extensible para añadir nuevas validaciones.
    /// </summary>
    public interface IValidadorServirJunto
    {
        /// <summary>
        /// Valida si se puede desmarcar "Servir junto".
        /// </summary>
        /// <param name="almacen">Almacén del pedido.</param>
        /// <param name="productos">Productos bonificados (regalos) seleccionados.</param>
        /// <param name="lineasPedido">
        /// Líneas del pedido (tipoLinea=1). Issue #161: opcional — sólo lo usa
        /// ValidadorMaterialPromocional. Clientes que no lo envíen mantienen el
        /// comportamiento anterior.
        /// </param>
        /// <param name="pedido">
        /// NestoAPI#262: número del pedido que se valida. Se excluye del cálculo de stock para que las
        /// líneas del propio pedido no cuenten su reserva contra sí mismas. null = no excluye (retrocompat).
        /// </param>
        Task<ValidarServirJuntoResponse> Validar(
            string almacen,
            List<ProductoBonificadoConCantidadRequest> productos,
            List<ProductoBonificadoConCantidadRequest> lineasPedido,
            int? pedido = null);
    }
}
