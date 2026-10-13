using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// Sugerencia 582 (María José, venta telefónica): en el selector de clientes de Rapports, cuando
    /// el cliente tiene varios contactos, la vendedora telefónica quiere el contacto que está en
    /// estado 9 («En vías de recuperación», el de la venta por teléfono) y no el principal, que
    /// suele ser el del vendedor presencial o uno en estado 7.
    ///
    /// Solo aplica si quien pregunta es un vendedor telefónico (Vendedores.Estado = 9) y solo cuando
    /// Nesto lo pide (hoy, el selector de Rapports). Para el resto, todo sigue igual: el principal
    /// al teclear el número y la lista en el orden de siempre.
    /// </summary>
    public static class ContactoTelefonicoPrimero
    {
        public static bool EsVendedorTelefonico(IQueryable<Vendedor> vendedores, string empresa, string vendedor)
        {
            if (string.IsNullOrWhiteSpace(empresa) || string.IsNullOrWhiteSpace(vendedor))
            {
                return false;
            }
            string numero = vendedor.Trim();
            return vendedores.Any(v => v.Empresa == empresa && v.Número == numero
                && v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO);
        }

        /// <summary>
        /// Los contactos en estado 9 del cliente, en el orden en que se prefieren: primero los que
        /// lleva el propio vendedor y, a igualdad, el contacto más bajo. Vacío si no tiene ninguno
        /// (y entonces el que llama sigue con el principal de siempre).
        /// </summary>
        public static IQueryable<Cliente> ContactosTelefonicos(IQueryable<Cliente> clientes,
            string empresa, string cliente, string vendedor)
        {
            string numeroVendedor = vendedor?.Trim();
            return clientes
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente
                    && c.Estado == Constantes.Clientes.Estados.VISITA_TELEFONICA)
                .OrderByDescending(c => c.Vendedor == numeroVendedor)
                .ThenBy(c => c.Contacto);
        }

        /// <summary>
        /// En la lista de la búsqueda, dentro de cada cliente, los contactos en estado 9 delante.
        /// Los clientes conservan su orden (el de su primera aparición) y, dentro de cada grupo, el
        /// orden de llegada: solo se adelanta lo que está en estado 9.
        /// </summary>
        public static List<ClienteDTO> Ordenar(List<ClienteDTO> lista)
        {
            if (lista == null || lista.Count < 2)
            {
                return lista;
            }

            var posicionCliente = new Dictionary<string, int>();
            foreach (ClienteDTO c in lista)
            {
                string clave = c.cliente?.Trim() ?? string.Empty;
                if (!posicionCliente.ContainsKey(clave))
                {
                    posicionCliente.Add(clave, posicionCliente.Count);
                }
            }

            // OrderBy/ThenBy son estables: a igualdad, el orden de llegada
            return lista
                .OrderBy(c => posicionCliente[c.cliente?.Trim() ?? string.Empty])
                .ThenByDescending(c => c.estado == Constantes.Clientes.Estados.VISITA_TELEFONICA)
                .ToList();
        }
    }
}
