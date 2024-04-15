using NestoAPI.Models.PedidosCompra;
using NestoAPI.Models;
using System.Threading.Tasks;
using System;

namespace NestoAPI.Infraestructure.PedidosCompra
{
    internal interface IPedidosCompraService
    {
        Task<int> CrearAlbaran(int pedidoId, NVEntities db, string usuario = null);
        Task<CrearFacturaCmpResponse> CrearAlbaranYFactura(int pedidoId, DateTime fecha, NVEntities db, string usuario = null);
        Task<CrearFacturaCmpResponse> CrearFactura(int pedidoId, DateTime fecha, NVEntities db, string usuario = null);
        Task<int> CrearPagoFactura(CrearFacturaCmpRequest request, CrearFacturaCmpResponse respuesta, NVEntities db);
        Task<CabPedidoCmp> CrearPedido(PedidoCompraDTO pedido, NVEntities db);        
    }
}
