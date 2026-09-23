using System;
using System.Linq;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#518: al guardar, el modo de servicio elegido tiene que estar entre los permitidos
    /// (<see cref="ModosServicioPermitidos"/>); si no, <see cref="ModoServicioNoPermitidoException"/> con el
    /// modo que sí vale. Solo se comprueba cuando el modo lo ELIGE el cliente:
    /// <list type="bullet">
    /// <item>Al crear, si el pedido trae modo (sin modo se aplica el sugerido y no hay nada que rechazar).</item>
    /// <item>Al modificar, solo si el modo efectivo cambia respecto al guardado: un pedido que ya tiene
    /// picking no se bloquea por un cambio de stock que no ha pedido nadie.</item>
    /// </list>
    /// Los marketplaces (modo 1 explícito, #476) nunca chocan: salen de Algete, donde «Todo junto» siempre
    /// está permitido, o de un almacén de otro tipo (Amazon), sin restricción.
    /// </summary>
    public static class ValidadorModoServicio
    {
        /// <summary>
        /// Al crear: el modo que trae el pedido (antes de normalizar) debe estar permitido. Devuelve el modo con
        /// el que hay que guardarlo si hubo que corregirlo (solo en tienda), o null si vale el que trae.
        /// </summary>
        public static byte? ComprobarAlCrear(PedidoVentaDTO pedido, IGestorStocks stocks)
        {
            if (pedido?.modoServicio == null)
            {
                return null;
            }
            return Comprobar(pedido, pedido.modoServicio.Value, SugeridorModoServicio.Sugerir(pedido, stocks),
                "El stock ha cambiado mientras montabas el pedido y ");
        }

        /// <summary>
        /// Al modificar un pedido ya grabado. Sus propias unidades ya están reservadas y el color que se
        /// calcula con la cantidad las descontaría dos veces: una rosa podría salir roja y rechazar «Tras reponer
        /// de tiendas» sin motivo. Por eso aquí solo se aplica lo que ese doble descuento no puede falsear: la
        /// regla de tienda y «todo verde» (si aun descontando dos veces sale todo verde, lo es). Las rojas se
        /// tratan como posibles rosas.
        /// </summary>
        public static byte? ComprobarAlModificar(PedidoVentaDTO pedido, byte modoEfectivoAnterior, byte modoEfectivoNuevo, IGestorStocks stocks)
        {
            if (pedido == null || modoEfectivoAnterior == modoEfectivoNuevo)
            {
                return null;
            }
            SugeridorModoServicio.Sugerencia sugerencia = SugeridorModoServicio.Sugerir(pedido, stocks);
            var lineas = LineasProducto(pedido);
            var tolerante = new SugeridorModoServicio.Sugerencia
            {
                Modo = sugerencia.Modo,
                Nombre = sugerencia.Nombre,
                Motivo = sugerencia.Motivo,
                Modos = ModosServicioPermitidos.Calcular(ModosServicioPermitidos.Clasificar(lineas),
                    sugerencia.LineasVerdes, sugerencia.LineasRosas + sugerencia.LineasRojas, 0, hayLineasProducto: lineas.Any())
            };
            tolerante.ModosPermitidos = ModosServicioPermitidos.Permitidos(tolerante.Modos);
            return Comprobar(pedido, modoEfectivoNuevo, tolerante, "Con el stock de ahora ");
        }

        private static System.Collections.Generic.List<LineaPedidoVentaDTO> LineasProducto(PedidoVentaDTO pedido)
            => (pedido.Lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                            && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0)
                .ToList();

        private static byte? Comprobar(PedidoVentaDTO pedido, byte modo, SugeridorModoServicio.Sugerencia sugerencia, string arranque)
        {
            if (sugerencia?.ModosPermitidos == null || !sugerencia.ModosPermitidos.Any() || sugerencia.ModosPermitidos.Contains(modo))
            {
                return null;
            }
            byte valido = sugerencia.ModosPermitidos.Contains(sugerencia.Modo) ? sugerencia.Modo : sugerencia.ModosPermitidos.First();
            string nombreValido = Constantes.Pedidos.ModosServicio.Nombre(valido);
            string porQue = sugerencia.Modos?.FirstOrDefault(m => m.Modo == modo)?.Motivo?.TrimEnd('.') ?? "no encaja con el stock del pedido";
            bool esTienda = porQue == ModosServicioPermitidos.MOTIVO_TIENDA.TrimEnd('.');
            if (esTienda)
            {
                // Decisión de Carlos (23/09/26): en tienda no se rechaza (el cliente está delante); se guarda
                // con el único modo que tiene sentido. Los clientes ya solo ofrecen ese modo, así que no debería darse.
                return valido;
            }
            string mensaje = (esTienda ? string.Empty : arranque) +
                             $"{(esTienda ? "El" : "el")} modo «{Constantes.Pedidos.ModosServicio.Nombre(modo)}» no tiene sentido para este pedido ({porQue}). " +
                             $"Elige «{nombreValido}» y vuelve a guardar.";
            throw new ModoServicioNoPermitidoException(mensaje, valido, nombreValido, sugerencia.ModosPermitidos, pedido.empresa, pedido.numero == 0 ? (int?)null : pedido.numero);
        }
    }
}
