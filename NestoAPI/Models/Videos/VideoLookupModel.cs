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

    /// <summary>
    /// NestoAPI#454 (solo en Videos/Buscar): el momento, en segundos, del producto del vídeo que
    /// casa con lo que se buscó. Null si el vídeo salió por el título o la transcripción y no por
    /// un producto: entonces el módulo se queda con <see cref="UrlVideo"/>. Si casan varios, el
    /// más temprano.
    /// </summary>
    public int? TiempoAparicion { get; set; }

    /// <summary>NestoAPI#454: el producto que ha provocado la coincidencia, si lo hay.</summary>
    public string ProductoCoincidente { get; set; }

    /// <summary>NestoAPI#454: el enlace al momento del producto; null si no hay momento.</summary>
    public string UrlVideoEnMomento => TiempoAparicion.HasValue && !string.IsNullOrEmpty(VideoId)
        ? $"https://www.youtube.com/watch?v={VideoId}&t={TiempoAparicion.Value}s"
        : null;
    public string UrlImagen => !string.IsNullOrEmpty(VideoId) ? $"https://img.youtube.com/vi/{VideoId}/0.jpg" : string.Empty;
}