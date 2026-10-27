using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.Anomalias
{
    /// <summary>NestoAPI#715: GET api/PedidosVenta/Anomalias/Informe. Recuentos y precisión por regla en un rango de fechas de grabación.</summary>
    public class InformeAnomaliasDTO
    {
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        /// <summary>Grabaciones registradas (filas de contexto: una por POST o PUT).</summary>
        public int Grabaciones { get; set; }
        /// <summary>Pedidos distintos registrados.</summary>
        public int Pedidos { get; set; }
        /// <summary>Pedidos distintos con alguna señal (sin contar SinStock).</summary>
        public int PedidosConSenal { get; set; }
        public int Senales { get; set; }
        /// <summary>De las grabaciones ya etiquetadas, qué parte se corrigió en 48 h (la tasa base con la que comparar la precisión de cada regla).</summary>
        public decimal? TasaBaseCorreccion { get; set; }
        public int GrabacionesEtiquetadas { get; set; }
        public List<InformeReglaAnomaliaDTO> Reglas { get; set; } = new List<InformeReglaAnomaliaDTO>();
        public List<InformeAplicacionAnomaliaDTO> Aplicaciones { get; set; } = new List<InformeAplicacionAnomaliaDTO>();
        public string Modo { get; set; }
        public bool TablaExiste { get; set; } = true;
    }

    public class InformeReglaAnomaliaDTO
    {
        public string Regla { get; set; }
        public int Senales { get; set; }
        public int Pedidos { get; set; }
        public int Etiquetadas { get; set; }
        public int CorregidasEn48h { get; set; }
        public int Abonadas { get; set; }
        public int SinCambios { get; set; }
        /// <summary>(Corregidas + Abonadas) / Etiquetadas. Null sin etiquetadas.</summary>
        public decimal? Precision { get; set; }
        public decimal? PuntuacionMedia { get; set; }
    }

    public class InformeAplicacionAnomaliaDTO
    {
        public string Aplicacion { get; set; }
        public int Grabaciones { get; set; }
        public int GrabacionesConSenal { get; set; }
        public int Senales { get; set; }
    }

    /// <summary>NestoAPI#715: la agregación del informe. Pura.</summary>
    public static class InformeAnomalias
    {
        public const int DIAS_POR_DEFECTO = 30;

        public static InformeAnomaliasDTO Calcular(IEnumerable<FilaAnomaliaPedido> filas, DateTime desde, DateTime hasta)
        {
            List<FilaAnomaliaPedido> todas = (filas ?? Enumerable.Empty<FilaAnomaliaPedido>()).Where(f => f != null).ToList();
            List<FilaAnomaliaPedido> contexto = todas.Where(f => f.Regla == ReglasAnomalia.CONTEXTO).ToList();
            List<FilaAnomaliaPedido> senales = todas.Where(f => f.Regla != ReglasAnomalia.CONTEXTO).ToList();
            List<FilaAnomaliaPedido> senalesSinStock = senales.Where(f => f.Regla != ReglasAnomalia.SIN_STOCK).ToList();
            List<FilaAnomaliaPedido> contextoEtiquetado = contexto.Where(f => f.Resultado != null).ToList();

            var informe = new InformeAnomaliasDTO
            {
                Desde = desde,
                Hasta = hasta,
                Grabaciones = contexto.Count,
                Pedidos = todas.Select(f => Clave(f)).Distinct().Count(),
                PedidosConSenal = senalesSinStock.Select(f => Clave(f)).Distinct().Count(),
                Senales = senales.Count,
                GrabacionesEtiquetadas = contextoEtiquetado.Count,
                TasaBaseCorreccion = contextoEtiquetado.Any()
                    ? Redondear((decimal)contextoEtiquetado.Count(f => f.Resultado == ResultadosAnomalia.CORREGIDO_48H) / contextoEtiquetado.Count)
                    : (decimal?)null
            };

            foreach (IGrouping<string, FilaAnomaliaPedido> grupo in senales.GroupBy(f => f.Regla ?? string.Empty).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            {
                List<FilaAnomaliaPedido> etiquetadas = grupo.Where(f => f.Resultado != null).ToList();
                int corregidas = etiquetadas.Count(f => f.Resultado == ResultadosAnomalia.CORREGIDO_48H);
                int abonadas = etiquetadas.Count(f => f.Resultado == ResultadosAnomalia.ABONADO);
                informe.Reglas.Add(new InformeReglaAnomaliaDTO
                {
                    Regla = grupo.Key,
                    Senales = grupo.Count(),
                    Pedidos = grupo.Select(f => Clave(f)).Distinct().Count(),
                    Etiquetadas = etiquetadas.Count,
                    CorregidasEn48h = corregidas,
                    Abonadas = abonadas,
                    SinCambios = etiquetadas.Count(f => f.Resultado == ResultadosAnomalia.SIN_CAMBIOS),
                    Precision = etiquetadas.Any() ? Redondear((decimal)(corregidas + abonadas) / etiquetadas.Count) : (decimal?)null,
                    PuntuacionMedia = grupo.Any(f => f.Puntuacion.HasValue) ? Redondear(grupo.Where(f => f.Puntuacion.HasValue).Average(f => f.Puntuacion.Value)) : (decimal?)null
                });
            }

            // Por aplicación: las grabaciones (filas de contexto) y cuántas de ellas tienen alguna señal
            HashSet<string> grabacionesConSenal = new HashSet<string>(senalesSinStock.Select(f => ClaveGrabacion(f)));
            foreach (IGrouping<string, FilaAnomaliaPedido> grupo in contexto.GroupBy(f => f.Aplicacion ?? AplicacionesOrigen.DESCONOCIDA).OrderByDescending(g => g.Count()))
            {
                informe.Aplicaciones.Add(new InformeAplicacionAnomaliaDTO
                {
                    Aplicacion = grupo.Key,
                    Grabaciones = grupo.Count(),
                    GrabacionesConSenal = grupo.Count(f => grabacionesConSenal.Contains(ClaveGrabacion(f))),
                    Senales = senalesSinStock.Count(f => (f.Aplicacion ?? AplicacionesOrigen.DESCONOCIDA) == grupo.Key)
                });
            }
            return informe;
        }

        private static string Clave(FilaAnomaliaPedido f) => (f.Empresa?.Trim() ?? string.Empty) + "/" + f.Pedido;

        /// <summary>Una grabación = las filas que salieron del mismo POST/PUT (mismo pedido y misma fecha).</summary>
        private static string ClaveGrabacion(FilaAnomaliaPedido f) => Clave(f) + "@" + f.Fecha.ToString("yyyyMMddHHmmssfff");

        private static decimal Redondear(decimal valor) => Math.Round(valor, 4);
    }
}
