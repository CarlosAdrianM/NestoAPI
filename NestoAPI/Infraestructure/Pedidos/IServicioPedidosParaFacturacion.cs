using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pedidos
{
    /// <summary>
    /// Servicio para obtener pedidos listos para facturar según tipo de ruta
    /// </summary>
    public interface IServicioPedidosParaFacturacion
    {
        /// <summary>
        /// Obtiene los pedidos listos para facturar según el tipo de ruta y fecha de entrega.
        /// Filtra pedidos que cumplan:
        /// - Ruta según TipoRuta (16, AT para Propia; FW, 00 para Agencias)
        /// - Tienen líneas en estado EN_CURSO (1) con picking != 0 y != null
        /// - Fecha de entrega >= fechaEntregaDesde
        /// - VistoBueno = true
        /// </summary>
        /// <param name="tipoRuta">Tipo de ruta a procesar</param>
        /// <param name="fechaEntregaDesde">Fecha desde la cual considerar pedidos</param>
        /// <returns>Lista de pedidos que cumplen los criterios</returns>
        Task<List<CabPedidoVta>> ObtenerPedidosParaFacturar(
            TipoRutaFacturacion tipoRuta,
            DateTime fechaEntregaDesde);
    }
}
