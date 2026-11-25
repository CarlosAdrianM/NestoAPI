using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana;
using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>Un producto que un cliente tiene en un pedido pendiente o en curso.</summary>
    public class PendienteClienteOferta
    {
        public string Cliente { get; set; }
        public string Producto { get; set; }
    }

    /// <summary>«Ofertas para ti»: las lecturas, todas por tanda (de clientes o de productos), nunca una por par.</summary>
    public interface ILectorOfertasParaTi
    {
        /// <summary>Las filas vigentes hoy de la campaña (o de todas con nombre de campaña si va vacía), por producto, con su ficha.</summary>
        Task<List<FilaCampanaOferta>> LeerFilasCampana(string empresa, string campana, DateTime hoy);
        /// <summary>Producto → clientes en 12 meses (SugerenciasProductosPopularidad; vacío si no hay tabla).</summary>
        Task<Dictionary<string, int>> LeerPopularidad(IReadOnlyCollection<string> productos);
        /// <summary>Producto → disponible (ALG+REI+ALC, empresas 1 y 3, con lo que va de camino, menos lo pendiente).</summary>
        Task<Dictionary<string, int>> LeerDisponibles(IReadOnlyCollection<string> productos);
        /// <summary>Compras (albaranes, empresa 1) de esos clientes entre <paramref name="desde"/> y <paramref name="hasta"/>, por producto y día.</summary>
        Task<List<CompraDiaReposicion>> LeerCompras(string empresa, IReadOnlyCollection<string> clientes, DateTime desde, DateTime hasta);
        /// <summary>Lo que esos clientes tienen en pedidos pendientes o en curso (-1..1).</summary>
        Task<List<PendienteClienteOferta>> LeerPendientes(string empresa, IReadOnlyCollection<string> clientes);
        /// <summary>Producto → familia.</summary>
        Task<Dictionary<string, string>> LeerFamilias(string empresa, IReadOnlyCollection<string> productos);
    }

    /// <summary>
    /// Las lecturas de «Ofertas para ti» (SQL a mano, como el resto de la carpeta Campanas). Compras y pendientes con la lista de
    /// clientes (índices de LinPedidoVta que empiezan por Nº Cliente: búsquedas, como la lectura de un cliente del recordatorio de
    /// reposición, #661); el stock por el lector de la campaña «solo en stock» (#889) por lotes; la popularidad por el repositorio de
    /// las sugerencias (#671).
    /// </summary>
    public class LectorOfertasParaTiSql : ILectorOfertasParaTi
    {
        internal const int LOTE_CLIENTES = 200;
        internal const int LOTE_PRODUCTOS = 500;
        /// <summary>El lector de stock repite la lista tres veces: 600 × 3 + 7 parámetros, por debajo de los 2.100 de SQL Server.</summary>
        internal const int LOTE_STOCK = 600;

        internal const string SQL_FILAS = @"
SELECT RTRIM(d.Campana) AS Campana, RTRIM(d.[Nº Producto]) AS Producto, d.Descuento, d.Precio, d.AudienciaOferta,
       CAST(d.FechaDesde AS datetime) AS FechaDesde, CAST(d.FechaHasta AS datetime) AS FechaHasta,
       RTRIM(p.Nombre) AS Nombre, RTRIM(p.Familia) AS Familia, RTRIM(f.Descripción) AS NombreFamilia,
       RTRIM(p.Grupo) AS Grupo, RTRIM(p.SubGrupo) AS SubGrupo, p.PVP, p.Estado
FROM dbo.DescuentosProducto d WITH (NOLOCK)
INNER JOIN dbo.Productos p WITH (NOLOCK) ON p.Empresa = d.Empresa AND p.Número = d.[Nº Producto]
LEFT JOIN dbo.Familias f WITH (NOLOCK) ON f.Empresa = p.Empresa AND f.Número = p.Familia
WHERE d.Empresa = @empresa AND d.[Nº Producto] IS NOT NULL AND d.Campana IS NOT NULL AND LTRIM(RTRIM(d.Campana)) <> ''
  AND ISNULL(RTRIM(d.[Nº Cliente]), '') = '' AND ISNULL(RTRIM(d.NºProveedor), '') = '' AND d.CantidadMínima <= 1 AND d.FiltroProducto IS NULL
  AND (d.FechaDesde IS NULL OR d.FechaDesde <= @hoy) AND (d.FechaHasta IS NULL OR d.FechaHasta >= @hoy)
  AND (@campana IS NULL OR d.Campana = @campana)";

        internal const string SQL_COMPRAS = @"
SELECT RTRIM(l.[Nº Cliente]) AS Cliente, RTRIM(l.Producto) AS Producto,
       CAST(CAST(l.[Fecha Albarán] AS date) AS datetime) AS Dia, SUM(l.[Base Imponible]) AS BaseImponible,
       CAST(SUM(l.Cantidad) AS int) AS Cantidad
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.Estado >= 2 AND l.Cantidad > 0 AND l.Producto IS NOT NULL
  AND l.[Fecha Albarán] >= @desde AND l.[Fecha Albarán] < @hasta AND l.[Nº Cliente] IN ({0})
GROUP BY l.[Nº Cliente], l.Producto, CAST(l.[Fecha Albarán] AS date)";

        internal const string SQL_PENDIENTES = @"
SELECT DISTINCT RTRIM(l.[Nº Cliente]) AS Cliente, RTRIM(l.Producto) AS Producto
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Estado BETWEEN -1 AND 1 AND l.Empresa = @empresa AND l.TipoLinea = 1 AND l.Producto IS NOT NULL AND l.[Nº Cliente] IN ({0})";

        internal const string SQL_FAMILIAS = @"
SELECT RTRIM(p.Número) AS Producto, RTRIM(p.Familia) AS Familia FROM dbo.Productos p WITH (NOLOCK)
WHERE p.Empresa = @empresa AND p.Número IN ({0})";

        private readonly NVEntities db;
        private readonly Func<ILectorDisponibleCampanaStock> lectorStock;

        public LectorOfertasParaTiSql(NVEntities db, Func<ILectorDisponibleCampanaStock> lectorStock = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.lectorStock = lectorStock ?? (() => LectorDisponibleCampanaStockSql.DesdeConfiguracion());
        }

        public async Task<List<FilaCampanaOferta>> LeerFilasCampana(string empresa, string campana, DateTime hoy)
        {
            return await db.Database.SqlQuery<FilaCampanaOferta>(SQL_FILAS,
                new SqlParameter("@empresa", SqlDbType.VarChar, 3) { Value = empresa },
                new SqlParameter("@hoy", SqlDbType.Date) { Value = hoy.Date },
                new SqlParameter("@campana", SqlDbType.NVarChar, 50) { Value = string.IsNullOrWhiteSpace(campana) ? (object)DBNull.Value : campana.Trim() })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<Dictionary<string, int>> LeerPopularidad(IReadOnlyCollection<string> productos)
        {
            List<PopularidadProducto> filas = await new RepositorioSugerenciasProductosSql(db).LeerPopularidad(productos ?? new List<string>())
                .ConfigureAwait(false) ?? new List<PopularidadProducto>();
            var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (PopularidadProducto p in filas.Where(f => f?.Producto != null))
            {
                resultado[p.Producto.Trim()] = p.Clientes12Meses;
            }
            return resultado;
        }

        public async Task<Dictionary<string, int>> LeerDisponibles(IReadOnlyCollection<string> productos)
        {
            var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = Limpios(productos);
            if (lista.Count == 0)
            {
                return resultado;
            }
            ILectorDisponibleCampanaStock lector = lectorStock();
            for (int i = 0; i < lista.Count; i += LOTE_STOCK)
            {
                List<string> lote = lista.Skip(i).Take(LOTE_STOCK).ToList();
                // Pedido 0: no hay pedido propio que apartar
                foreach (DisponibleProductoCampana d in await lector.Leer(Constantes.Empresas.EMPRESA_POR_DEFECTO, 0, lote).ConfigureAwait(false)
                    ?? new List<DisponibleProductoCampana>())
                {
                    if (d?.Producto != null)
                    {
                        resultado[d.Producto.Trim()] = d.Disponible;
                    }
                }
                foreach (string p in lote.Where(p => !resultado.ContainsKey(p)))
                {
                    resultado[p] = 0; // sin movimientos: no hay stock
                }
            }
            return resultado;
        }

        public async Task<List<CompraDiaReposicion>> LeerCompras(string empresa, IReadOnlyCollection<string> clientes, DateTime desde, DateTime hasta)
        {
            var resultado = new List<CompraDiaReposicion>();
            List<string> lista = Limpios(clientes);
            for (int i = 0; i < lista.Count; i += LOTE_CLIENTES)
            {
                List<string> lote = lista.Skip(i).Take(LOTE_CLIENTES).ToList();
                List<SqlParameter> parametros = Parametros("@c", lote, 10);
                parametros.Add(new SqlParameter("@empresa", SqlDbType.VarChar, 3) { Value = empresa });
                parametros.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde });
                parametros.Add(new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta });
                string sql = string.Format(SQL_COMPRAS, string.Join(", ", lote.Select((_, n) => "@c" + n)));
                resultado.AddRange(await db.Database.SqlQuery<CompraDiaReposicion>(sql, parametros.Cast<object>().ToArray()).ToListAsync().ConfigureAwait(false));
            }
            return resultado;
        }

        public async Task<List<PendienteClienteOferta>> LeerPendientes(string empresa, IReadOnlyCollection<string> clientes)
        {
            var resultado = new List<PendienteClienteOferta>();
            List<string> lista = Limpios(clientes);
            for (int i = 0; i < lista.Count; i += LOTE_CLIENTES)
            {
                List<string> lote = lista.Skip(i).Take(LOTE_CLIENTES).ToList();
                List<SqlParameter> parametros = Parametros("@c", lote, 10);
                parametros.Add(new SqlParameter("@empresa", SqlDbType.VarChar, 3) { Value = empresa });
                string sql = string.Format(SQL_PENDIENTES, string.Join(", ", lote.Select((_, n) => "@c" + n)));
                resultado.AddRange(await db.Database.SqlQuery<PendienteClienteOferta>(sql, parametros.Cast<object>().ToArray()).ToListAsync().ConfigureAwait(false));
            }
            return resultado;
        }

        public async Task<Dictionary<string, string>> LeerFamilias(string empresa, IReadOnlyCollection<string> productos)
        {
            var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = Limpios(productos);
            for (int i = 0; i < lista.Count; i += LOTE_PRODUCTOS)
            {
                List<string> lote = lista.Skip(i).Take(LOTE_PRODUCTOS).ToList();
                List<SqlParameter> parametros = Parametros("@p", lote, 15);
                parametros.Add(new SqlParameter("@empresa", SqlDbType.VarChar, 3) { Value = empresa });
                string sql = string.Format(SQL_FAMILIAS, string.Join(", ", lote.Select((_, n) => "@p" + n)));
                foreach (FamiliaProductoFila f in await db.Database.SqlQuery<FamiliaProductoFila>(sql, parametros.Cast<object>().ToArray()).ToListAsync().ConfigureAwait(false))
                {
                    if (f?.Producto != null)
                    {
                        resultado[f.Producto.Trim()] = f.Familia?.Trim();
                    }
                }
            }
            return resultado;
        }

        private class FamiliaProductoFila
        {
            public string Producto { get; set; }
            public string Familia { get; set; }
        }

        // varchar como la columna (char): con nvarchar la conversión implícita podría echar a perder la búsqueda por índice
        private static List<SqlParameter> Parametros(string prefijo, IReadOnlyList<string> valores, int longitud)
            => valores.Select((v, n) => new SqlParameter(prefijo + n, SqlDbType.VarChar, longitud) { Value = v }).ToList();

        private static List<string> Limpios(IEnumerable<string> valores)
            => (valores ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
