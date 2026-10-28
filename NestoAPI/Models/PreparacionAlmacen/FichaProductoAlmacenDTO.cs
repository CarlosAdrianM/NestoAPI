using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// Sugerencia 824 (Andre, 28/10/26): la ficha de un producto en Ariadna, desde el botón «Producto» del inicio. Todo lo que el
    /// mozo necesita ver de un producto en una sola respuesta: foto, nombre, referencia del proveedor, familia, los códigos de
    /// barras con sus unidades y, en cada almacén (ALG, REI, ALC), el stock, los huecos y lo pendiente de entregar.
    /// GET api/Almacen/Productos/{codigo}/Ficha. La familia, el subgrupo, la foto y la referencia del proveedor los pone
    /// <c>FichasProductoAlmacen</c>, como en todo lo que ve el mozo.
    /// </summary>
    public class FichaProductoAlmacenDTO : IConFichaProducto
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string Subgrupo { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        public string UrlFoto { get; set; }
        /// <summary>NestoAPI#681: la del proveedor principal. Null si no hay.</summary>
        public string ReferenciaProveedor { get; set; }
        /// <summary>El código principal (el de la ficha), solo si es un GTIN válido (NestoAPI#674).</summary>
        public string CodigoBarras { get; set; }
        /// <summary>Los códigos activos, el principal el primero, con las unidades que vale cada lectura.</summary>
        public List<CodigoBarrasFichaDTO> CodigosBarras { get; set; } = new List<CodigoBarrasFichaDTO>();
        /// <summary>ALG, REI y ALC, en ese orden.</summary>
        public List<AlmacenFichaProductoDTO> Almacenes { get; set; } = new List<AlmacenFichaProductoDTO>();
    }

    /// <summary>Un código de barras del producto (NestoAPI#605).</summary>
    public class CodigoBarrasFichaDTO
    {
        public string Codigo { get; set; }
        /// <summary>Las unidades que vale una lectura (1 = unidad; 12 = caja de 12).</summary>
        public int Cantidad { get; set; }
        public bool Principal { get; set; }
    }

    /// <summary>Lo que hay del producto en un almacén.</summary>
    public class AlmacenFichaProductoDTO
    {
        public string Almacen { get; set; }
        /// <summary>El stock contable (ExtractoProducto).</summary>
        public int Stock { get; set; }
        /// <summary>Unidades en pedidos de venta pendientes o en curso de ese almacén.</summary>
        public int PendienteEntregar { get; set; }
        /// <summary>Unidades en pedidos de compra enviados al proveedor y sin recibir.</summary>
        public int PendienteRecibir { get; set; }
        /// <summary>Lo que llega (o sale, en negativo) por reposiciones en curso.</summary>
        public int PendienteReposicion { get; set; }
        /// <summary>Stock − pendiente de entregar + reposición, nunca negativo (ProductoDTO.StockProducto.CantidadDisponible).</summary>
        public int Disponible { get; set; }
        /// <summary>Unidades recibidas que todavía no tienen hueco.</summary>
        public int PendienteDeUbicar { get; set; }
        /// <summary>Los huecos con existencias, en el orden del recorrido.</summary>
        public List<UbicacionAlmacenDTO> Ubicaciones { get; set; } = new List<UbicacionAlmacenDTO>();
    }

    /// <summary>Un producto que lleva el código leído, cuando lo llevan varios (409): la app pregunta cuál.</summary>
    public class ProductoCandidatoFichaDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
    }

    /// <summary>El cuerpo del 404 de la ficha: el motivo, como el de los demás errores (Message).</summary>
    public class MensajeFichaProductoDTO
    {
        public string Message { get; set; }
    }

    /// <summary>El cuerpo del 409 de la ficha: el código lo llevan varios productos.</summary>
    public class FichaProductoAmbiguaDTO
    {
        public string Message { get; set; }
        public List<ProductoCandidatoFichaDTO> Productos { get; set; } = new List<ProductoCandidatoFichaDTO>();
    }
}
