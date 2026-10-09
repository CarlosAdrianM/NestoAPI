using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#619: ENTRENAR con LightGBM necesita la librería nativa <c>lib_lightgbm.dll</c> (x64; para puntuar no hace
    /// falta). En IIS, el DllImport de Microsoft.ML.LightGbm no la busca en el bin de la web (busca junto a w3wp.exe, en
    /// System32 y en el PATH), así que se carga a mano desde su ruta completa antes de entrenar: una vez cargada en el
    /// proceso, el DllImport por nombre la encuentra. La copia al bin la hace NestoAPI.csproj (Content con CopyToOutputDirectory
    /// desde packages\LightGBM.3.3.5\runtimes\win-x64\native); al publicar queda en bin\ y en la raíz del sitio.
    /// Depende de vcomp140.dll (Visual C++ 2015-2022 x64), que en RDS2016 está en System32.
    /// </summary>
    public static class CargadorLightGbmNativo
    {
        internal const string NOMBRE = "lib_lightgbm.dll";
        private static readonly object bloqueo = new object();
        private static string cargadaDesde;

        /// <summary>Carga la librería si no lo está. Lanza si el proceso es de 32 bits o no la encuentra.</summary>
        public static string Asegurar()
        {
            lock (bloqueo)
            {
                if (cargadaDesde != null)
                {
                    return cargadaDesde;
                }
                if (!Environment.Is64BitProcess)
                {
                    throw new InvalidOperationException("NestoAPI#619: LightGBM solo funciona en 64 bits y el proceso de la API es de 32 " +
                        "(enable32BitAppOnWin64 del grupo de aplicaciones). No se puede reentrenar el modelo de llamadas.");
                }
                List<string> candidatas = Candidatas(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.RelativeSearchPath);
                string ruta = candidatas.FirstOrDefault(File.Exists);
                if (ruta == null)
                {
                    throw new FileNotFoundException("NestoAPI#619: no está " + NOMBRE + " (hace falta para entrenar el modelo de llamadas). " +
                        "Buscado en: " + string.Join("; ", candidatas), NOMBRE);
                }
                if (LoadLibrary(ruta) == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"NestoAPI#619: no se pudo cargar {ruta} ({new Win32Exception(error).Message}, " +
                        $"código {error}). ¿Falta vcomp140.dll (Visual C++ 2015-2022 x64)?");
                }
                cargadaDesde = ruta;
                return ruta;
            }
        }

        /// <summary>bin de la web (o el que diga RelativeSearchPath), la raíz y sus x64.</summary>
        internal static List<string> Candidatas(string baseDirectory, string relativeSearchPath)
        {
            var carpetas = new List<string>();
            if (!string.IsNullOrWhiteSpace(relativeSearchPath))
            {
                carpetas.AddRange(relativeSearchPath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => Path.IsPathRooted(c) ? c : Path.Combine(baseDirectory, c)));
            }
            carpetas.Add(Path.Combine(baseDirectory, "bin"));
            carpetas.Add(baseDirectory);
            return carpetas
                .SelectMany(c => new[] { Path.Combine(c, NOMBRE), Path.Combine(c, "x64", NOMBRE) })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);
    }
}
