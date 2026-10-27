using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#711: dónde se guardan los vídeos de las novedades. Detrás de una interfaz para no salir a Azure en los tests.
    /// </summary>
    public interface IAlmacenVideosNovedades
    {
        /// <summary>Hay cadena de conexión: se pueden guardar y servir vídeos.</summary>
        bool Configurado { get; }

        Task Subir(string ruta, byte[] contenido, string tipoContenido);

        /// <summary>El MP4 entero (son 10 MB como mucho), o null si el blob no existe.</summary>
        Task<byte[]> Descargar(string ruta);

        /// <summary>Lo borra. No falla si ya no existía.</summary>
        Task Borrar(string ruta);
    }

    /// <summary>
    /// NestoAPI#711: el mismo almacenamiento que las fotos de los bultos (<see cref="AlmacenFotosBultosAzure"/>: misma
    /// cadena de conexión y mismo contenedor privado «bultos»), con el prefijo <c>novedades-videos/</c>. Así no hay que
    /// crear nada en Azure. Los enlaces firmados (<see cref="SasBlobAzure"/>) solo los usa la API: los clientes piden
    /// el vídeo a la API con su token, como la imagen de la novedad.
    ///
    /// <para>El reproductor pide el vídeo a trozos (Range) muchas veces seguidas: se guarda en memoria un rato para no
    /// bajarlo de Azure en cada trozo. Cada subida tiene un nombre nuevo, así que lo guardado nunca se queda viejo.</para>
    /// </summary>
    public class AlmacenVideosNovedadesAzure : IAlmacenVideosNovedades
    {
        public const string CONTENEDOR = AlmacenFotosBultosAzure.CONTENEDOR;
        /// <summary>Permiso de la firma para borrar un blob.</summary>
        private const string PERMISO_BORRADO = "d";
        private static readonly TimeSpan VIGENCIA_CACHE = TimeSpan.FromMinutes(30);

        private static readonly HttpClient clienteCompartido = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private static readonly MemoryCache cache = new MemoryCache("NovedadesVideos");

        private readonly SasBlobAzure.CuentaAlmacenamiento cuenta;
        private readonly HttpClient cliente;
        private readonly Func<DateTime> ahoraUtc;

        public AlmacenVideosNovedadesAzure()
            : this(ConfigurationManager.AppSettings[AlmacenFotosBultosAzure.CLAVE_CONEXION], clienteCompartido, () => DateTime.UtcNow)
        {
        }

        internal AlmacenVideosNovedadesAzure(string cadenaDeConexion, HttpClient cliente, Func<DateTime> ahoraUtc)
        {
            cuenta = SasBlobAzure.LeerCadenaDeConexion(cadenaDeConexion);
            this.cliente = cliente;
            this.ahoraUtc = ahoraUtc;
        }

        public bool Configurado => cuenta != null;

        public async Task Subir(string ruta, byte[] contenido, string tipoContenido)
        {
            ExigirConfiguracion();
            Uri destino = SasBlobAzure.UrlFirmada(cuenta, CONTENEDOR, ruta, SasBlobAzure.PERMISO_SUBIDA, ahoraUtc().AddMinutes(5));
            using (var peticion = new HttpRequestMessage(HttpMethod.Put, destino))
            {
                peticion.Headers.Add("x-ms-blob-type", "BlockBlob");
                peticion.Headers.Add("x-ms-version", SasBlobAzure.VERSION_SERVICIO);
                peticion.Content = new ByteArrayContent(contenido);
                peticion.Content.Headers.ContentType = new MediaTypeHeaderValue(tipoContenido);
                using (HttpResponseMessage respuesta = await cliente.SendAsync(peticion).ConfigureAwait(false))
                {
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        string detalle = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                        // La dirección lleva la firma: no va al mensaje
                        throw new InvalidOperationException(
                            $"No se ha podido guardar el vídeo «{ruta}»: {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}. {Recortar(detalle)}");
                    }
                }
            }
        }

        public async Task<byte[]> Descargar(string ruta)
        {
            ExigirConfiguracion();
            if (cache.Get(ruta) is byte[] guardado)
            {
                return guardado;
            }
            // Cinco minutos hacia atrás por si el reloj del servidor va adelantado respecto al de Azure
            Uri origen = SasBlobAzure.UrlFirmada(cuenta, CONTENEDOR, ruta, SasBlobAzure.PERMISO_LECTURA,
                ahoraUtc().AddMinutes(5), ahoraUtc().AddMinutes(-5));
            using (var peticion = new HttpRequestMessage(HttpMethod.Get, origen))
            {
                peticion.Headers.Add("x-ms-version", SasBlobAzure.VERSION_SERVICIO);
                using (HttpResponseMessage respuesta = await cliente.SendAsync(peticion).ConfigureAwait(false))
                {
                    if (respuesta.StatusCode == HttpStatusCode.NotFound)
                    {
                        return null;
                    }
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"No se ha podido leer el vídeo «{ruta}»: {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}.");
                    }
                    byte[] contenido = await respuesta.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    cache.Set(ruta, contenido, new CacheItemPolicy { SlidingExpiration = VIGENCIA_CACHE });
                    return contenido;
                }
            }
        }

        public async Task Borrar(string ruta)
        {
            ExigirConfiguracion();
            cache.Remove(ruta);
            Uri destino = SasBlobAzure.UrlFirmada(cuenta, CONTENEDOR, ruta, PERMISO_BORRADO, ahoraUtc().AddMinutes(5));
            using (var peticion = new HttpRequestMessage(HttpMethod.Delete, destino))
            {
                peticion.Headers.Add("x-ms-version", SasBlobAzure.VERSION_SERVICIO);
                using (HttpResponseMessage respuesta = await cliente.SendAsync(peticion).ConfigureAwait(false))
                {
                    if (!respuesta.IsSuccessStatusCode && respuesta.StatusCode != HttpStatusCode.NotFound)
                    {
                        throw new InvalidOperationException(
                            $"No se ha podido borrar el vídeo «{ruta}»: {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}.");
                    }
                }
            }
        }

        private void ExigirConfiguracion()
        {
            if (!Configurado)
            {
                throw new InvalidOperationException(
                    $"Falta la cadena de conexión del almacenamiento de vídeos ({AlmacenFotosBultosAzure.CLAVE_CONEXION}).");
            }
        }

        private static string Recortar(string texto) => texto != null && texto.Length > 300 ? texto.Substring(0, 300) : texto;
    }
}
