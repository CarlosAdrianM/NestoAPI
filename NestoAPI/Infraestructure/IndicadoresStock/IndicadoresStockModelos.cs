using System;

namespace NestoAPI.Infraestructure.IndicadoresStock
{
    /// <summary>
    /// NestoAPI#629: un producto de la empresa 1 (la ficha manda también para la empresa 3) que no es ficticio.
    /// </summary>
    public class ProductoIndicadorStock
    {
        public string Producto { get; set; }
        /// <summary>Estado de HOY (no hay histórico de estados): en los meses reconstruidos es el estado actual.</summary>
        public int Estado { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        /// <summary>Productos.PrecioMedio de hoy: valora el stock de cualquier fecha, como la consulta de Carlos de la épica.</summary>
        public decimal PrecioMedio { get; set; }
        /// <summary>NestoAPI#628: Familias.TipoExclusiva de su familia, de HOY (como el estado); vacío si no tiene familia.</summary>
        public string TipoExclusiva { get; set; }
    }

    /// <summary>
    /// NestoAPI#628 (Manuel, 16/10/26): «tenemos que tener también los datos de las exclusivas: Nacionales (NAC), Madrid (MAD),
    /// ninguna (NIG), extinguir (EXT)… la rotación tendría que ser por cada uno de estos grupos». Las agrupaciones salen del
    /// tipo de exclusiva de la familia; los propios (familias PRP, fórmula P del motor) van aparte.
    /// <para>En IndicadoresStock son filas más del mes con <c>Almacen = "G:" + código</c> (G:PRP, G:NAC, G:MAD, G:NIG, G:EXT y
    /// G:SIN): caben en el varchar(5) de la columna sin tocar la tabla, no chocan con ningún almacén (Almacenes.Número es
    /// char(3)) y quien lee hoy <c>Almacen = 'TOTAL'</c> o un almacén no ve nada distinto.</para>
    /// </summary>
    public static class AgrupacionesStock
    {
        public const string PREFIJO = "G:";
        public const string PROPIOS = "PRP";
        public const string NACIONALES = "NAC";
        public const string MADRID = "MAD";
        public const string NINGUNA = "NIG";
        public const string EXTINGUIR = "EXT";
        /// <summary>Sin familia, o con un tipo de exclusiva que no es de los de arriba.</summary>
        public const string SIN_TIPO = "SIN";

        /// <summary>En el orden en que se enseñan.</summary>
        public static readonly string[] TODAS = { PROPIOS, NACIONALES, MADRID, NINGUNA, EXTINGUIR, SIN_TIPO };

        /// <summary>El código de agrupación de un tipo de exclusiva (PRP, NAC, MAD, NIG, EXT; cualquier otro, o vacío, SIN).</summary>
        public static string Codigo(string tipoExclusiva)
        {
            string tipo = tipoExclusiva?.Trim().ToUpperInvariant();
            return tipo != SIN_TIPO && Array.IndexOf(TODAS, tipo) >= 0 ? tipo : SIN_TIPO;
        }

        /// <summary>«G:NAC»: el valor de la columna Almacen de IndicadoresStock para esa agrupación.</summary>
        public static string Almacen(string codigo) => PREFIJO + codigo;

        public static bool EsAgrupacion(string almacen) => almacen != null && almacen.Trim().StartsWith(PREFIJO, StringComparison.OrdinalIgnoreCase);

        public static string Nombre(string codigo)
        {
            switch (codigo)
            {
                case PROPIOS: return "Propios (fabricación propia)";
                case NACIONALES: return "Exclusivas nacionales (NAC)";
                case MADRID: return "Exclusivas de Madrid (MAD)";
                case NINGUNA: return "Sin exclusiva (NIG)";
                case EXTINGUIR: return "A extinguir (EXT)";
                default: return "Sin tipo de exclusiva";
            }
        }
    }

    /// <summary>
    /// NestoAPI#629: los movimientos de ExtractoProducto de un producto en un almacén (empresas 1 y 3, sin EXT), sumados por tramo
    /// mensual. Tramo 11 = el mes que se calcula; 10 = el anterior; … 0 = once meses antes; −1 = todo lo anterior a esos 12 meses.
    /// </summary>
    public class MovimientoStockTramo
    {
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public int Tramo { get; set; }
        public int Cantidad { get; set; }
        /// <summary>El primer movimiento del tramo (para saber desde cuándo existe el producto).</summary>
        public DateTime PrimeraFecha { get; set; }
        /// <summary>Unidades del diario _ALBCOMP (albaranes de compra; las devoluciones a proveedor restan).</summary>
        public int UnidadesCompradas { get; set; }
        /// <summary>Importe de los albaranes de compra (lo que costaron, no el precio medio); las devoluciones restan.</summary>
        public decimal ImporteCompras { get; set; }
        /// <summary>Unidades de los diarios de bajas a pérdidas (Merma… y Robo); negativas cuando salen.</summary>
        public int UnidadesBajas { get; set; }
    }

    /// <summary>
    /// NestoAPI#629: ventas (LinPedidoVta, estado >= 2, TipoLinea 1, sin MTP ni MMP) de un producto en un almacén, por tramo
    /// mensual de la fecha de albarán. Tramo 23 = el mes que se calcula; … 0 = 23 meses antes.
    /// </summary>
    public class VentaTramo
    {
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public int Tramo { get; set; }
        /// <summary>Lo que sale menos lo que vuelve (las devoluciones de clientes restan).</summary>
        public int UnidadesNetas { get; set; }
        /// <summary>Solo lo que sale (líneas con cantidad positiva): para «sin venta».</summary>
        public int UnidadesSalida { get; set; }
    }

