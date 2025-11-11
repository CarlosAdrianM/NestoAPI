using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// Interfaz para el gestor de facturación masiva de pedidos por rutas
    /// </summary>
    public interface IGestorFacturacionRutas
    {
        /// <summary>
        /// Determina si se debe imprimir un documento físicamente basándose en los comentarios del pedido
        /// </summary>
        bool DebeImprimirDocumento(string comentarios);

        /// <summary>
        /// Verifica si un pedido puede ser facturado (respetando la lógica de MantenerJunto)
        /// </summary>
        bool PuedeFacturarPedido(CabPedidoVta pedido);

        /// <summary>
        /// Factura una lista de pedidos de rutas, creando albaranes, facturas e imprimiendo documentos
        /// </summary>
        /// <param name="pedidos">Lista de pedidos a facturar</param>
        /// <param name="usuario">Usuario que realiza la facturación</param>
        Task<FacturarRutasResponseDTO> FacturarRutas(List<CabPedidoVta> pedidos, string usuario);

        /// <summary>
        /// Genera un preview (simulación) de facturación de rutas SIN crear nada en la BD.
        /// Calcula qué albaranes, facturas y notas de entrega se crearían.
        /// </summary>
        /// <param name="pedidos">Lista de pedidos a analizar</param>
        /// <param name="fechaEntregaDesde">Fecha límite de entrega para filtrar líneas</param>
        PreviewFacturacionRutasResponseDTO PreviewFacturarRutas(List<CabPedidoVta> pedidos, System.DateTime fechaEntregaDesde);
    }
}
