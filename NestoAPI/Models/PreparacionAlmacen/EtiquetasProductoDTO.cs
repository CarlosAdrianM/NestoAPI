using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#654: de qué producto imprimir la etiqueta de código de barras (POST api/Almacen/EtiquetasProducto/Imprimir) y
    /// cuántas.
    /// </summary>
    public class EtiquetasProductoDTO
    {
        /// <summary>El número del producto (17404).</summary>
        public string Producto { get; set; }
        /// <summary>Cuántas etiquetas iguales. Sin él (o 0), una.</summary>
        public int Copias { get; set; }
        /// <summary>
        /// NestoAPI#791: se acepta y se ignora (Nesto y Ariadna lo mandan a veces). La etiqueta es siempre la 3×2 con el EAN-13
        /// de la unidad del producto; si no tiene, se le da el código interno (29…) y se imprime ese.
        /// </summary>
        public string Formato { get; set; }
    }

    public class ResultadoEtiquetasProductoDTO
    {
        public string Producto { get; set; }
        /// <summary>Las líneas de texto que lleva encima del código de barras (el nombre y «Ref. 17404 · 100 m»).</summary>
        public List<string> Lineas { get; set; } = new List<string>();
        public int Copias { get; set; }
        /// <summary>0 en un ensayo (vista previa).</summary>
        public int Impresas { get; set; }
        /// <summary>La impresora de etiquetas del usuario (parámetro ImpresoraCodBarras).</summary>
        public string Impresora { get; set; }
        public string Mensaje { get; set; }
        /// <summary>
        /// NestoAPI#762/#791: el EAN-13 impreso (o el que se imprimiría en el ensayo). Null solo en el ensayo de un producto al que
        /// le tocaría un interno del contador (299…), que no se sabe hasta darlo.
        /// </summary>
        public string CodigoBarras { get; set; }
        /// <summary>NestoAPI#762: es el código interno (29…), no el del fabricante.</summary>
        public bool CodigoInterno { get; set; }
        /// <summary>
        /// NestoAPI#762/#791: el código interno se acaba de crear al imprimir (nunca en el ensayo): las listas abiertas en las apps
        /// no lo tienen todavía.
        /// </summary>
        public bool CodigoAsignado { get; set; }
    }
}
