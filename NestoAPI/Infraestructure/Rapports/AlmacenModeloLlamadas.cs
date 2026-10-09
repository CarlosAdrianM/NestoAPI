using ModeloLlamadaPedido.Reentrenamiento;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#619: dónde vive el modelo de llamadas y cómo se cambia.
    /// <para>El que reentrena el job mensual va FUERA de la carpeta publicada (por defecto <see cref="CARPETA_POR_DEFECTO"/>,
    /// appSetting <see cref="CLAVE_CARPETA"/>) para que un deploy no lo pise. Si ahí no hay modelo, la API usa el de
    /// ModelsIA del proyecto (el que va en el deploy), como antes.</para>
    /// <para>Promover es atómico: el job guarda el zip nuevo en un temporal de la MISMA carpeta y <see cref="File.Replace(string, string, string)"/>
    /// lo cambia por el activo dejando el anterior en <see cref="NOMBRE_ANTERIOR"/>. Junto al zip quedan
    /// <see cref="MetadatosModelo.NOMBRE_FICHERO"/> (métricas, fecha y contactos vistos, para la próxima puerta) y el README.
    /// La API recarga sola el modelo al cambiar la fecha del fichero (<see cref="ModeloContacto"/>). Nada de git.</para>
    /// </summary>
    public class AlmacenModeloLlamadas
    {
        internal const string CLAVE_CARPETA = "ModeloLlamadas:Carpeta";
        internal const string CARPETA_POR_DEFECTO = @"C:\NestoAPI\ModelosIA";
        internal const string NOMBRE_MODELO = "modelo_llamadas.zip";
        internal const string NOMBRE_ANTERIOR = "modelo_llamadas_anterior.zip";
        internal const string NOMBRE_METADATOS_ANTERIOR = "modelo_llamadas_anterior.json";
        internal const string NOMBRE_README = "README_modelo_llamadas.md";
        internal const string NOMBRE_ULTIMO_INFORME = "ultimo_reentrenamiento.md";
        private const int INTENTOS = 5;

        private readonly string rutaDelDeploy;

        public AlmacenModeloLlamadas(string carpeta, string rutaDelDeploy)
        {
            Carpeta = carpeta;
            this.rutaDelDeploy = rutaDelDeploy;
        }

        /// <summary>El de producción: carpeta del appSetting (o la de por defecto) y, de respaldo, el de ModelsIA.</summary>
        public static AlmacenModeloLlamadas Produccion()
            => new AlmacenModeloLlamadas(CarpetaConfigurada(clave => ConfigurationManager.AppSettings[clave]), ModeloContacto.RutaDelDeploy);

        internal static string CarpetaConfigurada(Func<string, string> leerParametro)
        {
            string valor = leerParametro(CLAVE_CARPETA);
            return string.IsNullOrWhiteSpace(valor) ? CARPETA_POR_DEFECTO : valor.Trim();
        }

        public string Carpeta { get; }
        public string RutaModelo => Path.Combine(Carpeta, NOMBRE_MODELO);
        public string RutaAnterior => Path.Combine(Carpeta, NOMBRE_ANTERIOR);
        public string RutaMetadatos => Path.Combine(Carpeta, MetadatosModelo.NOMBRE_FICHERO);
        public string RutaMetadatosAnterior => Path.Combine(Carpeta, NOMBRE_METADATOS_ANTERIOR);
        public string RutaReadme => Path.Combine(Carpeta, NOMBRE_README);
        public string RutaUltimoInforme => Path.Combine(Carpeta, NOMBRE_ULTIMO_INFORME);

        /// <summary>El que usa la API ahora: el de la carpeta si existe; si no, el del deploy; null si no hay ninguno.</summary>
        public string RutaModeloActivo
            => File.Exists(RutaModelo) ? RutaModelo : (rutaDelDeploy != null && File.Exists(rutaDelDeploy) ? rutaDelDeploy : null);

        /// <summary>Metadatos del modelo de la carpeta (null si no hay modelo ahí, no hay json o no se puede leer).</summary>
        public MetadatosModelo LeerMetadatos()
        {
            try
            {
                return File.Exists(RutaModelo) && File.Exists(RutaMetadatos)
                    ? JsonConvert.DeserializeObject<MetadatosModelo>(File.ReadAllText(RutaMetadatos, Encoding.UTF8))
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Primer día de contactos que el modelo activo no vio al entrenar: el ContactosHasta de sus metadatos o, si no los
        /// tiene (el del deploy), la fecha del fichero menos 8 días. Null si no hay modelo.
        /// </summary>
        public DateTime? CorteModeloActivo()
        {
            MetadatosModelo meta = LeerMetadatos();
            if (meta != null && meta.ContactosHasta > DateTime.MinValue)
            {
                return meta.ContactosHasta.Date;
            }
            string activo = RutaModeloActivo;
            return activo == null ? (DateTime?)null : VentanaReentrenamiento.CorteDesdeFechaModelo(File.GetLastWriteTime(activo));
        }

        /// <summary>Un temporal en la MISMA carpeta (File.Replace solo es atómico dentro del mismo volumen).</summary>
        public string NuevaRutaTemporal()
        {
            _ = Directory.CreateDirectory(Carpeta);
            return Path.Combine(Carpeta, $"modelo_llamadas.{DateTime.Now:yyyyMMddHHmmss}.{Guid.NewGuid():N}.tmp");
        }

        /// <summary>
        /// Cambia el modelo activo por <paramref name="temporal"/> de una vez: el activo pasa a <see cref="NOMBRE_ANTERIOR"/>
        /// (si el activo era el del deploy, se copia ahí). Después escribe metadatos (los anteriores a
        /// <see cref="NOMBRE_METADATOS_ANTERIOR"/>) y README. Si falla el cambio del zip, no se toca nada.
        /// </summary>
        public void Promover(string temporal, MetadatosModelo metadatos, string readme)
        {
            if (!File.Exists(temporal))
            {
                throw new FileNotFoundException("NestoAPI#619: no está el modelo nuevo que se iba a promover", temporal);
            }
            _ = Directory.CreateDirectory(Carpeta);
            if (File.Exists(RutaModelo))
            {
                // ReplaceFile: atómico, conserva el anterior como copia de seguridad (pisando la que hubiera).
                Reintentar(() => File.Replace(temporal, RutaModelo, RutaAnterior, ignoreMetadataErrors: true));
            }
            else
            {
                if (rutaDelDeploy != null && File.Exists(rutaDelDeploy))
                {
                    File.Copy(rutaDelDeploy, RutaAnterior, overwrite: true);
                }
                Reintentar(() => File.Move(temporal, RutaModelo));
            }
            // La fecha del fichero es la que mira ModeloContacto para recargar: que sea la de ahora.
            File.SetLastWriteTimeUtc(RutaModelo, DateTime.UtcNow);

            if (File.Exists(RutaMetadatos))
            {
                File.Copy(RutaMetadatos, RutaMetadatosAnterior, overwrite: true);
            }
            EscribirAtomico(RutaMetadatos, JsonConvert.SerializeObject(metadatos, Formatting.Indented));
            EscribirAtomico(RutaReadme, readme ?? "");
        }

        /// <summary>El README de la última ejecución, se haya promovido o no (lo cita el aviso).</summary>
        public void GuardarInforme(string texto)
        {
            _ = Directory.CreateDirectory(Carpeta);
            EscribirAtomico(RutaUltimoInforme, texto ?? "");
        }

        /// <summary>Borra un temporal que no se ha promovido. Nunca lanza.</summary>
        public void Descartar(string temporal)
        {
            try
            {
                if (temporal != null && File.Exists(temporal))
                {
                    File.Delete(temporal);
                }
            }
            catch (Exception)
            {
                // Un temporal que no se puede borrar no es un error del reentrenamiento.
            }
        }

        private static void EscribirAtomico(string ruta, string texto)
        {
            string temporal = ruta + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporal, texto, new UTF8Encoding(false));
            if (File.Exists(ruta))
            {
                Reintentar(() => File.Replace(temporal, ruta, null, ignoreMetadataErrors: true));
            }
            else
            {
                File.Move(temporal, ruta);
            }
        }

        /// <summary>La API puede estar leyendo el zip para puntuar justo en ese momento: se reintenta unos segundos.</summary>
        private static void Reintentar(Action accion)
        {
            for (int intento = 1; ; intento++)
            {
                try
                {
                    accion();
                    return;
                }
                catch (IOException) when (intento < INTENTOS)
                {
                    Thread.Sleep(1000 * intento);
                }
                catch (UnauthorizedAccessException) when (intento < INTENTOS)
                {
                    Thread.Sleep(1000 * intento);
                }
            }
        }
    }
}
