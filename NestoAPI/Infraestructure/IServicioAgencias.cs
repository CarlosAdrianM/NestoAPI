using NestoAPI.Models;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IServicioAgencias
    {
        string LeerCodigoPostal(PedidoVentaDTO pedido);
        Task<RespuestaAgencia> LeerDireccionPedidoGoogleMaps(PedidoVentaDTO pedido);
        Task<RespuestaAgencia> LeerDireccionGoogleMaps(string direccion, string codigoPostal);
        DateTime HoraActual();
    }
}
