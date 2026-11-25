using NestoAPI.Infraestructure.ValidadoresServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using NestoAPI.Models.Picking;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Una muestra que el guardado de un pedido de tienda ha quitado (o dejado en lo que hay) por falta de stock.</summary>
    public class MuestraQuitadaTienda
    {
        /// <summary>La línea del DTO (para quitarla o cambiarle la cantidad).</summary>
        [JsonIgnore]
        internal LineaPedidoVentaDTO Linea { get; set; }
        /// <summary>Nº Orden de la línea (null si es nueva).</summary>
        public int? NumeroOrden { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Pedidas { get; set; }
        /// <summary>Las que se quedan en el pedido. 0 = la línea se quita entera.</summary>
        public int Quedan { get; set; }
        public int Quitadas => Pedidas - Quedan;
    }

    /// <summary>Lo que decide <see cref="ReglaMuestrasSinStockTienda.Decidir"/>.</summary>
    public class ResultadoMuestrasSinStockTienda
    {
        /// <summary>Null = se quitan; si no, por qué no (y el pedido da el 400 de siempre).</summary>
        public string Motivo { get; set; }
        public bool SoloMuestras { get; set; }
        public List<MuestraQuitadaTienda> Quitadas { get; set; } = new List<MuestraQuitadaTienda>();
    }

    /// <summary>
    /// Incidencia 1460 (Sancho; OK de Carlos 25/11/26): en un pedido de TIENDA (REI, ALC) solo vale «Según vaya entrando» y una muestra
    /// (material promocional, MMP) sin stock allí no puede quedarse pendiente: hasta ahora el guardado daba 400 y el vendedor tenía que
    /// borrarla a mano. Ahora, AL GUARDAR, se quita sola lo que no hay, con la MISMA regla que las faltas del picking
    /// (<see cref="GestorMuestrasSinStock.UnidadesQueSalen"/>: sin stock, fuera; con stock parcial, se deja en lo que hay), y se avisa.
    ///
    /// <para>Solo para la denegación de <c>ValidadorMaterialPromocional</c> (no los regalos/bonificados), con el interruptor
    /// <see cref="GestorMuestrasSinStock.CLAVE"/>, si en el pedido queda algo más que muestras y sin tocar lo que no se puede tocar
    /// (en el PUT, lo que ya está en curso o con picking: va primero contra el disponible). La validación de pantalla
    /// (ValidarServirJunto) no cambia.</para>
    /// </summary>
    public static class ReglaMuestrasSinStockTienda
    {
        public const string TIPO_AVISO = GestorMuestrasSinStock.TIPO_NOTIFICACION; // «MuestrasSinStockQuitadas»
        public const string OPERACION = "Pedido de tienda: muestra sin stock quitada al guardar";
        public const string MOTIVO_NADA = "No hay nada que quitar.";
        public const string MOTIVO_INTOCABLES = "Lo que no hay ya está en curso o con picking.";

        /// <summary>¿Se puede intentar? Tienda, interruptor encendido, ni presupuesto ni nota de entrega.</summary>
        internal static bool SeAplica(PedidoVentaDTO pedido, string almacen, bool interruptorActivo)
            => interruptorActivo && pedido != null && !pedido.EsPresupuesto && !pedido.notaEntrega && TextosServirJuntoTienda.EsTienda(almacen);

        /// <summary>Línea que cuenta (la misma criba que la validación de «Servir junto» del pedido).</summary>
        internal static bool LineaCuenta(LineaPedidoVentaDTO l)
            => l != null && l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.Producto)
               && l.Cantidad > 0 && l.estado <= Constantes.EstadosLineaVenta.EN_CURSO;

        /// <summary>
        /// LA REGLA (pura). <paramref name="sinStock"/>: lo denegado por el validador (con sus disponibles); <paramref name="muestras"/>: los
        /// productos MMP del pedido; <paramref name="sePuedeTocar"/>: qué líneas se pueden quitar o reducir. El disponible de cada producto
        /// lo gastan primero las intocables y luego las demás por orden. No cambia nada: eso lo hace <see cref="Aplicar"/>.
        /// </summary>
        public static ResultadoMuestrasSinStockTienda Decidir(IEnumerable<LineaPedidoVentaDTO> lineas, IEnumerable<ProductoSinStockDTO> sinStock,
            ISet<string> muestras, Func<LineaPedidoVentaDTO, bool> sePuedeTocar)
        {
            var resultado = new ResultadoMuestrasSinStockTienda();
            List<LineaPedidoVentaDTO> cuentan = (lineas ?? new List<LineaPedidoVentaDTO>()).Where(LineaCuenta).ToList();
            Dictionary<string, ProductoSinStockDTO> porProducto = (sinStock ?? Enumerable.Empty<ProductoSinStockDTO>())
                .Where(p => !string.IsNullOrWhiteSpace(p?.ProductoId))
                .GroupBy(p => p.ProductoId.Trim())
                .ToDictionary(g => g.Key, g => g.First());
            if (!porProducto.Any())
            {
                resultado.Motivo = MOTIVO_NADA;
                return resultado;
            }
            Func<LineaPedidoVentaDTO, bool> tocable = sePuedeTocar ?? (_ => true);

            foreach (KeyValuePair<string, ProductoSinStockDTO> producto in porProducto)
            {
                List<LineaPedidoVentaDTO> suyas = cuentan.Where(l => l.Producto.Trim() == producto.Key).ToList();
                int resto = Math.Max(producto.Value.Disponibles ?? 0, 0) - suyas.Where(l => !tocable(l)).Sum(l => l.Cantidad);
                if (resto < 0)
                {
                    resultado.Motivo = MOTIVO_INTOCABLES;
                    resultado.Quitadas.Clear();
                    return resultado;
                }
                foreach (LineaPedidoVentaDTO linea in suyas.Where(tocable))
                {
                    int quedan = GestorMuestrasSinStock.UnidadesQueSalen(linea.Cantidad, resto);
                    resto -= quedan;
                    if (quedan < linea.Cantidad)
                    {
                        resultado.Quitadas.Add(new MuestraQuitadaTienda
                        {
                            Linea = linea,
                            NumeroOrden = linea.id > 0 ? linea.id : (int?)null,
                            Producto = producto.Key,
                            Nombre = producto.Value.ProductoNombre?.Trim(),
                            Pedidas = linea.Cantidad,
                            Quedan = quedan
                        });
                    }
                }
            }

            if (!resultado.Quitadas.Any())
            {
                resultado.Motivo = MOTIVO_NADA;
                return resultado;
            }

            // Solo si queda algo MÁS que muestras (como en el picking): un pedido de solo muestras se quedaría vacío
            HashSet<LineaPedidoVentaDTO> fuera = new HashSet<LineaPedidoVentaDTO>(resultado.Quitadas.Where(q => q.Quedan <= 0).Select(q => q.Linea));
            bool quedaAlgoMas = cuentan.Any(l => !fuera.Contains(l) && !(muestras?.Contains(l.Producto.Trim()) ?? false));
            if (!quedaAlgoMas)
            {
                resultado.SoloMuestras = true;
                resultado.Motivo = TextoSoloMuestras();
                resultado.Quitadas.Clear();
            }
            return resultado;
        }

        /// <summary>Quita (o reduce) las líneas del DTO según lo decidido.</summary>
        public static void Aplicar(PedidoVentaDTO pedido, ResultadoMuestrasSinStockTienda resultado)
        {
            if (pedido?.Lineas == null || resultado == null || resultado.Motivo != null)
            {
                return;
            }
            foreach (MuestraQuitadaTienda quitada in resultado.Quitadas)
            {
                if (quitada.Quedan <= 0)
                {
                    _ = pedido.Lineas.Remove(quitada.Linea);
                }
                else
                {
                    quitada.Linea.Cantidad = quitada.Quedan;
                }
            }
        }

        /// <summary>Lo que se añade al 400 de siempre cuando el pedido solo lleva muestras.</summary>
        internal static string TextoSoloMuestras()
            => "Como el pedido no lleva nada más que muestras, no se quitan solas: se quedaría vacío.";

        /// <summary>
        /// «En Alcobendas no hay stock de estas muestras y se han quitado del pedido: 39918 BOLSA ANUBIS PAPEL, 40001 …. Si las necesita,
        /// pase el pedido desde Algete (ALG).»; con las que se dejan en lo que hay, «De 42654 … solo hay 1 de 3; se ha dejado en lo que hay.»
        /// </summary>
        public static AvisoPedidoDTO Aviso(string almacen, IList<MuestraQuitadaTienda> quitadas)
        {
            if (quitadas == null || !quitadas.Any())
            {
                return null;
            }
            string sede = TextosServirJuntoTienda.Sede(almacen);
            List<MuestraQuitadaTienda> enteras = quitadas.Where(q => q.Quedan <= 0).ToList();
            List<MuestraQuitadaTienda> reducidas = quitadas.Where(q => q.Quedan > 0).ToList();
            int productos = quitadas.Select(q => q.Producto).Distinct().Count();
            var partes = new List<string>();

            if (enteras.Any())
            {
                bool varias = enteras.Select(q => q.Producto).Distinct().Count() > 1;
                partes.Add($"En {sede} no hay stock de {(varias ? "estas muestras" : "esta muestra")} y se {(varias ? "han" : "ha")} quitado del pedido: " +
                    $"{string.Join(", ", enteras.Select(Nombrar).Distinct())}.");
            }
            if (reducidas.Any())
            {
                string detalle = string.Join(" y ", reducidas.Select(q => $"de {Nombrar(q)} solo hay {q.Quedan} de {q.Pedidas}"));
                bool varias = reducidas.Count > 1;
                string dejado = $"se {(varias ? "han" : "ha")} dejado en lo que hay.";
                partes.Add(enteras.Any()
                    ? $"{Mayuscula(detalle)}; {dejado}"
                    : $"En {sede} no hay stock suficiente de {(productos > 1 ? "estas muestras" : "esta muestra")}: {detalle}; {dejado}");
            }
            string siLasNecesita = enteras.Any() ? $"Si {(productos > 1 ? "las" : "la")} necesita" : "Si necesita más";
            partes.Add($"{siLasNecesita}, pase el pedido desde {TextosServirJuntoTienda.Algete()}.");

            return new AvisoPedidoDTO
            {
                Tipo = TIPO_AVISO,
                Mensaje = string.Join(" ", partes),
                Datos = quitadas.Select(q => new { q.NumeroOrden, q.Producto, q.Nombre, q.Pedidas, q.Quedan }).ToList()
            };
        }

        private static string Nombrar(MuestraQuitadaTienda q)
            => string.IsNullOrWhiteSpace(q.Nombre) ? q.Producto : $"{q.Producto} {q.Nombre}";

        private static string Mayuscula(string texto)
            => string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0]) + texto.Substring(1);

        /// <summary>
        /// El rastro de una muestra quitada al guardar: la misma tabla y la misma forma que las del picking
        /// (<see cref="EscritorMuestrasSinStock.Modificacion"/>), con el usuario que guarda el pedido.
        /// </summary>
        internal static Modificacion Modificacion(string empresa, int pedido, string almacen, string usuario, MuestraQuitadaTienda quitada, DateTime fecha)
        {
            return new Modificacion
            {
                Tabla = EscritorMuestrasSinStock.TABLA_MODIFICACIONES,
                Anterior = JsonConvert.SerializeObject(new
                {
                    Empresa = empresa?.Trim(),
                    Pedido = pedido,
                    NumeroOrden = quitada.NumeroOrden,
                    Producto = quitada.Producto,
                    Cantidad = quitada.Pedidas
                }),
                Nuevo = JsonConvert.SerializeObject(new
                {
                    Operacion = OPERACION,
                    Almacen = almacen?.Trim(),
                    Cantidad = quitada.Quedan,
                    LineaBorrada = quitada.Quedan <= 0,
                    Motivo = $"Sin stock en {almacen?.Trim()} de {quitada.Quitadas} ud(s) de la muestra {quitada.Producto}; en tienda solo vale " +
                        $"«{TextosServirJuntoTienda.ModoTienda()}» y la muestra no puede quedarse pendiente (incidencia 1460, {GestorMuestrasSinStock.CLAVE})"
                }),
                Usuario = usuario,
                Fecha = fecha
            };
        }
    }
}
