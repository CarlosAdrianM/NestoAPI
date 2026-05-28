using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
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
                o.OfertasCombinadasDetalles.Where(d=> d.Cantidad > 0).All(d => 
                    pedido.Lineas.Where(p=>p.PrecioUnitario >= d.Precio && p.Cantidad >= d.Cantidad).Select(p => p.Producto.Trim()).Contains(d.Producto.Trim())
                )
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
                                pedido.Lineas.Where(p => p.PrecioUnitario >= d.Precio && p.Cantidad >= d.Cantidad).Select(p => p.Producto.Trim()).Contains(d.Producto.Trim())
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
                    o.OfertasCombinadasDetalles.All(d =>
                        pedido.Lineas.Where(p => p.PrecioUnitario >= d.Precio && p.Cantidad >= d.Cantidad).Select(p => p.Producto.Trim()).Contains(d.Producto.Trim())
                        && o.ImporteMinimo>0
                    )
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
                        if (sumaImporte >= ofertaConImporteMinimo.ImporteMinimo)
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
                                + " tiene que tener un importe mínimo de " + ofertaConImporteMinimo.ImporteMinimo.ToString("C") + " para que sea válida";
                            ofertaCumplida = null;
                        }
                    }
                }
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
                        if (d.Cantidad > 0 && d.Producto != null && !explorados.Contains(d.Producto.Trim()))
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
                    if (d.Cantidad > 0 && d.Producto != null) productosOferta.Add(d.Producto.Trim());
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
                    if (d.Cantidad <= 0 || d.Producto == null) continue;
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
                            if (d.Cantidad > 0 && d.Producto != null && d.Producto.Trim() == p)
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