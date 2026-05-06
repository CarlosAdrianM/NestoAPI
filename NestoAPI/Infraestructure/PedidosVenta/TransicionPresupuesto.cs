using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#193: decide qué transición de estado solicita un PUT de pedido respecto al
    /// presupuesto, comparando lo que hay en BD con lo que envía el cliente. Antes la lógica
    /// dependía únicamente de la flag <see cref="PedidoVentaDTO.EsPresupuesto"/>, lo que hacía
    /// que "pasar a presupuesto" se confundiera con "aceptar presupuesto" y las líneas
    /// terminaran en EN_CURSO en vez de PRESUPUESTO.
    /// </summary>
    public static class TransicionPresupuesto
    {
        public class Decision
        {
            public bool EsAceptarPresupuesto { get; set; }
            public bool EsPasarAPresupuesto { get; set; }

            /// <summary>Conjunto de Nº_Orden de líneas que deben pasar a estado PRESUPUESTO.</summary>
            public ISet<int> IdsParaPresupuesto { get; } = new HashSet<int>();
        }

        /// <summary>
        /// Decide si el PUT corresponde a "aceptar presupuesto", "pasar a presupuesto" o ninguno.
        /// Las dos transiciones son mutuamente excluyentes.
        /// </summary>
        public static Decision Decidir(
            IEnumerable<LinPedidoVta> lineasBD,
            PedidoVentaDTO dto)
        {
            var decision = new Decision();
            if (lineasBD == null || dto == null || dto.Lineas == null)
            {
                return decision;
            }

            var lineasBDList = lineasBD.ToList();

            // Aceptar presupuesto: todas las líneas activas en BD están en PRESUPUESTO y el
            // DTO indica que ya no es presupuesto (EsPresupuesto=false). Mantenemos la
            // semántica anterior para no romper el flujo "Aceptar presupuesto" existente.
            bool todasBDEnPresupuesto = lineasBDList.Any()
                && lineasBDList
                    .Where(EsLineaActiva)
                    .All(l => l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO);

            if (todasBDEnPresupuesto && !dto.EsPresupuesto)
            {
                decision.EsAceptarPresupuesto = true;
                return decision;
            }

            // Pasar a presupuesto: hay líneas en BD en PENDIENTE/EN_CURSO sin picking y
            // el DTO las marca todas con estado PRESUPUESTO. Solo se mueven las líneas
            // sin picking; las que tengan picking, albarán o factura conservan su estado.
            var idsElegibles = lineasBDList
                .Where(l => (l.Estado == Constantes.EstadosLineaVenta.PENDIENTE
                          || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO)
                         && (l.Picking ?? 0) == 0)
                .Select(l => l.Nº_Orden)
                .ToHashSet();

            if (idsElegibles.Count == 0)
            {
                return decision;
            }

            // El DTO debe pedir explícitamente PRESUPUESTO para todas las elegibles.
            // Si el cliente no marcó alguna como PRESUPUESTO, no se interpreta como
            // "pasar a presupuesto" (probablemente sea otro tipo de modificación).
            bool todasElegiblesEnPresupuestoEnDTO = dto.Lineas
                .Where(l => idsElegibles.Contains(l.id))
                .All(l => l.estado == Constantes.EstadosLineaVenta.PRESUPUESTO);

            if (!todasElegiblesEnPresupuestoEnDTO)
            {
                return decision;
            }

            decision.EsPasarAPresupuesto = true;
            foreach (int id in idsElegibles)
            {
                decision.IdsParaPresupuesto.Add(id);
            }
            return decision;
        }

        private static bool EsLineaActiva(LinPedidoVta l)
        {
            return l.Estado == Constantes.EstadosLineaVenta.PENDIENTE
                || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO
                || l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO;
        }
    }
}
