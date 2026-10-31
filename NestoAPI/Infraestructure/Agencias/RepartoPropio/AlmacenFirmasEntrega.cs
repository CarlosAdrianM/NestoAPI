using System;
using System.Configuration;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.PreparacionAlmacen;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#770: dónde se guardan las firmas de las pruebas de entrega (detrás de una interfaz para probar sin Azure).</summary>
    public interface IAlmacenFirmasEntrega
    {
        /// <summary>Hay cadena de conexión: se pueden guardar y leer firmas.</summary>
        bool Configurado { get; }

        Task Subir(string ruta, byte[] png);

        /// <summary>El PNG guardado. Lanza si no se puede leer.</summary>
        Task<byte[]> Descargar(string ruta);
    }

    /// <summary>
    /// NestoAPI#770: contenedor PRIVADO <c>firmas</c> de la misma cuenta que las fotos de los bultos
    /// (<see cref="AlmacenFotosBultosAzure.CLAVE_CONEXION"/>). La firma nunca sale con un enlace: la API la descarga y la sirve
    /// a quien puede verla (<see cref="Controllers.PermisosRepartoPropio.GRUPOS_DATOS_PERSONALES"/>).
    /// </summary>
    public class AlmacenFirmasEntregaAzure : IAlmacenFirmasEntrega
    {
        public const string CONTENEDOR = "firmas";
        private static readonly HttpClient clienteCompartido = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private readonly SasBlobAzure.CuentaAlmacenamiento cuenta;
        private readonly HttpClient cliente;
        private readonly Func<DateTime> ahoraUtc;

        public AlmacenFirmasEntregaAzure()
            : this(ConfigurationManager.AppSettings[AlmacenFotosBultosAzure.CLAVE_CONEXION], clienteCompartido, () => DateTime.UtcNow)
        {
        }

        internal AlmacenFirmasEntregaAzure(string cadenaDeConexion, HttpClient cliente, Func<DateTime> ahoraUtc)
        {
            cuenta = SasBlobAzure.LeerCadenaDeConexion(cadenaDeConexion);
            this.cliente = cliente;
            this.ahoraUtc = ahoraUtc;
        }

        public bool Configurado => cuenta != null;

        /// <summary>«1/251300/4711-20261031123005.png»: empresa, envío, prueba y la hora del aparato (dentro del contenedor firmas).</summary>
        public static string Ruta(string empresa, int envio, int idPrueba, DateTime fechaHora)
        {
            string limpia = string.IsNullOrWhiteSpace(empresa) ? Models.Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            return $"{limpia}/{envio}/{idPrueba}-{fechaHora.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.png";
        }

        public async Task Subir(string ruta, byte[] png)
        {
            ExigirConfiguracion();
            Uri destino = SasBlobAzure.UrlFirmada(cuenta, CONTENEDOR, ruta, SasBlobAzure.PERMISO_SUBIDA, ahoraUtc().AddMinutes(5));
            using (var peticion = new HttpRequestMessage(HttpMethod.Put, destino))
            {
                peticion.Headers.Add("x-ms-blob-type", "BlockBlob");
                peticion.Headers.Add("x-ms-version", SasBlobAzure.VERSION_SERVICIO);
                peticion.Content = new ByteArrayContent(png);
                peticion.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                using (HttpResponseMessage respuesta = await cliente.SendAsync(peticion).ConfigureAwait(false))
                {
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        // La dirección lleva la firma SAS: no va al mensaje.
                        throw new InvalidOperationException($"No se ha podido guardar la firma «{ruta}»: {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}.");
                    }
                }
            }
        }

        public async Task<byte[]> Descargar(string ruta)
        {
            ExigirConfiguracion();
            Uri origen = SasBlobAzure.UrlFirmada(cuenta, CONTENEDOR, ruta, SasBlobAzure.PERMISO_LECTURA, ahoraUtc().AddMinutes(5), ahoraUtc().AddMinutes(-5));
            using (var peticion = new HttpRequestMessage(HttpMethod.Get, origen))
            {
                peticion.Headers.Add("x-ms-version", SasBlobAzure.VERSION_SERVICIO);
                using (HttpResponseMessage respuesta = await cliente.SendAsync(peticion).ConfigureAwait(false))
                {
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"No se ha podido leer la firma «{ruta}»: {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}.");
                    }
                    return await respuesta.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
        }

        private void ExigirConfiguracion()
        {
            if (!Configurado)
            {
                throw new InvalidOperationException($"Falta la cadena de conexión del almacenamiento ({AlmacenFotosBultosAzure.CLAVE_CONEXION}).");
            }
        }
    }
}
