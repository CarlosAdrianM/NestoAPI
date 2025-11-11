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
        public async Task<List<CabPedidoVta>> ObtenerPedidosParaFacturar(
            TipoRutaFacturacion tipoRuta,
            DateTime fechaEntregaDesde)
        {
            // Determinar las rutas según el tipo DINÁMICAMENTE desde el factory
            List<string> rutasABuscar = ObtenerRutasSegunTipo(tipoRuta);

            // Query con todos los filtros
            // NOTA: No se filtra por VtoBueno aquí, se valida en el procesamiento
            var pedidos = await db.CabPedidoVtas
                .Include(p => p.LinPedidoVtas)
                .Include(p => p.Cliente)
                .Where(p => rutasABuscar.Contains(p.Ruta))
                .Where(p => p.LinPedidoVtas.Any(l =>
                    l.Estado == Constantes.EstadosLineaVenta.EN_CURSO &&
                    l.Picking != null &&
                    l.Picking > 0 &&
                    l.Fecha_Entrega <= fechaEntregaDesde))
                .OrderBy(p => p.Fecha)
                .ThenBy(p => p.Número)
                .ToListAsync();

            return pedidos;
        }

        /// <summary>
        /// Devuelve la lista de códigos de ruta según el tipo de facturación.
        ///
        /// REFACTORIZACIÓN: Obtiene las rutas dinámicamente desde TipoRutaFactory
        /// en lugar de usar constantes hardcodeadas.
        ///
        /// Esto permite agregar nuevos tipos de ruta sin modificar este código.
        /// </summary>
        private List<string> ObtenerRutasSegunTipo(TipoRutaFacturacion tipoRuta)
        {
            switch (tipoRuta)
            {
                case TipoRutaFacturacion.RutaPropia:
                    // Obtener rutas de la implementación RutaPropia
                    var rutaPropia = TipoRutaFactory.ObtenerPorId("PROPIA");
                    return rutaPropia.RutasContenidas.ToList();

                case TipoRutaFacturacion.RutasAgencias:
                    // Obtener rutas de la implementación RutaAgencia
                    var rutaAgencia = TipoRutaFactory.ObtenerPorId("AGENCIA");
                    return rutaAgencia.RutasContenidas.ToList();

                default:
                    throw new ArgumentException($"Tipo de ruta no válido: {tipoRuta}", nameof(tipoRuta));
            }
        }
    }
}
