using NestoAPI.Models.PedidosBase;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Novedades 1314 (Alfredo, 17/11/26): los porcentajes de IVA y de recargo de una línea al crear el pedido. Una línea sin tipo
    /// de IVA (las de texto de una nota de recogida: «Recoged producto») o con uno que no está entre los parámetros del pedido se
    /// queda con 0 %: antes un <c>Single</c> rompía el POST con «La secuencia no contiene ningún elemento coincidente» y no se
    /// podía crear ningún pedido que solo tuviera líneas de texto. Pura.
    /// </summary>
    public static class PorcentajesIvaLinea
    {
        /// <summary>El parámetro del tipo de IVA de la línea, o null si la línea no lleva tipo o no está entre los del pedido.</summary>
        public static T Buscar<T>(IEnumerable<T> parametros, string ivaLinea) where T : ParametrosIvaBase
        {
            string codigo = ivaLinea?.Trim();
            if (parametros == null || string.IsNullOrEmpty(codigo))
            {
                return null;
            }
            return parametros.FirstOrDefault(p => p != null
                && string.Equals(p.CodigoIvaProducto?.Trim(), codigo, StringComparison.OrdinalIgnoreCase));
        }
    }
}
