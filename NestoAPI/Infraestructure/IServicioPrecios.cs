using NestoAPI.Models;
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
    }
}