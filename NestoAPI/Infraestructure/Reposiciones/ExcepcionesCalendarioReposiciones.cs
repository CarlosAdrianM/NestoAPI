using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>NestoAPI#867: los dos tipos de excepción (columna Tipo de dbo.ReposicionesCalendarioExcepciones).</summary>
    public static class TiposExcepcionCalendarioReposicion
    {
        /// <summary>Ese día no hay la reposición que dice el calendario semanal.</summary>
        public const string SIN_REPOSICION = "SinReposicion";
        /// <summary>Ese día hay una reposición que el calendario semanal no tiene (con sus horas).</summary>
        public const string REPOSICION_EXTRA = "ReposicionExtra";

        /// <summary>El tipo bien escrito, o null si no es ninguno de los dos (da igual las mayúsculas).</summary>
        public static string Normalizar(string tipo)
        {
            string limpio = tipo?.Trim();
            if (string.Equals(limpio, SIN_REPOSICION, StringComparison.OrdinalIgnoreCase))
            {
                return SIN_REPOSICION;
            }
            return string.Equals(limpio, REPOSICION_EXTRA, StringComparison.OrdinalIgnoreCase) ? REPOSICION_EXTRA : null;
        }
    }

    /// <summary>
    /// NestoAPI#867: una excepción del calendario de reposiciones en un día de LLEGADA concreto (tabla
    /// dbo.ReposicionesCalendarioExcepciones, fuera del EDMX).
    /// </summary>
    public class ExcepcionCalendarioReposicionDTO
    {
        /// <summary>Solo lectura (el POST busca por empresa, origen, destino y fecha).</summary>
        public int? Id { get; set; }
        public string Empresa { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>El día de llegada (sin hora).</summary>
        public DateTime FechaLlegada { get; set; }
        /// <summary><see cref="TiposExcepcionCalendarioReposicion.SIN_REPOSICION"/> o <see cref="TiposExcepcionCalendarioReposicion.REPOSICION_EXTRA"/>.</summary>
        public string Tipo { get; set; }
        /// <summary>Solo para la extra. Sin ella, la de la ruta en el calendario.</summary>
        public TimeSpan? HoraCierre { get; set; }
        /// <summary>Solo para la extra. Tiene que ser la del resto de la ruta (sin ella, esa).</summary>
        public byte? LaborablesAntelacionCierre { get; set; }
        /// <summary>Solo para la extra. Sin ella, la de la ruta en el calendario.</summary>
        public TimeSpan? HoraLlegadaHabitual { get; set; }
        public string Motivo { get; set; }
        /// <summary>Solo lectura.</summary>
        public string Usuario { get; set; }
        /// <summary>Solo lectura.</summary>
        public DateTime? FechaModificacion { get; set; }
        /// <summary>
        /// Solo lectura: cuándo cierra (o cerraba, si es un día quitado) la reposición de ese día. Desde ese momento la
        /// excepción ya no se puede cambiar ni borrar. Null si ese día no hay viaje (festivo, sin reparto propio).
        /// </summary>
        public DateTime? CierraEl { get; set; }
    }

    /// <summary>
    /// NestoAPI#867: LA REGLA (pura) de qué reposiciones hay de un origen a un destino con llegada un día concreto, juntando
    /// el calendario semanal (ReposicionesCalendario) y las excepciones por fecha. La usan <see cref="CalculadoraFechaReposicion"/>
    /// (próxima llegada, fechas de entrega, los cortes del job de la reposición automática) y la validación de las excepciones.
    /// <list type="bullet">
    /// <item>Sin excepción ese día: las filas ACTIVAS del calendario de ese día de la semana (como siempre).</item>
    /// <item><see cref="TiposExcepcionCalendarioReposicion.SIN_REPOSICION"/>: ninguna (el job no la rellena ni avisa
    /// «fuera de plazo»; la próxima llegada pasa a la siguiente).</item>
    /// <item><see cref="TiposExcepcionCalendarioReposicion.REPOSICION_EXTRA"/>: una, con las horas de la excepción (las que
    /// falten, de la primera fila activa de la ruta). Si la ruta no tiene ninguna fila activa, ninguna: la extra se apoya en
    /// una ruta que exista.</item>
    /// </list>
    /// Los festivos y los días sin reparto propio los sigue mirando la calculadora: una extra un festivo no cuenta.
    /// </summary>
    public static class ReglaCalendarioReposiciones
    {
        /// <summary>La excepción de esa ruta ese día de llegada, o null.</summary>
        public static ExcepcionCalendarioReposicionDTO ExcepcionDelDia(IEnumerable<ExcepcionCalendarioReposicionDTO> excepciones,
            string empresa, string origen, string destino, DateTime diaLlegada)
        {
            string empresaLimpia = Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string origenLimpio = Limpiar(origen);
            string destinoLimpio = Limpiar(destino);
            DateTime dia = diaLlegada.Date;
            return (excepciones ?? Enumerable.Empty<ExcepcionCalendarioReposicionDTO>())
                .Where(e => e != null
                    && TiposExcepcionCalendarioReposicion.Normalizar(e.Tipo) != null
                    && (Limpiar(e.Empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO) == empresaLimpia
                    && Limpiar(e.Origen) == origenLimpio
                    && Limpiar(e.Destino) == destinoLimpio
                    && e.FechaLlegada.Date == dia)
                .OrderByDescending(e => e.FechaModificacion ?? DateTime.MinValue)
                .FirstOrDefault();
        }

        /// <summary>
        /// Las reposiciones de esa ruta que llegan ese día (sin mirar festivos). <paramref name="calendario"/> puede traer
        /// otras rutas y filas inactivas: se filtran aquí.
        /// </summary>
        public static List<ReposicionCalendario> ViajesDelDia(IEnumerable<ReposicionCalendario> calendario,
            IEnumerable<ExcepcionCalendarioReposicionDTO> excepciones, string empresa, string origen, string destino, DateTime diaLlegada)
        {
            string empresaLimpia = Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string origenLimpio = Limpiar(origen);
            string destinoLimpio = Limpiar(destino);
            DateTime dia = diaLlegada.Date;
            byte diaSemana = CalculadoraFechaReposicion.DiaSemana(dia);

            List<ReposicionCalendario> filasRuta = (calendario ?? Enumerable.Empty<ReposicionCalendario>())
                .Where(f => f != null && f.Activo
                    && (Limpiar(f.Empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO) == empresaLimpia
                    && Limpiar(f.AlmacenOrigen) == origenLimpio
                    && Limpiar(f.AlmacenDestino) == destinoLimpio)
                .ToList();

            ExcepcionCalendarioReposicionDTO excepcion = ExcepcionDelDia(excepciones, empresaLimpia, origenLimpio, destinoLimpio, dia);
            string tipo = TiposExcepcionCalendarioReposicion.Normalizar(excepcion?.Tipo);
            if (tipo == TiposExcepcionCalendarioReposicion.SIN_REPOSICION)
            {
                return new List<ReposicionCalendario>();
            }
            if (tipo == TiposExcepcionCalendarioReposicion.REPOSICION_EXTRA)
            {
                ReposicionCalendario modelo = filasRuta.OrderBy(f => f.DiaSemana).ThenBy(f => f.HoraCierre).FirstOrDefault();
                if (modelo == null)
                {
                    return new List<ReposicionCalendario>();
                }
                return new List<ReposicionCalendario>
                {
                    new ReposicionCalendario
                    {
                        Id = 0,
                        Empresa = empresaLimpia,
                        AlmacenOrigen = origenLimpio,
                        AlmacenDestino = destinoLimpio,
                        DiaSemana = diaSemana,
                        HoraCierre = excepcion.HoraCierre ?? modelo.HoraCierre,
                        HoraLlegadaHabitual = excepcion.HoraLlegadaHabitual ?? modelo.HoraLlegadaHabitual,
                        LaborablesAntelacionCierre = excepcion.LaborablesAntelacionCierre ?? modelo.LaborablesAntelacionCierre,
                        Activo = true,
                        Usuario = excepcion.Usuario,
                        FechaModificacion = excepcion.FechaModificacion ?? DateTime.MinValue
                    }
                };
            }
            return filasRuta.Where(f => f.DiaSemana == diaSemana).ToList();
        }

        internal static string Limpiar(string valor)
        {
            string limpio = valor?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }

    /// <summary>
    /// NestoAPI#867: las excepciones vigentes (de hace <see cref="DIAS_ATRAS"/> días en adelante, todas las empresas) en
    /// caché, como <c>CalendarioFestivos</c>: se leen como mucho una vez cada <see cref="DURACION_CACHE"/> y
    /// <see cref="Invalidar"/> (al escribir por api/Reposiciones/Calendario/Excepciones) hace que la siguiente consulta vuelva
    /// a leer. Sin lector (las pruebas) o sin la tabla: ninguna excepción (todo como antes de #867). Si falla la lectura,
    /// lo último leído y ELMAH. Nunca lanza ni devuelve null. El lector lo pone Startup (<see cref="UsarBaseDeDatos"/>).
    /// </summary>
    public static class ExcepcionesCalendarioReposiciones
    {
        /// <summary>Corta: un cambio desde otro servidor tarda como mucho esto en verse.</summary>
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Hacia atrás también: con antelación, el corte de hoy puede ser de una llegada de dentro de unos días, pero una
        /// consulta de hoy nunca mira llegadas de hace más de esto.
        /// </summary>
        public const int DIAS_ATRAS = 14;

        internal const string SQL_LEER =
            "IF OBJECT_ID(N'dbo.ReposicionesCalendarioExcepciones', N'U') IS NULL " +
            "SELECT CAST(0 AS bit) AS HayTabla, CAST(0 AS int) AS Id, CAST(NULL AS varchar(3)) AS Empresa, CAST(NULL AS varchar(3)) AS Origen, " +
            "CAST(NULL AS varchar(3)) AS Destino, CAST(NULL AS datetime) AS FechaLlegada, CAST(NULL AS varchar(20)) AS Tipo, " +
            "CAST(NULL AS time(0)) AS HoraCierre, CAST(NULL AS tinyint) AS LaborablesAntelacionCierre, CAST(NULL AS time(0)) AS HoraLlegadaHabitual, " +
            "CAST(NULL AS nvarchar(200)) AS Motivo, CAST(NULL AS nvarchar(50)) AS Usuario, CAST(NULL AS datetime) AS FechaModificacion " +
            "ELSE SELECT CAST(1 AS bit) AS HayTabla, Id, RTRIM(Empresa) AS Empresa, RTRIM(Origen) AS Origen, RTRIM(Destino) AS Destino, " +
            "CAST(FechaLlegada AS datetime) AS FechaLlegada, RTRIM(Tipo) AS Tipo, HoraCierre, LaborablesAntelacionCierre, HoraLlegadaHabitual, " +
            "Motivo, Usuario, FechaModificacion FROM dbo.ReposicionesCalendarioExcepciones WITH (NOLOCK) " +
            "WHERE FechaLlegada >= DATEADD(day, -14, CAST(GETDATE() AS date))";

        private static readonly IReadOnlyList<ExcepcionCalendarioReposicionDTO> NINGUNA = new List<ExcepcionCalendarioReposicionDTO>();
        private static readonly object candado = new object();
        private static Func<IReadOnlyList<ExcepcionCalendarioReposicionDTO>> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static IReadOnlyList<ExcepcionCalendarioReposicionDTO> enCache = NINGUNA;
        private static DateTime caducaEn = DateTime.MinValue;

        /// <summary>Las excepciones vigentes. Nunca lanza ni devuelve null.</summary>
        public static IReadOnlyList<ExcepcionCalendarioReposicionDTO> Vigentes()
        {
            Func<IReadOnlyList<ExcepcionCalendarioReposicionDTO>> lectorActual = lector;
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
            IReadOnlyList<ExcepcionCalendarioReposicionDTO> resultado;
            lock (candado)
            {
                if (ahora < caducaEn)
                {
                    return enCache;
                }
                try
                {
                    enCache = lectorActual() ?? NINGUNA;
                }
                catch (Exception ex)
                {
                    error = new Exception($"[Calendario de reposiciones #867] No se han podido leer las excepciones (se usa lo último leído): {ex.Message}", ex);
                }
                caducaEn = ahora + DURACION_CACHE;
                resultado = enCache;
            }
            if (error != null)
            {
                try
                {
                    registrar?.Invoke(error);
                }
                catch
                {
                    // El registro nunca puede tumbar el calendario.
                }
            }
            return resultado;
        }

        /// <summary>Al escribir en la tabla: la siguiente consulta la vuelve a leer.</summary>
        public static void Invalidar()
        {
            lock (candado)
            {
                caducaEn = DateTime.MinValue;
            }
        }

        private class FilaLeida
        {
            public bool HayTabla { get; set; }
            public int Id { get; set; }
            public string Empresa { get; set; }
            public string Origen { get; set; }
            public string Destino { get; set; }
            public DateTime? FechaLlegada { get; set; }
            public string Tipo { get; set; }
            public TimeSpan? HoraCierre { get; set; }
            public byte? LaborablesAntelacionCierre { get; set; }
            public TimeSpan? HoraLlegadaHabitual { get; set; }
            public string Motivo { get; set; }
            public string Usuario { get; set; }
            public DateTime? FechaModificacion { get; set; }
        }

        /// <summary>Startup: lee la tabla con su propio contexto.</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(LeerDeLaBaseDeDatos, registrarError);
        }

        internal static IReadOnlyList<ExcepcionCalendarioReposicionDTO> LeerDeLaBaseDeDatos()
        {
            List<FilaLeida> filas;
            using (var db = new NVEntities())
            {
                filas = db.Database.SqlQuery<FilaLeida>(SQL_LEER).ToList();
            }
            return filas
                .Where(f => f.HayTabla && f.FechaLlegada.HasValue)
                .Select(f => new ExcepcionCalendarioReposicionDTO
                {
                    Id = f.Id,
                    Empresa = f.Empresa?.Trim(),
                    Origen = f.Origen?.Trim(),
                    Destino = f.Destino?.Trim(),
                    FechaLlegada = f.FechaLlegada.Value.Date,
                    Tipo = f.Tipo?.Trim(),
                    HoraCierre = f.HoraCierre,
                    LaborablesAntelacionCierre = f.LaborablesAntelacionCierre,
                    HoraLlegadaHabitual = f.HoraLlegadaHabitual,
                    Motivo = f.Motivo,
                    Usuario = f.Usuario?.Trim(),
                    FechaModificacion = f.FechaModificacion
                })
                .ToList();
        }

        /// <summary>Pone el lector (null = sin BD: ninguna excepción) y vacía la caché.</summary>
        internal static void Configurar(Func<IReadOnlyList<ExcepcionCalendarioReposicionDTO>> nuevoLector,
            Action<Exception> registrarError = null, Func<DateTime> nuevoReloj = null)
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

        /// <summary>Pruebas: vuelve al estado de arranque (sin lector: ninguna excepción).</summary>
        internal static void Reiniciar() => Configurar(null);
    }
}
