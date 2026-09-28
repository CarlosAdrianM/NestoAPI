using System.Collections.Generic;

namespace NestoAPI.Models.Videos
{
    /// <summary>
    /// NestoAPI#545: lo que se ha borrado con DELETE api/Videos/{id}, para que quien lo pidió (Nesto)
    /// pueda confirmarlo y, si hiciera falta, rehacerlo (también queda en LogVideosProductos y en ELMAH).
    /// </summary>
    public class VideoBorradoDTO
    {
        public int Id { get; set; }
        public string VideoId { get; set; }
        public string Titulo { get; set; }
        public int ProductosBorrados { get; set; }

        /// <summary>Los otros vídeos con el mismo VideoId de YouTube (vacío si se borró forzando).</summary>
        public List<int> DuplicadoDe { get; set; } = new List<int>();
    }
}
