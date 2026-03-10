using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    public interface IServicioRecomendacionesPostCompra
    {
        /// <summary>
        /// Obtiene los datos de correo post-compra para todos los clientes que tuvieron
        /// albaranes en el rango de fechas especificado y cumplen los filtros
        /// (tienen email, Estado distinto de 8, CP empieza por 28/45/19).
        /// Para cada cliente devuelve los top 3 productos por BaseImponible que tienen vídeo,
        /// y hasta 4 productos recomendados de esos vídeos que el cliente nunca ha comprado.
        /// </summary>
        Task<List<CorreoPostCompraClienteDTO>> ObtenerCorreosSemana(string empresa, DateTime fechaDesde, DateTime fechaHasta);
    }
}
