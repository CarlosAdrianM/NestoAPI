using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega
{
    /// <summary>
    /// Fechas de entrega fiables (corte 1, en sombra, 11/11/26): lo que se prometía de un producto de un pedido pendiente que NO sale
    /// del stock de Algete (tienda, reposición en camino, proveedor o sin fecha) el día de la foto. Una fila por pedido, producto y día
    /// en que la promesa CAMBIA (no una fila diaria idéntica). Se guarda en dbo.FechasEntregaFotos (fuera del EDMX).
    /// </summary>
    public class FotoFechaEntrega
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public DateTime FechaFoto { get; set; }
        public int Unidades { get; set; }
        /// <summary>El día en que puede salir TODO lo del producto (la partida más tardía). Null = alguna partida sin fecha.</summary>
        public DateTime? FechaSalida { get; set; }
        /// <summary>El origen de la partida que manda (<see cref="PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR"/>…).</summary>
        public string Origen { get; set; }
        /// <summary>
        /// En cuál de los casos de Carlos (11/11/26) estaba lo que manda: <see cref="CasosFotoFechaEntrega"/>. Es lo que habrá que comparar
        /// para saber qué caso predice peor.
        /// </summary>
        public string Caso { get; set; }
        public int? PedidoProveedor { get; set; }
        public DateTime? FechaPrevistaProveedor { get; set; }
        /// <summary>La prevista del proveedor ya había pasado y se suponía que llegaba el laborable siguiente.</summary>
        public bool PrevistaVencida { get; set; }
        /// <summary>La fecha de entrega a la agencia del pedido entero que se calculaba ese día (la que ve el vendedor).</summary>
        public DateTime? FechaEntregaAgencia { get; set; }
        /// <summary>CabPedidoVta.FechaEntregaAgenciaPrometida (la de la creación, NestoAPI#606).</summary>
        public DateTime? FechaPrometidaAlCrear { get; set; }
    }

    /// <summary>
    /// Los casos de lo que falta (Carlos, 11/11/26): 1) SinPedir, aún no hay pedido al proveedor; 2) PedidoEnviado, pedido enviado sin
    /// fecha fiable; 3) EnTransito, el proveedor ya lo ha enviado con seguimiento de la agencia (hoy la BD no lo sabe: no se asigna
    /// nunca, queda reservado para el corte que lea el seguimiento). Interno: sale de una tienda o de una reposición en camino.
    /// </summary>
    public static class CasosFotoFechaEntrega
    {
        public const string INTERNO = "Interno";
        public const string SIN_PEDIR = "SinPedir";
        public const string PEDIDO_ENVIADO = "PedidoEnviado";
        public const string EN_TRANSITO = "EnTransito";

        public static string De(string origen)
        {
            switch (origen)
            {
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA:
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO:
                    return INTERNO;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR:
                    return PEDIDO_ENVIADO;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK:
                    return SIN_PEDIR;
                default:
                    return null;
            }
        }
    }

    public enum AccionFotoFechaEntrega
    {
        Nada,
        Insertar,
        /// <summary>Ya hay foto de hoy y la promesa ha cambiado: se sustituye (idempotente si el job se relanza el mismo día).</summary>
        Sustituir
    }

    /// <summary>Una fila de dbo.vFechasEntregaError: un producto de un pedido ya albaraneado entero, con su primera y su última foto.</summary>
    public class ErrorFechaEntregaFila
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public string OrigenPrimera { get; set; }
        public string ProveedorPrimera { get; set; }
        public DateTime? FechaSalidaPrimera { get; set; }
        public string OrigenUltima { get; set; }
        public string ProveedorUltima { get; set; }
        public DateTime? FechaSalidaUltima { get; set; }
        public int Fotos { get; set; }
        public DateTime FechaAlbaran { get; set; }
        public int? ErrorPrimeraDias { get; set; }
        public int? ErrorUltimaDias { get; set; }
    }

    /// <summary>Resumen del error (real − prometida, en días) por origen y proveedor de la PRIMERA promesa medida.</summary>
    public class ResumenErrorFechaEntregaDTO
    {
        public string Origen { get; set; }
        /// <summary><see cref="CasosFotoFechaEntrega"/> del origen.</summary>
        public string Caso { get; set; }
        public string Proveedor { get; set; }
        public int Lineas { get; set; }
        /// <summary>Las que no tenían fecha en la primera foto (no entran en el error).</summary>
        public int SinFecha { get; set; }
        public int? ErrorMediano { get; set; }
        public int? ErrorP80 { get; set; }
        /// <summary>Porcentaje de las fechadas que salieron DESPUÉS de lo prometido.</summary>
        public int? PorcentajeTarde { get; set; }
        public int? PorcentajeAntes { get; set; }
        /// <summary>Mediana del error de la última foto antes de albaranear (lo bien que se corrige sobre la marcha).</summary>
        public int? ErrorMedianoUltima { get; set; }
        /// <summary>Media de fotos por línea (cuántas veces cambió la promesa).</summary>
        public decimal FotosPorLinea { get; set; }
    }

    public static class ReglasFotosFechaEntrega
    {
        /// <summary>Lo que sale del stock de Algete: no es lo que se quiere medir aquí.</summary>
        internal static readonly string[] ORIGENES_CON_STOCK =
        {
            PartidaFechaEntregaAgenciaDTO.ORIGEN_PICKING,
            PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE,
            PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT
        };

        /// <summary>Las fotos de un pedido a partir del cálculo de su fecha de entrega: una por producto que no sale del stock.</summary>
        public static List<FotoFechaEntrega> Construir(string empresa, int pedido, FechaEntregaAgenciaDTO calculo, DateTime hoy)
        {
            var fotos = new List<FotoFechaEntrega>();
            if (calculo?.Desglose == null)
            {
                return fotos;
            }
            foreach (IGrouping<string, PartidaFechaEntregaAgenciaDTO> grupo in calculo.Desglose
                .Where(p => !string.IsNullOrWhiteSpace(p?.Producto))
                .GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                List<PartidaFechaEntregaAgenciaDTO> sinStock = grupo.Where(p => !ORIGENES_CON_STOCK.Contains(p.Origen)).ToList();
                if (!sinStock.Any())
                {
                    continue;
                }
                PartidaFechaEntregaAgenciaDTO manda = QuienManda(grupo.ToList());
                PartidaFechaEntregaAgenciaDTO proveedor = sinStock.FirstOrDefault(p => p.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR);
                // Las fechas centinela (31/12/2099 «sin determinar», 2100…) son «sin fecha», no una fecha con la que medir
                bool algunaSinFecha = grupo.Any(p => SinFecha(p.PuedeSalirEl, hoy) == null);
                fotos.Add(new FotoFechaEntrega
                {
                    Empresa = empresa?.Trim(),
                    Pedido = pedido,
                    Producto = grupo.Key,
                    FechaFoto = hoy.Date,
                    Unidades = grupo.Sum(p => p.Unidades),
                    FechaSalida = algunaSinFecha ? (DateTime?)null : grupo.Max(p => p.PuedeSalirEl.Value).Date,
                    Origen = manda.Origen,
                    Caso = CasosFotoFechaEntrega.De(manda.Origen),
                    PedidoProveedor = proveedor?.PedidoProveedor,
                    FechaPrevistaProveedor = SinFecha(proveedor?.FechaPrevistaProveedor, hoy),
                    PrevistaVencida = proveedor?.LlegadaSupuesta != null,
                    FechaEntregaAgencia = SinFecha(calculo.FechaEntregaAgencia, hoy),
                    FechaPrometidaAlCrear = SinFecha(calculo.FechaPrometida, hoy)
                });
            }
            return fotos;
        }

        /// <summary>
        /// La fecha (sin hora), o null si es una fecha centinela: un año disparatado para el calendario
        /// (<see cref="global::NestoAPI.Models.RecursosHumanos.CalendarioFestivos.EsAnioDisparatado"/>: 2099, 2100…).
        /// </summary>
        internal static DateTime? SinFecha(DateTime? fecha, DateTime hoy) =>
            fecha.HasValue && !global::NestoAPI.Models.RecursosHumanos.CalendarioFestivos.EsAnioDisparatado(fecha.Value.Year, hoy.Year) ? fecha.Value.Date : (DateTime?)null;

        /// <summary>La partida que marca la fecha: la que no tiene fecha y, si todas la tienen, la más tardía (a igualdad, la de origen más lejano).</summary>
        internal static PartidaFechaEntregaAgenciaDTO QuienManda(List<PartidaFechaEntregaAgenciaDTO> partidas)
        {
            return partidas
                .OrderBy(p => p.PuedeSalirEl.HasValue ? 1 : 0)
                .ThenByDescending(p => p.PuedeSalirEl ?? DateTime.MaxValue)
                .ThenByDescending(p => Array.IndexOf(PartidaFechaEntregaAgenciaDTO.ORDEN_ORIGENES, p.Origen))
                .First();
        }

        /// <summary>¿Es la misma promesa? Lo que cuenta es lo que se promete y de qué depende, no las unidades.</summary>
        public static bool MismaPromesa(FotoFechaEntrega a, FotoFechaEntrega b)
        {
            return a.FechaSalida == b.FechaSalida
                && string.Equals(a.Origen, b.Origen, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Caso, b.Caso, StringComparison.OrdinalIgnoreCase)
                && a.PedidoProveedor == b.PedidoProveedor
                && a.FechaPrevistaProveedor == b.FechaPrevistaProveedor
                && a.FechaEntregaAgencia == b.FechaEntregaAgencia;
        }

        public static AccionFotoFechaEntrega Decidir(FotoFechaEntrega ultima, FotoFechaEntrega nueva)
        {
            if (ultima == null)
            {
                return AccionFotoFechaEntrega.Insertar;
            }
            if (MismaPromesa(ultima, nueva))
            {
                return AccionFotoFechaEntrega.Nada;
            }
            return ultima.FechaFoto.Date == nueva.FechaFoto.Date ? AccionFotoFechaEntrega.Sustituir : AccionFotoFechaEntrega.Insertar;
        }

        /// <summary>El percentil por rango más cercano (sin PERCENTILE_CONT: NV está en compatibilidad 100).</summary>
        internal static int? Percentil(List<int> valores, double p)
        {
            if (valores == null || valores.Count == 0)
            {
                return null;
            }
            List<int> ordenados = valores.OrderBy(v => v).ToList();
            int rango = (int)Math.Ceiling(p * ordenados.Count);
            return ordenados[Math.Max(1, Math.Min(rango, ordenados.Count)) - 1];
        }

        public static List<ResumenErrorFechaEntregaDTO> Resumir(IEnumerable<ErrorFechaEntregaFila> filas)
        {
            return (filas ?? Enumerable.Empty<ErrorFechaEntregaFila>())
                .GroupBy(f => new { Origen = f.OrigenPrimera?.Trim(), Proveedor = f.ProveedorPrimera?.Trim() })
                .Select(g =>
                {
                    List<int> errores = g.Where(f => f.ErrorPrimeraDias.HasValue).Select(f => f.ErrorPrimeraDias.Value).ToList();
                    List<int> ultimas = g.Where(f => f.ErrorUltimaDias.HasValue).Select(f => f.ErrorUltimaDias.Value).ToList();
                    return new ResumenErrorFechaEntregaDTO
                    {
                        Origen = g.Key.Origen,
                        Caso = CasosFotoFechaEntrega.De(g.Key.Origen),
                        Proveedor = g.Key.Proveedor,
                        Lineas = g.Count(),
                        SinFecha = g.Count(f => !f.ErrorPrimeraDias.HasValue),
                        ErrorMediano = Percentil(errores, 0.5),
                        ErrorP80 = Percentil(errores, 0.8),
                        PorcentajeTarde = errores.Count == 0 ? (int?)null : (int)Math.Round(100.0 * errores.Count(e => e > 0) / errores.Count, MidpointRounding.AwayFromZero),
                        PorcentajeAntes = errores.Count == 0 ? (int?)null : (int)Math.Round(100.0 * errores.Count(e => e < 0) / errores.Count, MidpointRounding.AwayFromZero),
                        ErrorMedianoUltima = Percentil(ultimas, 0.5),
                        FotosPorLinea = Math.Round((decimal)g.Average(f => f.Fotos), 2, MidpointRounding.AwayFromZero)
                    };
                })
                .OrderByDescending(r => r.Lineas)
                .ThenBy(r => r.Origen)
                .ThenBy(r => r.Proveedor)
                .ToList();
        }
    }
}
