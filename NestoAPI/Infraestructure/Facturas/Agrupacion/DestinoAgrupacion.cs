using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727: elección del pedido destino, común a las estrategias de agrupación (P.O. y fin de mes): el pedido más
    /// antiguo cuyo contacto sea el del cliente principal (<c>Clientes.ClientePrincipal = 1</c>); si ninguno lo es, el más
    /// antiguo, y se le pone en cabecera el contacto principal para que el deudor de la factura sea el principal.
    /// </summary>
    public static class DestinoAgrupacion
    {
        /// <summary>
        /// Devuelve el destino entre <paramref name="candidatos"/>, o <c>null</c> si no hay ninguno.
        /// </summary>
        public static CabPedidoVta Elegir(IEnumerable<CabPedidoVta> candidatos, string contactoPrincipal)
        {
            List<CabPedidoVta> lista = (candidatos ?? Enumerable.Empty<CabPedidoVta>())
                .Where(p => p != null)
                .OrderBy(p => p.Número)
                .ToList();
            if (!lista.Any())
            {
                return null;
            }

            // Preferimos el pedido (más antiguo) cuyo contacto ya sea el del cliente principal.
            CabPedidoVta destino = lista
                .FirstOrDefault(p => !string.IsNullOrEmpty(contactoPrincipal)
                         && string.Equals((p.Contacto ?? string.Empty).Trim(), contactoPrincipal,
                                StringComparison.OrdinalIgnoreCase));
            if (destino != null)
            {
                return destino;
            }

            // Si ninguno es el principal, tomamos el más antiguo y le ponemos el contacto
            // del principal en cabecera para que el deudor de la factura sea el principal.
            destino = lista.First();
            if (!string.IsNullOrEmpty(contactoPrincipal))
            {
                destino.Contacto = contactoPrincipal;
            }
            return destino;
        }

        /// <summary>El contacto del cliente principal (recortado), o <c>null</c> si el cliente no tiene ninguno marcado.</summary>
        public static string ContactoClientePrincipal(NVEntities db, string empresa, string cliente)
        {
            return db.Clientes
                .Where(c => c.Empresa == empresa
                         && c.Nº_Cliente == cliente
                         && c.ClientePrincipal)
                .Select(c => c.Contacto)
                .FirstOrDefault()
                ?.Trim();
        }
    }
}
