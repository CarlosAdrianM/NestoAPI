using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#764 (Carlos 30/10/26): qué día de la semana reparte NUESTRA ruta en cada código postal. Sale de la tabla
    /// <c>dbo.CodigosPostalesDiaReparto</c> (Empresa, CodigoPostal, Ruta '01'..'05', FechaModificacion), fuera del EDMX a
    /// propósito: se lee por SQL y, si la tabla no existe, no hay días (todo como antes). Las rutas 01-05 NO son la ruta de
    /// ningún cliente ni pedido: solo dicen qué día saldría un pedido por nuestra ruta.
    /// <para>La regla (<see cref="DiaRutaInterna"/>): un pedido tiene día de reparto si VA por ruta interna (su ruta está en
    /// <see cref="RutasPropiasConfiguradas"/>) y el código postal de entrega (el del contacto del pedido) está en la tabla. Sin
    /// día (la 16 de la zona de Algete, un CP fuera de la tabla, o un pedido de agencia) todo sigue como antes.</para>
    /// <para>Solo vale la empresa 1 (como las rutas); los pedidos de la 3 se miran con ella. Se lee como mucho una vez cada
    /// <see cref="DURACION_CACHE"/>; sin lector configurado (las pruebas) no toca la BD. El lector de la BD lo pone Startup
    /// (<see cref="UsarBaseDeDatos"/>).</para>
    /// </summary>
    public static class DiasRepartoCodigosPostales
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);

        internal const string SQL_LEER =
            "IF OBJECT_ID(N'dbo.CodigosPostalesDiaReparto', N'U') IS NOT NULL " +
            "SELECT RTRIM(CodigoPostal) AS CodigoPostal, RTRIM(Ruta) AS Ruta FROM dbo.CodigosPostalesDiaReparto WITH (NOLOCK) " +
            "WHERE Empresa = @p0";

        private static readonly IReadOnlyDictionary<string, DayOfWeek> VACIO = new Dictionary<string, DayOfWeek>();
        private static readonly object candado = new object();
        private static Func<IEnumerable<KeyValuePair<string, string>>> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static IReadOnlyDictionary<string, DayOfWeek> enCache = VACIO;
        private static DateTime caducaEn = DateTime.MinValue;

        /// <summary>Código postal → día de la semana. Nunca lanza; vacío si no hay tabla o no se ha podido leer.</summary>
        public static IReadOnlyDictionary<string, DayOfWeek> Actuales
        {
            get
            {
                Func<IEnumerable<KeyValuePair<string, string>>> lectorActual = lector;
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
                        enCache = Interpretar(lectorActual());
                    }
                    catch (Exception ex)
                    {
                        // Se queda con lo último que leyó y lo vuelve a intentar al caducar.
                        try
                        {
                            registrar?.Invoke(new Exception($"NestoAPI#764: no se han podido leer los días de reparto por código postal: {ex.Message}", ex));
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

        /// <summary>El día de reparto de nuestra ruta en ese código postal, o null si no tiene.</summary>
        public static DayOfWeek? DiaDe(string codigoPostal)
        {
            string cp = codigoPostal?.Trim();
            if (string.IsNullOrEmpty(cp))
            {
                return null;
            }
            return Actuales.TryGetValue(cp, out DayOfWeek dia) ? dia : (DayOfWeek?)null;
        }

        /// <summary>¿La ruta es interna (de las de <see cref="RutasPropiasConfiguradas"/>) y hay algún código postal con día?
        /// Sirve para no leer el código postal del pedido cuando no puede tener día.</summary>
        public static bool PuedeTenerDia(string ruta) => RutasPropiasConfiguradas.Contiene(ruta) && Actuales.Count > 0;

        /// <summary>
        /// LA REGLA: el día de reparto de un pedido que va por ruta interna (<paramref name="ruta"/> en
        /// <see cref="RutasPropiasConfiguradas"/>) al código postal <paramref name="codigoPostal"/>; null si no va por ruta
        /// interna o el código postal no tiene día (entonces, como hasta ahora).
        /// </summary>
        public static DayOfWeek? DiaRutaInterna(string ruta, string codigoPostal)
        {
            return RutasPropiasConfiguradas.Contiene(ruta) ? DiaDe(codigoPostal) : null;
        }

        /// <summary>Filas (CP, '01'..'05') → diccionario. Ignora las filas vacías y las rutas que no son de 01 a 05.</summary>
        internal static IReadOnlyDictionary<string, DayOfWeek> Interpretar(IEnumerable<KeyValuePair<string, string>> filas)
        {
            var resultado = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> fila in filas ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                string cp = fila.Key?.Trim();
                DayOfWeek? dia = CalendarioRutasDia.DiaDeLaSemana(fila.Value);
                if (!string.IsNullOrEmpty(cp) && dia.HasValue)
                {
                    resultado[cp] = dia.Value;
                }
            }
            return resultado;
        }

        /// <summary>Startup: lee la tabla con su propio contexto.</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(LeerDeLaBaseDeDatos, registrarError);
        }

        private class FilaDiaReparto
        {
            public string CodigoPostal { get; set; }
            public string Ruta { get; set; }
        }

        internal static IEnumerable<KeyValuePair<string, string>> LeerDeLaBaseDeDatos()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<FilaDiaReparto>(SQL_LEER, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                    .ToList()
                    .Select(f => new KeyValuePair<string, string>(f.CodigoPostal, f.Ruta))
                    .ToList();
            }
        }

        /// <summary>Pone el lector (null = sin BD: ningún código postal con día) y vacía la caché.</summary>
        internal static void Configurar(Func<IEnumerable<KeyValuePair<string, string>>> nuevoLector, Action<Exception> registrarError = null,
            Func<DateTime> nuevoReloj = null)
        {
            lock (candado)
            {
                lector = nuevoLector;
                registrar = registrarError;
                reloj = nuevoReloj ?? (() => DateTime.UtcNow);
                enCache = VACIO;
                caducaEn = DateTime.MinValue;
            }
        }

        /// <summary>Pruebas: con estos códigos postales y días («28001» → «03»).</summary>
        internal static void ConfigurarPara(IDictionary<string, string> codigosPostales)
        {
            List<KeyValuePair<string, string>> filas = codigosPostales?.ToList() ?? new List<KeyValuePair<string, string>>();
            Configurar(() => filas);
        }

        /// <summary>Pruebas: vuelve al estado de arranque (sin lector, sin días).</summary>
        internal static void Reiniciar() => Configurar(null);
    }
}
