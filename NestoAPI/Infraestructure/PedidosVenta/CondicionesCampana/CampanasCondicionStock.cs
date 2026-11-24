using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana
{
    /// <summary>Una fila de <c>dbo.CampanasCondicionStock</c>: el descuento de ese producto, en esa campaña y esas fechas, es solo para lo que haya en stock.</summary>
    public class ProductoCampanaStock
    {
        public string Campana { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
    }

    /// <summary>
    /// NestoAPI#889 (Black Friday 2026, Manuel 23/11/26: «en principio solo para lo que tenga stock»): los productos de una campaña
    /// cuyo descuento es solo para lo que haya en stock. Salen de <c>dbo.CampanasCondicionStock</c> (Campana, Empresa, Producto,
    /// FechaDesde, FechaHasta, Usuario, FechaModificacion; <c>Scripts/BlackFriday2026_CampanasCondicionStock.sql</c>), fuera del EDMX
    /// como <see cref="CampanasCondicionContado"/> (misma forma, misma caché): si la tabla no existe, ninguna campaña (nunca se avisa).
    /// <para>Se lee como mucho una vez cada <see cref="DURACION_CACHE"/>. Sin lector configurado (las pruebas) no toca la BD. Si falla
    /// la lectura, se queda con lo último leído, lo apunta en ELMAH UNA vez (hasta que vuelva a leer bien) y lo reintenta al caducar.</para>
    /// </summary>
    public static class CampanasCondicionStock
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);

        internal const string SQL_LEER =
            "IF OBJECT_ID(N'dbo.CampanasCondicionStock', N'U') IS NOT NULL " +
            "SELECT RTRIM(Campana) AS Campana, RTRIM(Empresa) AS Empresa, RTRIM(Producto) AS Producto, " +
            "CAST(FechaDesde AS datetime) AS FechaDesde, CAST(FechaHasta AS datetime) AS FechaHasta " +
            "FROM dbo.CampanasCondicionStock WITH (NOLOCK)";

        private static readonly IReadOnlyList<ProductoCampanaStock> NINGUNO = new List<ProductoCampanaStock>();
        private static readonly object candado = new object();
        private static Func<IEnumerable<ProductoCampanaStock>> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static IReadOnlyList<ProductoCampanaStock> enCache = NINGUNO;
        private static DateTime caducaEn = DateTime.MinValue;
        private static bool falloRegistrado;

        /// <summary>Todas las filas (vigentes o no). Nunca lanza; vacío si no hay tabla o no se ha podido leer.</summary>
        public static IReadOnlyList<ProductoCampanaStock> Actuales
        {
            get
            {
                Func<IEnumerable<ProductoCampanaStock>> lectorActual = lector;
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
                        enCache = (lectorActual() ?? Enumerable.Empty<ProductoCampanaStock>())
                            .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Producto))
                            .ToList();
                        falloRegistrado = false;
                    }
                    catch (Exception ex)
                    {
                        if (!falloRegistrado)
                        {
                            falloRegistrado = true;
                            try
                            {
                                registrar?.Invoke(new Exception($"[Campañas solo en stock] No se han podido leer los productos de campaña con condición de stock: {ex.Message}", ex));
                            }
                            catch
                            {
                                // El registro nunca puede tumbar la lectura.
                            }
                        }
                    }
                    caducaEn = ahora + DURACION_CACHE;
                    return enCache;
                }
            }
        }

        /// <summary>Las filas vigentes hoy (por día inclusive) de esa empresa (sin empresa en la fila = la 1).</summary>
        public static List<ProductoCampanaStock> Vigentes(IEnumerable<ProductoCampanaStock> filas, string empresa, DateTime hoy)
        {
            string laEmpresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            return (filas ?? Enumerable.Empty<ProductoCampanaStock>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Producto)
                    && f.FechaDesde.Date <= hoy.Date && hoy.Date <= f.FechaHasta.Date
                    && string.Equals(string.IsNullOrWhiteSpace(f.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : f.Empresa.Trim(),
                        laEmpresa, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>La siguiente consulta vuelve a leer la tabla.</summary>
        public static void Invalidar()
        {
            lock (candado)
            {
                caducaEn = DateTime.MinValue;
            }
        }

        /// <summary>Startup: lee la tabla con su propio contexto.</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(LeerDeLaBaseDeDatos, registrarError);
        }

        internal static IEnumerable<ProductoCampanaStock> LeerDeLaBaseDeDatos()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<ProductoCampanaStock>(SQL_LEER).ToList();
            }
        }

        /// <summary>Pone el lector (null = sin BD: ninguna campaña) y vacía la caché.</summary>
        internal static void Configurar(Func<IEnumerable<ProductoCampanaStock>> nuevoLector, Action<Exception> registrarError = null,
            Func<DateTime> nuevoReloj = null)
        {
            lock (candado)
            {
                lector = nuevoLector;
                registrar = registrarError;
                reloj = nuevoReloj ?? (() => DateTime.UtcNow);
                enCache = NINGUNO;
                caducaEn = DateTime.MinValue;
                falloRegistrado = false;
            }
        }

        /// <summary>Pruebas: con estas filas.</summary>
        internal static void ConfigurarPara(params ProductoCampanaStock[] filas)
        {
            List<ProductoCampanaStock> lista = filas?.ToList() ?? new List<ProductoCampanaStock>();
            Configurar(() => lista);
        }

        /// <summary>Pruebas: vuelve al estado de arranque (sin lector, sin campañas).</summary>
        internal static void Reiniciar() => Configurar(null);
    }
}
