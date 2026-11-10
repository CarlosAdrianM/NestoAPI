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
        /// <summary>NestoAPI#698: modo 5, retenido hasta nuevo aviso.</summary>
        public const string ERROR_RETENIDO = "PICKING_RETENIDO";

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
                case MotivoNoSalePicking.Retenido:
                    string retenido = pedido.Retencion != null
                        ? "está " + Infraestructure.PedidosVenta.Retencion.TextosRetencion.Descripcion(pedido.Retencion, false)
                        : "está retenido hasta nuevo aviso";
                    return Crear(inicio + retenido + ". No saldrá en ningún picking hasta que se libere: botón «Liberar» en el pedido (lo puede hacer el vendedor o el almacén).",
                        ERROR_RETENIDO, pedido);
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
                        $"disponible {pedido.ImporteDisponiblePrepago.ToString("N2", castellano)} €). Saldrá cuando se registre el pago." +
                        PedidoPicking.TextoReembolsosPendientesDeEntrega(pedido.ExtractosPendientes),
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

        /// <summary>Carlos 03/11/26: en el picking de un cliente se explican como mucho estos pedidos.</summary>
        public const int MAXIMO_PEDIDOS_EXPLICADOS = 5;

        /// <summary>
        /// Carlos 03/11/26 (pedido 929864): el error del picking de un CLIENTE cuando no sale ninguno de sus pedidos. Uno solo, tal
        /// cual; varios, un mensaje por pedido (una línea cada uno) con el código del primero. Sin ninguno, null (el mensaje de siempre).
        /// </summary>
        public static NestoBusinessException ErrorVarios(IList<NestoBusinessException> errores)
        {
            if (errores == null || errores.Count == 0)
            {
                return null;
            }
            if (errores.Count == 1)
            {
                return errores[0];
            }
            string mensaje = string.Join(Environment.NewLine, errores.Select(e => e.Message));
            return new NestoBusinessException(mensaje, new ErrorContext { ErrorCode = errores[0].Context?.ErrorCode })
            {
                IsWarning = true
            };
        }

        /// <summary>
        /// Novedades 1164 (Alfredo, 10/11/26): el picking de VARIOS pedidos (rutas, nuestra ruta, automático, cliente) cuando no sale
        /// ninguno. Antes se explicaban los 5 primeros descartados, que eran los más antiguos y todos de entrega futura, y parecía un
        /// fallo; lo importante (7 pedidos esperando la reposición de las tiendas) no salía. Ahora la verdad útil: qué hay detenido
        /// y por qué, por motivo, de más a menos relevante. Los de entrega futura y los que no tienen stock de nada, solo contados.
        /// </summary>
        public const string ERROR_NADA_QUE_SACAR = "PICKING_NADA_QUE_SACAR";

        /// <summary>Pedidos que se nombran en cada motivo del resumen; el resto, «y N más».</summary>
        public const int MAXIMO_PEDIDOS_POR_MOTIVO = 8;
        private const int MAXIMO_PRODUCTOS_POR_PEDIDO = 3;

        public const string NADA_QUE_SACAR = "No hay ningún pedido que pueda salir ahora.";

        /// <summary>
        /// El resumen por motivo de los descartados, o null si no hay ninguno (el llamante da el genérico). Con un solo descartado,
        /// su explicación concreta (<paramref name="explicacionUnico"/>, la de <see cref="Error"/>) detrás de la primera frase.
        /// Mismo código siempre, para que el picking automático y Nesto lo traten como «no había nada», no como un fallo.
        /// </summary>
        public static NestoBusinessException ErrorNadaQueSacar(IList<PedidoPicking> descartados, NestoBusinessException explicacionUnico = null)
        {
            if (descartados == null || descartados.Count == 0)
            {
                return null;
            }
            if (descartados.Count == 1 && explicacionUnico != null)
            {
                return CrearNadaQueSacar(NADA_QUE_SACAR + " " + explicacionUnico.Message);
            }

            var lineas = new List<string> { NADA_QUE_SACAR };
            void Motivo(MotivoNoSalePicking motivo, string texto, bool conProductos)
            {
                List<PedidoPicking> pedidos = descartados.Where(p => p != null && p.MotivoNoSale == motivo).ToList();
                if (pedidos.Count > 0)
                {
                    lineas.Add($"{texto} ({pedidos.Count}): {ListaPedidos(pedidos, conProductos)}.");
                }
            }
            string Modo(byte modo) => Constantes.Pedidos.ModosServicio.Nombre(modo);

            // De más a menos relevante: lo que tiene mercancía lista o el almacén esperaría ver salir
            Motivo(MotivoNoSalePicking.EsperaReposicionDeTiendas,
                $"Esperan la reposición de las tiendas («{Modo(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS)}»)", true);
            Motivo(MotivoNoSalePicking.TodoJuntoSinStock,
                $"En «{Modo(Constantes.Pedidos.ModosServicio.TODO_JUNTO)}» y les falta stock", true);
            Motivo(MotivoNoSalePicking.RestoDeUnaVezSinStock,
                $"En «{Modo(Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ)}» y les falta stock para el resto", true);
            Motivo(MotivoNoSalePicking.EsperaSoloRegalos,
                "Lo que se cobra tiene stock, pero esperan regalos", true);
            Motivo(MotivoNoSalePicking.RetenidoPorPrepago,
                "De prepago con el pago sin cubrir", false);
            Motivo(MotivoNoSalePicking.Retenido,
                "Retenidos hasta nuevo aviso", false);

            // Lo que no se espera ver salir hoy: solo contado
            int futuros = descartados.Count(p => p != null && p.MotivoNoSale == MotivoNoSalePicking.SinLineas && p.PrimeraEntregaFuturaQuitada.HasValue);
            int sinStock = descartados.Count(p => p != null && p.MotivoNoSale == MotivoNoSalePicking.SinStockDePago);
            var ademas = new List<string>();
            if (futuros > 0)
            {
                ademas.Add(futuros == 1 ? "1 pedido tiene la entrega para más adelante" : $"{futuros} pedidos tienen la entrega para más adelante");
            }
            if (sinStock > 0)
            {
                ademas.Add(sinStock == 1 ? "1 no tiene stock de nada de lo que se cobra" : $"{sinStock} no tienen stock de nada de lo que se cobra");
            }
            if (ademas.Count > 0)
            {
                // Si solo hay de estos, sin «Además»
                string frase = string.Join(" y ", ademas) + ".";
                lineas.Add(lineas.Count > 1 ? "Además, " + frase : char.ToUpper(frase[0], castellano) + frase.Substring(1));
            }
            return CrearNadaQueSacar(string.Join(Environment.NewLine, lineas));
        }

        private static string ListaPedidos(List<PedidoPicking> pedidos, bool conProductos)
        {
            List<string> partes = pedidos.Take(MAXIMO_PEDIDOS_POR_MOTIVO).Select(p =>
            {
                if (!conProductos)
                {
                    return p.Id.ToString();
                }
                List<string> productos = Faltan(p).Select(l => l.Producto?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
                if (productos.Count == 0)
                {
                    return p.Id.ToString();
                }
                string lista = string.Join(", ", productos.Take(MAXIMO_PRODUCTOS_POR_PEDIDO));
                if (productos.Count > MAXIMO_PRODUCTOS_POR_PEDIDO)
                {
                    lista += $" y {productos.Count - MAXIMO_PRODUCTOS_POR_PEDIDO} más";
                }
                return $"{p.Id} ({lista})";
            }).ToList();
            string texto = string.Join(", ", partes);
            if (pedidos.Count > MAXIMO_PEDIDOS_POR_MOTIVO)
            {
                texto += $" y {pedidos.Count - MAXIMO_PEDIDOS_POR_MOTIVO} más";
            }
            return texto;
        }

        private static NestoBusinessException CrearNadaQueSacar(string mensaje)
        {
            return new NestoBusinessException(mensaje, new ErrorContext { ErrorCode = ERROR_NADA_QUE_SACAR })
            {
                IsWarning = true
            };
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
