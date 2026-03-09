using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure
{
    public interface IServicioPrecios
    {
        Producto BuscarProducto(string producto);
        List<OfertaPermitida> BuscarOfertasPermitidas(string producto);
        List<DescuentosProducto> BuscarDescuentosPermitidos(string numeroProducto, string numeroCliente, string contactoCliente);
        List<OfertaCombinada> BuscarOfertasCombinadas(string numeroProducto);
        decimal CalcularImporteGrupo(PedidoVentaDTO pedido, string grupo, string subGrupo);
        List<LineaPedidoVentaDTO> FiltrarLineas(PedidoVentaDTO pedido, string filtroProducto, string familia);
        List<RegaloImportePedido> BuscarRegaloPorImportePedido(string numeroProducto);

        /// <summary>
        /// Obtiene los Ganavisiones activos para un producto (puntos de bonificación).
        /// Issue #94: Sistema Ganavisiones
        /// </summary>
        /// <param name="numeroProducto">ID del producto</param>
        /// <returns>Número de Ganavisiones del producto, o null si no tiene configurado</returns>
        int? BuscarGanavisionesProducto(string numeroProducto);

        /// <summary>
        /// Obtiene el stock disponible total (todas las sedes) de un producto.
        /// Issue #117: Validar stock de Ganavisiones al crear pedido
        /// Disponible = Stock - PendienteEntregar + PendienteRecibir + PendienteReposicion
        /// </summary>
        int BuscarStockDisponibleTotal(string numeroProducto);
    }
}