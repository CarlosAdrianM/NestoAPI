using Microsoft.ML;
using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Features;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>NestoAPI#671 (corte 2c): el modelo 1 (propensión de compra) tal y como lo usa Calcular (sustituible en los tests).</summary>
    public interface IPredictorSugerenciasProductos
    {
        /// <summary>
        /// La versión del modelo que hay que usar ahora (va en VersionMotor, «m1-AAAAMMDD_HHmm»), o null si no hay que usarlo:
        /// interruptor apagado, ningún modelo que haya pasado la puerta o no se puede cargar. Nunca lanza.
        /// </summary>
        string ModeloDisponible();

        /// <summary>
        /// NestoAPI#683: versión del esquema de features del modelo que devolvió <see cref="ModeloDisponible"/> por última vez
        /// (<see cref="ModeloSugerenciaEntrada.VERSION_FEATURES_INICIAL"/> si ninguno). Con 2 o más, Calcular lee el estado del
        /// cliente y la popularidad por estado; con 1, no hace falta (el zip no mira esas columnas).
        /// </summary>
        int VersionFeatures { get; }

        /// <summary>P1 (probabilidad calibrada) de cada entrada, en el mismo orden. Puede lanzar.</summary>
        IList<float> Puntuar(IList<ModeloSugerenciaEntrada> entradas);

        /// <summary>Algo ha fallado al preparar o puntuar: a ELMAH una sola vez por modelo, y ese modelo deja de usarse hasta que cambie.</summary>
        void AvisarFallo(Exception ex);
    }

    /// <summary>El modelo elegido en la carpeta del job.</summary>
    public class ModeloSugerenciasElegido
    {
        public string RutaModelo { get; set; }
        public string Version { get; set; }
        /// <summary>NestoAPI#683: versión del esquema de features (MetadatosModelo.VersionFeatures; 1 en los json de antes).</summary>
        public int VersionFeatures { get; set; } = ModeloSugerenciaEntrada.VERSION_FEATURES_INICIAL;
    }

    /// <summary>
    /// NestoAPI#671 (corte 2c): el de verdad. Usa el ÚLTIMO zip de la carpeta del job de reentrenamiento
    /// (<see cref="AlmacenModeloSugerencias"/>, «modelo_sugerencias_productos_AAAAMMDD_HHmm.zip») cuyo json diga
    /// <c>PasaPuerta: true</c>. Como <see cref="Rapports.ModeloContacto"/>: el zip se carga una vez y se guarda en memoria hasta
    /// que cambie el fichero (cargarlo cuesta mucho más que puntuar); puntuar es un Transform por lotes del ITransformer, que es
    /// seguro entre hilos. La carpeta se mira como mucho cada <see cref="SEGUNDOS_RELEER_CARPETA"/> s, así que un modelo nuevo
    /// entra solo, sin publicar.
    /// <para>Interruptor: parámetro <see cref="Constantes.ParametrosUsuario.MODELO_SUGERENCIAS_ACTIVO"/> de «(defecto)» (empresa
    /// por defecto). Sin fila, ENCENDIDO; «0» lo apaga (vuelven las reglas) en menos de un minuto.</para>
    /// <para>Sin modelo, si no carga o si falla al puntuar, Calcular sigue con las reglas («reglas-1»); el motivo va a ELMAH
    /// una sola vez (no en cada llamada) y un modelo que ha fallado no se vuelve a intentar hasta que cambie su fichero.</para>
    /// <para>NestoAPI#683: VERSIÓN DE LAS FEATURES. Solo se usan zips cuyo json dice una versión que esta API sabe calcular
    /// AHORA: hasta <see cref="ModeloSugerenciaEntrada.VERSION_FEATURES"/> y, la 2, solo si el job nocturno ya ha rellenado la
    /// popularidad por estado (script Issue683; se mira con la carpeta, como mucho cada 60 s). Si el último que pasa la puerta
    /// es de otra versión, el último compatible; si no hay ninguno, reglas-1. Así, al publicar, el zip de antes (versión 1)
    /// sigue funcionando hasta que haya uno nuevo y el job haya pasado.</para>
    /// </summary>
    public class PredictorSugerenciasProductos : IPredictorSugerenciasProductos
    {
        public const int SEGUNDOS_RELEER_CARPETA = 60;
        internal const string PREFIJO_FICHERO = "modelo_sugerencias_productos_";
        internal const string PREFIJO_VERSION = "m1-";

        /// <summary>La de la API: la caché del zip es de todo el proceso.</summary>
        public static readonly PredictorSugerenciasProductos Instancia = new PredictorSugerenciasProductos(
            () => AlmacenModeloSugerencias.Produccion().Carpeta,
            new InterruptorCacheado(() => new LectorParametrosUsuario().LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.MODELO_SUGERENCIAS_ACTIVO), TimeSpan.FromSeconds(60)),
            null, null, VersionCalculableEnBd);

        /// <summary>NestoAPI#683: 2 si la popularidad por estado está rellena (script Issue683 y una pasada del job); si no, 1.</summary>
        internal static int VersionCalculableEnBd()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<bool>(SugerenciasProductosSql.SQL_HAY_POPULARIDAD_ESTADO).First()
                    ? ModeloSugerenciaEntrada.VERSION_FEATURES
                    : ModeloSugerenciaEntrada.VERSION_FEATURES_INICIAL;
            }
        }

        private readonly Func<string> carpeta;
        private readonly InterruptorCacheado interruptor;
        private readonly Action<Exception> registrarError;
        private readonly Func<DateTime> ahoraUtc;
        private readonly Func<int> versionCalculable;
        private readonly object bloqueo = new object();
        private readonly HashSet<string> avisados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ModeloSugerenciasElegido elegido;
        private DateTime carpetaLeidaUtc;
        private bool carpetaLeida;
        private string rutaCargada;
        private DateTime fechaCargada;
        private ITransformer modeloCargado;
        private MLContext contextoCargado;
        private string rutaFallida;
        private DateTime fechaFallida;
        private int versionFeatures = ModeloSugerenciaEntrada.VERSION_FEATURES_INICIAL;

        /// <param name="versionCalculable">
        /// NestoAPI#683: la versión de features más alta que se puede calcular ahora (null: <see cref="ModeloSugerenciaEntrada.VERSION_FEATURES"/>).
        /// Si lanza, la inicial (y a ELMAH una vez).
        /// </param>
        internal PredictorSugerenciasProductos(Func<string> carpeta, InterruptorCacheado interruptor, Action<Exception> registrarError, Func<DateTime> ahoraUtc,
            Func<int> versionCalculable = null)
        {
            this.carpeta = carpeta;
            this.interruptor = interruptor;
            this.registrarError = registrarError ?? ElmahHelper.Log;
            this.ahoraUtc = ahoraUtc ?? (() => DateTime.UtcNow);
            this.versionCalculable = versionCalculable ?? (() => ModeloSugerenciaEntrada.VERSION_FEATURES);
        }

        public int VersionFeatures
        {
            get
            {
                lock (bloqueo)
                {
                    return versionFeatures;
                }
            }
        }

        public string ModeloDisponible()
        {
            if (interruptor != null && !interruptor.EstaActivo())
            {
                return null;
            }
            string carpetaModelos = null;
            string ruta = null;
            try
            {
                carpetaModelos = carpeta();
                ModeloSugerenciasElegido modelo = Elegido(carpetaModelos);
                if (modelo == null)
                {
                    AvisarUnaVez("sin-modelo|" + carpetaModelos, new Exception(
                        $"[Sugerencias #671] No hay ningún modelo de sugerencias que haya pasado la puerta en {carpetaModelos}: Calcular usa las reglas (reglas-1)."));
                    return null;
                }
                ruta = modelo.RutaModelo;
                DateTime fecha = File.GetLastWriteTimeUtc(ruta);
                lock (bloqueo)
                {
                    if (rutaFallida == ruta && fechaFallida == fecha)
                    {
                        return null;
                    }
                    if (modeloCargado == null || rutaCargada != ruta || fechaCargada != fecha)
                    {
                        MLContext nuevo = Entrenador.CrearContexto();
                        // FileShare.Delete (Entrenador.Cargar): el job puede escribir otro zip mientras se lee este.
                        ITransformer cargado = Entrenador.Cargar(nuevo, ruta);
                        modeloCargado = cargado;
                        contextoCargado = nuevo;
                        rutaCargada = ruta;
                        fechaCargada = fecha;
                    }
                    versionFeatures = modelo.VersionFeatures;
                    return modelo.Version;
                }
            }
            catch (Exception ex)
            {
                MarcarFallido(ruta);
                AvisarUnaVez("carga|" + ruta, new Exception($"[Sugerencias #671] No se ha podido cargar el modelo de sugerencias {ruta ?? carpetaModelos}: " +
                    $"Calcular usa las reglas (reglas-1). {ex.Message}", ex));
                return null;
            }
        }

        public IList<float> Puntuar(IList<ModeloSugerenciaEntrada> entradas)
        {
            if (entradas == null || entradas.Count == 0)
            {
                return new List<float>();
            }
            ITransformer modelo;
            MLContext ml;
            lock (bloqueo)
            {
                modelo = modeloCargado;
                ml = contextoCargado;
            }
            if (modelo == null)
            {
                throw new InvalidOperationException("[Sugerencias #671] No hay ningún modelo de sugerencias cargado.");
            }
            List<float> probabilidades = Entrenador.Probabilidades(ml, modelo, entradas);
            if (probabilidades.Count != entradas.Count || probabilidades.Any(float.IsNaN))
            {
                throw new InvalidOperationException($"[Sugerencias #671] El modelo {rutaCargada} ha devuelto {probabilidades.Count} probabilidades para {entradas.Count} candidatos (o alguna no es un número).");
            }
            return probabilidades;
        }

        public void AvisarFallo(Exception ex)
        {
            string ruta;
            lock (bloqueo)
            {
                ruta = rutaCargada;
            }
            MarcarFallido(ruta);
            AvisarUnaVez("prediccion|" + ruta, new Exception($"[Sugerencias #671] Ha fallado el modelo de sugerencias {ruta}: Calcular usa las reglas (reglas-1) " +
                $"hasta que haya otro modelo. {ex?.Message}", ex));
        }

        private void MarcarFallido(string ruta)
        {
            if (ruta == null)
            {
                return;
            }
            lock (bloqueo)
            {
                rutaFallida = ruta;
                try
                {
                    fechaFallida = File.GetLastWriteTimeUtc(ruta);
                }
                catch (Exception)
                {
                    fechaFallida = default;
                }
                if (rutaCargada == ruta)
                {
                    modeloCargado = null;
                    contextoCargado = null;
                    rutaCargada = null;
                }
            }
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

        private ModeloSugerenciasElegido Elegido(string carpetaModelos)
        {
            DateTime ahora = ahoraUtc();
            lock (bloqueo)
            {
                if (carpetaLeida && (ahora - carpetaLeidaUtc).TotalSeconds < SEGUNDOS_RELEER_CARPETA
                    && (elegido == null || File.Exists(elegido.RutaModelo)))
                {
                    return elegido;
                }
            }
            int calculable;
            try
            {
                calculable = versionCalculable();
            }
            catch (Exception ex)
            {
                calculable = ModeloSugerenciaEntrada.VERSION_FEATURES_INICIAL;
                AvisarUnaVez("version-calculable", new Exception("[Sugerencias #683] No se ha podido mirar si está la popularidad por estado: " +
                    $"solo se usan modelos de la versión {calculable} de las features. {ex.Message}", ex));
            }
            ModeloSugerenciasElegido nuevo = ElegirModelo(carpetaModelos, calculable);
            lock (bloqueo)
            {
                elegido = nuevo;
                carpetaLeidaUtc = ahora;
                carpetaLeida = true;
            }
            return nuevo;
        }

        /// <summary>
        /// El último modelo de la carpeta (por el sufijo AAAAMMDD_HHmm del nombre) cuyo json dice que pasa la puerta, cuyo zip
        /// existe y cuya versión de features (NestoAPI#683; 1 si el json no la dice) está entre 1 y
        /// <paramref name="versionMaxima"/> (que nunca pasa de <see cref="ModeloSugerenciaEntrada.VERSION_FEATURES"/>). Null si no
        /// hay ninguno (o no existe la carpeta). Un json que no se puede leer no cuenta.
        /// </summary>
        internal static ModeloSugerenciasElegido ElegirModelo(string carpetaModelos, int versionMaxima = ModeloSugerenciaEntrada.VERSION_FEATURES)
        {
            int maxima = Math.Min(versionMaxima, ModeloSugerenciaEntrada.VERSION_FEATURES);
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
                MetadatosModelo metadatos;
                try
                {
                    metadatos = JsonConvert.DeserializeObject<MetadatosModelo>(File.ReadAllText(json));
                }
                catch (Exception)
                {
                    continue;
                }
                if (metadatos?.PasaPuerta == true && metadatos.VersionFeatures >= ModeloSugerenciaEntrada.VERSION_FEATURES_INICIAL
                    && metadatos.VersionFeatures <= maxima)
                {
                    return new ModeloSugerenciasElegido { RutaModelo = zip, Version = Version(zip), VersionFeatures = metadatos.VersionFeatures };
                }
            }
            return null;
        }

        /// <summary>«m1-20261020_2330» (cabe en VersionMotor varchar(20)).</summary>
        internal static string Version(string rutaModelo)
        {
            string nombre = Path.GetFileNameWithoutExtension(rutaModelo) ?? string.Empty;
            string sufijo = nombre.StartsWith(PREFIJO_FICHERO, StringComparison.OrdinalIgnoreCase) ? nombre.Substring(PREFIJO_FICHERO.Length) : nombre;
            return SugerenciasProductosSql.Recortar(PREFIJO_VERSION + sufijo, 20);
        }
    }
}
