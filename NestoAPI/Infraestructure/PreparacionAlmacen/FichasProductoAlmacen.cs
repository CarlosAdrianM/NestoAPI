using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Lo que se enseña de un producto, además del nombre, para no equivocarse al cogerlo o ubicarlo.</summary>
    public class FichaProductoAlmacen
    {
        public string Producto { get; set; }
        public string Familia { get; set; }
        public string Subgrupo { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
    }

    public interface IRepositorioFichasProducto
    {
        Task<List<FichaProductoAlmacen>> LeerFichas(string empresa, IReadOnlyCollection<string> productos);
    }

    public interface IFichasProductoAlmacen
    {
        /// <summary>Rellena familia, subgrupo, tamaño y unidad de medida de todos los productos con una sola consulta.</summary>
        Task Completar(string empresa, IEnumerable<IConFichaProducto> productos);
    }

    /// <summary>
    /// Ariadna (03/10/26): el único sitio que resuelve la ficha de los productos que ve el mozo (recorrido, ubicar,
    /// buscar, recibir). Cada lectura sigue con su consulta; esto se añade al final, así que no hay un JOIN a
    /// Familias y SubGruposProducto repetido en cada una.
    /// </summary>
    public class FichasProductoAlmacen : IFichasProductoAlmacen
    {
        private readonly IRepositorioFichasProducto repositorio;

        public FichasProductoAlmacen(IRepositorioFichasProducto repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task Completar(string empresa, IEnumerable<IConFichaProducto> productos)
        {
            List<IConFichaProducto> lista = (productos ?? Enumerable.Empty<IConFichaProducto>()).Where(p => p != null).ToList();
            List<string> numeros = lista
                .Select(p => p.Producto?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (numeros.Count == 0)
            {
                return;
            }

            Dictionary<string, FichaProductoAlmacen> fichas = (await repositorio.LeerFichas(empresa, numeros).ConfigureAwait(false) ?? new List<FichaProductoAlmacen>())
                .Where(f => !string.IsNullOrWhiteSpace(f?.Producto))
                .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (IConFichaProducto producto in lista)
            {
                if (producto.Producto == null || !fichas.TryGetValue(producto.Producto.Trim(), out FichaProductoAlmacen ficha))
                {
                    continue;
                }
                producto.Familia = Limpio(ficha.Familia);
                producto.Subgrupo = Limpio(ficha.Subgrupo);
                producto.Tamano = ficha.Tamano ?? producto.Tamano;
                producto.UnidadMedida = Limpio(ficha.UnidadMedida) ?? producto.UnidadMedida;
            }
        }

        private static string Limpio(string texto)
        {
            return string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        }
    }

    public class RepositorioFichasProducto : IRepositorioFichasProducto, IDisposable
    {
        /// <summary>Muy por debajo del límite de 2.100 parámetros de SQL Server.</summary>
        internal const int PRODUCTOS_POR_CONSULTA = 500;

        internal const string SQL_FICHAS = @"
SELECT RTRIM(p.[Número]) AS Producto,
       RTRIM(f.[Descripción]) AS Familia,
       RTRIM(s.[Descripción]) AS Subgrupo,
       p.[Tamaño] AS Tamano,
       RTRIM(p.UnidadMedida) AS UnidadMedida
FROM Productos p
     LEFT JOIN Familias f ON f.Empresa = p.Empresa AND f.[Número] = p.Familia
     LEFT JOIN SubGruposProducto s ON s.Empresa = p.Empresa AND s.Grupo = p.Grupo AND s.[Número] = p.SubGrupo
WHERE p.Empresa = @p0 AND p.[Número] IN ({0})";

        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioFichasProducto(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioFichasProducto ConContextoPropio()
        {
            return new RepositorioFichasProducto(new NVEntities()) { contextoPropio = true };
        }

        public async Task<List<FichaProductoAlmacen>> LeerFichas(string empresa, IReadOnlyCollection<string> productos)
        {
            var resultado = new List<FichaProductoAlmacen>();
            List<string> todos = productos.ToList();
            for (int desde = 0; desde < todos.Count; desde += PRODUCTOS_POR_CONSULTA)
            {
                List<string> tanda = todos.Skip(desde).Take(PRODUCTOS_POR_CONSULTA).ToList();
                // Tipados como las columnas (char): con nvarchar SQL Server convertiría cada fila y no usaría el índice
                var parametros = new List<object> { new SqlParameter("@p0", System.Data.SqlDbType.Char, 3) { Value = empresa } };
                for (int i = 0; i < tanda.Count; i++)
                {
                    parametros.Add(new SqlParameter("@p" + (i + 1), System.Data.SqlDbType.Char, 15) { Value = tanda[i] });
                }
                string lista = string.Join(", ", Enumerable.Range(1, tanda.Count).Select(i => "@p" + i));
                resultado.AddRange(await db.Database.SqlQuery<FichaProductoAlmacen>(string.Format(SQL_FICHAS, lista), parametros.ToArray())
                    .ToListAsync().ConfigureAwait(false));
            }
            return resultado;
        }

        public void Dispose()
        {
            if (contextoPropio)
            {
                db.Dispose();
            }
        }
    }
}
