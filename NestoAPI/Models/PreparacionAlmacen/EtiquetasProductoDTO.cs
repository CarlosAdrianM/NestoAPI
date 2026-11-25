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
        /// <summary>
        /// Opcional (Carlos 25/11/26): la impresora (\\servidor\cola, de GET api/Almacen/EtiquetasProducto/Impresoras). Sin ella ni
        /// <see cref="Almacen"/>, la del usuario, como siempre (Nesto y Ariadna no la mandan).
        /// </summary>
        public string Impresora { get; set; }
        /// <summary>Opcional: ALG, REI o ALC. Sin <see cref="Impresora"/>, sale en la de ese almacén; otro, la del usuario.</summary>
        public string Almacen { get; set; }
    }

    public class ResultadoEtiquetasProductoDTO
    {
        public string Producto { get; set; }
        /// <summary>Las líneas de texto que lleva encima del código de barras (el nombre y «Ref. 17404 · 100 m»).</summary>
        public List<string> Lineas { get; set; } = new List<string>();
        public int Copias { get; set; }
        /// <summary>0 en un ensayo (vista previa).</summary>
        public int Impresas { get; set; }
        /// <summary>Donde sale (o saldría): la pedida, la del almacén o la del usuario (parámetro ImpresoraCodBarras).</summary>
        public string Impresora { get; set; }
        /// <summary>De dónde sale la impresora: «Pedida», «Almacen» o «Usuario».</summary>
        public string OrigenImpresora { get; set; }
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

    /// <summary>Una impresora de etiquetas para elegir (GET api/Almacen/EtiquetasProducto/Impresoras).</summary>
    public class ImpresoraEtiquetasDTO
    {
        /// <summary>ALG, REI o ALC; null en la del usuario cuando no es la de ningún almacén.</summary>
        public string Almacen { get; set; }
        /// <summary>«Algete», «Reina», «Alcobendas» o «La suya».</summary>
        public string Nombre { get; set; }
        public string Impresora { get; set; }
        /// <summary>Es la ImpresoraCodBarras de quien pregunta.</summary>
        public bool EsLaDelUsuario { get; set; }
    }
}
