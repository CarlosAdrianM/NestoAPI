using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// Datos completos para generar un correo post-compra
    /// </summary>
    public class RecomendacionPostCompraDTO
    {
        public string Empresa { get; set; }
        public string ClienteId { get; set; }
        public string ClienteNombre { get; set; }
        public string ClienteEmail { get; set; }
        public int PedidoNumero { get; set; }
        public DateTime FechaPedido { get; set; }

        /// <summary>
        /// Videos relevantes para los productos comprados
        /// </summary>
        public List<VideoRecomendadoDTO> Videos { get; set; } = new List<VideoRecomendadoDTO>();
    }

    /// <summary>
    /// Video recomendado con sus productos (comprados y no comprados)
    /// </summary>
    public class VideoRecomendadoDTO
    {
        public int VideoId { get; set; }
        public string VideoYoutubeId { get; set; }
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public DateTime? FechaPublicacion { get; set; }

        /// <summary>
        /// Todos los productos que aparecen en el video
        /// </summary>
        public List<ProductoEnVideoDTO> Productos { get; set; } = new List<ProductoEnVideoDTO>();

        /// <summary>
        /// Cantidad de productos del video que el cliente YA compró (en este pedido o antes)
        /// </summary>
        public int ProductosComprados => Productos?.FindAll(p => p.YaComprado).Count ?? 0;

        /// <summary>
        /// Cantidad de productos del video que el cliente NO ha comprado nunca
        /// </summary>
        public int ProductosNoComprados => Productos?.FindAll(p => !p.YaComprado).Count ?? 0;
    }

    /// <summary>
    /// Producto que aparece en un video, con info de si el cliente lo tiene
    /// </summary>
    public class ProductoEnVideoDTO
    {
        public string ProductoId { get; set; }
        public string NombreProducto { get; set; }

        /// <summary>
        /// Segundo del video donde aparece este producto
        /// </summary>
        public string TiempoAparicion { get; set; }

        /// <summary>
        /// URL al video en el momento exacto donde aparece el producto
        /// </summary>
        public string EnlaceVideo { get; set; }

        /// <summary>
        /// URL a la ficha del producto en la tienda online
        /// </summary>
        public string EnlaceTienda { get; set; }

        /// <summary>
        /// True si el cliente ya compró este producto (en este pedido o en pedidos anteriores)
        /// </summary>
        public bool YaComprado { get; set; }

        /// <summary>
        /// True si este producto está en el pedido actual (el que dispara el correo)
        /// </summary>
        public bool EnPedidoActual { get; set; }
    }
}
