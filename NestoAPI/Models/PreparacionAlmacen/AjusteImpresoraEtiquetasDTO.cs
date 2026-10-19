using System;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#672: cuánto se corre todo lo que se imprime en una impresora de etiquetas (EPL, 203 ppp: 8 puntos = 1 mm).
    /// X positivo lleva la etiqueta a la derecha; Y positivo, hacia abajo. Sin ajuste guardado, 0 y 0 (lo de siempre).
    /// </summary>
    public class AjusteImpresoraEtiquetasDTO
    {
        /// <summary>La ruta de la impresora, como en el parámetro ImpresoraCodBarras (\\RDS2016\etiquetasreina).</summary>
        public string Impresora { get; set; }
        public int DesplazamientoX { get; set; }
        public int DesplazamientoY { get; set; }
        /// <summary>Quién lo cambió por última vez (null si no hay ajuste guardado).</summary>
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    public class ResultadoEtiquetaPruebaDTO
    {
        public string Impresora { get; set; }
        /// <summary>El desplazamiento con el que ha salido la etiqueta de prueba (el guardado o el que se ha pedido).</summary>
        public int DesplazamientoX { get; set; }
        public int DesplazamientoY { get; set; }
        public string Mensaje { get; set; }
    }
}
