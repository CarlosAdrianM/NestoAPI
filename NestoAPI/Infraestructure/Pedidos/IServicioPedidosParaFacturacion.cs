using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pedidos
{
    /// <summary>
    /// Servicio para obtener pedidos listos para facturar según tipo de ruta.
    /// REFACTORIZADO: Ahora usa string tipoRutaId en lugar de enum para permitir extensibilidad.
    /// </summary>
    public interface IServicioPedidosParaFacturacion
    {
        /// <summary>
        /// Obtiene los pedidos listos para facturar según el tipo de ruta y fecha de entrega.
        /// Filtra pedidos que cumplan:
        /// - Ruta según tipoRutaId (obtenido dinámicamente desde TipoRutaFactory)
        /// - Tienen líneas en estado EN_CURSO (1) con picking != 0 y != null
        /// - Fecha de entrega >= fechaEntregaDesde
        /// - VistoBueno = true
        /// </summary>
        /// <param name="tipoRutaId">Id del tipo de ruta a procesar (ej: "PROPIA", "AGENCIA")</param>
        /// <param name="fechaEntregaDesde">Fecha desde la cual considerar pedidos</param>
        /// <returns>Lista de pedidos que cumplen los criterios</returns>
        Task<List<CabPedidoVta>> ObtenerPedidosParaFacturar(
            string tipoRutaId,
            DateTime fechaEntregaDesde);

        /// <summary>
        /// El pedido indicado si tiene algo que facturar con EXACTAMENTE la misma condición que
        /// <see cref="ObtenerPedidosParaFacturar"/> (sin filtrar por ruta); null si no.
        /// Lo usa «Facturar al imprimir etiqueta» de Agencias, que tiene que seguir el mismo camino
        /// que la facturación de rutas.
        /// </summary>
        Task<CabPedidoVta> ObtenerPedidoParaFacturar(string empresa, int numero, DateTime fechaEntregaDesde);
    }
}
