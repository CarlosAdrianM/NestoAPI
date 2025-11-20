using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// Mensaje de sincronización específico para Productos
    /// Contiene solo los campos relevantes para la entidad Producto
    /// </summary>
    public class ProductoSyncMessage : SyncMessageBase
    {
        /// <summary>
        /// ID del producto (Número)
        /// </summary>
        public string Producto { get; set; }

        /// <summary>
        /// Nombre del producto
        /// </summary>
        public string Nombre { get; set; }

        /// <summary>
        /// Precio profesional (PVP)
        /// </summary>
        public decimal? PrecioProfesional { get; set; }

        /// <summary>
        /// Precio público final
        /// </summary>
        public decimal? PrecioPublicoFinal { get; set; }

        /// <summary>
        /// Código de barras del producto
        /// </summary>
        public string CodigoBarras { get; set; }

        /// <summary>
        /// Rotura de stock de proveedor
        /// </summary>
        public bool? RoturaStockProveedor { get; set; }

        /// <summary>
        /// Estado del producto
        /// </summary>
        public short? Estado { get; set; }

        /// <summary>
        /// Tamaño del producto (volumen en ml)
        /// Decimal para aceptar valores desde Odoo como 500.0
        /// </summary>
        public decimal? Tamanno { get; set; }

        /// <summary>
        /// Unidad de medida
        /// </summary>
        public string UnidadMedida { get; set; }

        /// <summary>
        /// Familia del producto
        /// </summary>
        public string Familia { get; set; }

        /// <summary>
        /// Grupo del producto
        /// </summary>
        public string Grupo { get; set; }

        /// <summary>
        /// Subgrupo del producto
        /// </summary>
        public string Subgrupo { get; set; }

        /// <summary>
        /// URL de la foto del producto
        /// </summary>
        public string UrlFoto { get; set; }

        /// <summary>
        /// URL de enlace del producto
        /// </summary>
        public string UrlEnlace { get; set; }

        /// <summary>
        /// Clasificación de más vendidos
        /// </summary>
        public int? ClasificacionMasVendidos { get; set; }

        /// <summary>
        /// Lista de productos del kit
        /// </summary>
        public List<string> ProductosKit { get; set; }

        /// <summary>
        /// Información de stocks por almacén
        /// </summary>
        public List<ProductoDTO.StockProducto> Stocks { get; set; }
    }
}
