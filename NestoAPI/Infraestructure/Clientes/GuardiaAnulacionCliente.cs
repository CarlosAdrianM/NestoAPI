using NestoAPI.Models;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#476: una comprobación que puede impedir anular un cliente (Estado negativo).
    /// Devuelve el motivo, redactado para el usuario, o null si por su parte se puede anular.
    /// Mismo patrón que los validadores del pedido (IValidadorDenegacion): añadir una regla es
    /// añadir una clase a la lista de <see cref="GuardiaAnulacionCliente"/>.
    /// </summary>
    public interface IComprobacionAnulacionCliente
    {
        Task<string> MotivoParaNoAnular(NVEntities db, string empresa, string cliente);
    }

    /// <summary>
    /// NestoAPI#476: el único punto por el que pasa una anulación de cliente. Nació del 40445
    /// (Zulay), anulado con productos pendientes de servir: nadie lo impedía, y Alfredo se lo
    /// encontró al ir a anular el pendiente. Se comprueban TODAS las reglas y se devuelven todos
    /// los motivos, como hace GestorPrecios con el pedido: si hay pendientes Y deuda, el usuario
    /// quiere saber las dos cosas de una vez.
    ///
    /// Hoy se aplica en el PUT de ClienteComercial (Nesto, ficha comercial) y en DejarDeVisitar
    /// cuando el resultado es dejar al cliente en NULO. Nesto aún escribe Estado por EF en pantallas
    /// sin migrar (Nesto#340): esas no pasan por aquí hasta que migren.
    /// </summary>
    public static class GuardiaAnulacionCliente
    {
        private static readonly IReadOnlyList<IComprobacionAnulacionCliente> comprobaciones = new List<IComprobacionAnulacionCliente>
        {
            new ComprobacionSinProductosPendientes(),
            new ComprobacionSinDeuda()
        };

        /// <summary>
        /// Anular es pasar de un estado vivo (>= 0) a uno negativo. Reactivar, cambiar entre vivos
        /// o dejar el mismo estado no lo es; un estado null en el DTO significa "no tocar".
        /// </summary>
        public static bool EsAnulacion(short? nuevoEstado, short? estadoActual)
        {
            return nuevoEstado.HasValue
                && nuevoEstado.Value < Constantes.Clientes.Estados.VISITA_PRESENCIAL
                && (estadoActual ?? Constantes.Clientes.Estados.VISITA_PRESENCIAL) >= Constantes.Clientes.Estados.VISITA_PRESENCIAL;
        }

        /// <summary>Todos los motivos por los que NO se puede anular; vacía si se puede.</summary>
        public static async Task<List<string>> MotivosParaNoAnular(NVEntities db, string empresa, string cliente)
        {
            List<string> motivos = new List<string>();
            foreach (IComprobacionAnulacionCliente comprobacion in comprobaciones)
            {
                string motivo = await comprobacion.MotivoParaNoAnular(db, empresa, cliente).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(motivo))
                {
                    motivos.Add(motivo);
                }
            }
            return motivos;
        }

        /// <summary>
        /// Lanza <see cref="ValidationException"/> (que los controllers ya convierten en 400) con
        /// todos los motivos si el cliente no se puede anular.
        /// </summary>
        public static async Task ExigirAnulable(NVEntities db, string empresa, string cliente)
        {
            List<string> motivos = await MotivosParaNoAnular(db, empresa, cliente).ConfigureAwait(false);
            if (motivos.Any())
            {
                throw new ValidationException(string.Join(" ", motivos));
            }
        }
    }

    /// <summary>Con líneas de venta pendientes de servir (entre PENDIENTE y EN_CURSO) no se anula.</summary>
    internal class ComprobacionSinProductosPendientes : IComprobacionAnulacionCliente
    {
        public async Task<string> MotivoParaNoAnular(NVEntities db, string empresa, string cliente)
        {
            // Todos los contactos del cliente: la cuenta es una, y lo pendiente se sirve igual.
            List<int> pedidos = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && l.Nº_Cliente == cliente
                    && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE
                    && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .Select(l => l.Número)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            if (!pedidos.Any())
            {
                return null;
            }
            return $"El cliente {cliente.Trim()} tiene productos pendientes de servir en {(pedidos.Count == 1 ? "el pedido" : "los pedidos")} " +
                $"{string.Join(", ", pedidos.OrderBy(p => p))}: hay que servirlos o anularlos antes de anular al cliente.";
        }
    }

    /// <summary>Con apuntes vivos en el extracto (importe pendiente distinto de cero) no se anula.</summary>
    internal class ComprobacionSinDeuda : IComprobacionAnulacionCliente
    {
        public async Task<string> MotivoParaNoAnular(NVEntities db, string empresa, string cliente)
        {
            List<decimal> pendientes = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa && e.Número == cliente && e.ImportePdte != 0)
                .Select(e => e.ImportePdte)
                .ToListAsync().ConfigureAwait(false);
            if (!pendientes.Any())
            {
                return null;
            }
            decimal deuda = pendientes.Sum();
            return $"El cliente {cliente.Trim()} tiene {deuda:N2} € pendientes en el extracto ({pendientes.Count} apunte{(pendientes.Count == 1 ? "" : "s")}): " +
                "hay que liquidarlos antes de anular al cliente.";
        }
    }
}
