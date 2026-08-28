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
        /// NestoAPI#413: oferta de tarifa para el grupo Profesionales, en porcentaje 0-100.
        /// null = sin oferta (retirar el specific_price porcentual si lo hubiera). Los precios
        /// del mensaje siguen siendo PLENOS: la tienda pinta tachado + %.
        /// </summary>
        public decimal? DescuentoPorcentajeProfesional { get; set; }

        /// <summary>
        /// NestoAPI#413: oferta de tarifa para el público, en porcentaje 0-100. null = sin
        /// oferta. Puede diferir del profesional (o existir solo uno de los dos).
        /// </summary>
        public decimal? DescuentoPorcentajePublico { get; set; }

        /// <summary>
        /// NestoAPI#421 / prestashop-nestosync#19: el producto NO se vende al público (se ve en
        /// la tienda, pero sin precio ni compra para quien no sea profesional).
        ///
        /// Contrato con el consumidor:
        ///   true  → restringir
        ///   false → producto normal; si estaba marcado, DESMARCARLO
        ///   null / clave ausente → NO tocar la marca
        ///
        /// El null es una salvaguarda, no un caso normal: aquí siempre viaja con valor explícito.
        /// La asimetría es deliberada — si un fallo mandara null, un producto restringido sigue
        /// restringido (molesto pero inocuo); al revés abriría al público lo que no debe venderse.
        /// </summary>
        public bool? ExclusivoProfesional { get; set; }

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

        /// <summary>
        /// NestoAPI#414: categorías comerciales SECUNDARIAS del producto, en orden. La principal
        /// sigue siendo Grupo/Subgrupo de la ficha y NO debe tocarse por esta lista.
        /// Semántica: null/ausente = no tocar; lista vacía = el producto no tiene secundarias
        /// (retirar las que sobren en el consumidor).
        /// </summary>
        public List<CategoriaSecundariaDTO> CategoriasSecundarias { get; set; }
    }
}
