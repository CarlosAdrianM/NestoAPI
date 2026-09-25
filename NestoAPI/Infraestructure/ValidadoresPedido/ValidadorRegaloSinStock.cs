using NestoAPI.Models;
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
            FaltaDeStockRegalo falta = PrimeraFaltaDeStock(pedido, servicio, l => true);
            if (falta == null)
            {
                return respuesta;
            }

            respuesta.ValidacionSuperada = false;
            respuesta.AutorizadaDenegadaExpresamente = true;
            respuesta.ProductoId = falta.Producto;
            string donde = falta.Almacen == null ? string.Empty : $" en {falta.Almacen}";
            respuesta.Motivo = falta.Disponible <= 0
                ? $"No se puede regalar el producto {falta.Producto}: no hay stock{donde}. Elige otro regalo."
                : $"No se pueden regalar {falta.UnidadesNuevas} unidades del producto {falta.Producto}: solo hay {falta.Disponible} disponibles{donde}.";
            return respuesta;
        }

        /// <summary>
        /// NestoAPI#528: núcleo común del stock de los regalos (este validador, ValidadorGanavisiones y
        /// las sugerencias). Primer grupo de regalos (producto + almacén del que se tienen que servir)
        /// cuyas unidades nuevas no caben en el stock; null si caben todos.
        /// </summary>
        internal static FaltaDeStockRegalo PrimeraFaltaDeStock(PedidoVentaDTO pedido, IServicioPrecios servicio, Func<LineaPedidoVentaDTO, bool> filtro)
        {
            if (pedido?.Lineas == null || servicio == null)
            {
                return null;
            }

            var grupos = pedido.Lineas
                .Where(l => EsRegalo(l) && filtro(l))
                .GroupBy(l => new { Producto = l.Producto.Trim(), Almacen = AlmacenDelStock(pedido, l) });

            foreach (var regalos in grupos)
            {
                int unidadesNuevas = regalos.Sum(UnidadesNuevas);
                if (unidadesNuevas <= 0 || servicio.BuscarProducto(regalos.Key.Producto)?.Ficticio == true)
                {
                    // Un producto ficticio no lleva stock: exigírselo lo bloquearía siempre
                    continue;
                }

                int disponible = servicio.BuscarStockDisponibleParaRegalar(regalos.Key.Producto, regalos.Key.Almacen);
                if (unidadesNuevas > disponible)
                {
                    return new FaltaDeStockRegalo
                    {
                        Producto = regalos.Key.Producto,
                        Almacen = regalos.Key.Almacen,
                        UnidadesNuevas = unidadesNuevas,
                        Disponible = disponible
                    };
                }
            }

            return null;
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
        /// <summary>Null = todas las sedes.</summary>
        public string Almacen { get; set; }
        public int UnidadesNuevas { get; set; }
        public int Disponible { get; set; }
    }
}
