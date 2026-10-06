using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Nesto#510 (sugerencia 508, pedido 927808): el almacén de un pedido entero se puede cambiar desde Nesto en cualquier
    /// serie cuando no hay nada que lo impida. El PUT lo revalida aquí por si algo cambió entre que se abrió el pedido y se
    /// guardó. Se exige, en TODAS las líneas de producto del pedido:
    /// <list type="bullet">
    /// <item>estado presupuesto, pendiente o en curso (ni nota de entrega, ni albarán, ni factura);</item>
    /// <item>sin picking;</item>
    /// </list>
    /// y en el pedido: que no sea nota de entrega, que no esté en un picking en curso (PickingEnCursoDelPedido) y que no
    /// tenga una etiqueta de agencia viva (envío con código de barras en curso, tramitado o incidentado).
    /// </summary>
    public static class CambioAlmacenPedido
    {
        /// <summary>¿Alguna línea ya guardada viene con otro almacén?</summary>
        internal static bool CambiaAlmacen(IEnumerable<LinPedidoVta> lineasGuardadas, IEnumerable<LineaPedidoVentaDTO> lineasPedido)
        {
            if (lineasGuardadas == null || lineasPedido == null)
            {
                return false;
            }
            Dictionary<int, LinPedidoVta> porOrden = lineasGuardadas.GroupBy(l => l.Nº_Orden).ToDictionary(g => g.Key, g => g.First());
            return lineasPedido.Any(l => l.id != 0
                && porOrden.TryGetValue(l.id, out LinPedidoVta guardada)
                && !string.IsNullOrWhiteSpace(l.almacen)
                && !string.Equals(guardada.Almacén?.Trim(), l.almacen.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Envío con etiqueta viva: tiene código de barras y no está entregado, devuelto ni pendiente de imprimir.</summary>
        internal static bool EsEtiquetaViva(EnviosAgencia envio)
        {
            return envio != null && !string.IsNullOrWhiteSpace(envio.CodigoBarras)
                && (envio.Estado == Constantes.Agencias.ESTADO_EN_CURSO
                    || envio.Estado == Constantes.Agencias.ESTADO_TRAMITADO
                    || envio.Estado == Constantes.Agencias.ESTADO_INCIDENTADO);
        }

        /// <summary>Por qué no se puede cambiar el almacén, o null si se puede. Pura para testear sin BD.</summary>
        internal static string Motivo(IEnumerable<LinPedidoVta> lineas, bool notaEntrega, IEnumerable<EnviosAgencia> envios, int? pickingEnCurso)
        {
            const string NO_SE_PUEDE = "No se puede cambiar el almacén del pedido: ";
            List<LinPedidoVta> producto = (lineas ?? Enumerable.Empty<LinPedidoVta>())
                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO).ToList();

            if (notaEntrega || producto.Any(l => l.Estado == Constantes.EstadosLineaVenta.NOTA_ENTREGA))
            {
                return NO_SE_PUEDE + "es una nota de entrega.";
            }
            LinPedidoVta servida = producto.FirstOrDefault(l => l.Estado > Constantes.EstadosLineaVenta.EN_CURSO
                || l.Nº_Albarán.HasValue || !string.IsNullOrWhiteSpace(l.Nº_Factura));
            if (servida != null)
            {
                string que = servida.Estado >= Constantes.EstadosLineaVenta.FACTURA || !string.IsNullOrWhiteSpace(servida.Nº_Factura)
                    ? "factura" : "albarán";
                return NO_SE_PUEDE + $"la línea {servida.Nº_Orden} ({servida.Producto?.Trim()}) ya tiene {que}. " +
                    "Solo se cambia cuando todas las líneas están pendientes o en presupuesto.";
            }
            LinPedidoVta conPicking = producto.FirstOrDefault(l => (l.Picking ?? 0) != 0);
            if (conPicking != null)
            {
                return NO_SE_PUEDE + $"la línea {conPicking.Nº_Orden} ({conPicking.Producto?.Trim()}) ya tiene picking ({conPicking.Picking}).";
            }
            if (pickingEnCurso.HasValue && pickingEnCurso.Value > 0)
            {
                return NO_SE_PUEDE + $"está en el picking {pickingEnCurso.Value}, que aún no ha salido.";
            }
            EnviosAgencia viva = (envios ?? Enumerable.Empty<EnviosAgencia>()).FirstOrDefault(EsEtiquetaViva);
            if (viva != null)
            {
                return NO_SE_PUEDE + $"ya tiene una etiqueta de agencia ({viva.CodigoBarras.Trim()}) sin entregar.";
            }
            return null;
        }
    }
}
