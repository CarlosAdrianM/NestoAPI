using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#898 (Carlos, 26/11/26; caso 27593, factura NV2616046 con tres P.O.): LA REGLA de qué pedidos pueden ir juntos en
    /// una misma factura o en un mismo pedido. Dos pedidos solo se juntan si tienen la MISMA P.O. (<c>CabPedidoVta.SuPedido</c>),
    /// comparada sin los espacios de relleno de la BD ni mayúsculas; sin P.O. (NULL o en blanco) es un valor más: solo se junta con
    /// otros sin P.O. La usan las dos estrategias de agrupación (P.O. y fin de mes), el motor que mueve las líneas (última barrera)
    /// y «Unir pedidos». Pura: sin BD.
    /// </summary>
    public static class ReglaPOAgrupacion
    {
        /// <summary>Código de error de «Unir pedidos» con P.O. distintas.</summary>
        public const string CODIGO_PO_DISTINTAS = "PEDIDOS_PO_DISTINTAS";

        /// <summary>La clave de comparación: recortada y en mayúsculas; "" = sin P.O.</summary>
        public static string Clave(string suPedido)
        {
            return string.IsNullOrWhiteSpace(suPedido) ? string.Empty : suPedido.Trim().ToUpperInvariant();
        }

        /// <summary>¿Tiene P.O.?</summary>
        public static bool TienePO(string suPedido) => Clave(suPedido).Length > 0;

        /// <summary>¿Pueden ir juntos dos pedidos con estas P.O.?</summary>
        public static bool MismaPO(string suPedidoA, string suPedidoB)
        {
            return string.Equals(Clave(suPedidoA), Clave(suPedidoB), StringComparison.Ordinal);
        }

        /// <summary>
        /// Por qué no pueden ir juntos estos pedidos (número y P.O. de cada uno), o <c>null</c> si todos tienen la misma P.O.
        /// Se compara cada uno con el primero (de menor número) y se nombra el primer conflicto.
        /// </summary>
        public static string MotivoNoSePuedenJuntar(IEnumerable<KeyValuePair<int, string>> pedidos)
        {
            List<KeyValuePair<int, string>> lista = (pedidos ?? Enumerable.Empty<KeyValuePair<int, string>>())
                .OrderBy(p => p.Key)
                .ToList();
            if (lista.Count < 2)
            {
                return null;
            }
            KeyValuePair<int, string> primero = lista[0];
            List<KeyValuePair<int, string>> distintos = lista.Skip(1).Where(p => !MismaPO(primero.Value, p.Value)).ToList();
            if (!distintos.Any())
            {
                return null;
            }
            KeyValuePair<int, string> distinto = distintos[0];
            return $"No se pueden juntar en una misma factura: el pedido {primero.Key} {Descripcion(primero.Value)} y el {distinto.Key} {Descripcion(distinto.Value)}. Cada P.O. va en su propia factura.";
        }

        /// <summary>
        /// «Unir pedidos»: por qué no se puede unir la ampliación al original, o <c>null</c> si se puede. Una ampliación NUEVA
        /// (<paramref name="ampliacionEsNueva"/>, la de la plantilla, sin número) que no trae P.O. toma la del original: solo se
        /// rechaza si trae una P.O. distinta. Dos pedidos que ya existen: tienen que tener la misma (o ninguno).
        /// </summary>
        public static string MotivoNoSePuedenUnir(int numeroOriginal, string poOriginal, int numeroAmpliacion, string poAmpliacion, bool ampliacionEsNueva)
        {
            if (MismaPO(poOriginal, poAmpliacion))
            {
                return null;
            }
            if (ampliacionEsNueva)
            {
                return TienePO(poAmpliacion)
                    ? $"No se pueden unir: el pedido {numeroOriginal} {Descripcion(poOriginal)} y lo que quiere añadir es de la P.O. {poAmpliacion.Trim()}. Haga un pedido aparte para esa P.O."
                    : null;
            }
            return $"No se pueden unir: el pedido {numeroOriginal} {Descripcion(poOriginal)} y el {numeroAmpliacion} {Descripcion(poAmpliacion)}. Cada P.O. va en su propia factura: póngales la misma P.O. si de verdad son del mismo pedido del cliente.";
        }

        /// <summary>«es de la P.O. X» o «no tiene P.O.».</summary>
        public static string Descripcion(string suPedido)
        {
            return TienePO(suPedido) ? $"es de la P.O. {suPedido.Trim()}" : "no tiene P.O.";
        }
    }
}
