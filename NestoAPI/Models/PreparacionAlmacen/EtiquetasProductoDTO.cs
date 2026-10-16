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
    }

    public class ResultadoEtiquetasProductoDTO
    {
        public string Producto { get; set; }
        /// <summary>Las líneas de texto que lleva encima del código de barras (nombre, subgrupo, tamaño).</summary>
        public List<string> Lineas { get; set; } = new List<string>();
        public int Copias { get; set; }
        /// <summary>0 en un ensayo (vista previa).</summary>
        public int Impresas { get; set; }
        /// <summary>La impresora de etiquetas del usuario (parámetro ImpresoraCodBarras).</summary>
        public string Impresora { get; set; }
        public string Mensaje { get; set; }
    }
}
