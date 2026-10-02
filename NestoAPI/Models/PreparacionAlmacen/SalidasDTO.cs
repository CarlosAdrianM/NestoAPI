using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: lo que contesta POST api/Almacen/Recogidas/{tipo}/{numero}/Terminar. La salida de mercancía es el
    /// espejo de la recepción: el mismo núcleo para cualquier tipo (PICK, REPO) y lo propio de cada uno en su estrategia.
    /// </summary>
    public class ResultadoTerminarSalidaDTO
    {
        public string Tipo { get; set; }
        public int Numero { get; set; }
        /// <summary>No queda nada por resolver: todo cogido o dado por falta.</summary>
        public bool Terminada { get; set; }
        /// <summary>Terminada y sin ninguna falta ni producto de más.</summary>
        public bool Completa { get; set; }
        public int UnidadesEnFalta { get; set; }
        /// <summary>Qué pasa ahora, para enseñárselo al mozo tal cual.</summary>
        public string Mensaje { get; set; }
        public List<DiferenciaPreparacionDTO> Productos { get; set; } = new List<DiferenciaPreparacionDTO>();
    }
}
