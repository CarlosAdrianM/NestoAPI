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
            // OJO: hay que exigir que EXISTA alguna línea activa. Si no hay ninguna (p.ej. un
            // pedido todo en albarán), el .All() sobre la lista vacía daría true por verdad
            // vacua y se interpretaría como "aceptar presupuesto" sin haber ningún presupuesto,
            // disparando la validación del pedido al solo cambiar la cabecera (pedido 918386).
            var lineasActivasBD = lineasBDList.Where(EsLineaActiva).ToList();
            bool todasBDEnPresupuesto = lineasActivasBD.Any()
                && lineasActivasBD.All(l => l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO);

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

        /// <summary>
        /// NestoAPI#503: aplica el «pasar a presupuesto» sobre las líneas de BD que la decisión
        /// marcó como elegibles, vengan o no en el DTO. Antes el PUT recorría las líneas del DTO y
        /// una elegible que el cliente no mandaba se quedaba en -1 mientras las demás pasaban a -3.
        /// Devuelve cuántas líneas ha cambiado. Es idempotente.
        /// </summary>
        public static int AplicarPasoAPresupuesto(IEnumerable<LinPedidoVta> lineasBD, Decision decision)
        {
            if (lineasBD == null || decision == null || !decision.EsPasarAPresupuesto)
            {
                return 0;
            }
            int cambiadas = 0;
            foreach (LinPedidoVta linea in lineasBD)
            {
                if (decision.IdsParaPresupuesto.Contains(linea.Nº_Orden)
                    && linea.Estado != Constantes.EstadosLineaVenta.PRESUPUESTO)
                {
                    linea.Estado = Constantes.EstadosLineaVenta.PRESUPUESTO;
                    cambiadas++;
                }
            }
            return cambiadas;
        }

        /// <summary>
        /// NestoAPI#503: ¿el pedido, tal y como está en BD, es un presupuesto vivo? Es decir, tiene
        /// líneas editables (activas y sin picking) y TODAS están en PRESUPUESTO. Calcularlo ANTES
        /// de que el PUT toque estados.
        /// </summary>
        public static bool EsPresupuestoVivo(IEnumerable<LinPedidoVta> lineasBD)
        {
            var editables = (lineasBD ?? Enumerable.Empty<LinPedidoVta>()).Where(EsLineaEditable).ToList();
            return editables.Any() && editables.All(l => l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO);
        }

        /// <summary>
        /// NestoAPI#503: estado con el que debe nacer una línea NUEVA en el PUT, para que no pueda
        /// mezclarse presupuesto con pedido:
        /// <list type="bullet">
        /// <item>Pasando a presupuesto: PRESUPUESTO.</item>
        /// <item>Aceptando el presupuesto: lo que pida el DTO, salvo que pida PRESUPUESTO (entonces EN_CURSO,
        /// como las demás).</item>
        /// <item>Pedido que en BD es un presupuesto vivo y no se está aceptando (se está AMPLIANDO el
        /// presupuesto): PRESUPUESTO aunque el DTO mande otra cosa. Este era el hueco de la plantilla.</item>
        /// <item>Resto: lo que pida el DTO.</item>
        /// </list>
        /// </summary>
        public static short EstadoLineaNueva(Decision decision, bool pedidoEsPresupuestoVivoEnBD, short estadoDto)
        {
            if (decision != null && decision.EsPasarAPresupuesto)
            {
                return Constantes.EstadosLineaVenta.PRESUPUESTO;
            }
            if (decision != null && decision.EsAceptarPresupuesto)
            {
                return estadoDto == Constantes.EstadosLineaVenta.PRESUPUESTO
                    ? (short)Constantes.EstadosLineaVenta.EN_CURSO
                    : estadoDto;
            }
            if (pedidoEsPresupuestoVivoEnBD)
            {
                return Constantes.EstadosLineaVenta.PRESUPUESTO;
            }
            return estadoDto;
        }

        /// <summary>
        /// NestoAPI#503: guardia final del PUT. Entre las líneas editables (activas y sin picking) no
        /// puede haber a la vez PRESUPUESTO y PENDIENTE/EN_CURSO. Las líneas con picking, albarán o
        /// factura no cuentan: por diseño (#193) se quedan en su estado cuando el resto pasa a presupuesto.
        /// </summary>
        public static bool HayMezclaPresupuesto(IEnumerable<LinPedidoVta> lineasBD)
        {
            var editables = (lineasBD ?? Enumerable.Empty<LinPedidoVta>()).Where(EsLineaEditable).ToList();
            return editables.Any(l => l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO)
                && editables.Any(l => l.Estado != Constantes.EstadosLineaVenta.PRESUPUESTO);
        }

        private static bool EsLineaEditable(LinPedidoVta l)
        {
            return EsLineaActiva(l) && (l.Picking ?? 0) == 0;
        }

        private static bool EsLineaActiva(LinPedidoVta l)
        {
            return l.Estado == Constantes.EstadosLineaVenta.PENDIENTE
                || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO
                || l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO;
        }
    }
}
