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
}
