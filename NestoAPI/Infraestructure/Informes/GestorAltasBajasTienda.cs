using NestoAPI.Models;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// NestoAPI#886 (decisión de Carlos con Manuel y Santiago, 23/11/26, a raíz de la sugerencia 1419 de Paloma): al enviar una
    /// reposición lo que no está se da de baja y, si aparece, se da de alta. Para llevar el control, una vez al mes cada tienda (REI,
    /// ALC) recibe lo que se ha dado de alta o de baja de su stock: inventarios (diario «_RegInvent») y «No está» al enviar una
    /// reposición o diferencias de reposición (diario «_ErrRepo»), de las empresas 1 y 3. LA REGLA (pura) es <see cref="Construir"/>.
    /// <para>El valor es SIEMPRE a PVP (Productos.PVP de la empresa 1): las tiendas no pueden ver el precio de coste.</para>
    /// </summary>
    public static class GestorAltasBajasTienda
    {
        public const string DIARIO_INVENTARIO = "_RegInvent";
        public const string DIARIO_ERROR_REPOSICION = "_ErrRepo";
        public const string MOTIVO_INVENTARIO = "Inventario";

        /// <summary>Tope del periodo, como en FacturacionTienda.</summary>
        public const int MAXIMO_DIAS_PERIODO = GestorFacturacionTienda.MAXIMO_DIAS_PERIODO;

        // «Reposición 81028: «No está» al enviar (Paloma)» (StockACeroNoEsta.Texto) y «Diferencia de reposición 81028 (…)»
        // (DiferenciasReposicion). El texto es char(50): puede venir recortado, solo cuenta el principio.
        private static readonly Regex NO_ESTA = new Regex(@"^\s*Reposici[oó]n\s+(\d+)\s*:\s*«?\s*No est[aá]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex DIFERENCIA = new Regex(@"^\s*Diferencia\s+de\s+(?:la\s+)?reposici[oó]n\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // @p0 empresa (y la de la ficha del producto), @p1 empresa espejo, @p2 desde (incluido), @p3 hasta (excluido), @p4 almacén,
        // @p5 diario de inventario, @p6 diario de errores de reposición. Solo lectura y con NOLOCK: es un informe. Ni Coste ni Importe.
        internal const string SQL_ALTAS_BAJAS_TIENDA = @"
SELECT RTRIM(e.Empresa) AS Empresa, e.[Nº Orden] AS NumeroOrden, RTRIM(e.[Número]) AS Producto, RTRIM(e.[Almacén]) AS Almacen,
       e.Fecha, e.[Fecha Modificación] AS FechaModificacion, CAST(e.Cantidad AS int) AS Cantidad, RTRIM(e.Diario) AS Diario,
       RTRIM(e.Texto) AS Texto, RTRIM(e.Usuario) AS Usuario,
       RTRIM(p.Nombre) AS Nombre, RTRIM(p.Familia) AS Familia, CAST(p.PVP AS decimal(18,4)) AS Pvp
FROM ExtractoProducto e WITH (NOLOCK)
     LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = @p0 AND p.[Número] = e.[Número]
WHERE e.Empresa IN (@p0, @p1) AND e.[Almacén] = @p4 AND e.Fecha >= @p2 AND e.Fecha < @p3
      AND e.Diario IN (@p5, @p6) AND e.Cantidad <> 0
ORDER BY e.Fecha, e.[Fecha Modificación], e.[Nº Orden]";

        /// <summary>Los parámetros de <see cref="SQL_ALTAS_BAJAS_TIENDA"/> (días completos, ambos incluidos).</summary>
        internal static object[] Parametros(string almacen, DateTime desde, DateTime hasta) => new object[]
        {
            new SqlParameter("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO),
            new SqlParameter("@p1", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO),
            new SqlParameter("@p2", desde.Date),
            new SqlParameter("@p3", hasta.Date.AddDays(1)),
            new SqlParameter("@p4", almacen),
            new SqlParameter("@p5", DIARIO_INVENTARIO),
            new SqlParameter("@p6", DIARIO_ERROR_REPOSICION)
        };

        /// <summary>El nombre de la tienda para los textos (Reina, Alcobendas) o el código si no es una de ellas.</summary>
        public static string NombreTienda(string almacen)
        {
            switch (almacen?.Trim().ToUpperInvariant())
            {
                case Constantes.Almacenes.REINA:
                    return "Reina";
                case Constantes.Almacenes.ALCOBENDAS:
                    return "Alcobendas";
                default:
                    return almacen?.Trim();
            }
        }

        /// <summary>A quién va el correo de cada tienda: su buzón (el mismo que usan los correos de presupuestos y eventos).</summary>
        public static string CorreoTienda(string almacen)
        {
            switch (almacen?.Trim().ToUpperInvariant())
            {
                case Constantes.Almacenes.REINA:
                    return Constantes.Correos.TIENDA_REINA;
                case Constantes.Almacenes.ALCOBENDAS:
                    return Constantes.Correos.TIENDA_ALCOBENDAS;
                default:
                    return null;
            }
        }

        /// <summary>El mes natural anterior a <paramref name="hoy"/> (en enero, diciembre del año anterior).</summary>
        public static (DateTime Desde, DateTime Hasta) MesAnterior(DateTime hoy)
        {
            DateTime primeroDeEsteMes = new DateTime(hoy.Year, hoy.Month, 1);
            return (primeroDeEsteMes.AddMonths(-1), primeroDeEsteMes.AddDays(-1));
        }

        /// <summary>
        /// El periodo pedido (días completos, ambos incluidos). Sin fechas, el mes natural anterior; con solo «desde», hasta el final de
        /// su mes; con solo «hasta», desde el principio de su mes. Devuelve el mensaje de error, o null si vale.
        /// </summary>
        public static string ResolverPeriodo(DateTime? desde, DateTime? hasta, DateTime hoy, out DateTime fechaDesde, out DateTime fechaHasta)
        {
            if (!desde.HasValue && !hasta.HasValue)
            {
                (fechaDesde, fechaHasta) = MesAnterior(hoy);
            }
            else
            {
                fechaDesde = (desde ?? new DateTime(hasta.Value.Year, hasta.Value.Month, 1)).Date;
                fechaHasta = (hasta ?? new DateTime(desde.Value.Year, desde.Value.Month, 1).AddMonths(1).AddDays(-1)).Date;
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

        /// <summary>El motivo en llano: Inventario · «No está» al enviar la reposición N · Diferencia de la reposición N · si no, el texto.</summary>
        public static string Motivo(string diario, string texto)
        {
            string limpio = texto?.Trim();
            if (string.Equals(diario?.Trim(), DIARIO_INVENTARIO, StringComparison.OrdinalIgnoreCase))
            {
                return MOTIVO_INVENTARIO;
            }
            if (!string.IsNullOrEmpty(limpio))
            {
                Match noEsta = NO_ESTA.Match(limpio);
                if (noEsta.Success)
                {
                    return $"«No está» al enviar la reposición {noEsta.Groups[1].Value}";
                }
                Match diferencia = DIFERENCIA.Match(limpio);
                if (diferencia.Success)
                {
                    return $"Diferencia de la reposición {diferencia.Groups[1].Value}";
                }
            }
            return string.IsNullOrEmpty(limpio) ? diario?.Trim() : limpio;
        }

        /// <summary>El usuario sin dominio («NUEVAVISION\Paloma» → «Paloma»).</summary>
        public static string UsuarioSinDominio(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1).Trim() : limpio;
        }

        /// <summary>
        /// LA REGLA: el informe a partir de las filas leídas. Líneas por fecha (y hora y Nº de orden), totales de altas, bajas y neto
        /// a PVP (sin PVP = 0) y los productos con alguna alta Y alguna baja en el periodo. Las filas a 0 unidades no cuentan.
        /// </summary>
        public static AltasBajasTiendaDTO Construir(IEnumerable<FilaAltaBajaTiendaDTO> filas, string almacen, DateTime desde, DateTime hasta)
        {
            List<(FilaAltaBajaTiendaDTO Fila, MovimientoAltaBajaTiendaDTO Movimiento)> lineas = (filas ?? Enumerable.Empty<FilaAltaBajaTiendaDTO>())
                .Where(f => f != null && f.Cantidad != 0)
                .Select(f => (f, Movimiento(f)))
                .OrderBy(x => x.Item2.Fecha)
                .ThenBy(x => x.Item2.FechaHora)
                .ThenBy(x => x.f.NumeroOrden)
                .ToList();
            List<MovimientoAltaBajaTiendaDTO> movimientos = lineas.Select(x => x.Movimiento).ToList();

            List<MovimientoAltaBajaTiendaDTO> altas = movimientos.Where(m => m.Unidades > 0).ToList();
            List<MovimientoAltaBajaTiendaDTO> bajas = movimientos.Where(m => m.Unidades < 0).ToList();

            List<ProductoApareceDesapareceDTO> aparece = movimientos
                .GroupBy(m => m.Producto, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Any(m => m.Unidades > 0) && g.Any(m => m.Unidades < 0))
                .Select(g => new ProductoApareceDesapareceDTO
                {
                    Producto = g.Key,
                    Nombre = g.Select(m => m.Nombre).FirstOrDefault(n => !string.IsNullOrEmpty(n)),
                    Familia = g.Select(m => m.Familia).FirstOrDefault(n => !string.IsNullOrEmpty(n)),
                    UnidadesAlta = g.Where(m => m.Unidades > 0).Sum(m => m.Unidades),
                    UnidadesBaja = -g.Where(m => m.Unidades < 0).Sum(m => m.Unidades),
                    UnidadesNeto = g.Sum(m => m.Unidades),
                    ValorPvpNeto = Redondear(g.Sum(m => m.ValorPvp))
                })
                .OrderBy(p => p.Producto, StringComparer.Ordinal)
                .ToList();

            int unidadesAlta = altas.Sum(m => m.Unidades);
            int unidadesBaja = -bajas.Sum(m => m.Unidades);
            decimal valorAlta = Redondear(altas.Sum(m => m.ValorPvp));
            decimal valorBaja = Redondear(-bajas.Sum(m => m.ValorPvp));
            string tienda = almacen?.Trim().ToUpperInvariant();
            return new AltasBajasTiendaDTO
            {
                Almacen = tienda,
                NombreTienda = NombreTienda(tienda),
                Desde = desde.Date,
                Hasta = hasta.Date,
                Movimientos = movimientos,
                ApareceYDesaparece = aparece,
                UnidadesAlta = unidadesAlta,
                ValorPvpAlta = valorAlta,
                UnidadesBaja = unidadesBaja,
                ValorPvpBaja = valorBaja,
                UnidadesNeto = unidadesAlta - unidadesBaja,
                ValorPvpNeto = valorAlta - valorBaja
            };
        }

        private static MovimientoAltaBajaTiendaDTO Movimiento(FilaAltaBajaTiendaDTO f)
        {
            decimal pvp = Redondear(f.Pvp ?? 0m);
            return new MovimientoAltaBajaTiendaDTO
            {
                Fecha = f.Fecha.Date,
                FechaHora = f.FechaModificacion == default ? f.Fecha : f.FechaModificacion,
                Producto = f.Producto?.Trim(),
                Nombre = VacioANull(f.Nombre),
                Familia = VacioANull(f.Familia),
                Unidades = f.Cantidad,
                Motivo = Motivo(f.Diario, f.Texto),
                Usuario = UsuarioSinDominio(f.Usuario),
                PvpUnitario = pvp,
                ValorPvp = Redondear(f.Cantidad * pvp)
            };
        }

        private static string VacioANull(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        private static decimal Redondear(decimal importe) => RoundingHelper.Round(importe, 2);
    }
}
