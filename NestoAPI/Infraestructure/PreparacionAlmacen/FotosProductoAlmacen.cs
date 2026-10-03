using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    public interface IFotosProductoAlmacen
    {
        /// <summary>La URL de la foto de cada producto (la de la tienda); null si no tiene o no se ha podido saber.</summary>
        Task<IDictionary<string, string>> Urls(IEnumerable<string> productos);

        /// <summary>Ariadna#8: la foto que tiene la tienda AHORA (sin mirar la caché, que se pone al día). Null si no tiene o no contesta.</summary>
        Task<string> UrlActual(string producto);

        /// <summary>Ariadna#8: la foto se ha corregido: la próxima vez se pregunta a la tienda.</summary>
        void Olvidar(string producto);
    }

    /// <summary>
    /// Ariadna#2: la foto del producto para el mozo. La URL la da la API (la tienda, imagenesPorReferencia.php, por
    /// ProductoDTO.RutaImagen): la app no lleva la clave de PrestaShop. Una llamada por producto, así que se cachea en
    /// memoria (también el «no tiene foto») y se piden como mucho <see cref="MAXIMO_EN_PARALELO"/> a la vez, cada una con
    /// un tiempo máximo: un picking largo no puede quedarse esperando a la tienda.
    /// </summary>
    public class FotosProductoAlmacen : IFotosProductoAlmacen
    {
        internal const int MAXIMO_EN_PARALELO = 8;
        internal static readonly TimeSpan VIGENCIA = TimeSpan.FromHours(12);
        /// <summary>Si la tienda no ha contestado, se vuelve a preguntar pronto: puede que sí tenga foto.</summary>
        internal static readonly TimeSpan VIGENCIA_SI_FALLA = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan TIEMPO_MAXIMO = TimeSpan.FromSeconds(5);

        /// <summary>Común a todas las peticiones: el servicio es por petición, la caché no.</summary>
        private static readonly CacheFotosProducto cacheComun = new CacheFotosProducto();

        private readonly Func<string, Task<string>> buscar;
        private readonly CacheFotosProducto cache;
        private readonly Func<DateTime> ahora;
        private readonly TimeSpan tiempoMaximo;

        public FotosProductoAlmacen() : this(ProductoDTO.RutaImagen, cacheComun, () => DateTime.UtcNow, TIEMPO_MAXIMO)
        {
        }

        internal FotosProductoAlmacen(Func<string, Task<string>> buscar, CacheFotosProducto cache, Func<DateTime> ahora, TimeSpan tiempoMaximo)
        {
            this.buscar = buscar ?? throw new ArgumentNullException(nameof(buscar));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.ahora = ahora ?? throw new ArgumentNullException(nameof(ahora));
            this.tiempoMaximo = tiempoMaximo;
        }

        public async Task<IDictionary<string, string>> Urls(IEnumerable<string> productos)
        {
            List<string> lista = (productos ?? Enumerable.Empty<string>())
                .Select(p => p?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var resultado = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var porBuscar = new List<string>();
            foreach (string producto in lista)
            {
                if (cache.TryLeer(producto, ahora(), out string url))
                {
                    resultado[producto] = url;
                }
                else
                {
                    porBuscar.Add(producto);
                }
            }

            using (var turnos = new SemaphoreSlim(MAXIMO_EN_PARALELO))
            {
                await Task.WhenAll(porBuscar.Select(async producto =>
                {
                    await turnos.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        resultado[producto] = await Buscar(producto).ConfigureAwait(false);
                    }
                    finally
                    {
                        _ = turnos.Release();
                    }
                })).ConfigureAwait(false);
            }
            return resultado;
        }

        public Task<string> UrlActual(string producto)
        {
            string limpio = producto?.Trim();
            return string.IsNullOrEmpty(limpio) ? Task.FromResult<string>(null) : Buscar(limpio);
        }

        public void Olvidar(string producto)
        {
            if (!string.IsNullOrWhiteSpace(producto))
            {
                cache.Olvidar(producto.Trim());
            }
        }

        private async Task<string> Buscar(string producto)
        {
            string ruta;
            try
            {
                Task<string> pregunta = buscar(producto);
                ruta = await Task.WhenAny(pregunta, Task.Delay(tiempoMaximo)).ConfigureAwait(false) == pregunta
                    ? await pregunta.ConfigureAwait(false)
                    : null;
            }
            catch (Exception)
            {
                ruta = null;
            }

            if (ruta == null)
            {
                // RutaImagen da null cuando no ha podido preguntar: no es «no tiene foto»
                cache.Guardar(producto, null, ahora() + VIGENCIA_SI_FALLA);
                return null;
            }
            string url = EsRota(ruta) ? null : ruta.Trim();
            cache.Guardar(producto, url, ahora() + VIGENCIA);
            return url;
        }

        /// <summary>El criterio de siempre (GestorPresupuestos, NestoAPI#170) y, además, la respuesta vacía («https://» solo).</summary>
        internal static bool EsRota(string ruta)
        {
            return GestorPresupuestos.EsRutaImagenRota(ruta) || string.IsNullOrWhiteSpace(ruta.Trim().Replace("https://", string.Empty));
        }
    }

    internal class CacheFotosProducto
    {
        private readonly ConcurrentDictionary<string, (string Url, DateTime Caduca)> fotos =
            new ConcurrentDictionary<string, (string, DateTime)>(StringComparer.OrdinalIgnoreCase);

        public bool TryLeer(string producto, DateTime ahora, out string url)
        {
            if (fotos.TryGetValue(producto, out (string Url, DateTime Caduca) entrada) && entrada.Caduca > ahora)
            {
                url = entrada.Url;
                return true;
            }
            url = null;
            return false;
        }

        public void Guardar(string producto, string url, DateTime caduca)
        {
            fotos[producto] = (url, caduca);
        }

        public void Olvidar(string producto)
        {
            _ = fotos.TryRemove(producto, out _);
        }
    }
}