    /// <summary>NestoAPI#629: las líneas servidas (albaranadas) en el mes en un almacén.</summary>
    public class ServicioAlmacen
    {
        public string Almacen { get; set; }
        public int Lineas { get; set; }
        /// <summary>Suma de días entre la fecha de entrega pedida y la de albarán.</summary>
        public long SumaDias { get; set; }
        /// <summary>Líneas que salieron enteras: sin otra línea partida (mismo pedido, producto y fecha de entrega) en otro albarán o pendiente.</summary>
        public int LineasCompletas { get; set; }
    }

    /// <summary>
    /// NestoAPI#629: un movimiento del mes de un producto que se ha comprado en el mes (empresas 1 y 3, sin EXT), para saber la
    /// cobertura que tenía justo antes de cada compra.
    /// </summary>
    public class MovimientoProductoComprado
    {
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public string Empresa { get; set; }
        public int Orden { get; set; }
        public int Cantidad { get; set; }
        /// <summary>Entrada por albarán de compra (_ALBCOMP con cantidad positiva).</summary>
        public bool EsCompra { get; set; }
    }

    /// <summary>
    /// NestoAPI#629: una fila de la tabla IndicadoresStock (un mes y un almacén, o TOTAL). Los indicadores que no se han podido
    /// calcular (falló su consulta) quedan a null.
    /// </summary>
    public class FilaIndicadoresStock
    {
        public const string TOTAL = "TOTAL";

        /// <summary>Día 1 del mes.</summary>
        public DateTime Mes { get; set; }
        /// <summary>Código del almacén, «TOTAL» (todos menos EXT) o una agrupación por tipo de exclusiva («G:NAC»…, <see cref="AgrupacionesStock"/>).</summary>
        public string Almacen { get; set; }

        public decimal? ValorStock { get; set; }
        public int? UnidadesStock { get; set; }
        public decimal? ValorEstado4 { get; set; }
        public int? ProductosEstado4 { get; set; }
        public decimal? ValorEstado1 { get; set; }
        public int? ProductosEstado1 { get; set; }
        public decimal? ValorSinVenta6Meses { get; set; }
        public int? ProductosSinVenta6Meses { get; set; }
        public decimal? ValorSinVenta12Meses { get; set; }
        public int? ProductosSinVenta12Meses { get; set; }
        public decimal? ValorSinVenta24Meses { get; set; }
        public int? ProductosSinVenta24Meses { get; set; }
        public decimal? CosteVentas12Meses { get; set; }
        public decimal? ValorStockMedio12Meses { get; set; }
        public decimal? Rotacion { get; set; }
        public decimal? CoberturaMediaDias { get; set; }
        public int? LineasServidas { get; set; }
        public decimal? DiasServirMedia { get; set; }
        public decimal? NivelServicio { get; set; }
        public decimal? ComprasValor { get; set; }
        public int? ComprasUnidades { get; set; }
        public decimal? BajasPerdidasValor { get; set; }
        public int? BajasPerdidasUnidades { get; set; }
        public int? AltasReferencias { get; set; }
        public int? BajasReferencias { get; set; }
        public int? UnidadesCompradasMes { get; set; }
        public int? UnidadesCompradasCoberturaAlta { get; set; }
        /// <summary>Solo en la fila TOTAL: valor de stock en Algete.</summary>
        public decimal? ValorStockAlgete { get; set; }
        /// <summary>Solo en la fila TOTAL: valor de stock en las tiendas (Reina y Alcobendas).</summary>
        public decimal? ValorStockTiendas { get; set; }
        public DateTime? FechaCalculo { get; set; }
    }

    /// <summary>
    /// NestoAPI#628 (cuadre del Excel de propuestas): el stock a fin de mes de lo que el motor de propuestas deja fuera a propósito
    /// (<see cref="Stocks.Tendencias.DemandaStock"/>): el grupo MTP (materias primas) y el subgrupo MMP (muestras), por familia.
    /// Mismo criterio de valoración que la fila TOTAL de IndicadoresStock: empresas 1 y 3, sin EXT, sin ficticios, × PrecioMedio.
    /// </summary>
    public class StockFueraDeLaDemanda
    {
        public const string MATERIAS_PRIMAS = "MTP";
        public const string MUESTRAS = "MMP";

        /// <summary><see cref="MATERIAS_PRIMAS"/> o <see cref="MUESTRAS"/> (un producto MTP y MMP a la vez cuenta como MTP).</summary>
        public string Partida { get; set; }
        public string Familia { get; set; }
        public string NombreFamilia { get; set; }
        /// <summary>Productos con stock distinto de 0 a fin de mes.</summary>
        public int Productos { get; set; }
        public int Unidades { get; set; }
        public decimal Valor { get; set; }
    }

    /// <summary>
    /// NestoAPI#629: lo que devuelve GET api/IndicadoresStock: la fila tal cual más el % de unidades compradas en los 12 meses que
    /// acaban en ese mes que ya tenían más de 6 meses de cobertura al comprarlas (sale de las 12 filas del mismo almacén; null si
    /// falta alguno de esos meses).
    /// </summary>
    public class IndicadoresStockDTO : FilaIndicadoresStock
    {
        /// <summary>Mes en formato AAAA-MM.</summary>
        public string Periodo { get; set; }
        public decimal? PorcentajeComprasCoberturaAlta12Meses { get; set; }
    }
}
