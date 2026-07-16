using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Nesto#397: invierte un <see cref="PedidoVentaDTO"/> a la forma de plantilla de venta,
    /// deshaciendo lo que codifica PlantillaVentaState al crear el pedido:
    ///
    /// - Producto normal: 1 línea con oferta = null.
    /// - Oferta (6+1, etc.): 2 líneas con el MISMO nº de oferta; la de pago es la de mayor precio
    ///   efectivo y la otra es la de oferta (gratis si va a base 0; "personalizada" tipo 2ª unidad
    ///   al 50 % —Nesto#371— si va con precio/descuento propios).
    /// - Regalo Ganavisiones: oferta = null y EsBonificadoGanavisiones (confirmado contra la tabla
    ///   Ganavision por el GET, #279). NUNCA por el texto '(BONIFICADO)': se trunca a 50 caracteres
    ///   y cada cliente pone un sufijo distinto.
    /// - Líneas a 0 € SIN flag (MMP, regalo por importe...): línea normal (round-trip-safe), NO regalo.
    /// - Se descartan las líneas de sistema (portes/reembolso: tipoLinea != PRODUCTO o EsFicticio).
    ///
    /// Lógica pura y compartida: Nesto y NestoApp consumen el resultado sin reimplementar nada.
    /// </summary>
    public static class ConvertidorPedidoAPlantilla
    {
        public static PedidoParaPlantillaDTO Convertir(PedidoVentaDTO pedido)
        {
            if (pedido == null)
            {
                return null;
            }

            var resultado = new PedidoParaPlantillaDTO
            {
                Empresa = pedido.empresa?.Trim(),
                Cliente = pedido.cliente?.Trim(),
                Contacto = pedido.contacto?.Trim(),
                NumeroPedido = pedido.numero,
                EsPresupuesto = pedido.EsPresupuesto,
                FormaPago = pedido.formaPago?.Trim(),
                PlazosPago = pedido.plazosPago?.Trim(),
                ComentarioPicking = pedido.comentarioPicking?.Trim(),
                Comentarios = pedido.comentarios?.Trim(),
                Ruta = pedido.ruta?.Trim(),
                ServirJunto = pedido.servirJunto,
                MantenerJunto = pedido.mantenerJunto
            };

            List<LineaPedidoVentaDTO> lineasProducto = (pedido.Lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => (l.tipoLinea ?? Constantes.TiposLineaVenta.PRODUCTO) == Constantes.TiposLineaVenta.PRODUCTO
                    && !l.EsFicticio)
                .ToList();

            // NestoAPI#303: las líneas ya en albarán/factura (estado >= 2) NO se cargan (no son
            // modificables); se cuenta cuántas quedan fuera para que el cliente avise. El PUT las
            // conserva sin tocar aunque no vengan en el payload.
            resultado.LineasEnAlbaranOFactura = lineasProducto.Count(l => l.estado >= Constantes.EstadosLineaVenta.ALBARAN);
            List<LineaPedidoVentaDTO> lineasReales = lineasProducto
                .Where(l => l.estado < Constantes.EstadosLineaVenta.ALBARAN)
                .ToList();

            if (lineasReales.Any())
            {
                resultado.FechaEntrega = lineasReales.Min(l => l.fechaEntrega);
                resultado.Almacen = lineasReales.First().almacen?.Trim();
            }

            // 1) Grupos de oferta: las líneas enlazadas por el mismo nº de oferta colapsan en una.
            foreach (var grupo in lineasReales
                .Where(l => l.oferta.HasValue && l.oferta.Value != 0)
                .GroupBy(l => l.oferta.Value))
            {
                resultado.Lineas.Add(ColapsarGrupoOferta(grupo.ToList()));
            }

            // 2) Resto de líneas (sin oferta): Ganavisiones a regalos, lo demás líneas normales.
            foreach (LineaPedidoVentaDTO linea in lineasReales.Where(l => !l.oferta.HasValue || l.oferta.Value == 0))
            {
                if (linea.EsBonificadoGanavisiones)
                {
                    resultado.Regalos.Add(new RegaloParaPlantillaDTO
                    {
                        Producto = linea.Producto?.Trim(),
                        Texto = linea.texto?.Trim(),
                        Cantidad = linea.Cantidad,
                        IdLinea = linea.id,
                        TienePicking = linea.picking != 0
                    });
                }
                else
                {
                    resultado.Lineas.Add(new LineaParaPlantillaDTO
                    {
                        Producto = linea.Producto?.Trim(),
                        Texto = linea.texto?.Trim(),
                        Cantidad = linea.Cantidad,
                        Precio = linea.PrecioUnitario,
                        Descuento = linea.DescuentoLinea,
                        AplicarDescuento = linea.AplicarDescuento,
                        IdLineaPago = linea.id,
                        PagoTienePicking = linea.picking != 0
                    });
                }
            }

            return resultado;
        }

        // Dentro del grupo, la línea de PAGO es la de mayor precio efectivo (precio·(1−dto)); las
        // demás son la parte de oferta: gratis si van a base 0, personalizada (precio/descuento
        // propios, Nesto#371) si van con base > 0. Caso borde: grupo de una sola línea a base 0
        // (solo quedó la parte regalada) → cantidad de pago 0.
        private static LineaParaPlantillaDTO ColapsarGrupoOferta(List<LineaPedidoVentaDTO> grupo)
        {
            LineaPedidoVentaDTO pago = grupo
                .OrderByDescending(l => l.PrecioUnitario * (1 - l.DescuentoLinea))
                .First();
            List<LineaPedidoVentaDTO> deOferta = grupo.Where(l => l != pago).ToList();

            bool pagoEsRegalo = deOferta.Count == 0 && pago.PrecioUnitario * (1 - pago.DescuentoLinea) == 0;
            var linea = new LineaParaPlantillaDTO
            {
                Producto = pago.Producto?.Trim(),
                Texto = pago.texto?.Trim(),
                Cantidad = pagoEsRegalo ? 0 : pago.Cantidad,
                CantidadOferta = pagoEsRegalo ? pago.Cantidad : deOferta.Sum(l => l.Cantidad),
                Precio = pagoEsRegalo ? 0 : pago.PrecioUnitario,
                Descuento = pagoEsRegalo ? 0 : pago.DescuentoLinea,
                AplicarDescuento = pago.AplicarDescuento,
                IdLineaPago = pagoEsRegalo ? 0 : pago.id,
                IdLineaOferta = pagoEsRegalo ? pago.id : deOferta.FirstOrDefault()?.id,
                PagoTienePicking = !pagoEsRegalo && pago.picking != 0,
                OfertaTienePicking = pagoEsRegalo ? pago.picking != 0 : deOferta.Any(l => l.picking != 0)
            };

            // Oferta personalizada (Nesto#371): la parte de oferta va con precio/descuento propios
            // (base > 0) en vez de gratis.
            LineaPedidoVentaDTO personalizada = deOferta
                .FirstOrDefault(l => l.PrecioUnitario * (1 - l.DescuentoLinea) != 0);
            if (personalizada != null)
            {
                linea.PersonalizarOferta = true;
                linea.PrecioOferta = personalizada.PrecioUnitario;
                linea.DescuentoOferta = personalizada.DescuentoLinea;
            }

            return linea;
        }
    }
}
