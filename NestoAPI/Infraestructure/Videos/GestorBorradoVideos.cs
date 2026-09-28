using NestoAPI.Models;
using NestoAPI.Models.Videos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Videos
{
    public enum EstadoBorradoVideo
    {
        Borrado,
        NoExiste,
        /// <summary>No hay otro vídeo con el mismo VideoId: para retirarlo se usa la baja.</summary>
        NoEsDuplicado,
        /// <summary>Se ha pedido forzar el borrado de un vídeo que no es duplicado sin ser de Dirección.</summary>
        ForzarNoPermitido
    }

    public class ResultadoBorradoVideo
    {
        public EstadoBorradoVideo Estado { get; set; }
        public string Mensaje { get; set; }
        public VideoBorradoDTO Borrado { get; set; }
    }

    /// <summary>
    /// NestoAPI#545: borrar un vídeo DUPLICADO (dos filas de Videos con el mismo VideoId de YouTube,
    /// como 1981 y 1983 el 25/09/26) sin tener que pedir un script.
    ///
    /// <para>Borrar no es retirar: un vídeo que sale de la tienda se da de baja (FechaBaja), y eso es
    /// lo que hace NVIA. Por eso solo se borra lo que está duplicado; borrar un vídeo que no lo está
    /// exige forzar, y forzar solo lo puede Dirección.</para>
    /// </summary>
    public class GestorBorradoVideos
    {
        internal const string ACCION_LOG = "BorradoVideo";
        internal const string MENSAJE_NO_ES_DUPLICADO =
            "Este vídeo no está duplicado (no hay otro con el mismo vídeo de YouTube). Para retirar un vídeo usa la baja, no el borrado.";
        internal const string MENSAJE_FORZAR_NO_PERMITIDO =
            "Este vídeo no está duplicado y solo Dirección puede borrar un vídeo que no lo esté. Para retirarlo usa la baja.";

        private const int LONGITUD_OBSERVACIONES = 500;

        private readonly NVEntities db;

        public GestorBorradoVideos(NVEntities db)
        {
            this.db = db;
        }

        /// <param name="forzar">Borrar aunque no sea duplicado.</param>
        /// <param name="puedeForzar">Quien lo pide es de Dirección.</param>
        public async Task<ResultadoBorradoVideo> Borrar(int id, bool forzar, bool puedeForzar, string usuario)
        {
            Video video = await db.Videos.Where(v => v.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);
            if (video == null)
            {
                return new ResultadoBorradoVideo { Estado = EstadoBorradoVideo.NoExiste, Mensaje = $"No existe el vídeo {id}" };
            }

            List<int> duplicadoDe = string.IsNullOrWhiteSpace(video.VideoId)
                ? new List<int>()
                : await db.Videos
                    .Where(v => v.Id != id && v.VideoId == video.VideoId)
                    .Select(v => v.Id)
                    .ToListAsync().ConfigureAwait(false);

            if (!duplicadoDe.Any())
            {
                if (!forzar)
                {
                    return new ResultadoBorradoVideo { Estado = EstadoBorradoVideo.NoEsDuplicado, Mensaje = MENSAJE_NO_ES_DUPLICADO };
                }
                if (!puedeForzar)
                {
                    return new ResultadoBorradoVideo { Estado = EstadoBorradoVideo.ForzarNoPermitido, Mensaje = MENSAJE_FORZAR_NO_PERMITIDO };
                }
            }

            List<VideoProducto> productos = await db.VideosProductos
                .Where(vp => vp.VideoId == id)
                .ToListAsync().ConfigureAwait(false);

            string observaciones = Recortar(duplicadoDe.Any()
                ? $"Vídeo {video.Id} (YouTube {video.VideoId}) «{video.Titulo}» borrado por duplicado de {string.Join(", ", duplicadoDe)}"
                : $"Vídeo {video.Id} (YouTube {video.VideoId}) «{video.Titulo}» borrado forzando (no era duplicado)", LONGITUD_OBSERVACIONES);

            // Lo borrado queda en el log (sin FK: sobrevive al borrado) para poder rehacerlo.
            foreach (VideoProducto vp in productos)
            {
                _ = db.LogVideosProductos.Add(new LogVideoProducto
                {
                    VideoProductoId = vp.Id,
                    CampoModificado = "Todos los campos",
                    ValorAnterior = $"Nombre: {vp.NombreProducto}, Referencia: {vp.Referencia}, Tiempo: {vp.TiempoAparicion}, Enlace: {vp.EnlaceTienda}, Enlace vídeo: {vp.EnlaceVideo}",
                    ValorNuevo = null,
                    Usuario = usuario,
                    Accion = ACCION_LOG,
                    Observaciones = observaciones,
                    FechaCambio = DateTime.UtcNow
                });
            }

            // En la BD VideosProductos cuelga de Videos con ON DELETE CASCADE, pero el modelo de EF no
            // lo sabe: los productos se quitan a mano. Todo va en un único SaveChanges, que EF hace en
            // una transacción: o se borra el vídeo con sus productos y su log, o no se borra nada.
            _ = db.VideosProductos.RemoveRange(productos);
            _ = db.Videos.Remove(video);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            return new ResultadoBorradoVideo
            {
                Estado = EstadoBorradoVideo.Borrado,
                Mensaje = observaciones,
                Borrado = new VideoBorradoDTO
                {
                    Id = video.Id,
                    VideoId = video.VideoId,
                    Titulo = video.Titulo,
                    ProductosBorrados = productos.Count,
                    DuplicadoDe = duplicadoDe
                }
            };
        }

        private static string Recortar(string texto, int longitud)
        {
            return texto.Length <= longitud ? texto : texto.Substring(0, longitud);
        }
    }
}
