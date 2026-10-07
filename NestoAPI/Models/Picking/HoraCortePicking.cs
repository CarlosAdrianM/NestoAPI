using System;
using System.Globalization;
using System.Linq;
using System.Runtime.Caching;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// NestoAPI#577: la hora de corte del picking, en UN solo sitio. Sale del parámetro de usuario «(defecto)»
    /// <see cref="CLAVE"/> de la empresa ("11:00"); si no está o no se entiende, <see cref="POR_DEFECTO"/>.
    ///
    /// La usan el picking (<see cref="GestorPicking.CalcularFechaPicking"/>), la fecha de entrega mínima de los
    /// pedidos (<c>GestorPedidosVenta.FechaEntregaAjustada</c>), el bloqueo de ampliar pedidos con picking y el
    /// calendario de reposiciones. Lo único que NO puede leerla es el cron de Hangfire del picking de cierre
    /// (Startup.cs, «picking-cierre-diario»): si se cambia el parámetro, hay que cambiar también ese cron.
    /// </summary>
    public static class HoraCortePicking
    {
        public const string CLAVE = "HoraCortePicking";
        public static readonly TimeSpan POR_DEFECTO = new TimeSpan(11, 0, 0);

        private static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);
        private static readonly MemoryCache cache = MemoryCache.Default;

        /// <summary>
        /// Cómo se lee el valor crudo del parámetro para una empresa. Sustituible en tests (sin BD).
        /// </summary>
        internal static Func<string, string> LectorValor { get; set; } = LeerValorDeBD;

        /// <summary>Hora de corte de la empresa (cacheada 10 minutos). Nunca lanza: ante cualquier fallo, 11:00.</summary>
        public static TimeSpan Leer(string empresa)
        {
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string claveCache = "HoraCortePicking:" + empresaLimpia;
            if (cache.Get(claveCache) is TimeSpan cacheada)
            {
                return cacheada;
            }

            TimeSpan hora;
            try
            {
                hora = Interpretar(LectorValor(empresaLimpia));
            }
            catch
            {
                hora = POR_DEFECTO;
            }
            cache.Set(claveCache, hora, DateTimeOffset.Now.Add(DURACION_CACHE));
            return hora;
        }

        /// <summary>"11:00" → 11:00; vacío o ilegible → <see cref="POR_DEFECTO"/>.</summary>
        public static TimeSpan Interpretar(string valor)
        {
            return TimeSpan.TryParse(valor?.Trim(), CultureInfo.InvariantCulture, out TimeSpan hora)
                && hora > TimeSpan.Zero && hora < TimeSpan.FromDays(1)
                ? hora
                : POR_DEFECTO;
        }

        /// <summary>Solo para tests: olvida lo cacheado.</summary>
        internal static void LimpiarCache()
        {
            foreach (string clave in cache.Where(c => c.Key.StartsWith("HoraCortePicking:")).Select(c => c.Key).ToList())
            {
                cache.Remove(clave);
            }
        }

        private static string LeerValorDeBD(string empresa)
        {
            using (NVEntities db = new NVEntities())
            {
                return db.ParametrosUsuario
                    .Where(p => p.Empresa == empresa && p.Usuario == "(defecto)" && p.Clave == CLAVE)
                    .Select(p => p.Valor)
                    .FirstOrDefault();
            }
        }
    }
}
