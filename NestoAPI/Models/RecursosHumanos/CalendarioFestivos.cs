using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Models.RecursosHumanos
{
    /// <summary>
    /// NestoAPI#779 (sigue a #777): el calendario de festivos sale de la tabla <c>dbo.Festivos</c> (Fecha, Tipo, Descripcion,
    /// Usuario, FechaModificacion; <c>Scripts/Issue779_Festivos.sql</c>), fuera del EDMX a propósito: se lee por SQL, como
    /// <c>DiasRepartoCodigosPostales</c>. Así un festivo nuevo (o un año entero) ya no necesita un deploy.
    /// <para>Respaldo (la lista de <see cref="GestorFestivos"/>, que se quitará cuando la tabla esté en producción):</para>
    /// <list type="bullet">
    /// <item>Sin lector configurado (las pruebas) o sin la tabla: la lista del código, como hasta ahora, sin ELMAH.</item>
    /// <item>Con la tabla pero sin ninguna fila del año consultado: ELMAH (una vez por año y proceso) y la lista del código de
    /// ese año si la tiene; si tampoco, ningún festivo (solo los fines de semana).</item>
    /// <item>Si falla la lectura: se queda con lo último que leyó (al arrancar, la lista del código) y lo vuelve a intentar al
    /// caducar.</item>
    /// </list>
    /// <para>Se lee como mucho una vez cada <see cref="DURACION_CACHE"/>; <see cref="Invalidar"/> (al escribir por
    /// <c>api/Festivos</c>) hace que la siguiente consulta vuelva a leer. El lector de la BD lo pone Startup
    /// (<see cref="UsarBaseDeDatos"/>).</para>
    /// </summary>
    public static class CalendarioFestivos
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);

        /// <summary>Sin tabla, una sola fila centinela con HayTabla = 0; con tabla (aunque esté vacía), sus filas con HayTabla = 1.</summary>
        internal const string SQL_LEER =
            "IF OBJECT_ID(N'dbo.Festivos', N'U') IS NULL " +
            "SELECT CAST(0 AS bit) AS HayTabla, CAST(NULL AS datetime) AS Fecha, CAST(NULL AS nvarchar(20)) AS Tipo, " +
            "CAST(NULL AS nvarchar(100)) AS Descripcion " +
            "ELSE SELECT CAST(1 AS bit) AS HayTabla, CAST(Fecha AS datetime) AS Fecha, RTRIM(Tipo) AS Tipo, Descripcion " +
            "FROM dbo.Festivos WITH (NOLOCK)";

        /// <summary>Lo que se ha leído de la BD.</summary>
        public class LecturaFestivos
        {
            /// <summary>False: la tabla no existe (todavía no se ha lanzado el script).</summary>
            public bool HayTabla { get; set; }
            public List<Festivo> Festivos { get; set; } = new List<Festivo>();
        }

        private class Estado
        {
            internal bool HayTabla;
            internal Dictionary<int, IReadOnlyList<Festivo>> PorAnio = new Dictionary<int, IReadOnlyList<Festivo>>();
        }

        private static readonly IReadOnlyList<Festivo> NINGUNO = new List<Festivo>();
        private static readonly object candado = new object();
        private static readonly HashSet<int> aniosAvisados = new HashSet<int>();
        private static Func<LecturaFestivos> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static Estado enCache = new Estado();
        private static DateTime caducaEn = DateTime.MinValue;

        /// <summary>
        /// Los festivos de ese año (de todos los tipos), de la tabla o, si no hay, de la lista del código. Nunca lanza ni
        /// devuelve null.
        /// </summary>
        public static IReadOnlyList<Festivo> DelAnio(int anio)
        {
            // Un año disparatado (las fechas centinela 31/12/2099, 2100…, o anteriores a 2000): sin festivos, sin ELMAH y sin tocar la
            // caché (los fines de semana siguen contando). El aviso legítimo del año que viene sin cargar no cambia.
            if (EsAnioDisparatado(anio, reloj().Year))
            {
                return NINGUNO;
            }
            Estado estado = Actual();
            if (!estado.HayTabla)
            {
                return GestorFestivos.FestivosDelCodigo(anio);
            }
            if (estado.PorAnio.TryGetValue(anio, out IReadOnlyList<Festivo> delAnio) && delAnio.Count > 0)
            {
                return delAnio;
            }
            IReadOnlyList<Festivo> delCodigo = GestorFestivos.FestivosDelCodigo(anio);
            AvisarAnioSinFestivos(anio, delCodigo.Count);
            return delCodigo;
        }

        /// <summary>Más de estos años por delante de hoy ya no es una fecha de verdad (las centinela son 2099 y 2100).</summary>
        public const int ANIOS_ADELANTE_MAXIMO = 5;
        public const int ANIO_MINIMO = 2000;

        /// <summary>Un año anterior a 2000 o más de <see cref="ANIOS_ADELANTE_MAXIMO"/> por delante del actual.</summary>
        public static bool EsAnioDisparatado(int anio, int anioActual) => anio < ANIO_MINIMO || anio > anioActual + ANIOS_ADELANTE_MAXIMO;

        /// <summary>¿La tabla tiene alguna fila de ese año? False si no hay tabla o no se ha podido leer.</summary>
        public static bool TablaTieneAnio(int anio)
        {
            Estado estado = Actual();
            return estado.HayTabla && estado.PorAnio.TryGetValue(anio, out IReadOnlyList<Festivo> delAnio) && delAnio.Count > 0;
        }

        /// <summary>Al escribir en la tabla: la siguiente consulta la vuelve a leer.</summary>
        public static void Invalidar()
        {
            lock (candado)
            {
                caducaEn = DateTime.MinValue;
            }
        }

        private static Estado Actual()
        {
            Func<LecturaFestivos> lectorActual = lector;
            if (lectorActual == null)
            {
                return enCache;
            }
            DateTime ahora = reloj();
            if (ahora < caducaEn)
            {
                return enCache;
            }
            Exception error = null;
            Estado resultado;
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
                    error = new Exception($"[Festivos #779] No se ha podido leer la tabla Festivos (se usa lo último leído): {ex.Message}", ex);
                }
                caducaEn = ahora + DURACION_CACHE;
                resultado = enCache;
            }
            Registrar(error);
            return resultado;
        }

        private static void AvisarAnioSinFestivos(int anio, int delCodigo)
        {
            lock (candado)
            {
                if (!aniosAvisados.Add(anio))
                {
                    return;
                }
            }
            Registrar(new Exception(delCodigo > 0
                ? $"[Festivos #779] La tabla Festivos no tiene ningún festivo de {anio}: se usan los {delCodigo} de la lista del código. Cárguelos desde Nesto o con POST api/Festivos."
                : $"[Festivos #779] La tabla Festivos no tiene ningún festivo de {anio} y la lista del código tampoco: todo {anio} cuenta como laborable salvo los fines de semana. Cárguelos desde Nesto o con POST api/Festivos."));
        }

        private static void Registrar(Exception error)
        {
            if (error == null)
            {
                return;
            }
            try
            {
                registrar?.Invoke(error);
            }
            catch
            {
                // El registro nunca puede tumbar el calendario.
            }
        }

        private static Estado Interpretar(LecturaFestivos lectura)
        {
            var estado = new Estado { HayTabla = lectura?.HayTabla == true };
            if (!estado.HayTabla)
            {
                return estado;
            }
            estado.PorAnio = (lectura.Festivos ?? new List<Festivo>())
                .Where(f => f != null)
                .GroupBy(f => f.Fecha.Year)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Festivo>)g.ToList());
            return estado;
        }

        private class FilaFestivo
        {
            public bool HayTabla { get; set; }
            public DateTime? Fecha { get; set; }
            public string Tipo { get; set; }
            public string Descripcion { get; set; }
        }

        /// <summary>Startup: lee la tabla con su propio contexto.</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(LeerDeLaBaseDeDatos, registrarError);
        }

        internal static LecturaFestivos LeerDeLaBaseDeDatos()
        {
            List<FilaFestivo> filas;
            using (var db = new NVEntities())
            {
                filas = db.Database.SqlQuery<FilaFestivo>(SQL_LEER).ToList();
            }
            var lectura = new LecturaFestivos { HayTabla = filas.Count == 0 || filas.Any(f => f.HayTabla) };
            var desconocidas = new List<string>();
            foreach (FilaFestivo fila in filas.Where(f => f.HayTabla && f.Fecha.HasValue))
            {
                if (TiposFestivo.TryParse(fila.Tipo, out TipoFestivo tipo))
                {
                    lectura.Festivos.Add(new Festivo { Fecha = fila.Fecha.Value.Date, TipoFestivo = tipo, Fiesta = fila.Descripcion });
                }
                else
                {
                    desconocidas.Add($"{fila.Fecha.Value:dd/MM/yyyy} «{fila.Tipo}»");
                }
            }
            if (desconocidas.Any())
            {
                Registrar(new Exception($"[Festivos #779] Filas de la tabla Festivos con un tipo desconocido (no cuentan): {string.Join("; ", desconocidas)}"));
            }
            return lectura;
        }

        /// <summary>Pone el lector (null = sin BD: la lista del código) y vacía la caché y los avisos ya dados.</summary>
        internal static void Configurar(Func<LecturaFestivos> nuevoLector, Action<Exception> registrarError = null,
            Func<DateTime> nuevoReloj = null)
        {
            lock (candado)
            {
                lector = nuevoLector;
                registrar = registrarError;
                reloj = nuevoReloj ?? (() => DateTime.UtcNow);
                enCache = new Estado();
                caducaEn = DateTime.MinValue;
                aniosAvisados.Clear();
            }
        }

        /// <summary>Pruebas: vuelve al estado de arranque (sin lector: la lista del código).</summary>
        internal static void Reiniciar() => Configurar(null);
    }

    /// <summary>
    /// NestoAPI#779: el Tipo de la tabla Festivos es el nombre del enum <see cref="TipoFestivo"/> (Nacional, Autonómica,
    /// Empresa, Algete, Alcobendas, Madrid). Al leer da igual la tilde y las mayúsculas («Autonomica», «nacional»).
    /// </summary>
    public static class TiposFestivo
    {
        /// <summary>Lo que se guarda en la columna Tipo.</summary>
        public static string Texto(TipoFestivo tipo) => tipo.ToString();

        public static IReadOnlyList<string> Todos => Enum.GetValues(typeof(TipoFestivo)).Cast<TipoFestivo>().Select(Texto).ToList();

        public static bool TryParse(string texto, out TipoFestivo tipo)
        {
            tipo = default(TipoFestivo);
            string buscado = SinTildes(texto?.Trim());
            if (string.IsNullOrEmpty(buscado))
            {
                return false;
            }
            foreach (TipoFestivo candidato in Enum.GetValues(typeof(TipoFestivo)))
            {
                if (string.Equals(SinTildes(candidato.ToString()), buscado, StringComparison.OrdinalIgnoreCase))
                {
                    tipo = candidato;
                    return true;
                }
            }
            return false;
        }

        private static string SinTildes(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return texto;
            }
            var resultado = new StringBuilder(texto.Length);
            foreach (char c in texto.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = resultado.Append(c);
                }
            }
            return resultado.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
