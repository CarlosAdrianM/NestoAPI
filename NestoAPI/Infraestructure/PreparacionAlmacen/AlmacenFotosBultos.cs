using System;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: dónde se guardan las fotos de los bultos. Detrás de una interfaz para que el
    /// resto no sepa que es Azure y para poder probar sin salir a internet.
    /// </summary>
    public interface IAlmacenFotosBultos
    {
        /// <summary>Hay cadena de conexión: se pueden guardar y enseñar fotos.</summary>
        bool Configurado { get; }

        Task Subir(string ruta, byte[] contenido, string tipoContenido);

        /// <summary>Un enlace que deja ver la foto durante un rato, sin ninguna otra credencial.</summary>
        Uri EnlaceDeLectura(string ruta, TimeSpan vigencia);
    }

    /// <summary>
    /// Contenedor privado <c>bultos</c> de la cuenta de almacenamiento. El móvil nunca habla con
    /// Azure: sube la foto a la API, y la API la guarda aquí con un enlace de escritura de cinco
    /// minutos que firma ella misma. Para verla, un enlace de lectura que caduca.
    /// </summary>
    public class AlmacenFotosBultosAzure : IAlmacenFotosBultos
    {
        public const string CLAVE_CONEXION = "Almacen:FotosBultosConexion";
        public const string CONTENEDOR = "bultos";

        // Un HttpClient para todo el proceso, como pide la propia clase
        private static readonly HttpClient clienteCompartido = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private readonly SasBlobAzure.CuentaAlmacenamiento cuenta;
        private readonly HttpClient cliente;
        private readonly Func<DateTime> ahoraUtc;

        public AlmacenFotosBultosAzure()
            : this(ConfigurationManager.AppSettings[CLAVE_CONEXION], clienteCompartido, () => DateTime.UtcNow)
        {
        }

        internal AlmacenFotosBultosAzure(string cadenaDeConexion, HttpClient cliente, Func<DateTime> ahoraUtc)
        {
            cuenta = SasBlobAzure.LeerCadenaDeConexion(cadenaDeConexion);
            this.cliente = cliente;
            this.ahoraUtc = ahoraUtc;
        }

        public bool Configurado => cuenta != null;

        public async Task Subir(string ruta, byte[] contenido, string tipoContenido)
        {
            ExigirConfiguracion();
            Uri destino = SasBlobAzure.UrlFirmada(cuenta, CONTENEDOR, ruta, SasBlobAzure.PERMISO_SUBIDA,
                ahoraUtc().AddMinutes(5));

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
                            $"No se ha podido guardar la foto «{ruta}»: {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}. {Recortar(detalle)}");
                    }
                }
            }
        }

        public Uri EnlaceDeLectura(string ruta, TimeSpan vigencia)
        {
            ExigirConfiguracion();
            // Cinco minutos hacia atrás por si el reloj del servidor va adelantado respecto al de Azure
            return SasBlobAzure.UrlFirmada(cuenta, CONTENEDOR, ruta, SasBlobAzure.PERMISO_LECTURA,
                ahoraUtc().Add(vigencia), ahoraUtc().AddMinutes(-5));
        }

        private void ExigirConfiguracion()
        {
            if (!Configurado)
            {
                throw new InvalidOperationException(
                    $"Falta la cadena de conexión del almacenamiento de fotos ({CLAVE_CONEXION}).");
            }
        }

        private static string Recortar(string texto)
        {
            return texto != null && texto.Length > 300 ? texto.Substring(0, 300) : texto;
        }
    }
}
