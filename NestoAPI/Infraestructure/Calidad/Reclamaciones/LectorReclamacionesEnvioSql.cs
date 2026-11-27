using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Globalization;

namespace NestoAPI.Infraestructure.Calidad.Reclamaciones
{
    /// <summary>
    /// NestoAPI#914 (Carlos 27/11/26): LA LECTURA de «error de envío reclamado», la misma para el corte 2 (Almacén) y el pedido perfecto. Es un
    /// trozo de lote que se pega al de cada lector: parte de su tabla temporal <c>#s0 (Empresa, Pedido, Linea)</c> con las líneas que le
    /// interesan y SOLO LEE (READ UNCOMMITTED + NOLOCK, lo pone el lote). Todo entra por clave: las líneas por LinPedidoVta (Empresa, Número,
    /// Nº Orden); lo posterior del mismo producto por Empresa + Nº Cliente + Producto (como RegistroOfertasMostradas y los recordatorios de
    /// reposición); las rectificativas por factura + línea; lo de sus rectificativas por Nº Factura; y lo sin coste del cliente alrededor de
    /// la rectificativa por CabPedidoVta Empresa + Nº Cliente (solo para las líneas rectificadas, que son pocas). Nunca recorre LinPedidoVta
    /// por fechas. Necesita el parámetro <c>@diasReclamacion</c> (int).
    /// </summary>
    public static class LectorReclamacionesEnvioSql
    {
        /// <summary>Columnas de una línea posterior (alias y = LinPedidoVta, c = CabPedidoVta). {0}: EnRectificativa; {1}: la factura rectificativa.</summary>
        internal const string COLUMNAS_POSTERIOR = @"
       CAST({0} AS bit) AS EnRectificativa, {1} AS FacturaRectificativa, RTRIM(y.[Nº Cliente]) AS Cliente, y.[Número] AS Pedido, y.[Nº Orden] AS Linea,
       CAST(CAST(ISNULL(c.Fecha, y.[Fecha Entrega]) AS date) AS datetime) AS Fecha, RTRIM(y.Producto) AS Producto, CAST(y.Cantidad AS int) AS Cantidad,
       CAST(y.[Base Imponible] AS decimal(18, 2)) AS BaseImponible, RTRIM(ISNULL(y.Grupo, '')) AS Grupo, RTRIM(ISNULL(y.SubGrupo, '')) AS SubGrupo,
       RTRIM(ISNULL(y.Familia, '')) AS Familia, y.PrecioTarifa, y.[NºOferta] AS Oferta,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Ganavisiones g WITH (NOLOCK)
                              WHERE g.Empresa = y.Empresa AND g.ProductoId = y.Producto AND g.FechaDesde < DATEADD(day, 1, ISNULL(c.Fecha, y.[Fecha Entrega]))
                                AND (g.FechaHasta IS NULL OR g.FechaHasta >= CAST(ISNULL(c.Fecha, y.[Fecha Entrega]) AS date))) THEN 1 ELSE 0 END AS bit) AS EsGanavisiones,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.RegaloImportePedido ri WITH (NOLOCK)
                              WHERE ri.Empresa = y.Empresa AND ri.Producto = y.Producto
                                AND (ri.FechaInicio IS NULL OR ri.FechaInicio < DATEADD(day, 1, ISNULL(c.Fecha, y.[Fecha Entrega])))
                                AND (ri.FechaFin IS NULL OR ri.FechaFin >= CAST(ISNULL(c.Fecha, y.[Fecha Entrega]) AS date))) THEN 1 ELSE 0 END AS bit) AS EsRegaloPorImporte,
       CAST(ISNULL(c.NotaEntrega, 0) AS bit) AS NotaEntrega, c.PedidoOrigen";

        /// <summary>
        /// El trozo de lote (resultados, en este orden): (1) las líneas servidas de #s0 (de producto, con albarán); (2) sus rectificaciones;
        /// (3) lo sin coste del MISMO producto al mismo cliente desde el albarán hasta @diasReclamacion días después; (4) de las rectificadas,
        /// las líneas de sus rectificativas (también las que abonan, para ver si es una refacturación) y lo sin coste de cualquier producto a
        /// ese cliente <see cref="ReglaErrorEnvioReclamado.DIAS_ALREDEDOR_RECTIFICATIVA"/> días antes o después de la rectificativa.
        /// </summary>
        public static readonly string SQL_RECLAMACIONES = @"
