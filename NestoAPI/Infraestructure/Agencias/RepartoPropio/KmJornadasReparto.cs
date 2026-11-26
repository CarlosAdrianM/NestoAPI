using Hangfire;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#881, corte 5 (26/11/26): kilómetros de cada jornada del reparto propio, SOLO por mapa (decisión de Carlos 19/11/26:
    // posiciones reales de las paradas en su orden; nada que teclee el repartidor).
    //
    //  - Recorrido = sede (Algete) → cada parada cerrada ese día con posición (RepartoParadasTiempos.Latitud/Longitud, la del aparato
    //    al entregar, recoger o poner la incidencia), por la hora real (FechaHoraFin) → sede. Las paradas sin posición no cuentan (se
    //    dicen en ParadasSinPosicion) y dos seguidas en el mismo sitio (< 30 m) cuentan una vez.
    //  - Kilómetros por carretera con el MISMO motor que el fin de ruta: OSRM /table (IMatrizTiempos, tramo a tramo, en bloques de como
    //    mucho 60 puntos) y, si no responde, la línea recta × 1,3 (MatrizTiempos.Heuristica): entonces Aproximada = true.
    //  - Job nocturno reparto-km-jornadas (3:50 Madrid, cola «indicadores»): las jornadas de los últimos DIAS_REVISION días que no tienen
    //    km, cuyas paradas con posición han cambiado o que se calcularon sin OSRM. Guarda en dbo.RepartoJornadasKm
    //    (Scripts/Issue881_RepartoJornadasKm.sql; sin ella, nada y el informe sigue con Km null).
    //  - El informe (corte 4) lo lee en su misma pasada: Km y KmAproximados por jornada; Km, JornadasConKm y MediaKm en los totales.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Una fila de RepartoJornadasKm.</summary>
    public class KmJornadaReparto
    {
        /// <summary>Sin dominio, como RepartoParadasTiempos.</summary>
        public string Repartidor { get; set; }
        public DateTime Dia { get; set; }
        public decimal Km { get; set; }
        /// <summary>Paradas con posición que entran en el recorrido.</summary>
        public int Paradas { get; set; }
        public int ParadasSinPosicion { get; set; }
        /// <summary>OSRM o Heuristica (si algún tramo salió en línea recta, Heuristica).</summary>
        public string Motor { get; set; }
        public bool Aproximada { get; set; }
        public DateTime FechaCalculo { get; set; }
    }

    /// <summary>Una parada cerrada con su posición (lo que hace falta de RepartoParadasTiempos).</summary>
    public class PosicionParadaReparto
    {
        public long Id { get; set; }
        public string Repartidor { get; set; }
        public DateTime Dia { get; set; }
        public DateTime FechaHoraFin { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
    }

    /// <summary>NestoAPI#881 corte 5: LA REGLA (pura) del recorrido de una jornada.</summary>
    public static class CalculoKmJornada
    {
        public const double METROS_MISMO_SITIO = 30;

        /// <summary>
        /// Sede → paradas con posición por la hora real (y el Id) → sede. Sin posición o en (0, 0): fuera (<paramref name="sinPosicion"/>);
        /// dos seguidas a menos de <see cref="METROS_MISMO_SITIO"/> m, una.
        /// </summary>
        public static List<CoordenadasGeo> Recorrido(IEnumerable<PosicionParadaReparto> paradas, CoordenadasGeo sede, out int sinPosicion, out int conPosicion)
        {
            var puntos = new List<CoordenadasGeo> { sede };
            sinPosicion = 0;
            conPosicion = 0;
            foreach (PosicionParadaReparto p in (paradas ?? Enumerable.Empty<PosicionParadaReparto>()).Where(p => p != null).OrderBy(p => p.FechaHoraFin).ThenBy(p => p.Id))
            {
                if (!p.Latitud.HasValue || !p.Longitud.HasValue || (p.Latitud.Value == 0 && p.Longitud.Value == 0))
                {
                    sinPosicion++;
                    continue;
                }
                conPosicion++;
                var punto = new CoordenadasGeo(p.Latitud.Value, p.Longitud.Value);
                if (HeuristicaRutas.DistanciaKm(puntos[puntos.Count - 1], punto) * 1000d >= METROS_MISMO_SITIO)
                {
                    puntos.Add(punto);
                }
            }
            puntos.Add(sede);
            return puntos;
        }

        /// <summary>
        /// Los bloques de puntos consecutivos (como mucho <paramref name="maximo"/>), solapados en uno: así cada tramo i → i+1 está en
        /// algún bloque y la matriz de cada bloque cabe en una petición.
        /// </summary>
        public static List<(int Desde, int Hasta)> Bloques(int puntos, int maximo)
        {
            var bloques = new List<(int, int)>();
            if (puntos < 2 || maximo < 2)
            {
                return bloques;
            }
            for (int desde = 0; desde < puntos - 1; desde += maximo - 1)
            {
                bloques.Add((desde, Math.Min(desde + maximo - 1, puntos - 1)));
            }
            return bloques;
        }

        /// <summary>¿Hay que (re)calcular? Sin fila, con otro número de paradas con posición o calculada sin el mapa.</summary>
        public static bool HayQueCalcular(KmJornadaReparto existente, int paradasConPosicion)
            => existente == null || existente.Paradas != paradasConPosicion || !string.Equals(existente.Motor, OrdenSugeridoRepartoDTO.MOTOR_OSRM, StringComparison.Ordinal);
    }

    /// <summary>NestoAPI#881 corte 5: RepartoParadasTiempos (lectura) y RepartoJornadasKm.</summary>
    public interface IRepositorioKmJornadas
    {
        /// <summary>¿Existen RepartoParadasTiempos y RepartoJornadasKm?</summary>
        bool Disponible();
        Task<List<PosicionParadaReparto>> LeerParadas(DateTime desde, DateTime hasta);
        Task<List<KmJornadaReparto>> Leer(DateTime desde, DateTime hasta);
        /// <summary>Crea o sustituye la de ese repartidor y día.</summary>
        Task Guardar(KmJornadaReparto km);
    }

    public class ServicioKmJornadasReparto
    {
        public const int DIAS_REVISION = 7;

        private readonly IRepositorioKmJornadas repositorio;
        private readonly IMatrizTiempos matriz;
        private readonly Func<DateTime> ahora;
        private readonly CoordenadasGeo sede;

        /// <param name="matriz">El motor del fin de ruta (OSRM /table). Null: solo la línea recta.</param>
        public ServicioKmJornadasReparto(IRepositorioKmJornadas repositorio, IMatrizTiempos matriz, Func<DateTime> ahora = null, CoordenadasGeo sede = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.matriz = matriz;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.sede = sede ?? SedesReparto.ALGETE;
        }

        /// <summary>Los km de una jornada (no guarda). Nunca lanza por el motor: si no responde, línea recta y Aproximada.</summary>
        public async Task<KmJornadaReparto> Calcular(string repartidor, DateTime dia, IReadOnlyCollection<PosicionParadaReparto> paradas)
        {
            List<CoordenadasGeo> puntos = CalculoKmJornada.Recorrido(paradas, sede, out int sinPosicion, out int conPosicion);
            double km = 0;
            bool aproximada = false;
            foreach ((int desde, int hasta) in CalculoKmJornada.Bloques(puntos.Count, MatrizTiemposOsrm.MAXIMO_PUNTOS))
            {
                List<CoordenadasGeo> bloque = puntos.Skip(desde).Take(hasta - desde + 1).ToList();
                MatrizTiempos m = null;
                if (matriz != null)
                {
                    try
                    {
                        m = await matriz.Calcular(bloque).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[Reparto #881] Km de la jornada de {repartidor} del {dia:dd/MM/yyyy}: el mapa ha fallado y va en línea recta. {ex.Message}", ex));
                    }
                }
                if (m?.Kilometros == null)
                {
                    m = MatrizTiempos.Heuristica(bloque);
                    aproximada = true;
                }
                for (int i = 0; i < bloque.Count - 1; i++)
                {
                    km += m.Kilometros[i, i + 1];
                }
            }
            return new KmJornadaReparto
            {
                Repartidor = repartidor,
                Dia = dia.Date,
                Km = Math.Round((decimal)km, 1),
                Paradas = conPosicion,
                ParadasSinPosicion = sinPosicion,
                Motor = aproximada ? OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA : OrdenSugeridoRepartoDTO.MOTOR_OSRM,
                Aproximada = aproximada || sinPosicion > 0,
                FechaCalculo = ahora()
            };
        }

        /// <summary>
        /// Las jornadas de los últimos <paramref name="dias"/> días (sin hoy) que hay que (re)calcular. Devuelve cuántas se han guardado.
        /// Sin las tablas, 0. Una jornada que falla no para las demás (ELMAH).
        /// </summary>
        public async Task<int> CalcularPendientes(int dias = DIAS_REVISION)
        {
            if (!repositorio.Disponible())
            {
                return 0;
            }
            DateTime hasta = ahora().Date.AddDays(-1);
            DateTime desde = hasta.AddDays(-(Math.Max(1, dias) - 1));
            List<PosicionParadaReparto> paradas = await repositorio.LeerParadas(desde, hasta).ConfigureAwait(false) ?? new List<PosicionParadaReparto>();
            Dictionary<string, KmJornadaReparto> hechas = (await repositorio.Leer(desde, hasta).ConfigureAwait(false) ?? new List<KmJornadaReparto>())
                .GroupBy(Clave, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            int guardadas = 0;
            foreach (IGrouping<string, PosicionParadaReparto> jornada in paradas.Where(p => !string.IsNullOrWhiteSpace(p.Repartidor))
                .GroupBy(p => Clave(ServicioRepartoPropio.NombreRepartidor(p.Repartidor), p.Dia), StringComparer.OrdinalIgnoreCase))
            {
                List<PosicionParadaReparto> suyas = jornada.ToList();
                string repartidor = ServicioRepartoPropio.NombreRepartidor(suyas[0].Repartidor);
                DateTime dia = suyas[0].Dia.Date;
                _ = CalculoKmJornada.Recorrido(suyas, sede, out _, out int conPosicion);
                hechas.TryGetValue(jornada.Key, out KmJornadaReparto existente);
                if (!CalculoKmJornada.HayQueCalcular(existente, conPosicion))
                {
                    continue;
                }
                try
                {
                    await repositorio.Guardar(await Calcular(repartidor, dia, suyas).ConfigureAwait(false)).ConfigureAwait(false);
                    guardadas++;
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Reparto #881] No se han podido guardar los km de la jornada de {repartidor} del {dia:dd/MM/yyyy}: {ex.Message}", ex));
                }
            }
            return guardadas;
        }

        private static string Clave(KmJornadaReparto k) => Clave(k.Repartidor, k.Dia);

        private static string Clave(string repartidor, DateTime dia) => (repartidor ?? string.Empty).Trim() + "|" + dia.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    public class RepositorioKmJornadasReparto : IRepositorioKmJornadas
    {
        public const string TABLA = "RepartoJornadasKm";
        internal const string NOMBRE_APLICACION = "NestoAPI-KmReparto";
        private const int TIMEOUT = 60;

        internal const string SQL_PARADAS = @"
SELECT Id, RTRIM(Repartidor), Dia, FechaHoraFin, Latitud, Longitud
FROM dbo." + RepositorioTiemposReparto.TABLA_TIEMPOS + @" WITH (NOLOCK) WHERE Dia >= @desde AND Dia <= @hasta";

        internal const string SQL_LEER = @"
SELECT RTRIM(Repartidor), Dia, Km, Paradas, ParadasSinPosicion, RTRIM(Motor), Aproximada, FechaCalculo
FROM dbo." + TABLA + @" WITH (NOLOCK) WHERE Dia >= @desde AND Dia <= @hasta";

        internal const string SQL_GUARDAR = @"
UPDATE dbo." + TABLA + @" SET Km = @Km, Paradas = @Paradas, ParadasSinPosicion = @SinPosicion, Motor = @Motor, Aproximada = @Aproximada, FechaCalculo = @FechaCalculo
WHERE Repartidor = @Repartidor AND Dia = @Dia;
IF @@ROWCOUNT = 0
    INSERT INTO dbo." + TABLA + @" (Repartidor, Dia, Km, Paradas, ParadasSinPosicion, Motor, Aproximada, FechaCalculo)
    VALUES (@Repartidor, @Dia, @Km, @Paradas, @SinPosicion, @Motor, @Aproximada, @FechaCalculo);";

        private readonly string cadenaConexion;

        public RepositorioKmJornadasReparto(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = cadenaConexion;
        }

        public static RepositorioKmJornadasReparto DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioKmJornadasReparto(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        public bool Disponible()
        {
            try
            {
                using (var conexion = new SqlConnection(cadenaConexion))
                using (var comando = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@a, 'U') IS NOT NULL AND OBJECT_ID(@b, 'U') IS NOT NULL THEN 1 ELSE 0 END", conexion) { CommandTimeout = TIMEOUT })
                {
                    conexion.Open();
                    comando.Parameters.Add("@a", SqlDbType.NVarChar, 200).Value = "dbo." + RepositorioTiemposReparto.TABLA_TIEMPOS;
                    comando.Parameters.Add("@b", SqlDbType.NVarChar, 200).Value = "dbo." + TABLA;
                    return Convert.ToInt32(comando.ExecuteScalar()) == 1;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<List<PosicionParadaReparto>> LeerParadas(DateTime desde, DateTime hasta)
        {
            var filas = new List<PosicionParadaReparto>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_PARADAS, conexion) { CommandTimeout = TIMEOUT })
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde.Date;
                comando.Parameters.Add("@hasta", SqlDbType.DateTime).Value = hasta.Date;
                using (SqlDataReader l = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await l.ReadAsync().ConfigureAwait(false))
                    {
                        filas.Add(new PosicionParadaReparto
                        {
                            Id = Convert.ToInt64(l.GetValue(0)),
                            Repartidor = l.IsDBNull(1) ? null : l.GetString(1),
                            Dia = l.GetDateTime(2),
                            FechaHoraFin = l.GetDateTime(3),
                            Latitud = l.IsDBNull(4) ? (decimal?)null : Convert.ToDecimal(l.GetValue(4)),
                            Longitud = l.IsDBNull(5) ? (decimal?)null : Convert.ToDecimal(l.GetValue(5))
                        });
                    }
                }
            }
            return filas;
        }

        public async Task<List<KmJornadaReparto>> Leer(DateTime desde, DateTime hasta)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                return await LeerKm(conexion, desde, hasta, TIMEOUT).ConfigureAwait(false);
            }
        }

        /// <summary>También la usa el informe (su misma conexión).</summary>
        internal static async Task<List<KmJornadaReparto>> LeerKm(SqlConnection conexion, DateTime desde, DateTime hasta, int timeout)
        {
            var filas = new List<KmJornadaReparto>();
            using (var comando = new SqlCommand(SQL_LEER, conexion) { CommandTimeout = timeout })
            {
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde.Date;
                comando.Parameters.Add("@hasta", SqlDbType.DateTime).Value = hasta.Date;
                using (SqlDataReader l = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await l.ReadAsync().ConfigureAwait(false))
                    {
                        filas.Add(new KmJornadaReparto
                        {
                            Repartidor = l.IsDBNull(0) ? null : l.GetString(0),
                            Dia = l.GetDateTime(1),
                            Km = Convert.ToDecimal(l.GetValue(2)),
                            Paradas = Convert.ToInt32(l.GetValue(3)),
                            ParadasSinPosicion = Convert.ToInt32(l.GetValue(4)),
                            Motor = l.IsDBNull(5) ? null : l.GetString(5),
                            Aproximada = Convert.ToBoolean(l.GetValue(6)),
                            FechaCalculo = l.GetDateTime(7)
                        });
                    }
                }
            }
            return filas;
        }

        public async Task Guardar(KmJornadaReparto km)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_GUARDAR, conexion) { CommandTimeout = TIMEOUT })
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                comando.Parameters.Add("@Repartidor", SqlDbType.VarChar, 50).Value = km.Repartidor ?? string.Empty;
                comando.Parameters.Add("@Dia", SqlDbType.Date).Value = km.Dia.Date;
                comando.Parameters.Add("@Km", SqlDbType.Decimal).Value = km.Km;
                comando.Parameters["@Km"].Precision = 9;
                comando.Parameters["@Km"].Scale = 1;
                comando.Parameters.Add("@Paradas", SqlDbType.Int).Value = km.Paradas;
                comando.Parameters.Add("@SinPosicion", SqlDbType.Int).Value = km.ParadasSinPosicion;
                comando.Parameters.Add("@Motor", SqlDbType.VarChar, 15).Value = km.Motor ?? OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA;
                comando.Parameters.Add("@Aproximada", SqlDbType.Bit).Value = km.Aproximada;
                comando.Parameters.Add("@FechaCalculo", SqlDbType.DateTime).Value = km.FechaCalculo;
                _ = await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>NestoAPI#881 corte 5: el job nocturno de los km (todos los días a las 3:50 de Madrid, cola «indicadores»). Nunca lanza.</summary>
    public static class KmJornadasRepartoJobsService
    {
        public const string JOB_ID = "reparto-km-jornadas";
        public const string CRON = "50 3 * * *";
        public const string COLA = IndicadoresStock.IndicadoresStockJobsService.COLA;

        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static async Task Calcular()
        {
            try
            {
                _ = await new ServicioKmJornadasReparto(RepositorioKmJornadasReparto.DesdeConfiguracion(), new MatrizTiemposOsrm()).CalcularPendientes().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] El cálculo nocturno de los km de las jornadas ha fallado: {ex.Message}", ex));
            }
        }
    }
}
