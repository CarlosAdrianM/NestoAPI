using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// Datos completos para generar un correo post-compra semanal para un cliente.
    /// Agrupa los productos comprados durante la semana que tienen vídeo tutorial.
    /// </summary>
    public class CorreoPostCompraClienteDTO
    {
        public string Empresa { get; set; }
        public string ClienteId { get; set; }
        public string ClienteNombre { get; set; }
        public string ClienteEmail { get; set; }
        /// <summary>NestoAPI#707: el contacto de la persona de contacto cuyo correo se usa (se guarda en cada fila enviada).</summary>
        public string ClienteContacto { get; set; }
        public DateTime SemanaDesde { get; set; }
        public DateTime SemanaHasta { get; set; }

        /// <summary>
        /// Top 3 productos comprados en la semana con mayor BaseImponible que tienen vídeo.
        /// Cada uno enlazado al vídeo más reciente donde aparece.
        /// </summary>
        public List<ProductoCompradoConVideoDTO> ProductosComprados { get; set; } = new List<ProductoCompradoConVideoDTO>();

        /// <summary>
        /// Hasta 4 productos de los mismos vídeos que el cliente nunca ha comprado.
        /// </summary>
        public List<ProductoRecomendadoDTO> ProductosRecomendados { get; set; } = new List<ProductoRecomendadoDTO>();

        /// <summary>
        /// NestoAPI#707: el cliente está en el grupo control (CorreosPostCompra:PorcentajeGrupoControl). Recibe el correo con
        /// lo que ha comprado, pero sin recomendados: los que habría visto van en <see cref="ProductosRecomendadosNoMostrados"/>
        /// y se guardan con GrupoControl = 1 para medir el uplift.
        /// </summary>
        public bool GrupoControl { get; set; }

        /// <summary>NestoAPI#707: los recomendados que se le habrían enseñado a un cliente del grupo control (no salen en el correo).</summary>
        public List<ProductoRecomendadoDTO> ProductosRecomendadosNoMostrados { get; set; } = new List<ProductoRecomendadoDTO>();

        /// <summary>NestoAPI#707: con qué se ordenaron los recomendados (PuntuadorRecomendacionesPostCompra.VERSION_MODELO; luego, el zip de ML.NET).</summary>
        public string VersionModelo { get; set; }
    }

    /// <summary>
    /// Producto que el cliente compró esta semana y tiene un vídeo tutorial asociado.
    /// </summary>
    public class ProductoCompradoConVideoDTO
    {
        public string ProductoId { get; set; }
        public string NombreProducto { get; set; }
        public decimal BaseImponibleTotal { get; set; }
        public string VideoYoutubeId { get; set; }
        public string VideoTitulo { get; set; }

        /// <summary>
        /// URL al punto exacto del vídeo donde aparece este producto.
        /// </summary>
        public string EnlaceVideoProducto { get; set; }

        /// <summary>
        /// URL a la ficha del producto en la tienda online (Prestashop).
        /// </summary>
        public string EnlaceTienda { get; set; }
    }

    /// <summary>
    /// Producto que aparece en los mismos vídeos pero que el cliente nunca ha comprado.
    /// Sección "Otros productos que te pueden interesar".
    /// </summary>
    public class ProductoRecomendadoDTO
    {
        public string ProductoId { get; set; }

        /// <summary>NestoAPI#707: la regla que lo puso en su sitio (pares, afinidad, popularidad u orden_original).</summary>
        public string Regla { get; set; }

        /// <summary>NestoAPI#707: la puntuación (0-1) con la que se ordenó. Null con orden_original.</summary>
        public decimal? Puntuacion { get; set; }
        public string NombreProducto { get; set; }
        public string VideoYoutubeId { get; set; }
        public string VideoTitulo { get; set; }

        /// <summary>
        /// URL al punto exacto del vídeo donde aparece este producto.
        /// </summary>
        public string EnlaceVideoProducto { get; set; }

        /// <summary>
        /// URL a la ficha del producto en la tienda online (Prestashop).
        /// </summary>
        public string EnlaceTienda { get; set; }
    }
}
