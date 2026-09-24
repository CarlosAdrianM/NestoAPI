using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#526/#527: las reglas puras de las sugerencias y del buscador de Novedades.
    /// </summary>
    public static class ReglasSugerenciasNovedades
    {
        public const string ESTADO_PENDIENTE = "Pendiente";
        public const string ESTADO_ACEPTADA = "Aceptada";
        public const string ESTADO_IMPLEMENTADA = "Implementada";
        public const string ESTADO_DESCARTADA = "Descartada";
        public static readonly string[] ESTADOS = { ESTADO_PENDIENTE, ESTADO_ACEPTADA, ESTADO_IMPLEMENTADA, ESTADO_DESCARTADA };
        public static readonly string[] CATEGORIAS = { "Nuevo", "Mejorado", "Corregido" };
        public const string CATEGORIA_SUGERENCIA = "Nuevo";

        public const int LONGITUD_TITULO = 200;
        public const int LONGITUD_DESCRIPCION = 1000;
        public const int LONGITUD_VERSION = 23;

        /// <summary>Buscador: palabras de al menos 2 letras, hasta 6.</summary>
        public const int LONGITUD_MINIMA_PALABRA = 2;
        public const int MAXIMO_PALABRAS = 6;
        public const int MAXIMO_RESULTADOS = 50;

        /// <summary>
        /// El título con el que nace la sugerencia: la primera línea de lo que escribió el usuario,
        /// recortada. Luego lo reescribimos nosotros si hace falta.
        /// </summary>
        public static string TituloDesde(string texto)
        {
            string primeraLinea = (texto ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            if (primeraLinea.Length <= LONGITUD_TITULO)
            {
                return primeraLinea;
            }
            return primeraLinea.Substring(0, LONGITUD_TITULO - 1).TrimEnd() + "…";
        }

        /// <summary>Las abiertas (pendientes y aceptadas) salen en la página de sugerencias.</summary>
        public static bool EstaAbierta(string estado) =>
            string.Equals(estado, ESTADO_PENDIENTE, StringComparison.OrdinalIgnoreCase)
            || string.Equals(estado, ESTADO_ACEPTADA, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Valida y normaliza un cambio nuestro. Devuelve el motivo si no vale, o null. Con versión,
        /// la sugerencia pasa a Implementada (se ha hecho en esa versión).
        /// </summary>
        public static string Normalizar(ActualizarSugerenciaNovedadDTO cambios)
        {
            if (cambios == null)
            {
                return "No se ha recibido ningún cambio";
            }
            if (cambios.Titulo != null)
            {
                cambios.Titulo = cambios.Titulo.Trim();
                if (cambios.Titulo.Length == 0 || cambios.Titulo.Length > LONGITUD_TITULO)
                {
                    return $"El título tiene que tener entre 1 y {LONGITUD_TITULO} caracteres";
                }
            }
            if (cambios.Descripcion != null)
            {
                cambios.Descripcion = cambios.Descripcion.Trim();
                if (cambios.Descripcion.Length > LONGITUD_DESCRIPCION)
                {
                    return $"La descripción no puede pasar de {LONGITUD_DESCRIPCION} caracteres";
                }
            }
            if (!string.IsNullOrWhiteSpace(cambios.Version))
            {
                cambios.Version = cambios.Version.Trim();
                if (cambios.Version.Length > LONGITUD_VERSION || !Version.TryParse(cambios.Version, out _))
                {
                    return "La versión no es válida (p. ej. 1.10.31.0)";
                }
                cambios.Estado = ESTADO_IMPLEMENTADA;
            }
            else
            {
                cambios.Version = null;
            }
            if (cambios.Estado != null)
            {
                string estado = ESTADOS.FirstOrDefault(e => string.Equals(e, cambios.Estado.Trim(), StringComparison.OrdinalIgnoreCase));
                if (estado == null)
                {
                    return "El estado tiene que ser " + string.Join(", ", ESTADOS);
                }
                if (estado == ESTADO_IMPLEMENTADA && cambios.Version == null)
                {
                    return "Para darla por implementada hay que decir en qué versión";
                }
                cambios.Estado = estado;
            }
            if (cambios.Categoria != null)
            {
                string categoria = CATEGORIAS.FirstOrDefault(c => string.Equals(c, cambios.Categoria.Trim(), StringComparison.OrdinalIgnoreCase));
                if (categoria == null)
                {
                    return "La categoría tiene que ser " + string.Join(", ", CATEGORIAS);
                }
                cambios.Categoria = categoria;
            }
            return null;
        }

        /// <summary>
        /// NestoAPI#527: las palabras que se buscan (todas tienen que aparecer). Sin palabras
        /// válidas, lista vacía.
        /// </summary>
        public static List<string> Palabras(string texto)
        {
            return (texto ?? string.Empty)
                .Split(new[] { ' ', '\t', ',', ';', '.', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length >= LONGITUD_MINIMA_PALABRA)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MAXIMO_PALABRAS)
                .ToList();
        }

        /// <summary>El patrón LIKE de una palabra, con los comodines de SQL escapados.</summary>
        public static string PatronLike(string palabra)
        {
            string escapada = (palabra ?? string.Empty)
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");
            return "%" + escapada + "%";
        }
    }
}
