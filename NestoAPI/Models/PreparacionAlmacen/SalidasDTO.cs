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
        /// <summary>Lo que se ha cambiado al terminar (o se cambiaría, en un ensayo), una frase por cambio.</summary>
        public List<string> Cambios { get; set; } = new List<string>();

        /// <summary>
        /// Ensayo (?ensayo=true, solo Admin o Dirección): el mismo código, dentro de una transacción que se deshace
        /// siempre. No se ha guardado nada.
        /// </summary>
        public bool Ensayo { get; set; }
        /// <summary>Las filas implicadas antes de terminar (solo en un ensayo).</summary>
        public List<FilaEnsayoDTO> FilasAntes { get; set; }
        /// <summary>Las mismas filas justo antes de deshacer (solo en un ensayo que ha ido bien).</summary>
        public List<FilaEnsayoDTO> FilasDespues { get; set; }
        /// <summary>El error real (procedimiento, trigger o restricción) si el ensayo ha fallado.</summary>
        public string ErrorEnsayo { get; set; }
    }

    /// <summary>Una fila de una tabla en un ensayo, con las columnas que importan en texto.</summary>
    public class FilaEnsayoDTO
    {
        public string Tabla { get; set; }
        public string Clave { get; set; }
        public string Datos { get; set; }
    }

    /// <summary>Ariadna#6: de qué mozo son las lecturas que se anulan.</summary>
    public class AnularLecturasDTO
    {
        public string Usuario { get; set; }
    }

    public class ResultadoAnularLecturasDTO
    {
        /// <summary>Las lecturas en negativo añadidas (0 si ese mozo no tenía nada subido).</summary>
        public int Filas { get; set; }
        public string Mensaje { get; set; }
    }
}
