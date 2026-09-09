using NestoAPI.Infraestructure.Buscador;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static NestoAPI.Infraestructure.Buscador.LuceneBuscador;

namespace NestoAPI.Infraestructure.Videos
{
    public class ServicioVideos : IServicioVideos
    {
        private static readonly DateTime fechaLimite = DateTime.Now.AddYears(-3);

        /// <summary>
        /// Cuánto protocolo se trae de la base de datos para componer la descripción de la ficha.
        /// Con esto sobra para saltar los encabezados de cabecera y quedarse con el primer párrafo,
        /// y evita arrastrar protocolos enteros (hasta 4 KB) en cada página del listado.
        /// </summary>
        private const int CaracteresDeProtocoloQueSeLeen = 1200;

        public Task<List<VideoLookupModel>> GetVideos(int skip, int take, bool tieneComprasRecientes, bool soloProtocolos = false, bool incluirBajas = false)
        {
            using (NVEntities db = new NVEntities())
            {
                IQueryable<Video> query = db.Videos.AsQueryable();

                query = QuitarLasBajas(query, incluirBajas);

                if (soloProtocolos)
                {
                    query = query.Where(v => v.EsUnProtocolo);
                }

                // El desempate por Id NO es cosmético: este listado se pagina con Skip/Take en
                // peticiones independientes, y hay vídeos que comparten FechaPublicacion. Sin un
                // orden total, dos páginas consecutivas pueden repetir una fila y saltarse otra, y
                // la tienda —que deduce las bajas por ausencia— despublicaría un vídeo vivo.
                query = !tieneComprasRecientes
                    // Si no es cliente, primero los videos antiguos (disponibles)
                    // y luego los nuevos (restringidos)
                    ? query
                        .OrderBy(v => v.FechaPublicacion >= fechaLimite) // Primero los antiguos (false)
                        .ThenByDescending(v => v.FechaPublicacion)       // Después por fecha descendente
                        .ThenBy(v => v.Id)
                    // Si es cliente, los más recientes primero (orden normal)
                    : query
                        .OrderByDescending(v => v.FechaPublicacion)
                        .ThenBy(v => v.Id);

                List<VideoLookupModel> videos = Proyectar(query.Skip(skip).Take(take), tieneComprasRecientes)
                    .ToList()
                    .Select(AModelo)
                    .ToList();

                return Task.FromResult(videos);
            }
        }

        public Task<List<VideoLookupModel>> GetVideos(List<int> ids, bool tieneComprasRecientes, bool soloProtocolos = false)
        {
            using (NVEntities db = new NVEntities())
            {
                // El buscador no ofrece vídeos retirados: el índice de Lucene puede ir por detrás.
                IQueryable<Video> query = QuitarLasBajas(db.Videos.Where(v => ids.Contains(v.Id)), incluirBajas: false);

                if (soloProtocolos)
                {
                    query = query.Where(v => v.EsUnProtocolo);
                }

                List<VideoLookupModel> videos = Proyectar(query, tieneComprasRecientes)
                    .ToList()
                    .Select(AModelo)
                    .OrderBy(v => ids.IndexOf(v.Id)) // Mantener el orden de relevancia devuelto por Lucene
                    .ToList();

                return Task.FromResult(videos);
            }
        }

        public Task<List<VideoLookupModel>> GetVideosConProducto(string productoId)
        {
            using (NVEntities db = new NVEntities())
            {
                // Buscar todos los videos que contengan el producto especificado
                IQueryable<Video> query = QuitarLasBajas(
                        db.Videos.Where(v => v.VideosProductos.Any(vp => vp.Referencia == productoId)),
                        incluirBajas: false)
                    .OrderByDescending(v => v.FechaPublicacion)
                    .ThenBy(v => v.Id);

                List<VideoLookupModel> videos = Proyectar(query, tieneComprasRecientes: true)
                    .ToList()
                    .Select(AModelo)
                    .ToList();

                return Task.FromResult(videos);
            }
        }

        public Task<List<VideoLookupModel>> BuscarVideos(string query, bool tieneComprasRecientes, bool soloProtocolos = false, int skip = 0, int take = 20)
        {
            List<VideoResultadoBusqueda> resultadosLucene = LuceneBuscador.BuscarVideos(query, skip, take);
            List<int> ids = resultadosLucene.Select(r => r.Id).ToList();

            List<VideoLookupModel> videos = GetVideos(ids, tieneComprasRecientes, soloProtocolos).Result;
            AnotarProductoCoincidente(query, videos);
            return Task.FromResult(videos);
        }

        /// <summary>
        /// NestoAPI#454: para cada vídeo del resultado, el momento del producto que casa con la
        /// búsqueda (si casa alguno), para que el buscador de la tienda enlace con &amp;t= en vez de
        /// al minuto 0. Una sola consulta para todos los vídeos de la página: la ruta caliente del
        /// buscador no puede pagar una llamada por vídeo.
        /// </summary>
        private static void AnotarProductoCoincidente(string query, List<VideoLookupModel> videos)
        {
            if (videos == null || videos.Count == 0)
            {
                return;
            }
            List<int> ids = videos.Select(v => v.Id).ToList();
            using (NVEntities db = new NVEntities())
            {
                var productos = db.Videos
                    .Where(v => ids.Contains(v.Id))
                    .SelectMany(v => v.VideosProductos.Select(vp => new { VideoId = v.Id, vp.NombreProducto, vp.TiempoAparicion }))
                    .ToList();

                foreach (VideoLookupModel video in videos)
                {
                    CoincidenciaProductoVideo.Coincidencia coincidencia = CoincidenciaProductoVideo.Elegir(query,
                        productos.Where(p => p.VideoId == video.Id)
                            .Select(p => new CoincidenciaProductoVideo.ProductoEnVideo { Nombre = p.NombreProducto, TiempoAparicion = p.TiempoAparicion }));
                    if (coincidencia != null)
                    {
                        video.TiempoAparicion = coincidencia.Segundos;
                        video.ProductoCoincidente = coincidencia.Producto;
                    }
                }
            }
        }

