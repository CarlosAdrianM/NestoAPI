using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Lo que la sombra de precios medios (Issue #547, corte b) lee de la BD para UN producto, tal cual viene de
    /// las consultas (sin interpretar). Lo convierte en historial para la calculadora
    /// <see cref="ConstructorHistorialPrecioMedio"/> y en estado actual para el comparador
    /// <see cref="EstadoActualPrecioMedio"/>. Objetos planos: los tests los rellenan a mano.
    /// </summary>
    public sealed class DatosProductoPrecioMedio
    {
        public string Empresa { get; set; }
        public string EmpresaEspejo { get; set; }
        public string Producto { get; set; }

        /// <summary><c>Productos</c> de la empresa E. Nulo si el producto no tiene ficha en esa empresa.</summary>
        public FichaProductoPrecioMedio Ficha { get; set; }

        /// <summary><c>LinPedidoCmp</c> del producto, <c>TipoLínea = 1</c>, E + espejo, cualquier estado.</summary>
        public List<LineaCompraBDPrecioMedio> Compras { get; set; } = new List<LineaCompraBDPrecioMedio>();

        /// <summary><c>ExtractoProducto</c> del producto (E + espejo) agregado por fecha exacta.</summary>
        public List<MovimientoStockPrecioMedio> Movimientos { get; set; } = new List<MovimientoStockPrecioMedio>();

        /// <summary>
        /// <c>LinPedidoVta</c> del producto (E + espejo, estados -1, 1 y ≥ 2). Nulo si este producto no entra en el
        /// muestreo de ventas de la pasada (leerlas todas es justo lo que no queremos).
        /// </summary>
        public List<LineaVentaPrecioMedio> Ventas { get; set; }
    }

    /// <summary>Una fila de <c>Productos</c> (empresa E).</summary>
    public sealed class FichaProductoPrecioMedio
    {
        public decimal? PrecioMedio { get; set; }
        public bool? Ficticio { get; set; }
    }

    /// <summary>
    /// Una fila de <c>LinPedidoCmp</c> tal cual sale de la consulta (nombres ASCII para <c>SqlDataReader</c>).
    /// </summary>
    public sealed class LineaCompraBDPrecioMedio
    {
        public string Empresa { get; set; }

        /// <summary><c>TRY_CAST(NºFactura AS int)</c>: nulo si la línea no está facturada.</summary>
        public int? NumeroFactura { get; set; }

        public DateTime? FechaAlbaran { get; set; }
        public int? NumeroAlbaran { get; set; }
        public int NumeroOrden { get; set; }
        public int? Cantidad { get; set; }
        public decimal? BaseImponible { get; set; }
        public decimal? Coste { get; set; }
        public short? Estado { get; set; }

        /// <summary>
        /// <c>CabFacturaCmp.[Fecha Modificación]</c> de su factura: cuándo se creó (o se tocó por última vez) la
        /// factura. Si es posterior a la última pasada del SP, el SP todavía no ha visto esa factura.
        /// </summary>
        public DateTime? FechaModificacionFactura { get; set; }
    }

    /// <summary>Apuntes de <c>ExtractoProducto</c> de una misma fecha (con hora), sumados.</summary>
    public sealed class MovimientoStockPrecioMedio
    {
        public DateTime Fecha { get; set; }

        /// <summary><c>sum(Cantidad)</c> de los apuntes de esa fecha exacta.</summary>
        public int Cantidad { get; set; }

        /// <summary>Hay algún apunte de montaje de kit (<c>Diario='_MontarKit'</c>, <c>Texto like 'Montaje %'</c>, <c>Cantidad&gt;0</c>).</summary>
        public bool EsMontaje { get; set; }

        /// <summary>Hay alguna recepción de compra (<c>NºProveedor</c> no nulo y <c>Cantidad&gt;0</c>).</summary>
        public bool EsRecepcion { get; set; }
    }

    /// <summary>Una línea de <c>LinPedidoVta</c> del muestreo de ventas.</summary>
    public sealed class LineaVentaPrecioMedio
    {
        public string Empresa { get; set; }
        public int Numero { get; set; }
        public int NumeroOrden { get; set; }
        public short Estado { get; set; }
        public DateTime? FechaAlbaran { get; set; }
        public decimal? Coste { get; set; }

        /// <summary><c>[Fecha Modificación]</c>: si es posterior a la pasada del SP, el SP no la ha visto así.</summary>
        public DateTime? FechaModificacion { get; set; }
    }
}
