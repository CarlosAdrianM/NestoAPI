using Hangfire;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Data.Entity;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#701: publicar un producto en la tienda (Pub/Sub) SIN que un Google lento rompa el guardado.
    /// El 23/10/26 Enrique guardó los textos web de un producto 8 veces entre las 10:11 y las 10:13 porque
    /// <c>PUT api/PrestashopProductos</c> guardaba bien y luego fallaba con <c>DeadlineExceeded</c> al publicar:
    /// Nesto enseñaba un error aunque lo guardado ya estaba hecho, y el cambio no llegaba a la tienda hasta que
    /// alguien volviera a guardar.
    ///
    /// Ahora, si Pub/Sub falla:
    ///   1. La petición responde bien con un aviso (<see cref="AVISO_TIENDA_PENDIENTE"/>) y deja programado en
    ///      Hangfire el reintento nº 1 (a los 2 minutos).
    ///   2. Cada reintento vuelve a construir el mensaje por la puerta de publicación con el estado ACTUAL del
    ///      producto (no con el DTO de aquel momento) y publica. Si vuelve a fallar, programa el siguiente con
    ///      espera creciente (2, 4, 8 y 16 minutos: <see cref="Espera"/>).
    ///   3. Agotados los <see cref="MAXIMO_INTENTOS"/>: ELMAH y correo a Tienda online.
    ///
    /// Es el núcleo común (DRY) para todo lo que publique un producto tras guardar; las dependencias se inyectan
    /// para poder probarlo con un publicador que falla.
    /// </summary>
    public class PublicacionTienda
    {
        /// <summary>Reintentos en segundo plano tras el fallo en línea.</summary>
        public const int MAXIMO_INTENTOS = 4;
        public const string AVISO_TIENDA_PENDIENTE = "Guardado. La tienda se actualizará en unos minutos.";
        public const string USUARIO_ELMAH = "Sistema (publicación en la tienda)";

        /// <summary>Espera antes del reintento <paramref name="intento"/> (1 → 2 min, 2 → 4, 3 → 8, 4 → 16).</summary>
        public static TimeSpan Espera(int intento) => TimeSpan.FromMinutes(2 << Math.Max(0, intento - 1));

        private readonly IGestorProductos gestor;
        private readonly Func<string, Task<ResultadoPuertaPublicacion>> construirPorLaPuerta;
        private readonly Func<string, string, string, int, TimeSpan, string> programar;
        private readonly IServicioCorreoElectronico correo;
        private readonly Action<Exception> log;

        /// <param name="gestor">Quien publica el mensaje en Pub/Sub.</param>
        /// <param name="construirPorLaPuerta">(productoId) → el resultado de la puerta con el DTO del estado actual; null si el producto no existe. Solo hace falta para <see cref="Reintentar"/>.</param>
        /// <param name="programar">(productoId, source, usuario, intento, espera) → id del job. Por defecto, BackgroundJob.Schedule del job de reintento.</param>
        /// <param name="correo">Para el aviso a Tienda online al agotar los intentos.</param>
        /// <param name="log">A ELMAH, por defecto.</param>
        public PublicacionTienda(IGestorProductos gestor,
            Func<string, Task<ResultadoPuertaPublicacion>> construirPorLaPuerta = null,
            Func<string, string, string, int, TimeSpan, string> programar = null,
            IServicioCorreoElectronico correo = null,
            Action<Exception> log = null)
        {
            this.gestor = gestor;
            this.construirPorLaPuerta = construirPorLaPuerta;
            this.programar = programar ?? ((productoId, source, usuario, intento, espera) =>
                BackgroundJob.Schedule(() => ReintentoPublicacionTiendaJobsService.Reintentar(productoId, source, usuario, intento), espera));
            this.correo = correo ?? new ServicioCorreoElectronico();
            this.log = log ?? (ex => ElmahHelper.Log(ex, USUARIO_ELMAH));
        }

        /// <summary>
        /// El intento en línea, justo después de guardar. Si Pub/Sub falla, programa el reintento nº 1 y devuelve
        /// el aviso para el usuario en vez de lanzar. Solo lanza (el error original) si tampoco se puede programar
        /// el reintento: entonces el usuario tiene que saber que la tienda NO se va a actualizar.
        /// </summary>
        public async Task<ResultadoPublicacionTienda> PublicarOAplazar(ProductoDTO dto, string source, string usuario)
        {
            if (gestor == null || dto == null)
            {
                return ResultadoPublicacionTienda.NadaQuePublicar("sin gestor o sin producto");
            }
            try
            {
                await gestor.PublicarProductoSincronizar(dto, source, usuario).ConfigureAwait(false);
                return ResultadoPublicacionTienda.Hecha();
            }
            catch (Exception ex)
            {
                string productoId = dto.Producto?.Trim();
                Console.WriteLine($"⚠️ Pub/Sub ha fallado publicando {productoId} ({ex.GetType().Name}: {ex.Message}); se reintenta en {Espera(1).TotalMinutes} min");
                string jobId = null;
                bool programado = false;
                try
                {
                    jobId = programar(productoId, source, usuario, 1, Espera(1));
                    programado = true;
                }
                catch (Exception exProgramar)
                {
                    log(new Exception($"[PublicacionTienda] Pub/Sub ha fallado con el producto {productoId} y tampoco se ha podido programar el reintento: {exProgramar.Message}", exProgramar));
                }
                if (!programado)
                {
                    throw;
                }
                return ResultadoPublicacionTienda.Aplazada(jobId, ex);
            }
        }

        /// <summary>
        /// Un reintento programado: reconstruye el mensaje por la puerta con el estado actual del producto y publica.
        /// Si falla y quedan intentos, programa el siguiente con más espera; si era el último, ELMAH y correo a Tienda online.
        /// </summary>
        public async Task<ResultadoPublicacionTienda> Reintentar(string productoId, string source, string usuario, int intento)
        {
            if (construirPorLaPuerta == null)
            {
                throw new InvalidOperationException("Para reintentar hace falta saber construir el producto por la puerta de publicación");
            }
            try
            {
                ResultadoPuertaPublicacion puerta = await construirPorLaPuerta(productoId).ConfigureAwait(false);
                if (puerta == null)
                {
                    return ResultadoPublicacionTienda.NadaQuePublicar($"el producto {productoId} ya no existe");
                }
                if (!puerta.Publicable)
                {
                    // El estado actual manda: si ahora no debe estar en la tienda, no se publica ni se insiste.
                    return ResultadoPublicacionTienda.NadaQuePublicar($"el producto {productoId} ya no es publicable: {puerta.Motivo}");
                }
                await gestor.PublicarProductoSincronizar(puerta.Dto, source, usuario).ConfigureAwait(false);
                Console.WriteLine($"✅ Reintento {intento}/{MAXIMO_INTENTOS}: producto {productoId} publicado en la tienda");
                return ResultadoPublicacionTienda.Hecha();
            }
            catch (Exception ex)
            {
                if (intento < MAXIMO_INTENTOS)
                {
                    int siguiente = intento + 1;
                    Console.WriteLine($"⚠️ Reintento {intento}/{MAXIMO_INTENTOS} del producto {productoId} ha fallado ({ex.Message}); el {siguiente} va en {Espera(siguiente).TotalMinutes} min");
                    string jobId = programar(productoId, source, usuario, siguiente, Espera(siguiente));
                    return ResultadoPublicacionTienda.Aplazada(jobId, ex);
                }
                Exception agotado = new Exception(MensajeAgotado(productoId, intento, ex), ex);
                log(agotado);
                AvisarTiendaOnline(productoId, intento, ex);
                return ResultadoPublicacionTienda.SinMasIntentos(ex);
            }
        }

        internal static string MensajeAgotado(string productoId, int intentos, Exception ultimo) =>
            $"[PublicacionTienda] No se ha podido actualizar en la tienda el producto {productoId} tras {intentos} reintentos. " +
            $"Último error: {ultimo?.GetType().Name}: {ultimo?.Message}";

        private void AvisarTiendaOnline(string productoId, int intentos, Exception ultimo)
        {
            try
            {
                MailMessage mail = new MailMessage
                {
                    From = new MailAddress("nesto@nuevavision.es", "NestoAPI"),
                    Subject = $"No se ha podido actualizar en la tienda el producto {productoId}",
                    IsBodyHtml = true,
                    Body =
                        $"<p>El producto <b>{productoId}</b> se guardó bien en Nesto, pero no se ha podido avisar a la tienda " +
                        $"(Google Pub/Sub ha fallado en el momento de guardar y en los {intentos} reintentos posteriores).</p>" +
                        $"<p>Último error: {ultimo?.Message}</p>" +
                        "<p>Para que la tienda reciba el cambio, vuelva a guardar la ficha del producto en Nesto " +
                        "(o publíquelo con <code>api/Productos/Publicar/" + productoId + "</code>). " +
                        "Si el problema persiste, avise a Informática.</p>"
                };
                mail.To.Add(Constantes.Correos.TIENDA_ONLINE);
                _ = correo.EnviarCorreoSMTP(mail);
            }
            catch (Exception ex)
            {
                log(new Exception($"[PublicacionTienda] No se ha podido avisar a Tienda online del producto {productoId}: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Carga el producto como lo hace cada sitio que publica (kits, familia y subgrupo) y lo pasa por la puerta
        /// de publicación. null si el producto no existe. Es el camino que usan el PUT de textos web y el reintento,
        /// para que ambos publiquen exactamente lo mismo.
        /// </summary>
        public static async Task<ResultadoPuertaPublicacion> ConstruirPorLaPuerta(string productoId, NVEntities db, IProductoService productoService)
        {
            Producto producto = await db.Productos
                .Include(p => p.Kits)
                .Include(p => p.Familia1)
                .Include(p => p.SubGruposProducto)
                .SingleOrDefaultAsync(p => p.Número == productoId && p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO)
                .ConfigureAwait(false);
            if (producto == null)
            {
                return null;
            }
            return await PuertaPublicacionTienda.ConstruirParaPublicarSiPasa(producto, db, productoService).ConfigureAwait(false);
        }
    }

    /// <summary>Lo que ha pasado con la publicación: publicada, aplazada (reintento programado), agotada o nada que publicar.</summary>
    public class ResultadoPublicacionTienda
    {
        public bool Publicada { get; private set; }
        /// <summary>Hay un reintento programado en Hangfire (su id, informativo).</summary>
        public string JobReintento { get; private set; }
        /// <summary>El aviso para el usuario cuando lo guardado llegará a la tienda más tarde; null si no hay nada que avisar.</summary>
        public string Aviso { get; private set; }
        /// <summary>Por qué no se ha publicado nada (no publicable, no existe...). null si se publicó o se aplazó.</summary>
        public string Motivo { get; private set; }
        public Exception Error { get; private set; }
        public bool Agotada { get; private set; }

        public static ResultadoPublicacionTienda Hecha() => new ResultadoPublicacionTienda { Publicada = true };

        public static ResultadoPublicacionTienda Aplazada(string jobId, Exception error) => new ResultadoPublicacionTienda
        {
            JobReintento = jobId,
            Aviso = PublicacionTienda.AVISO_TIENDA_PENDIENTE,
            Error = error
        };

        public static ResultadoPublicacionTienda SinMasIntentos(Exception error) => new ResultadoPublicacionTienda { Agotada = true, Error = error };

        public static ResultadoPublicacionTienda NadaQuePublicar(string motivo) => new ResultadoPublicacionTienda { Motivo = motivo };
    }

    /// <summary>NestoAPI#701: el job de Hangfire con cada reintento. Los reintentos y su espera los lleva <see cref="PublicacionTienda"/>.</summary>
    public static class ReintentoPublicacionTiendaJobsService
    {
        [AutomaticRetry(Attempts = 0)]
        public static async Task Reintentar(string productoId, string source, string usuario, int intento)
        {
            Console.WriteLine($"🔁 [Hangfire] Reintento {intento}/{PublicacionTienda.MAXIMO_INTENTOS} de publicar en la tienda el producto {productoId}");
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                IProductoService productoService = new ProductoService();
                PublicacionTienda publicacion = new PublicacionTienda(
                    new GestorProductos(new SincronizacionEventWrapper(new GooglePubSubEventPublisher())),
                    id => PublicacionTienda.ConstruirPorLaPuerta(id, db, productoService));
                ResultadoPublicacionTienda resultado = await publicacion.Reintentar(productoId, source, usuario, intento).ConfigureAwait(false);
                if (resultado.Motivo != null)
                {
                    Console.WriteLine($"⛔ Reintento del producto {productoId}: {resultado.Motivo}");
                }
            }
        }
    }
}
