using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NestoAPI.Models;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#767: un punto WGS84.</summary>
    public class CoordenadasGeo
    {
        public CoordenadasGeo(decimal latitud, decimal longitud)
        {
            Latitud = latitud;
            Longitud = longitud;
        }

        public decimal Latitud { get; }
        public decimal Longitud { get; }

        public static bool Validas(decimal? latitud, decimal? longitud)
            => latitud.HasValue && longitud.HasValue && latitud.Value >= -90m && latitud.Value <= 90m && longitud.Value >= -180m && longitud.Value <= 180m
                && !(latitud.Value == 0m && longitud.Value == 0m);

        public override string ToString() => Latitud.ToString(CultureInfo.InvariantCulture) + "," + Longitud.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// NestoAPI#767: las coordenadas FIJAS de las sedes (origen y vuelta del reparto, y las paradas de reposición). Sacadas el
    /// 30/10/26 de Nominatim con las direcciones de <see cref="PedidosVenta.TiendasRecogida"/> (las de la web):
    /// <list type="bullet">
    /// <item>Algete, C/ Río Tiétar 11 (28119): 40.598625, -3.521404 — Nominatim da la calle (no tiene el número): la nave está en
    /// ella, así que vale para ordenar paradas.</item>
    /// <item>Alcobendas, C/ La Granja 1 (28108): 40.526141, -3.660018 — el portal.</item>
    /// <item>Reina (Madrid centro), C/ Reina 5 (28004): 40.420467, -3.700505 — el portal.</item>
    /// </list>
    /// Son constantes y no un parámetro: no cambian, y un parámetro en el Web.config se pierde al publicar.
    /// </summary>
    public static class SedesReparto
    {
        public static readonly CoordenadasGeo ALGETE = new CoordenadasGeo(40.598625m, -3.521404m);
        public static readonly CoordenadasGeo ALCOBENDAS = new CoordenadasGeo(40.526141m, -3.660018m);
        public static readonly CoordenadasGeo REINA = new CoordenadasGeo(40.420467m, -3.700505m);

        /// <summary>Las del almacén (ALG, REI, ALC); null si no es una sede.</summary>
        public static CoordenadasGeo DeAlmacen(string almacen)
        {
            switch (almacen?.Trim().ToUpperInvariant())
            {
                case Constantes.Almacenes.ALGETE: return ALGETE;
                case Constantes.Almacenes.ALCOBENDAS: return ALCOBENDAS;
                case Constantes.Almacenes.REINA: return REINA;
                default: return null;
            }
        }
    }

    /// <summary>
    /// NestoAPI#767: la dirección de una parada tal como viene del envío. La <see cref="Clave"/> (SHA-1 de la dirección, el CP y la
    /// población normalizados: mayúsculas, sin tildes ni signos) es la de la caché DireccionesCoordenadas.
    /// </summary>
    public class DireccionParada
    {
        public DireccionParada(string direccion, string codigoPostal, string poblacion)
        {
            Direccion = direccion?.Trim();
            CodigoPostal = codigoPostal?.Trim();
            Poblacion = poblacion?.Trim();
            Clave = CalcularClave(Direccion, CodigoPostal, Poblacion);
        }

        public string Direccion { get; }
        public string CodigoPostal { get; }
        public string Poblacion { get; }
        /// <summary>40 caracteres hexadecimales en mayúsculas.</summary>
        public string Clave { get; }

        /// <summary>Sin CP ni población no hay nada que buscar.</summary>
        public bool Geocodificable => !string.IsNullOrWhiteSpace(CodigoPostal) || !string.IsNullOrWhiteSpace(Poblacion);

        internal static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }
            string descompuesto = texto.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
                if (categoria == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }
                _ = sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        internal static string CalcularClave(string direccion, string codigoPostal, string poblacion)
        {
            string texto = Normalizar(direccion) + "|" + Normalizar(codigoPostal) + "|" + Normalizar(poblacion);
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(texto));
                return string.Concat(hash.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// La calle para Nominatim: abreviaturas desplegadas («C/» → «Calle») y sin lo que va detrás del número (piso, letra,
        /// local, nave…), que Nominatim no entiende y le hace fallar. «C/ RUBENS, 12 - 9ºD» → «Calle RUBENS 12».
        /// </summary>
        internal static string CalleParaBuscar(string direccion)
        {
            if (string.IsNullOrWhiteSpace(direccion))
            {
                return null;
            }
            string calle = " " + direccion.Trim() + " ";
            var abreviaturas = new (string Patron, string Texto)[]
            {
                (@"\sC\s*/\s*", " Calle "), (@"\sCL\.?\s", " Calle "), (@"\sCALLE\s", " Calle "),
                (@"\sAVDA?\.?\s*", " Avenida "), (@"\sAV\.\s*", " Avenida "),
                (@"\sP[ºª°]\.?\s*", " Paseo "), (@"\sPZA?\.?\s*", " Plaza "), (@"\sCTRA\.?\s*", " Carretera "),
                (@"\sRDA\.?\s*", " Ronda "), (@"\sTRAV?\.?\s", " Travesía "), (@"\sGTA\.?\s*", " Glorieta ")
            };
            foreach (var (patron, texto) in abreviaturas)
            {
                calle = Regex.Replace(calle, patron, texto, RegexOptions.IgnoreCase);
            }
            // Hasta el primer número (el portal); lo de detrás es piso, puerta o local.
            Match numero = Regex.Match(calle, @"^(.*?[A-Za-zÁÉÍÓÚÜÑáéíóúüñ][^0-9]*?)\b(\d+)");
            if (numero.Success)
            {
                calle = numero.Groups[1].Value + " " + numero.Groups[2].Value;
            }
            calle = Regex.Replace(calle, @"[,;\-]+", " ");
            calle = Regex.Replace(calle, @"\s+", " ").Trim();
            return calle.Length == 0 ? null : calle;
        }
    }

    /// <summary>NestoAPI#767: lo que dice el geocodificador. Null = no la encuentra.</summary>
    public class ResultadoGeocodificacion
    {
        public const string PRECISION_DIRECCION = "Direccion";
        public const string PRECISION_CODIGO_POSTAL = "CodigoPostal";
        public const string PRECISION_NO_ENCONTRADA = "NoEncontrada";

        public decimal Latitud { get; set; }
        public decimal Longitud { get; set; }
    }

    /// <summary>NestoAPI#767: buscar coordenadas. Lanza si el servicio no responde (para no apuntar «no encontrada» por un corte).</summary>
    public interface IGeocodificador
    {
        string Fuente { get; }
        /// <summary>Con calle (null: solo CP y población). Null si no la encuentra.</summary>
        Task<ResultadoGeocodificacion> Buscar(string calle, string codigoPostal, string poblacion);
    }

    /// <summary>
    /// NestoAPI#767: Nominatim de OpenStreetMap (gratis, sin clave). Cumple su política de uso
    /// (https://operations.osmfoundation.org/policies/nominatim/): User-Agent propio con contacto, como mucho 1 petición por
    /// segundo para TODO el proceso (un candado estático y una espera mínima entre peticiones) y nunca en la petición de la PDA:
    /// solo lo llaman los jobs de <see cref="GeocodificacionRepartoJobsService"/>.
    /// </summary>
    public class GeocodificadorNominatim : IGeocodificador
    {
        public const string USER_AGENT = "NestoAPI/1.0 (nuevavision.es; carlosadrian@nuevavision.es)";
        internal const string URL = "https://nominatim.openstreetmap.org/search";
        internal static readonly TimeSpan ESPERA_MINIMA = TimeSpan.FromMilliseconds(1100);
        private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(15);

        private static readonly HttpClient cliente = CrearCliente();
        private static readonly SemaphoreSlim candado = new SemaphoreSlim(1, 1);
        private static DateTime ultimaPeticion = DateTime.MinValue;

        public string Fuente => "Nominatim";

        private static HttpClient CrearCliente()
        {
            var http = new HttpClient { Timeout = TIMEOUT };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
            http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
            return http;
        }

        internal static string Url(string calle, string codigoPostal, string poblacion)
        {
            var partes = new List<string>();
            if (!string.IsNullOrWhiteSpace(calle))
            {
                partes.Add("street=" + Uri.EscapeDataString(calle.Trim()));
            }
            if (!string.IsNullOrWhiteSpace(codigoPostal))
            {
                partes.Add("postalcode=" + Uri.EscapeDataString(codigoPostal.Trim()));
            }
            if (!string.IsNullOrWhiteSpace(poblacion))
            {
                partes.Add("city=" + Uri.EscapeDataString(poblacion.Trim()));
            }
            partes.Add("countrycodes=es");
            partes.Add("format=jsonv2");
            partes.Add("limit=1");
            return URL + "?" + string.Join("&", partes);
        }

        public async Task<ResultadoGeocodificacion> Buscar(string calle, string codigoPostal, string poblacion)
            => Leer(await PedirConTurno(Url(calle, codigoPostal, poblacion), CancellationToken.None).ConfigureAwait(false));

        /// <summary>
        /// Un GET a Nominatim respetando su política para TODO el proceso (el mismo candado y la misma espera mínima para la
        /// búsqueda de los jobs y la geocodificación inversa de la firma, NestoAPI#770). Con <paramref name="cancelar"/> se corta
        /// también la espera del turno.
        /// </summary>
        internal static async Task<string> PedirConTurno(string url, CancellationToken cancelar)
        {
            await candado.WaitAsync(cancelar).ConfigureAwait(false);
            try
            {
                TimeSpan falta = ESPERA_MINIMA - (DateTime.UtcNow - ultimaPeticion);
                if (falta > TimeSpan.Zero)
                {
                    await Task.Delay(falta, cancelar).ConfigureAwait(false);
                }
                try
                {
                    using (HttpResponseMessage respuesta = await cliente.GetAsync(url, cancelar).ConfigureAwait(false))
                    {
                        _ = respuesta.EnsureSuccessStatusCode();
                        return await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    ultimaPeticion = DateTime.UtcNow;
                }
            }
            finally
            {
                _ = candado.Release();
            }
        }

        /// <summary>El primer resultado del JSON de /search, o null si no hay.</summary>
        internal static ResultadoGeocodificacion Leer(string json)
        {
            JArray resultados = JArray.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            JToken primero = resultados.FirstOrDefault();
            if (primero == null
                || !decimal.TryParse((string)primero["lat"], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal lat)
                || !decimal.TryParse((string)primero["lon"], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal lon))
            {
                return null;
            }
            return new ResultadoGeocodificacion { Latitud = Math.Round(lat, 6), Longitud = Math.Round(lon, 6) };
        }
    }

    /// <summary>NestoAPI#767: una fila de DireccionesCoordenadas.</summary>
    public class CoordenadasEnCache
    {
        public string Clave { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        /// <summary>Null si no se encontró (caché negativa: se vuelve a intentar pasados <see cref="ServicioGeocodificacion.DIAS_REINTENTO_NO_ENCONTRADA"/> días).</summary>
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        public string Precision { get; set; }
        public string Fuente { get; set; }
        public DateTime Fecha { get; set; }
    }

    public interface IRepositorioCoordenadas
    {
        /// <summary>¿Existe DireccionesCoordenadas (Scripts/Issue767_DireccionesCoordenadas.sql)?</summary>
        bool ScriptAplicado();
        Task<List<CoordenadasEnCache>> Leer(IReadOnlyCollection<string> claves);
        /// <summary>Upsert por Clave.</summary>
        Task Guardar(CoordenadasEnCache fila);
    }

    /// <summary>NestoAPI#767: DireccionesCoordenadas, fuera del EDMX (SQL a mano), como las demás tablas del reparto propio.</summary>
    public class RepositorioCoordenadas : IRepositorioCoordenadas
    {
        public const string TABLA = "DireccionesCoordenadas";
        private static readonly Regex CLAVE_VALIDA = new Regex("^[0-9A-F]{40}$", RegexOptions.Compiled);

        private const string SQL_GUARDAR = @"
SET NOCOUNT ON;
UPDATE dbo." + TABLA + @" SET Direccion = @Direccion, CodigoPostal = @CodigoPostal, Poblacion = @Poblacion, Latitud = @Latitud,
    Longitud = @Longitud, Precision = @Precision, Fuente = @Fuente, Fecha = GETDATE()
WHERE Clave = @Clave;
IF @@ROWCOUNT = 0
    INSERT INTO dbo." + TABLA + @" (Clave, Direccion, CodigoPostal, Poblacion, Latitud, Longitud, Precision, Fuente, Fecha)
    VALUES (@Clave, @Direccion, @CodigoPostal, @Poblacion, @Latitud, @Longitud, @Precision, @Fuente, GETDATE());";

        private readonly NVEntities db;

        public RepositorioCoordenadas(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public Task<List<CoordenadasEnCache>> Leer(IReadOnlyCollection<string> claves)
        {
            // Solo hexadecimal de 40: se puede poner en el texto sin inyección posible.
            List<string> validas = (claves ?? new string[0]).Where(c => c != null && CLAVE_VALIDA.IsMatch(c)).Distinct().ToList();
            if (validas.Count == 0)
            {
                return Task.FromResult(new List<CoordenadasEnCache>());
            }
            string lista = string.Join(", ", validas.Select(c => "'" + c + "'"));
            return db.Database.SqlQuery<CoordenadasEnCache>(
                $"SELECT RTRIM(Clave) AS Clave, Direccion, CodigoPostal, Poblacion, Latitud, Longitud, RTRIM(Precision) AS Precision, RTRIM(Fuente) AS Fuente, Fecha " +
                $"FROM dbo.{TABLA} WITH (NOLOCK) WHERE Clave IN ({lista})").ToListAsync();
        }

        public Task Guardar(CoordenadasEnCache fila)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR,
                new SqlParameter("@Clave", SqlDbType.Char, 40) { Value = fila.Clave },
                Texto("@Direccion", fila.Direccion, 200),
                Texto("@CodigoPostal", fila.CodigoPostal, 20),
                Texto("@Poblacion", fila.Poblacion, 100),
                Decimal("@Latitud", fila.Latitud),
                Decimal("@Longitud", fila.Longitud),
                new SqlParameter("@Precision", SqlDbType.VarChar, 12) { Value = fila.Precision },
                new SqlParameter("@Fuente", SqlDbType.VarChar, 20) { Value = fila.Fuente ?? string.Empty });
        }

        private static SqlParameter Texto(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.NVarChar, largo) { Value = valor == null ? (object)DBNull.Value : (valor.Length > largo ? valor.Substring(0, largo) : valor) };

        private static SqlParameter Decimal(string nombre, decimal? valor)
            => new SqlParameter(nombre, SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = valor.HasValue ? (object)valor.Value : DBNull.Value };
    }

    /// <summary>
    /// NestoAPI#767: lo que las paradas necesitan de las coordenadas: leer la caché (rápido, nunca sale a internet) y pedir que se
    /// geocodifique lo que falta (en segundo plano).
    /// </summary>
    public interface ICoordenadasParadas
    {
        /// <summary>Las que ya están en la caché, por <see cref="DireccionParada.Clave"/>. Sin el script, ninguna.</summary>
        Task<Dictionary<string, CoordenadasGeo>> Leer(IReadOnlyCollection<DireccionParada> direcciones);
        /// <summary>Que un job las busque (no espera; varias llamadas seguidas encolan uno solo). Nunca lanza.</summary>
        void PedirGeocodificacion();
    }

    /// <summary>NestoAPI#767: leer y rellenar la caché DireccionesCoordenadas.</summary>
    public class ServicioGeocodificacion : ICoordenadasParadas
    {
        /// <summary>Una «no encontrada» se vuelve a intentar pasado este tiempo (OSM mejora; la dirección puede corregirse con la misma clave no).</summary>
        public const int DIAS_REINTENTO_NO_ENCONTRADA = 30;

        private readonly IRepositorioCoordenadas repositorio;
        private readonly IGeocodificador geocodificador;
        private readonly Action pedir;
        private readonly Func<DateTime> ahora;

        public ServicioGeocodificacion(IRepositorioCoordenadas repositorio, IGeocodificador geocodificador = null, Action pedir = null, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.geocodificador = geocodificador;
            this.pedir = pedir;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<Dictionary<string, CoordenadasGeo>> Leer(IReadOnlyCollection<DireccionParada> direcciones)
        {
            var resultado = new Dictionary<string, CoordenadasGeo>();
            if (direcciones == null || direcciones.Count == 0 || !repositorio.ScriptAplicado())
            {
                return resultado;
            }
            List<CoordenadasEnCache> filas = await repositorio.Leer(direcciones.Select(d => d.Clave).Distinct().ToList()).ConfigureAwait(false)
                ?? new List<CoordenadasEnCache>();
            foreach (CoordenadasEnCache fila in filas.Where(f => CoordenadasGeo.Validas(f.Latitud, f.Longitud)))
            {
                resultado[fila.Clave] = new CoordenadasGeo(fila.Latitud.Value, fila.Longitud.Value);
            }
            return resultado;
        }

        public void PedirGeocodificacion()
        {
            if (pedir == null || !repositorio.ScriptAplicado())
            {
                return;
            }
            try
            {
                pedir();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto propio #767] No se pudo encolar la geocodificación: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Geocodifica las que no están en la caché (o son «no encontrada» de hace más de un mes), como mucho
        /// <paramref name="maximo"/> direcciones distintas. Primero con la calle; si no, solo CP y población; si tampoco, «no
        /// encontrada» (la parada se queda donde esté). Si el servicio falla, para y no apunta nada de esa dirección (la próxima
        /// pasada lo reintenta). Devuelve cuántas ha guardado.
        /// </summary>
        public async Task<int> Geocodificar(IEnumerable<DireccionParada> direcciones, int maximo)
        {
            if (geocodificador == null || !repositorio.ScriptAplicado())
            {
                return 0;
            }
            List<DireccionParada> unicas = (direcciones ?? Enumerable.Empty<DireccionParada>())
                .Where(d => d != null && d.Geocodificable)
                .GroupBy(d => d.Clave).Select(g => g.First()).ToList();
            if (unicas.Count == 0)
            {
                return 0;
            }
            var enCache = new Dictionary<string, CoordenadasEnCache>();
            foreach (List<string> lote in Lotes(unicas.Select(d => d.Clave).ToList(), 500))
            {
                foreach (CoordenadasEnCache fila in await repositorio.Leer(lote).ConfigureAwait(false) ?? new List<CoordenadasEnCache>())
                {
                    enCache[fila.Clave] = fila;
                }
            }
            DateTime limite = ahora().AddDays(-DIAS_REINTENTO_NO_ENCONTRADA);
            List<DireccionParada> pendientes = unicas
                .Where(d => !enCache.TryGetValue(d.Clave, out CoordenadasEnCache fila)
                    || (!CoordenadasGeo.Validas(fila.Latitud, fila.Longitud) && fila.Fecha < limite))
                .Take(Math.Max(0, maximo))
                .ToList();

            int guardadas = 0;
            foreach (DireccionParada direccion in pendientes)
            {
                ResultadoGeocodificacion encontrada;
                string precision;
                try
                {
                    string calle = DireccionParada.CalleParaBuscar(direccion.Direccion);
                    encontrada = calle == null ? null : await geocodificador.Buscar(calle, direccion.CodigoPostal, direccion.Poblacion).ConfigureAwait(false);
                    precision = ResultadoGeocodificacion.PRECISION_DIRECCION;
                    if (encontrada == null)
                    {
                        encontrada = await geocodificador.Buscar(null, direccion.CodigoPostal, direccion.Poblacion).ConfigureAwait(false);
                        precision = encontrada == null ? ResultadoGeocodificacion.PRECISION_NO_ENCONTRADA : ResultadoGeocodificacion.PRECISION_CODIGO_POSTAL;
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Reparto propio #767] {geocodificador.Fuente} no responde: se deja la geocodificación para la próxima pasada. {ex.Message}", ex));
                    break;
                }
                await repositorio.Guardar(new CoordenadasEnCache
                {
                    Clave = direccion.Clave,
                    Direccion = direccion.Direccion,
                    CodigoPostal = direccion.CodigoPostal,
                    Poblacion = direccion.Poblacion,
                    Latitud = encontrada?.Latitud,
                    Longitud = encontrada?.Longitud,
                    Precision = precision,
                    Fuente = geocodificador.Fuente,
                    Fecha = ahora()
                }).ConfigureAwait(false);
                guardadas++;
            }
            return guardadas;
        }

        private static IEnumerable<List<string>> Lotes(List<string> claves, int tamano)
        {
            for (int i = 0; i < claves.Count; i += tamano)
            {
                yield return claves.Skip(i).Take(tamano).ToList();
            }
        }
    }
}
