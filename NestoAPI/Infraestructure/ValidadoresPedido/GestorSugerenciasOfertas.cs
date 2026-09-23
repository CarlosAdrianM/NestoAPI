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
        /// <summary>Tipo: una de las constantes TIPO_* de <see cref="GestorSugerenciasOfertas"/>.</summary>
        public string Tipo { get; set; }
        public string Producto { get; set; }
        /// <summary>Unidades cobradas que hay ahora en el pedido.</summary>
        public int CantidadActual { get; set; }
        /// <summary>Unidades cobradas que tiene que haber para que aplique (igual a la actual si ya aplica).</summary>
        public int CantidadSugerida { get; set; }
        /// <summary>Unidades de regalo que se llevaría con la cantidad sugerida.</summary>
        public int CantidadRegalo { get; set; }
        /// <summary>Lo que falta para llegar al importe del regalo (0 en las de cantidad y si ya se llega).</summary>
        public decimal ImporteQueFalta { get; set; }
        /// <summary>Importe de pedido a partir del cual hay regalo (0 en las de cantidad).</summary>
        public decimal ImportePedido { get; set; }
        public string Texto { get; set; }
        /// <summary>Nº de orden de la oferta permitida que la sustenta.</summary>
        public int? Oferta { get; set; }
        /// <summary>Corte 3: descuento en tanto por uno al que da derecho el tramo (0 en las demás).</summary>
        public decimal Descuento { get; set; }
        /// <summary>Corte 3: Id de la oferta escalonada que la sustenta.</summary>
        public int? OfertaEscalonada { get; set; }
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
        // Corte 2: regalo por importe de pedido (RegalosImportePedido).
        public const string TIPO_REGALO_NO_APLICADO = "RegaloNoAplicado";
        public const string TIPO_AMPLIAR_IMPORTE = "AmpliarImporte";
        // Corte 3: ofertas escalonadas (descuento por volumen sobre una lista de referencias, #226).
        public const string TIPO_DESCUENTO_NO_APLICADO = "DescuentoNoAplicado";
        public const string TIPO_AMPLIAR_CANTIDAD_ESCALONADA = "AmpliarCantidadEscalonada";

        /// <summary>Misma tolerancia que <see cref="ValidadorOfertasEscalonadas"/>: medio céntimo por redondeo.</summary>
        private const decimal TOLERANCIA_REDONDEO = 0.005M;

        /// <summary>
        /// Corte 2: se sugiere ampliar el pedido para llegar al regalo solo si falta como mucho esta
        /// fracción del importe (0,25 = un 25 %: con 150 € de un regalo a 200 € sí, con 100 € no).
        /// Criterio de partida, a ajustar al verlo en la plantilla.
        /// </summary>
        public const decimal FRACCION_CERCANIA_IMPORTE = 0.25M;
        private static readonly System.Globalization.CultureInfo CASTELLANO = new System.Globalization.CultureInfo("es-ES");

        public static List<SugerenciaOfertaDTO> Calcular(PedidoVentaDTO pedido, IServicioPrecios servicio, Func<PedidoVentaDTO, RespuestaValidacion> validar = null)
        {
            var sugerencias = new List<SugerenciaOfertaDTO>();
            if (pedido?.Lineas == null || servicio == null)
            {
                return sugerencias;
            }
            // NestoAPI#517: UNA caché de lecturas para toda la petición, compartida con las validaciones que
            // se lanzan por cada candidata. Sin ella cada validación releía productos y ofertas de BD: el
            // 23/09/26 fueron ~50.000 lecturas de Productos en 90 s (RDS2016 al 100 %).
            servicio = ServicioPreciosCacheado.Envolver(servicio);
            IServicioPrecios servicioPeticion = servicio;
            validar = validar ?? (p => GestorPrecios.EsPedidoValido(p, servicioPeticion));

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
            sugerencias.AddRange(SugerirRegalosPorImporte(pedido, servicio, validar));
            sugerencias.AddRange(SugerirEscalonadas(pedido, servicio, validar));
            return sugerencias;
        }

        /// <summary>
        /// Corte 3: ofertas escalonadas (#226). Las unidades cobradas de todas las referencias de la
        /// oferta se suman; los tramos son «cantidad mínima o más». Dos avisos por oferta:
        ///   - DescuentoNoAplicado (uno por producto afectado): el pedido ya alcanza un tramo y alguna
        ///     línea paga más que el suelo de ese tramo (PrecioBase × (1 − dto)). Se valida con el
        ///     descuento puesto en esas líneas; lo que el pedido rechazaría no se sugiere.
        ///   - AmpliarCantidadEscalonada: al siguiente tramo le falta como mucho la mitad de sus
        ///     unidades. Se propone añadirlas al producto de la oferta con más unidades en el pedido.
        ///     No se valida: la cantidad aún no existe; cuando se llegue saldrá como DescuentoNoAplicado.
        /// Las líneas regaladas (0 €) no cuentan como unidades, igual que en el validador.
        /// </summary>
        internal static List<SugerenciaOfertaDTO> SugerirEscalonadas(PedidoVentaDTO pedido, IServicioPrecios servicio, Func<PedidoVentaDTO, RespuestaValidacion> validar)
        {
            var sugerencias = new List<SugerenciaOfertaDTO>();
            List<string> productosPedido = pedido.Lineas
                .Where(l => EsLineaCobrada(l))
                .Select(l => l.Producto.Trim())
                .Distinct()
                .ToList();
            if (!productosPedido.Any())
            {
                return sugerencias;
            }

            var ofertas = new Dictionary<int, OfertaEscalonada>();
            foreach (string producto in productosPedido)
            {
                foreach (OfertaEscalonada oferta in servicio.BuscarOfertasEscalonadas(producto) ?? new List<OfertaEscalonada>())
                {
                    if (oferta != null && !ofertas.ContainsKey(oferta.Id))
                    {
                        ofertas[oferta.Id] = oferta;
                    }
                }
            }

            foreach (OfertaEscalonada oferta in ofertas.Values)
            {
                if (oferta.OfertasEscalonadasProductos == null || oferta.OfertasEscalonadasTramos == null || !oferta.OfertasEscalonadasTramos.Any())
                {
                    continue;
                }
                Dictionary<string, decimal> precioBase = oferta.OfertasEscalonadasProductos
                    .Where(p => !string.IsNullOrWhiteSpace(p.Producto))
                    .GroupBy(p => p.Producto.Trim())
                    .ToDictionary(g => g.Key, g => g.First().PrecioBase);
                List<LineaPedidoVentaDTO> lineasOferta = pedido.Lineas
                    .Where(l => EsLineaCobrada(l) && precioBase.ContainsKey(l.Producto.Trim()))
                    .ToList();
                int unidades = lineasOferta.Sum(l => l.Cantidad);
                if (unidades <= 0)
                {
                    continue;
                }

                List<OfertaEscalonadaTramo> tramos = oferta.OfertasEscalonadasTramos.OrderBy(t => t.CantidadMinima).ToList();
                OfertaEscalonadaTramo alcanzado = tramos.LastOrDefault(t => t.CantidadMinima <= unidades);
                OfertaEscalonadaTramo siguiente = tramos.FirstOrDefault(t => t.CantidadMinima > unidades);

                if (alcanzado != null)
                {
                    sugerencias.AddRange(SugerirDescuentoNoAplicado(pedido, oferta, alcanzado, lineasOferta, precioBase, unidades, validar));
                }
                if (siguiente != null && unidades * 2 >= siguiente.CantidadMinima)
                {
                    int falta = siguiente.CantidadMinima - unidades;
                    string productoSugerido = lineasOferta
                        .GroupBy(l => l.Producto.Trim())
                        .OrderByDescending(g => g.Sum(l => l.Cantidad))
                        .First().Key;
                    int actualDelProducto = lineasOferta.Where(l => l.Producto.Trim() == productoSugerido).Sum(l => l.Cantidad);
                    sugerencias.Add(new SugerenciaOfertaDTO
                    {
                        Tipo = TIPO_AMPLIAR_CANTIDAD_ESCALONADA,
                        Producto = productoSugerido,
                        CantidadActual = actualDelProducto,
                        CantidadSugerida = actualDelProducto + falta,
                        Descuento = siguiente.Descuento,
                        OfertaEscalonada = oferta.Id,
                        Texto = $"Con {falta} unidad{(falta == 1 ? string.Empty : "es")} más de la oferta «{oferta.Nombre?.Trim()}» " +
                                $"(por ejemplo del producto {productoSugerido}) llegas a {siguiente.CantidadMinima} y pasas al " +
                                $"{Porcentaje(siguiente.Descuento)} de descuento en todas sus referencias" +
                                (alcanzado != null ? $" (ahora tienes el {Porcentaje(alcanzado.Descuento)})." : ".")
                    });
                }
            }
            return sugerencias;
        }

        private static List<SugerenciaOfertaDTO> SugerirDescuentoNoAplicado(PedidoVentaDTO pedido, OfertaEscalonada oferta, OfertaEscalonadaTramo tramo,
            List<LineaPedidoVentaDTO> lineasOferta, Dictionary<string, decimal> precioBase, int unidades, Func<PedidoVentaDTO, RespuestaValidacion> validar)
        {
            var sugerencias = new List<SugerenciaOfertaDTO>();
            // Líneas que pagan MÁS que el suelo del tramo: tienen derecho a más descuento del que llevan.
            List<LineaPedidoVentaDTO> pagandoDeMas = lineasOferta
                .Where(l => PrecioNeto(l) > precioBase[l.Producto.Trim()] * (1 - tramo.Descuento) + TOLERANCIA_REDONDEO)
                .ToList();
            if (!pagandoDeMas.Any())
            {
                return sugerencias;
            }

            PedidoVentaDTO hipotetico = ConDescuentoEscalonado(pedido, pagandoDeMas, precioBase, tramo.Descuento);
            RespuestaValidacion validacion = validar(hipotetico);
            if (validacion == null || !validacion.ValidacionSuperada)
            {
                return sugerencias;
            }

            foreach (IGrouping<string, LineaPedidoVentaDTO> porProducto in pagandoDeMas.GroupBy(l => l.Producto.Trim()))
            {
                int cantidad = lineasOferta.Where(l => l.Producto.Trim() == porProducto.Key).Sum(l => l.Cantidad);
                sugerencias.Add(new SugerenciaOfertaDTO
                {
                    Tipo = TIPO_DESCUENTO_NO_APLICADO,
                    Producto = porProducto.Key,
                    CantidadActual = cantidad,
                    CantidadSugerida = cantidad,
                    Descuento = tramo.Descuento,
                    OfertaEscalonada = oferta.Id,
                    Texto = $"Con {unidades} unidades de la oferta «{oferta.Nombre?.Trim()}» te corresponde un {Porcentaje(tramo.Descuento)} " +
                            $"de descuento en el producto {porProducto.Key} y no lo estás aplicando."
                });
            }
            return sugerencias;
        }

        /// <summary>
        /// Copia del pedido con el descuento del tramo puesto en las líneas indicadas: precio base de la
        /// oferta y el descuento del tramo como descuento de línea (sin otros descuentos), que es justo el
        /// suelo que exige <see cref="ValidadorOfertasEscalonadas"/>. Las demás líneas van tal cual.
        /// </summary>
        internal static PedidoVentaDTO ConDescuentoEscalonado(PedidoVentaDTO pedido, List<LineaPedidoVentaDTO> lineasARebajar, Dictionary<string, decimal> precioBase, decimal descuento)
        {
            var lineas = new List<LineaPedidoVentaDTO>();
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                if (lineasARebajar.Any(r => ReferenceEquals(r, linea)))
                {
                    LineaPedidoVentaDTO copia = Copiar(linea);
                    copia.PrecioUnitario = precioBase[linea.Producto.Trim()];
                    copia.AplicarDescuento = true;
                    copia.DescuentoEntidad = 0;
                    copia.DescuentoProducto = 0;
                    copia.DescuentoLinea = descuento;
                    lineas.Add(copia);
                }
                else
                {
                    lineas.Add(linea);
                }
            }
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

        private static bool EsLineaCobrada(LineaPedidoVentaDTO l)
            => EsLineaDeProducto(l) && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0 && l.PrecioUnitario > 0;

        private static decimal PrecioNeto(LineaPedidoVentaDTO l) => l.PrecioUnitario * (1 - l.SumaDescuentosSinPP);

        private static string Porcentaje(decimal tantoPorUno) => (tantoPorUno * 100).ToString("0.##", CASTELLANO) + " %";

        /// <summary>
        /// Corte 2: regalos por importe de pedido. El importe es la suma de BaseImponible del pedido,
        /// el mismo que usa <see cref="ValidadorRegaloPorImportePedido"/>. Por producto regalado:
        ///   - RegaloNoAplicado: el pedido ya llega a algún tramo y el producto no va de regalo.
        ///     Se ofrece el tramo que más unidades da, y se valida con la línea de regalo puesta.
        ///   - AmpliarImporte: no llega a ningún tramo, pero al más cercano le falta como mucho
        ///     <see cref="FRACCION_CERCANIA_IMPORTE"/> del importe. No se valida: el pedido
        ///     hipotético necesitaría importe que aún no existe; cuando se llegue, saldrá como
        ///     RegaloNoAplicado y entonces sí.
        /// </summary>
        internal static List<SugerenciaOfertaDTO> SugerirRegalosPorImporte(PedidoVentaDTO pedido, IServicioPrecios servicio, Func<PedidoVentaDTO, RespuestaValidacion> validar)
        {
            var sugerencias = new List<SugerenciaOfertaDTO>();
            LineaPedidoVentaDTO modelo = pedido.Lineas.FirstOrDefault(l => EsLineaDeProducto(l) && l.Cantidad > 0 && l.BaseImponible > 0);
            if (modelo == null)
            {
                return sugerencias;
            }
            List<RegaloImportePedido> regalos = (servicio.BuscarRegalosPorImportePedidoVigentes() ?? new List<RegaloImportePedido>())
                .Where(r => r.Cantidad > 0 && r.ImportePedido > 0 && !string.IsNullOrWhiteSpace(r.Producto))
                .Where(r => string.IsNullOrWhiteSpace(r.Empresa) || r.Empresa.Trim() == pedido.empresa?.Trim())
                .ToList();
            if (!regalos.Any())
            {
                return sugerencias;
            }
            decimal importe = pedido.Lineas.Sum(l => l.BaseImponible);

            foreach (IGrouping<string, RegaloImportePedido> porProducto in regalos.GroupBy(r => r.Producto.Trim()))
            {
                string producto = porProducto.Key;
                bool yaRegalado = pedido.Lineas.Any(l => EsLineaDeProducto(l) && l.Producto?.Trim() == producto && l.Cantidad > 0 && l.BaseImponible == 0);
                if (yaRegalado)
                {
                    continue;
                }

                RegaloImportePedido alcanzado = porProducto
                    .Where(r => importe >= r.ImportePedido)
                    .OrderByDescending(r => r.Cantidad)
                    .FirstOrDefault();
                if (alcanzado != null)
                {
                    var sugerencia = new SugerenciaOfertaDTO
                    {
                        Tipo = TIPO_REGALO_NO_APLICADO,
                        Producto = producto,
                        CantidadRegalo = alcanzado.Cantidad,
                        ImportePedido = alcanzado.ImportePedido,
                        ImporteQueFalta = 0,
                        Texto = $"El pedido supera los {alcanzado.ImportePedido.ToString("C", CASTELLANO)}: le corresponde{(alcanzado.Cantidad == 1 ? string.Empty : "n")} " +
                                $"{alcanzado.Cantidad} unidad{(alcanzado.Cantidad == 1 ? string.Empty : "es")} del producto {producto} de regalo."
                    };
                    RespuestaValidacion validacion = validar(ConRegaloPorImporte(pedido, modelo, servicio.BuscarProducto(producto), producto, alcanzado.Cantidad));
                    if (validacion != null && validacion.ValidacionSuperada)
                    {
                        sugerencias.Add(sugerencia);
                    }
                    continue;
                }

                RegaloImportePedido cercano = porProducto
                    .Where(r => r.ImportePedido - importe <= r.ImportePedido * FRACCION_CERCANIA_IMPORTE)
                    .OrderBy(r => r.ImportePedido)
                    .FirstOrDefault();
                if (cercano != null)
                {
                    decimal falta = cercano.ImportePedido - importe;
                    sugerencias.Add(new SugerenciaOfertaDTO
                    {
                        Tipo = TIPO_AMPLIAR_IMPORTE,
                        Producto = producto,
                        CantidadRegalo = cercano.Cantidad,
                        ImportePedido = cercano.ImportePedido,
                        ImporteQueFalta = falta,
                        Texto = $"Añadiendo {falta.ToString("C", CASTELLANO)} más al pedido llegas a los {cercano.ImportePedido.ToString("C", CASTELLANO)} " +
                                $"y te llevas {cercano.Cantidad} unidad{(cercano.Cantidad == 1 ? string.Empty : "es")} del producto {producto} de regalo."
                    });
                }
            }
            return sugerencias;
        }

        /// <summary>
        /// Copia del pedido con una línea de regalo del producto (precio 0) al final. La línea toma
        /// almacén, delegación, forma de venta, IVA, estado y fecha de la primera línea cobrada.
        /// </summary>
        internal static PedidoVentaDTO ConRegaloPorImporte(PedidoVentaDTO pedido, LineaPedidoVentaDTO modelo, Producto producto, string numeroProducto, int cantidad)
        {
            LineaPedidoVentaDTO regalo = Copiar(modelo);
            regalo.id = 0;
            regalo.Producto = numeroProducto;
            regalo.texto = producto?.Nombre;
            regalo.Cantidad = cantidad;
            regalo.PrecioUnitario = 0;
            regalo.DescuentoLinea = 0;
            regalo.DescuentoProducto = 0;
            regalo.oferta = null;
            regalo.GrupoProducto = producto?.Grupo;
            regalo.SubgrupoProducto = producto?.SubGrupo;
            regalo.precioTarifa = producto?.PVP ?? 0;
            return new PedidoVentaDTO
            {
                empresa = pedido.empresa,
                cliente = pedido.cliente,
                contacto = pedido.contacto,
                contactoCobro = pedido.contactoCobro,
                fecha = pedido.fecha,
                Lineas = pedido.Lineas.Concat(new[] { regalo }).ToList()
            };
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
            else if (producto != null && !GestorPrecios.calcularAplicarDescuento(producto))
            {
                // 23/09/26 (vendedor, 45917 de Anubis): un producto que no admite descuento (Aplicar_Dto = 0)
                // no entra en el N+M de su familia; el regalo es un 100 % de descuento. Solo le vale una
                // oferta dada de alta expresamente para él (hay tres: 40640, 40642 y 44731).
                return null;
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
