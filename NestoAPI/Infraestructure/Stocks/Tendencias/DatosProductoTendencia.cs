using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#630: lo que el motor necesita de un producto, ya leído de la BD. Las series son de <see cref="MESES_SERIE"/>
    /// meses y van alineadas: el índice <c>MESES_SERIE - 1</c> es el mes de referencia (el último cerrado) y el 0, 59 meses antes.
    /// </summary>
    public class DatosProductoTendencia
    {
        public const int MESES_SERIE = 60;
        public const int ULTIMO = MESES_SERIE - 1;

        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        /// <summary>NestoAPI#655: Familias.TipoExclusiva de la familia del producto (empresa 1), sin el relleno del char(3).</summary>
        public string TipoExclusiva { get; set; }
        /// <summary>NestoAPI#655: familia de fabricación propia (Unión Láser, Eva Visnú…): va por la fórmula P.</summary>
        public bool EsFabricacionPropia => string.Equals(TipoExclusiva?.Trim(), TIPO_EXCLUSIVA_PROPIA, StringComparison.OrdinalIgnoreCase);
        public const string TIPO_EXCLUSIVA_PROPIA = "PRP";
        /// <summary>Productos.Estado: 0 disponible, 1 sobre pedido, 4 anular («a extinguir»), −1 nulo…</summary>
        public short? Estado { get; set; }
        public decimal PrecioMedio { get; set; }
        public string Proveedor { get; set; }
        /// <summary>Proveedores.DíasEnServir del primer proveedor del producto (por Orden); null si no tiene.</summary>
        public int? DiasEnServir { get; set; }

        /// <summary>ControlesStock de Algete (null si no hay fila).</summary>
        public int? StockMaximoAlg { get; set; }
        public int? StockMinimoAlg { get; set; }
        public int MultiplosAlg { get; set; } = 1;

        /// <summary>Ventas netas por mes (definición de <see cref="DemandaStock"/>), negativas recortadas a 0.</summary>
        public int[] Ventas { get; set; } = new int[MESES_SERIE];
        /// <summary>Stock (sin EXT) a fin de cada mes.</summary>
        public int[] StockFinMes { get; set; } = new int[MESES_SERIE];
        public int Stock => StockFinMes[ULTIMO];

        /// <summary>Primera venta de toda la historia (no solo de la serie).</summary>
        public DateTime? FechaPrimeraVenta { get; set; }
        /// <summary>Primera entrada de stock: la edad cuando aún no se ha vendido nada.</summary>
        public DateTime? FechaPrimeraEntrada { get; set; }

        public int StockExt { get; set; }
        public DateTime? UltimaEntradaExt { get; set; }

        /// <summary>Unidades en líneas de venta −1 y 1 (demanda no servida).</summary>
        public int UnidadesPendientes { get; set; }
        /// <summary>Unidades en pedidos de compra abiertos (−1 y 1).</summary>
        public int UnidadesPedidoCompraAbierto { get; set; }

        /// <summary>Solo se rellena para los que pueden ser fase A: cada venta (cliente y fecha) desde la primera.</summary>
        public List<VentaClienteTendencia> VentasPorCliente { get; set; } = new List<VentaClienteTendencia>();
    }

    public class VentaClienteTendencia
    {
        public string Cliente { get; set; }
        public DateTime Fecha { get; set; }
    }
}
