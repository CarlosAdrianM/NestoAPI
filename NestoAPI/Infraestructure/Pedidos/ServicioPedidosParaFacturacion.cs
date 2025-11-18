using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pedidos
{
    /// <summary>
    /// Implementación del servicio para obtener pedidos listos para facturar.
    ///
    /// REFACTORIZACIÓN: Usa TipoRutaFactory para obtener dinámicamente las rutas
    /// manejadas por cada tipo, eliminando constantes hardcodeadas.
    ///
    /// NUEVA VERSIÓN: Recibe string tipoRutaId en lugar de enum, permitiendo extensibilidad.
    /// </summary>
    public class ServicioPedidosParaFacturacion : IServicioPedidosParaFacturacion
    {
        private readonly NVEntities db;

        public ServicioPedidosParaFacturacion(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Obtiene los pedidos listos para facturar según el tipo de ruta y fecha de entrega.
        /// </summary>
        /// <param name="tipoRutaId">Id del tipo de ruta (ej: "PROPIA", "AGENCIA")</param>
        /// <param name="fechaEntregaDesde">Fecha desde la cual considerar pedidos</param>
        /// <returns>Lista de pedidos que cumplen los criterios</returns>
        public async Task<List<CabPedidoVta>> ObtenerPedidosParaFacturar(
            string tipoRutaId,
            DateTime fechaEntregaDesde)
        {
            // Validar que tipoRutaId no sea null o vacío
            if (string.IsNullOrWhiteSpace(tipoRutaId))
                throw new ArgumentException("El tipo de ruta no puede ser null o vacío", nameof(tipoRutaId));

            // Obtener el tipo de ruta desde el factory
            var tipoRuta = TipoRutaFactory.ObtenerPorId(tipoRutaId);
            if (tipoRuta == null)
                throw new ArgumentException($"Tipo de ruta '{tipoRutaId}' no encontrado en el factory", nameof(tipoRutaId));

            // Obtener las rutas contenidas en este tipo
            List<string> rutasABuscar = tipoRuta.RutasContenidas.ToList();

            // Query con todos los filtros
            // NOTA: No se filtra por VtoBueno aquí, se valida en el procesamiento
            // IMPORTANTE: Incluimos líneas EN_CURSO (sin albarán) y ALBARAN (con albarán pero sin factura)
            // Esto permite re-facturar pedidos NRM que ya tienen albarán pero necesitan factura
            var pedidos = await db.CabPedidoVtas
                .Include(p => p.LinPedidoVtas)
                .Include(p => p.Cliente)
                .Where(p => rutasABuscar.Contains(p.Ruta))
                .Where(p => p.LinPedidoVtas.Any(l =>
                    (l.Estado == Constantes.EstadosLineaVenta.EN_CURSO ||
                     l.Estado == Constantes.EstadosLineaVenta.ALBARAN) &&
                    l.Picking != null &&
                    l.Picking > 0 &&
                    l.Fecha_Entrega <= fechaEntregaDesde))
                .OrderBy(p => p.Fecha)
                .ThenBy(p => p.Número)
                .ToListAsync();

            return pedidos;
        }
    }
}
