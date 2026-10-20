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

        /// <summary>
        /// Demanda no servida: unidades en líneas de venta pendientes (−1) con fecha de entrega vencida (antes del fin del mes de
        /// referencia). NestoAPI#656: ya no cuenta el estado 1 (en curso) ni lo de entrega futura.
        /// </summary>
        public int UnidadesPendientes { get; set; }
        /// <summary>
        /// NestoAPI#656 (corte 2): unidades albaranadas en los 12 meses que acaban en el de referencia que salieron más de
        /// <see cref="ParametrosTendencias.DiasRetraso"/> días después de la fecha de entrega.
        /// </summary>
        public int UnidadesServidasConRetraso { get; set; }
        /// <summary>Unidades en pedidos de compra abiertos (−1 y 1).</summary>
        public int UnidadesPedidoCompraAbierto { get; set; }

        /// <summary>Solo se rellena para los que pueden ser fase A: cada venta (cliente y fecha) desde la primera.</summary>
        public List<VentaClienteTendencia> VentasPorCliente { get; set; } = new List<VentaClienteTendencia>();

        /// <summary>
        /// NestoAPI#678: hasta dónde llegan los datos por días (exclusive, a las 00:00): hoy si el mes de referencia es el último
        /// cerrado, o el día siguiente al fin del mes de referencia si se calcula un mes pasado (como si fuera el día 1). Null = el
        /// corte del mes de referencia (sin nada del mes en curso).
        /// </summary>
        public DateTime? FechaDatos { get; set; }

        /// <summary>
        /// NestoAPI#678: venta neta por día (misma definición que <see cref="Ventas"/>) de los que pueden ser fase A, desde 12 meses
        /// antes del corte hasta <see cref="FechaDatos"/>; incluye los días del mes en curso. Null = no se ha leído (el motor
        /// calcula la velocidad de los nuevos solo con los meses cerrados, como antes).
        /// </summary>
        public List<VentaDiariaTendencia> VentasDiarias { get; set; }
    }

    public class VentaClienteTendencia
    {
        public string Cliente { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#678: venta neta de un día (puede ser negativa si ese día hubo más devoluciones que ventas).</summary>
    public class VentaDiariaTendencia
    {
        public DateTime Fecha { get; set; }
        public int Cantidad { get; set; }
    }
}
