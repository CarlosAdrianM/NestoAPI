using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#626: sugerencias de ofertas combinadas (OfertasCombinadas). Caso real: la 267 «Oferta
    /// mascarillas PDRN» (6+1 de la 45664 y de regalo una 45665). Dos casos:
    ///   - RegaloCombinadaNoAplicado (Accion = AnadirLinea): el pedido ya lleva la parte que se paga y le
    ///     faltan las líneas de regalo. Se propone añadirlas: producto, unidades y al 100 % de descuento.
    ///   - AmpliarCantidadCombinada (Accion = ModificarLinea): a la parte que se paga le faltan pocas
    ///     unidades de un solo producto (como mucho la mitad, la misma regla que el N+M).
    ///
    /// Qué es «regalo» en una combinada: una fila de producto a precio 0 cuyo producto NO va cobrado en el
    /// pedido. Las filas a precio 0 de productos que sí se cobran son la parte pagada (en la 267 las dos
    /// filas van a precio 0; lo que se paga lo fija el importe mínimo de 132 € = 6 × 22 €). Por eso un
    /// regalo nunca se propone sobre un producto que ya lleva unidades cobradas.
    ///
    /// Se valida con el mismo circuito que el guardado (<see cref="GestorPrecios.EsPedidoValido"/>) con los
    /// regalos puestos tal y como los manda la plantilla (precio 0 y número de oferta) y, además, la propia
    /// combinada tiene que autorizar cada regalo: si solo lo aceptara el 5 % de muestras y material
    /// promocional, no es la oferta y no se sugiere. Sin stock del regalo tampoco se sugiere.
    /// Fuera de este corte (no se sugieren): ofertas de «regalar menor importe», filas de filtro como
    /// regalo y filas de «permitir cantidad menor» (son extras opcionales).
    /// </summary>
    public static partial class GestorSugerenciasOfertas
    {
        public const string TIPO_REGALO_COMBINADA_NO_APLICADO = "RegaloCombinadaNoAplicado";
        public const string TIPO_AMPLIAR_CANTIDAD_COMBINADA = "AmpliarCantidadCombinada";

        public const string ACCION_MODIFICAR_LINEA = "ModificarLinea";
        public const string ACCION_ANADIR_LINEA = "AnadirLinea";

        /// <summary>Misma tolerancia por instancia que el importe mínimo de <see cref="ValidadorOfertasCombinadas"/>.</summary>
        private const decimal TOLERANCIA_IMPORTE_MINIMO = 0.005M;

        internal static List<SugerenciaOfertaDTO> SugerirCombinadas(PedidoVentaDTO pedido, IServicioPrecios servicio, Func<PedidoVentaDTO, RespuestaValidacion> validar)
        {
            var sugerencias = new List<SugerenciaOfertaDTO>();
            var productosCobrados = new HashSet<string>(pedido.Lineas
                .Where(EsLineaCobradaCombinada)
                .Select(l => l.Producto.Trim()));
            if (!productosCobrados.Any())
            {
                return sugerencias;
            }

            var ofertas = new Dictionary<int, OfertaCombinada>();
            foreach (string producto in productosCobrados)
            {
                foreach (OfertaCombinada oferta in servicio.BuscarOfertasCombinadas(producto) ?? new List<OfertaCombinada>())
                {
                    if (oferta?.OfertasCombinadasDetalles != null && !ofertas.ContainsKey(oferta.Id))
                    {
                        ofertas[oferta.Id] = oferta;
                    }
                }
            }

            foreach (OfertaCombinada oferta in ofertas.Values.OrderBy(o => o.Id))
            {
                if (!string.IsNullOrWhiteSpace(oferta.Empresa) && !string.IsNullOrWhiteSpace(pedido.empresa)
                    && oferta.Empresa.Trim() != pedido.empresa.Trim())
                {
                    continue;
                }
                sugerencias.AddRange(SugerirParaCombinada(oferta, pedido, productosCobrados, servicio, validar));
            }
            return sugerencias;
        }

        /// <summary>Una línea de regalo que le falta al pedido para cumplir la combinada.</summary>
        internal sealed class RegaloCombinadaQueFalta
        {
            public string Producto { get; set; }
            public int Cantidad { get; set; }
            /// <summary>Solo en grupos de alternativas: las referencias que valen y tienen stock.</summary>
            public List<string> Alternativas { get; set; }
        }

        private static List<SugerenciaOfertaDTO> SugerirParaCombinada(OfertaCombinada oferta, PedidoVentaDTO pedido, HashSet<string> productosCobrados,
            IServicioPrecios servicio, Func<PedidoVentaDTO, RespuestaValidacion> validar)
        {
            var ninguna = new List<SugerenciaOfertaDTO>();
            if (oferta.RegalarMenorImporte)
            {
                return ninguna; // el regalo es una de las unidades que ya se piden, no una línea que añadir
            }
            List<OfertaCombinadaDetalle> detalles = oferta.OfertasCombinadasDetalles
                .Where(d => d != null && d.Cantidad > 0 && !string.IsNullOrWhiteSpace(d.Producto))
                .ToList();
            List<OfertaCombinadaDetalle> obligatorias = detalles
                .Where(d => d.GrupoAlternativa == null && !d.PermitirCantidadMenor)
                .ToList();
            List<OfertaCombinadaDetalle> pagadas = obligatorias
                .Where(d => d.Precio > 0 || productosCobrados.Contains(d.Producto.Trim()))
                .ToList();
            List<OfertaCombinadaDetalle> regalosSueltos = obligatorias.Except(pagadas).ToList();
            List<List<OfertaCombinadaDetalle>> gruposRegalo = detalles
                .Where(d => d.GrupoAlternativa.HasValue)
                .GroupBy(d => d.GrupoAlternativa.Value)
                .Where(g => g.All(d => d.Precio == 0 && !productosCobrados.Contains(d.Producto.Trim())))
                .Select(g => g.ToList())
                .ToList();
            if (!pagadas.Any() || (!regalosSueltos.Any() && !gruposRegalo.Any()))
            {
                return ninguna;
            }

            LineaPedidoVentaDTO modelo = pedido.Lineas.FirstOrDefault(l => EsLineaCobradaCombinada(l) && pagadas.Any(d => d.Producto.Trim() == l.Producto.Trim()));
            if (modelo == null)
            {
                return ninguna;
            }
            var productosOferta = new HashSet<string>(detalles.Select(d => d.Producto.Trim()));
            decimal importeOferta = pedido.Lineas
                .Where(l => EsLineaDeProducto(l) && l.Producto != null && productosOferta.Contains(l.Producto.Trim()))
                .Sum(l => l.BaseImponible);

            int instancias = pagadas.Min(d => UnidadesDelDetalle(pedido, d) / d.Cantidad);
            if (oferta.ImporteMinimo > 0 && instancias > 0)
            {
                int porImporte = (int)Math.Floor((importeOferta + TOLERANCIA_IMPORTE_MINIMO * (instancias + 1)) / oferta.ImporteMinimo);
                instancias = Math.Min(instancias, porImporte);
            }

            if (instancias <= 0)
            {
                SugerenciaOfertaDTO ampliar = SugerirAmpliarCombinada(oferta, pedido, pagadas, regalosSueltos, gruposRegalo, importeOferta, modelo, servicio);
                return ampliar == null ? ninguna : new List<SugerenciaOfertaDTO> { ampliar };
            }

            // De la más generosa a una sola instancia: la primera que el pedido aceptaría.
            for (int n = instancias; n >= 1; n--)
            {
                List<RegaloCombinadaQueFalta> faltan = RegalosQueFaltan(pedido, regalosSueltos, gruposRegalo, n, modelo, servicio, out bool sinStock);
                if (sinStock)
                {
                    continue;
                }
                if (!faltan.Any())
                {
                    return ninguna; // ya lleva los regalos de esas instancias: la oferta está aplicada
                }
                PedidoVentaDTO hipotetico = ConRegalosCombinada(pedido, modelo, faltan, servicio);
                if (!RegalosAceptados(hipotetico, faltan, servicio, validar))
                {
                    continue;
                }
                return faltan.Select(f => new SugerenciaOfertaDTO
                {
                    Tipo = TIPO_REGALO_COMBINADA_NO_APLICADO,
                    Accion = ACCION_ANADIR_LINEA,
                    Producto = f.Producto,
                    CantidadActual = 0,
                    CantidadSugerida = 0,
                    CantidadRegalo = f.Cantidad,
                    Descuento = 1M,
                    OfertaCombinada = oferta.Id,
                    Alternativas = f.Alternativas,
                    Texto = $"El pedido cumple la oferta «{oferta.Nombre?.Trim()}»: le corresponde{(f.Cantidad == 1 ? string.Empty : "n")} " +
                            $"{DescripcionRegalo(f, servicio)} de regalo y no se está aplicando."
                }).ToList();
            }
            return ninguna;
        }

        /// <summary>
        /// «Ampliar»: un solo producto de la parte pagada se queda corto, ya hay al menos la mitad de las
        /// unidades de la parte pagada, el importe mínimo se alcanzaría al precio que ya lleva la línea y
        /// hay stock de los regalos. No se valida (el pedido aún no llega); cuando llegue, saldrá el regalo.
        /// </summary>
        private static SugerenciaOfertaDTO SugerirAmpliarCombinada(OfertaCombinada oferta, PedidoVentaDTO pedido, List<OfertaCombinadaDetalle> pagadas,
            List<OfertaCombinadaDetalle> regalosSueltos, List<List<OfertaCombinadaDetalle>> gruposRegalo, decimal importeOferta,
            LineaPedidoVentaDTO modelo, IServicioPrecios servicio)
        {
            var porDetalle = pagadas.Select(d => new { Detalle = d, Unidades = UnidadesDelDetalle(pedido, d) }).ToList();
            if (porDetalle.Any(x => x.Unidades <= 0))
            {
                return null; // falta un producto entero de la parte pagada: eso no es «ampliar»
            }
            var cortos = porDetalle.Where(x => x.Unidades < x.Detalle.Cantidad).ToList();
            if (cortos.Count != 1)
            {
                return null; // con nada corto el que falla es el importe; con varios, no hay una sola línea que tocar
            }
            int requeridas = pagadas.Sum(d => (int)d.Cantidad);
            int presentes = porDetalle.Sum(x => Math.Min(x.Unidades, (int)x.Detalle.Cantidad));
            if (presentes * 2 < requeridas)
            {
                return null;
            }

            string producto = cortos[0].Detalle.Producto.Trim();
            int falta = cortos[0].Detalle.Cantidad - cortos[0].Unidades;
            if (oferta.ImporteMinimo > 0)
            {
                List<LineaPedidoVentaDTO> cobradasProducto = pedido.Lineas.Where(l => EsLineaCobradaCombinada(l) && l.Producto.Trim() == producto).ToList();
                int unidadesCobradas = cobradasProducto.Sum(l => l.Cantidad);
                decimal precioNeto = unidadesCobradas > 0 ? cobradasProducto.Sum(l => l.BaseImponible) / unidadesCobradas : 0;
                if (importeOferta + falta * precioNeto < oferta.ImporteMinimo - TOLERANCIA_IMPORTE_MINIMO * 2)
                {
                    return null; // al precio que lleva no llegaría al mínimo: se prometería un regalo que no entra
                }
            }

            List<RegaloCombinadaQueFalta> regalos = RegalosQueFaltan(pedido, regalosSueltos, gruposRegalo, 1, modelo, servicio, out bool sinStock);
            if (sinStock || !regalos.Any())
            {
                return null;
            }
            int unidadesRegalo = regalos.Sum(r => r.Cantidad);
            return new SugerenciaOfertaDTO
            {
                Tipo = TIPO_AMPLIAR_CANTIDAD_COMBINADA,
                Accion = ACCION_MODIFICAR_LINEA,
                Producto = producto,
                CantidadActual = cortos[0].Unidades,
                CantidadSugerida = cortos[0].Detalle.Cantidad,
                CantidadRegalo = unidadesRegalo,
                OfertaCombinada = oferta.Id,
                Texto = $"Con {falta} unidad{(falta == 1 ? string.Empty : "es")} más del producto {producto} completa la oferta " +
                        $"«{oferta.Nombre?.Trim()}» y se lleva de regalo {string.Join(" y ", regalos.Select(r => DescripcionRegalo(r, servicio)))}."
            };
        }

        /// <summary>
        /// Regalos que faltan para <paramref name="instancias"/> instancias de la oferta. Una fila suelta
        /// sin stock deja la oferta sin sugerir (<paramref name="sinStock"/>); en un grupo de alternativas
        /// valen las que tengan stock, por el orden de la oferta.
        /// </summary>
        private static List<RegaloCombinadaQueFalta> RegalosQueFaltan(PedidoVentaDTO pedido, List<OfertaCombinadaDetalle> regalosSueltos,
            List<List<OfertaCombinadaDetalle>> gruposRegalo, int instancias, LineaPedidoVentaDTO modelo, IServicioPrecios servicio, out bool sinStock)
        {
            sinStock = false;
            var faltan = new List<RegaloCombinadaQueFalta>();
            foreach (IGrouping<string, OfertaCombinadaDetalle> porProducto in regalosSueltos.GroupBy(d => d.Producto.Trim()))
            {
                int falta = porProducto.Sum(d => (int)d.Cantidad) * instancias - UnidadesDelProducto(pedido, porProducto.Key);
                if (falta <= 0)
                {
                    continue;
                }
                if (!HayStockParaRegalar(servicio, pedido, modelo, porProducto.Key, falta))
                {
                    sinStock = true;
                    return faltan;
                }
                faltan.Add(new RegaloCombinadaQueFalta { Producto = porProducto.Key, Cantidad = falta });
            }
            foreach (List<OfertaCombinadaDetalle> grupo in gruposRegalo)
            {
                List<string> referencias = grupo.Select(d => d.Producto.Trim()).Distinct().ToList();
                int falta = grupo.Max(d => (int)d.Cantidad) * instancias - referencias.Sum(p => UnidadesDelProducto(pedido, p));
                if (falta <= 0)
                {
                    continue;
                }
                List<string> conStock = referencias.Where(p => HayStockParaRegalar(servicio, pedido, modelo, p, falta)).ToList();
                if (!conStock.Any())
                {
                    sinStock = true;
                    return faltan;
                }
                faltan.Add(new RegaloCombinadaQueFalta
                {
                    Producto = conStock[0],
                    Cantidad = falta,
                    Alternativas = conStock.Count > 1 ? conStock : null
                });
            }
            return faltan;
        }

        /// <summary>
        /// El pedido con los regalos puestos lo acepta el circuito de validación Y cada regalo lo autoriza
        /// una oferta combinada (no el 5 % de muestras y material promocional, que también daría el pedido
        /// por bueno pero no es la oferta).
        /// </summary>
        private static bool RegalosAceptados(PedidoVentaDTO hipotetico, List<RegaloCombinadaQueFalta> faltan, IServicioPrecios servicio, Func<PedidoVentaDTO, RespuestaValidacion> validar)
        {
            RespuestaValidacion validacion = validar(hipotetico);
            if (validacion == null || !validacion.ValidacionSuperada)
            {
                return false;
            }
            var validadorCombinadas = new ValidadorOfertasCombinadas();
            return faltan.All(f => validadorCombinadas.EsPedidoValido(hipotetico, f.Producto, servicio)?.ValidacionSuperada == true);
        }

        /// <summary>
        /// Copia del pedido con los regalos al final, como los manda la plantilla de Nesto (unidades de
        /// oferta: precio 0, sin descuentos y con su número de oferta). Almacén, delegación, forma de venta,
        /// IVA, estado y fecha de la primera línea cobrada de la oferta. Las líneas originales no se tocan.
        /// </summary>
        internal static PedidoVentaDTO ConRegalosCombinada(PedidoVentaDTO pedido, LineaPedidoVentaDTO modelo, List<RegaloCombinadaQueFalta> regalos, IServicioPrecios servicio)
        {
            int siguienteOferta = (pedido.Lineas.Max(l => l?.oferta) ?? 0) + 1;
            var lineas = pedido.Lineas.ToList();
            foreach (RegaloCombinadaQueFalta r in regalos)
            {
                Producto producto = servicio.BuscarProducto(r.Producto);
                LineaPedidoVentaDTO regalo = Copiar(modelo);
                regalo.id = 0;
                regalo.Producto = r.Producto;
                regalo.texto = producto?.Nombre;
                regalo.Cantidad = r.Cantidad;
                regalo.PrecioUnitario = 0;
                regalo.DescuentoLinea = 0;
                regalo.DescuentoProducto = 0;
                regalo.DescuentoEntidad = 0;
                regalo.AplicarDescuento = false;
                regalo.oferta = siguienteOferta++;
                regalo.GrupoProducto = producto?.Grupo;
                regalo.SubgrupoProducto = producto?.SubGrupo;
                regalo.precioTarifa = producto?.PVP ?? 0;
                lineas.Add(regalo);
            }
            return new PedidoVentaDTO
            {
                empresa = pedido.empresa,
                cliente = pedido.cliente,
                contacto = pedido.contacto,
                contactoCobro = pedido.contactoCobro,
                fecha = pedido.fecha,
                servirJunto = pedido.servirJunto,
                modoServicio = pedido.modoServicio,
                Lineas = lineas
            };
        }

        private static string DescripcionRegalo(RegaloCombinadaQueFalta r, IServicioPrecios servicio)
        {
            string unidades = $"{r.Cantidad} unidad{(r.Cantidad == 1 ? string.Empty : "es")}";
            if (r.Alternativas != null && r.Alternativas.Count > 1)
            {
                return $"{unidades} a elegir entre los productos {string.Join(", ", r.Alternativas)}";
            }
            string nombre = servicio.BuscarProducto(r.Producto)?.Nombre?.Trim();
            return $"{unidades} del producto {r.Producto}" + (string.IsNullOrEmpty(nombre) ? string.Empty : $" ({nombre})");
        }

        /// <summary>
        /// Unidades del pedido que cuentan para la fila: todas las del producto a un precio igual o mayor que
        /// el de la fila, también las gratis (en la 267 las 7 de la 45664 son 6 cobradas + la del 6+1).
        /// Mismo criterio que el validador.
        /// </summary>
        private static int UnidadesDelDetalle(PedidoVentaDTO pedido, OfertaCombinadaDetalle d)
            => pedido.Lineas
                .Where(l => EsLineaDeProducto(l) && l.Producto != null && l.Producto.Trim() == d.Producto.Trim() && l.PrecioUnitario >= d.Precio)
                .Sum(l => l.Cantidad);

        private static int UnidadesDelProducto(PedidoVentaDTO pedido, string producto)
            => pedido.Lineas
                .Where(l => EsLineaDeProducto(l) && l.Producto != null && l.Producto.Trim() == producto)
                .Sum(l => l.Cantidad);

        private static bool EsLineaCobradaCombinada(LineaPedidoVentaDTO l)
            => EsLineaDeProducto(l) && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0 && l.BaseImponible > 0;
    }
}
