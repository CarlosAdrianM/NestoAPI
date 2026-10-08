using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#616: qué ficheros se pueden colgar en una novedad. PDF e imágenes, hasta 10 MB cada uno.
    /// Se valida el tipo MIME Y la extensión (y que digan lo mismo). Si el cliente no manda tipo (o manda
    /// application/octet-stream, como HttpClient sin cabecera), manda la extensión.
    /// </summary>
    public static class ReglasAdjuntosNovedades
    {
        public const int TAMANO_MAXIMO = 10 * 1024 * 1024;
        public const int LONGITUD_MAXIMA_NOMBRE = 200;
        public const string CAMPO_FICHERO = "fichero";

        public const string TIPO_PDF = "application/pdf";
        public const string TIPO_PNG = "image/png";
        public const string TIPO_JPEG = "image/jpeg";
        public const string TIPO_GIF = "image/gif";
        public const string TIPO_WEBP = "image/webp";

        private static readonly Dictionary<string, string> TipoPorExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = TIPO_PDF,
            [".png"] = TIPO_PNG,
            [".jpg"] = TIPO_JPEG,
            [".jpeg"] = TIPO_JPEG,
            [".gif"] = TIPO_GIF,
            [".webp"] = TIPO_WEBP
        };

        // Variantes que mandan algunos navegadores y programas para el mismo tipo
        private static readonly Dictionary<string, string> AliasTipos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpg"] = TIPO_JPEG,
            ["image/pjpeg"] = TIPO_JPEG,
            ["application/x-pdf"] = TIPO_PDF
        };

        private static readonly HashSet<string> TiposSinInformar = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/octet-stream",
            "binary/octet-stream"
        };

        public const string TIPOS_PERMITIDOS_TEXTO = "PDF, PNG, JPEG, GIF o WebP";

        /// <summary>Valida un fichero. Devuelve el adjunto listo para grabar o, en el resultado, el error.</summary>
        public static ValidacionAdjuntosNovedad Validar(IEnumerable<(string Nombre, string Tipo, byte[] Contenido)> ficheros)
        {
            var resultado = new ValidacionAdjuntosNovedad();
            List<(string Nombre, string Tipo, byte[] Contenido)> lista = (ficheros ?? Enumerable.Empty<(string, string, byte[])>()).ToList();
            if (lista.Count == 0)
            {
                resultado.Error = $"No ha llegado ningún fichero. Envíalo como multipart/form-data en el campo «{CAMPO_FICHERO}».";
                return resultado;
            }
            foreach ((string nombreOriginal, string tipoOriginal, byte[] contenido) in lista)
            {
                string nombre = NormalizarNombre(nombreOriginal);
                if (string.IsNullOrEmpty(nombre))
                {
                    resultado.Error = "Falta el nombre del fichero.";
                    return Fallo(resultado);
                }
                string extension = Path.GetExtension(nombre);
                if (!TipoPorExtension.TryGetValue(extension ?? string.Empty, out string tipoPorExtension))
                {
                    resultado.Error = $"«{nombre}» no se puede adjuntar: solo se admiten ficheros {TIPOS_PERMITIDOS_TEXTO}.";
                    resultado.TipoNoPermitido = true;
                    return Fallo(resultado);
                }
                string tipo = NormalizarTipo(tipoOriginal);
                if (tipo == null)
                {
                    tipo = tipoPorExtension;
                }
                else if (!TipoPorExtension.ContainsValue(tipo))
                {
                    resultado.Error = $"«{nombre}» no se puede adjuntar: el tipo {tipo} no está permitido (solo {TIPOS_PERMITIDOS_TEXTO}).";
                    resultado.TipoNoPermitido = true;
                    return Fallo(resultado);
                }
                else if (tipo != tipoPorExtension)
                {
                    resultado.Error = $"«{nombre}»: la extensión {extension} no corresponde al tipo {tipo}.";
                    return Fallo(resultado);
                }
                if (contenido == null || contenido.Length == 0)
                {
                    resultado.Error = $"«{nombre}» está vacío.";
                    return Fallo(resultado);
                }
                if (contenido.Length > TAMANO_MAXIMO)
                {
                    resultado.Error = $"«{nombre}» pesa demasiado ({contenido.Length / (1024.0 * 1024):0.#} MB; el máximo es {TAMANO_MAXIMO / (1024 * 1024)} MB por fichero).";
                    return Fallo(resultado);
                }
                resultado.Adjuntos.Add(new AdjuntoNovedadAGrabar { Nombre = nombre, Tipo = tipo, Contenido = contenido });
            }
            return resultado;
        }

        private static ValidacionAdjuntosNovedad Fallo(ValidacionAdjuntosNovedad resultado)
        {
            // Todo o nada: si uno falla no se graba ninguno
            resultado.Adjuntos.Clear();
            return resultado;
        }

        /// <summary>
        /// Solo el nombre (sin ruta, que algunos navegadores mandan la completa), sin comillas y como
        /// mucho 200 caracteres, conservando la extensión.
        /// </summary>
        public static string NormalizarNombre(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }
            string limpio = nombre.Trim().Trim('"').Trim();
            int barra = Math.Max(limpio.LastIndexOf('\\'), limpio.LastIndexOf('/'));
            if (barra >= 0)
            {
                limpio = limpio.Substring(barra + 1);
            }
            limpio = new string(limpio.Where(c => !char.IsControl(c) && Path.GetInvalidFileNameChars().All(i => i != c)).ToArray()).Trim();
            if (limpio.Length == 0)
            {
                return null;
            }
            if (limpio.Length > LONGITUD_MAXIMA_NOMBRE)
            {
                string extension = Path.GetExtension(limpio) ?? string.Empty;
                if (extension.Length >= LONGITUD_MAXIMA_NOMBRE)
                {
                    extension = string.Empty;
                }
                limpio = limpio.Substring(0, LONGITUD_MAXIMA_NOMBRE - extension.Length).TrimEnd() + extension;
            }
            return limpio;
        }

        /// <summary>El tipo sin parámetros y en su forma canónica, o null si no viene informado.</summary>
        public static string NormalizarTipo(string tipo)
        {
            if (string.IsNullOrWhiteSpace(tipo))
            {
                return null;
            }
            string limpio = tipo.Split(';')[0].Trim().ToLowerInvariant();
            if (limpio.Length == 0 || TiposSinInformar.Contains(limpio))
            {
                return null;
            }
            return AliasTipos.TryGetValue(limpio, out string canonico) ? canonico : limpio;
        }

        /// <summary>
        /// El nombre para el filename="..." de Content-Disposition, que solo admite ASCII: sin tildes
        /// (Camión → Camion) y lo demás que no sea ASCII imprimible, a «_». El nombre real va en filename*.
        /// </summary>
        public static string NombreAscii(string nombre)
        {
            if (string.IsNullOrEmpty(nombre))
            {
                return "adjunto";
            }
            string descompuesto = nombre.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }
                sb.Append(c >= 32 && c < 127 && c != '"' && c != '\\' ? c : '_');
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
