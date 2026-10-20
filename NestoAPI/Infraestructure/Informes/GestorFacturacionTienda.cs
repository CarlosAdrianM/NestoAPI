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
    /// </summary>
    public static class GestorFacturacionTienda
    {
        public static readonly string[] TIENDAS = { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        /// <summary>Tope del periodo, para que nadie lance por error una consulta de años.</summary>
        public const int MAXIMO_DIAS_PERIODO = 366;

        // @p0 empresa (y la de las fichas de clientes y vendedores), @p1 empresa espejo, @p2 desde (incluido), @p3 hasta (excluido),
        // @p4 almacén de la tienda, @p5 grupo de peluquería, @p6 serie de cursos. Solo lectura y con NOLOCK: es un informe.
        internal const string SQL_FACTURACION_TIENDA = @"
SELECT RTRIM(f.Empresa) AS Empresa, RTRIM(f.[Número]) AS Factura, f.Fecha, RTRIM(f.Serie) AS Serie,
       CAST(CASE WHEN RTRIM(f.Serie) = @p6 THEN 1 ELSE 0 END AS bit) AS EsCurso,
       RTRIM(f.[Nº Cliente]) AS Cliente, RTRIM(f.Contacto) AS Contacto,
       RTRIM(ISNULL(c.Nombre, f.NombreFiscal)) AS Nombre,
       RTRIM(ISNULL(c.[Dirección], f.DireccionFiscal)) AS Direccion,
       RTRIM(ISNULL(c.CodPostal, f.CodPostalFiscal)) AS CodigoPostal,
       RTRIM(ISNULL(c.[Población], f.PoblacionFiscal)) AS Poblacion,
       RTRIM(c.Vendedor) AS Vendedor, RTRIM(v.[Descripción]) AS NombreVendedor,
       RTRIM(vp.Vendedor) AS VendedorPeluqueria, RTRIM(vpn.[Descripción]) AS NombreVendedorPeluqueria,
       CAST(SUM(l.[Base Imponible]) AS decimal(18,4)) AS BaseImponible,
       CAST(SUM(l.Total) AS decimal(18,4)) AS Total
FROM CabFacturaVta f WITH (NOLOCK)
     INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = f.Empresa AND l.[Nº Factura] = f.[Número]
     LEFT JOIN Clientes c WITH (NOLOCK) ON c.Empresa = @p0 AND c.[Nº Cliente] = f.[Nº Cliente] AND c.Contacto = f.Contacto
     LEFT JOIN Vendedores v WITH (NOLOCK) ON v.Empresa = @p0 AND v.[Número] = c.Vendedor
     OUTER APPLY (SELECT TOP 1 g.Vendedor FROM VendedoresClienteGrupoProducto g WITH (NOLOCK)
                  WHERE g.Empresa = @p0 AND g.Cliente = f.[Nº Cliente] AND g.Contacto = f.Contacto AND g.GrupoProducto = @p5
                  ORDER BY g.Id DESC) vp
     LEFT JOIN Vendedores vpn WITH (NOLOCK) ON vpn.Empresa = @p0 AND vpn.[Número] = vp.Vendedor
WHERE f.Empresa IN (@p0, @p1) AND f.Fecha >= @p2 AND f.Fecha < @p3 AND l.[Almacén] = @p4
GROUP BY f.Empresa, f.[Número], f.Fecha, f.Serie, f.[Nº Cliente], f.Contacto, c.Nombre, f.NombreFiscal, c.[Dirección], f.DireccionFiscal,
         c.CodPostal, f.CodPostalFiscal, c.[Población], f.PoblacionFiscal, c.Vendedor, v.[Descripción], vp.Vendedor, vpn.[Descripción]
ORDER BY f.Fecha, f.[Número]";

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
            new SqlParameter("@p5", Constantes.Productos.GRUPO_PELUQUERIA),
            new SqlParameter("@p6", Constantes.Series.SERIE_CURSOS)
        };

        /// <summary>El informe a partir de las facturas leídas: limpia los textos, agrupa por cliente y calcula los totales.</summary>
        public static FacturacionTiendaDTO Construir(IEnumerable<FacturaTiendaDTO> filas, string tienda, DateTime desde, DateTime hasta)
        {
            List<FacturaTiendaDTO> facturas = (filas ?? Enumerable.Empty<FacturaTiendaDTO>())
                .Select(Limpiar)
                .OrderBy(f => f.Fecha).ThenBy(f => f.Factura, StringComparer.Ordinal)
                .ToList();

            List<ClienteFacturacionTiendaDTO> clientes = facturas
                .GroupBy(f => new { f.Cliente, f.Contacto })
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
                        Vendedor = ultima.Vendedor,
                        NombreVendedor = ultima.NombreVendedor,
                        VendedorPeluqueria = ultima.VendedorPeluqueria,
                        NombreVendedorPeluqueria = ultima.NombreVendedorPeluqueria,
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
                NumeroClientes = clientes.Count,
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
            f.VendedorPeluqueria = VacioANull(f.VendedorPeluqueria);
            f.NombreVendedorPeluqueria = VacioANull(f.NombreVendedorPeluqueria);
            f.BaseImponible = Redondear(f.BaseImponible);
            f.Total = Redondear(f.Total);
            return f;
        }

        private static string VacioANull(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        private static decimal Redondear(decimal importe) => RoundingHelper.Round(importe, 2);
    }
}
