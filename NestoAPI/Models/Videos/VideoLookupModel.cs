using System;

public class VideoLookupModel
{
    public int Id { get; set; }
    public string VideoId { get; set; } // Si no es cliente dejamos en blanco
    public string Titulo { get; set; }
    public string Descripcion { get; set; }
    public DateTime FechaPublicacion { get; set; }
    public bool EsUnProtocolo { get; set; }
    public bool BloqueadoPorComprasRecientes { get; set; }
    public string UrlVideo => !string.IsNullOrEmpty(VideoId) ? $"https://www.youtube.com/watch?v={VideoId}" : string.Empty;
    public string UrlImagen => !string.IsNullOrEmpty(VideoId) ? $"https://img.youtube.com/vi/{VideoId}/0.jpg" : string.Empty;
}