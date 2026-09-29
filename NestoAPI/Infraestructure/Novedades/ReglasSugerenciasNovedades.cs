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
        public static readonly string[] CATEGORIAS = { "Nuevo", "Mejorado", "Corregido", CATEGORIA_INCIDENCIA };
        public const string CATEGORIA_SUGERENCIA = "Nuevo";
        /// <summary>
        /// NestoAPI#558: «Algo no funciona». Se guarda como una sugerencia pero con esta categoría; al
        /// corregirla (con versión) pasa a Corregido y sale en el changelog como cualquier novedad.
        /// </summary>
        public const string CATEGORIA_INCIDENCIA = "Incidencia";
        public const string CATEGORIA_CORREGIDO = "Corregido";

        /// <summary>NestoAPI#558: contexto automático de las incidencias.</summary>
        public const int MINUTOS_ERRORES_ELMAH = 60;
        public const int MAXIMO_ERRORES_ELMAH = 5;
        public const int LONGITUD_MENSAJE_ERROR = 200;
        public const int LONGITUD_PANTALLA = 100;
        public const int LONGITUD_VERSION_CLIENTE = 30;

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
            // NestoAPI#558: una incidencia con versión es que ya está corregida (sin categoría, el UPDATE
            // también pasa las incidencias a Corregido).
            if (cambios.Version != null && cambios.Categoria == CATEGORIA_INCIDENCIA)
            {
                cambios.Categoria = CATEGORIA_CORREGIDO;
            }
            return null;
        }

        /// <summary>
        /// NestoAPI#558: los nombres con los que ELMAH puede tener guardado al usuario: tal cual, sin el
        /// dominio y con él (Nesto graba «NUEVAVISION\Alfredo»; NestoApp, el UserName).
        /// </summary>
        public static List<string> UsuariosElmah(string nombreIdentidad, string dominio)
        {
            var usuarios = new List<string>();
            if (string.IsNullOrWhiteSpace(nombreIdentidad))
            {
                return usuarios;
            }
            string completo = nombreIdentidad.Trim();
            string sinDominio = completo.Substring(completo.IndexOf('\\') + 1).Trim();
            foreach (string candidato in new[] { completo, sinDominio, (dominio ?? string.Empty) + sinDominio })
            {
                if (candidato.Length > 0 && !usuarios.Contains(candidato, StringComparer.OrdinalIgnoreCase))
                {
                    usuarios.Add(candidato);
                }
            }
            return usuarios;
        }

        /// <summary>
        /// NestoAPI#558: el contexto que se guarda con la incidencia, en texto para leerlo de un vistazo:
        /// versión, pantalla y los errores de ELMAH del usuario de la última hora (hora de España,
        /// tipo y el principio del mensaje). <paramref name="errores"/> null = no se pudieron leer.
        /// </summary>
        public static string ComponerContexto(string versionCliente, string pantalla, IEnumerable<ErrorElmahResumen> errores)
        {
            var lineas = new List<string>
            {
                "Versión: " + (string.IsNullOrWhiteSpace(versionCliente) ? "(desconocida)" : Recortar(versionCliente, LONGITUD_VERSION_CLIENTE))
            };
            if (!string.IsNullOrWhiteSpace(pantalla))
            {
                lineas.Add("Pantalla: " + Recortar(pantalla, LONGITUD_PANTALLA));
            }
            List<ErrorElmahResumen> lista = errores?.Where(e => e != null).ToList();
            if (lista == null)
            {
                lineas.Add("Errores de la última hora: no se pudieron consultar.");
            }
            else if (lista.Count == 0)
            {
                lineas.Add("Errores de la última hora: ninguno.");
            }
            else
            {
                lineas.Add($"Errores de la última hora ({lista.Count}):");
                foreach (ErrorElmahResumen error in lista)
                {
                    string mensaje = System.Text.RegularExpressions.Regex.Replace(error.Message ?? string.Empty, @"\s+", " ").Trim();
                    lineas.Add($"- {HoraEspana(error.TimeUtc):HH:mm} {error.Type?.Trim()}: {Recortar(mensaje, LONGITUD_MENSAJE_ERROR)}");
                }
            }
            return string.Join(Environment.NewLine, lineas);
        }

        private static DateTime HoraEspana(DateTime utc)
        {
            DateTime enUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(enUtc, TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"));
            }
            catch (Exception)
            {
                return enUtc.ToLocalTime();
            }
        }

        private static string Recortar(string texto, int longitud)
        {
            string limpio = (texto ?? string.Empty).Trim();
            return limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud - 1).TrimEnd() + "…";
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
