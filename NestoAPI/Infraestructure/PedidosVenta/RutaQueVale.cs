using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#830 (sugerencia 1065, Alfredo; decisión de Carlos 05/11/26): la ruta de reparto que vale es SIEMPRE la de la
    /// ficha del cliente/contacto en la empresa 1, aunque el pedido sea de otra empresa (la 3). La ruta es un dato de reparto,
    /// igual para las dos empresas: en la 3 había 58 contactos con FW mientras en la 1 iban por nuestra ruta (AT, 16), y sus
    /// pedidos salían por agencia al día siguiente en vez de en su día de ruta. Sin ficha (viva) o sin ruta en la empresa 1,
    /// la de su propia empresa. ÚNICO sitio con la regla: el POST/PUT del pedido, la fecha de entrega de la plantilla y lo que
    /// la API propone al montar el pedido (direcciones de entrega, ficha del cliente) pasan por aquí.
    /// No cambia lo que se graba en Clientes.Ruta.
    /// </summary>
    public static class RutaQueVale
    {
        /// <summary>Si la empresa es la 1 (la que manda en la ruta).</summary>
        public static bool EsEmpresaQueManda(string empresa)
            => string.Equals(empresa?.Trim(), Constantes.Empresas.EMPRESA_POR_DEFECTO, StringComparison.Ordinal);

        /// <summary>LA REGLA: la ruta de la ficha de la empresa 1 si tiene; si no, la de la ficha de su empresa.</summary>
        public static string Elegir(string empresa, string rutaFichaEmpresa, string rutaFichaEmpresa1)
        {
            if (EsEmpresaQueManda(empresa) || string.IsNullOrWhiteSpace(rutaFichaEmpresa1))
            {
                return rutaFichaEmpresa?.Trim();
            }
            return rutaFichaEmpresa1.Trim();
        }

        /// <summary>
        /// La ruta con la que se graba el pedido. En la empresa 1, la que venga (como siempre). En otra empresa, si llega vacía
        /// o es la de la ficha de su empresa (la que proponía la aplicación), la que vale; si llega otra (Glovo, una agencia
        /// elegida a mano), esa: es una decisión del usuario para ese pedido, igual que en la empresa 1.
        /// </summary>
        public static string ParaGrabar(string empresa, string rutaPedido, string rutaFichaEmpresa, string rutaFichaEmpresa1)
        {
            if (EsEmpresaQueManda(empresa))
            {
                return rutaPedido;
            }
            string queVale = Elegir(empresa, rutaFichaEmpresa, rutaFichaEmpresa1);
            if (string.IsNullOrWhiteSpace(queVale))
            {
                return rutaPedido;
            }
            string venia = rutaPedido?.Trim();
            if (string.IsNullOrEmpty(venia) || string.Equals(venia, rutaFichaEmpresa?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return queVale;
            }
            return rutaPedido;
        }

        /// <summary>Las rutas de las fichas (de su empresa y de la 1) de un contacto. Una sola consulta.</summary>
        public static (string RutaFichaEmpresa, string RutaFichaEmpresa1) LeerFichas(NVEntities db, string empresa, string cliente, string contacto)
        {
            string empresaQueManda = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            var fichas = db.Clientes
                .Where(c => (c.Empresa == empresa || c.Empresa == empresaQueManda) && c.Nº_Cliente == cliente && c.Contacto == contacto)
                .Select(c => new { c.Empresa, c.Ruta, c.Estado })
                .ToList();
            string propia = fichas.FirstOrDefault(f => f.Empresa?.Trim() == empresa?.Trim())?.Ruta?.Trim();
            string deLa1 = fichas.FirstOrDefault(f => f.Empresa?.Trim() == empresaQueManda && f.Estado >= 0)?.Ruta?.Trim();
            return (propia, deLa1);
        }

        /// <summary>La ruta que vale para el contacto (leyendo sus fichas).</summary>
        public static string Leer(NVEntities db, string empresa, string cliente, string contacto)
        {
            if (EsEmpresaQueManda(empresa))
            {
                return db.Clientes
                    .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto)
                    .Select(c => c.Ruta).FirstOrDefault()?.Trim();
            }
            var (propia, deLa1) = LeerFichas(db, empresa, cliente, contacto);
            return Elegir(empresa, propia, deLa1);
        }

        /// <summary>Contacto → ruta de la ficha de la empresa 1 (vivas y con ruta) de todos los contactos de un cliente.</summary>
        public static Dictionary<string, string> RutasEmpresa1DelCliente(NVEntities db, string cliente)
        {
            string empresaQueManda = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            return db.Clientes
                .Where(c => c.Empresa == empresaQueManda && c.Nº_Cliente == cliente && c.Estado >= 0)
                .Select(c => new { c.Contacto, c.Ruta })
                .ToList()
                .Where(c => !string.IsNullOrWhiteSpace(c.Ruta) && c.Contacto != null)
                .GroupBy(c => c.Contacto.Trim())
                .ToDictionary(g => g.Key, g => g.First().Ruta.Trim());
        }

        /// <summary>
        /// Pone en el pedido la ruta con la que se graba (<see cref="ParaGrabar"/>). Devuelve la que venía si la ha cambiado; null
        /// si no. Nunca lanza: sin ficha legible el pedido se graba con lo que venga, como hasta ahora.
        /// </summary>
        public static string AjustarPedido(NVEntities db, PedidoVentaDTO pedido)
        {
            try
            {
                if (db == null || pedido == null || EsEmpresaQueManda(pedido.empresa)
                    || string.IsNullOrWhiteSpace(pedido.cliente) || pedido.contacto == null)
                {
                    return null;
                }
                var (propia, deLa1) = LeerFichas(db, pedido.empresa, pedido.cliente, pedido.contacto);
                string nueva = ParaGrabar(pedido.empresa, pedido.ruta, propia, deLa1);
                if (string.Equals(nueva?.Trim(), pedido.ruta?.Trim(), StringComparison.Ordinal))
                {
                    return null;
                }
                string venia = pedido.ruta;
                pedido.ruta = nueva;
                return venia ?? string.Empty;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
