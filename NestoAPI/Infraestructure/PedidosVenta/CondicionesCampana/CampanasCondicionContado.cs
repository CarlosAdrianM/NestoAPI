using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana
{
    /// <summary>Una fila de <c>dbo.CampanasCondicionContado</c>: ese producto, en esa campaña y esas fechas, va con condición de contado.</summary>
    public class ProductoCampanaContado
    {
        public string Campana { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        /// <summary>Texto propio del aviso; null = el de <see cref="AvisoCondicionesCampana.MENSAJE_POR_DEFECTO"/>.</summary>
        public string TextoAviso { get; set; }
    }

    /// <summary>
    /// Feria online 12-16/11/26 (Carlos 11/11/26): los productos de una campaña cuyas condiciones exigen pago al contado. Salen de
    /// la tabla <c>dbo.CampanasCondicionContado</c> (Campana, Empresa, Producto, FechaDesde, FechaHasta, TextoAviso, Usuario,
    /// FechaModificacion; <c>Scripts/FeriaOnline_CampanasCondicionContado.sql</c>), fuera del EDMX como
    /// <see cref="DiasRepartoClientes"/>: se lee por SQL y, si la tabla no existe, no hay ninguna campaña (nunca se avisa).
    /// <para>Se lee como mucho una vez cada <see cref="DURACION_CACHE"/>. Sin lector configurado (las pruebas) no toca la BD. Si
    /// falla la lectura, se queda con lo último leído y lo vuelve a intentar al caducar.</para>
    /// </summary>
    public static class CampanasCondicionContado
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);

        internal const string SQL_LEER =
            "IF OBJECT_ID(N'dbo.CampanasCondicionContado', N'U') IS NOT NULL " +
            "SELECT RTRIM(Campana) AS Campana, RTRIM(Empresa) AS Empresa, RTRIM(Producto) AS Producto, " +
            "CAST(FechaDesde AS datetime) AS FechaDesde, CAST(FechaHasta AS datetime) AS FechaHasta, TextoAviso " +
            "FROM dbo.CampanasCondicionContado WITH (NOLOCK)";

        private static readonly IReadOnlyList<ProductoCampanaContado> NINGUNO = new List<ProductoCampanaContado>();
        private static readonly object candado = new object();
        private static Func<IEnumerable<ProductoCampanaContado>> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static IReadOnlyList<ProductoCampanaContado> enCache = NINGUNO;
        private static DateTime caducaEn = DateTime.MinValue;

        /// <summary>Todas las filas (vigentes o no). Nunca lanza; vacío si no hay tabla o no se ha podido leer.</summary>
        public static IReadOnlyList<ProductoCampanaContado> Actuales
        {
            get
            {
                Func<IEnumerable<ProductoCampanaContado>> lectorActual = lector;
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
                        enCache = (lectorActual() ?? Enumerable.Empty<ProductoCampanaContado>())
                            .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Producto))
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            registrar?.Invoke(new Exception($"[Campañas contado] No se han podido leer los productos de campaña con condición de contado: {ex.Message}", ex));
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

        internal static IEnumerable<ProductoCampanaContado> LeerDeLaBaseDeDatos()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<ProductoCampanaContado>(SQL_LEER).ToList();
            }
        }

        /// <summary>Pone el lector (null = sin BD: ninguna campaña) y vacía la caché.</summary>
        internal static void Configurar(Func<IEnumerable<ProductoCampanaContado>> nuevoLector, Action<Exception> registrarError = null,
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

        /// <summary>Pruebas: con estas filas.</summary>
        internal static void ConfigurarPara(params ProductoCampanaContado[] filas)
        {
            List<ProductoCampanaContado> lista = filas?.ToList() ?? new List<ProductoCampanaContado>();
            Configurar(() => lista);
        }

        /// <summary>Pruebas: vuelve al estado de arranque (sin lector, sin campañas).</summary>
        internal static void Reiniciar() => Configurar(null);
    }
}
