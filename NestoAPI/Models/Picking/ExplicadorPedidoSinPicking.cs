using NestoAPI.Infraestructure.Exceptions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>NestoAPI#608: el primer pedido a proveedor enviado y sin recibir de un producto.</summary>
    public class CompraPendientePicking
    {
        public int Pedido { get; set; }
        public DateTime? FechaPrevista { get; set; }
    }

    /// <summary>
    /// NestoAPI#608: en el picking de UN pedido que no sale, el porqué en palabras de almacén, en vez del genérico
    /// «No hay stock suficiente para asignar picking a ninguna línea» (Novedades 547: pedido 916947, modo 2, todo
    /// lo de pago con stock y faltaban 6 regalos de material promocional; el mensaje decía lo contrario de lo que
    /// se veía en la estantería). No decide nada: lee <see cref="PedidoPicking.MotivoNoSale"/>.
    /// </summary>
    public static class ExplicadorPedidoSinPicking
    {
        public const string ERROR_ESPERA_REGALOS = "PICKING_ESPERA_REGALOS";
        public const string ERROR_TODO_JUNTO_SIN_STOCK = "PICKING_TODO_JUNTO_SIN_STOCK";
        public const string ERROR_RESTO_DE_UNA_VEZ_SIN_STOCK = "PICKING_RESTO_DE_UNA_VEZ_SIN_STOCK";
        public const string ERROR_RETENIDO_PREPAGO = "PICKING_RETENIDO_PREPAGO";
        public const string ERROR_ESPERA_REPOSICION_TIENDAS = "PICKING_ESPERA_REPOSICION_TIENDAS";
        public const string ERROR_ENTREGA_FUTURA = "PICKING_ENTREGA_FUTURA";

        private const int MAXIMO_PRODUCTOS_EN_MENSAJE = 8;
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        /// <summary>Los productos que el mensaje va a nombrar (para buscar sus nombres y pedidos a proveedor).</summary>
        public static List<string> ProductosANombrar(PedidoPicking pedido)
        {
            return Faltan(pedido).Select(l => l.Producto?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        }

        /// <summary>
        /// El error con el motivo concreto, o null si no hay nada concreto que decir (el llamante da el genérico).
        /// SinStockDePago conserva <see cref="Constantes.Picking.ERROR_SIN_STOCK"/>: es el mismo caso de siempre,
        /// ahora diciendo qué falta.
        /// </summary>
        public static NestoBusinessException Error(PedidoPicking pedido,
            IDictionary<string, string> nombres, IDictionary<string, CompraPendientePicking> compras)
        {
            if (pedido == null)
            {
                return null;
            }
            nombres = nombres ?? new Dictionary<string, string>();
            compras = compras ?? new Dictionary<string, CompraPendientePicking>();
            string inicio = $"El pedido {pedido.Id} no sale: ";
            string faltan = ListaProductos(Faltan(pedido), nombres, compras);

            switch (pedido.MotivoNoSale)
            {
                case MotivoNoSalePicking.EsperaSoloRegalos:
                    return Crear(inicio + $"lo que se cobra tiene stock, pero faltan regalos ({faltan}) y no se sirve a medias " +
                        "para no mandar luego un envío solo con el regalo. Quita esas líneas o espera a que entren.",
                        ERROR_ESPERA_REGALOS, pedido);
                case MotivoNoSalePicking.TodoJuntoSinStock:
                    return Crear(inicio + $"está en «{Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.TODO_JUNTO)}» " +
                        $"y falta stock de: {faltan}. Saldrá cuando esté todo; si el cliente acepta recibirlo en varias veces, cambia el modo de servicio.",
                        ERROR_TODO_JUNTO_SIN_STOCK, pedido);
                case MotivoNoSalePicking.RestoDeUnaVezSinStock:
                    return Crear(inicio + $"está en «{Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ)}», " +
                        $"ya se mandó la primera entrega y el resto sale de una vez, pero falta stock de: {faltan}. Saldrá cuando esté todo.",
                        ERROR_RESTO_DE_UNA_VEZ_SIN_STOCK, pedido);
                case MotivoNoSalePicking.EsperaReposicionDeTiendas:
                    return Crear(inicio + $"está en «{Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS)}» " +
                        "y está esperando stock suyo que tiene que llegar de las tiendas" + (faltan.Length > 0 ? $" ({faltan})" : "") +
                        ". Saldrá cuando llegue la reposición.",
                        ERROR_ESPERA_REPOSICION_TIENDAS, pedido);
                case MotivoNoSalePicking.RetenidoPorPrepago:
                    return Crear(inicio + $"es de prepago y el pago no está cubierto (total {pedido.ImporteTotalPrepago.ToString("N2", castellano)} €, " +
                        $"disponible {pedido.ImporteDisponiblePrepago.ToString("N2", castellano)} €). Saldrá cuando se registre el pago.",
                        ERROR_RETENIDO_PREPAGO, pedido);
                case MotivoNoSalePicking.SinLineas when pedido.PrimeraEntregaFuturaQuitada.HasValue:
                    return Crear(inicio + "todas sus líneas pendientes tienen fecha de entrega posterior " +
                        $"(la primera, el {pedido.PrimeraEntregaFuturaQuitada.Value.ToString("dd/MM/yyyy", castellano)}). Saldrá en el picking de ese día.",
                        ERROR_ENTREGA_FUTURA, pedido);
                case MotivoNoSalePicking.SinStockDePago:
                    bool regalosConStock = pedido.Lineas != null && pedido.Lineas.Any(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                        && l.BaseImponible == 0 && l.CantidadReservada > 0);
                    return Crear(inicio + "no hay stock suficiente de lo que se cobra" + (faltan.Length > 0 ? $" ({faltan})" : "") + "." +
                        (regalosConStock ? " Los regalos sí tienen stock, pero no se manda un envío solo con regalos." : ""),
                        Constantes.Picking.ERROR_SIN_STOCK, pedido);
                default:
                    return null;
            }
        }

        /// <summary>Las que se guardaron al decidir (GeneradorPendientes quita luego las que no tienen nada reservado).</summary>
        private static List<LineaPedidoPicking> Faltan(PedidoPicking pedido)
            => pedido.LineasQueFaltanAlDecidir ?? LineasQueFaltan(pedido);

        /// <summary>
        /// Las líneas de producto que no tienen todo su stock en esta pasada. Si el pedido no sale por lo de pago,
        /// solo las de pago (los regalos sin stock no son el motivo); si son solo regalos, los regalos.
        /// </summary>
        internal static List<LineaPedidoPicking> LineasQueFaltan(PedidoPicking pedido)
        {
            if (pedido?.Lineas == null)
            {
                return new List<LineaPedidoPicking>();
            }
            List<LineaPedidoPicking> faltan = pedido.Lineas
                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.Cantidad > l.CantidadReservada)
                .ToList();
            if (pedido.MotivoNoSale == MotivoNoSalePicking.SinStockDePago && faltan.Any(l => l.BaseImponible != 0))
            {
                faltan = faltan.Where(l => l.BaseImponible != 0).ToList();
            }
            return faltan;
        }

        internal static string ListaProductos(List<LineaPedidoPicking> lineas,
            IDictionary<string, string> nombres, IDictionary<string, CompraPendientePicking> compras)
        {
            List<string> partes = lineas
                .GroupBy(l => l.Producto?.Trim() ?? "")
                .Select(g =>
                {
                    string producto = g.Key;
                    int unidades = g.Sum(l => l.Cantidad - l.CantidadReservada);
                    string texto = producto;
                    if (nombres.TryGetValue(producto, out string nombre) && !string.IsNullOrWhiteSpace(nombre))
                    {
                        texto += " " + nombre.Trim();
                    }
                    string detalle = unidades == 1 ? "falta 1" : $"faltan {unidades}";
                    if (compras.TryGetValue(producto, out CompraPendientePicking compra) && compra != null)
                    {
                        detalle += $"; pedido a proveedor {compra.Pedido}" +
                            (compra.FechaPrevista.HasValue ? $", previsto el {compra.FechaPrevista.Value.ToString("dd/MM", castellano)}" : "");
                    }
                    else
                    {
                        detalle += "; sin pedido a proveedor";
                    }
                    return $"{texto} ({detalle})";
                })
                .ToList();
            if (partes.Count > MAXIMO_PRODUCTOS_EN_MENSAJE)
            {
                int resto = partes.Count - MAXIMO_PRODUCTOS_EN_MENSAJE;
                partes = partes.Take(MAXIMO_PRODUCTOS_EN_MENSAJE).ToList();
                partes.Add($"y {resto} más");
            }
            return string.Join(", ", partes);
        }

        private static NestoBusinessException Crear(string mensaje, string codigo, PedidoPicking pedido)
        {
            return new NestoBusinessException(mensaje, new ErrorContext
            {
                ErrorCode = codigo,
                Empresa = pedido.Empresa?.Trim(),
                Pedido = pedido.Id
            })
            {
                IsWarning = true
            };
        }
    }
}
