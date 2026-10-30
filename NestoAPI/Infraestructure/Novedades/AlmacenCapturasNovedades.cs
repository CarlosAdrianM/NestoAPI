using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#754 (sugerencia 900): dónde se guardan las capturas de las sugerencias, incidencias y comentarios de
    /// Novedades. Detrás de una interfaz para no salir a Azure en los tests.
    /// </summary>
    public interface IAlmacenCapturasNovedades
    {
        /// <summary>Hay cadena de conexión: se pueden guardar y servir capturas.</summary>
        bool Configurado { get; }

        /// <summary>El nombre del contenedor (para los mensajes y la migración).</summary>
        string Contenedor { get; }

        Task Subir(string ruta, byte[] contenido, string tipoContenido);

        /// <summary>La captura entera (2 MB como mucho), o null si el blob no existe.</summary>
        Task<byte[]> Descargar(string ruta);

        /// <summary>true si el contenedor existe; false si Azure dice que no (hay que crearlo).</summary>
        Task<bool> ExisteContenedor();
    }

    /// <summary>
    /// NestoAPI#754: misma cuenta de almacenamiento que las fotos de los bultos (<see cref="AlmacenFotosBultosAzure"/>,
    /// cadena Almacen:FotosBultosConexion), pero en un contenedor PRIVADO propio, <c>novedades-capturas</c>: son pantallas
    /// internas (clientes, precios) que no deben salir a terceros (#520). Nunca se dan enlaces a los clientes: la API
    /// baja la captura con una firma de cinco minutos que solo usa ella y la sirve tras comprobar los permisos, como
    /// la imagen de siempre.
    /// </summary>
    public class AlmacenCapturasNovedadesAzure : IAlmacenCapturasNovedades
    {
        public const string CONTENEDOR = "novedades-capturas";

        private static readonly HttpClient clienteCompartido = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private readonly SasBlobAzure.CuentaAlmacenamiento cuenta;
        private readonly HttpClient cliente;
        private readonly Func<DateTime> ahoraUtc;

        public AlmacenCapturasNovedadesAzure()
            : this(ConfigurationManager.AppSettings[AlmacenFotosBultosAzure.CLAVE_CONEXION], clienteCompartido, () => DateTime.UtcNow)
        {
        }

        internal AlmacenCapturasNovedadesAzure(string cadenaDeConexion, HttpClient cliente, Func<DateTime> ahoraUtc)
        {
            cuenta = SasBlobAzure.LeerCadenaDeConexion(cadenaDeConexion);
            this.cliente = cliente;
            this.ahoraUtc = ahoraUtc;
        }

        public bool Configurado => cuenta != null;

        public string Contenedor => CONTENEDOR;

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
                            $"No se ha podido guardar la captura «{ruta}» en el contenedor {CONTENEDOR}: {(int)respuesta.StatusCode} {respuesta.ReasonPhrase} {CodigoError(respuesta)}. {Recortar(detalle)}");
                    }
                }
            }
        }

        public async Task<byte[]> Descargar(string ruta)
        {
            ExigirConfiguracion();
            using (HttpResponseMessage respuesta = await Leer(ruta).ConfigureAwait(false))
            {
                if (respuesta.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
                if (!respuesta.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"No se ha podido leer la captura «{ruta}»: {(int)respuesta.StatusCode} {respuesta.ReasonPhrase} {CodigoError(respuesta)}.");
                }
                return await respuesta.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> ExisteContenedor()
        {
            ExigirConfiguracion();
            // Se pide un blob que no existe: Azure contesta BlobNotFound si el contenedor está y ContainerNotFound si no.
            using (HttpResponseMessage respuesta = await Leer("comprobacion/no-existe-" + Guid.NewGuid().ToString("N")).ConfigureAwait(false))
            {
                string codigo = CodigoError(respuesta);
                if (string.Equals(codigo, "ContainerNotFound", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (respuesta.StatusCode == HttpStatusCode.NotFound || respuesta.IsSuccessStatusCode)
                {
                    return true;
                }
                throw new InvalidOperationException(
                    $"No se ha podido comprobar el contenedor {CONTENEDOR}: {(int)respuesta.StatusCode} {respuesta.ReasonPhrase} {codigo}.");
            }
        }

        private Task<HttpResponseMessage> Leer(string ruta)
        {
            // Cinco minutos hacia atrás por si el reloj del servidor va adelantado respecto al de Azure
            Uri origen = SasBlobAzure.UrlFirmada(cuenta, CONTENEDOR, ruta, SasBlobAzure.PERMISO_LECTURA,
                ahoraUtc().AddMinutes(5), ahoraUtc().AddMinutes(-5));
            var peticion = new HttpRequestMessage(HttpMethod.Get, origen);
            peticion.Headers.Add("x-ms-version", SasBlobAzure.VERSION_SERVICIO);
            return cliente.SendAsync(peticion);
        }

        private static string CodigoError(HttpResponseMessage respuesta) =>
            respuesta.Headers.TryGetValues("x-ms-error-code", out var valores) ? valores.FirstOrDefault() : null;

        private void ExigirConfiguracion()
        {
            if (!Configurado)
            {
                throw new InvalidOperationException(
                    $"Falta la cadena de conexión del almacenamiento de capturas ({AlmacenFotosBultosAzure.CLAVE_CONEXION}).");
            }
        }

        private static string Recortar(string texto) => texto != null && texto.Length > 300 ? texto.Substring(0, 300) : texto;
    }
}
