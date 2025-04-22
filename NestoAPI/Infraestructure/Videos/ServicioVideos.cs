using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Videos
{
    public class ServicioVideos : IServicioVideos
    {
        public Task<List<VideoLookupModel>> GetVideos(int skip, int take, bool tieneComprasRecientes)
        {
            using (NVEntities db = new NVEntities())
            {
                IQueryable<Video> query = db.Videos.AsQueryable();
                DateTime fechaHasta = DateTime.Now.AddYears(-3);

                if (!tieneComprasRecientes)
                {
                    // Si no es cliente, primero los videos antiguos (disponibles)
                    // y luego los nuevos (restringidos)
                    query = query
                        .OrderBy(v => v.FechaPublicacion >= fechaHasta) // Primero los antiguos (false)
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
                        BloqueadoPorComprasRecientes = v.FechaPublicacion >= fechaHasta && !tieneComprasRecientes
                    })
                    .ToList();

                return Task.FromResult(videos);
            }
        }
    }
}
