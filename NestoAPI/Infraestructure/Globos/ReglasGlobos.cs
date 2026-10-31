using NestoAPI.Models.Globos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;

namespace NestoAPI.Infraestructure.Globos
{
    /// <summary>
    /// NestoAPI#774: qué globos le tocan a un usuario en una pantalla. Puro (sin BD) para poder probarlo:
    /// <list type="bullet">
    /// <item>Activo, y hoy entre FechaDesde y FechaHasta (las dos incluidas; FechaHasta null = sin fin).</item>
    /// <item>De la aplicación (y de la pantalla, si se pide una).</item>
    /// <item>VersionDesde: si el globo la tiene y la app manda la suya, solo si la de la app es igual o posterior (una app vieja
    ///   no tiene el control nuevo). Sin versión de la app o sin parsear, sale (mejor de más que perder un consejo).</item>
    /// <item>Grupos: vacío = todos. Si no, grupos de dominio o roles de Identity (sin dominio, sin distinguir mayúsculas ni tildes)
    ///   o nombres de usuario (sin dominio). Basta uno.</item>
    /// <item>No vistos por ese usuario.</item>
    /// </list>
    /// Orden: Orden y luego Id. El máximo que se enseña seguidos lo decide cada app (1-2).
    /// </summary>
    public static class ReglasGlobos
    {
        public const string NESTO = "Nesto";
        public const string ARIADNA = "Ariadna";
        public const string TNV = "TNV";
        public const string NESTOAPP = "NestoApp";

        public static readonly IReadOnlyList<string> APLICACIONES = new[] { NESTO, ARIADNA, TNV, NESTOAPP };

        /// <summary>Longitud de GlobosVistos.Usuario.</summary>
        public const int LONGITUD_USUARIO = 128;

        /// <summary>El nombre canónico de la aplicación (sin distinguir mayúsculas), o null si no es ninguna.</summary>
        public static string Aplicacion(string aplicacion)
        {
            string limpia = aplicacion?.Trim();
            return APLICACIONES.FirstOrDefault(a => string.Equals(a, limpia, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>El usuario con el que se guarda «visto»: sin dominio, recortado. Null si no hay.</summary>
        public static string UsuarioVisto(IPrincipal usuario)
        {
            string nombre = usuario?.Identity?.IsAuthenticated == true ? usuario.Identity.Name : null;
            string sinDominio = SinDominio(nombre);
            if (string.IsNullOrEmpty(sinDominio))
            {
                return null;
            }
            return sinDominio.Length > LONGITUD_USUARIO ? sinDominio.Substring(0, LONGITUD_USUARIO) : sinDominio;
        }

        public static List<GloboDTO> Pendientes(IEnumerable<GloboFila> globos, string aplicacion, string pantalla, string versionApp,
            IPrincipal usuario, ICollection<int> vistos, DateTime hoy)
        {
            string app = Aplicacion(aplicacion);
            if (app == null)
            {
                return new List<GloboDTO>();
            }
            string pantallaPedida = pantalla?.Trim();
            bool hayVersion = Version.TryParse(versionApp?.Trim(), out Version version);
            DateTime dia = hoy.Date;
            return (globos ?? Enumerable.Empty<GloboFila>())
                .Where(g => g != null && g.Activo)
                .Where(g => string.Equals(g.Aplicacion?.Trim(), app, StringComparison.OrdinalIgnoreCase))
                .Where(g => string.IsNullOrEmpty(pantallaPedida) || string.Equals(g.Pantalla?.Trim(), pantallaPedida, StringComparison.OrdinalIgnoreCase))
                .Where(g => !string.IsNullOrWhiteSpace(g.Texto))
                .Where(g => g.FechaDesde.Date <= dia && (g.FechaHasta == null || g.FechaHasta.Value.Date >= dia))
                .Where(g => !hayVersion || EsDesdeVersion(g.VersionDesde, version))
                .Where(g => EsDelUsuario(g.Grupos, usuario))
                .Where(g => vistos == null || !vistos.Contains(g.Id))
                .OrderBy(g => g.Orden)
                .ThenBy(g => g.Id)
                .Select(g => g.ADto())
                .ToList();
        }

        /// <summary>Sin VersionDesde (o sin parsear) vale para todas.</summary>
        public static bool EsDesdeVersion(string versionDesde, Version versionApp)
        {
            if (versionApp == null || !Version.TryParse(versionDesde?.Trim(), out Version desde))
            {
                return true;
            }
            return Normalizar(versionApp) >= Normalizar(desde);
        }

        /// <summary>«Almacén, Tiendas; carlos»: separados por coma, punto y coma o barra. Vacío = todos.</summary>
        public static bool EsDelUsuario(string grupos, IPrincipal usuario)
        {
            List<string> lista = (grupos ?? string.Empty)
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(g => Clave(SinDominio(g)))
                .Where(g => !string.IsNullOrEmpty(g))
                .ToList();
            if (lista.Count == 0)
            {
                return true;
            }
            if (usuario?.Identity?.IsAuthenticated != true)
            {
                return false;
            }
            var propios = new HashSet<string>(StringComparer.Ordinal) { Clave(SinDominio(usuario.Identity.Name)) };
            if (usuario is ClaimsPrincipal claims)
            {
                foreach (Claim rol in claims.Claims.Where(c => c.Type == ClaimTypes.Role))
                {
                    _ = propios.Add(Clave(SinDominio(rol.Value)));
                }
            }
            return lista.Any(propios.Contains);
        }

        /// <summary>«NUEVAVISION\Carlos» → «Carlos».</summary>
        public static string SinDominio(string nombre)
        {
            string limpio = nombre?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1).Trim() : limpio;
        }

        // 1.10.53 = 1.10.53.0 (Version da -1 a las cifras que faltan)
        private static Version Normalizar(Version v)
            => new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

        private static string Clave(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return texto;
            }
            string descompuesto = texto.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = sb.Append(c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }
    }
}
