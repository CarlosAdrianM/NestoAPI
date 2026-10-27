using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Abandono
{
    /// <summary>Precisión de la regla por vendedor y mes de la instantánea (GET api/Clientes/RiesgoAbandono/Informe).</summary>
    public class FilaInformeRiesgoAbandonoDTO
    {
        public string Vendedor { get; set; }
        /// <summary>AAAA-MM del día de la instantánea.</summary>
        public string Mes { get; set; }
        /// <summary>Alertas (primer día que la regla marcó al cliente) en el mes.</summary>
        public int Alertas { get; set; }
        /// <summary>Alertas con el horizonte vencido y etiquetadas.</summary>
        public int Resueltas { get; set; }
        public int Abandonos { get; set; }
        public int Bajas { get; set; }
        public int Compraron { get; set; }
        public int Desconocidos { get; set; }
        public int Pendientes { get; set; }
        /// <summary>(Abandonos + Bajas) / (Resueltas − Desconocidos); null sin resueltas conocidas.</summary>
        public double? Precision { get; set; }
        /// <summary>Compradores regulares en las instantáneas semanales del mes (filas Instantanea), y cuántos marcaba la regla.</summary>
        public int RegularesInstantaneas { get; set; }
        public int RegularesMarcados { get; set; }
    }

    public class InformeRiesgoAbandonoDTO
    {
        public DateTime Desde { get; set; }
        /// <summary>Último día incluido.</summary>
        public DateTime Hasta { get; set; }
        public string Modo { get; set; }
        public string VersionRegla { get; set; }
        public FilaInformeRiesgoAbandonoDTO Totales { get; set; }
        public List<FilaInformeRiesgoAbandonoDTO> PorVendedorYMes { get; set; } = new List<FilaInformeRiesgoAbandonoDTO>();
    }

    /// <summary>NestoAPI#714: el informe como función pura sobre las filas leídas.</summary>
    public static class InformeRiesgoAbandono
    {
        public const int DIAS_POR_DEFECTO = 90;

        public static InformeRiesgoAbandonoDTO Calcular(List<FilaInformeAbandono> filas, DateTime desde, DateTime hastaExclusivo, ModoAbandono modo)
        {
            filas = filas ?? new List<FilaInformeAbandono>();
            var informe = new InformeRiesgoAbandonoDTO
            {
                Desde = desde.Date,
                Hasta = hastaExclusivo.Date.AddDays(-1),
                Modo = modo.ToString(),
                VersionRegla = CalculadoraAbandono.VERSION_REGLA
            };
            informe.PorVendedorYMes = filas
                .GroupBy(f => new { Vendedor = (f.Vendedor ?? "").Trim(), Mes = f.FechaInstantanea.ToString("yyyy-MM") })
                .OrderBy(g => g.Key.Vendedor).ThenBy(g => g.Key.Mes)
                .Select(g => Agregar(g.ToList(), g.Key.Vendedor, g.Key.Mes))
                .ToList();
            informe.Totales = Agregar(filas, null, null);
            return informe;
        }

        internal static FilaInformeRiesgoAbandonoDTO Agregar(List<FilaInformeAbandono> filas, string vendedor, string mes)
        {
            List<FilaInformeAbandono> alertas = filas.Where(f => f.Tipo == TiposFilaAbandono.ALERTA).ToList();
            List<FilaInformeAbandono> resueltas = alertas.Where(f => f.Resuelto.HasValue).ToList();
            var fila = new FilaInformeRiesgoAbandonoDTO
            {
                Vendedor = vendedor,
                Mes = mes,
                Alertas = alertas.Count,
                Resueltas = resueltas.Count,
                Abandonos = resueltas.Count(f => f.Resultado == ResultadoAbandono.ABANDONO),
                Bajas = resueltas.Count(f => f.Resultado == ResultadoAbandono.BAJA),
                Compraron = resueltas.Count(f => f.Resultado == ResultadoAbandono.COMPRO),
                Desconocidos = resueltas.Count(f => f.Resultado == ResultadoAbandono.DESCONOCIDO),
                Pendientes = alertas.Count - resueltas.Count,
                RegularesInstantaneas = filas.Count(f => f.Tipo == TiposFilaAbandono.INSTANTANEA),
                RegularesMarcados = filas.Count(f => f.Tipo == TiposFilaAbandono.INSTANTANEA && f.Baseline)
            };
            int conocidas = fila.Resueltas - fila.Desconocidos;
            fila.Precision = conocidas == 0 ? (double?)null : Math.Round((double)(fila.Abandonos + fila.Bajas) / conocidas, 4);
            return fila;
        }
    }
}
