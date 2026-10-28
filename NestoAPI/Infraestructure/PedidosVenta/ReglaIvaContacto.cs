using NestoAPI.Models.PedidosVenta;
using System;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Pedido 929498 (MariaJose, 28/10/26): la plantilla de Nesto mandó IVA G21 con la entrega al contacto 1, que es de
    /// recargo de equivalencia (E52). La cabecera, las líneas y el correo salieron sin recargo y lo tuvo que arreglar
    /// Alfredo a mano. El régimen de IVA es del CONTACTO del pedido (cada contacto es una ficha con su IVA), así que al
    /// crear el pedido manda el de la ficha: si el cliente manda otro, se sustituye y se deja constancia en ELMAH para ver
    /// qué aplicación lo hace. Null (sin IVA) se respeta: es una decisión explícita del usuario (botón «Cambiar IVA»).
    /// </summary>
    public static class ReglaIvaContacto
    {
        /// <summary>Si hay que cambiar el IVA del pedido por el del contacto, lo cambia (y vacía ParametrosIva, que eran del
        /// otro régimen) y devuelve el que venía; null si no hay nada que cambiar.</summary>
        public static string Ajustar(PedidoVentaDTO pedido, string ivaContacto)
        {
            if (pedido == null || string.IsNullOrWhiteSpace(pedido.iva) || string.IsNullOrWhiteSpace(ivaContacto))
            {
                return null;
            }
            string venia = pedido.iva.Trim();
            string ficha = ivaContacto.Trim();
            if (string.Equals(venia, ficha, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            pedido.iva = ficha;
            pedido.ParametrosIva = null; // el POST los vuelve a cargar para el IVA bueno
            return venia;
        }

        public static string MensajeElmah(PedidoVentaDTO pedido, string venia)
            => $"[IVA del contacto] El pedido del cliente {pedido?.cliente?.Trim()}/{pedido?.contacto?.Trim()} venía con IVA {venia} y la ficha del contacto tiene {pedido?.iva}: se graba con el de la ficha. Usuario {pedido?.Usuario}, origen {pedido?.origen}.";
    }
}
