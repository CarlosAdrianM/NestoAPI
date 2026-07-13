using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorOfertasCombinadas : IValidadorAceptacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "No hay ninguna oferta combinada que autorice a vender el producto " + numeroProducto + " a ese precio",
                ProductoId = numeroProducto
            };
            List<OfertaCombinada> ofertasCombinadas = servicio.BuscarOfertasCombinadas(numeroProducto);
            if (ofertasCombinadas == null || ofertasCombinadas.Count() == 0)
            {
                return respuesta;
            }

            // Si varias ofertas se satisfacen a la vez (escalados por tramos: la misma lista de
            // referencias con cantidades 1, 2, 3... y distinto descuento, como las Allure 247-250),
            // hay que quedarse con la MÁS exigente que el pedido cumple, no con la primera por Id:
            // los grupos exigen "al menos" su cantidad, así que el tramo 1 también "se cumple" con
            // 3 unidades, y elegirlo dispara el sobresurtido y tira un pedido perfectamente válido.
            ofertasCombinadas = ofertasCombinadas.OrderByDescending(UnidadesRequeridas).ToList();

            OfertaCombinada ofertaCumplida = ofertasCombinadas.FirstOrDefault(o =>
                o.OfertasCombinadasDetalles.Where(d=> d.Cantidad > 0 && d.GrupoAlternativa == null).All(d =>
                    DetalleSatisfecho(d, pedido, servicio)
                )
                && GruposSatisfechos(o, pedido, InstanciasEnPedido(o, pedido, servicio))
            );

            if (ofertaCumplida != null)
            {
                // En las ofertas de varios productos exigimos que el pedido lleve algún
                // producto distinto del que se valida (si no, no sería una "combinada").
                // Las ofertas de un solo producto (p. ej. 2ª unidad al 50 %) no tienen ese
                // otro producto por definición, así que esa exigencia no se les aplica.
                // Las filas de FILTRO cuentan por su clave familia|prefijo (Issue #282).
                bool esOfertaDeUnSoloProducto = ofertaCumplida.OfertasCombinadasDetalles
                    .Select(d => d.Producto?.Trim() ?? $"F:{d.Familia?.Trim()}|{d.FiltroProducto}")
                    .Distinct().Count() == 1;

                // Issue #290: si el producto validado está en un GRUPO de alternativas, no se le
                // exige "otro producto": las alternativas son intercambiables por definición y
                // llevar todas las unidades del mismo producto (3 iguales en un 2+1 mezclable) es
                // una elección legítima del grupo. El abuso lo evita la cantidad del grupo
                // (GruposSatisfechos) y el sobresurtido.
                bool cubiertoPorGrupo = ofertaCumplida.OfertasCombinadasDetalles.Any(d =>
                    d.GrupoAlternativa.HasValue
                    && d.Producto != null
                    && d.Producto.Trim() == numeroProducto.Trim());

                if (!esOfertaDeUnSoloProducto && !cubiertoPorGrupo)
                {
                    // Issue #290: en las filas de GRUPO basta con que el pedido lleve alguna unidad
                    // del producto (a precio de la fila): la cantidad del grupo es del grupo entero,
                    // no de cada fila, y exigirla por fila impedía las combinaciones mezcladas
                    // (2+1 entre A/B/C con 1 unidad de cada). La cantidad real la vigila
                    // GruposSatisfechos.
                    bool tieneAlgunProducto = ofertasCombinadas.FirstOrDefault(o =>
                            o.OfertasCombinadasDetalles.Where(d => d.Producto == null || d.Producto != numeroProducto).Any(d =>
                                d.GrupoAlternativa.HasValue
                                    ? LineasQueCasan(d, pedido, servicio).Any(l => l.Cantidad > 0 && l.PrecioUnitario >= d.Precio)
                                    : DetalleSatisfecho(d, pedido, servicio)
                            )
                        ) != null;

                    if (!tieneAlgunProducto)
                    {
                        ofertaCumplida = null;
                    }
                }
            }

            if (ofertaCumplida == null || ofertaCumplida.ImporteMinimo > 0)
            {
                IEnumerable<OfertaCombinada> ofertasConImporteMinimo = ofertasCombinadas.Where(o =>
                    o.ImporteMinimo > 0
                    && o.OfertasCombinadasDetalles.Where(d => d.GrupoAlternativa == null).All(d =>
                        DetalleSatisfecho(d, pedido, servicio)
                    )
                    && GruposSatisfechos(o, pedido, InstanciasEnPedido(o, pedido, servicio))
                );
                OfertaCombinada ofertaConImporteMinimo;
                for (int i = 0; i < ofertasConImporteMinimo.Count(); i++)
                {
                    //ofertaConImporteMinimo = ofertaCumplida ?? ofertasCombinadas.FirstOrDefault(o => o.ImporteMinimo > 0);
                    ofertaConImporteMinimo = ofertasConImporteMinimo.ElementAt(i);

                    if (ofertaConImporteMinimo != null)
                    {
                        // Al importe mínimo suman las líneas que casan con CUALQUIER línea de la
                        // oferta, sea de producto o de filtro (Issue #282). Distinct: FiltrarLineas
                        // devuelve las mismas instancias de línea del pedido, no copias.
                        IEnumerable<LineaPedidoVentaDTO> lineasOfertaPedido = ofertaConImporteMinimo.OfertasCombinadasDetalles
                            .SelectMany(d => LineasQueCasan(d, pedido, servicio))
                            .Distinct();
                        var sumaImporte = lineasOfertaPedido.Sum(l => l.BaseImponible);
                        // El importe mínimo es por instancia de oferta: si el pedido lleva N veces
                        // las cantidades de la oferta, hay que cumplirlo N veces (antes se exigía una
                        // sola vez, lo que dejaba pasar varias instancias con un único suelo).
                        int instancias = InstanciasEnPedido(ofertaConImporteMinimo, pedido, servicio);
                        decimal importeMinimoRequerido = instancias * ofertaConImporteMinimo.ImporteMinimo;
                        // Tolerancia de redondeo: ImporteMinimo se guarda a 2 decimales (suele ser el
                        // precio con el descuento de la oferta) y aquí se multiplica por las instancias,
                        // acumulando hasta ~medio céntimo por instancia frente a la base imponible real de
                        // las líneas (que se redondea una sola vez). Sin esto, comprar al descuento exacto
                        // de la oferta se queda 1 céntimo corto (Modellare 239-242: 4×10,84=43,36 vs 43,35).
                        decimal toleranciaRedondeo = 0.005m * (instancias + 1);
                        if (sumaImporte >= importeMinimoRequerido - toleranciaRedondeo)
                        {
                            ofertaCumplida = ofertaConImporteMinimo;
                            respuesta.ValidacionSuperada = true;
                            respuesta.Motivo = "La oferta "+ ofertaCumplida.Id.ToString()
                                +" permite poner el producto "+ numeroProducto +" a ese precio";
                            break;
                        }
                        else
                        {
                            respuesta.Motivo = "La oferta " + ofertaConImporteMinimo.Id
                                + " tiene que tener un importe mínimo de " + importeMinimoRequerido.ToString("C") + " para que sea válida";
                            ofertaCumplida = null;
                        }
                    }
                }
            }

            // Si el producto validado pertenece a un grupo de alternativas con más unidades de las que
            // la oferta cubre (p. ej. 2 camisetas cuando la oferta da 1), la oferta autoriza solo las
            // unidades justas: las que sobran son material promocional suelto. Rechazamos este producto
            // (NO la oferta entera) para que el pipeline pase a ValidadorMuestrasYMaterialPromocional,
            // que cuenta solo las unidades sobrantes contra el 5 % del pedido.
            if (ofertaCumplida != null && ProductoEnGrupoSobresurtido(ofertaCumplida, pedido, numeroProducto, servicio))
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "La oferta " + ofertaCumplida.Id.ToString()
                        + " solo cubre las unidades justas del grupo de alternativas; las que sobran del producto "
                        + numeroProducto + " son material promocional suelto",
                    ProductoId = numeroProducto
                };
            }

            // Si el producto validado solo lo cubre una fila de FILTRO y las líneas que casan con ese
            // filtro superan la cantidad de la oferta (× instancias), las unidades sobrantes no las
            // cubre la oferta: son material promocional suelto (mismo criterio que los grupos de
            // alternativas). Sin esto, cumplida la oferta colarían unidades gratis extra del filtro.
            if (ofertaCumplida != null && FiltroSobresurtido(ofertaCumplida, pedido, numeroProducto, servicio))
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "La oferta " + ofertaCumplida.Id.ToString()
                        + " solo cubre las unidades justas del filtro; las que sobran del producto "
                        + numeroProducto + " son material promocional suelto",
                    ProductoId = numeroProducto
                };
            }

            // Issue #290: ofertas con "regalar menor importe" (2+1 combinable entre referencias de
            // precios distintos): la(s) unidad(es) a base 0 deben ser las de menor tarifa del
            // conjunto, y las pagadas deben cubrir su tarifa (suelo dinámico por combinación, que
            // el ImporteMinimo fijo no puede expresar).
            if (ofertaCumplida != null && ofertaCumplida.RegalarMenorImporte)
            {
                string motivoRegalo = MotivoRegaloNoEsMenorImporte(ofertaCumplida, pedido, servicio);
                if (motivoRegalo != null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        Motivo = motivoRegalo,
                        ProductoId = numeroProducto
                    };
                }
            }

            // Comprobamos los múltiplos (solo aplica a filas de producto concreto: si el producto
            // únicamente lo cubre un filtro, cantidadOferta sería 0 y el control es FiltroSobresurtido)
            if (ofertaCumplida != null)
            {
                var cantidadLineas = pedido.Lineas.Where(l => l.Producto == numeroProducto).Sum(l => l.Cantidad);
                var cantidadOferta = ofertaCumplida.OfertasCombinadasDetalles.Where(o => o.Producto == numeroProducto).Sum(o => o.Cantidad);
                if (cantidadOferta > 0 && cantidadLineas > cantidadOferta)
                {
                    IEnumerable<LineaPedidoVentaDTO> lineasOfertaPedido = pedido.Lineas.Where(l =>
                        ofertaCumplida.OfertasCombinadasDetalles.Where(o => !o.PermitirCantidadMenor && (float)l.Cantidad / o.Cantidad < (float)cantidadLineas / cantidadOferta).Select(d => d.Producto).Contains(l.Producto)
                    );
                    if (lineasOfertaPedido != null && lineasOfertaPedido.Count() > 0)
                    {
                        respuesta.Motivo = "Está ofertando más cantidad de la permitida en el producto " + numeroProducto + " para que la oferta " + ofertaCumplida.Id.ToString() + " sea válida";
                        ofertaCumplida = null;
                    }
                }
            }

            // Gate de consumo exacto: si un producto está compartido por varias ofertas, las
            // cantidades del pedido deben poder repartirse en instancias ENTERAS de oferta sin que
            // sobre ni falte. Evita que la misma unidad de un producto compartido "justifique" varias
            // ofertas a la vez (p.ej. 4 parejas con descuento pero 1 sola unidad del producto
            // vinculado, cuando harían falta 4). La lógica anterior valida oferta a oferta y no lo ve.
            bool seriaValido = ofertaCumplida != null || respuesta.ValidacionSuperada;
            if (seriaValido && !ConsumoExactoFactible(pedido, numeroProducto, servicio))
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "La oferta combinada del producto " + numeroProducto
                        + " no cuadra: las cantidades no permiten repartir las unidades en ofertas completas",
                    ProductoId = numeroProducto
                };
            }

            if (ofertaCumplida == null)
            {
                return respuesta;
            }

            return new RespuestaValidacion
            {
                ValidacionSuperada = true,
                Motivo = "La oferta " + ofertaCumplida.Id.ToString()
                    + " permite poner el producto " + numeroProducto + " a ese precio",
                ProductoId = numeroProducto
            };
        }

        /// <summary>
        /// ¿El pedido cubre la línea de oferta <paramref name="d"/>? El producto debe estar presente
        /// (a un precio &gt;= al de la oferta) y la SUMA de cantidades de sus líneas debe alcanzar la
        /// cantidad requerida. Se agrega por producto (no se exige una única línea con la cantidad)
        /// para permitir repartir la oferta en varias líneas — p.ej. "2ª unidad al 50 %" como 1 línea
        /// a precio completo + 1 línea con el 50 % (Nesto#371). Antes se comprobaba línea a línea y un
        /// 1+1 no validaba contra una oferta de cantidad 2.
        /// </summary>
        /// <summary>
        /// Líneas del pedido que casan con la línea de oferta: si el detalle es de PRODUCTO, las del
        /// producto concreto; si es de FILTRO (Issue #282: Producto NULL + Familia y/o prefijo de
        /// nombre), las que devuelve el mismo matching de OfertasPermitidas (FiltrarLineas). En ambos
        /// casos las cantidades se cuentan AGREGADAS sobre todas las líneas que casan.
        /// </summary>
        private static List<LineaPedidoVentaDTO> LineasQueCasan(OfertaCombinadaDetalle d, PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            if (d.Producto != null)
            {
                return pedido.Lineas
                    .Where(p => p.Producto != null && p.Producto.Trim() == d.Producto.Trim())
                    .ToList();
            }
            return servicio.FiltrarLineas(pedido, d.FiltroProducto ?? string.Empty, d.Familia?.Trim())
                ?? new List<LineaPedidoVentaDTO>();
        }

        private static bool DetalleSatisfecho(OfertaCombinadaDetalle d, PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            var lineasProducto = LineasQueCasan(d, pedido, servicio)
                .Where(p => p.PrecioUnitario >= d.Precio);

            // "Permitir cantidad menor" (NestoAPI#239): la Cantidad es un MÁXIMO, no una cantidad
            // exacta. La línea se satisface con cualquier cantidad de 0 a Cantidad (extra opcional:
            // p. ej. folletos/expositor). El exceso (> Cantidad) no lo cubre la oferta.
            if (d.PermitirCantidadMenor)
            {
                return lineasProducto.Sum(p => p.Cantidad) <= d.Cantidad;
            }

            return lineasProducto.Any() && lineasProducto.Sum(p => p.Cantidad) >= d.Cantidad;
        }

        /// <summary>
        /// Unidades totales que una instancia de la oferta exige: las de las líneas obligatorias
        /// más la cantidad de cada grupo de alternativas (compartida por todas sus líneas). Sirve
        /// para ordenar ofertas solapadas de más a menos exigente y elegir el tramo que mejor
        /// encaja con el pedido.
        /// </summary>
        private static int UnidadesRequeridas(OfertaCombinada oferta)
        {
            // Las filas de filtro (Producto == null) también son obligatorias y exigen su cantidad.
            int obligatorias = oferta.OfertasCombinadasDetalles
                .Where(d => d.Cantidad > 0 && d.GrupoAlternativa == null && !d.PermitirCantidadMenor)
                .Sum(d => (int)d.Cantidad);
            int grupos = oferta.OfertasCombinadasDetalles
                .Where(d => d.GrupoAlternativa.HasValue)
                .GroupBy(d => d.GrupoAlternativa.Value)
                .Sum(g => g.Max(d => (int)d.Cantidad));
            return obligatorias + grupos;
        }

        /// <summary>
        /// Nº de instancias de la oferta representadas en el pedido: el mínimo, entre los productos
        /// de la oferta (con Cantidad &gt; 0), de cuántas veces cabe su cantidad en la pedida.
        /// </summary>
        private static int InstanciasEnPedido(OfertaCombinada oferta, PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            int instancias = int.MaxValue;
            foreach (OfertaCombinadaDetalle d in oferta.OfertasCombinadasDetalles)
            {
                // Las líneas agrupadas (alternativas) no definen el nº de instancias: se comprueban
                // aparte en GruposSatisfechos. Las de "cantidad menor" tampoco: son extras opcionales
                // (0..máx) que no escalan la oferta. El nº de instancias lo marcan las líneas obligatorias
                // (de producto concreto o de filtro, con la cantidad agregada de las líneas que casan).
                if (d.Cantidad <= 0 || d.GrupoAlternativa != null || d.PermitirCantidadMenor) continue;
                int cantidad = (int)LineasQueCasan(d, pedido, servicio).Sum(l => l.Cantidad);
                int posibles = cantidad / d.Cantidad;
                if (posibles < instancias) instancias = posibles;
            }

            // Issue #290: si la oferta es SOLO de grupos (2+1 mezclable entre N referencias, sin
            // líneas obligatorias), las instancias las marcan los propios grupos: cuántas veces
            // cabe la cantidad del grupo en las unidades pedidas que casan (a precio de cada fila).
            // Sin esto, un pedido con 2×(2+1) se quedaba en 1 instancia y el control de
            // sobresurtido rechazaba las unidades de la segunda.
            if (instancias == int.MaxValue)
            {
                foreach (var grupo in oferta.OfertasCombinadasDetalles
                    .Where(d => d.GrupoAlternativa.HasValue && d.Producto != null)
                    .GroupBy(d => d.GrupoAlternativa.Value))
                {
                    int cantidadGrupo = grupo.Max(d => d.Cantidad);
                    if (cantidadGrupo <= 0) continue;
                    int pedidas = grupo.Sum(det => (int)pedido.Lineas
                        .Where(l => l.Producto != null
                                    && l.Producto.Trim() == det.Producto.Trim()
                                    && l.PrecioUnitario >= det.Precio)
                        .Sum(l => l.Cantidad));
                    int posibles = pedidas / cantidadGrupo;
                    if (posibles < instancias) instancias = posibles;
                }
            }

            return instancias == int.MaxValue || instancias < 1 ? 1 : instancias;
        }

        /// <summary>
        /// Issue #290: en ofertas con RegalarMenorImporte, comprueba que del conjunto de líneas que
        /// casan con la oferta (a) toda unidad a base 0 tenga tarifa &lt;= que toda unidad pagada
        /// (regalar solo la más barata; los empates de tarifa valen), y (b) lo pagado cubra la
        /// tarifa (PVP) de las unidades no regaladas — el suelo pasa a ser DINÁMICO por combinación,
        /// que es lo que un ImporteMinimo fijo no puede expresar cuando los precios difieren.
        /// Devuelve null si la regla se cumple, o el motivo del rechazo.
        /// </summary>
        internal static string MotivoRegaloNoEsMenorImporte(OfertaCombinada oferta, PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            List<LineaPedidoVentaDTO> conjunto = oferta.OfertasCombinadasDetalles
                .SelectMany(d => LineasQueCasan(d, pedido, servicio))
                .Distinct()
                .Where(l => l.Producto != null && l.Cantidad > 0)
                .ToList();

            List<LineaPedidoVentaDTO> gratis = conjunto.Where(l => l.BaseImponible == 0).ToList();
            List<LineaPedidoVentaDTO> pagadas = conjunto.Where(l => l.BaseImponible != 0).ToList();
            if (!gratis.Any() || !pagadas.Any())
            {
                return null; // sin unidad regalada no hay nada que ordenar (lo demás lo ven otros controles)
            }

            Dictionary<string, decimal> tarifas = conjunto
                .Select(l => l.Producto.Trim())
                .Distinct()
                .ToDictionary(p => p, p => servicio.BuscarProducto(p)?.PVP ?? 0);

            LineaPedidoVentaDTO gratisMasCara = gratis.OrderByDescending(l => tarifas[l.Producto.Trim()]).First();
            LineaPedidoVentaDTO pagadaMasBarata = pagadas.OrderBy(l => tarifas[l.Producto.Trim()]).First();
            decimal tarifaGratis = tarifas[gratisMasCara.Producto.Trim()];
            decimal tarifaPagada = tarifas[pagadaMasBarata.Producto.Trim()];
            if (tarifaGratis > tarifaPagada)
            {
                return "La oferta " + oferta.Id + " solo permite regalar la referencia de menor importe: no se puede regalar "
                    + gratisMasCara.Producto.Trim() + " (tarifa " + tarifaGratis.ToString("C") + ") mientras se cobra "
                    + pagadaMasBarata.Producto.Trim() + " (tarifa " + tarifaPagada.ToString("C") + ")";
            }

            // Suelo dinámico: la base del conjunto debe cubrir la tarifa de las unidades pagadas
            // (la oferta no acumula otros descuentos en las unidades de pago). Misma tolerancia
            // de redondeo que el ImporteMinimo fijo.
            decimal requerido = pagadas.Sum(l => l.Cantidad * tarifas[l.Producto.Trim()]);
            decimal pagado = conjunto.Sum(l => l.BaseImponible);
            decimal tolerancia = 0.005m * (pagadas.Count + 1);
            if (pagado < requerido - tolerancia)
            {
                return "La oferta " + oferta.Id + " exige cobrar a tarifa las unidades no regaladas: el importe de las líneas de la oferta debe ser al menos "
                    + requerido.ToString("C");
            }

            return null;
        }

        /// <summary>
        /// ¿El producto validado lo cubre una fila de FILTRO cuyas líneas casadas superan la cantidad
        /// de la oferta (× instancias)? Espejo de ProductoEnGrupoSobresurtido para filas de filtro
        /// (Issue #282): la oferta autoriza solo las unidades justas; las sobrantes se tratan como
        /// material promocional suelto. Las filas del mismo filtro se agrupan por si el alta reparte
        /// la cantidad en varias filas.
        /// </summary>
        private static bool FiltroSobresurtido(OfertaCombinada oferta, PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            var gruposFiltro = oferta.OfertasCombinadasDetalles
                .Where(d => d.Producto == null && d.GrupoAlternativa == null && d.Cantidad > 0)
                .GroupBy(d => new { Familia = d.Familia?.Trim(), d.FiltroProducto });

            int instancias = 0; // se calcula solo si hay filtros (evita el coste si no los hay)
            foreach (var grupo in gruposFiltro)
            {
                List<LineaPedidoVentaDTO> lineas = LineasQueCasan(grupo.First(), pedido, servicio);
                if (!lineas.Any(l => l.Producto != null && l.Producto.Trim() == numeroProducto.Trim()))
                {
                    continue;
                }
                if (instancias == 0)
                {
                    instancias = InstanciasEnPedido(oferta, pedido, servicio);
                }
                int cuota = grupo.Sum(d => (int)d.Cantidad) * instancias;
                int pedidas = (int)lineas.Sum(l => l.Cantidad);
                if (pedidas > cuota)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Grupos de alternativas: las líneas que comparten GrupoAlternativa son intercambiables
        /// ("elige 1 de N"; p. ej. una camiseta de cualquier talla). Para cada grupo, el pedido debe
        /// llevar productos del grupo (a precio aceptable) que sumen AL MENOS la cantidad requerida
        /// (cantidad del grupo × nº de instancias). Así no vale 0 (olvidar la camiseta), pero SÍ vale
        /// de más: las unidades sobrantes del grupo no tiran la oferta para el resto de productos; se
        /// tratan como material promocional suelto (las rechaza este validador a nivel del producto
        /// concreto vía ProductoEnGrupoSobresurtido y las cuenta el 5 % en
        /// ValidadorMuestrasYMaterialPromocional). Las ofertas sin grupos devuelven true (intacto).
        /// </summary>
        private static bool GruposSatisfechos(OfertaCombinada oferta, PedidoVentaDTO pedido, int instancias)
        {
            var grupos = oferta.OfertasCombinadasDetalles
                .Where(d => d.GrupoAlternativa.HasValue && d.Producto != null)
                .GroupBy(d => d.GrupoAlternativa.Value);

            foreach (var grupo in grupos)
            {
                // Todas las alternativas comparten cantidad (lo valida el alta de la oferta).
                int cantidadGrupo = grupo.Max(d => d.Cantidad);
                int requerido = instancias * cantidadGrupo;

                int pedidas = 0;
                foreach (OfertaCombinadaDetalle det in grupo)
                {
                    pedidas += (int)pedido.Lineas
                        .Where(l => l.Producto != null
                                    && l.Producto.Trim() == det.Producto.Trim()
                                    && l.PrecioUnitario >= det.Precio)
                        .Sum(l => l.Cantidad);
                }

                if (pedidas < requerido)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// ¿El producto validado pertenece a un grupo de alternativas de la oferta que lleva MÁS
        /// unidades de las que la oferta cubre (cantidad del grupo × nº de instancias)? En ese caso la
        /// oferta autoriza solo las unidades justas y este producto concreto no se da por cubierto: las
        /// que sobran son material promocional suelto. (Las ofertas reales 244/245 dan 1 camiseta; si
        /// el pedido lleva 2, la 2ª sobra.)
        /// </summary>
        private static bool ProductoEnGrupoSobresurtido(OfertaCombinada oferta, PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            OfertaCombinadaDetalle detalleGrupo = oferta.OfertasCombinadasDetalles
                .FirstOrDefault(d => d.GrupoAlternativa.HasValue
                    && d.Producto != null
                    && d.Producto.Trim() == numeroProducto.Trim());
            if (detalleGrupo == null)
            {
                return false;
            }

            int instancias = InstanciasEnPedido(oferta, pedido, servicio);
            int cantidadGrupo = oferta.OfertasCombinadasDetalles
                .Where(d => d.GrupoAlternativa == detalleGrupo.GrupoAlternativa)
                .Max(d => (int)d.Cantidad);
            int requerido = instancias * cantidadGrupo;

            int pedidas = oferta.OfertasCombinadasDetalles
                .Where(d => d.GrupoAlternativa == detalleGrupo.GrupoAlternativa && d.Producto != null)
                .Sum(d => (int)pedido.Lineas
                    .Where(l => l.Producto != null && l.Producto.Trim() == d.Producto.Trim())
                    .Sum(l => l.Cantidad));

            return pedidas > requerido;
        }

        /// <summary>
        /// Nº de unidades de <paramref name="numeroProducto"/> que una oferta combinada cubre en este
        /// pedido (para descontarlas del 5 % de muestras sueltas). Es el mínimo entre lo pedido y la
        /// cuota de la oferta (cantidad del producto —o del grupo si es alternativa— × nº de instancias),
        /// de entre las ofertas que el pedido satisface. 0 si ninguna oferta aplica.
        /// </summary>
        public static int UnidadesCubiertas(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            List<OfertaCombinada> ofertas = servicio.BuscarOfertasCombinadas(numeroProducto);
            if (ofertas == null || ofertas.Count == 0)
            {
                return 0;
            }

            int pedidas = (int)pedido.Lineas
                .Where(l => l.Producto != null && l.Producto.Trim() == numeroProducto.Trim())
                .Sum(l => l.Cantidad);

            int mejor = 0;
            foreach (OfertaCombinada oferta in ofertas)
            {
                if (!OfertaCubreElPedido(oferta, pedido, servicio))
                {
                    continue;
                }

                // Cuentan tanto las filas del producto concreto como las de FILTRO que lo casan
                // (Issue #282): en éstas la cuota es agregada para todos los productos del filtro,
                // lo que es generoso al descontar muestras; el exceso real lo corta FiltroSobresurtido.
                List<OfertaCombinadaDetalle> detallesProducto = oferta.OfertasCombinadasDetalles
                    .Where(d => (d.Producto != null && d.Producto.Trim() == numeroProducto.Trim())
                        || (d.Producto == null && LineasQueCasan(d, pedido, servicio)
                                .Any(l => l.Producto != null && l.Producto.Trim() == numeroProducto.Trim())))
                    .ToList();
                if (detallesProducto.Count == 0)
                {
                    continue;
                }

                int instancias = InstanciasEnPedido(oferta, pedido, servicio);
                OfertaCombinadaDetalle detalleGrupo = detallesProducto.FirstOrDefault(d => d.GrupoAlternativa.HasValue);
                int cuota;
                if (detalleGrupo != null)
                {
                    int cantidadGrupo = oferta.OfertasCombinadasDetalles
                        .Where(d => d.GrupoAlternativa == detalleGrupo.GrupoAlternativa)
                        .Max(d => (int)d.Cantidad);
                    cuota = cantidadGrupo * instancias;
                }
                else
                {
                    cuota = (int)detallesProducto.Sum(d => d.Cantidad) * instancias;
                }

                int cubiertas = Math.Min(pedidas, cuota);
                if (cubiertas > mejor)
                {
                    mejor = cubiertas;
                }
            }

            return mejor;
        }

        /// <summary>
        /// ¿El pedido satisface esta oferta? (productos obligatorios presentes a precio aceptable,
        /// grupos de alternativas con al menos lo requerido e importe mínimo cumplido por instancia).
        /// Se usa para contar unidades cubiertas; reutiliza la misma lógica que el matching principal.
        /// </summary>
        private static bool OfertaCubreElPedido(OfertaCombinada oferta, PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            bool obligatoriasOk = oferta.OfertasCombinadasDetalles
                .Where(d => d.Cantidad > 0 && d.GrupoAlternativa == null)
                .All(d => DetalleSatisfecho(d, pedido, servicio));
            if (!obligatoriasOk)
            {
                return false;
            }

            int instancias = InstanciasEnPedido(oferta, pedido, servicio);
            if (!GruposSatisfechos(oferta, pedido, instancias))
            {
                return false;
            }

            if (oferta.ImporteMinimo > 0)
            {
                IEnumerable<LineaPedidoVentaDTO> lineasOferta = oferta.OfertasCombinadasDetalles
                    .SelectMany(d => LineasQueCasan(d, pedido, servicio))
                    .Distinct();
                decimal suma = lineasOferta.Sum(l => l.BaseImponible);
                decimal requerido = instancias * oferta.ImporteMinimo;
                decimal tolerancia = 0.005m * (instancias + 1);
                if (suma < requerido - tolerancia)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Comprueba que las cantidades del pedido de los productos de las ofertas combinadas
        /// conectadas con <paramref name="numeroProducto"/> se puedan repartir en un número entero
        /// de instancias de oferta consumiéndolas EXACTAMENTE. Solo se aplica cuando hay un producto
        /// compartido por dos o más ofertas (que es donde la validación oferta-a-oferta se queda
        /// corta); en el resto de casos devuelve true y manda la lógica anterior.
        /// </summary>
        private static bool ConsumoExactoFactible(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            // Componente conexo de ofertas: las que contienen el producto y, transitivamente, las
            // que comparten algún producto con ellas.
            Dictionary<int, OfertaCombinada> ofertas = new Dictionary<int, OfertaCombinada>();
            Queue<string> porExplorar = new Queue<string>();
            HashSet<string> explorados = new HashSet<string>();
            porExplorar.Enqueue(numeroProducto.Trim());
            while (porExplorar.Count > 0)
            {
                string prod = porExplorar.Dequeue();
                if (!explorados.Add(prod)) continue;
                List<OfertaCombinada> encontradas = servicio.BuscarOfertasCombinadas(prod);
                if (encontradas == null) continue;
                foreach (OfertaCombinada o in encontradas)
                {
                    if (!ofertas.ContainsKey(o.Id)) ofertas[o.Id] = o;
                    foreach (OfertaCombinadaDetalle d in o.OfertasCombinadasDetalles)
                    {
                        if (d.Cantidad > 0 && d.Producto != null && d.GrupoAlternativa == null && !explorados.Contains(d.Producto.Trim()))
                        {
                            porExplorar.Enqueue(d.Producto.Trim());
                        }
                    }
                }
            }

            List<OfertaCombinada> listaOfertas = ofertas.Values.ToList();
            if (listaOfertas.Count <= 1)
            {
                return true; // con una sola oferta no hay producto compartido entre ofertas
            }

            // ¿Algún producto compartido por 2+ ofertas? Si no, los múltiplos por oferta ya bastan.
            Dictionary<string, int> ofertasPorProducto = new Dictionary<string, int>();
            foreach (OfertaCombinada o in listaOfertas)
            {
                HashSet<string> productosOferta = new HashSet<string>();
                foreach (OfertaCombinadaDetalle d in o.OfertasCombinadasDetalles)
                {
                    if (d.Cantidad > 0 && d.Producto != null && d.GrupoAlternativa == null) productosOferta.Add(d.Producto.Trim());
                }
                foreach (string p in productosOferta)
                {
                    ofertasPorProducto[p] = (ofertasPorProducto.TryGetValue(p, out int c) ? c : 0) + 1;
                }
            }
            if (!ofertasPorProducto.Values.Any(c => c >= 2))
            {
                return true; // ningún producto compartido
            }

            HashSet<string> productos = new HashSet<string>(ofertasPorProducto.Keys);
            Dictionary<string, int> cantidadPedida = new Dictionary<string, int>();
            foreach (string p in productos)
            {
                cantidadPedida[p] = (int)pedido.Lineas
                    .Where(l => l.Producto != null && l.Producto.Trim() == p)
                    .Sum(l => l.Cantidad);
            }

            return ExisteRepartoExacto(listaOfertas, productos, cantidadPedida);
        }

        /// <summary>
        /// ¿Existen enteros n_O &gt;= 0 (instancias por oferta) tales que, para cada producto,
        /// la suma de n_O * cantidad(oferta, producto) sea exactamente la cantidad pedida?
        /// Las cantidades reales son pequeñas, así que se resuelve por fuerza bruta acotada.
        /// </summary>
        private static bool ExisteRepartoExacto(List<OfertaCombinada> ofertas, HashSet<string> productos, Dictionary<string, int> cantidadPedida)
        {
            int[] maxInstancias = new int[ofertas.Count];
            long combinaciones = 1;
            for (int i = 0; i < ofertas.Count; i++)
            {
                int max = int.MaxValue;
                foreach (OfertaCombinadaDetalle d in ofertas[i].OfertasCombinadasDetalles)
                {
                    if (d.Cantidad <= 0 || d.Producto == null || d.GrupoAlternativa != null) continue;
                    int disponible = cantidadPedida.TryGetValue(d.Producto.Trim(), out int q) ? q : 0;
                    int posible = disponible / d.Cantidad;
                    if (posible < max) max = posible;
                }
                maxInstancias[i] = max == int.MaxValue ? 0 : max;
                combinaciones *= maxInstancias[i] + 1;
                if (combinaciones > 100000) return true; // demasiadas combinaciones: no rechazamos (conservador)
            }

            return BuscarReparto(0, new int[ofertas.Count], maxInstancias, ofertas, productos, cantidadPedida);
        }

        private static bool BuscarReparto(int indice, int[] n, int[] max, List<OfertaCombinada> ofertas, HashSet<string> productos, Dictionary<string, int> cantidadPedida)
        {
            if (indice == ofertas.Count)
            {
                foreach (string p in productos)
                {
                    int consumido = 0;
                    for (int i = 0; i < ofertas.Count; i++)
                    {
                        foreach (OfertaCombinadaDetalle d in ofertas[i].OfertasCombinadasDetalles)
                        {
                            if (d.Cantidad > 0 && d.Producto != null && d.GrupoAlternativa == null && d.Producto.Trim() == p)
                            {
                                consumido += n[i] * d.Cantidad;
                            }
                        }
                    }
                    if (consumido != cantidadPedida[p]) return false;
                }
                return true;
            }

            for (int v = 0; v <= max[indice]; v++)
            {
                n[indice] = v;
                if (BuscarReparto(indice + 1, n, max, ofertas, productos, cantidadPedida)) return true;
            }
            return false;
        }
    }
}