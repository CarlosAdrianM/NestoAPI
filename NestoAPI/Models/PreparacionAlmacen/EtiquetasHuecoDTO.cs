using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// Qué etiquetas de hueco imprimir (POST api/Almacen/EtiquetasHueco/Imprimir): o unos huecos sueltos, o un rango de un
    /// pasillo. Si llegan huecos, el rango no se mira.
    /// </summary>
    public class EtiquetasHuecoDTO
    {
        /// <summary>Cada hueco como lo lee el lector (002004001) o como se enseña (002/004/001).</summary>
        public List<string> Huecos { get; set; }
        public string Pasillo { get; set; }
        public string FilaDesde { get; set; }
        /// <summary>Sin él, solo la fila «desde».</summary>
        public string FilaHasta { get; set; }
        public string ColumnaDesde { get; set; }
        /// <summary>Sin él, solo la columna «desde».</summary>
        public string ColumnaHasta { get; set; }
        /// <summary>Solo los huecos que tienen algo ubicado ahora (Ubicaciones estado 0) en el almacén.</summary>
        public bool SoloEnUso { get; set; }
    }

    public class ResultadoEtiquetasHuecoDTO
    {
        /// <summary>0 en un ensayo (vista previa) o si no había nada que imprimir.</summary>
        public int Impresas { get; set; }
        /// <summary>La impresora de etiquetas del usuario (parámetro ImpresoraCodBarras).</summary>
        public string Impresora { get; set; }
        /// <summary>Los huecos, de 9 cifras, en el orden en que salen.</summary>
        public List<string> Huecos { get; set; } = new List<string>();
        public string Mensaje { get; set; }
    }
}
