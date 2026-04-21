using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta.ServirJunto
{
    /// <summary>
    /// Request para validar si se puede desmarcar "Servir junto" en un pedido.
    /// Retrocompatible: NestoApp envía ProductosBonificados (string[]); Nesto envía
    /// ProductosBonificadosConCantidad con cantidades reales y LineasPedido.
    /// </summary>
    public class ValidarServirJuntoRequest
    {
        public string Almacen { get; set; }

        /// <summary>
        /// Formato antiguo (NestoApp): solo IDs de productos, asume cantidad=1.
        /// </summary>
        public List<string> ProductosBonificados { get; set; }

        /// <summary>
        /// Formato nuevo (Nesto): IDs con cantidades reales.
        /// </summary>
        public List<ProductoBonificadoConCantidadRequest> ProductosBonificadosConCantidad { get; set; }

        /// <summary>
        /// Líneas de producto del pedido (tipoLinea=1) con sus cantidades.
        /// Issue NestoAPI#161: necesarias para que ValidadorMaterialPromocional pueda
        /// detectar si alguna muestra (subgrupo MMP) del pedido se quedaría pendiente al
        /// desmarcar "Servir junto". Opcional — si viene null/vacío, la validación de
        /// MMP se salta (retrocompatible con NestoApp y otros clientes no actualizados).
        /// </summary>
        public List<ProductoBonificadoConCantidadRequest> LineasPedido { get; set; }
    }
}
