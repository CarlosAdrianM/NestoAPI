using System;
using System.IO;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#711: el vídeo de una novedad. Solo MP4, hasta 10 MB, y como mucho UNO por versión y ámbito
    /// (Carlos, 27/10/26: «no pondría más de uno por versión, porque van a decir que les hacemos perder mucho
    /// tiempo»). La regla de uno por versión la hace cumplir el servidor (aquí el mensaje y, en
    /// <see cref="ServicioVideosNovedades.GuardarVideo"/>, la comprobación atómica).
    /// </summary>
    public static class ReglasVideoNovedades
    {
        public const int TAMANO_MAXIMO = 10 * 1024 * 1024;
        public const string TIPO_MP4 = "video/mp4";
        public const string EXTENSION_MP4 = ".mp4";
        public const string CAMPO_FICHERO = "fichero";
        /// <summary>Dentro del contenedor de las fotos de los bultos: no hace falta crear nada en Azure.</summary>
        public const string PREFIJO_BLOB = "novedades-videos";

        public const string MENSAJE_SIN_COLUMNA =
            "Todavía no se pueden poner vídeos en las novedades: falta lanzar Scripts/Issue711_NovedadesVideo.sql.";

        public const string MENSAJE_SIN_VERSION =
            "Solo se puede poner un vídeo a una novedad con versión (es como mucho uno por versión).";

        /// <summary>
        /// Valida el fichero. Devuelve null si vale; si no, el error y si es por el tipo (415) o por otra cosa (400).
        /// Si el cliente no informa el tipo (o manda application/octet-stream), manda la extensión .mp4 y,
        /// si tampoco hay nombre, el contenido. En cualquier caso el contenido tiene que parecer un MP4.
        /// </summary>
        public static (string Error, bool TipoNoPermitido) Validar(string nombre, string tipo, byte[] contenido)
        {
            string tipoNormalizado = ReglasAdjuntosNovedades.NormalizarTipo(tipo);
            string extension = string.IsNullOrWhiteSpace(nombre) ? null : Path.GetExtension(ReglasAdjuntosNovedades.NormalizarNombre(nombre) ?? string.Empty);
            if (tipoNormalizado != null && tipoNormalizado != TIPO_MP4)
            {
                return ($"Solo se admiten vídeos MP4 (video/mp4), no {tipoNormalizado}.", true);
            }
            if (tipoNormalizado == null && !string.IsNullOrEmpty(extension) && !string.Equals(extension, EXTENSION_MP4, StringComparison.OrdinalIgnoreCase))
            {
                return ($"Solo se admiten vídeos MP4, no ficheros {extension}.", true);
            }
            if (contenido == null || contenido.Length == 0)
            {
                return ("No ha llegado ningún vídeo (está vacío).", false);
            }
            if (contenido.Length > TAMANO_MAXIMO)
            {
                return ($"El vídeo pesa demasiado ({contenido.Length / (1024.0 * 1024):0.#} MB; el máximo es {TAMANO_MAXIMO / (1024 * 1024)} MB).", false);
            }
            if (!PareceMp4(contenido))
            {
                return ("El fichero no es un vídeo MP4 válido.", true);
            }
            return (null, false);
        }

        /// <summary>
        /// Un MP4 (ISO BMFF) empieza por una caja «ftyp»: 4 bytes de tamaño y después las letras f t y p.
        /// </summary>
        public static bool PareceMp4(byte[] contenido)
        {
            return contenido != null && contenido.Length >= 12
                && contenido[4] == (byte)'f' && contenido[5] == (byte)'t' && contenido[6] == (byte)'y' && contenido[7] == (byte)'p';
        }

        /// <summary>
        /// La ruta del blob. Un nombre nuevo en cada subida: al sustituir el vídeo, la caché y quien esté viendo el
        /// anterior no se mezclan con el nuevo.
        /// </summary>
        public static string RutaDelBlob(int novedadId, Guid id) => $"{PREFIJO_BLOB}/{novedadId}/{id:N}.mp4";

        /// <summary>El 409 cuando otra novedad de la misma versión y ámbito ya tiene vídeo.</summary>
        public static string MensajeYaHayVideo(string version, int otraNovedadId, string otroTitulo)
        {
            string titulo = string.IsNullOrWhiteSpace(otroTitulo) ? string.Empty : $" («{otroTitulo.Trim()}»)";
            return $"La versión {version?.Trim()} ya tiene un vídeo, en la novedad {otraNovedadId}{titulo}. " +
                   "Como mucho se pone un vídeo por versión: quítelo de esa novedad antes de poner otro.";
        }
    }
}
