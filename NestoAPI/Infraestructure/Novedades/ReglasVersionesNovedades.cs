using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// 28/10/26 (Carlos, al publicar la 1.10.51.1 sobre la 1.10.51.0 del mismo día): el popup de arranque de Nesto pide las
    /// novedades «desde la versión vista» y solo enseñaba la de la versión menor. Las versiones menores (cuarta cifra) salen
    /// JUNTO a las de su versión mayor (tres primeras cifras): si hay algo nuevo desde la vista, se enseñan también todas las
    /// de la misma versión mayor que la más nueva, aunque ya se hubieran visto. La campana no filtra por versión y no cambia.
    /// </summary>
    public static class ReglasVersionesNovedades
    {
        /// <summary>Las tres primeras cifras (1.10.51.1 → 1.10.51); null si no es una versión.</summary>
        public static Version VersionMayor(string version)
        {
            if (!Version.TryParse(version, out Version v))
            {
                return null;
            }
            return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));
        }

        /// <summary>
        /// Las entradas que ve quien vio por última vez <paramref name="versionVista"/>: las posteriores y, si las hay, todas
        /// las de la versión mayor de la más nueva. Las de versión no parseable se dejan (mejor de más que de menos).
        /// </summary>
        public static List<T> DesdeVersionVista<T>(IEnumerable<T> novedades, Version versionVista, Func<T, string> version)
        {
            List<T> lista = (novedades ?? Enumerable.Empty<T>()).ToList();
            if (versionVista == null)
            {
                return lista;
            }
            Version masNueva = lista
                .Select(n => Version.TryParse(version(n), out Version v) ? v : null)
                .Where(v => v != null && v > versionVista)
                .OrderByDescending(v => v)
                .FirstOrDefault();
            if (masNueva == null)
            {
                return lista.Where(n => !Version.TryParse(version(n), out Version _)).ToList();
            }
            Version mayor = VersionMayor(masNueva.ToString());
            return lista
                .Where(n => !Version.TryParse(version(n), out Version v) || v > versionVista || VersionMayor(v.ToString()) == mayor)
                .ToList();
        }
    }
}
