using System.Collections.Generic;

namespace NestoAPI.Models.Productos
{
    // NestoAPI#738 (sugerencia 793, Manuel): contrato del cambio de estado de los productos, uno a uno (ficha) y masivo.
    // Lo usa Nesto (ventana masiva y desplegable en la ficha); lo podrán usar Odoo, Ariadna y NestoApp.

    /// <summary>Un estado de la tabla EstadosProducto de la empresa.</summary>
    public class EstadoProductoDTO
    {
        public short Estado { get; set; }
        public string Descripcion { get; set; }
        /// <summary>«Extinguir»: no lo queremos trabajar.</summary>
        public bool AExtinguir { get; set; }
        /// <summary>«Anular»: lo ha dejado el proveedor.</summary>
        public bool AAnular { get; set; }
    }

    /// <summary>Qué productos entran en el cambio. Todos los criterios que vengan se cumplen a la vez (Y).</summary>
    public class FiltroEstadoProductos
    {
        public string Proveedor { get; set; }
        public string Familia { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public List<string> Productos { get; set; }
    }

    /// <summary>Body de POST api/Productos/Estado/VistaPrevia.</summary>
    public class VistaPreviaEstadoProductosPeticion
    {
        public string Empresa { get; set; }
        public FiltroEstadoProductos Filtro { get; set; }
        public short? EstadoNuevo { get; set; }
    }

    public class PedidoPendienteEstadoDTO
    {
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public int Cantidad { get; set; }
    }

    public class ControlesStockAlmacenesDTO
    {
        public bool ALG { get; set; }
        public bool REI { get; set; }
        public bool ALC { get; set; }
    }

    /// <summary>Un control de stock que se creará (o se subirá desde 0) al volver a Disponible.</summary>
    public class ControlStockSugeridoDTO
    {
        public string Almacen { get; set; }
        public int StockMinimo { get; set; }
        public int StockMaximo { get; set; }
        /// <summary>Cómo sale (en ALG, la frase del Excel de propuestas de stock; en las tiendas, la media de 24 meses).</summary>
        public string Explicacion { get; set; }
    }

    public class ProductoEstadoVistaPreviaDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public short? EstadoActual { get; set; }
        public short EstadoNuevo { get; set; }
        public int StockALG { get; set; }
        public int StockREI { get; set; }
        public int StockALC { get; set; }
        public int PendienteRecibirProveedor { get; set; }
        public List<PedidoPendienteEstadoDTO> PedidosPendientes { get; set; } = new List<PedidoPendienteEstadoDTO>();
        public ControlesStockAlmacenesDTO TieneControlesStock { get; set; } = new ControlesStockAlmacenesDTO();
        /// <summary>Al volver a Disponible: los controles que se crearán con los máximos sugeridos. Vacío en los demás casos.</summary>
        public List<ControlStockSugeridoDTO> ControlesSugeridos { get; set; } = new List<ControlStockSugeridoDTO>();
        /// <summary>Avisos de este producto (pedidos pendientes, stock que queda, controles…).</summary>
        public List<string> Avisos { get; set; } = new List<string>();
        /// <summary>
        /// Si no se va a cambiar aunque se aplique, por qué (ya está en ese estado, no existe). Null si se cambiará; los que
        /// tienen controles de stock se cambian si se confirma quitarlos (<see cref="VistaPreviaEstadoProductosDTO.PreguntaControlesStock"/>).
        /// </summary>
        public string MotivoNoSeCambia { get; set; }
    }

    /// <summary>Respuesta de POST api/Productos/Estado/VistaPrevia.</summary>
    public class VistaPreviaEstadoProductosDTO
    {
        public List<ProductoEstadoVistaPreviaDTO> Productos { get; set; } = new List<ProductoEstadoVistaPreviaDTO>();
        /// <summary>Avisos del lote entero, para enseñarlos antes de «Aplicar».</summary>
        public List<string> Avisos { get; set; } = new List<string>();
        /// <summary>Cuántos productos salen de Disponible con controles de stock (habría que quitarlos).</summary>
        public int ControlesStockAfectados { get; set; }
        public int ProductosConPedidosPendientes { get; set; }
        /// <summary>La pregunta que hace la pantalla UNA vez por lote si <see cref="ControlesStockAfectados"/> &gt; 0. Null si no hay que preguntar.</summary>
        public string PreguntaControlesStock { get; set; }
        /// <summary>Si el usuario puede aplicar el cambio (Compras o Dirección): la pantalla deshabilita «Aplicar» si no.</summary>
        public bool PuedeCambiar { get; set; }
    }

    /// <summary>Body de POST api/Productos/Estado.</summary>
    public class CambioEstadoProductosPeticion
    {
        public string Empresa { get; set; }
        public List<string> Productos { get; set; }
        public short? EstadoNuevo { get; set; }
        /// <summary>True si el usuario ha dicho «quítelos y cambie el estado» a la pregunta de los controles de stock.</summary>
        public bool QuitarControlesStock { get; set; }
        /// <summary>Solo si no viene en el token (el token manda).</summary>
        public string Usuario { get; set; }
    }

    public class ProductoOmitidoDTO
    {
        public string Producto { get; set; }
        public string Motivo { get; set; }
    }

    /// <summary>Respuesta de POST api/Productos/Estado.</summary>
    public class ResultadoCambioEstadoProductosDTO
    {
        public int Cambiados { get; set; }
        public List<ProductoOmitidoDTO> Omitidos { get; set; } = new List<ProductoOmitidoDTO>();
        /// <summary>Controles de stock puestos a 0 (máximo y mínimo) en los que salen de Disponible.</summary>
        public int ControlesStockQuitados { get; set; }
        /// <summary>Controles de stock creados (o subidos desde 0) con los máximos sugeridos en los que vuelven a Disponible.</summary>
        public int ControlesStockCreados { get; set; }
        public List<string> Avisos { get; set; } = new List<string>();
    }
}
