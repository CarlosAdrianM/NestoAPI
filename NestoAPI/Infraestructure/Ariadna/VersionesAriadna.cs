using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Ariadna
{
    /// <summary>NestoAPI#844: la última versión de Ariadna con la que ha llamado un usuario.</summary>
    public class VersionAriadnaUsuarioDTO
    {
        /// <summary>Sin dominio («Miguel»).</summary>
        public string Usuario { get; set; }
        public string Version { get; set; }
        public string Dispositivo { get; set; }
        public DateTime UltimaVez { get; set; }
    }

    /// <summary>
    /// NestoAPI#844 (Carlos 09/11/26): para distinguir «no ha actualizado» de «no lo ha hecho», la versión de Ariadna que manda cada
    /// usuario en <c>X-Ariadna-Version</c> (todas las llamadas desde Ariadna#58). La apunta <see cref="VersionMinimaAriadnaHandler"/>.
    /// En memoria: se pierde al reciclar el pool, pero vuelve con la siguiente llamada de cada uno (no hace falta script).
    /// </summary>
    public static class VersionesAriadna
    {
        private const char SEPARADOR_DOMINIO = (char)92;
        private static readonly ConcurrentDictionary<string, VersionAriadnaUsuarioDTO> Ultimas =
            new ConcurrentDictionary<string, VersionAriadnaUsuarioDTO>(StringComparer.OrdinalIgnoreCase);

        public static string SinDominio(string usuario)
            => string.IsNullOrWhiteSpace(usuario) ? null : usuario.Substring(usuario.IndexOf(SEPARADOR_DOMINIO) + 1).Trim();

        public static void Apuntar(string usuario, string version, string dispositivo, DateTime cuando)
        {
            string nombre = SinDominio(usuario);
            if (string.IsNullOrEmpty(nombre) || string.IsNullOrWhiteSpace(version))
            {
                return;
            }
            Ultimas[nombre] = new VersionAriadnaUsuarioDTO
            {
                Usuario = nombre,
                Version = version.Trim(),
                Dispositivo = string.IsNullOrWhiteSpace(dispositivo) ? null : dispositivo.Trim(),
                UltimaVez = cuando
            };
        }

        /// <summary>La última de ese usuario (con o sin dominio), o null si no ha llamado desde Ariadna desde el último arranque.</summary>
        public static VersionAriadnaUsuarioDTO De(string usuario)
        {
            string nombre = SinDominio(usuario);
            return nombre != null && Ultimas.TryGetValue(nombre, out VersionAriadnaUsuarioDTO v) ? v : null;
        }

        public static List<VersionAriadnaUsuarioDTO> Todas()
            => Ultimas.Values.OrderBy(v => v.Usuario, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Solo para los tests.</summary>
        internal static void Vaciar() => Ultimas.Clear();
    }
}
