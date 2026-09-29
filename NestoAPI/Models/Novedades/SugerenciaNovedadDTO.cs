using System;

namespace NestoAPI.Models.Novedades
{
    /// <summary>
    /// NestoAPI#526: una característica que ha sugerido un usuario. Es una fila de Novedades sin
    /// versión: sale por delante de la versión actual y se vota y comenta como cualquier novedad.
    /// Cuando se implementa se le pone la versión y pasa a su sitio.
    /// </summary>
    public class SugerenciaNovedadDTO : NovedadConFeedbackDTO
    {
        /// <summary>Lo que escribió el usuario, tal cual. La Descripcion es la versión clara.</summary>
        public string TextoOriginal { get; set; }
        public string SugeridaNombre { get; set; }
        public DateTime? SugeridaFecha { get; set; }
        /// <summary>Pendiente, Aceptada, Implementada o Descartada.</summary>
        public string Estado { get; set; }
        public bool TieneImagen { get; set; }
        /// <summary>NestoAPI#558: «Algo no funciona» (categoría Incidencia), no una idea nueva.</summary>
        public bool EsIncidencia { get; set; }
        /// <summary>
        /// NestoAPI#558: en las incidencias, la versión, la pantalla y los errores de ELMAH del usuario de
        /// la última hora. Solo lo ven Dirección / Informática (a los demás les llega a null).
        /// </summary>
        public string Contexto { get; set; }
    }

    /// <summary>
    /// NestoAPI#558: el cuerpo de POST api/Novedades/Sugerencias. El mismo que el de los comentarios y,
    /// para «Algo no funciona», EsIncidencia y la pantalla que tenía abierta (Nesto).
    /// </summary>
    public class NuevaSugerenciaNovedadDTO : NuevoComentarioNovedadDTO
    {
        public bool EsIncidencia { get; set; }
        /// <summary>La pantalla que tenía abierta el usuario (opcional; solo se guarda en las incidencias).</summary>
        public string Pantalla { get; set; }
    }

    /// <summary>NestoAPI#558: un error de ELMAH del usuario, para el contexto de la incidencia.</summary>
    public class ErrorElmahResumen
    {
        public DateTime TimeUtc { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Fila de la consulta de sugerencias y del buscador. Clase aparte porque SqlQuery exige una
    /// columna por propiedad (el feedback se rellena después).
    /// </summary>
    public class NovedadConSugerenciaFila
    {
        public int Id { get; set; }
        public string Version { get; set; }
        public DateTime Fecha { get; set; }
        public string Categoria { get; set; }
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public string Ambito { get; set; }
        public string TextoOriginal { get; set; }
        public string SugeridaNombre { get; set; }
        public DateTime? SugeridaFecha { get; set; }
        public string Estado { get; set; }
        public bool TieneImagen { get; set; }
        public string Contexto { get; set; }

        public SugerenciaNovedadDTO ADto() => new SugerenciaNovedadDTO
        {
            Id = Id,
            Version = Version,
            Fecha = Fecha,
            Categoria = Categoria,
            Titulo = Titulo,
            Descripcion = Descripcion,
            Ambito = Ambito,
            TextoOriginal = TextoOriginal,
            SugeridaNombre = SugeridaNombre,
            SugeridaFecha = SugeridaFecha,
            Estado = Estado,
            TieneImagen = TieneImagen,
            EsIncidencia = string.Equals(Categoria?.Trim(), Infraestructure.Novedades.ReglasSugerenciasNovedades.CATEGORIA_INCIDENCIA, StringComparison.OrdinalIgnoreCase),
            Contexto = Contexto
        };
    }

    public class SugerenciaNovedadAGrabar
    {
        public string Ambito { get; set; }
        public string Titulo { get; set; }
        public string TextoOriginal { get; set; }
        public byte[] Imagen { get; set; }
        public string ImagenTipo { get; set; }
        public string SugeridaPor { get; set; }
        public string SugeridaNombre { get; set; }
        /// <summary>NestoAPI#558: Nuevo (sugerencia) o Incidencia («Algo no funciona»). Sin ella, Nuevo.</summary>
        public string Categoria { get; set; }
        /// <summary>NestoAPI#558: el contexto automático de la incidencia (null en las sugerencias).</summary>
        public string Contexto { get; set; }
    }

    /// <summary>
    /// NestoAPI#526: lo que cambiamos nosotros de una sugerencia (Dirección / Informática). Lo que
    /// venga a null no se toca. Con Version, pasa a Implementada y a su sitio en el changelog.
    /// </summary>
    public class ActualizarSugerenciaNovedadDTO
    {
        public string Titulo { get; set; }
        /// <summary>El texto claro, redactado por nosotros. El original del usuario no se toca nunca.</summary>
        public string Descripcion { get; set; }
        public string Estado { get; set; }
        public string Version { get; set; }
        /// <summary>
        /// Nuevo / Mejorado / Corregido, al implementarla. Por defecto se queda en Nuevo; una incidencia
        /// (NestoAPI#558) pasa a Corregido. Incidencia sirve para reclasificar una que aún no tiene versión.
        /// </summary>
        public string Categoria { get; set; }
    }
}
