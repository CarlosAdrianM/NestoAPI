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
        /// Nombre personalizado para la tienda (puede diferir del Nombre de la ficha).
        /// null = sin personalización: el consumidor NO debe tocar el nombre que tenga.
        /// </summary>
        public string NombrePersonalizado { get; set; }

        /// <summary>
        /// Descripción completa del producto para la tienda. null = no tocar.
        /// </summary>
        public string Descripcion { get; set; }

        /// <summary>
        /// Descripción breve del producto para la tienda. null = no tocar.
        /// </summary>
        public string DescripcionBreve { get; set; }

        /// <summary>
        /// NestoAPI#415: tipo de IVA de la ficha (G21/R10/SR...). Los precios del mensaje viajan
        /// CON IVA; el consumidor debe mapear este tipo a su grupo de impuestos (tax rules group
        /// en PrestaShop) en la creación Y en los updates, en vez de usar uno fijo.
        /// </summary>
        public string TipoIva { get; set; }

        /// <summary>
        /// NestoAPI#415: porcentaje de IVA resuelto (21/10/4/0), por si el consumidor prefiere el
        /// número o quiere validar su mapeo de <see cref="TipoIva"/>.
        /// </summary>
        public decimal? PorcentajeIva { get; set; }

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
        /// Lista de productos del kit (solo los números, sin cantidades).
        /// Se mantiene por compatibilidad; la composición completa va en
        /// <see cref="ComponentesKit"/>.
        /// </summary>
        public List<string> ProductosKit { get; set; }

        /// <summary>
        /// NestoAPI#412: composición del kit — cada componente con las unidades que lleva por
        /// kit. null o vacío = el producto no es un kit. Pensado para que Odoo construya la BoM
        /// del kit; PrestaShop puede ignorarlo (para la web basta con
        /// <c>Stocks[].CantidadMontable</c>).
        /// </summary>
        public List<ProductoKit> ComponentesKit { get; set; }

        /// <summary>
        /// Información de stocks por almacén
        /// </summary>
        public List<ProductoDTO.StockProducto> Stocks { get; set; }
    }
}
