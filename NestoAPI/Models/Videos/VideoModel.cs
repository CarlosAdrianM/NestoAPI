using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Videos
{
    public class VideoModel
    {
        public VideoModel()
        {
            if (Productos == null)
            {
                foreach (ProductoVideoModel producto in Productos)
                {
                    producto.Video = this;
                }
            }
        }
        public int Id { get; set; }
        public string VideoId { get; set; } // Si no es cliente dejamos en blanco
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public DateTime FechaPublicacion { get; set; }
        public string Protocolo { get; set; }
        public string UrlVideo => !string.IsNullOrEmpty(VideoId) ? $"https://www.youtube.com/watch?v={VideoId}" : string.Empty;
        public string UrlImagen => !string.IsNullOrEmpty(VideoId) ? $"https://img.youtube.com/vi/{VideoId}/0.jpg" : string.Empty;

        public List<ProductoVideoModel> Productos { get; set; }
    }
}
