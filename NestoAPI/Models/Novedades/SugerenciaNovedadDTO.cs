using System.Collections.Generic;
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
        /// <summary>
        /// Pendiente, Aceptada, Implementada, Descartada o (recordatorios de respuesta, 16/10/26) Cerrada: sin
        /// respuesta del autor a lo que le preguntamos; la sigue viendo y, si comenta, vuelve a Pendiente.
        /// </summary>
        public string Estado { get; set; }
        public bool TieneImagen { get; set; }
        /// <summary>NestoAPI#558: «Algo no funciona» (categoría Incidencia), no una idea nueva.</summary>
        public bool EsIncidencia { get; set; }
        /// <summary>
        /// NestoAPI#558: en las incidencias, la versión, la pantalla y los errores de ELMAH del usuario de
        /// la última hora. Solo lo ven Dirección / Informática (a los demás les llega a null).
        /// </summary>
        public string Contexto { get; set; }
        /// <summary>
        /// NestoAPI#650: solo la ven quien la creó y Dirección / Informática (a los demás ni les llega). No admite
        /// «Me gusta» y va detrás de las públicas. false en las de siempre (y si la columna aún no existe).
        /// </summary>
        public bool Privada { get; set; }
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
        /// <summary>
        /// NestoAPI#650: que solo la vean quien la crea y Dirección / Informática. Opcional: los clientes que no lo
        /// mandan crean públicas, como siempre.
        /// </summary>
        public bool Privada { get; set; }
    }

    /// <summary>NestoAPI#650: fila de la consulta de las sugerencias privadas (Id y su autor).</summary>
    public class SugerenciaPrivadaFila
    {
        public int Id { get; set; }
        public string SugeridaPor { get; set; }
        public string SugeridaNombre { get; set; }
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
        /// <summary>NestoAPI#575: clave del autor (para que siga viendo sus descartadas). No sale en el DTO.</summary>
        public string SugeridaPor { get; set; }
        /// <summary>Novedades.Fecha_Modificación: la toca cada cambio nuestro (descartarla incluida). No sale en el DTO.</summary>
        public DateTime? FechaModificacion { get; set; }
        /// <summary>El comentario (no borrado) más reciente, o null. No sale en el DTO.</summary>
        public DateTime? FechaUltimoComentario { get; set; }

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
        /// <summary>NestoAPI#650: se graba privada (el controlador ya ha comprobado que la columna existe).</summary>
        public bool Privada { get; set; }
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
        /// <summary>
        /// Sugerencia 551: a quién afecta cuando pasa a ser novedad (Vendedores, Almacén, Tiendas, Administración).
        /// null = no se toca; vacía o con «Todos» = para todos.
        /// </summary>
        public List<string> Perfiles { get; set; }
        /// <summary>NestoAPI#650: pasarla a privada (true) o a pública (false). null = no se toca.</summary>
        public bool? Privada { get; set; }
        /// <summary>
        /// NestoAPI#703: el programa en el que se ha implementado (Nesto, NestoAPI, NestoApp o Ariadna). null = se
        /// deduce de la versión (1.x → escritorio; 2.x → Ariadna, salvo que ya fuera de NestoApp). Lo rellena el
        /// controlador con <see cref="Infraestructure.Novedades.ReglasSugerenciasNovedades.AmbitoAlImplementar"/>.
        /// </summary>
        public string Ambito { get; set; }
    }
}
