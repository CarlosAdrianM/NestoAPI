using System.Collections.Generic;

namespace NestoAPI.Models.Novedades
{
    /// <summary>
    /// NestoAPI#616: lo que sale de cada adjunto dentro de las novedades y sugerencias (sin el
    /// contenido): lo justo para pintar el chip. El fichero se baja con GET api/Novedades/Adjuntos/{Id}.
    /// </summary>
    public class AdjuntoNovedadResumenDTO
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        /// <summary>MIME: application/pdf, image/png, image/jpeg, image/gif o image/webp.</summary>
        public string Tipo { get; set; }
        /// <summary>En bytes.</summary>
        public int Tamano { get; set; }
    }

    /// <summary>NestoAPI#616: GET api/Novedades/{id}/Adjuntos y la respuesta del POST.</summary>
    public class AdjuntoNovedadDTO : AdjuntoNovedadResumenDTO
    {
        public int Orden { get; set; }
    }

    /// <summary>Fila de la consulta de los adjuntos de varias novedades a la vez (sin Contenido).</summary>
    public class AdjuntoNovedadFila
    {
        public int NovedadId { get; set; }
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public int Tamano { get; set; }
        public int Orden { get; set; }

        public AdjuntoNovedadDTO ADto() => new AdjuntoNovedadDTO
        {
            Id = Id,
            Nombre = Nombre,
            Tipo = Tipo,
            Tamano = Tamano,
            Orden = Orden
        };

        public AdjuntoNovedadResumenDTO AResumen() => new AdjuntoNovedadResumenDTO
        {
            Id = Id,
            Nombre = Nombre,
            Tipo = Tipo,
            Tamano = Tamano
        };
    }

    /// <summary>El fichero para la descarga.</summary>
    public class AdjuntoNovedadContenido
    {
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public byte[] Contenido { get; set; }
    }

    /// <summary>Un fichero ya validado, listo para grabar.</summary>
    public class AdjuntoNovedadAGrabar
    {
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public byte[] Contenido { get; set; }
    }

    /// <summary>Resultado de validar los ficheros subidos: o la lista para grabar, o el error.</summary>
    public class ValidacionAdjuntosNovedad
    {
        public List<AdjuntoNovedadAGrabar> Adjuntos { get; set; } = new List<AdjuntoNovedadAGrabar>();
        /// <summary>null si todo está bien.</summary>
        public string Error { get; set; }
        /// <summary>True si el error es de tipo no permitido (415); si no, 400.</summary>
        public bool TipoNoPermitido { get; set; }
    }
}
