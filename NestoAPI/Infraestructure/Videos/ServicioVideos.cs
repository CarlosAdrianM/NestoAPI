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

        public Task<List<VideoLookupModel>> BuscarVideos(string query, bool tieneComprasRecientes, int skip = 0, int take = 20)
        {
            List<VideoResultadoBusqueda> resultadosLucene = LuceneBuscador.BuscarVideos(query, skip, take);
            List<int> ids = resultadosLucene.Select(r => r.Id).ToList();

            return GetVideos(ids, tieneComprasRecientes);
        }
    }


}
