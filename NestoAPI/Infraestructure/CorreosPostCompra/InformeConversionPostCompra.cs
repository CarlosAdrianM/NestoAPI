using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#707: una fila enviada de CorreosPostCompraRecomendaciones con lo que el cliente compró de ese producto en los
    /// <see cref="InformeConversionPostCompra.DIAS_VENTANA"/> días siguientes. Lo que lee
    /// <see cref="RepositorioRecomendacionesPostCompraSql.SQL_CONVERSION"/>.
    /// </summary>
    public class FilaConversionPostCompra
    {
        public DateTime FechaEnvio { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Producto { get; set; }
        public string Tipo { get; set; }
        public string Regla { get; set; }
        public bool GrupoControl { get; set; }
        /// <summary>Líneas de pedido de ese producto en la ventana (0 = no lo ha comprado).</summary>
        public int Lineas { get; set; }
        public int Unidades { get; set; }
        public decimal BaseImponible { get; set; }
    }

    /// <summary>NestoAPI#707: la conversión de un grupo (semana, tipo, regla y grupo control; o sin semana, en los totales).</summary>
    public class ConversionPostCompraGrupoDTO
    {
        /// <summary>Lunes de la semana del envío. Null en los totales.</summary>
        public DateTime? Semana { get; set; }
        public string Tipo { get; set; }
        public string Regla { get; set; }
        public bool GrupoControl { get; set; }
        /// <summary>Filas enviadas (productos enseñados; en el grupo control, los que se habrían enseñado).</summary>
        public int Recomendaciones { get; set; }
        public int Clientes { get; set; }
        /// <summary>Filas cuyo producto compró ese cliente en la ventana.</summary>
        public int Compradas { get; set; }
        /// <summary>Clientes que compraron al menos uno de sus productos de este grupo en la ventana.</summary>
        public int ClientesCompradores { get; set; }
        public int Unidades { get; set; }
        public decimal BaseImponible { get; set; }
        /// <summary>Compradas / Recomendaciones, en %, con un decimal. Null sin recomendaciones.</summary>
        public decimal? PorcentajeConversion { get; set; }
        /// <summary>
        /// Si ya han pasado los <see cref="InformeConversionPostCompra.DIAS_VENTANA"/> días desde todos los envíos del grupo: hasta
        /// entonces, la conversión todavía puede subir.
        /// </summary>
        public bool VentanaCerrada { get; set; }
    }

    public class InformeConversionPostCompraDTO
    {
        public DateTime Desde { get; set; }
        /// <summary>Exclusivo: envíos anteriores a esta fecha.</summary>
        public DateTime Hasta { get; set; }
        public int DiasVentana { get; set; } = InformeConversionPostCompra.DIAS_VENTANA;
        /// <summary>Por semana del envío, tipo, regla y grupo control.</summary>
        public List<ConversionPostCompraGrupoDTO> PorSemana { get; set; } = new List<ConversionPostCompraGrupoDTO>();
        /// <summary>Todo el periodo, por tipo, regla y grupo control (para comparar enviados con grupo control).</summary>
        public List<ConversionPostCompraGrupoDTO> Totales { get; set; } = new List<ConversionPostCompraGrupoDTO>();
    }

    /// <summary>
    /// NestoAPI#707 (corte 1): informe de conversión del correo post-compra (GET api/CorreosPostCompra/Conversion). De cada
    /// producto enviado, si ese cliente lo pidió en los 30 días siguientes (LinPedidoVta, misma empresa y cliente, pedido con
    /// fecha desde el día del envío, estado desde pendiente: sin presupuestos), con unidades y base imponible. Agrupa por
    /// semana, tipo (Comprado/Recomendado), regla y grupo control. Solo calcula: la consulta está en el repositorio.
    /// </summary>
    public static class InformeConversionPostCompra
    {
        public const int DIAS_VENTANA = 30;
        public const int SEMANAS_POR_DEFECTO = 12;
        /// <summary>Lo que se pone en Regla cuando la fila no la tiene (los comprados).</summary>
        public const string SIN_REGLA = "";

        /// <summary>El lunes de la semana de <paramref name="fecha"/>.</summary>
        public static DateTime Lunes(DateTime fecha)
        {
            int diasDesdeLunes = ((int)fecha.DayOfWeek + 6) % 7;
            return fecha.Date.AddDays(-diasDesdeLunes);
        }

        public static InformeConversionPostCompraDTO Calcular(IEnumerable<FilaConversionPostCompra> filas, DateTime desde, DateTime hasta, DateTime hoy)
        {
            List<FilaConversionPostCompra> lista = (filas ?? Enumerable.Empty<FilaConversionPostCompra>()).Where(f => f != null).ToList();
            return new InformeConversionPostCompraDTO
            {
                Desde = desde,
                Hasta = hasta,
                PorSemana = lista
                    .GroupBy(f => new { Semana = Lunes(f.FechaEnvio), Tipo = Limpio(f.Tipo), Regla = Limpio(f.Regla), f.GrupoControl })
                    .Select(g => Grupo(g, g.Key.Semana, g.Key.Tipo, g.Key.Regla, g.Key.GrupoControl, hoy))
                    .OrderBy(g => g.Semana)
                    .ThenBy(g => g.Tipo, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(g => g.Regla, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(g => g.GrupoControl)
                    .ToList(),
                Totales = lista
                    .GroupBy(f => new { Tipo = Limpio(f.Tipo), Regla = Limpio(f.Regla), f.GrupoControl })
                    .Select(g => Grupo(g, null, g.Key.Tipo, g.Key.Regla, g.Key.GrupoControl, hoy))
                    .OrderBy(g => g.Tipo, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(g => g.Regla, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(g => g.GrupoControl)
                    .ToList()
            };
        }

        private static ConversionPostCompraGrupoDTO Grupo(IEnumerable<FilaConversionPostCompra> filas, DateTime? semana, string tipo,
            string regla, bool grupoControl, DateTime hoy)
        {
            List<FilaConversionPostCompra> lista = filas.ToList();
            List<FilaConversionPostCompra> compradas = lista.Where(f => f.Lineas > 0).ToList();
            var grupo = new ConversionPostCompraGrupoDTO
            {
                Semana = semana,
                Tipo = tipo,
                Regla = regla,
                GrupoControl = grupoControl,
                Recomendaciones = lista.Count,
                Clientes = lista.Select(ClaveCliente).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Compradas = compradas.Count,
                ClientesCompradores = compradas.Select(ClaveCliente).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Unidades = compradas.Sum(f => f.Unidades),
                BaseImponible = compradas.Sum(f => f.BaseImponible),
                VentanaCerrada = lista.All(f => f.FechaEnvio.Date.AddDays(DIAS_VENTANA) <= hoy.Date)
            };
            grupo.PorcentajeConversion = grupo.Recomendaciones == 0
                ? (decimal?)null
                : Math.Round(100m * grupo.Compradas / grupo.Recomendaciones, 1, MidpointRounding.AwayFromZero);
            return grupo;
        }

        private static string ClaveCliente(FilaConversionPostCompra f) => (f.Empresa ?? string.Empty).Trim() + "|" + (f.Cliente ?? string.Empty).Trim();

        private static string Limpio(string valor) => valor?.Trim() ?? SIN_REGLA;
    }
}
