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

            OfertaCombinada ofertaCumplida = ofertasCombinadas.FirstOrDefault(o =>
                o.OfertasCombinadasDetalles.Where(d=> d.Cantidad > 0 && d.GrupoAlternativa == null).All(d =>
                    DetalleSatisfecho(d, pedido)
                )
                && GruposSatisfechos(o, pedido, InstanciasEnPedido(o, pedido))
            );

            if (ofertaCumplida != null)
            {
                // En las ofertas de varios productos exigimos que el pedido lleve algún
                // producto distinto del que se valida (si no, no sería una "combinada").
                // Las ofertas de un solo producto (p. ej. 2ª unidad al 50 %) no tienen ese
                // otro producto por definición, así que esa exigencia no se les aplica.
                bool esOfertaDeUnSoloProducto = ofertaCumplida.OfertasCombinadasDetalles
                    .Select(d => d.Producto.Trim()).Distinct().Count() == 1;

                if (!esOfertaDeUnSoloProducto)
                {
                    bool tieneAlgunProducto = ofertasCombinadas.FirstOrDefault(o =>
                            o.OfertasCombinadasDetalles.Where(d => d.Producto != numeroProducto).Any(d =>
                                DetalleSatisfecho(d, pedido)
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
                        DetalleSatisfecho(d, pedido)
                    )
                    && GruposSatisfechos(o, pedido, InstanciasEnPedido(o, pedido))
                );
                OfertaCombinada ofertaConImporteMinimo;
                for (int i = 0; i < ofertasConImporteMinimo.Count(); i++)
                {
                    //ofertaConImporteMinimo = ofertaCumplida ?? ofertasCombinadas.FirstOrDefault(o => o.ImporteMinimo > 0);
                    ofertaConImporteMinimo = ofertasConImporteMinimo.ElementAt(i);

                    if (ofertaConImporteMinimo != null)
                    {
                        IEnumerable<LineaPedidoVentaDTO> lineasOfertaPedido = pedido.Lineas.Where(l =>
                            ofertaConImporteMinimo.OfertasCombinadasDetalles.Select(d => d.Producto.Trim()).Contains(l.Producto.Trim())
                        );
                        var sumaImporte = lineasOfertaPedido.Sum(l => l.BaseImponible);
                        // El importe mínimo es por instancia de oferta: si el pedido lleva N veces
                        // las cantidades de la oferta, hay que cumplirlo N veces (antes se exigía una
                        // sola vez, lo que dejaba pasar varias instancias con un único suelo).
                        int instancias = InstanciasEnPedido(ofertaConImporteMinimo, pedido);
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
            if (ofertaCumplida != null && ProductoEnGrupoSobresurtido(ofertaCumplida, pedido, numeroProducto))
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

            // Comprobamos los múltiplos
            if (ofertaCumplida != null)
            {
                var cantidadLineas = pedido.Lineas.Where(l => l.Producto == numeroProducto).Sum(l => l.Cantidad);
                var cantidadOferta = ofertaCumplida.OfertasCombinadasDetalles.Where(o => o.Producto == numeroProducto).Sum(o => o.Cantidad);
                if (cantidadLineas > cantidadOferta)
                {
                    IEnumerable<LineaPedidoVentaDTO> lineasOfertaPedido = pedido.Lineas.Where(l =>
                        ofertaCumplida.OfertasCombinadasDetalles.Where(o => (float)l.Cantidad / o.Cantidad < (float)cantidadLineas / cantidadOferta).Select(d => d.Producto).Contains(l.Producto)
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
        private static bool DetalleSatisfecho(OfertaCombinadaDetalle d, PedidoVentaDTO pedido)
        {
            var lineasProducto = pedido.Lineas
                .Where(p => p.Producto != null
                            && p.Producto.Trim() == d.Producto.Trim()
                            && p.PrecioUnitario >= d.Precio);

            return lineasProducto.Any() && lineasProducto.Sum(p => p.Cantidad) >= d.Cantidad;
        }

        /// <summary>
        /// Nº de instancias de la oferta representadas en el pedido: el mínimo, entre los productos
        /// de la oferta (con Cantidad &gt; 0), de cuántas veces cabe su cantidad en la pedida.
        /// </summary>
        private static int InstanciasEnPedido(OfertaCombinada oferta, PedidoVentaDTO pedido)
        {
            int instancias = int.MaxValue;
            foreach (OfertaCombinadaDetalle d in oferta.OfertasCombinadasDetalles)
            {
                // Las líneas agrupadas (alternativas) no definen el nº de instancias: se comprueban
                // aparte en GruposSatisfechos. El nº de instancias lo marcan las líneas obligatorias.
                if (d.Cantidad <= 0 || d.Producto == null || d.GrupoAlternativa != null) continue;
                int cantidad = (int)pedido.Lineas
                    .Where(l => l.Producto != null && l.Producto.Trim() == d.Producto.Trim())
                    .Sum(l => l.Cantidad);
                int posibles = cantidad / d.Cantidad;
                if (posibles < instancias) instancias = posibles;
            }
            return instancias == int.MaxValue || instancias < 1 ? 1 : instancias;
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
        private static bool ProductoEnGrupoSobresurtido(OfertaCombinada oferta, PedidoVentaDTO pedido, string numeroProducto)
        {
            OfertaCombinadaDetalle detalleGrupo = oferta.OfertasCombinadasDetalles
                .FirstOrDefault(d => d.GrupoAlternativa.HasValue
                    && d.Producto != null
                    && d.Producto.Trim() == numeroProducto.Trim());
            if (detalleGrupo == null)
            {
                return false;
            }

            int instancias = InstanciasEnPedido(oferta, pedido);
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
                if (!OfertaCubreElPedido(oferta, pedido))
                {
                    continue;
                }

                List<OfertaCombinadaDetalle> detallesProducto = oferta.OfertasCombinadasDetalles
                    .Where(d => d.Producto != null && d.Producto.Trim() == numeroProducto.Trim())
                    .ToList();
                if (detallesProducto.Count == 0)
                {
                    continue;
                }

                int instancias = InstanciasEnPedido(oferta, pedido);
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
        private static bool OfertaCubreElPedido(OfertaCombinada oferta, PedidoVentaDTO pedido)
        {
            bool obligatoriasOk = oferta.OfertasCombinadasDetalles
                .Where(d => d.Cantidad > 0 && d.GrupoAlternativa == null)
                .All(d => DetalleSatisfecho(d, pedido));
            if (!obligatoriasOk)
            {
                return false;
            }

            int instancias = InstanciasEnPedido(oferta, pedido);
            if (!GruposSatisfechos(oferta, pedido, instancias))
            {
                return false;
            }

            if (oferta.ImporteMinimo > 0)
            {
                IEnumerable<LineaPedidoVentaDTO> lineasOferta = pedido.Lineas.Where(l =>
                    l.Producto != null
                    && oferta.OfertasCombinadasDetalles.Select(d => d.Producto.Trim()).Contains(l.Producto.Trim()));
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