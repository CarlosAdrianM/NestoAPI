using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>De dónde sale la impresora de un trabajo de etiquetas (<see cref="ImpresoraEtiquetasElegida.Origen"/>).</summary>
    public static class OrigenesImpresoraEtiquetas
    {
        /// <summary>La que dice la petición.</summary>
        public const string PEDIDA = "Pedida";
        /// <summary>La del almacén del trabajo (ALG, REI o ALC).</summary>
        public const string ALMACEN = "Almacen";
        /// <summary>La de quien imprime (ParametrosUsuario ImpresoraCodBarras).</summary>
        public const string USUARIO = "Usuario";
    }

    public class ImpresoraEtiquetasElegida
    {
        public ImpresoraEtiquetasElegida(string impresora, string origen)
        {
            Impresora = impresora;
            Origen = origen;
        }

        public string Impresora { get; }
        /// <summary><see cref="OrigenesImpresoraEtiquetas"/>.</summary>
        public string Origen { get; }
    }

    /// <summary>
    /// LA REGLA (pura) de en qué impresora sale un trabajo de etiquetas (Carlos 25/11/26: Carlos, usuario de Algete, imprimía en
    /// Alcobendas las muestras de ALC y salían en Algete). Orden: (1) la que diga la petición, si viene, validada con
    /// <see cref="MotivoNoValida"/>; (2) la del ALMACÉN del trabajo (ALG, REI o ALC): ParametrosUsuario empresa 1 «(defecto)»
    /// <c>ImpresoraEtiquetas.{ALG|REI|ALC}</c> (antes, si la hay, la clave antigua propia del trabajo, como
    /// <c>ImpresoraEtiquetasInmovilizado.{…}</c>) y, sin fila, <see cref="POR_DEFECTO"/>; (3) sin almacén u otro, la del usuario.
    /// Nunca crea filas al leer: el lector que se le pase debe ser «sin defecto» (<see cref="LectorDefecto"/>).
    /// </summary>
    public static class ReglaImpresoraEtiquetas
    {
        public const string PREFIJO_PARAMETRO = "ImpresoraEtiquetas.";
        public const int LARGO_MAXIMO = ServicioAjustesImpresoras.LARGO_IMPRESORA;
        private const string USUARIO_DEFECTO = "(defecto)";

        public static readonly IReadOnlyDictionary<string, string> POR_DEFECTO = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Constantes.Almacenes.ALGETE] = @"\\RDS2016\etiquetas2",
            [Constantes.Almacenes.REINA] = @"\\RDS2016\etiquetasreina",
            [Constantes.Almacenes.ALCOBENDAS] = @"\\RDS2016\etiquetasalcobendas"
        };

        /// <summary>Los tres almacenes con impresora propia, en el orden de la lista, con su nombre.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> ALMACENES = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(Constantes.Almacenes.ALGETE, "Algete"),
            new KeyValuePair<string, string>(Constantes.Almacenes.REINA, "Reina"),
            new KeyValuePair<string, string>(Constantes.Almacenes.ALCOBENDAS, "Alcobendas")
        };

        // \\servidor\cola: servidor con letras, cifras, guiones y puntos (nombre DNS); cola sin barras ni caracteres raros
        private static readonly Regex rutaUnc = new Regex(
            @"^\\\\(?<servidor>[A-Za-z0-9](?:[A-Za-z0-9-]{0,62})(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,62}))*)\\(?<cola>[A-Za-z0-9_][A-Za-z0-9 _.()\-]{0,79})$",
            RegexOptions.CultureInvariant);

        /// <summary>ALG, REI o ALC (sin espacios ni mayúsculas); null si es otro o no viene.</summary>
        public static string NormalizarAlmacen(string almacen)
        {
            string a = almacen?.Trim().ToUpperInvariant();
            return a != null && POR_DEFECTO.ContainsKey(a) ? a : null;
        }

        /// <summary>La clave del parámetro de ese almacén con ese prefijo, o null si no es ALG, REI ni ALC.</summary>
        public static string ClaveParametro(string almacen, string prefijo = PREFIJO_PARAMETRO)
        {
            string a = NormalizarAlmacen(almacen);
            return a == null ? null : prefijo + a;
        }

        /// <summary>
        /// La impresora del almacén: la clave antigua (si se da y tiene valor), la general y, sin fila, la del código. Null si no
        /// es ALG, REI ni ALC (no se lee nada).
        /// </summary>
        public static string DelAlmacen(string almacen, Func<string, string> valorParametro, string prefijoAntiguo = null)
        {
            string a = NormalizarAlmacen(almacen);
            if (a == null)
            {
                return null;
            }
            foreach (string prefijo in new[] { prefijoAntiguo, PREFIJO_PARAMETRO }.Where(p => !string.IsNullOrEmpty(p)))
            {
                string valor = valorParametro?.Invoke(prefijo + a);
                if (!string.IsNullOrWhiteSpace(valor))
                {
                    return valor.Trim();
                }
            }
            return POR_DEFECTO[a];
        }

        /// <param name="pedida">La de la petición (manda si viene; se valida).</param>
        /// <param name="almacen">El almacén del trabajo (ALG, REI, ALC); otro o null, la del usuario.</param>
        /// <param name="valorParametro">Lee la fila «(defecto)» de esa clave (null si no hay). Solo se llama si hace falta.</param>
        /// <param name="impresoraDelUsuario">La ImpresoraCodBarras de quien imprime; puede lanzar ArgumentException (no tiene).</param>
        /// <param name="prefijoAntiguo">La clave propia de un trabajo que gana sobre la general (inmovilizados).</param>
        /// <exception cref="ArgumentException">La pedida no vale o el usuario no tiene impresora: el motivo es para el usuario.</exception>
        public static ImpresoraEtiquetasElegida Elegir(string pedida, string almacen, Func<string, string> valorParametro,
            Func<string> impresoraDelUsuario, string prefijoAntiguo = null)
        {
            if (!string.IsNullOrWhiteSpace(pedida))
            {
                string motivo = MotivoNoValida(pedida, Conocidas(valorParametro, impresoraDelUsuario, prefijoAntiguo));
                if (motivo != null)
                {
                    throw new ArgumentException(motivo);
                }
                return new ImpresoraEtiquetasElegida(pedida.Trim(), OrigenesImpresoraEtiquetas.PEDIDA);
            }
            string delAlmacen = DelAlmacen(almacen, valorParametro, prefijoAntiguo);
            if (delAlmacen != null)
            {
                return new ImpresoraEtiquetasElegida(delAlmacen, OrigenesImpresoraEtiquetas.ALMACEN);
            }
            if (impresoraDelUsuario == null)
            {
                throw new ArgumentException("No se sabe en qué impresora imprimir las etiquetas: indíquela.");
            }
            return new ImpresoraEtiquetasElegida(impresoraDelUsuario(), OrigenesImpresoraEtiquetas.USUARIO);
        }

        /// <summary>
        /// Por qué no vale la impresora pedida (null si vale). Llega del cliente y acaba en un OpenPrinter del servidor: tiene que
        /// ser una ruta <c>\\servidor\cola</c> sin caracteres raros, de <see cref="LARGO_MAXIMO"/> como mucho, y en un servidor
        /// de impresión CONOCIDO (el de alguna de las impresoras de los almacenes o la del usuario): así nadie hace que el
        /// servidor se conecte a una máquina cualquiera.
        /// </summary>
        public static string MotivoNoValida(string pedida, IEnumerable<string> impresorasConocidas)
        {
            string limpia = pedida?.Trim();
            if (string.IsNullOrEmpty(limpia))
            {
                return "Falta la impresora.";
            }
            if (limpia.Length > LARGO_MAXIMO)
            {
                return $"La ruta de la impresora no puede pasar de {LARGO_MAXIMO} caracteres.";
            }
            string servidor = Servidor(limpia);
            if (servidor == null || limpia.Contains(".."))
            {
                return $"«{limpia}» no es una impresora válida: tiene que ser como \\\\RDS2016\\etiquetas2. Elíjala de la lista.";
            }
            List<string> servidores = (impresorasConocidas ?? Enumerable.Empty<string>())
                .Concat(POR_DEFECTO.Values)
                .Select(Servidor)
                .Where(s => s != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return servidores.Contains(servidor, StringComparer.OrdinalIgnoreCase)
                ? null
                : $"La impresora «{limpia}» no está en ningún servidor de impresión conocido ({string.Join(", ", servidores)}). Elíjala de la lista.";
        }

        /// <summary>El servidor de una ruta <c>\\servidor\cola</c> válida; null si no lo es.</summary>
        public static string Servidor(string impresora)
        {
            Match m = rutaUnc.Match(impresora?.Trim() ?? string.Empty);
            return m.Success ? m.Groups["servidor"].Value : null;
        }

        /// <summary>
        /// Las impresoras para elegir: las de los tres almacenes (según la regla) y, si es otra, la del usuario al final
        /// (sin almacén). EsLaDelUsuario marca la suya. Si el usuario no tiene impresora, solo las tres.
        /// </summary>
        public static List<ImpresoraEtiquetasDTO> Lista(Func<string, string> valorParametro, Func<string> impresoraDelUsuario)
        {
            string delUsuario = Intentar(impresoraDelUsuario);
            string claveUsuario = ServicioAjustesImpresoras.Normalizar(delUsuario);
            var lista = ALMACENES.Select(a =>
            {
                string impresora = DelAlmacen(a.Key, valorParametro);
                return new ImpresoraEtiquetasDTO
                {
                    Almacen = a.Key,
                    Nombre = a.Value,
                    Impresora = impresora,
                    EsLaDelUsuario = claveUsuario != null && ServicioAjustesImpresoras.Normalizar(impresora) == claveUsuario
                };
            }).ToList();
            if (claveUsuario != null && !lista.Any(i => i.EsLaDelUsuario))
            {
                lista.Add(new ImpresoraEtiquetasDTO { Almacen = null, Nombre = "La suya", Impresora = delUsuario, EsLaDelUsuario = true });
            }
            return lista;
        }

        /// <summary>
        /// Lee la fila «(defecto)» de la empresa 1 (sin crear ninguna: <see cref="ILectorParametrosUsuario.LeerParametroSinDefecto"/>),
        /// una vez por clave.
        /// </summary>
        public static Func<string, string> LectorDefecto(ILectorParametrosUsuario parametros)
        {
            var leidos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return clave =>
            {
                if (!leidos.TryGetValue(clave, out string valor))
                {
                    valor = parametros?.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, clave);
                    leidos[clave] = valor;
                }
                return valor;
            };
        }

        private static IEnumerable<string> Conocidas(Func<string, string> valorParametro, Func<string> impresoraDelUsuario, string prefijoAntiguo)
        {
            foreach (KeyValuePair<string, string> almacen in ALMACENES)
            {
                yield return DelAlmacen(almacen.Key, valorParametro, prefijoAntiguo);
                if (prefijoAntiguo != null)
                {
                    yield return DelAlmacen(almacen.Key, valorParametro);
                }
            }
            yield return Intentar(impresoraDelUsuario);
        }

        private static string Intentar(Func<string> impresoraDelUsuario)
        {
            try
            {
                return impresoraDelUsuario?.Invoke();
            }
            catch (ArgumentException)
            {
                // No tiene impresora: no cuenta
                return null;
            }
        }
    }
}