        /// <summary>
        /// Obtiene un videoprotocolo para mostrar en correos promocionales.
        /// Por ahora devuelve el último videoprotocolo publicado.
        /// FUTURO: Personalizar según el cliente y sus compras habituales.
        /// </summary>
        /// <param name="cliente">Cliente al que se enviará el correo (reservado para futura personalización)</param>
        /// <returns>VideoLookupModel del videoprotocolo o null si no hay ninguno</returns>
        public Task<VideoLookupModel> ObtenerVideoprotocoloParaCorreo(string cliente = null)
        {
            // FUTURO: Si se proporciona cliente, buscar videoprotocolos relacionados con
            // los productos que más compra (familias/grupos de sus compras recientes).
            // Por ahora, simplemente devolvemos el último videoprotocolo publicado.

            using (NVEntities db = new NVEntities())
            {
                IQueryable<Video> query = QuitarLasBajas(db.Videos.Where(v => v.EsUnProtocolo), incluirBajas: false)
                    .OrderByDescending(v => v.Id);

                FilaVideo fila = Proyectar(query, tieneComprasRecientes: true).FirstOrDefault();

                return Task.FromResult(fila == null ? null : AModelo(fila));
            }
        }

        /// <summary>
        /// Deja fuera los vídeos retirados salvo que se pidan expresamente.
        ///
        /// <para><b>El listado por defecto NO devuelve las bajas, y no puede cambiar</b>: la versión
        /// del módulo de la tienda que hay en producción (1.6.6) pide el listado sin
        /// <c>incluirBajas</c>, no mira <c>FechaBaja</c> y REACTIVA cualquier ficha que reaparezca
        /// en el listado. Si esto dejara de filtrar, esa versión resucitaría de golpe las 84 fichas
        /// que ya están de baja (los 77 vídeos retirados y las 7 gemelas duplicadas): 84 URL muertas
        /// devolviendo 200 (equipo de SEO, 08/09/26).</para>
        ///
        /// <para>Está aquí, en un método con nombre y con tests, precisamente para que sea difícil
        /// cambiarlo sin querer.</para>
        /// </summary>
        internal static IQueryable<Video> QuitarLasBajas(IQueryable<Video> query, bool incluirBajas)
        {
            return incluirBajas ? query : query.Where(v => v.FechaBaja == null);
        }

        /// <summary>
        /// La única proyección del listado: todo lo que viaja a los clientes sale de aquí, para que
        /// una columna nueva no haya que añadirla en cinco sitios. Del protocolo solo se lee la
        /// cabecera, que es lo que necesita <see cref="DescripcionFichaVideo"/>.
        /// </summary>
        private static IQueryable<FilaVideo> Proyectar(IQueryable<Video> query, bool tieneComprasRecientes)
        {
            return query.Select(v => new FilaVideo
            {
                Id = v.Id,
                VideoId = v.VideoId,
                Titulo = v.Titulo,
                Descripcion = v.Descripcion,
                InicioProtocolo = v.Protocolo.Substring(0, CaracteresDeProtocoloQueSeLeen),
                FechaPublicacion = v.FechaPublicacion,
                EsUnProtocolo = v.EsUnProtocolo,
                FechaBaja = v.FechaBaja,
                BloqueadoPorComprasRecientes = v.FechaPublicacion >= fechaLimite && !tieneComprasRecientes
            });
        }

        private static VideoLookupModel AModelo(FilaVideo fila)
        {
            return new VideoLookupModel
            {
                Id = fila.Id,
                VideoId = fila.VideoId,
                Titulo = fila.Titulo,
                Descripcion = fila.Descripcion,
                DescripcionParaFicha = DescripcionFichaVideo.Componer(fila.Descripcion, fila.InicioProtocolo),
                FechaPublicacion = fila.FechaPublicacion ?? DateTime.MinValue,
                EsUnProtocolo = fila.EsUnProtocolo,
                FechaBaja = fila.FechaBaja,
                BloqueadoPorComprasRecientes = fila.BloqueadoPorComprasRecientes
            };
        }

        /// <summary>Lo que se trae de la base de datos, antes de componer el modelo que se publica.</summary>
        private class FilaVideo
        {
            public int Id { get; set; }
            public string VideoId { get; set; }
            public string Titulo { get; set; }
            public string Descripcion { get; set; }
            public string InicioProtocolo { get; set; }
            public DateTime? FechaPublicacion { get; set; }
            public bool EsUnProtocolo { get; set; }
            public DateTime? FechaBaja { get; set; }
            public bool BloqueadoPorComprasRecientes { get; set; }
        }
    }


}
