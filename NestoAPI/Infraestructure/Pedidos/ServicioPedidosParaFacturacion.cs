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
    /// Implementación del servicio para obtener pedidos listos para facturar
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
            // Determinar las rutas según el tipo
            List<string> rutasABuscar = ObtenerRutasSegunTipo(tipoRuta);

            // Query con todos los filtros
            var pedidos = await db.CabPedidoVtas
                .Include(p => p.LinPedidoVtas)
                .Include(p => p.Cliente)
                .Where(p => rutasABuscar.Contains(p.Ruta))
                .Where(p => p.LinPedidoVtas.Any(l =>
                    l.Estado == Constantes.EstadosLineaVenta.EN_CURSO &&
                    l.Picking != null &&
                    l.Picking != 0))
                .Where(p => p.Fecha >= fechaEntregaDesde)
                .Where(p => p.VistoBueno == true)
                .OrderBy(p => p.Fecha)
                .ThenBy(p => p.Número)
                .ToListAsync();

            return pedidos;
        }

        /// <summary>
        /// Devuelve la lista de códigos de ruta según el tipo de facturación
        /// </summary>
        private List<string> ObtenerRutasSegunTipo(TipoRutaFacturacion tipoRuta)
        {
            switch (tipoRuta)
            {
                case TipoRutaFacturacion.RutaPropia:
                    return new List<string>
                    {
                        Constantes.Pedidos.RUTA_PROPIA_16,
                        Constantes.Pedidos.RUTA_PROPIA_AT
                    };

                case TipoRutaFacturacion.RutasAgencias:
                    return new List<string>
                    {
                        Constantes.Pedidos.RUTA_AGENCIA_FW,
                        Constantes.Pedidos.RUTA_AGENCIA_00
                    };

                default:
                    throw new ArgumentException($"Tipo de ruta no válido: {tipoRuta}", nameof(tipoRuta));
            }
        }
    }
}
