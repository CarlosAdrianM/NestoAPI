using NestoAPI.Infraestructure.Videos;
using NestoAPI.Models;
using NestoAPI.Models.Videos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/Videos")]
    public class VideosController : ApiController
    {
        private readonly IServicioVideos _servicioVideos;

        public VideosController(IServicioVideos servicioVideos)
        {
            _servicioVideos = servicioVideos;
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<VideoLookupModel>))]
        public async Task<IHttpActionResult> GetVideos(int skip, int take, bool soloProtocolos = false)
        {
            bool tieneComprasRecientes = false;

            // Verificar si el usuario está autenticado y tiene el claim
            if (User.Identity.IsAuthenticated && User.Identity is ClaimsIdentity identity)
            {
                Claim purchasesClaim = identity.FindFirst("HasRecentPurchases");
                if (purchasesClaim != null)
                {
                    _ = bool.TryParse(purchasesClaim.Value, out tieneComprasRecientes);
                }
            }

            // Obtener los videos en función de si tiene compras recientes
            List<VideoLookupModel> videos = await _servicioVideos.GetVideos(skip, take, tieneComprasRecientes, soloProtocolos);
            return Ok(videos);
        }


        // Si necesitas otros endpoints específicos para videos, puedes agregarlos aquí
        // Por ejemplo, un endpoint para obtener detalles de un video específico

        [HttpGet]
        [Route("{id:int}")]
        [ResponseType(typeof(VideoModel))]
        public async Task<IHttpActionResult> GetVideoById(int id)
        {
            bool tieneComprasRecientes = false;

            // Verificar si está autenticado y tiene el claim
            if (User.Identity.IsAuthenticated)
            {
                if (User.Identity is ClaimsIdentity identity)
                {
                    Claim purchasesClaim = identity.FindFirst("HasRecentPurchases");
                    if (purchasesClaim != null)
                    {
                        _ = bool.TryParse(purchasesClaim.Value, out tieneComprasRecientes);
                    }
                }
            }

            // Lógica para obtener un video por ID
            // Esto es un ejemplo, ajusta según tu servicio real
            using (NVEntities db = new NVEntities())
            {
                Video video = await db.Videos.FindAsync(id);

                if (video == null)
                {
                    return NotFound();
                }

                // Verificar si el usuario puede acceder a este video
                bool esVideoReciente = video.FechaPublicacion >= DateTime.Now.AddYears(-3);
                if (esVideoReciente && !tieneComprasRecientes)
                {
                    return Content(System.Net.HttpStatusCode.Forbidden,
                        "Este video de momento sólo está disponible para clientes.");
                }

                VideoModel model = new VideoModel
                {
                    Id = video.Id,
                    VideoId = video.VideoId,
                    Titulo = video.Titulo,
                    Descripcion = video.Descripcion,
                    FechaPublicacion = (DateTime)video.FechaPublicacion,
                    Protocolo = video.Protocolo,
                    Productos = video.VideosProductos.Select(vp => new ProductoVideoModel
                    {
                        Id = vp.Id,
                        NombreProducto = vp.NombreProducto,
                        Referencia = vp.Referencia,
                        EnlaceTienda = vp.EnlaceTienda,
                        EnlaceVideo = vp.EnlaceVideo,
                        TiempoAparicion = vp.TiempoAparicion
                    }).ToList()
                };

                return Ok(model);
            }
        }

        [HttpGet]
        [Route("Buscar")]
        public async Task<IHttpActionResult> Buscar(
            [FromUri] string q,
            [FromUri] int skip = 0,
            [FromUri] int take = 20,
            [FromUri] bool soloProtocolos = false)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest("Debe proporcionar una consulta.");
            }

            bool tieneComprasRecientes = false;

            // Verificar si el usuario está autenticado y tiene el claim
            if (User.Identity.IsAuthenticated && User.Identity is ClaimsIdentity identity)
            {
                Claim purchasesClaim = identity.FindFirst("HasRecentPurchases");
                if (purchasesClaim != null)
                {
                    _ = bool.TryParse(purchasesClaim.Value, out tieneComprasRecientes);
                }
            }

            List<VideoLookupModel> resultados = await _servicioVideos.BuscarVideos(q, tieneComprasRecientes, soloProtocolos, skip, take);
            return Ok(resultados);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("Producto/{productoId}")]
        [ResponseType(typeof(List<VideoLookupModel>))]
        public async Task<IHttpActionResult> GetVideosPorProducto(string productoId)
        {
            if (string.IsNullOrWhiteSpace(productoId))
            {
                return BadRequest("Debe proporcionar un ID de producto válido.");
            }
            List<VideoLookupModel> videos = await _servicioVideos.GetVideosConProducto(productoId);
            return Ok(videos);
        }
    }
}