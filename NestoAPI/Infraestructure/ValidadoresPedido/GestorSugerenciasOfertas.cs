using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#457: una sugerencia de oferta sobre el pedido que se está montando. Pensada desde el
    /// principio para la tienda: accionable (producto y cantidades, no solo un texto), sin datos
    /// internos y con un texto que puede leer el cliente final.
    /// </summary>
    public class SugerenciaOfertaDTO
    {
        /// <summary>Tipo: <see cref="GestorSugerenciasOfertas.TIPO_OFERTA_NO_APLICADA"/> o <see cref="GestorSugerenciasOfertas.TIPO_AMPLIAR_CANTIDAD"/>.</summary>
        public string Tipo { get; set; }
        public string Producto { get; set; }
        /// <summary>Unidades cobradas que hay ahora en el pedido.</summary>
        public int CantidadActual { get; set; }
        /// <summary>Unidades cobradas que tiene que haber para que aplique (igual a la actual si ya aplica).</summary>
        public int CantidadSugerida { get; set; }
        /// <summary>Unidades de regalo que se llevaría con la cantidad sugerida.</summary>
        public int CantidadRegalo { get; set; }
        /// <summary>Reservado para las sugerencias por importe de pedido (0 en las de cantidad).</summary>
        public decimal ImporteQueFalta { get; set; }
        public string Texto { get; set; }
        /// <summary>Nº de orden de la oferta permitida que la sustenta.</summary>
        public int? Oferta { get; set; }
    }

    /// <summary>
    /// NestoAPI#457 (corte 1): qué ofertas de OfertasPermitidas (N+M por producto o familia) se podrían
    /// aplicar al pedido y no se están aplicando. Dos casos:
    ///   - OfertaNoAplicada: ya hay unidades cobradas de sobra para el N+M y no hay línea de regalo.
    ///   - AmpliarCantidad: faltan pocas unidades (como mucho la mitad del N) para llegar al N+M.
    /// Cada sugerencia se comprueba contra el MISMO circuito de validación del pedido con la línea de
    /// regalo puesta (<see cref="GestorPrecios.EsPedidoValido"/>): lo que el pedido rechazaría al
    /// guardar no se sugiere. Ofertas combinadas, escalonadas y regalo por importe: cortes siguientes.
    /// </summary>
    public static class GestorSugerenciasOfertas
    {
        public const string TIPO_OFERTA_NO_APLICADA = "OfertaNoAplicada";
        public const string TIPO_AMPLIAR_CANTIDAD = "AmpliarCantidad";

        public static List<SugerenciaOfertaDTO> Calcular(PedidoVentaDTO pedido, IServicioPrecios servicio, Func<PedidoVentaDTO, RespuestaValidacion> validar = null)
        {
            var sugerencias = new List<SugerenciaOfertaDTO>();
            if (pedido?.Lineas == null || servicio == null)
            {
                return sugerencias;
            }
            validar = validar ?? GestorPrecios.EsPedidoValido;

            IEnumerable<IGrouping<string, LineaPedidoVentaDTO>> porProducto = pedido.Lineas
                .Where(l => EsLineaDeProducto(l) && !string.IsNullOrWhiteSpace(l.Producto))
                .GroupBy(l => l.Producto.Trim());

            foreach (IGrouping<string, LineaPedidoVentaDTO> grupo in porProducto)
            {
                SugerenciaOfertaDTO sugerencia = SugerirParaProducto(grupo.Key, grupo.ToList(), pedido, servicio, validar);
                if (sugerencia != null)
                {
                    sugerencias.Add(sugerencia);
                }
            }
            return sugerencias;
        }

        private static SugerenciaOfertaDTO SugerirParaProducto(string numeroProducto, List<LineaPedidoVentaDTO> lineas, PedidoVentaDTO pedido,
            IServicioPrecios servicio, Func<PedidoVentaDTO, RespuestaValidacion> validar)
        {
            int cantidadCobrada = lineas.Where(l => l.Cantidad > 0 && l.BaseImponible > 0).Sum(l => l.Cantidad);
            int cantidadRegalada = lineas.Where(l => l.Cantidad > 0 && l.BaseImponible == 0).Sum(l => l.Cantidad);
            if (cantidadCobrada <= 0 || cantidadRegalada > 0)
            {
                // Sin unidades cobradas no hay oferta que sugerir; con regalo ya puesto, la oferta
                // está aplicada (si está mal aplicada, lo dirá la validación al guardar, no esto).
                return null;
            }

            Producto producto = servicio.BuscarProducto(numeroProducto);
            List<OfertaPermitida> ofertas = servicio.BuscarOfertasPermitidas(numeroProducto) ?? new List<OfertaPermitida>();
            List<OfertaPermitida> aplicables = ofertas
                .Where(o => !o.Denegar && o.CantidadConPrecio > 0 && o.CantidadRegalo > 0)
                .Where(o => (o.Cliente == null || o.Cliente.Trim() == pedido.cliente?.Trim())
                         && (o.Contacto == null || (o.Cliente != null && o.Contacto.Trim() == pedido.contacto?.Trim())))
                .Where(o => string.IsNullOrWhiteSpace(o.FiltroProducto)
                         || (producto?.Nombre != null && producto.Nombre.StartsWith(o.FiltroProducto.Trim(), StringComparison.OrdinalIgnoreCase)))
                .ToList();
            // Si hay ofertas expresas del producto mandan sobre las de familia (misma regla que el validador).
            List<OfertaPermitida> especificas = aplicables.Where(o => o.Número?.Trim() == numeroProducto).ToList();
            if (especificas.Any())
            {
                aplicables = especificas;
            }
            if (!aplicables.Any())
            {
                return null;
            }

            // 1. Ya cumple alguna: la que más regalo dé con lo que hay.
            SugerenciaOfertaDTO mejor = aplicables
                .Where(o => cantidadCobrada >= o.CantidadConPrecio)
                .Select(o => new SugerenciaOfertaDTO
                {
                    Tipo = TIPO_OFERTA_NO_APLICADA,
                    Producto = numeroProducto,
                    CantidadActual = cantidadCobrada,
                    CantidadSugerida = cantidadCobrada,
                    CantidadRegalo = o.CantidadRegalo * (cantidadCobrada / o.CantidadConPrecio),
                    Oferta = o.NºOrden,
                    Texto = $"El producto {numeroProducto} tiene un {o.CantidadConPrecio}+{o.CantidadRegalo} y no lo estás aplicando: " +
                            $"con {cantidadCobrada} unidades te corresponden {o.CantidadRegalo * (cantidadCobrada / o.CantidadConPrecio)} de regalo."
                })
                .OrderByDescending(s => s.CantidadRegalo)
                .FirstOrDefault();

            // 2. Si no, la más cercana: como mucho falta la mitad de las unidades del tramo.
            if (mejor == null)
            {
                mejor = aplicables
                    .Where(o => cantidadCobrada < o.CantidadConPrecio && cantidadCobrada * 2 >= o.CantidadConPrecio)
                    .Select(o => new SugerenciaOfertaDTO
                    {
                        Tipo = TIPO_AMPLIAR_CANTIDAD,
                        Producto = numeroProducto,
                        CantidadActual = cantidadCobrada,
                        CantidadSugerida = o.CantidadConPrecio,
                        CantidadRegalo = o.CantidadRegalo,
                        Oferta = o.NºOrden,
                        Texto = $"Con {o.CantidadConPrecio - cantidadCobrada} unidad{(o.CantidadConPrecio - cantidadCobrada == 1 ? string.Empty : "es")} " +
                                $"más del producto {numeroProducto} te llevas {o.CantidadRegalo} de regalo ({o.CantidadConPrecio}+{o.CantidadRegalo})."
                    })
                    .OrderBy(s => s.CantidadSugerida - s.CantidadActual)
                    .ThenByDescending(s => s.CantidadRegalo)
                    .FirstOrDefault();
            }
            if (mejor == null)
            {
                return null;
            }

            // 3. No sugerir lo que el pedido rechazaría al guardar.
            PedidoVentaDTO hipotetico = ConSugerenciaAplicada(pedido, lineas, mejor);
            RespuestaValidacion validacion = validar(hipotetico);
            return validacion != null && validacion.ValidacionSuperada ? mejor : null;
        }

        /// <summary>
        /// Copia del pedido con la sugerencia puesta: la primera línea cobrada del producto sube a la
        /// cantidad sugerida (misma proporción de base imponible) y se añade la línea de regalo a 0 €.
        /// Las líneas originales no se tocan.
        /// </summary>
        internal static PedidoVentaDTO ConSugerenciaAplicada(PedidoVentaDTO pedido, List<LineaPedidoVentaDTO> lineasProducto, SugerenciaOfertaDTO sugerencia)
        {
            LineaPedidoVentaDTO modelo = lineasProducto.First(l => l.Cantidad > 0 && l.BaseImponible > 0);
            var lineas = new List<LineaPedidoVentaDTO>();
            bool ampliada = false;
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                if (!ampliada && ReferenceEquals(linea, modelo) && sugerencia.CantidadSugerida > sugerencia.CantidadActual)
                {
                    int extra = sugerencia.CantidadSugerida - sugerencia.CantidadActual;
                    // BaseImponible se calcula sola (PrecioUnitario × Cantidad menos descuentos).
                    LineaPedidoVentaDTO copia = Copiar(linea);
                    copia.Cantidad = linea.Cantidad + extra;
                    lineas.Add(copia);
                    ampliada = true;
                }
                else
                {
                    lineas.Add(linea);
                }
            }
            // Línea de regalo: id 0 (= línea nueva) y precio 0 (= base imponible 0, que es lo que
            // MontarOfertaPedido lee como "unidades de oferta").
            LineaPedidoVentaDTO regalo = Copiar(modelo);
            regalo.id = 0;
            regalo.Cantidad = sugerencia.CantidadRegalo;
            regalo.PrecioUnitario = 0;
            regalo.DescuentoLinea = 0;
            regalo.DescuentoProducto = 0;
            regalo.oferta = sugerencia.CantidadRegalo;
            lineas.Add(regalo);

            return new PedidoVentaDTO
            {
                empresa = pedido.empresa,
                cliente = pedido.cliente,
                contacto = pedido.contacto,
                contactoCobro = pedido.contactoCobro,
                fecha = pedido.fecha,
                Lineas = lineas
            };
        }

        private static LineaPedidoVentaDTO Copiar(LineaPedidoVentaDTO l) => new LineaPedidoVentaDTO
        {
            id = l.id,
            almacen = l.almacen,
            delegacion = l.delegacion,
            estado = l.estado,
            Producto = l.Producto,
            texto = l.texto,
            Cantidad = l.Cantidad,
            PrecioUnitario = l.PrecioUnitario,
            DescuentoLinea = l.DescuentoLinea,
            DescuentoProducto = l.DescuentoProducto,
            DescuentoEntidad = l.DescuentoEntidad,
            AplicarDescuento = l.AplicarDescuento,
            oferta = l.oferta,
            tipoLinea = l.tipoLinea,
            formaVenta = l.formaVenta,
            iva = l.iva,
            fechaEntrega = l.fechaEntrega,
            GrupoProducto = l.GrupoProducto,
            SubgrupoProducto = l.SubgrupoProducto,
            precioTarifa = l.precioTarifa,
            usuario = l.usuario
        };

        private static bool EsLineaDeProducto(LineaPedidoVentaDTO l)
            => l != null && (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO);
    }
}
