using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    public interface IServicioRecomendacionesPostCompra
    {
        /// <summary>
        /// Obtiene las recomendaciones de videos y productos para un pedido.
        /// Incluye videos donde aparecen los productos comprados, y para cada video
        /// lista todos sus productos indicando cuáles tiene el cliente y cuáles no.
        /// </summary>
        /// <param name="empresa">Código de empresa</param>
        /// <param name="pedidoNumero">Número del pedido</param>
        /// <returns>Datos completos para generar el correo post-compra</returns>
        Task<RecomendacionPostCompraDTO> ObtenerRecomendaciones(string empresa, int pedidoNumero);
    }
}
