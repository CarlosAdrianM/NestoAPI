using System;

namespace NestoAPI.Models.Videos
{
    public class ProductoVideoModel
    {
        public VideoModel Video { get; set; }
        public int Id { get; set; }
        public string NombreProducto { get; set; }
        public string Referencia { get; set; }
        public string EnlaceTienda { get; set; }
        public string EnlaceVideo { get; set; }
        public string TiempoAparicion { get; set; }
        public string UrlVideo => !string.IsNullOrEmpty(EnlaceVideo)
                    ? EnlaceVideo
                    : !string.IsNullOrEmpty(Video?.VideoId) ? $"https://www.youtube.com/watch?v={Video.VideoId}&t={StringToSeconds(TiempoAparicion)}" : string.Empty;
        public string UrlImagen { get; set; }


        private int StringToSeconds(string tiempoAparicion)
        {
            if (string.IsNullOrWhiteSpace(tiempoAparicion))
            {
                return 0;
            }

            if (TimeSpan.TryParse(tiempoAparicion, out TimeSpan ts))
            {
                // Si hay milisegundos, redondeamos hacia arriba
                return (int)Math.Ceiling(ts.TotalSeconds);
            }

            // Esto es para evitar que se rompa el video
            // Si no se puede parsear, devolvemos 1
            return 1;
        }

    }
}
