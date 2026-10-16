using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#628 (épica de stock obsoleto): la DEFINICIÓN DE DEMANDA y de STOCK que comparten los cortes 1 (#629, indicadores
    /// mensuales) y 2 (#630, tendencias por producto). Está sola y pequeña a propósito: el día que se unifiquen los dos cortes,
    /// este es el único sitio que hay que mirar.
    /// <para>Decisiones de la épica (Carlos, 14/10/26), no se reabren:</para>
    /// <list type="bullet">
    /// <item>Demanda = toda la empresa (Algete, Reina, Alcobendas, Amazon, web): «si lo vendo lo tengo que comprar». Cuenta lo que
    /// sale a clientes con estado &gt;= 2 (albarán o factura). Fuera: presupuestos (−3), notas de entrega (−2), el grupo MTP
    /// (materias primas) y el subgrupo MMP (muestras).</item>
    /// <item>Lo pendiente NO entra en la tendencia; solo como señal aparte de «demanda no servida» en los nuevos. NestoAPI#656:
    /// solo el pendiente (−1) con fecha de entrega vencida; el en curso (1) ya se está sirviendo y lo de entrega futura nadie
    /// ha intentado servirlo todavía.</item>
    /// <item>Stock = todos los almacenes menos EXT (extinguidos), empresas 1 y 3 con producto de la 1, sin ficticios.</item>
    /// </list>
    /// <para>Decidido en #630 (no estaba en la épica): la venta del mes es NETA (las devoluciones, en negativo, restan: lo que vuelve
    /// al almacén no hay que comprarlo), fechada por <c>[Fecha Albarán]</c> (cuándo salió), solo líneas de producto
    /// (<c>TipoLinea = 1</c>) y de las empresas 1 y 3 (la 4 es histórica). Un mes con más devoluciones que ventas cuenta 0.</para>
    /// </summary>
    public static class DemandaStock
    {
        public static readonly string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        public static readonly string EMPRESA_ESPEJO = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
        public const short ESTADO_MINIMO_DEMANDA = 2;
        public const string GRUPO_EXCLUIDO = Constantes.Productos.GRUPO_MATERIAS_PRIMAS;
        public const string SUBGRUPO_EXCLUIDO = Constantes.Productos.SUBGRUPO_MUESTRAS;
        public const string ALMACEN_EXTINGUIDOS = "EXT";

        /// <summary>Empresas de demanda y de stock, como lista SQL.</summary>
        public const string SQL_EMPRESAS = "('1', '3')";

        /// <summary>Filtro de los productos que entran (alias <c>p</c> sobre Productos).</summary>
        public const string SQL_FILTRO_PRODUCTO =
            "p.Empresa = '1' AND p.Ficticio = 0 AND ISNULL(p.Grupo, '') <> 'MTP' AND ISNULL(p.SubGrupo, '') <> 'MMP'";

        /// <summary>Filtro de la demanda (alias <c>l</c> sobre LinPedidoVta). La fecha de la venta es <c>l.[Fecha Albarán]</c>.</summary>
        public const string SQL_FILTRO_DEMANDA =
            "l.Empresa IN ('1', '3') AND l.Estado >= 2 AND l.TipoLinea = 1 AND l.[Fecha Albarán] IS NOT NULL";

        /// <summary>
        /// Filtro de la demanda no servida (alias <c>l</c> sobre LinPedidoVta). NestoAPI#656 (corte 1): solo pendiente (−1) con
        /// <c>[Fecha Entrega]</c> antes de <c>@hasta</c> (el día siguiente al fin del mes de referencia). Lo usa solo
        /// <see cref="RepositorioTendenciasSql.SQL_PENDIENTES"/>.
        /// </summary>
        public const string SQL_FILTRO_PENDIENTE =
            "l.Empresa IN ('1', '3') AND l.Estado = -1 AND l.TipoLinea = 1 AND l.[Fecha Entrega] < @hasta";

        /// <summary>
        /// NestoAPI#656 (corte 2): lo servido con retraso (alias <c>l</c> sobre LinPedidoVta): albaranado (estado &gt;= 2), de producto,
        /// salidas (cantidad &gt; 0), empresas 1 y 3, en <c>[@desde12, @hasta)</c> por fecha de albarán, y más de <c>@dias</c> días
        /// naturales después de la fecha de entrega (mismo origen que «días en servir» de #629).
        /// </summary>
        public const string SQL_FILTRO_SERVIDO_CON_RETRASO =
            "l.Empresa IN ('1', '3') AND l.Estado >= 2 AND l.TipoLinea = 1 AND l.Cantidad > 0 AND l.[Fecha Albarán] >= @desde12 " +
            "AND l.[Fecha Albarán] < @hasta AND l.[Fecha Entrega] IS NOT NULL AND DATEDIFF(day, l.[Fecha Entrega], l.[Fecha Albarán]) > @dias";

        /// <summary>Filtro del stock (alias <c>e</c> sobre ExtractoProducto), sin EXT.</summary>
        public const string SQL_FILTRO_STOCK =
            "e.Empresa IN ('1', '3') AND e.Almacén <> 'EXT'";
    }
}
