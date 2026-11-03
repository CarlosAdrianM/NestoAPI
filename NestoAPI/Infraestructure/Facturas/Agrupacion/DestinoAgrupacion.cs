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

        /// <summary>
        /// NestoAPI#792: el contacto del cliente principal SOLO si su tipo de IVA es <paramref name="iva"/>. El IVA va en la
        /// cabecera (CabPedidoVta.IVA → CabFacturaVta.IVA) y cada contacto puede tener el suyo (31367: el 0 en E52 y el 2 en
        /// G21): si al destino de un grupo G21 se le pusiera el contacto principal E52, la factura saldría a nombre de un
        /// contacto con otro IVA. Con <c>null</c> el destino se queda con su contacto. Sin <paramref name="iva"/>, como
        /// <see cref="ContactoClientePrincipal(NVEntities, string, string)"/>.
        /// </summary>
        public static string ContactoClientePrincipal(NVEntities db, string empresa, string cliente, string iva)
        {
            if (string.IsNullOrWhiteSpace(iva))
            {
                return ContactoClientePrincipal(db, empresa, cliente);
            }
            var principal = db.Clientes
                .Where(c => c.Empresa == empresa
                         && c.Nº_Cliente == cliente
                         && c.ClientePrincipal)
                .Select(c => new { c.Contacto, c.IVA })
                .FirstOrDefault();
            if (principal == null)
            {
                return null;
            }
            return string.Equals((principal.IVA ?? string.Empty).Trim(), iva.Trim(), StringComparison.OrdinalIgnoreCase)
                ? principal.Contacto?.Trim()
                : null;
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
