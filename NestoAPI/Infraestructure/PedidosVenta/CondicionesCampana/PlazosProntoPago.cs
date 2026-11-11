using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana
{
    /// <summary>Unos plazos de pago con descuento por pronto pago (<c>PlazosPago.DtoProntoPago</c> distinto de 0): PP, PP2, PP5…</summary>
    public class PlazoProntoPago
    {
        public string Empresa { get; set; }
        public string Plazos { get; set; }
        public decimal DtoProntoPago { get; set; }
    }

    /// <summary>
    /// Feria online (Carlos 11/11/26): «los pedidos con pronto pago deben admitirse en la feria online, al fin y al cabo son al
    /// contado». Los plazos de pago con descuento por pronto pago, leídos de <c>dbo.PlazosPago</c> como mucho una vez cada
    /// <see cref="DURACION_CACHE"/> (mismo patrón que <see cref="CampanasCondicionContado"/>). Sin lector configurado (las
    /// pruebas) no toca la BD. Si falla la lectura, se queda con lo último leído y lo vuelve a intentar al caducar.
    /// </summary>
    public static class PlazosProntoPago
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);

        internal const string SQL_LEER =
            "SELECT RTRIM(Empresa) AS Empresa, RTRIM([Número]) AS Plazos, DtoProntoPago " +
            "FROM dbo.PlazosPago WITH (NOLOCK) WHERE DtoProntoPago <> 0";

        private static readonly IReadOnlyList<PlazoProntoPago> NINGUNO = new List<PlazoProntoPago>();
        private static readonly object candado = new object();
        private static Func<IEnumerable<PlazoProntoPago>> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static IReadOnlyList<PlazoProntoPago> enCache = NINGUNO;
        private static DateTime caducaEn = DateTime.MinValue;

        /// <summary>Todos los plazos con pronto pago de todas las empresas. Nunca lanza; vacío si no se ha podido leer.</summary>
        public static IReadOnlyList<PlazoProntoPago> Actuales
        {
            get
            {
                Func<IEnumerable<PlazoProntoPago>> lectorActual = lector;
                if (lectorActual == null)
                {
                    return enCache;
                }
                DateTime ahora = reloj();
                if (ahora < caducaEn)
                {
                    return enCache;
                }
                lock (candado)
                {
                    if (ahora < caducaEn)
                    {
                        return enCache;
                    }
                    try
                    {
                        enCache = (lectorActual() ?? Enumerable.Empty<PlazoProntoPago>())
                            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Plazos) && p.DtoProntoPago != 0)
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            registrar?.Invoke(new Exception($"[Campañas contado] No se han podido leer los plazos de pago con pronto pago: {ex.Message}", ex));
                        }
                        catch
                        {
                            // El registro nunca puede tumbar la lectura.
                        }
                    }
                    caducaEn = ahora + DURACION_CACHE;
                    return enCache;
                }
            }
        }

        /// <summary>¿Son esos plazos, en esa empresa, unos plazos con descuento por pronto pago?</summary>
        public static bool LlevanProntoPago(IEnumerable<PlazoProntoPago> plazosConProntoPago, string empresa, string plazosPago)
        {
            string plazos = plazosPago?.Trim();
            if (string.IsNullOrEmpty(plazos) || plazosConProntoPago == null)
            {
                return false;
            }
            string empresaPedido = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            return plazosConProntoPago.Any(p => p != null && p.DtoProntoPago != 0 &&
                string.Equals(p.Plazos?.Trim(), plazos, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(string.IsNullOrWhiteSpace(p.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : p.Empresa.Trim(), empresaPedido,
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Startup: lee la tabla con su propio contexto.</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(LeerDeLaBaseDeDatos, registrarError);
        }

        internal static IEnumerable<PlazoProntoPago> LeerDeLaBaseDeDatos()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<PlazoProntoPago>(SQL_LEER).ToList();
            }
        }

        /// <summary>Pone el lector (null = sin BD: ningún plazo con pronto pago) y vacía la caché.</summary>
        internal static void Configurar(Func<IEnumerable<PlazoProntoPago>> nuevoLector, Action<Exception> registrarError = null,
            Func<DateTime> nuevoReloj = null)
        {
            lock (candado)
            {
                lector = nuevoLector;
                registrar = registrarError;
                reloj = nuevoReloj ?? (() => DateTime.UtcNow);
                enCache = NINGUNO;
                caducaEn = DateTime.MinValue;
            }
        }

        /// <summary>Pruebas: con estos plazos.</summary>
        internal static void ConfigurarPara(params PlazoProntoPago[] plazos)
        {
            List<PlazoProntoPago> lista = plazos?.ToList() ?? new List<PlazoProntoPago>();
            Configurar(() => lista);
        }

        /// <summary>Pruebas: vuelve al estado de arranque.</summary>
        internal static void Reiniciar() => Configurar(null);
    }
}
