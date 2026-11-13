using NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.FechasOrientativas
{
    /// <summary>NestoAPI#861: un producto con venta pendiente que hoy se queda sin fecha y con fecha ORIENTATIVA de lo no pedido.</summary>
    public class ProductoSinPedir
    {
        public string Producto { get; set; }
        /// <summary>Unidades sin fecha de todos los pedidos de venta que lo esperan.</summary>
        public int Unidades { get; set; }
        /// <summary>Pedidos de venta que lo esperan.</summary>
        public int PedidosVenta { get; set; }
        /// <summary>La <see cref="PartidaFechaEntregaAgenciaDTO.FechaOrientativa"/> más temprana (la que ve el usuario: día en que saldría).</summary>
        public DateTime FechaSalidaOrientativa { get; set; }
    }

    /// <summary>NestoAPI#861: de dónde sale la fecha de <see cref="ReglaFechaOrientativaSinPedir"/> (los mismos números, desglosados).</summary>
    public class IngredientesFechaOrientativa
    {
        public DateTime? UltimoPedido { get; set; }
        /// <summary>IntervaloPedidosMediano del proveedor.</summary>
        public int Cadencia { get; set; }
        public int DiasDesdeUltimo { get; set; }
        /// <summary>max(1, cadencia − días desde el último).</summary>
        public int DiasHastaPedido { get; set; }
        /// <summary>P80 propio o, sin plazo propio, el genérico.</summary>
        public int PlazoDias { get; set; }
        public bool PlazoPropio { get; set; }
        public bool YaTocabaPedir { get; set; }
        /// <summary>Hoy + días hasta el pedido.</summary>
        public DateTime FechaPedidoPrevista { get; set; }
        /// <summary>Hoy + días hasta el pedido + plazo: lo que da la regla (llegada).</summary>
        public DateTime FechaLlegadaOrientativa { get; set; }
    }

    /// <summary>NestoAPI#861: una fila de dbo.FechasOrientativasSinPedir (la foto del día; lo real lo rellena después el resolver).</summary>
    public class FotoFechaOrientativa
    {
        public DateTime DiaFoto { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public string Proveedor { get; set; }
        public int Unidades { get; set; }
        public int PedidosVenta { get; set; }
        public DateTime? UltimoPedido { get; set; }
        public int Cadencia { get; set; }
        public int DiasDesdeUltimo { get; set; }
        public int DiasHastaPedido { get; set; }
        public DateTime FechaPedidoPrevista { get; set; }
        public int PlazoDias { get; set; }
        public bool PlazoPropio { get; set; }
        public DateTime FechaLlegadaOrientativa { get; set; }
        public DateTime? FechaSalidaOrientativa { get; set; }
        public bool YaTocabaPedir { get; set; }
        public bool PrimeraDelEpisodio { get; set; }
    }

    /// <summary>NestoAPI#861: una foto abierta (sin resolver ni caducar) tal como está guardada.</summary>
    public class FotoOrientativaAbierta
    {
        public int Id { get; set; }
        public DateTime DiaFoto { get; set; }
        public DateTime FechaPedidoPrevista { get; set; }
        public DateTime FechaLlegadaOrientativa { get; set; }
        public string EmpresaPedidoReal { get; set; }
        public int? PedidoReal { get; set; }
        public DateTime? FechaPedidoReal { get; set; }
        public DateTime? FechaAlbaranReal { get; set; }
    }

    /// <summary>NestoAPI#861: el primer pedido de compra al proveedor, enviado desde el día de la foto, que lleva el producto.</summary>
    public class PedidoRealOrientativa
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Día del envío (dbo.PedidosCompraEnvios) o, si no está ahí, CabPedidoCmp.[Fecha Modificación].</summary>
        public DateTime FechaPedido { get; set; }
        /// <summary>¿La fecha sale de dbo.PedidosCompraEnvios?</summary>
        public bool DeEnvios { get; set; }
        /// <summary>El primer albarán de las líneas de ese producto en ese pedido. Null si aún no ha llegado.</summary>
        public DateTime? FechaAlbaran { get; set; }
    }

    /// <summary>NestoAPI#861: lo que el resolver escribe en una foto abierta.</summary>
    public class ResolucionFechaOrientativa
    {
        public int Id { get; set; }
        /// <summary>¿Hay algo distinto de lo guardado? Si no, no se escribe.</summary>
        public bool Cambia { get; set; }
        public string EmpresaPedidoReal { get; set; }
        public int? PedidoReal { get; set; }
        public DateTime? FechaPedidoReal { get; set; }
        public bool? FechaPedidoRealDeEnvios { get; set; }
        public DateTime? FechaAlbaranReal { get; set; }
        /// <summary>Pedido real − previsto, en días (positivo: se pidió más tarde de lo que decía la regla).</summary>
        public int? ErrorPedidoDias { get; set; }
        /// <summary>Albarán real − llegada orientativa, en días (positivo: la orientativa se quedó corta).</summary>
        public int? ErrorLlegadaDias { get; set; }
        public DateTime? ResueltaEl { get; set; }
        public bool Caducada { get; set; }
    }

    /// <summary>NestoAPI#861: una primera foto de episodio ya resuelta (lo que entra en el resumen).</summary>
    public class FilaErrorOrientativa
    {
        public string Proveedor { get; set; }
        public bool YaTocabaPedir { get; set; }
        public int ErrorPedidoDias { get; set; }
        public int ErrorLlegadaDias { get; set; }
    }

    /// <summary>NestoAPI#861: errores de un grupo de casos.</summary>
    public class EstadisticasFechasOrientativas
    {
        public int Casos { get; set; }
        public decimal? ErrorPedidoMedio { get; set; }
        public int? ErrorPedidoMediano { get; set; }
        public decimal? ErrorLlegadaMedio { get; set; }
        public int? ErrorLlegadaMediano { get; set; }
        /// <summary>% de casos que llegaron DESPUÉS de la fecha orientativa (error de llegada &gt; 0).</summary>
        public decimal? PorcentajeCorta { get; set; }
    }

    /// <summary>NestoAPI#861: el resumen de un proveedor (o de todos, con Proveedor null).</summary>
    public class ResumenProveedorFechasOrientativas
    {
        public string Proveedor { get; set; }
        public EstadisticasFechasOrientativas Todas { get; set; }
        /// <summary>Solo los casos en que, el día de la foto, ya tocaba pedir (días desde el último &gt;= cadencia).</summary>
        public EstadisticasFechasOrientativas YaTocabaPedir { get; set; }
        public EstadisticasFechasOrientativas NoTocabaPedir { get; set; }
    }

    /// <summary>NestoAPI#861: respuesta de GET api/Compras/PlazosProveedores/Orientativas.</summary>
    public class ResumenFechasOrientativasDTO
    {
        public int Meses { get; set; }
        public DateTime Desde { get; set; }
        /// <summary>Episodios (primeras fotos) desde <see cref="Desde"/> todavía abiertos: sin pedir o sin llegar.</summary>
        public int Abiertas { get; set; }
        /// <summary>Episodios caducados: más de <see cref="ReglasFechasOrientativas.DIAS_CADUCIDAD"/> días sin llegar.</summary>
        public int Caducadas { get; set; }
        public ResumenProveedorFechasOrientativas Totales { get; set; }
        /// <summary>Por proveedor, de más a menos casos.</summary>
        public List<ResumenProveedorFechasOrientativas> Proveedores { get; set; } = new List<ResumenProveedorFechasOrientativas>();
    }

    /// <summary>
    /// NestoAPI#861: reglas PURAS para apuntar y medir las fechas orientativas de lo no pedido (<see cref="ReglaFechaOrientativaSinPedir"/>,
    /// que NO se toca).
    /// </summary>
    public static class ReglasFechasOrientativas
    {
        /// <summary>Una foto sin llegar a los cuántos días de hecha se da por caducada.</summary>
        public const int DIAS_CADUCIDAD = 120;
        /// <summary>Cuántos días atrás se mira la última foto del producto para decidir si es la primera del episodio.</summary>
        public const int DIAS_BUSCAR_EPISODIO = 15;
        /// <summary>Tope de días hacia atrás buscando el laborable anterior (por si el calendario dice que todo es festivo).</summary>
        private const int TOPE_LABORABLE = 30;

        /// <summary>
        /// ¿Es una partida sin fecha con la orientativa de LO NO PEDIDO? (no la de un pedido a proveedor enviado con la prevista lejana, que
        /// lleva <see cref="PartidaFechaEntregaAgenciaDTO.FechaPrevistaProveedorOrientativo"/>).
        /// </summary>
        public static bool EsOrientativaSinPedir(PartidaFechaEntregaAgenciaDTO partida) =>
            partida != null
            && string.Equals(partida.Origen, PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, StringComparison.Ordinal)
            && !partida.PuedeSalirEl.HasValue
            && partida.FechaOrientativa.HasValue
            && !partida.PedidoProveedorOrientativo.HasValue
            && !partida.FechaPrevistaProveedorOrientativo.HasValue
            && !string.IsNullOrWhiteSpace(partida.Producto);

        /// <summary>Los productos sin pedir de los cálculos de los pedidos de venta pendientes (uno por producto).</summary>
        public static List<ProductoSinPedir> ProductosSinPedir(IEnumerable<KeyValuePair<int, FechaEntregaAgenciaDTO>> calculos)
        {
            var porProducto = new Dictionary<string, (int Unidades, HashSet<int> Pedidos, DateTime Salida)>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, FechaEntregaAgenciaDTO> calculo in calculos ?? Enumerable.Empty<KeyValuePair<int, FechaEntregaAgenciaDTO>>())
            {
                foreach (PartidaFechaEntregaAgenciaDTO partida in (calculo.Value?.Desglose ?? new List<PartidaFechaEntregaAgenciaDTO>()).Where(EsOrientativaSinPedir))
                {
                    string producto = partida.Producto.Trim();
                    DateTime salida = partida.FechaOrientativa.Value.Date;
                    if (porProducto.TryGetValue(producto, out (int Unidades, HashSet<int> Pedidos, DateTime Salida) acumulado))
                    {
                        _ = acumulado.Pedidos.Add(calculo.Key);
                        porProducto[producto] = (acumulado.Unidades + Math.Max(0, partida.Unidades), acumulado.Pedidos,
                            salida < acumulado.Salida ? salida : acumulado.Salida);
                    }
                    else
                    {
                        porProducto[producto] = (Math.Max(0, partida.Unidades), new HashSet<int> { calculo.Key }, salida);
                    }
                }
            }
            return porProducto.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(p => new ProductoSinPedir
                {
                    Producto = p.Key,
                    Unidades = p.Value.Unidades,
                    PedidosVenta = p.Value.Pedidos.Count,
                    FechaSalidaOrientativa = p.Value.Salida
                })
                .ToList();
        }

        /// <summary>¿Ya tocaba pedir el día de la foto? Días desde el último pedido &gt;= cadencia (la regla dice entonces «mañana»).</summary>
        public static bool YaTocabaPedir(int diasDesdeUltimo, int cadencia) => diasDesdeUltimo >= cadencia;

        /// <summary>
        /// Los ingredientes de <see cref="ReglaFechaOrientativaSinPedir.LlegadaEstimada"/>. Null cuando la regla no da fecha. La llegada es
        /// SIEMPRE la de la regla (no se recalcula aquí).
        /// </summary>
        public static IngredientesFechaOrientativa Ingredientes(DateTime hoy, PlazoProveedor propio, PlazoProveedor generico)
        {
            hoy = hoy.Date;
            DateTime? llegada = ReglaFechaOrientativaSinPedir.LlegadaEstimada(hoy, propio, generico);
            if (!llegada.HasValue)
            {
                return null;
            }
            bool plazoPropio = propio.TienePlazoPropio;
            int plazo = plazoPropio ? propio.DiasP80.Value : generico.DiasP80.Value;
            int cadencia = propio.IntervaloPedidosMediano.Value;
            int desdeElUltimo = Math.Max(0, (hoy - propio.UltimoPedido.Value.Date).Days);
            int hastaElPedido = Math.Max(1, (llegada.Value - hoy).Days - plazo);
            return new IngredientesFechaOrientativa
            {
                UltimoPedido = propio.UltimoPedido.Value.Date,
                Cadencia = cadencia,
                DiasDesdeUltimo = desdeElUltimo,
                DiasHastaPedido = hastaElPedido,
                PlazoDias = plazo,
                PlazoPropio = plazoPropio,
                YaTocabaPedir = YaTocabaPedir(desdeElUltimo, cadencia),
                FechaPedidoPrevista = hoy.AddDays(hastaElPedido),
                FechaLlegadaOrientativa = llegada.Value.Date
            };
        }

        /// <summary>El laborable anterior a <paramref name="hoy"/> (<paramref name="esFestivo"/> cuenta sábados, domingos y festivos).</summary>
        public static DateTime AnteriorLaborable(DateTime hoy, Func<DateTime, bool> esFestivo)
        {
            DateTime dia = hoy.Date.AddDays(-1);
            for (int i = 0; i < TOPE_LABORABLE && esFestivo != null && esFestivo(dia); i++)
            {
                dia = dia.AddDays(-1);
            }
            return dia;
        }

        /// <summary>
        /// ¿Es la primera foto del episodio? Sí, salvo que el producto tuviera foto el laborable anterior o después (los fines de semana y
        /// festivos entre medias no cortan el episodio: una falta que dura 10 días es UN caso).
        /// </summary>
        public static bool PrimeraDelEpisodio(DateTime hoy, DateTime? ultimaFotoAnterior, Func<DateTime, bool> esFestivo) =>
            !ultimaFotoAnterior.HasValue || ultimaFotoAnterior.Value.Date < AnteriorLaborable(hoy, esFestivo) || ultimaFotoAnterior.Value.Date >= hoy.Date;

        /// <summary>La foto de hoy de un producto, o null si no hay proveedor o la regla no da fecha (sin cadencia o sin plazo).</summary>
        public static FotoFechaOrientativa ConstruirFoto(DateTime hoy, string empresa, ProductoSinPedir producto, string proveedor,
            PlazosProveedores plazos, DateTime? ultimaFotoAnterior, Func<DateTime, bool> esFestivo)
        {
            if (producto == null || string.IsNullOrWhiteSpace(producto.Producto) || string.IsNullOrWhiteSpace(proveedor) || plazos == null)
            {
                return null;
            }
            IngredientesFechaOrientativa ingredientes = Ingredientes(hoy, plazos.DeProveedor(proveedor), plazos.Generico);
            if (ingredientes == null)
            {
                return null;
            }
            return new FotoFechaOrientativa
            {
                DiaFoto = hoy.Date,
                Empresa = empresa?.Trim(),
                Producto = producto.Producto.Trim(),
                Proveedor = proveedor.Trim(),
                Unidades = producto.Unidades,
                PedidosVenta = producto.PedidosVenta,
                UltimoPedido = ingredientes.UltimoPedido,
                Cadencia = ingredientes.Cadencia,
                DiasDesdeUltimo = ingredientes.DiasDesdeUltimo,
                DiasHastaPedido = ingredientes.DiasHastaPedido,
                FechaPedidoPrevista = ingredientes.FechaPedidoPrevista,
                PlazoDias = ingredientes.PlazoDias,
                PlazoPropio = ingredientes.PlazoPropio,
                FechaLlegadaOrientativa = ingredientes.FechaLlegadaOrientativa,
                FechaSalidaOrientativa = producto.FechaSalidaOrientativa.Date,
                YaTocabaPedir = ingredientes.YaTocabaPedir,
                PrimeraDelEpisodio = PrimeraDelEpisodio(hoy, ultimaFotoAnterior, esFestivo)
            };
        }

        /// <summary>Real − previsto, en días.</summary>
        public static int ErrorDias(DateTime real, DateTime previsto) => (real.Date - previsto.Date).Days;

        /// <summary>¿Caduca? Sin llegar y con más de <see cref="DIAS_CADUCIDAD"/> días desde la foto (se haya pedido o no).</summary>
        public static bool Caduca(DateTime diaFoto, DateTime hoy, bool llegado) => !llegado && (hoy.Date - diaFoto.Date).Days > DIAS_CADUCIDAD;

        /// <summary>
        /// Qué escribir en una foto abierta con el pedido real encontrado (o null). Con albarán, queda resuelta (lo real y los dos errores);
        /// con pedido sin albarán, se apunta el pedido y su error y sigue abierta (se vuelve a mirar cada noche); sin llegar y vieja, caduca.
        /// </summary>
        public static ResolucionFechaOrientativa Resolver(FotoOrientativaAbierta foto, PedidoRealOrientativa real, DateTime ahora)
        {
            if (foto == null)
            {
                return null;
            }
            var resolucion = new ResolucionFechaOrientativa { Id = foto.Id };
            if (real != null)
            {
                resolucion.EmpresaPedidoReal = real.Empresa?.Trim();
                resolucion.PedidoReal = real.Pedido;
                resolucion.FechaPedidoReal = real.FechaPedido.Date;
                resolucion.FechaPedidoRealDeEnvios = real.DeEnvios;
                resolucion.ErrorPedidoDias = ErrorDias(real.FechaPedido, foto.FechaPedidoPrevista);
                if (real.FechaAlbaran.HasValue)
                {
                    resolucion.FechaAlbaranReal = real.FechaAlbaran.Value.Date;
                    resolucion.ErrorLlegadaDias = ErrorDias(real.FechaAlbaran.Value, foto.FechaLlegadaOrientativa);
                    resolucion.ResueltaEl = ahora;
                }
            }
            resolucion.Caducada = Caduca(foto.DiaFoto, ahora, resolucion.FechaAlbaranReal.HasValue);
            resolucion.Cambia = resolucion.ResueltaEl.HasValue || resolucion.Caducada
                || !string.Equals(resolucion.EmpresaPedidoReal, foto.EmpresaPedidoReal?.Trim(), StringComparison.OrdinalIgnoreCase)
                || resolucion.PedidoReal != foto.PedidoReal
                || resolucion.FechaPedidoReal != foto.FechaPedidoReal?.Date
                || resolucion.FechaAlbaranReal != foto.FechaAlbaranReal?.Date;
            return resolucion;
        }

        /// <summary>Estadísticas de un grupo (medias con 2 decimales; medianas por rango más cercano, como el resto de plazos).</summary>
        public static EstadisticasFechasOrientativas Estadisticas(IEnumerable<FilaErrorOrientativa> filas)
        {
            List<FilaErrorOrientativa> lista = (filas ?? Enumerable.Empty<FilaErrorOrientativa>()).Where(f => f != null).ToList();
            if (!lista.Any())
            {
                return new EstadisticasFechasOrientativas { Casos = 0 };
            }
            List<int> pedido = lista.Select(f => f.ErrorPedidoDias).ToList();
            List<int> llegada = lista.Select(f => f.ErrorLlegadaDias).ToList();
            return new EstadisticasFechasOrientativas
            {
                Casos = lista.Count,
                ErrorPedidoMedio = Math.Round((decimal)pedido.Average(), 2, MidpointRounding.AwayFromZero),
                ErrorPedidoMediano = ReglasFotosFechaEntrega.Percentil(pedido, 0.5),
                ErrorLlegadaMedio = Math.Round((decimal)llegada.Average(), 2, MidpointRounding.AwayFromZero),
                ErrorLlegadaMediano = ReglasFotosFechaEntrega.Percentil(llegada, 0.5),
                PorcentajeCorta = Math.Round(100m * llegada.Count(e => e > 0) / lista.Count, 2, MidpointRounding.AwayFromZero)
            };
        }

        private static ResumenProveedorFechasOrientativas ResumenDe(string proveedor, List<FilaErrorOrientativa> filas) => new ResumenProveedorFechasOrientativas
        {
            Proveedor = proveedor,
            Todas = Estadisticas(filas),
            YaTocabaPedir = Estadisticas(filas.Where(f => f.YaTocabaPedir)),
            NoTocabaPedir = Estadisticas(filas.Where(f => !f.YaTocabaPedir))
        };

        /// <summary>El resumen por proveedor (de más a menos casos) y los totales, de las primeras fotos de cada episodio ya resueltas.</summary>
        public static ResumenFechasOrientativasDTO Resumir(IEnumerable<FilaErrorOrientativa> filas, int meses, DateTime desde, int abiertas, int caducadas)
        {
            List<FilaErrorOrientativa> lista = (filas ?? Enumerable.Empty<FilaErrorOrientativa>()).Where(f => f != null).ToList();
            return new ResumenFechasOrientativasDTO
            {
                Meses = meses,
                Desde = desde.Date,
                Abiertas = abiertas,
                Caducadas = caducadas,
                Totales = ResumenDe(null, lista),
                Proveedores = lista.GroupBy(f => f.Proveedor?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(g => ResumenDe(g.Key, g.ToList()))
                    .OrderByDescending(r => r.Todas.Casos)
                    .ThenBy(r => r.Proveedor, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }
    }
}
