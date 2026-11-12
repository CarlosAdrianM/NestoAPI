using NestoAPI.Models;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// NestoAPI#658 (sugerencia 648 de Paloma): el único punto de cálculo del informe de lo facturado en tienda durante la semana,
    /// cursos incluidos, con el cliente, su dirección, su comercial y el importe. Lo usan GET api/Informes/FacturacionTienda (JSON
    /// para Nesto u otros clientes) y su /Excel.
    /// <para>«De la tienda» = líneas facturadas con el <b>almacén</b> de la tienda (REI, ALC), no con su delegación (decisión de Carlos,
    /// 20/10/26): lo que se sirve desde Algete no es de la tienda aunque lleve la delegación ALC (desde agosto, 236 facturas así), y los
    /// cursos (serie CV) con almacén ALG tampoco. Los cursos con almacén de la tienda sí entran. De cada factura solo suman las líneas de
    /// ese almacén. Entran la empresa normal y la espejo; las rectificativas restan.</para>
    /// <para>Sugerencia 648: de cada factura también sale su forma de venta (TIE, DIR, TEL…; si sus líneas de la tienda llevan varias,
    /// separadas por comas). Ver una tienda: Dirección, Administración e Informática, cualquiera; el grupo Tiendas, solo la suya.</para>
    /// <para>Sugerencia 648 (decisión de Carlos, 26/10/26): la dirección es la de <b>entrega del pedido</b> (el Contacto de CabPedidoVta y
    /// su fila de Clientes; el de cobro es ContactoCobro), no la del contacto de la factura. Si una factura junta pedidos con entregas
    /// distintas, manda la del primer pedido (el de número más bajo con líneas de la tienda); sin pedido, la del contacto de la factura.
    /// La vista por cliente agrupa por cliente, contacto de entrega y vendedor: una fila por dirección de entrega.</para>
    /// <para>El comercial de cada factura es el <b>vendedor de la cabecera de la factura</b> (decisión de Carlos, 20/10/26), no el de
    /// la ficha del cliente ni el de peluquería: es a quien se le apunta la venta. Por eso la vista por cliente agrupa por cliente,
    /// contacto y vendedor: si un cliente tiene facturas de dos comerciales en el periodo, sale una fila por comercial.</para>
    /// </summary>
    public static class GestorFacturacionTienda
    {
        public static readonly string[] TIENDAS = { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        /// <summary>Sugerencia 648: el parámetro de usuario con la tienda de cada uno (la que ve si no es Dirección, Administración o Informática).</summary>
        public const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";

        public const string MENSAJE_SOLO_SU_TIENDA = "Cada tienda solo puede ver lo facturado en su almacén.";

        public const string MENSAJE_SIN_TIENDA =
            "Su usuario no tiene tienda (parámetro AlmacénPedidoVta con REI o ALC): pida a la oficina que se la pongan.";

        /// <summary>Tope del periodo, para que nadie lance por error una consulta de años.</summary>
        public const int MAXIMO_DIAS_PERIODO = 366;

        // @p0 empresa (y la de las fichas de clientes y vendedores), @p1 empresa espejo, @p2 desde (incluido), @p3 hasta (excluido),
        // @p4 almacén de la tienda, @p5 serie de cursos. Solo lectura y con NOLOCK: es un informe.
        internal const string SQL_FACTURACION_TIENDA = @"
SELECT RTRIM(f.Empresa) AS Empresa, RTRIM(f.[Número]) AS Factura, f.Fecha, RTRIM(f.Serie) AS Serie,
       CAST(CASE WHEN RTRIM(f.Serie) = @p5 THEN 1 ELSE 0 END AS bit) AS EsCurso,
       RTRIM(f.[Nº Cliente]) AS Cliente, RTRIM(ISNULL(pe.Contacto, f.Contacto)) AS Contacto, pe.Pedido,
       RTRIM(ISNULL(c.Nombre, f.NombreFiscal)) AS Nombre,
       RTRIM(ISNULL(c.[Dirección], f.DireccionFiscal)) AS Direccion,
       RTRIM(ISNULL(c.CodPostal, f.CodPostalFiscal)) AS CodigoPostal,
       RTRIM(ISNULL(c.[Población], f.PoblacionFiscal)) AS Poblacion,
       RTRIM(f.Vendedor) AS Vendedor, RTRIM(v.[Descripción]) AS NombreVendedor,
       STUFF((SELECT DISTINCT ', ' + RTRIM(l2.[Forma Venta]) FROM LinPedidoVta l2 WITH (NOLOCK)
              WHERE l2.Empresa = f.Empresa AND l2.[Nº Factura] = f.[Número] AND l2.[Almacén] = @p4
              FOR XML PATH('')), 1, 2, '') AS FormaVenta,
       CAST(SUM(l.[Base Imponible]) AS decimal(18,4)) AS BaseImponible,
       CAST(SUM(l.Total) AS decimal(18,4)) AS Total
FROM CabFacturaVta f WITH (NOLOCK)
     INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = f.Empresa AND l.[Nº Factura] = f.[Número]
     OUTER APPLY (SELECT TOP 1 l3.[Número] AS Pedido, p.Contacto
                  FROM LinPedidoVta l3 WITH (NOLOCK)
                       INNER JOIN CabPedidoVta p WITH (NOLOCK) ON p.Empresa = l3.Empresa AND p.[Número] = l3.[Número]
                  WHERE l3.Empresa = f.Empresa AND l3.[Nº Factura] = f.[Número] AND l3.[Almacén] = @p4
                  ORDER BY l3.[Número]) pe
     LEFT JOIN Clientes c WITH (NOLOCK) ON c.Empresa = @p0 AND c.[Nº Cliente] = f.[Nº Cliente] AND c.Contacto = ISNULL(pe.Contacto, f.Contacto)
     LEFT JOIN Vendedores v WITH (NOLOCK) ON v.Empresa = @p0 AND v.[Número] = f.Vendedor
WHERE f.Empresa IN (@p0, @p1) AND f.Fecha >= @p2 AND f.Fecha < @p3 AND l.[Almacén] = @p4
GROUP BY f.Empresa, f.[Número], f.Fecha, f.Serie, f.[Nº Cliente], f.Contacto, pe.Contacto, pe.Pedido, c.Nombre, f.NombreFiscal, c.[Dirección], f.DireccionFiscal,
         c.CodPostal, f.CodPostalFiscal, c.[Población], f.PoblacionFiscal, f.Vendedor, v.[Descripción]
ORDER BY f.Fecha, f.[Número]";

        /// <summary>NestoAPI#719: los tipos del informe (parámetro «tipo» de GET api/Informes/FacturacionTienda).</summary>
        public const string TIPO_SEMANA = "Semana";
        public const string TIPO_ACUMULADO = "Acumulado";
        public const string TIPO_COMPARATIVO = "Comparativo";

        /// <summary>NestoAPI#719: años anteriores del «Comparativo» si no se dice (y el tope).</summary>
        public const int ANIOS_COMPARATIVO_POR_DEFECTO = 2;
        public const int MAXIMO_ANIOS_COMPARATIVO = 10;

        public static readonly string[] NOMBRES_MESES =
        {
            "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        // NestoAPI#719: agregado en SQL por año y mes. @p0 empresa, @p1 espejo, @p2 1 de enero del año más antiguo (incluido), @p3 el día
        // siguiente a «hasta» (excluido), @p4 almacén de la tienda, @p5 serie de cursos, @p6 mes y @p7 día de «hasta»: de cada año entra
        // del 1 de enero a ese mismo día y mes (el rango de fechas acota la lectura; el mes y día solo descartan dentro de él).
        internal const string SQL_FACTURACION_TIENDA_ANUAL = @"
SELECT YEAR(f.Fecha) AS Anio, MONTH(f.Fecha) AS Mes,
       COUNT(DISTINCT f.Empresa + '|' + f.[Número]) AS NumeroFacturas,
       CAST(SUM(l.[Base Imponible]) AS decimal(18,4)) AS BaseImponible,
       CAST(SUM(CASE WHEN RTRIM(f.Serie) = @p5 THEN l.[Base Imponible] ELSE 0 END) AS decimal(18,4)) AS BaseImponibleCursos
FROM CabFacturaVta f WITH (NOLOCK)
     INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = f.Empresa AND l.[Nº Factura] = f.[Número]
WHERE f.Empresa IN (@p0, @p1) AND f.Fecha >= @p2 AND f.Fecha < @p3 AND l.[Almacén] = @p4
      AND (MONTH(f.Fecha) < @p6 OR (MONTH(f.Fecha) = @p6 AND DAY(f.Fecha) <= @p7))
GROUP BY YEAR(f.Fecha), MONTH(f.Fecha)";

        /// <summary>
        /// NestoAPI#719: el tipo normalizado («Semana», «Acumulado», «Comparativo»; vacío = «Semana») o null si no es ninguno.
        /// </summary>
        public static string NormalizarTipo(string tipo)
        {
            if (string.IsNullOrWhiteSpace(tipo))
            {
                return TIPO_SEMANA;
            }
            string limpio = tipo.Trim();
            return new[] { TIPO_SEMANA, TIPO_ACUMULADO, TIPO_COMPARATIVO }
                .FirstOrDefault(t => string.Equals(t, limpio, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// NestoAPI#719: los años anteriores del informe anual (0 en el «Acumulado»; en el «Comparativo», <paramref name="anios"/> o 2,
        /// de 1 a 10). Devuelve el mensaje de error, o null si vale.
        /// </summary>
        public static string ResolverAniosAnteriores(string tipo, int? anios, out int aniosAnteriores)
        {
            aniosAnteriores = 0;
            if (tipo != TIPO_COMPARATIVO)
            {
                return null;
            }
            aniosAnteriores = anios ?? ANIOS_COMPARATIVO_POR_DEFECTO;
            if (aniosAnteriores < 1 || aniosAnteriores > MAXIMO_ANIOS_COMPARATIVO)
            {
                return $"«anios» tiene que estar entre 1 y {MAXIMO_ANIOS_COMPARATIVO}.";
            }
            return null;
        }

        /// <summary>Los parámetros de <see cref="SQL_FACTURACION_TIENDA_ANUAL"/>.</summary>
        internal static object[] ParametrosAnual(string tienda, DateTime hasta, int aniosAnteriores) => new object[]
        {
            new SqlParameter("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO),
            new SqlParameter("@p1", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO),
            new SqlParameter("@p2", new DateTime(hasta.Year - Math.Max(0, aniosAnteriores), 1, 1)),
            new SqlParameter("@p3", hasta.Date.AddDays(1)),
            new SqlParameter("@p4", tienda),
            new SqlParameter("@p5", Constantes.Series.SERIE_CURSOS),
            new SqlParameter("@p6", hasta.Month),
            new SqlParameter("@p7", hasta.Day)
        };

        /// <summary>
        /// NestoAPI#719: el informe anual a partir de las filas agregadas (año, mes): todos los meses de enero al de <paramref name="hasta"/>
        /// (aunque no tengan facturas), su acumulado y, con años anteriores, la variación del año de «hasta» respecto a cada uno. Las filas
        /// de otros años o meses (no debería haberlas) se ignoran.
        /// </summary>
        public static FacturacionTiendaAnualDTO ConstruirAnual(IEnumerable<FilaMesFacturacionTiendaDTO> filas, string tienda, DateTime hasta,
            int aniosAnteriores)
        {
            DateTime dia = hasta.Date;
            int anio = dia.Year;
            List<int> anteriores = Enumerable.Range(1, Math.Max(0, aniosAnteriores)).Select(i => anio - i).ToList();
            Dictionary<(int, int), FilaMesFacturacionTiendaDTO> porAnioMes = (filas ?? Enumerable.Empty<FilaMesFacturacionTiendaDTO>())
                .GroupBy(f => (f.Anio, f.Mes))
                .ToDictionary(g => g.Key, g => new FilaMesFacturacionTiendaDTO
                {
                    Anio = g.Key.Item1,
                    Mes = g.Key.Item2,
                    NumeroFacturas = g.Sum(f => f.NumeroFacturas),
                    BaseImponible = g.Sum(f => f.BaseImponible),
                    BaseImponibleCursos = g.Sum(f => f.BaseImponibleCursos)
                });
            FilaMesFacturacionTiendaDTO Leer(int a, int m) =>
                porAnioMes.TryGetValue((a, m), out FilaMesFacturacionTiendaDTO f) ? f : new FilaMesFacturacionTiendaDTO { Anio = a, Mes = m };

            var meses = new List<MesFacturacionTiendaDTO>();
            decimal acumulado = 0;
            for (int mes = 1; mes <= dia.Month; mes++)
            {
                FilaMesFacturacionTiendaDTO actual = Leer(anio, mes);
                decimal baseMes = Redondear(actual.BaseImponible);
                acumulado += baseMes;
                DateTime primero = new DateTime(anio, mes, 1);
                meses.Add(new MesFacturacionTiendaDTO
                {
                    Mes = mes,
                    NombreMes = NOMBRES_MESES[mes - 1],
                    Desde = primero,
                    Hasta = mes == dia.Month ? dia : primero.AddMonths(1).AddDays(-1),
                    NumeroFacturas = actual.NumeroFacturas,
                    BaseImponible = baseMes,
                    BaseImponibleCursos = Redondear(actual.BaseImponibleCursos),
                    Acumulado = acumulado,
                    Anteriores = anteriores.Select(a => Comparar(baseMes, a, Leer(a, mes).NumeroFacturas, Redondear(Leer(a, mes).BaseImponible))).ToList()
                });
            }

            var total = new MesFacturacionTiendaDTO
            {
                Mes = 0,
                NombreMes = "Total",
                Desde = new DateTime(anio, 1, 1),
                Hasta = dia,
                NumeroFacturas = meses.Sum(m => m.NumeroFacturas),
                BaseImponible = acumulado,
                BaseImponibleCursos = meses.Sum(m => m.BaseImponibleCursos),
                Acumulado = acumulado,
                Anteriores = anteriores.Select((a, i) => Comparar(acumulado, a,
                    meses.Sum(m => m.Anteriores[i].NumeroFacturas), meses.Sum(m => m.Anteriores[i].BaseImponible))).ToList()
            };

            return new FacturacionTiendaAnualDTO
            {
                Tipo = anteriores.Count > 0 ? TIPO_COMPARATIVO : TIPO_ACUMULADO,
                Tienda = tienda,
                Anio = anio,
                Desde = new DateTime(anio, 1, 1),
                Hasta = dia,
                AniosAnteriores = anteriores,
                Meses = meses,
                Total = total
            };
        }

        private static ComparativoMesFacturacionTiendaDTO Comparar(decimal baseActual, int anio, int numeroFacturas, decimal baseAnterior)
        {
            decimal variacion = baseActual - baseAnterior;
            return new ComparativoMesFacturacionTiendaDTO
            {
                Anio = anio,
                NumeroFacturas = numeroFacturas,
                BaseImponible = baseAnterior,
                VariacionImporte = variacion,
                VariacionPorcentaje = baseAnterior == 0 ? (decimal?)null : Redondear(variacion * 100 / Math.Abs(baseAnterior))
            };
        }

        /// <summary>La tienda normalizada (REI, ALC) o null si no es una tienda.</summary>
        public static string NormalizarTienda(string tienda)
        {
            string limpia = tienda?.Trim().ToUpperInvariant();
            return TIENDAS.Contains(limpia) ? limpia : null;
        }

        /// <summary>La última semana cerrada, de lunes a domingo, anterior a <paramref name="hoy"/> (un lunes da la semana pasada).</summary>
        public static (DateTime Desde, DateTime Hasta) SemanaCerradaAnterior(DateTime hoy)
        {
            int diasDesdeLunes = ((int)hoy.DayOfWeek + 6) % 7;
            DateTime lunesDeEstaSemana = hoy.Date.AddDays(-diasDesdeLunes);
            return (lunesDeEstaSemana.AddDays(-7), lunesDeEstaSemana.AddDays(-1));
        }

        /// <summary>
        /// El periodo pedido (días completos, ambos incluidos). Sin fechas, la última semana cerrada; con solo una, la semana que empieza
        /// (desde) o acaba (hasta) en ella. Devuelve el mensaje de error, o null si el periodo vale.
        /// </summary>
        public static string ResolverPeriodo(DateTime? desde, DateTime? hasta, DateTime hoy, out DateTime fechaDesde, out DateTime fechaHasta)
        {
            if (!desde.HasValue && !hasta.HasValue)
            {
                (fechaDesde, fechaHasta) = SemanaCerradaAnterior(hoy);
            }
            else
            {
                fechaDesde = (desde ?? hasta.Value.AddDays(-6)).Date;
                fechaHasta = (hasta ?? desde.Value.AddDays(6)).Date;
            }
            if (fechaDesde > fechaHasta)
            {
                return "«desde» no puede ser posterior a «hasta».";
            }
            if ((fechaHasta - fechaDesde).TotalDays >= MAXIMO_DIAS_PERIODO)
            {
                return $"El periodo no puede pasar de {MAXIMO_DIAS_PERIODO} días.";
            }
            return null;
        }

        /// <summary>Los parámetros de <see cref="SQL_FACTURACION_TIENDA"/> (cada consulta necesita los suyos).</summary>
        internal static object[] Parametros(string tienda, DateTime desde, DateTime hasta) => new object[]
        {
            new SqlParameter("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO),
            new SqlParameter("@p1", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO),
            new SqlParameter("@p2", desde.Date),
            new SqlParameter("@p3", hasta.Date.AddDays(1)),
            new SqlParameter("@p4", tienda),
            new SqlParameter("@p5", Constantes.Series.SERIE_CURSOS)
        };

        /// <summary>El informe a partir de las facturas leídas: limpia los textos, agrupa por cliente y calcula los totales.</summary>
        public static FacturacionTiendaDTO Construir(IEnumerable<FacturaTiendaDTO> filas, string tienda, DateTime desde, DateTime hasta)
        {
            List<FacturaTiendaDTO> facturas = (filas ?? Enumerable.Empty<FacturaTiendaDTO>())
                .Select(Limpiar)
                .OrderBy(f => f.Fecha).ThenBy(f => f.Factura, StringComparer.Ordinal)
                .ToList();

            List<ClienteFacturacionTiendaDTO> clientes = facturas
                .GroupBy(f => new { f.Cliente, f.Contacto, f.Vendedor })
                .Select(g =>
                {
                    FacturaTiendaDTO ultima = g.Last();
                    return new ClienteFacturacionTiendaDTO
                    {
                        Cliente = g.Key.Cliente,
                        Contacto = g.Key.Contacto,
                        Nombre = ultima.Nombre,
                        Direccion = ultima.Direccion,
                        CodigoPostal = ultima.CodigoPostal,
                        Poblacion = ultima.Poblacion,
                        Vendedor = g.Key.Vendedor,
                        NombreVendedor = ultima.NombreVendedor,
                        NumeroFacturas = g.Count(),
                        BaseImponible = Redondear(g.Sum(f => f.BaseImponible)),
                        Total = Redondear(g.Sum(f => f.Total))
                    };
                })
                .OrderByDescending(c => c.BaseImponible)
                .ThenBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new FacturacionTiendaDTO
            {
                Tienda = tienda,
                Desde = desde.Date,
                Hasta = hasta.Date,
                Facturas = facturas,
                Clientes = clientes,
                NumeroFacturas = facturas.Count,
                NumeroClientes = clientes.Select(c => new { c.Cliente, c.Contacto }).Distinct().Count(),
                TotalBaseImponible = Redondear(facturas.Sum(f => f.BaseImponible)),
                Total = Redondear(facturas.Sum(f => f.Total)),
                TotalBaseImponibleCursos = Redondear(facturas.Where(f => f.EsCurso).Sum(f => f.BaseImponible))
            };
        }

        private static FacturaTiendaDTO Limpiar(FacturaTiendaDTO f)
        {
            f.Empresa = f.Empresa?.Trim();
            f.Factura = f.Factura?.Trim();
            f.Serie = f.Serie?.Trim();
            f.EsCurso = string.Equals(f.Serie, Constantes.Series.SERIE_CURSOS, StringComparison.OrdinalIgnoreCase);
            f.Cliente = f.Cliente?.Trim();
            f.Contacto = f.Contacto?.Trim();
            f.Nombre = f.Nombre?.Trim();
            f.Direccion = f.Direccion?.Trim();
            f.CodigoPostal = f.CodigoPostal?.Trim();
            f.Poblacion = f.Poblacion?.Trim();
            f.Vendedor = VacioANull(f.Vendedor);
            f.NombreVendedor = VacioANull(f.NombreVendedor);
            f.FormaVenta = VacioANull(f.FormaVenta);
            f.BaseImponible = Redondear(f.BaseImponible);
            f.Total = Redondear(f.Total);
            return f;
        }

        private static string VacioANull(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        private static decimal Redondear(decimal importe) => RoundingHelper.Round(importe, 2);
    }
}
