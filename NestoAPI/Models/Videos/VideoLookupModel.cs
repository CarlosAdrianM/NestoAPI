using System;

public class VideoLookupModel
{
    public int Id { get; set; }
    public string VideoId { get; set; } // Si no es cliente dejamos en blanco
    public string Titulo { get; set; }
    public string Descripcion { get; set; }

    /// <summary>
    /// Lo que la tienda online debe publicar como descripción de la ficha: <see cref="Descripcion"/>
    /// cuando la hay y, cuando está vacía, un arranque sacado del protocolo. Null si no hay ninguna
    /// de las dos, para que la tienda se quede con su texto de reserva. Lo compone
    /// NestoAPI.Infraestructure.Videos.DescripcionFichaVideo (equipo de SEO, 08/09/26).
    /// </summary>
    public string DescripcionParaFicha { get; set; }

    public DateTime FechaPublicacion { get; set; }

    /// <summary>
    /// Cuándo se retiró el vídeo; null mientras está vivo. El listado solo devuelve vídeos de baja
    /// si se piden con incluirBajas=true, y entonces este campo es el que dice cuáles lo están: la
    /// tienda online deja de tener que deducir la baja por ausencia (equipo de SEO, 08/09/26).
    /// </summary>
    public DateTime? FechaBaja { get; set; }
    public bool EsUnProtocolo { get; set; }
    public bool BloqueadoPorComprasRecientes { get; set; }
    public string UrlVideo => !string.IsNullOrEmpty(VideoId) ? $"https://www.youtube.com/watch?v={VideoId}" : string.Empty;
    public string UrlImagen => !string.IsNullOrEmpty(VideoId) ? $"https://img.youtube.com/vi/{VideoId}/0.jpg" : string.Empty;
}