IF OBJECT_ID('tempdb..#s') IS NOT NULL DROP TABLE #s;
IF OBJECT_ID('tempdb..#rx') IS NOT NULL DROP TABLE #rx;
SELECT l.Empresa, l.[Número] AS Pedido, l.[Nº Orden] AS Linea, l.Producto, l.[Nº Cliente] AS Cliente,
       CAST(CAST(l.[Fecha Albarán] AS date) AS datetime) AS FechaAlbaran, NULLIF(RTRIM(l.[Nº Factura]), '') AS Factura,
       CAST(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS int) AS Cantidad, CAST(l.[Base Imponible] AS decimal(18, 2)) AS BaseImponible,
       l.Grupo, l.SubGrupo, l.Familia, l.PrecioTarifa, l.[NºOferta] AS Oferta
INTO #s
FROM (SELECT DISTINCT Empresa, Pedido, Linea FROM #s0) s0
INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = s0.Empresa AND l.[Número] = s0.Pedido AND l.[Nº Orden] = s0.Linea
WHERE l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado >= 2 AND l.[Fecha Albarán] IS NOT NULL;
CREATE CLUSTERED INDEX IX_s ON #s (Empresa, Cliente, Producto);
SELECT s.Pedido, s.Linea, RTRIM(s.Producto) AS Producto, RTRIM(s.Cliente) AS Cliente, s.FechaAlbaran, RTRIM(s.Factura) AS Factura, s.Cantidad,
       s.BaseImponible, RTRIM(ISNULL(s.Grupo, '')) AS Grupo, RTRIM(ISNULL(s.SubGrupo, '')) AS SubGrupo, RTRIM(ISNULL(s.Familia, '')) AS Familia,
       s.PrecioTarifa, s.Oferta
FROM #s s;
SELECT s.Empresa, s.Pedido, s.Linea, s.Cliente, x.NumeroFactura, CAST(CAST(cf.Fecha AS date) AS datetime) AS Fecha,
       CAST(ABS(x.CantidadRectificada) AS decimal(18, 2)) AS Cantidad
INTO #rx
FROM #s s
INNER JOIN LinFacturaVtaRectificacion x WITH (NOLOCK) ON x.Empresa = s.Empresa AND x.FacturaOriginalNumero = s.Factura AND x.FacturaOriginalLinea = s.Linea
LEFT JOIN CabFacturaVta cf WITH (NOLOCK) ON cf.Empresa = x.Empresa AND cf.[Número] = x.NumeroFactura
WHERE s.Factura IS NOT NULL AND x.NumeroFactura <> s.Factura;
SELECT r.Pedido, r.Linea, RTRIM(r.NumeroFactura) AS FacturaRectificativa, r.Fecha, r.Cantidad
FROM #rx r;
SELECT" + string.Format(CultureInfo.InvariantCulture, COLUMNAS_POSTERIOR, "0", "CAST(NULL AS varchar(20))") + @"
FROM (SELECT Empresa, Cliente, Producto, MIN(FechaAlbaran) AS Desde, MAX(FechaAlbaran) AS Hasta FROM #s GROUP BY Empresa, Cliente, Producto) k
INNER JOIN LinPedidoVta y WITH (NOLOCK) ON y.Empresa = k.Empresa AND y.[Nº Cliente] = k.Cliente AND y.Producto = k.Producto
INNER JOIN CabPedidoVta c WITH (NOLOCK) ON c.Empresa = y.Empresa AND c.[Número] = y.[Número]
WHERE y.TipoLinea = 1 AND y.Cantidad > 0 AND y.[Base Imponible] = 0 AND y.Estado <> -3
  AND c.Fecha >= k.Desde AND c.Fecha < DATEADD(day, @diasReclamacion + 1, k.Hasta);
SELECT" + string.Format(CultureInfo.InvariantCulture, COLUMNAS_POSTERIOR, "1", "RTRIM(f.NumeroFactura)") + @"
FROM (SELECT DISTINCT Empresa, NumeroFactura FROM #rx) f
INNER JOIN LinPedidoVta y WITH (NOLOCK) ON y.Empresa = f.Empresa AND y.[Nº Factura] = f.NumeroFactura
LEFT JOIN CabPedidoVta c WITH (NOLOCK) ON c.Empresa = y.Empresa AND c.[Número] = y.[Número]
WHERE y.TipoLinea = 1 AND y.Cantidad <> 0
UNION ALL
SELECT" + string.Format(CultureInfo.InvariantCulture, COLUMNAS_POSTERIOR, "0", "CAST(NULL AS varchar(20))") + @"
FROM (SELECT Empresa, Cliente, MIN(Fecha) AS Desde, MAX(Fecha) AS Hasta FROM #rx WHERE Fecha IS NOT NULL GROUP BY Empresa, Cliente) k
INNER JOIN CabPedidoVta c WITH (NOLOCK) ON c.Empresa = k.Empresa AND c.[Nº Cliente] = k.Cliente
       AND c.Fecha >= DATEADD(day, -" + ReglaErrorEnvioReclamado.DIAS_ALREDEDOR_RECTIFICATIVA + @", k.Desde)
       AND c.Fecha < DATEADD(day, " + (ReglaErrorEnvioReclamado.DIAS_ALREDEDOR_RECTIFICATIVA + 1) + @", k.Hasta)
INNER JOIN LinPedidoVta y WITH (NOLOCK) ON y.Empresa = c.Empresa AND y.[Número] = c.[Número]
WHERE y.TipoLinea = 1 AND y.Cantidad > 0 AND y.[Base Imponible] = 0 AND y.Estado <> -3;
DROP TABLE #rx;
DROP TABLE #s;
";

        /// <summary>El parámetro «(defecto)» de los días, sin crear fila (la fila de «(defecto)» o nada).</summary>
        internal const string SQL_DIAS = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT TOP 1 RTRIM(p.Valor) FROM ParametrosUsuario p WITH (NOLOCK)
WHERE p.Empresa = @empresa AND p.Usuario = '(defecto)' AND p.Clave = @clave;";

        /// <summary>Los días de reclamación (<see cref="ReglaErrorEnvioReclamado.PARAMETRO_DIAS"/>); sin fila o si falla, 45. No crea la fila.</summary>
        public static int LeerDias(SqlConnection conexion, string empresa)
        {
            try
            {
                using (var comando = new SqlCommand(SQL_DIAS, conexion) { CommandTimeout = 30 })
                {
                    comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@clave", SqlDbType.VarChar, 50).Value = ReglaErrorEnvioReclamado.PARAMETRO_DIAS;
                    object valor = comando.ExecuteScalar();
                    return ReglaErrorEnvioReclamado.Dias(valor == null || valor == DBNull.Value ? null : Convert.ToString(valor, CultureInfo.InvariantCulture));
                }
            }
            catch (SqlException)
            {
                return ReglaErrorEnvioReclamado.DIAS_POR_DEFECTO;
            }
        }

        /// <summary>
        /// Los cuatro resultados de <see cref="SQL_RECLAMACIONES"/>, con el lector YA en el primero. Si el lote no los trae (un lector simulado
        /// de antes), no lee nada. Público para los tests con resultados simulados.
        /// </summary>
        public static void LeerResultados(DbDataReader r, LecturaReclamacionesEnvio destino)
        {
            // 1. Las servidas
            while (r.Read())
            {
                destino.Servidas.Add(new LineaServidaReclamacion
                {
                    Pedido = Entero(r, 0),
                    Linea = Entero(r, 1),
                    Producto = Texto(r, 2),
                    Cliente = Texto(r, 3),
                    FechaAlbaran = Fecha(r, 4),
                    Factura = Texto(r, 5),
                    Cantidad = Entero(r, 6),
                    BaseImponible = Decimal(r, 7),
                    Grupo = Texto(r, 8),
                    SubGrupo = Texto(r, 9),
                    Familia = Texto(r, 10),
                    PrecioTarifa = r.IsDBNull(11) ? (decimal?)null : Decimal(r, 11),
                    Oferta = r.IsDBNull(12) ? (int?)null : Entero(r, 12)
                });
            }
            // 2. Rectificaciones
            if (!r.NextResult())
            {
                return;
            }
            while (r.Read())
            {
                destino.Rectificaciones.Add(new RectificacionLineaReclamacion
                {
                    Pedido = Entero(r, 0),
                    Linea = Entero(r, 1),
                    FacturaRectificativa = Texto(r, 2),
                    Fecha = Fecha(r, 3),
                    Cantidad = Decimal(r, 4)
                });
            }
            // 3. El mismo producto, sin coste, después
            if (!r.NextResult())
            {
                return;
            }
            while (r.Read())
            {
                destino.MismoProducto.Add(Posterior(r));
            }
            // 4. Otros productos de las rectificadas
            if (!r.NextResult())
            {
                return;
            }
            while (r.Read())
            {
                destino.OtrosProductos.Add(Posterior(r));
            }
        }

        /// <summary>Junta lo leído en una tanda con lo de las anteriores.</summary>
        public static void Juntar(LecturaReclamacionesEnvio destino, LecturaReclamacionesEnvio tanda)
        {
            if (destino == null || tanda == null)
            {
                return;
            }
            destino.Servidas.AddRange(tanda.Servidas);
            destino.Rectificaciones.AddRange(tanda.Rectificaciones);
            destino.MismoProducto.AddRange(tanda.MismoProducto);
            destino.OtrosProductos.AddRange(tanda.OtrosProductos);
        }

        private static LineaPosteriorReclamacion Posterior(DbDataReader r) => new LineaPosteriorReclamacion
        {
            EnRectificativa = Bool(r, 0),
            FacturaRectificativa = Texto(r, 1),
            Cliente = Texto(r, 2),
            Pedido = Entero(r, 3),
            Linea = Entero(r, 4),
            Fecha = Fecha(r, 5) ?? DateTime.MinValue,
            Producto = Texto(r, 6),
            Cantidad = Entero(r, 7),
            BaseImponible = Decimal(r, 8),
            Grupo = Texto(r, 9),
            SubGrupo = Texto(r, 10),
            Familia = Texto(r, 11),
            PrecioTarifa = r.IsDBNull(12) ? (decimal?)null : Decimal(r, 12),
            Oferta = r.IsDBNull(13) ? (int?)null : Entero(r, 13),
            EsGanavisiones = Bool(r, 14),
            EsRegaloPorImporte = Bool(r, 15),
            NotaEntrega = Bool(r, 16),
            PedidoOrigen = r.IsDBNull(17) ? (int?)null : Entero(r, 17)
        };

        private static int Entero(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

        private static decimal Decimal(DbDataReader r, int i) => r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);

        private static bool Bool(DbDataReader r, int i) => !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i), CultureInfo.InvariantCulture);

        private static DateTime? Fecha(DbDataReader r, int i) => r.IsDBNull(i) ? (DateTime?)null : Convert.ToDateTime(r.GetValue(i), CultureInfo.InvariantCulture);

        private static string Texto(DbDataReader r, int i)
        {
            if (r.IsDBNull(i))
            {
                return null;
            }
            string texto = Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrEmpty(texto) ? null : texto;
        }
    }
}
