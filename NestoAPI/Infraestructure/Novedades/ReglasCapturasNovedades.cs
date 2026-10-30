using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>NestoAPI#754: a dónde van las capturas que llegan con una sugerencia o un comentario.</summary>
    public enum DestinoCapturasNovedad
    {
        /// <summary>No viene ninguna.</summary>
        Ninguna,
        /// <summary>Al blob privado y a la tabla NovedadesCapturas (lo normal).</summary>
        Blob,
        /// <summary>Como antes: la única captura, en la columna Imagen (sin tabla o sin almacenamiento).</summary>
        Columna,
        /// <summary>Vienen varias y aún no se pueden guardar (sin tabla o sin almacenamiento): se rechaza.</summary>
        NoDisponible
    }

    /// <summary>NestoAPI#754 (sugerencia 900): reglas puras de las capturas de Novedades.</summary>
    public static class ReglasCapturasNovedades
    {
        public const string PREFIJO_MIGRADAS = "migradas/";
        public const int MAXIMO_POR_PASADA = 100;
        public const int POR_PASADA_DEFECTO = 25;

        public const string ERROR_VARIAS_NO_DISPONIBLE =
            "Ahora mismo solo se puede enviar una captura. Quite las demás y vuelva a intentarlo, por favor.";
        public const string ERROR_NO_SE_HAN_GUARDADO =
            "No se han podido guardar las capturas. Inténtelo de nuevo dentro de un momento, por favor.";

        /// <summary>
        /// Con tabla y almacenamiento, todas al blob. Sin ellos, como antes: una sola en la columna; con varias, error
        /// (mejor decirlo que perder capturas en silencio).
        /// </summary>
        public static DestinoCapturasNovedad Destino(int numeroCapturas, bool blobDisponible)
        {
            if (numeroCapturas <= 0)
            {
                return DestinoCapturasNovedad.Ninguna;
            }
            if (blobDisponible)
            {
                return DestinoCapturasNovedad.Blob;
            }
            return numeroCapturas == 1 ? DestinoCapturasNovedad.Columna : DestinoCapturasNovedad.NoDisponible;
        }

        /// <summary>
        /// Ruta de una captura nueva: <c>2026/10/{guid}.png</c>. No depende del Id de la novedad, porque se sube ANTES
        /// de grabarla (si el blob falla, no se crea nada a medias).
        /// </summary>
        public static string RutaNueva(DateTime ahora, Guid guid, string tipo) =>
            ahora.ToString("yyyy'/'MM", CultureInfo.InvariantCulture) + "/" + guid.ToString("N") + Extension(tipo);

        /// <summary>
        /// Ruta fija de una imagen migrada (<c>migradas/novedad-7.png</c>, <c>migradas/comentario-12.jpg</c>): si una
        /// pasada se corta a medias, la siguiente sobrescribe el mismo blob y no registra dos veces la misma captura.
        /// </summary>
        public static string RutaMigrada(string origen, int id, string tipo) =>
            PREFIJO_MIGRADAS + (origen ?? string.Empty).ToLowerInvariant() + "-" + id.ToString(CultureInfo.InvariantCulture) + Extension(tipo);

        public static string Extension(string tipo) =>
            string.Equals(tipo, ReglasFeedbackNovedades.TIPO_JPEG, StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";

        /// <summary>El tipo de una imagen de antes: el de sus bytes; si no se reconoce, el guardado; si no, PNG.</summary>
        public static string TipoDeImagenGuardada(byte[] imagen, string tipoGuardado) =>
            ReglasFeedbackNovedades.TipoPorContenido(imagen)
            ?? (string.IsNullOrWhiteSpace(tipoGuardado) ? ReglasFeedbackNovedades.TIPO_PNG : tipoGuardado.Trim());

        /// <summary>Cuántas pasar en una pasada de la migración: por defecto 25, entre 1 y 100.</summary>
        public static int PorPasada(int? maximo) =>
            !maximo.HasValue ? POR_PASADA_DEFECTO : Math.Max(1, Math.Min(MAXIMO_POR_PASADA, maximo.Value));

        /// <summary>Las capturas de cada clave (novedad o comentario), en su orden.</summary>
        public static Dictionary<int, List<CapturaNovedadDTO>> Agrupar(IEnumerable<CapturaNovedadFila> filas, Func<CapturaNovedadFila, int> clave) =>
            (filas ?? Enumerable.Empty<CapturaNovedadFila>())
                .GroupBy(clave)
                .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Orden).ThenBy(f => f.Id).Select(f => f.ADto()).ToList());
    }
}
