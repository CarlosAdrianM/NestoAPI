using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Campanas
{
    /// <summary>Una fila de <c>dbo.CampanasAcumulanDistribuidores</c>: en esa campaña y esas fechas, a los distribuidores se les suma encima del suyo.</summary>
    public class CampanaAcumulaDistribuidores
    {
        public string Campana { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
    }

    /// <summary>
    /// La marca POR DATO de las campañas cuyo descuento se suma al del distribuidor (Carlos 20/11/26): tabla
    /// <c>dbo.CampanasAcumulanDistribuidores</c> (fuera del EDMX, <c>Scripts/BlackFriday2026_CampanasAcumulanDistribuidores.sql</c>).
    /// Sin tabla, o sin lector configurado (las pruebas), ninguna campaña está marcada: todo como siempre. Caché de
    /// <see cref="DURACION_CACHE"/>; si falla la lectura, se queda con lo último leído.
    /// </summary>
    public static class CampanasAcumulanDistribuidores
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(5);

        internal const string SQL_LEER =
            "IF OBJECT_ID(N'dbo.CampanasAcumulanDistribuidores', N'U') IS NOT NULL " +
            "SELECT RTRIM(Campana) AS Campana, CAST(FechaDesde AS datetime) AS FechaDesde, CAST(FechaHasta AS datetime) AS FechaHasta " +
            "FROM dbo.CampanasAcumulanDistribuidores WITH (NOLOCK)";

        private static readonly IReadOnlyList<CampanaAcumulaDistribuidores> NINGUNA = new List<CampanaAcumulaDistribuidores>();
        private static readonly object candado = new object();
        private static Func<IEnumerable<CampanaAcumulaDistribuidores>> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static IReadOnlyList<CampanaAcumulaDistribuidores> enCache = NINGUNA;
        private static DateTime caducaEn = DateTime.MinValue;

        public static IReadOnlyList<CampanaAcumulaDistribuidores> Actuales
        {
            get
            {
                Func<IEnumerable<CampanaAcumulaDistribuidores>> lectorActual = lector;
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
                        enCache = (lectorActual() ?? Enumerable.Empty<CampanaAcumulaDistribuidores>())
                            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Campana)).ToList();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            registrar?.Invoke(new Exception($"[Campañas distribuidores] No se han podido leer las campañas que se suman al distribuidor: {ex.Message}", ex));
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

        /// <summary>Si la campaña de ese nombre está marcada y su marca vale ese día.</summary>
        public static bool EstaMarcada(string campana, DateTime dia) => DescuentoDistribuidorCampana.EstaMarcada(Actuales, campana, dia);

        public static void Invalidar()
        {
            lock (candado)
            {
                caducaEn = DateTime.MinValue;
            }
        }

        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(() =>
            {
                using (var db = new NVEntities())
                {
                    return db.Database.SqlQuery<CampanaAcumulaDistribuidores>(SQL_LEER).ToList();
                }
            }, registrarError);
        }

        internal static void Configurar(Func<IEnumerable<CampanaAcumulaDistribuidores>> nuevoLector, Action<Exception> registrarError = null, Func<DateTime> nuevoReloj = null)
        {
            lock (candado)
            {
                lector = nuevoLector;
                registrar = registrarError;
                reloj = nuevoReloj ?? (() => DateTime.UtcNow);
                enCache = NINGUNA;
                caducaEn = DateTime.MinValue;
            }
        }

        internal static void ConfigurarPara(params CampanaAcumulaDistribuidores[] campanas)
        {
            List<CampanaAcumulaDistribuidores> lista = campanas?.ToList() ?? new List<CampanaAcumulaDistribuidores>();
            Configurar(() => lista);
        }

        internal static void Reiniciar() => Configurar(null);
    }

    /// <summary>
    /// LA REGLA del descuento de campaña para distribuidores (Carlos 20/11/26): a un cliente distribuidor
    /// (<see cref="Clientes.ClientesDistribuidores"/>) el descuento de una campaña MARCADA
    /// (<see cref="CampanasAcumulanDistribuidores"/>) se aplica ENCIMA del suyo: primero el suyo y luego el de la campaña sobre lo
    /// que queda, <c>1 − (1 − propio) × (1 − campaña)</c> (30 % + 20 % → 44 %; 50 % + 25 % → 62,5 %). Para los demás clientes, y
    /// sin marca, nada cambia: el mejor de los dos, como siempre.
    /// <para>Criterios (prudentes): «propio» es lo que el motor calcula SIN la fila de campaña (su jerarquía de hoy: familia,
    /// grupo, cliente, su descuento propio del producto…); sin propio, queda el de la campaña. Si el cliente tiene PRECIO ESPECIAL
    /// propio del producto, manda como hoy y NO se compone. Nunca sale peor que lo de hoy (el máximo de los dos). Se redondea a 4
    /// decimales (los de la columna) con <see cref="RoundingHelper"/>. Solo cuenta la fila de campaña de PRODUCTO (las que crea
    /// el alta en bloque); una campaña por familia no se compone.</para>
    /// </summary>
    public static class DescuentoDistribuidorCampana
    {
        public const int DECIMALES = 4;

        /// <summary>1 − (1 − propio) × (1 − campaña), entre 0 y 1, a 4 decimales.</summary>
        public static decimal Compuesto(decimal propio, decimal campana)
        {
            decimal p = Math.Min(1M, Math.Max(0M, propio));
            decimal c = Math.Min(1M, Math.Max(0M, campana));
            return RoundingHelper.Round(1M - ((1M - p) * (1M - c)), DECIMALES);
        }

        /// <summary>
        /// El descuento final. <paramref name="descuentoDeHoy"/> es lo que el motor da hoy (el mejor de los dos);
        /// <paramref name="propioSinCampana"/>, lo que daría sin la fila de campaña.
        /// </summary>
        public static decimal Aplicar(decimal descuentoDeHoy, decimal propioSinCampana, decimal? descuentoCampana, bool campanaMarcada,
            bool esDistribuidor, bool tienePrecioEspecialPropio)
        {
            if (!descuentoCampana.HasValue || !campanaMarcada || !esDistribuidor || tienePrecioEspecialPropio)
            {
                return descuentoDeHoy;
            }
            return Math.Max(descuentoDeHoy, Compuesto(propioSinCampana, descuentoCampana.Value));
        }

        public static bool EstaMarcada(IEnumerable<CampanaAcumulaDistribuidores> marcas, string campana, DateTime dia)
        {
            if (string.IsNullOrWhiteSpace(campana))
            {
                return false;
            }
            string nombre = campana.Trim();
            return (marcas ?? Enumerable.Empty<CampanaAcumulaDistribuidores>()).Any(m =>
                string.Equals(m.Campana?.Trim(), nombre, StringComparison.OrdinalIgnoreCase)
                && Vigencia.EsVigente(m.FechaDesde, m.FechaHasta, dia.Date, 0));
        }

        /// <summary>
        /// Para la VALIDACIÓN del pedido: el mayor descuento que se puede autorizar a un distribuidor con una campaña marcada, o null
        /// si no hay ninguna. Campaña = la mejor fila de tarifa del PRODUCTO (sin cliente) de una campaña marcada; propio = el mayor
        /// de las demás filas autorizadas (las del cliente y las de tarifa por familia o grupo), con el mismo criterio generoso
        /// que el validador de siempre (cualquier fila que lo autorice).
        /// </summary>
        public static decimal? MaximoAutorizado(IEnumerable<DescuentosProducto> filasAutorizadas, string producto, Func<string, bool> campanaMarcada)
        {
            List<DescuentosProducto> filas = (filasAutorizadas ?? Enumerable.Empty<DescuentosProducto>()).Where(f => f != null).ToList();
            List<DescuentosProducto> deCampana = filas.Where(f => EsFilaDeCampanaDeProducto(f, producto) && campanaMarcada(f.Campana)).ToList();
            if (!deCampana.Any())
            {
                return null;
            }
            decimal campana = deCampana.Max(f => f.Descuento);
            decimal propio = filas.Where(f => !deCampana.Contains(f)).Select(f => f.Descuento).DefaultIfEmpty(0M).Max();
            return Compuesto(propio, campana);
        }

        /// <summary>Fila de tarifa de producto (sin cliente, sin proveedor, sin familia ni grupo) con nombre de campaña.</summary>
        public static bool EsFilaDeCampanaDeProducto(DescuentosProducto f, string producto) =>
            f != null && !string.IsNullOrWhiteSpace(f.Campana)
            && string.Equals(f.Nº_Producto?.Trim(), producto?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(f.Nº_Cliente) && string.IsNullOrWhiteSpace(f.NºProveedor)
            && f.Familia == null && f.GrupoProducto == null;
    }
}
