using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#518: un modo de servicio y si tiene sentido para el pedido (y por qué no, si no lo tiene).</summary>
    public class ModoServicioPermitidoDTO
    {
        public byte Modo { get; set; }
        public string Nombre { get; set; }
        public bool Permitido { get; set; }
        /// <summary>Por qué NO se puede elegir, en lenguaje de usuario. Null si está permitido.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#518 (Carlos, 23/09/26): qué modos de servicio tienen sentido para un pedido, para que Nesto y
    /// NestoApp no dejen elegir los que no (los usuarios elegían «Tras reponer de tiendas» en pedidos que salen
    /// enteros de Algete). Regla, con los colores de <see cref="SugeridorModoServicio"/>:
    /// <list type="bullet">
    /// <item>Pedido de TIENDA (todas sus líneas de producto en Alcobendas o Reina): solo «Según vaya entrando»;
    /// el cliente se lleva lo que hay.</item>
    /// <item>Algete, todo verde: solo «Todo junto» (los demás acaban haciendo lo mismo).</item>
    /// <item>NestoAPI#561 (pedido 927293, matiz de Carlos 29/09/26): Algete sin NINGUNA unidad que servir ahora
    /// (ni verdes, ni rosas, ni stock parcial de alguna roja): solo «Todo junto». Si alguna línea tiene parte
    /// del stock (1 de 2), «Ahora lo que hay, el resto de una vez» sí tiene sentido: sale esa unidad con sus
    /// portes y la otra en una entrega más.</item>
    /// <item>Algete, ninguna rosa: todos menos «Tras reponer de tiendas» (no hay nada que reponer).</item>
    /// <item>Algete con alguna rosa, pedido sin líneas de producto, o almacén de otro tipo (Amazon…): todos.</item>
    /// </list>
    /// Almacén del pedido con líneas de varios almacenes: si alguna es de Algete, manda Algete (el pedido lo
    /// sirve el almacén central); solo es «de tienda» si TODAS sus líneas son de una tienda.
    /// </summary>
    public static class ModosServicioPermitidos
    {
        public enum TipoAlmacen
        {
            /// <summary>Algete: aplica la regla por colores.</summary>
            Central,
            /// <summary>Alcobendas o Reina: solo «Según vaya entrando».</summary>
            Tienda,
            /// <summary>Cualquier otro (Amazon FBA…) o sin líneas: sin restricción.</summary>
            Otro
        }

        internal const string MOTIVO_TIENDA = "En tienda el cliente se lleva lo que hay: el pedido se sirve según vaya entrando.";
        internal const string MOTIVO_TODO_VERDE = "Todo el pedido tiene stock en Algete: sale todo junto.";
        internal const string MOTIVO_SIN_ROSAS = "No hay nada que traer de las tiendas: esperar a la reposición no cambiaría nada.";
        internal const string MOTIVO_NADA_CON_STOCK = "No hay ninguna unidad del pedido en stock: sale todo junto cuando llegue.";

        private static readonly byte[] TODOS =
        {
            Constantes.Pedidos.ModosServicio.TODO_JUNTO,
            Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
            Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
            Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ
        };

        /// <summary>El tipo de almacén del pedido a partir de sus líneas de producto.</summary>
        public static TipoAlmacen Clasificar(IEnumerable<LineaPedidoVentaDTO> lineasProducto)
        {
            List<string> almacenes = (lineasProducto ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Select(l => l?.almacen?.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!almacenes.Any())
            {
                return TipoAlmacen.Otro;
            }
            if (almacenes.Any(a => string.Equals(a, Constantes.Almacenes.ALGETE, System.StringComparison.OrdinalIgnoreCase)))
            {
                return TipoAlmacen.Central;
            }
            bool todasTienda = almacenes.All(a =>
                string.Equals(a, Constantes.Almacenes.ALCOBENDAS, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, Constantes.Almacenes.REINA, System.StringComparison.OrdinalIgnoreCase));
            return todasTienda ? TipoAlmacen.Tienda : TipoAlmacen.Otro;
        }

        /// <summary>El núcleo puro: los cuatro modos con su permiso y el motivo de los que no.</summary>
        /// <param name="hayAlgoQueServirAhora">NestoAPI#561: alguna línea tiene al menos una unidad disponible ya en su
        /// almacén (verde, o roja con stock parcial). Por defecto true: sin ese dato no se restringe nada.</param>
        public static List<ModoServicioPermitidoDTO> Calcular(TipoAlmacen tipo, int verdes, int rosas, int rojas, bool hayLineasProducto,
            bool hayAlgoQueServirAhora = true)
        {
            return TODOS.Select(modo => new ModoServicioPermitidoDTO
            {
                Modo = modo,
                Nombre = Constantes.Pedidos.ModosServicio.Nombre(modo),
                Motivo = MotivoNoPermitido(modo, tipo, verdes, rosas, rojas, hayLineasProducto, hayAlgoQueServirAhora)
            })
            .Select(m => { m.Permitido = m.Motivo == null; return m; })
            .ToList();
        }

        private static string MotivoNoPermitido(byte modo, TipoAlmacen tipo, int verdes, int rosas, int rojas, bool hayLineasProducto,
            bool hayAlgoQueServirAhora)
        {
            if (!hayLineasProducto)
            {
                return null;
            }
            switch (tipo)
            {
                case TipoAlmacen.Tienda:
                    return modo == Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO ? null : MOTIVO_TIENDA;
                case TipoAlmacen.Central:
                    if (verdes > 0 && rosas == 0 && rojas == 0)
                    {
                        return modo == Constantes.Pedidos.ModosServicio.TODO_JUNTO ? null : MOTIVO_TODO_VERDE;
                    }
                    if (verdes == 0 && rosas == 0 && !hayAlgoQueServirAhora)
                    {
                        // NestoAPI#561: ninguna unidad que servir ahora: todo junto (si no, salían solo los portes)
                        if (modo == Constantes.Pedidos.ModosServicio.TODO_JUNTO)
                        {
                            return null;
                        }
                        return modo == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS ? MOTIVO_SIN_ROSAS : MOTIVO_NADA_CON_STOCK;
                    }
                    if (rosas == 0)
                    {
                        return modo == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS ? MOTIVO_SIN_ROSAS : null;
                    }
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>Los números de los modos permitidos (lo que consumen los clientes para habilitar el combo).</summary>
        public static List<byte> Permitidos(IEnumerable<ModoServicioPermitidoDTO> modos)
            => (modos ?? Enumerable.Empty<ModoServicioPermitidoDTO>()).Where(m => m.Permitido).Select(m => m.Modo).ToList();
    }
}
