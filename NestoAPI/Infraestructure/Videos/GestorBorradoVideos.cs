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
        ForzarNoPermitido,
        /// <summary>Carlos 28/09/26: es el único activo de sus duplicados (los otros están de baja).</summary>
        DuplicadosDeBaja
    }

    public enum EstadoBajaVideo
    {
        DadoDeBaja,
        NoExiste,
        YaEstabaDeBaja
    }

    public class ResultadoBajaVideo
    {
        public EstadoBajaVideo Estado { get; set; }
        public string Mensaje { get; set; }
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
        internal const string MENSAJE_DUPLICADOS_DE_BAJA =
            "Los demás vídeos con el mismo vídeo de YouTube están de baja: si borras este, no queda ninguno activo. Si lo que quieres es retirarlo, dalo de baja.";
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

            var duplicados = string.IsNullOrWhiteSpace(video.VideoId)
                ? new List<DuplicadoVideo>()
                : await db.Videos
                    .Where(v => v.Id != id && v.VideoId == video.VideoId)
                    .Select(v => new DuplicadoVideo { Id = v.Id, DeBaja = v.FechaBaja != null })
                    .ToListAsync().ConfigureAwait(false);
            List<int> duplicadoDe = duplicados.Select(d => d.Id).ToList();

            // Carlos 28/09/26: borrar el activo cuando los duplicados están de baja deja el vídeo sin
            // ninguna ficha viva: eso es retirarlo, y retirar es la baja (salvo Dirección forzando).
            if (video.FechaBaja == null && duplicados.Any() && duplicados.All(d => d.DeBaja) && !(forzar && puedeForzar))
            {
                return new ResultadoBorradoVideo { Estado = EstadoBorradoVideo.DuplicadosDeBaja, Mensaje = MENSAJE_DUPLICADOS_DE_BAJA };
            }

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

        /// <summary>
        /// Carlos 28/09/26: tienda online retira desde Nesto los vídeos que no pintan nada en Nesto
        /// (shorts, vídeos de vida corta). Es la misma baja que hace NVIA: FechaBaja; el vídeo sale del
        /// listado, del buscador y de la tienda, y se puede reponer quitando la fecha.
        /// </summary>
        public async Task<ResultadoBajaVideo> DarDeBaja(int id, string usuario)
        {
            Video video = await db.Videos.Where(v => v.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);
            if (video == null)
            {
                return new ResultadoBajaVideo { Estado = EstadoBajaVideo.NoExiste, Mensaje = $"No existe el vídeo {id}" };
            }
            if (video.FechaBaja != null)
            {
                return new ResultadoBajaVideo { Estado = EstadoBajaVideo.YaEstabaDeBaja, Mensaje = $"El vídeo ya estaba de baja desde el {video.FechaBaja:dd/MM/yyyy}." };
            }
            video.FechaBaja = DateTime.Now;
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
            return new ResultadoBajaVideo
            {
                Estado = EstadoBajaVideo.DadoDeBaja,
                Mensaje = $"Vídeo {video.Id} (YouTube {video.VideoId}) «{video.Titulo}» dado de baja por {usuario}"
            };
        }

        private class DuplicadoVideo
        {
            public int Id { get; set; }
            public bool DeBaja { get; set; }
        }

        private static string Recortar(string texto, int longitud)
        {
            return texto.Length <= longitud ? texto : texto.Substring(0, longitud);
        }
    }
}
