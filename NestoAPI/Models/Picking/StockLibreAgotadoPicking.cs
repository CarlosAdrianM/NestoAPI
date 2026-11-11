using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>Unidades de un producto reservadas (Ubicaciones Estado 3) para un pedido.</summary>
    public class ReservaUbicacionPicking
    {
        public int Pedido { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// 11/11/26 (producto 45313, pedido 929982): «La ubicación está descuadrada o el stock está en otra empresa» salía cuando en
    /// realidad lo ubicado estaba todo reservado (Estado 3) por el picking de OTROS pedidos: no quedaba stock libre. Antes de dar ese
    /// error, si hay reservas de otros pedidos, se da este otro, claro y de usted. Si no las hay, se queda el de siempre.
    /// </summary>
    public static class StockLibreAgotadoPicking
    {
        public static bool EsDescuadrada(Exception ex) =>
            ex != null && ex.Message != null && ex.Message.StartsWith(GestorUbicaciones.MENSAJE_DESCUADRADA, StringComparison.Ordinal);

        /// <summary>
        /// «No queda stock libre del producto 45313 en Algete: las 3 unidades están reservadas para los pedidos 929001, 929002 y
        /// 929003. Quite el picking de alguno o espere a que entre género.» Null si no hay reservas de otros pedidos.
        /// </summary>
        public static string Mensaje(string producto, string almacen, int pedidoActual, IEnumerable<ReservaUbicacionPicking> reservas)
        {
            List<ReservaUbicacionPicking> deOtros = (reservas ?? Enumerable.Empty<ReservaUbicacionPicking>())
                .Where(r => r != null && r.Pedido != pedidoActual && r.Cantidad > 0)
                .ToList();
            if (deOtros.Count == 0)
            {
                return null;
            }
            int unidades = deOtros.Sum(r => r.Cantidad);
            List<string> pedidos = deOtros.Select(r => r.Pedido).Distinct().OrderBy(p => p)
                .Select(p => p.ToString(CultureInfo.InvariantCulture)).ToList();
            string cuales = unidades == 1 ? "la unidad está reservada" : $"las {unidades} unidades están reservadas";
            string paraQuien = pedidos.Count == 1 ? $"para el pedido {pedidos[0]}" : $"para los pedidos {Lista(pedidos)}";
            string quitar = pedidos.Count == 1 ? "Quite el picking de ese pedido" : "Quite el picking de alguno";
            return $"No queda stock libre del producto {producto?.Trim()} en {NombreAlmacen(almacen)}: {cuales} {paraQuien}. " +
                $"{quitar} o espere a que entre género.";
        }

        internal static string NombreAlmacen(string almacen)
        {
            switch (almacen?.Trim().ToUpperInvariant())
            {
                case Constantes.Almacenes.ALGETE: return "Algete";
                case Constantes.Almacenes.REINA: return "Reina";
                case Constantes.Almacenes.ALCOBENDAS: return "Alcobendas";
                case null:
                case "": return "el almacén";
                default: return almacen.Trim();
            }
        }

        private static string Lista(List<string> elementos) =>
            elementos.Count == 1 ? elementos[0] : string.Join(", ", elementos.Take(elementos.Count - 1)) + " y " + elementos.Last();
    }
}
