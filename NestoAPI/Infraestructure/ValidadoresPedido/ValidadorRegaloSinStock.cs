using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#529: no se mete un regalo que no tiene stock. Regalo = línea de producto a base 0
    /// sin nº de oferta (Ganavisión, regalo por importe de pedido, material promocional); las
    /// unidades gratis de una oferta las decide la oferta. Un regalo sin stock acababa saliendo
    /// solo, en un envío de 0 € (pedido 926923), o dejando más pendientes que stock (40144, #528).
    ///
    /// <para>Es un rechazo DURO (AutorizadaDenegadaExpresamente): ningún validador de aceptación lo
    /// anula. Hasta ahora el stock solo lo miraba ValidadorGanavisiones, y solo cuando otro
    /// validador de denegación había señalado antes el producto.</para>
    ///
    /// <para>Solo cuentan las unidades NUEVAS: líneas nuevas, líneas a las que se cambia el
    /// producto y lo que sube la cantidad de una línea guardada. Lo ya guardado se respeta, como
    /// en ValidadorGanavisiones (#228). El stock es el del almacén del que tiene que salir el
    /// regalo, o el de todas las sedes si el pedido espera a reponer (#528, ver AlmacenDelStock).</para>
    /// </summary>
    public class ValidadorRegaloSinStock : IValidadorDenegacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion { ValidacionSuperada = true };
            List<FaltaDeStockRegalo> faltas = FaltasDeStock(pedido, servicio, l => true).ToList();
            if (!faltas.Any())
            {
                return respuesta;
            }

            respuesta.ValidacionSuperada = false;
            respuesta.AutorizadaDenegadaExpresamente = true;

            // NestoAPI#649: las muestras de un pedido por ruta van todas en UN mensaje (la usuaria del
            // 15/10/26 tenía dos), diciendo cuántas hay en Algete, para que las quite o las reduzca.
            List<FaltaDeStockRegalo> muestras = faltas.Where(f => f.EsMuestraPorRuta).ToList();
            if (muestras.Any())
            {
                respuesta.ProductoId = muestras[0].Producto;
                respuesta.Motivo = MensajeMuestrasPorRuta(muestras);
                return respuesta;
            }

            FaltaDeStockRegalo falta = faltas[0];
            respuesta.ProductoId = falta.Producto;
            string donde = falta.Almacen == null ? string.Empty : $" en {falta.Almacen}";
            respuesta.Motivo = falta.Disponible <= 0
                ? $"No se puede regalar el producto {falta.Producto}: no hay stock{donde}. Elija otro regalo."
                : $"No se pueden regalar {falta.UnidadesNuevas} unidades del producto {falta.Producto}: solo hay {falta.Disponible} disponibles{donde}.";
            return respuesta;
        }

        /// <summary>
        /// NestoAPI#649: mensaje de usted con cada muestra y las unidades que quedan en Algete.
        /// </summary>
        internal static string MensajeMuestrasPorRuta(IList<FaltaDeStockRegalo> muestras)
        {
            string lista = string.Join(", ", muestras.Select(m =>
            {
                string nombre = string.IsNullOrWhiteSpace(m.Nombre) ? string.Empty : $" {m.Nombre.Trim()}";
                string cuantas = m.Disponible <= 0
                    ? "no hay ninguna en Algete"
                    : $"solo hay {m.Disponible} disponible{(m.Disponible == 1 ? string.Empty : "s")} en Algete";
                return $"{m.Producto}{nombre} ({cuantas})";
            }));

            return muestras.Count == 1
                ? "Este pedido sale por ruta desde Algete y las muestras (material promocional) solo pueden llevar las unidades " +
                  $"que hay allí: no se reponen para un pedido. Quite esta muestra del pedido o reduzca su cantidad: {lista}."
                : "Este pedido sale por ruta desde Algete y las muestras (material promocional) solo pueden llevar las unidades " +
                  $"que hay allí: no se reponen para un pedido. Quite estas muestras del pedido o reduzca su cantidad: {lista}.";
        }

        /// <summary>
        /// NestoAPI#528: núcleo común del stock de los regalos (este validador, ValidadorGanavisiones y
        /// las sugerencias). Primer grupo de regalos (producto + almacén del que se tienen que servir)
        /// cuyas unidades nuevas no caben en el stock; null si caben todos.
        /// </summary>
        internal static FaltaDeStockRegalo PrimeraFaltaDeStock(PedidoVentaDTO pedido, IServicioPrecios servicio, Func<LineaPedidoVentaDTO, bool> filtro)
        {
            return FaltasDeStock(pedido, servicio, filtro).FirstOrDefault();
        }

        /// <summary>
        /// Todos los grupos de regalos (producto + almacén del que se tienen que servir) cuyas unidades
        /// nuevas no caben en el stock, en orden de aparición. Perezoso: PrimeraFaltaDeStock para en la primera.
        /// </summary>
        internal static IEnumerable<FaltaDeStockRegalo> FaltasDeStock(PedidoVentaDTO pedido, IServicioPrecios servicio, Func<LineaPedidoVentaDTO, bool> filtro)
        {
            if (pedido?.Lineas == null || servicio == null)
            {
                yield break;
            }

            var grupos = pedido.Lineas
                .Where(l => EsRegalo(l) && filtro(l))
                .GroupBy(l => new
                {
                    Producto = l.Producto.Trim(),
                    Almacen = AlmacenDelStock(pedido, l, servicio),
                    EsMuestraPorRuta = EsMuestraQueSalePorRutaDesdeAlgete(pedido, l, servicio)
                });

            foreach (var regalos in grupos)
            {
                int unidadesNuevas = regalos.Sum(UnidadesNuevas);
                Producto producto = unidadesNuevas > 0 ? servicio.BuscarProducto(regalos.Key.Producto) : null;
                if (unidadesNuevas <= 0 || producto?.Ficticio == true)
                {
                    // Un producto ficticio no lleva stock: exigírselo lo bloquearía siempre
                    continue;
                }

                int disponible = servicio.BuscarStockDisponibleParaRegalar(regalos.Key.Producto, regalos.Key.Almacen);
                if (unidadesNuevas > disponible)
                {
                    yield return new FaltaDeStockRegalo
                    {
                        Producto = regalos.Key.Producto,
                        Nombre = producto?.Nombre?.Trim(),
                        Almacen = regalos.Key.Almacen,
                        UnidadesNuevas = unidadesNuevas,
                        Disponible = disponible,
                        EsMuestraPorRuta = regalos.Key.EsMuestraPorRuta
                    };
                }
            }
        }

        /// <summary>
        /// NestoAPI#649: rutas de reparto que salen de Algete (ruta propia y agencias, las mismas que
        /// factura GestorFacturacionRutas). Las demás (recoger en tienda, Glovo…) quedan fuera.
        /// </summary>
        internal static readonly HashSet<string> RUTAS_QUE_SALEN_DE_ALGETE = new HashSet<string>(
            new RutaPropia().RutasContenidas.Concat(new RutaAgencia().RutasContenidas).Select(r => r.Trim()),
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// NestoAPI#649 (Manuel, Compras, 15/10/26; decisión de Carlos): en un pedido que sale por ruta
        /// desde Algete, una muestra (subgrupo MMP, «Muestras y material promocional») solo puede llevar
        /// las unidades que hay en Algete, sea cual sea el modo de servicio: las muestras no se reponen
        /// para un pedido, así que con stock solo en las tiendas el pedido se quedaba esperando o salía
        /// en dos veces. Se rechaza al grabar para que el usuario la quite; no se recorta sola.
        ///
        /// <para>Solo cuando todo encaja: ruta de reparto (RUTAS_QUE_SALEN_DE_ALGETE), línea en ALG y
        /// no es presupuesto. Pedidos de tienda (líneas en REI/ALC) y presupuestos siguen como antes.</para>
        /// </summary>
        internal static bool EsMuestraQueSalePorRutaDesdeAlgete(PedidoVentaDTO pedido, LineaPedidoVentaDTO linea, IServicioPrecios servicio, string producto = null)
        {
            if (pedido == null || linea == null || servicio == null || pedido.EsPresupuesto
                || linea.estado == Constantes.EstadosLineaVenta.PRESUPUESTO)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(pedido.ruta) || !RUTAS_QUE_SALEN_DE_ALGETE.Contains(pedido.ruta.Trim()))
            {
                return false;
            }
            if (!string.Equals(linea.almacen?.Trim(), Constantes.Almacenes.ALGETE, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string numero = (producto ?? linea.Producto)?.Trim();
            if (string.IsNullOrEmpty(numero))
            {
                return false;
            }
            string subgrupo = servicio.BuscarProducto(numero)?.SubGrupo
                ?? (producto == null ? linea.SubgrupoProducto : null);
            return string.Equals(subgrupo?.Trim(), Constantes.Productos.SUBGRUPO_MUESTRAS, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// NestoAPI#649: como <see cref="AlmacenDelStock(PedidoVentaDTO, LineaPedidoVentaDTO)"/>, pero una
        /// muestra de un pedido por ruta desde Algete solo cuenta con el stock de Algete.
        /// <paramref name="producto"/>: el regalo, si no es el de la línea (las sugerencias usan otra línea de modelo).
        /// </summary>
        internal static string AlmacenDelStock(PedidoVentaDTO pedido, LineaPedidoVentaDTO linea, IServicioPrecios servicio, string producto = null)
        {
            return EsMuestraQueSalePorRutaDesdeAlgete(pedido, linea, servicio, producto)
                ? Constantes.Almacenes.ALGETE
                : AlmacenDelStock(pedido, linea);
        }

        /// <summary>
        /// NestoAPI#528: de qué almacén tiene que salir el regalo. Solo en «Según vaya entrando» (o, sin
        /// modo, sin servir junto) el regalo sale con lo que haya en SU almacén: si ahí no está, se queda
        /// pendiente o sale él solo. En los demás modos el pedido espera a las reposiciones de las
        /// tiendas, así que vale el stock de todas las sedes (null). Mismo criterio que
        /// ProductosBonificables (servirJunto) y que la validación de #491 al cambiar de modo.
        /// </summary>
        internal static string AlmacenDelStock(PedidoVentaDTO pedido, LineaPedidoVentaDTO linea)
        {
            bool soloSuAlmacen = pedido.modoServicio.HasValue
                ? pedido.modoServicio.Value == Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO
                : !pedido.servirJunto;
            return soloSuAlmacen && !string.IsNullOrWhiteSpace(linea.almacen) ? linea.almacen.Trim() : null;
        }

        internal static bool EsRegalo(LineaPedidoVentaDTO linea)
        {
            return linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                && !string.IsNullOrWhiteSpace(linea.Producto)
                && linea.Cantidad > 0
                && linea.BaseImponible == 0
                && (linea.oferta == null || linea.oferta == 0);
        }

        /// <summary>
        /// Las unidades que entran ahora: toda la línea si es nueva, si cambia de producto o si viene
        /// de un presupuesto (#528); si ya estaba guardada, solo lo que sube la cantidad.
        /// </summary>
        internal static int UnidadesNuevas(LineaPedidoVentaDTO linea)
        {
            if (linea.EsLineaNueva || linea.CambioProducto || linea.VieneDePresupuesto)
            {
                return linea.Cantidad;
            }
            return linea.CantidadAnterior.HasValue ? Math.Max(0, linea.Cantidad - linea.CantidadAnterior.Value) : 0;
        }
    }

    /// <summary>NestoAPI#528: regalos de un producto que no caben en el stock de donde tienen que salir.</summary>
    internal class FaltaDeStockRegalo
    {
        public string Producto { get; set; }
        /// <summary>NestoAPI#649: para el mensaje de las muestras (puede ser null).</summary>
        public string Nombre { get; set; }
        /// <summary>Null = todas las sedes.</summary>
        public string Almacen { get; set; }
        public int UnidadesNuevas { get; set; }
        public int Disponible { get; set; }
        /// <summary>NestoAPI#649: muestra de un pedido que sale por ruta desde Algete.</summary>
        public bool EsMuestraPorRuta { get; set; }
    }
}
