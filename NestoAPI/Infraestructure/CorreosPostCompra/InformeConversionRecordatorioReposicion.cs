using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#736: una fila de RecordatoriosReposicionEnvios (Enviado o Control) con las líneas de pedido de ese producto que
    /// ese cliente hizo en los N días siguientes. Lo que lee <see cref="RepositorioRecordatorioReposicionSql.LeerConversion"/>.
    /// </summary>
    public class FilaConversionReposicion
    {
        public DateTime Fecha { get; set; }
        public string Cliente { get; set; }
        public string Producto { get; set; }
        /// <summary>Enviado | Control.</summary>
        public string Grupo { get; set; }
        /// <summary>La última compra que tenía cuando se le recordó.</summary>
        public DateTime UltimaCompra { get; set; }
        public int IntervaloDias { get; set; }
        /// <summary>Null si se registró sin la puerta del modelo (antes de #736 o sin el script).</summary>
        public decimal? ProbabilidadModelo { get; set; }
        public bool? FueraPorModelo { get; set; }
        /// <summary>Líneas de pedido de ese producto en la ventana (0 = no lo ha vuelto a pedir).</summary>
        public int Lineas { get; set; }
    }

    /// <summary>NestoAPI#736: la conversión de un tramo, Enviado frente a Control.</summary>
    public class ConversionReposicionTramoDTO
    {
        /// <summary>«Total», «Antigüedad», «Intervalo» o «P1».</summary>
        public string Dimension { get; set; }
        public string Tramo { get; set; }
        public int EnviadoProductos { get; set; }
        public int EnviadoRecomprados { get; set; }
        /// <summary>% de productos recordados que el cliente volvió a pedir en la ventana (un decimal). Null sin productos.</summary>
        public decimal? EnviadoPorcentaje { get; set; }
        public int ControlProductos { get; set; }
        public int ControlRecomprados { get; set; }
        public decimal? ControlPorcentaje { get; set; }
        /// <summary>Enviado − Control, en puntos. Null si falta alguno de los dos.</summary>
        public decimal? DiferenciaPuntos { get; set; }
    }

    public class InformeConversionReposicionDTO
    {
        public DateTime Desde { get; set; }
        public int DiasVentana { get; set; }
        /// <summary>Envíos que cuentan: los de ventana cerrada (hace al menos <see cref="DiasVentana"/> días).</summary>
        public int EnviosCerrados { get; set; }
        /// <summary>Envíos del periodo cuya ventana sigue abierta: no cuentan todavía.</summary>
        public int EnviosEnCurso { get; set; }
        public ConversionReposicionTramoDTO Total { get; set; }
        /// <summary>Por días desde la última compra cuando se le recordó.</summary>
        public List<ConversionReposicionTramoDTO> PorAntiguedad { get; set; } = new List<ConversionReposicionTramoDTO>();
        /// <summary>Por su ritmo (mediana de días entre compras).</summary>
        public List<ConversionReposicionTramoDTO> PorIntervalo { get; set; } = new List<ConversionReposicionTramoDTO>();
        /// <summary>Por la P1 del modelo (solo si alguna fila la tiene).</summary>
        public List<ConversionReposicionTramoDTO> PorProbabilidad { get; set; } = new List<ConversionReposicionTramoDTO>();
    }

    /// <summary>
    /// NestoAPI#736: ¿convierte el recordatorio de reposición? De cada producto recordado (Enviado) o apuntado en el grupo de
    /// control (Control), si ese cliente volvió a pedir ese producto en los N días siguientes (LinPedidoVta, pedido con fecha
    /// desde el día del envío, sin presupuestos: como el informe de #707). Solo cuentan los envíos de ventana cerrada. Desglose
    /// por antigüedad de la última compra, por intervalo y por P1 del modelo. Solo calcula: la consulta está en el repositorio.
    /// </summary>
    public static class InformeConversionRecordatorioReposicion
    {
        public const int DIAS_VENTANA_POR_DEFECTO = 30;
        public const int DIAS_VENTANA_MAXIMO = 180;
        public const int MESES_POR_DEFECTO = 6;
        public const string DIMENSION_TOTAL = "Total";
        public const string DIMENSION_ANTIGUEDAD = "Antigüedad";
        public const string DIMENSION_INTERVALO = "Intervalo";
        public const string DIMENSION_P1 = "P1";
        public const string SIN_P1 = "Sin P1";

        /// <summary>Si la ventana de <paramref name="dias"/> días desde el envío ya ha terminado hoy.</summary>
        public static bool VentanaCerrada(DateTime fechaEnvio, int dias, DateTime hoy) => fechaEnvio.Date.AddDays(dias) <= hoy.Date;

        /// <summary>Días desde la última compra hasta el envío, en tramos.</summary>
        public static string TramoAntiguedad(int dias)
            => dias < 60 ? "< 60 días"
                : dias < 120 ? "60-119 días"
                : dias < 180 ? "120-179 días"
                : dias < 240 ? "180-239 días"
                : "240 días o más";

        public static string TramoIntervalo(int dias)
            => dias < 30 ? "< 30 días"
                : dias < 60 ? "30-59 días"
                : dias < 120 ? "60-119 días"
                : "120 días o más";

        public static string TramoProbabilidad(decimal? p1)
            => p1 == null ? SIN_P1
                : p1 < 0.05m ? "< 0,05"
                : p1 < 0.15m ? "0,05-0,15"
                : p1 < 0.30m ? "0,15-0,30"
                : "0,30 o más";

        private static readonly string[] ORDEN_ANTIGUEDAD = { "< 60 días", "60-119 días", "120-179 días", "180-239 días", "240 días o más" };
        private static readonly string[] ORDEN_INTERVALO = { "< 30 días", "30-59 días", "60-119 días", "120 días o más" };
        private static readonly string[] ORDEN_P1 = { "< 0,05", "0,05-0,15", "0,15-0,30", "0,30 o más", SIN_P1 };

        public static InformeConversionReposicionDTO Calcular(IEnumerable<FilaConversionReposicion> filas, DateTime desde, int dias, DateTime hoy)
        {
            List<FilaConversionReposicion> todas = (filas ?? Enumerable.Empty<FilaConversionReposicion>()).Where(f => f != null).ToList();
            List<FilaConversionReposicion> cerradas = todas.Where(f => VentanaCerrada(f.Fecha, dias, hoy)).ToList();
            var informe = new InformeConversionReposicionDTO
            {
                Desde = desde.Date,
                DiasVentana = dias,
                EnviosCerrados = cerradas.Count,
                EnviosEnCurso = todas.Count - cerradas.Count,
                Total = Tramo(DIMENSION_TOTAL, DIMENSION_TOTAL, cerradas),
                PorAntiguedad = Desglose(DIMENSION_ANTIGUEDAD, cerradas, f => TramoAntiguedad((int)(f.Fecha.Date - f.UltimaCompra.Date).TotalDays), ORDEN_ANTIGUEDAD),
                PorIntervalo = Desglose(DIMENSION_INTERVALO, cerradas, f => TramoIntervalo(f.IntervaloDias), ORDEN_INTERVALO)
            };
            if (cerradas.Any(f => f.ProbabilidadModelo != null))
            {
                informe.PorProbabilidad = Desglose(DIMENSION_P1, cerradas, f => TramoProbabilidad(f.ProbabilidadModelo), ORDEN_P1);
            }
            return informe;
        }

        private static List<ConversionReposicionTramoDTO> Desglose(string dimension, List<FilaConversionReposicion> filas,
            Func<FilaConversionReposicion, string> tramo, string[] orden)
            => filas.GroupBy(tramo)
                .Select(g => Tramo(dimension, g.Key, g.ToList()))
                .OrderBy(t => Array.IndexOf(orden, t.Tramo) < 0 ? int.MaxValue : Array.IndexOf(orden, t.Tramo))
                .ToList();

        private static ConversionReposicionTramoDTO Tramo(string dimension, string tramo, List<FilaConversionReposicion> filas)
        {
            List<FilaConversionReposicion> enviados = filas.Where(f => EsGrupo(f, RepositorioRecordatorioReposicionSql.GRUPO_ENVIADO)).ToList();
            List<FilaConversionReposicion> control = filas.Where(f => EsGrupo(f, RepositorioRecordatorioReposicionSql.GRUPO_CONTROL)).ToList();
            var resultado = new ConversionReposicionTramoDTO
            {
                Dimension = dimension,
                Tramo = tramo,
                EnviadoProductos = enviados.Count,
                EnviadoRecomprados = enviados.Count(f => f.Lineas > 0),
                ControlProductos = control.Count,
                ControlRecomprados = control.Count(f => f.Lineas > 0)
            };
            resultado.EnviadoPorcentaje = Porcentaje(resultado.EnviadoRecomprados, resultado.EnviadoProductos);
            resultado.ControlPorcentaje = Porcentaje(resultado.ControlRecomprados, resultado.ControlProductos);
            resultado.DiferenciaPuntos = resultado.EnviadoPorcentaje.HasValue && resultado.ControlPorcentaje.HasValue
                ? resultado.EnviadoPorcentaje - resultado.ControlPorcentaje
                : null;
            return resultado;
        }

        private static bool EsGrupo(FilaConversionReposicion fila, string grupo)
            => string.Equals(fila.Grupo?.Trim(), grupo, StringComparison.OrdinalIgnoreCase);

        private static decimal? Porcentaje(int parte, int total)
            => total == 0 ? (decimal?)null : Math.Round(100m * parte / total, 1, MidpointRounding.AwayFromZero);
    }
}
