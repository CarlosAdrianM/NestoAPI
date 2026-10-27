namespace NestoAPI.Models.Novedades
{
    /// <summary>NestoAPI#711: respuesta de POST api/Novedades/{id}/Video.</summary>
    public class VideoNovedadDTO
    {
        public int NovedadId { get; set; }
        public bool TieneVideo { get; set; }
        /// <summary>Bytes del MP4.</summary>
        public int? VideoTamano { get; set; }
    }

    /// <summary>NestoAPI#711: fila de las novedades que tienen vídeo (sin la ruta).</summary>
    public class VideoNovedadResumenFila
    {
        public int Id { get; set; }
        public int? VideoTamano { get; set; }
    }

    /// <summary>NestoAPI#711: una novedad con lo necesario para su vídeo.</summary>
    public class VideoNovedadFila
    {
        public int Id { get; set; }
        public string Version { get; set; }
        public string Ambito { get; set; }
        public string Titulo { get; set; }
        public bool Publicada { get; set; }
        /// <summary>La ruta del blob. null = sin vídeo (o la columna aún no existe).</summary>
        public string Video { get; set; }
        public int? VideoTamano { get; set; }
    }

    /// <summary>NestoAPI#711: lo que pasa al poner el vídeo (uno por versión y ámbito).</summary>
    public class ResultadoGuardarVideoNovedad
    {
        public bool Guardado { get; set; }
        /// <summary>El blob del vídeo que tenía antes (si lo sustituye), para borrarlo.</summary>
        public string RutaAnterior { get; set; }
        /// <summary>Si no se ha guardado: la otra novedad de la misma versión y ámbito que ya tiene vídeo.</summary>
        public int? OtraNovedadId { get; set; }
        public string OtroTitulo { get; set; }
        public string Version { get; set; }
    }
}
