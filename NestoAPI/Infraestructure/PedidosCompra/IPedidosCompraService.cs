using NestoAPI.Models.PedidosCompra;
using NestoAPI.Models;
using System.Threading.Tasks;
using System;

namespace NestoAPI.Infraestructure.PedidosCompra
{
    internal interface IPedidosCompraService
    {
        Task<int> CrearAlbaran(int pedidoId, NVEntities db);
        Task<CrearFacturaCmpResponse> CrearAlbaranYFactura(int pedidoId, DateTime fecha, NVEntities db);
        Task<CrearFacturaCmpResponse> CrearFactura(int pedidoId, DateTime fecha, NVEntities db);
        Task<int> CrearPagoFactura(CrearFacturaCmpRequest request, CrearFacturaCmpResponse respuesta, NVEntities db);
        Task<CabPedidoCmp> CrearPedido(PedidoCompraDTO pedido, NVEntities db);        
    }
}
