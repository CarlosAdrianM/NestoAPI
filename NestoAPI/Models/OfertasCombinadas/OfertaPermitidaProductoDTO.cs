using System;

namespace NestoAPI.Models.OfertasCombinadas
{
    /// <summary>
    /// Una oferta permitida de un PRODUCTO concreto: el clásico "6+2".
    ///
    /// Hasta ahora solo se podían meter desde Nesto viejo, y allí no se les puede poner fecha
    /// —la tabla no tenía columnas de fecha—, así que la única forma de apagar una oferta era
    /// borrar la fila y acordarse de hacerlo.
    ///
    /// Deja fuera a propósito las ofertas de un CLIENTE concreto: esas son otra cosa y su sitio
    /// natural es la ficha de ese cliente, no una pantalla de mantenimiento general (decisión de
    /// Carlos, 31/08/2026).
    /// </summary>
    public class OfertaPermitidaProductoDTO
    {
        public int NOrden { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }

        /// <summary>Solo lectura, para no tener que mirar la referencia en otra ventana.</summary>
        public string ProductoNombre { get; set; }

        /// <summary>Las unidades que se cobran. En un 6+2, el 6.</summary>
        public short CantidadConPrecio { get; set; }

        /// <summary>Las que van de regalo. En un 6+2, el 2.</summary>
        public short CantidadRegalo { get; set; }

        /// <summary>
        /// Prohíbe expresamente la oferta en vez de permitirla. Existe desde siempre en la tabla
        /// pero no se veía en ninguna pantalla.
        /// </summary>
        public bool Denegar { get; set; }

        public string FiltroProducto { get; set; }

        /// <summary>Nulas = sin límite por ese lado. Inclusivas: hasta el 30/09 vale todo el día 30.</summary>
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        /// <summary>Calculado por el servidor: si la oferta está en vigor hoy.</summary>
        public bool Vigente { get; set; }

        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    public class OfertaPermitidaProductoCreateDTO
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public short CantidadConPrecio { get; set; }
        public short CantidadRegalo { get; set; }
        public bool Denegar { get; set; }
        public string FiltroProducto { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
    }
}
