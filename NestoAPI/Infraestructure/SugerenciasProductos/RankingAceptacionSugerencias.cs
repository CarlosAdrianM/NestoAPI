using Microsoft.ML;
using ModeloSugerenciasProductos.Aceptacion;
using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Features;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>NestoAPI#671 (corte 2e): qué hace Calcular con el modelo 2 (Web.config <see cref="RankingAceptacionSugerencias.CLAVE_MODO"/>).</summary>
    public enum ModoRankingAceptacion
    {
        /// <summary>Ni se calcula ni se registra.</summary>
        Apagado,
        /// <summary>Se calcula y se registra en SugerenciasProductosSombra; lo que se enseña no cambia (por defecto).</summary>
        Sombra,
        /// <summary>Como Sombra y, si hay un modelo 2 entrenado que pasa la puerta, ordena los huecos nuevos por P2.</summary>
        Activo
    }

    /// <summary>Lo que sale del modelo 2 (o del bayesiano) para los candidatos de una llamada.</summary>
    public class PuntuacionAceptacion
    {
        /// <summary>«m2-AAAAMMDD_HHmm» (modelo entrenado) o «bayes-1».</summary>
        public string Version { get; set; }
        /// <summary>True si es un modelo 2 entrenado que ha pasado la puerta (el único que puede ordenar en modo Activo).</summary>
        public bool EsModeloEntrenado { get; set; }
        public IList<float> Puntuaciones { get; set; }
    }

    /// <summary>NestoAPI#671 (corte 2e): el modelo 2 tal y como lo usa Calcular (sustituible en los tests).</summary>
    public interface IRankingAceptacionSugerencias
    {
        ModoRankingAceptacion Modo { get; }

        /// <summary>
        /// P2 de cada candidato (mismo orden), con las tasas de aceptación de ahora. <paramref name="instantaneas"/> son las
        /// features del modelo 1 de cada candidato (puede haber nulls). Null si está apagado o algo falla (a ELMAH una vez). Nunca lanza.
        /// </summary>
        PuntuacionAceptacion Puntuar(IList<MostradaAceptacion> candidatos, IList<ModeloSugerenciaEntrada> instantaneas);
    }

    /// <summary>
    /// NestoAPI#671 (corte 2e): el de verdad. Con un zip del modelo 2 en la carpeta de modelos
    /// («modelo_aceptacion_sugerencias_AAAAMMDD_HHmm.zip») cuyo json diga PasaPuerta y la versión de features de esta API,
    /// puntúa con él; si no, con el reordenador bayesiano (P1 × tasa suavizada), que solo sirve para la sombra. Las tasas de
    /// aceptación (todas las respuestas) se leen como mucho cada <see cref="MINUTOS_RELEER_TASAS"/> minutos; la carpeta, cada
    /// <see cref="PredictorSugerenciasProductos.SEGUNDOS_RELEER_CARPETA"/> s; el zip se carga una vez hasta que cambie.
    /// <para>Interruptor: appSetting <see cref="CLAVE_MODO"/> = Apagado | Sombra | Activo; sin él (o con otra cosa), Sombra.
    /// Activo solo ordena con un modelo ENTRENADO que haya pasado la puerta: el bayesiano nunca cambia lo que se enseña.</para>
    /// </summary>
    public class RankingAceptacionSugerencias : IRankingAceptacionSugerencias
    {
        public const string CLAVE_MODO = "ModeloSugerencias:RankingAceptacion";
        public const int MINUTOS_RELEER_TASAS = 10;
        internal const string PREFIJO_FICHERO = "modelo_aceptacion_sugerencias_";
        internal const string PREFIJO_VERSION = RespuestasAceptacion.PREFIJO_MODELO2;

        public static readonly RankingAceptacionSugerencias Instancia = new RankingAceptacionSugerencias(
            () => LeerModo(clave => ConfigurationManager.AppSettings[clave]),
            () => AlmacenModeloSugerencias.Produccion().Carpeta,
            () => SugerenciasAceptacionSql.LeerRespondidas(ConfigurationManager.ConnectionStrings["NestoConnection"].ConnectionString),
            null, null);

        private readonly Func<ModoRankingAceptacion> modo;
        private readonly Func<string> carpeta;
        private readonly Func<List<MostradaAceptacion>> leerRespondidas;
        private readonly Action<Exception> registrarError;
        private readonly Func<DateTime> ahoraUtc;
        private readonly object bloqueo = new object();
        private readonly HashSet<string> avisados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private TasasAceptacion tasas;
        private DateTime tasasLeidasUtc;
        private string modeloElegido;
        private DateTime carpetaLeidaUtc;
        private bool carpetaLeida;
        private string rutaCargada;
        private DateTime fechaCargada;
        private ITransformer modeloCargado;
        private MLContext contextoCargado;
        private string rutaFallida;

        internal RankingAceptacionSugerencias(Func<ModoRankingAceptacion> modo, Func<string> carpeta, Func<List<MostradaAceptacion>> leerRespondidas,
            Action<Exception> registrarError, Func<DateTime> ahoraUtc)
        {
            this.modo = modo ?? (() => ModoRankingAceptacion.Sombra);
            this.carpeta = carpeta;
            this.leerRespondidas = leerRespondidas;
            this.registrarError = registrarError ?? ElmahHelper.Log;
            this.ahoraUtc = ahoraUtc ?? (() => DateTime.UtcNow);
        }

        /// <summary>Apagado | Sombra | Activo (sin distinguir mayúsculas); cualquier otra cosa o nada, Sombra.</summary>
        internal static ModoRankingAceptacion LeerModo(Func<string, string> leerParametro)
        {
            string valor = leerParametro?.Invoke(CLAVE_MODO)?.Trim();
            return Enum.TryParse(valor, true, out ModoRankingAceptacion m) && Enum.IsDefined(typeof(ModoRankingAceptacion), m)
                ? m : ModoRankingAceptacion.Sombra;
        }

        public ModoRankingAceptacion Modo
        {
            get
            {
                try
                {
                    return modo();
                }
                catch (Exception)
                {
                    return ModoRankingAceptacion.Sombra;
                }
            }
        }

        public PuntuacionAceptacion Puntuar(IList<MostradaAceptacion> candidatos, IList<ModeloSugerenciaEntrada> instantaneas)
        {
            if (candidatos == null || candidatos.Count == 0 || Modo == ModoRankingAceptacion.Apagado)
            {
                return null;
            }
            try
            {
                TasasAceptacion actuales = Tasas();
                List<ModeloAceptacionEntrada> entradas = candidatos
                    .Select((c, i) => CalculadoraFeaturesAceptacion.Calcular<ModeloAceptacionEntrada>(c,
                        instantaneas != null && i < instantaneas.Count ? instantaneas[i] : null, actuales))
                    .ToList();

                Tuple<ITransformer, MLContext, string> modelo = ModeloEntrenado();
                if (modelo != null)
                {
                    try
                    {
                        List<float> p2 = EntrenadorAceptacion.Probabilidades(modelo.Item2, modelo.Item1, entradas);
                        if (p2.Count == entradas.Count && !p2.Any(float.IsNaN))
                        {
                            return new PuntuacionAceptacion { Version = modelo.Item3, EsModeloEntrenado = true, Puntuaciones = p2 };
                        }
                        throw new InvalidOperationException($"El modelo de aceptación ha devuelto {p2.Count} probabilidades para {entradas.Count} candidatos (o alguna no es un número).");
                    }
                    catch (Exception ex)
                    {
                        MarcarFallido();
                        AvisarUnaVez("prediccion|" + modelo.Item3, new Exception($"[Sugerencias #671] Ha fallado el modelo de aceptación {modelo.Item3}: la sombra sigue con el bayesiano. {ex.Message}", ex));
                    }
                }
                return new PuntuacionAceptacion
                {
                    Version = ReordenadorBayesiano.VERSION,
                    EsModeloEntrenado = false,
                    Puntuaciones = candidatos.Select(c => ReordenadorBayesiano.Puntuar(c.TieneP1 ? (float)c.Puntuacion : 0f, actuales, c.Producto, c.Motivo)).ToList()
                };
            }
            catch (Exception ex)
            {
                AvisarUnaVez("puntuar", new Exception($"[Sugerencias #671] No se ha podido calcular la sombra del modelo de aceptación: {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>Las tasas con todas las respuestas; si no se pueden leer, las de antes (o vacías) y a ELMAH una vez.</summary>
        private TasasAceptacion Tasas()
        {
            DateTime ahora = ahoraUtc();
            lock (bloqueo)
            {
                if (tasas != null && (ahora - tasasLeidasUtc).TotalMinutes < MINUTOS_RELEER_TASAS)
                {
                    return tasas;
                }
            }
            TasasAceptacion nuevas;
            try
            {
                nuevas = TasasAceptacion.Desde(leerRespondidas?.Invoke() ?? new List<MostradaAceptacion>());
            }
            catch (Exception ex)
            {
                AvisarUnaVez("tasas", new Exception($"[Sugerencias #671] No se han podido leer las respuestas para las tasas de aceptación: {ex.Message}", ex));
                lock (bloqueo)
                {
                    nuevas = tasas ?? new TasasAceptacion();
                }
            }
            lock (bloqueo)
            {
                tasas = nuevas;
                tasasLeidasUtc = ahora;
                return nuevas;
            }
        }

        private Tuple<ITransformer, MLContext, string> ModeloEntrenado()
        {
            string ruta = null;
            try
            {
                ruta = Elegido();
                if (ruta == null)
                {
                    return null;
                }
                DateTime fecha = File.GetLastWriteTimeUtc(ruta);
                lock (bloqueo)
                {
                    if (rutaFallida == ruta)
                    {
                        return null;
                    }
                    if (modeloCargado == null || rutaCargada != ruta || fechaCargada != fecha)
                    {
                        MLContext nuevo = Entrenador.CrearContexto();
                        modeloCargado = Entrenador.Cargar(nuevo, ruta);
                        contextoCargado = nuevo;
                        rutaCargada = ruta;
                        fechaCargada = fecha;
                    }
                    return Tuple.Create(modeloCargado, contextoCargado, Version(ruta));
                }
            }
            catch (Exception ex)
            {
                lock (bloqueo)
                {
                    rutaFallida = ruta;
                }
                AvisarUnaVez("carga|" + ruta, new Exception($"[Sugerencias #671] No se ha podido cargar el modelo de aceptación {ruta}: la sombra sigue con el bayesiano. {ex.Message}", ex));
                return null;
            }
        }

        private void MarcarFallido()
        {
            lock (bloqueo)
            {
                rutaFallida = rutaCargada;
                modeloCargado = null;
                contextoCargado = null;
                rutaCargada = null;
            }
        }

        private string Elegido()
        {
            DateTime ahora = ahoraUtc();
            lock (bloqueo)
            {
                if (carpetaLeida && (ahora - carpetaLeidaUtc).TotalSeconds < PredictorSugerenciasProductos.SEGUNDOS_RELEER_CARPETA)
                {
                    return modeloElegido;
                }
            }
            string nuevo = ElegirModelo(carpeta?.Invoke());
            lock (bloqueo)
            {
                modeloElegido = nuevo;
                carpetaLeidaUtc = ahora;
                carpetaLeida = true;
            }
            return nuevo;
        }

        /// <summary>
        /// El zip del último modelo 2 (por el sufijo AAAAMMDD_HHmm) cuyo json dice PasaPuerta y la versión de features de esta
        /// API (<see cref="ModeloAceptacionEntrada.VERSION_FEATURES"/>). Null si no hay (o no existe la carpeta).
        /// </summary>
        internal static string ElegirModelo(string carpetaModelos)
        {
            if (string.IsNullOrWhiteSpace(carpetaModelos) || !Directory.Exists(carpetaModelos))
            {
                return null;
            }
            foreach (string json in Directory.GetFiles(carpetaModelos, PREFIJO_FICHERO + "*.json")
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                string zip = Path.ChangeExtension(json, ".zip");
                if (!File.Exists(zip))
                {
                    continue;
                }
                try
                {
                    MetadatosModeloAceptacion meta = JsonConvert.DeserializeObject<MetadatosModeloAceptacion>(File.ReadAllText(json));
                    if (meta?.PasaPuerta == true && meta.VersionFeatures == ModeloAceptacionEntrada.VERSION_FEATURES)
                    {
                        return zip;
                    }
                }
                catch (Exception)
                {
                    // un json que no se lee no cuenta
                }
            }
            return null;
        }

        /// <summary>«m2-20261101_0330» (cabe en VersionMotor y VersionSombra, varchar(20)).</summary>
        internal static string Version(string rutaModelo)
        {
            string nombre = Path.GetFileNameWithoutExtension(rutaModelo) ?? string.Empty;
            string sufijo = nombre.StartsWith(PREFIJO_FICHERO, StringComparison.OrdinalIgnoreCase) ? nombre.Substring(PREFIJO_FICHERO.Length) : nombre;
            return SugerenciasProductosSql.Recortar(PREFIJO_VERSION + sufijo, 20);
        }

        private void AvisarUnaVez(string clave, Exception ex)
        {
            lock (bloqueo)
            {
                if (!avisados.Add(clave))
                {
                    return;
                }
            }
            try
            {
                registrarError(ex);
            }
            catch (Exception)
            {
                // El aviso es una ayuda: nunca rompe Calcular.
            }
        }
    }

    /// <summary>
    /// NestoAPI#671 (corte 2e): la sombra de una sugerencia enseñada (una fila de SugerenciasProductosSombra, script Issue671e).
    /// </summary>
    public class SombraSugerencia
    {
        public int MostradaId { get; set; }
        /// <summary>«m2-AAAAMMDD_HHmm» o «bayes-1».</summary>
        public string VersionSombra { get; set; }
        public decimal? PuntuacionSombra { get; set; }
        /// <summary>Su puesto (1 = el mejor) en el orden del modelo 2 entre los candidatos de esa llamada.</summary>
        public short? RangoSombra { get; set; }
        /// <summary>Su puesto en el orden del modelo 1 (el que se usa hoy) entre los mismos candidatos.</summary>
        public short? RangoModelo1 { get; set; }
        public short? Candidatos { get; set; }
        public byte? VersionFeatures { get; set; }
        /// <summary>Instantánea de las features del modelo 1 (<see cref="InstantaneaFeatures"/>).</summary>
        public string Features { get; set; }
    }

    /// <summary>NestoAPI#671 (corte 2e): SQL a mano del modelo 2 (la sombra y la lectura de las respuestas).</summary>
    public static class SugerenciasAceptacionSql
    {
        internal const string TABLA_SOMBRA = "dbo.SugerenciasProductosSombra";

        internal const string SQL_INSERTAR_SOMBRA = @"
INSERT INTO dbo.SugerenciasProductosSombra (MostradaId, VersionSombra, PuntuacionSombra, RangoSombra, RangoModelo1, Candidatos, VersionFeatures, Features)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)";

        /// <summary>
        /// Las columnas de <see cref="MostradaAceptacion"/> (en este orden: las lee Leer por posición). {0} = Features (de la
        /// sombra o NULL), {1} = el JOIN con la sombra (o nada), {2} = el filtro. Fuera: el «DescartadoVendedor» provisional de
        /// antes del script Issue670b (NoLeInteresa con la marca en el Texto), que no es un rechazo del cliente.
        /// </summary>
        private const string SQL_BASE = @"
SELECT m.Id, CAST(m.SesionId AS varchar(36)) AS SesionId, RTRIM(m.Producto) AS Producto, m.Motivo, m.Fuentes, m.Puntuacion, m.Posicion,
       m.VersionMotor, m.FechaMostrada, m.Respuesta, m.EntroEnPedido, m.TamanoCarrito, m.Exploracion,
       RTRIM(s.Vendedor) AS Vendedor, s.Aplicacion, RTRIM(s.Cliente) AS Cliente, {0} AS Features
FROM dbo.SugerenciasProductosMostradas m WITH (NOLOCK)
JOIN dbo.SugerenciasProductosSesiones s WITH (NOLOCK) ON s.Id = m.SesionId
{1}
WHERE ({2}) AND NOT (m.Respuesta = 'NoLeInteresa' AND m.Texto LIKE '[[]DescartadoVendedor]%')";

        private const string FILTRO_RESPONDIDAS = "m.Respuesta IN ('Anadido', 'NoLeInteresa')";
        private const string FILTRO_ENTRENAMIENTO = FILTRO_RESPONDIDAS + " OR (m.Respuesta IS NULL AND m.EntroEnPedido = 1)";

        internal static string Consulta(bool haySombra, bool soloRespondidas)
            => string.Format(SQL_BASE,
                haySombra ? "sm.Features" : "CAST(NULL AS varchar(max))",
                haySombra ? "LEFT JOIN dbo.SugerenciasProductosSombra sm WITH (NOLOCK) ON sm.MostradaId = m.Id" : string.Empty,
                soloRespondidas ? FILTRO_RESPONDIDAS : FILTRO_ENTRENAMIENTO);

        /// <summary>Las respondidas (para las tasas de la API): sin la sombra, que no hace falta.</summary>
        public static List<MostradaAceptacion> LeerRespondidas(string cadenaConexion) => Leer(cadenaConexion, soloRespondidas: true, conSombra: false);

        /// <summary>Lo del entrenamiento: las respondidas y las señales débiles, con su instantánea si la hay.</summary>
        public static List<MostradaAceptacion> LeerEntrenamiento(string cadenaConexion) => Leer(cadenaConexion, soloRespondidas: false, conSombra: true);

        private static List<MostradaAceptacion> Leer(string cadenaConexion, bool soloRespondidas, bool conSombra)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                conexion.Open();
                if (!Existe(conexion, "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.SugerenciasProductosMostradas', N'U') IS NULL THEN 0 ELSE 1 END AS bit)"))
                {
                    return new List<MostradaAceptacion>();
                }
                bool haySombra = conSombra && Existe(conexion, "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.SugerenciasProductosSombra', N'U') IS NULL THEN 0 ELSE 1 END AS bit)");
                var filas = new List<MostradaAceptacion>();
                using (var comando = new SqlCommand(Consulta(haySombra, soloRespondidas), conexion) { CommandTimeout = 120 })
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    while (r.Read())
                    {
                        filas.Add(new MostradaAceptacion
                        {
                            Id = r.GetInt32(0),
                            SesionId = r.GetString(1),
                            Producto = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                            Motivo = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                            Fuentes = r.IsDBNull(4) ? null : r.GetString(4),
                            Puntuacion = r.IsDBNull(5) ? 0m : r.GetDecimal(5),
                            Posicion = r.IsDBNull(6) ? (byte)0 : r.GetByte(6),
                            VersionMotor = r.IsDBNull(7) ? string.Empty : r.GetString(7),
                            FechaMostrada = r.GetDateTime(8),
                            Respuesta = r.IsDBNull(9) ? null : r.GetString(9),
                            EntroEnPedido = r.IsDBNull(10) ? (bool?)null : r.GetBoolean(10),
                            TamanoCarrito = r.IsDBNull(11) ? (int?)null : r.GetInt32(11),
                            Exploracion = !r.IsDBNull(12) && r.GetBoolean(12),
                            Vendedor = r.IsDBNull(13) ? null : r.GetString(13),
                            Aplicacion = r.IsDBNull(14) ? null : r.GetString(14),
                            Cliente = r.IsDBNull(15) ? null : r.GetString(15),
                            Features = r.IsDBNull(16) ? null : r.GetString(16)
                        });
                    }
                }
                return filas;
            }
        }

        private static bool Existe(SqlConnection conexion, string sql)
        {
            using (var comando = new SqlCommand(sql, conexion))
            {
                return (bool)comando.ExecuteScalar();
            }
        }
    }
}
