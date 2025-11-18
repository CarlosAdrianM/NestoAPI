using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
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
        /// <param name="fechaEntregaDesde">Fecha límite de entrega para filtrar líneas procesables</param>
        Task<FacturarRutasResponseDTO> FacturarRutas(List<CabPedidoVta> pedidos, string usuario, DateTime fechaEntregaDesde);

        /// <summary>
        /// Genera un preview (simulación) de facturación de rutas SIN crear nada en la BD.
        /// Calcula qué albaranes, facturas y notas de entrega se crearían.
        /// </summary>
        /// <param name="pedidos">Lista de pedidos a analizar</param>
        /// <param name="fechaEntregaDesde">Fecha límite de entrega para filtrar líneas</param>
        PreviewFacturacionRutasResponseDTO PreviewFacturarRutas(List<CabPedidoVta> pedidos, DateTime fechaEntregaDesde);

        /// <summary>
        /// Obtiene los documentos de impresión para un pedido ya facturado.
        /// Genera PDFs con las copias y bandeja apropiadas según el tipo de ruta.
        /// </summary>
        /// <param name="empresa">Empresa del pedido</param>
        /// <param name="numeroPedido">Número del pedido</param>
        /// <param name="numeroFactura">Número de factura si se generó (null o "FDM" si es fin de mes)</param>
        /// <param name="numeroAlbaran">Número de albarán si se generó</param>
        /// <returns>DTO con los documentos listos para imprimir</returns>
        Task<DocumentosImpresionPedidoDTO> ObtenerDocumentosImpresion(
            string empresa,
            int numeroPedido,
            string numeroFactura = null,
            int? numeroAlbaran = null);
    }
}
