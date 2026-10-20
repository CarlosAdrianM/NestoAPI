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
        /// <summary>NestoAPI#654: para la etiqueta de código de barras del producto (aquí no se completa: ya lo trae cada lectura).</summary>
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string Subgrupo { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        /// <summary>NestoAPI#681: ProveedoresProducto.ReferenciaProv del proveedor principal (Orden = 1). Null si no hay.</summary>
        public string ReferenciaProveedor { get; set; }
    }

    public interface IRepositorioFichasProducto
    {
        Task<List<FichaProductoAlmacen>> LeerFichas(string empresa, IReadOnlyCollection<string> productos);
    }

    public interface IFichasProductoAlmacen
    {
        /// <summary>
        /// Rellena familia, subgrupo, tamaño y unidad de medida de todos los productos con una sola consulta, y la foto
        /// (Ariadna#2) con su caché.
        /// </summary>
        Task Completar(string empresa, IEnumerable<IConFichaProducto> productos);

        /// <summary>NestoAPI#605: solo los códigos de barras activos de cada línea (para las que no llevan ficha).</summary>
        Task CompletarCodigos(string empresa, IEnumerable<IConCodigosBarras> lineas);

        /// <summary>
        /// NestoAPI#653 (Ariadna#35): solo la foto, para las líneas que no llevan ficha (reposiciones, diferencias). Una
        /// llamada para todos los productos a la vez, con la misma caché que <see cref="Completar"/>.
        /// </summary>
        Task CompletarFotos(IEnumerable<IConFotoProducto> productos);

        /// <summary>
        /// Ariadna#47: los demás productos con stock en el hueco de cada línea, del almacén <paramref name="almacen"/>. Una
        /// consulta para todos los huecos; nunca falla: si no se puede leer, las líneas se quedan sin el campo (ELMAH).
        /// </summary>
        Task CompletarMismoHueco(string empresa, string almacen, IEnumerable<IConProductosMismoHueco> lineas);

        /// <summary>
        /// NestoAPI#681: solo la referencia del proveedor principal, para las líneas que no llevan ficha (reposiciones,
        /// diferencias). Una consulta para todos los productos a la vez (la misma de <see cref="Completar"/>).
        /// </summary>
        Task CompletarReferenciasProveedor(string empresa, IEnumerable<IConReferenciaProveedor> productos);
    }

    /// <summary>
    /// Ariadna (03/10/26): el único sitio que resuelve la ficha de los productos que ve el mozo (recorrido, ubicar,
    /// buscar, recibir). Cada lectura sigue con su consulta; esto se añade al final, así que no hay un JOIN a
    /// Familias y SubGruposProducto repetido en cada una.
    /// </summary>
    public class FichasProductoAlmacen : IFichasProductoAlmacen
    {
        private readonly IRepositorioFichasProducto repositorio;
        private readonly IFotosProductoAlmacen fotos;
        private readonly Productos.IRepositorioCodigosBarras codigos;
        private readonly IRepositorioProductosMismoHueco mismoHueco;

        /// <param name="fotos">Sin él, sin foto.</param>
        /// <param name="codigos">NestoAPI#605: sin él, CodigosBarras lleva solo el principal.</param>
        /// <param name="mismoHueco">Ariadna#47: sin él, las líneas no llevan los demás productos del hueco.</param>
        public FichasProductoAlmacen(IRepositorioFichasProducto repositorio, IFotosProductoAlmacen fotos = null,
            Productos.IRepositorioCodigosBarras codigos = null, IRepositorioProductosMismoHueco mismoHueco = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.fotos = fotos;
            this.codigos = codigos;
            this.mismoHueco = mismoHueco;
        }

        public Task CompletarMismoHueco(string empresa, string almacen, IEnumerable<IConProductosMismoHueco> lineas)
        {
            return ProductosMismoHueco.Completar(mismoHueco, empresa, almacen, lineas);
        }

        public Task CompletarCodigos(string empresa, IEnumerable<IConCodigosBarras> lineas)
        {
            return Productos.CompletadorCodigosBarras.Completar(codigos, empresa, lineas);
        }

        public async Task CompletarFotos(IEnumerable<IConFotoProducto> productos)
        {
            List<IConFotoProducto> lista = (productos ?? Enumerable.Empty<IConFotoProducto>()).Where(p => p != null).ToList();
            List<string> numeros = Numeros(lista);
            if (numeros.Count == 0 || fotos == null)
            {
                return;
            }
            PonerFotos(lista, await fotos.Urls(numeros).ConfigureAwait(false));
        }

        public async Task Completar(string empresa, IEnumerable<IConFichaProducto> productos)
        {
            List<IConFichaProducto> lista = (productos ?? Enumerable.Empty<IConFichaProducto>()).Where(p => p != null).ToList();
            List<string> numeros = Numeros(lista);
            if (numeros.Count == 0)
            {
                return;
            }
            await CompletarCodigos(empresa, lista.OfType<IConCodigosBarras>()).ConfigureAwait(false);

            // La foto va a la tienda: se pregunta a la vez que se lee la ficha
            Task<IDictionary<string, string>> urls = fotos == null
                ? Task.FromResult<IDictionary<string, string>>(new Dictionary<string, string>())
                : fotos.Urls(numeros);
            Dictionary<string, FichaProductoAlmacen> fichas = PorProducto(await repositorio.LeerFichas(empresa, numeros).ConfigureAwait(false));

            PonerFotos(lista, await urls.ConfigureAwait(false));

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
                producto.ReferenciaProveedor = Limpio(ficha.ReferenciaProveedor); // NestoAPI#681
            }
        }

        public async Task CompletarReferenciasProveedor(string empresa, IEnumerable<IConReferenciaProveedor> productos)
        {
            List<IConReferenciaProveedor> lista = (productos ?? Enumerable.Empty<IConReferenciaProveedor>()).Where(p => p != null).ToList();
            List<string> numeros = lista
                .Select(p => p.Producto?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (numeros.Count == 0)
            {
                return;
            }
            Dictionary<string, FichaProductoAlmacen> fichas = PorProducto(await repositorio.LeerFichas(empresa, numeros).ConfigureAwait(false));
            foreach (IConReferenciaProveedor producto in lista)
            {
                if (producto.Producto != null && fichas.TryGetValue(producto.Producto.Trim(), out FichaProductoAlmacen ficha))
                {
                    producto.ReferenciaProveedor = Limpio(ficha.ReferenciaProveedor);
                }
            }
        }

        private static Dictionary<string, FichaProductoAlmacen> PorProducto(IEnumerable<FichaProductoAlmacen> fichas)
        {
            return (fichas ?? new List<FichaProductoAlmacen>())
                .Where(f => !string.IsNullOrWhiteSpace(f?.Producto))
                .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        private static List<string> Numeros(IEnumerable<IConFotoProducto> productos)
        {
            return productos
                .Select(p => p.Producto?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>El único sitio que pone la foto de almacén en lo que ve el mozo.</summary>
        private static void PonerFotos(IEnumerable<IConFotoProducto> productos, IDictionary<string, string> fotosPorProducto)
        {
            if (fotosPorProducto == null)
            {
                return;
            }
            foreach (IConFotoProducto producto in productos)
            {
                if (producto.Producto != null && fotosPorProducto.TryGetValue(producto.Producto.Trim(), out string url))
                {
                    producto.UrlFoto = url;
                }
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
       RTRIM(p.Nombre) AS Nombre,
       RTRIM(f.[Descripción]) AS Familia,
       RTRIM(s.[Descripción]) AS Subgrupo,
       p.[Tamaño] AS Tamano,
       RTRIM(p.UnidadMedida) AS UnidadMedida,
       NULLIF(RTRIM(pp.ReferenciaProv), '') AS ReferenciaProveedor
FROM Productos p
     LEFT JOIN Familias f ON f.Empresa = p.Empresa AND f.[Número] = p.Familia
     LEFT JOIN SubGruposProducto s ON s.Empresa = p.Empresa AND s.Grupo = p.Grupo AND s.[Número] = p.SubGrupo
     OUTER APPLY (SELECT TOP 1 r.ReferenciaProv
                  FROM ProveedoresProducto r
                  WHERE r.Empresa = p.Empresa AND r.[Nº Producto] = p.[Número] AND r.Orden = 1
                  ORDER BY r.[Nº Proveedor], r.ContactoProveedor) pp
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
