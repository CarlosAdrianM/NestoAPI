using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#506: el modo de servicio con el que debe nacer un pedido según el stock REAL de sus
    /// líneas, con los mismos colores que el correo de pedido (<see cref="GestorStocks.ColorStock"/>):
    /// <list type="bullet">
    /// <item><b>verde</b>: stock suficiente en el almacén del pedido.</item>
    /// <item><b>rosa</b>: falta en el almacén pero lo hay en el conjunto (hay que traerlo de las tiendas).</item>
    /// <item><b>rojo</b>: no hay stock en ningún sitio.</item>
    /// </list>
    /// Regla (Carlos, 22/09/26): todo verde → «Todo junto» (1); solo verde y rojo → «Ahora lo que hay, el
    /// resto de una vez» (4); alguna rosa (haya o no verde y rojo) → «Tras reponer de tiendas» (3).
    /// Nunca se sugiere «Según vaya entrando» (2). Decisiones de los casos abiertos (22/09/26):
    /// <list type="bullet">
    /// <item>Pedido sin líneas de producto (solo texto o cuenta contable): el defecto de siempre (3), no hay nada que mirar.</item>
    /// <item>Rosa + rojo → 3, aceptando que la parte roja pueda llegar después en más de una entrega.</item>
    /// <item>Una línea parcialmente cubrible desde tiendas sale roja (como en el correo) y cuenta como roja.</item>
    /// <item>Solo se sugiere al crear; al modificar no se recalcula el modo.</item>
    /// <item>NestoAPI#515: el color se pide con la cantidad de la línea y agrupando por producto y
    /// almacén, porque el pedido aún no está grabado. Los recuentos (LineasVerdes/Rosas/Rojas) son, por
    /// tanto, de grupos producto+almacén, no de líneas sueltas.</item>
    /// </list>
    /// Es la única fuente: la usan <c>PostPedidoVenta</c> cuando el cliente no manda modo, la estimación de
    /// portes del carrito de la app y el endpoint <c>POST api/PedidosVenta/ModoServicioSugerido</c> con el
    /// que las plantillas de Nesto y NestoApp preseleccionan el modo antes de confirmar.
    /// </summary>
    public static class SugeridorModoServicio
    {
        public const string VERDE = "green";
        public const string ROSA = "DeepPink";
        public const string ROJO = "red";

        public class Sugerencia
        {
            public byte Modo { get; set; }
            public string Nombre { get; set; }
            public int LineasVerdes { get; set; }
            public int LineasRosas { get; set; }
            public int LineasRojas { get; set; }
            public string Motivo { get; set; }
            /// <summary>NestoAPI#518: los modos que se pueden elegir para este pedido (el sugerido siempre está).</summary>
            public List<byte> ModosPermitidos { get; set; } = new List<byte>();
            /// <summary>NestoAPI#518: los cuatro modos con su permiso y, si no se puede, el motivo para enseñarlo.</summary>
            public List<ModoServicioPermitidoDTO> Modos { get; set; } = new List<ModoServicioPermitidoDTO>();
        }

        public static Sugerencia Sugerir(PedidoVentaDTO pedido, IGestorStocks stocks)
        {
            if (stocks == null)
            {
                throw new ArgumentNullException(nameof(stocks));
            }
            List<LineaPedidoVentaDTO> productos = (pedido?.Lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                            && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0)
                .ToList();
            if (!productos.Any())
            {
                return ConModos(new Sugerencia
                {
                    Modo = Constantes.Pedidos.ModosServicio.POR_DEFECTO,
                    Nombre = Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.POR_DEFECTO),
                    Motivo = "El pedido no tiene líneas de producto: se aplica el modo por defecto."
                }, ModosServicioPermitidos.TipoAlmacen.Otro, hayLineasProducto: false);
            }

            // NestoAPI#518: en tienda el cliente se lleva lo que hay; no hace falta mirar el stock.
            ModosServicioPermitidos.TipoAlmacen tipo = ModosServicioPermitidos.Clasificar(productos);
            if (tipo == ModosServicioPermitidos.TipoAlmacen.Tienda)
            {
                return ConModos(new Sugerencia
                {
                    Modo = Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                    Nombre = Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO),
                    Motivo = "Pedido de tienda: el cliente se lleva lo que hay y el resto se sirve según vaya entrando."
                }, tipo, hayLineasProducto: true);
            }

            // NestoAPI#518 (Carlos, 23/09/26): un almacén que no es Algete ni tienda (Amazon, AMZ) no tiene
            // restricción de modos, pero por defecto sale todo junto: no se mira el stock de Algete.
            if (tipo == ModosServicioPermitidos.TipoAlmacen.Otro)
            {
                return ConModos(new Sugerencia
                {
                    Modo = Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                    Nombre = Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.TODO_JUNTO),
                    Motivo = "Pedido de un almacén sin reglas de stock (por ejemplo Amazon): por defecto sale todo junto."
                }, tipo, hayLineasProducto: true);
            }

            // NestoAPI#515: un color por producto y almacén, con la CANTIDAD que se pide sumada. Dos cosas:
            //   - el pedido todavía no existe, así que sus unidades no están en pendientes de entregar y
            //     hay que descontarlas a mano (si no, 7 en el almacén salen verdes aunque se pidan 8);
            //   - dos líneas del mismo producto y almacén (la normal y la de regalo de una oferta) consumen
            //     el mismo stock: se suman y se pide un solo color, o se contaría dos veces.
            var colores = productos
                .GroupBy(l => new { Producto = l.Producto.Trim(), Almacen = l.almacen?.Trim() })
                .Select(g => stocks.ColorStock(g.Key.Producto, g.Key.Almacen, g.Sum(l => l.Cantidad)))
                .ToList();
            int verdes = colores.Count(c => c == VERDE);
            int rosas = colores.Count(c => c == ROSA);
            int rojas = colores.Count(c => c != VERDE && c != ROSA);

            return ConModos(Decidir(verdes, rosas, rojas), tipo, hayLineasProducto: true);
        }

        /// <summary>NestoAPI#518: rellena los modos permitidos con los recuentos ya calculados.</summary>
        private static Sugerencia ConModos(Sugerencia sugerencia, ModosServicioPermitidos.TipoAlmacen tipo, bool hayLineasProducto)
        {
            sugerencia.Modos = ModosServicioPermitidos.Calcular(tipo, sugerencia.LineasVerdes, sugerencia.LineasRosas, sugerencia.LineasRojas, hayLineasProducto);
            sugerencia.ModosPermitidos = ModosServicioPermitidos.Permitidos(sugerencia.Modos);
            return sugerencia;
        }

        /// <summary>
        /// NestoAPI#518: el modo que fuerza el parámetro ModoServicioPorDefecto se respeta SOLO si tiene sentido
        /// para el pedido; si no, se propone el del stock y se explica. Devuelve una COPIA (la sugerencia de
        /// entrada puede venir de la caché por huella de #517, compartida entre usuarios).
        /// </summary>
        public static Sugerencia AplicarForzado(Sugerencia sugerencia, byte forzado)
        {
            var copia = new Sugerencia
            {
                Modo = sugerencia.Modo,
                Nombre = sugerencia.Nombre,
                LineasVerdes = sugerencia.LineasVerdes,
                LineasRosas = sugerencia.LineasRosas,
                LineasRojas = sugerencia.LineasRojas,
                Motivo = sugerencia.Motivo,
                ModosPermitidos = new List<byte>(sugerencia.ModosPermitidos ?? new List<byte>()),
                Modos = sugerencia.Modos
            };
            if (copia.ModosPermitidos.Contains(forzado))
            {
                copia.Modo = forzado;
                copia.Nombre = Constantes.Pedidos.ModosServicio.Nombre(forzado);
                copia.Motivo = "Modo fijado por el parámetro ModoServicioPorDefecto del usuario.";
                return copia;
            }
            string porQue = copia.Modos?.FirstOrDefault(m => m.Modo == forzado)?.Motivo;
            copia.Motivo = $"Tu parámetro ModoServicioPorDefecto fija «{Constantes.Pedidos.ModosServicio.Nombre(forzado)}», " +
                           $"pero no tiene sentido para este pedido{(string.IsNullOrWhiteSpace(porQue) ? string.Empty : $" ({porQue.TrimEnd('.')})")}. " +
                           sugerencia.Motivo;
            return copia;
        }

        /// <summary>El núcleo puro de la regla, a partir de los recuentos por color.</summary>
        internal static Sugerencia Decidir(int verdes, int rosas, int rojas)
        {
            byte modo;
            string motivo;
            if (rosas > 0)
            {
                modo = Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS;
                motivo = $"{rosas} línea{(rosas == 1 ? string.Empty : "s")} hay que traerla{(rosas == 1 ? string.Empty : "s")} de las tiendas: se espera a la reposición.";
            }
            else if (rojas > 0)
            {
                modo = Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ;
                motivo = $"{rojas} línea{(rojas == 1 ? string.Empty : "s")} sin stock en ningún almacén: sale lo que hay y el resto en una sola entrega más.";
            }
            else
            {
                modo = Constantes.Pedidos.ModosServicio.TODO_JUNTO;
                motivo = "Hay stock de todo en el almacén del pedido: sale todo junto.";
            }
            return new Sugerencia
            {
                Modo = modo,
                Nombre = Constantes.Pedidos.ModosServicio.Nombre(modo),
                LineasVerdes = verdes,
                LineasRosas = rosas,
                LineasRojas = rojas,
                Motivo = motivo
            };
        }
    }
}
