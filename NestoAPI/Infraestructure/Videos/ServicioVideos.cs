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
        public Task<List<VideoLookupModel>> GetVideos(int skip, int take, bool tieneComprasRecientes)
        {
            using (NVEntities db = new NVEntities())
            {
                IQueryable<Video> query = db.Videos.AsQueryable();

                if (!tieneComprasRecientes)
                {
                    // Si no es cliente, primero los videos antiguos (disponibles)
                    // y luego los nuevos (restringidos)
                    query = query
                        .OrderBy(v => v.FechaPublicacion >= fechaLimite) // Primero los antiguos (false)
                        .ThenByDescending(v => v.FechaPublicacion);                   // Después por fecha descendente
                }
                else
                {
                    // Si es cliente, los más recientes primero (orden normal)
                    query = query.OrderByDescending(v => v.FechaPublicacion);
                }

                List<VideoLookupModel> videos = query
                    .Skip(skip)
                    .Take(take)
                    .Select(v => new VideoLookupModel
                    {
                        Id = v.Id,
                        VideoId = v.VideoId,
                        Titulo = v.Titulo,
                        Descripcion = v.Descripcion,
                        FechaPublicacion = (DateTime)v.FechaPublicacion,
                        EsUnProtocolo = v.EsUnProtocolo,
                        BloqueadoPorComprasRecientes = v.FechaPublicacion >= fechaLimite && !tieneComprasRecientes
                    })
                    .ToList();

                return Task.FromResult(videos);
            }
        }

        public Task<List<VideoLookupModel>> GetVideos(List<int> ids, bool tieneComprasRecientes)
        {
            using (NVEntities db = new NVEntities())
            {
                var videos = db.Videos
                    .Where(v => ids.Contains(v.Id))
                    .ToList()
                    .Select(v => new VideoLookupModel
                    {
                        Id = v.Id,
                        VideoId = v.VideoId,
                        Titulo = v.Titulo,
                        Descripcion = v.Descripcion,
                        FechaPublicacion = (DateTime)v.FechaPublicacion,
                        EsUnProtocolo = v.EsUnProtocolo,
                        BloqueadoPorComprasRecientes = v.FechaPublicacion >= fechaLimite && !tieneComprasRecientes
                    })
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
                IQueryable<Video> query = db.Videos
                    .Where(v => v.VideosProductos.Any(vp => vp.Referencia == productoId))
                    .OrderByDescending(v => v.FechaPublicacion);


                List<VideoLookupModel> videos = query
                    .Select(v => new VideoLookupModel
                    {
                        Id = v.Id,
                        VideoId = v.VideoId,
                        Titulo = v.Titulo,
                        Descripcion = v.Descripcion,
                        FechaPublicacion = (DateTime)v.FechaPublicacion,
                        EsUnProtocolo = v.EsUnProtocolo,
                        BloqueadoPorComprasRecientes = false
                    })
                    .ToList();

                return Task.FromResult(videos);
            }
        }

        public Task<List<VideoLookupModel>> BuscarVideos(string query, bool tieneComprasRecientes, int skip = 0, int take = 20)
        {
            List<VideoResultadoBusqueda> resultadosLucene = LuceneBuscador.BuscarVideos(query, skip, take);
            List<int> ids = resultadosLucene.Select(r => r.Id).ToList();

            return GetVideos(ids, tieneComprasRecientes);
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
                var video = db.Videos
                    .Where(v => v.EsUnProtocolo)
                    .OrderByDescending(v => v.Id)
                    .Select(v => new VideoLookupModel
                    {
                        Id = v.Id,
                        VideoId = v.VideoId,
                        Titulo = v.Titulo,
                        Descripcion = v.Descripcion,
                        FechaPublicacion = v.FechaPublicacion ?? DateTime.MinValue,
                        EsUnProtocolo = v.EsUnProtocolo,
                        BloqueadoPorComprasRecientes = false
                    })
                    .FirstOrDefault();

                return Task.FromResult(video);
            }
        }
    }


}
