using System;

namespace NestoAPI.Models.Novedades
{
    /// <summary>
    /// Issue Nesto#372: entrada del changelog de cara al usuario final. Se asocia a la versión
    /// ClickOnce de Nesto publicada (los cambios de NestoAPI se adjuntan a la versión de Nesto
    /// más próxima). El texto es siempre en lenguaje de usuario, nunca técnico.
    /// </summary>
    public class NovedadDTO
    {
        public int Id { get; set; }
        public string Version { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>Nuevo / Mejorado / Corregido</summary>
        public string Categoria { get; set; }
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Nesto / NestoAPI (informativo; el usuario ve un único changelog)</summary>
        public string Ambito { get; set; }
    }

    /// <summary>
    /// NestoAPI#520: la novedad tal cual sale por GET api/Novedades, con el feedback de los usuarios.
    /// Clase aparte porque <c>SqlQuery&lt;NovedadDTO&gt;</c> exige una columna por propiedad. Los campos
    /// son opcionales: si las tablas de feedback aún no existen o fallan, salen a null y los clientes
    /// viejos ni se enteran.
    /// </summary>
    public class NovedadConFeedbackDTO : NovedadDTO
    {
        public int? VotosPositivos { get; set; }
        public int? VotosNegativos { get; set; }
        /// <summary>1, -1 o null (no ha votado, o petición sin usuario).</summary>
        public short? MiVoto { get; set; }
        public int? NumeroComentarios { get; set; }

        public static NovedadConFeedbackDTO Desde(NovedadDTO n) => new NovedadConFeedbackDTO
        {
            Id = n.Id,
            Version = n.Version,
            Fecha = n.Fecha,
            Categoria = n.Categoria,
            Titulo = n.Titulo,
            Descripcion = n.Descripcion,
            Ambito = n.Ambito
        };
    }
}
