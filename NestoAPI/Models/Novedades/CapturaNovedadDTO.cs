
using System.Collections.Generic;

namespace NestoAPI.Models.Novedades
{
    /// <summary>
    /// NestoAPI#754 (sugerencia 900): una de las capturas de una sugerencia / «Algo no funciona» o de un comentario,
    /// sin el contenido. La imagen se pide con GET api/Novedades/Capturas/{Id} (con los mismos permisos que la novedad).
    /// </summary>
    public class CapturaNovedadDTO
    {
        public int Id { get; set; }
        /// <summary>image/png o image/jpeg.</summary>
        public string Tipo { get; set; }
        /// <summary>Tamaño en bytes.</summary>
        public int Bytes { get; set; }
        /// <summary>1, 2, 3… en el orden en que se pegaron.</summary>
        public int Orden { get; set; }
    }

    /// <summary>Fila de dbo.NovedadesCapturas (Scripts/NovedadesCapturasABlob.sql).</summary>
    public class CapturaNovedadFila
    {
        public int Id { get; set; }
        /// <summary>La novedad / sugerencia a la que pertenece (también si es de un comentario): para los permisos.</summary>
        public int NovedadId { get; set; }
        /// <summary>null = captura de la propia sugerencia; con valor = de ese comentario.</summary>
        public int? ComentarioId { get; set; }
        /// <summary>Ruta del blob dentro del contenedor privado de capturas.</summary>
        public string Ruta { get; set; }
        public string Tipo { get; set; }
        public int Bytes { get; set; }
        public int Orden { get; set; }

        public CapturaNovedadDTO ADto() => new CapturaNovedadDTO
        {
            Id = Id,
            Tipo = Tipo,
            Bytes = Bytes,
            Orden = Orden
        };
    }

    /// <summary>Una captura ya validada (firma PNG/JPEG y tamaño), lista para guardar.</summary>
    public class CapturaValidada
    {
        public byte[] Contenido { get; set; }
        public string Tipo { get; set; }
    }

    /// <summary>Una captura ya subida al blob, para registrarla en la tabla.</summary>
    public class CapturaSubida
    {
        public string Ruta { get; set; }
        public string Tipo { get; set; }
        public int Bytes { get; set; }
    }

    /// <summary>NestoAPI#754: una imagen de las de antes (varbinary) pendiente de pasar al blob.</summary>
    public class ImagenPendienteMigrar
    {
        public const string ORIGEN_NOVEDAD = "Novedad";
        public const string ORIGEN_COMENTARIO = "Comentario";

        /// <summary>Novedad o Comentario.</summary>
        public string Origen { get; set; }
        /// <summary>Id de la novedad o del comentario.</summary>
        public int Id { get; set; }
        /// <summary>La novedad (si es un comentario, la suya).</summary>
        public int NovedadId { get; set; }
        public string Usuario { get; set; }
        public byte[] Imagen { get; set; }
        public string ImagenTipo { get; set; }
    }

    /// <summary>NestoAPI#754: cuántas imágenes quedan en varbinary.</summary>
    public class ResumenImagenesPendientes
    {
        public int Novedades { get; set; }
        public int Comentarios { get; set; }
        public long Bytes { get; set; }
    }

    /// <summary>NestoAPI#754: respuesta de POST api/Novedades/Capturas/Migrar.</summary>
    public class MigracionCapturasDTO
    {
        /// <summary>false = solo mirar (no se sube ni se vacía nada).</summary>
        public bool Aplicar { get; set; }
        /// <summary>Existe dbo.NovedadesCapturas (Scripts/NovedadesCapturasABlob.sql).</summary>
        public bool TablaDisponible { get; set; }
        /// <summary>Hay cadena de conexión del almacenamiento (Almacen:FotosBultosConexion).</summary>
        public bool AlmacenConfigurado { get; set; }
        /// <summary>Existe el contenedor privado de capturas. null = no se ha podido comprobar.</summary>
        public bool? ContenedorExiste { get; set; }
        public string Contenedor { get; set; }
        /// <summary>Lo que quedaba en varbinary ANTES de esta pasada.</summary>
        public ResumenImagenesPendientes PendientesAntes { get; set; }
        public List<CapturaMigradaDTO> Migradas { get; set; } = new List<CapturaMigradaDTO>();
        public List<string> Errores { get; set; } = new List<string>();
        /// <summary>Lo que queda en varbinary DESPUÉS de esta pasada (null en modo mirar).</summary>
        public ResumenImagenesPendientes PendientesDespues { get; set; }
        public string Mensaje { get; set; }
    }

    public class CapturaMigradaDTO
    {
        public string Origen { get; set; }
        public int Id { get; set; }
        public string Ruta { get; set; }
        public int Bytes { get; set; }
    }
}
