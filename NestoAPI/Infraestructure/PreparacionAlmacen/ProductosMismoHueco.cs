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
    /// <summary>Un producto con stock en un hueco (una fila de la consulta de <see cref="RepositorioProductosMismoHuecoSql"/>).</summary>
    public class ProductoEnHueco
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        /// <summary>Productos.CodBarras (el principal), tal cual: lo filtra <see cref="ProductosMismoHueco.UltimasCifras"/>.</summary>
        public string CodigoBarras { get; set; }
    }

    public interface IRepositorioProductosMismoHueco
    {
        /// <summary>
        /// Los productos con stock en esos huecos del almacén, en UNA consulta. Como mucho <paramref name="porHueco"/> por
        /// hueco (por número de producto).
        /// </summary>
        Task<List<ProductoEnHueco>> LeerProductos(string empresa, string almacen, IReadOnlyCollection<UbicacionAlmacenDTO> huecos, int porHueco);
    }

    /// <summary>
    /// Ariadna#47 (Carlos, 19/10/26): los demás productos con stock en el hueco de cada línea, para que el mozo distinga los
    /// que se parecen. Una consulta por respuesta para todos los huecos; si falla, las líneas salen sin el campo (ELMAH).
    /// </summary>
    public static class ProductosMismoHueco
    {
        /// <summary>
        /// Lo que se manda de cada hueco, como mucho (sin contar el propio producto de la línea): los que más se le parecen
        /// (ver <see cref="Ordenar"/>).
        /// </summary>
        public const int LIMITE_POR_HUECO = 10;
        /// <summary>
        /// Lo que se lee de cada hueco, como mucho, para elegir de ahí los que más se parecen a cada línea. Hay huecos con más
        /// de 60 productos (002/007/008, 19/10/26): cortar por número en la consulta podría dejar fuera justo el que se confunde.
        /// </summary>
        public const int LECTURA_POR_HUECO = 100;
        /// <summary>Huecos por consulta: 3 parámetros cada uno, muy por debajo de los 2.100 de SQL Server. Los de más salen sin el campo.</summary>
        public const int MAXIMO_HUECOS = 500;
        /// <summary>Cuántas cifras del código de barras se enseñan en grande.</summary>
        public const int CIFRAS = 5;

        public static async Task Completar(IRepositorioProductosMismoHueco repositorio, string empresa, string almacen,
            IEnumerable<IConProductosMismoHueco> lineas)
        {
            if (repositorio == null || string.IsNullOrWhiteSpace(almacen))
            {
                return;
            }
            List<IConProductosMismoHueco> lista = (lineas ?? Enumerable.Empty<IConProductosMismoHueco>()).Where(l => l != null).ToList();
            List<UbicacionAlmacenDTO> huecos = HuecosAConsultar(lista);
            if (huecos.Count == 0)
            {
                return;
            }
            try
            {
                List<ProductoEnHueco> filas = await repositorio.LeerProductos(empresa, almacen.Trim(), huecos, LECTURA_POR_HUECO).ConfigureAwait(false);
                Poner(lista, filas, huecos);
            }
            catch (Exception ex)
            {
                // Es una ayuda: sin ella las líneas salen igual (la app compara con las de su propia lista)
                ElmahHelper.Log(new Exception($"Ariadna#47: no se han podido leer los productos del mismo hueco ({almacen}, {huecos.Count} huecos).", ex));
            }
        }

        /// <summary>Los huecos distintos de las líneas, como mucho <see cref="MAXIMO_HUECOS"/>.</summary>
        internal static List<UbicacionAlmacenDTO> HuecosAConsultar(IEnumerable<IConProductosMismoHueco> lineas)
        {
            return (lineas ?? Enumerable.Empty<IConProductosMismoHueco>())
                .Where(l => l != null)
                .Select(l => l.HuecoDeReferencia)
                .Where(h => h != null)
                .GroupBy(Clave, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(MAXIMO_HUECOS)
                .ToList();
        }

        /// <summary>
        /// Pone a cada línea con hueco los demás productos de ese hueco: sin el propio, sin repetir, los que más se le parecen
        /// primero (<see cref="Ordenar"/>) y como mucho <see cref="LIMITE_POR_HUECO"/>. Una línea con hueco consultado y nadie más en él lleva la lista vacía;
        /// una sin hueco (o con un hueco que no se ha consultado) se queda como estaba.
        /// </summary>
        internal static void Poner(IEnumerable<IConProductosMismoHueco> lineas, IEnumerable<ProductoEnHueco> filas,
            IEnumerable<UbicacionAlmacenDTO> consultados)
        {
            var huecosConsultados = new HashSet<string>((consultados ?? Enumerable.Empty<UbicacionAlmacenDTO>()).Select(Clave), StringComparer.OrdinalIgnoreCase);
            List<ProductoEnHueco> validas = (filas ?? Enumerable.Empty<ProductoEnHueco>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Producto))
                .ToList();
            ILookup<string, ProductoEnHueco> porHueco = validas
                .ToLookup(f => Clave(UbicacionAlmacenDTO.De(f.Pasillo, f.Fila, f.Columna)), StringComparer.OrdinalIgnoreCase);
            // El nombre de cada producto de las líneas, si está en alguno de los huecos leídos (lo normal: está en el suyo)
            Dictionary<string, string> nombres = validas
                .Where(f => !string.IsNullOrWhiteSpace(f.Nombre))
                .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Nombre, StringComparer.OrdinalIgnoreCase);

            foreach (IConProductosMismoHueco linea in (lineas ?? Enumerable.Empty<IConProductosMismoHueco>()).Where(l => l != null))
            {
                UbicacionAlmacenDTO hueco = linea.HuecoDeReferencia;
                string clave = hueco == null ? null : Clave(hueco);
                if (clave == null || !huecosConsultados.Contains(clave))
                {
                    continue;
                }
                string propio = linea.Producto?.Trim() ?? string.Empty;
                IEnumerable<ProductoEnHueco> otros = porHueco[clave]
                    .Where(f => !string.Equals(f.Producto.Trim(), propio, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First());
                linea.ProductosMismoHueco = Ordenar(propio, nombres.TryGetValue(propio, out string nombrePropio) ? nombrePropio : null, otros)
                    .Take(LIMITE_POR_HUECO)
                    .Select(f => new ProductoMismoHuecoDTO
                    {
                        Producto = f.Producto.Trim(),
                        Nombre = Limpio(f.Nombre),
                        Tamano = f.Tamano,
                        UnidadMedida = Limpio(f.UnidadMedida),
                        UltimasCifras = UltimasCifras(f.CodigoBarras)
                    })
                    .ToList();
            }
        }

        /// <summary>
        /// Los que más se parecen al producto de la línea, primero: los que comparten más palabras del nombre (sin tildes ni
        /// mayúsculas: «SERUM LEVEL LASH» y «MÁSCARA LEVEL LASH» comparten dos); a igualdad, el número de producto más cercano
        /// (los de una misma línea de producto se dan de alta seguidos: 44724 y 44726); y al final, por número.
        /// </summary>
        internal static IEnumerable<ProductoEnHueco> Ordenar(string producto, string nombre, IEnumerable<ProductoEnHueco> otros)
        {
            HashSet<string> palabras = Palabras(nombre);
            bool numerico = long.TryParse(producto?.Trim(), out long numero);
            return (otros ?? Enumerable.Empty<ProductoEnHueco>())
                .OrderByDescending(f => palabras.Count == 0 ? 0 : Palabras(f.Nombre).Count(palabras.Contains))
                .ThenBy(f => numerico && long.TryParse(f.Producto.Trim(), out long otro) ? Math.Abs(otro - numero) : long.MaxValue)
                .ThenBy(f => f.Producto.Trim(), StringComparer.Ordinal);
        }

        /// <summary>Las palabras de un nombre (dos letras o más, o cualquier número), sin tildes y en mayúsculas.</summary>
        internal static HashSet<string> Palabras(string nombre)
        {
            var resultado = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return resultado;
            }
            string sinTildes = new string(nombre.Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray()).ToUpperInvariant();
            foreach (string palabra in System.Text.RegularExpressions.Regex.Split(sinTildes, "[^A-Z0-9]+"))
            {
                if (palabra.Length >= 2 || (palabra.Length == 1 && char.IsDigit(palabra[0])))
                {
                    _ = resultado.Add(palabra);
                }
            }
            return resultado;
        }

        /// <summary>Las <see cref="CIFRAS"/> últimas cifras del código, solo si es un GTIN válido (NestoAPI#674); si no, null.</summary>
        public static string UltimasCifras(string codigoBarras)
        {
            string valido = Productos.ValidadorGtin.SoloSiValido(codigoBarras);
            return valido == null ? null : valido.Substring(valido.Length - CIFRAS);
        }

        /// <summary>Pasillo, fila y columna, sin ceros de más ni de menos: «1/2/4» y «001/002/004» son el mismo hueco.</summary>
        internal static string Clave(UbicacionAlmacenDTO hueco)
        {
            if (hueco == null)
            {
                return null;
            }
            string Parte(string p) => p?.Trim().TrimStart('0') ?? string.Empty;
            return $"{Parte(hueco.Pasillo)}/{Parte(hueco.Fila)}/{Parte(hueco.Columna)}";
        }

        private static string Limpio(string texto)
        {
            return string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        }
    }

    /// <summary>
    /// Ariadna#47: qué hay en cada hueco. Cuenta como stock en el hueco lo que está físicamente en él: libre (0) y reservado
    /// para un picking (3), una reposición (4) o un kit (5) (<see cref="NestoAPI.Infraestructure.Ubicaciones.EstadosUbicacion"/>), con suma positiva.
    /// Lo pendiente de ubicar (2) no tiene hueco, y lo negativo ya ha salido.
    /// </summary>
    public class RepositorioProductosMismoHuecoSql : IRepositorioProductosMismoHueco, IDisposable
    {
        // Ubicaciones no tiene índice por almacén y hueco: el plan recorre el índice estrecho (Número, Empresa, Estado,
        // Pasillo, Fila, Columna), sea cuantos sean los huecos. Medido en producción con sp_executesql (19/10/26): ~42.000
        // lecturas lógicas, ~340 ms de CPU. Va en paralelo con la ficha. Un índice (Empresa, Almacén, Pasillo, Fila, Columna)
        // INCLUDE (Número, Estado, Cantidad) lo dejaría en milisegundos. NOLOCK: es una ayuda de lectura y no debe esperar a nadie.
        internal const string SQL_PRODUCTOS_EN_HUECOS = @"
WITH Huecos (Pasillo, Fila, Columna) AS (
    SELECT v.Pasillo, v.Fila, v.Columna FROM (VALUES {0}) v (Pasillo, Fila, Columna)
), EnHueco AS (
    SELECT u.Pasillo, u.Fila, u.Columna, u.[Número] AS Producto
    FROM Huecos h
         INNER JOIN Ubicaciones u WITH (NOLOCK) ON u.Pasillo = h.Pasillo AND u.Fila = h.Fila AND u.Columna = h.Columna
    WHERE u.Empresa = @p0 AND u.[Almacén] = @p1 AND u.Estado IN (0, 3, 4, 5)
    GROUP BY u.Pasillo, u.Fila, u.Columna, u.[Número]
    HAVING SUM(u.Cantidad) > 0
), Numerados AS (
    SELECT e.Pasillo, e.Fila, e.Columna, e.Producto,
           ROW_NUMBER() OVER (PARTITION BY e.Pasillo, e.Fila, e.Columna ORDER BY e.Producto) AS Puesto
    FROM EnHueco e
)
SELECT RTRIM(n.Pasillo) AS Pasillo, RTRIM(n.Fila) AS Fila, RTRIM(n.Columna) AS Columna, RTRIM(n.Producto) AS Producto,
       RTRIM(p.Nombre) AS Nombre, p.[Tamaño] AS Tamano, RTRIM(p.UnidadMedida) AS UnidadMedida, RTRIM(p.CodBarras) AS CodigoBarras
FROM Numerados n
     LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = @p0 AND p.[Número] = n.Producto
WHERE n.Puesto <= @p2";

        private readonly Database baseDeDatos;
        private readonly NVEntities contextoPropio;

        public RepositorioProductosMismoHuecoSql(Database baseDeDatos)
        {
            this.baseDeDatos = baseDeDatos ?? throw new ArgumentNullException(nameof(baseDeDatos));
        }

        private RepositorioProductosMismoHuecoSql(NVEntities db) : this(db.Database)
        {
            contextoPropio = db;
        }

        public static RepositorioProductosMismoHuecoSql ConContextoPropio()
        {
            return new RepositorioProductosMismoHuecoSql(new NVEntities());
        }

        public Task<List<ProductoEnHueco>> LeerProductos(string empresa, string almacen, IReadOnlyCollection<UbicacionAlmacenDTO> huecos, int porHueco)
        {
            List<UbicacionAlmacenDTO> lista = (huecos ?? new List<UbicacionAlmacenDTO>()).Where(h => h != null).Take(ProductosMismoHueco.MAXIMO_HUECOS).ToList();
            if (lista.Count == 0)
            {
                return Task.FromResult(new List<ProductoEnHueco>());
            }
            // Tipados como las columnas (char): con nvarchar SQL Server convertiría cada fila y no usaría el índice
            var parametros = new List<object>
            {
                Char("@p0", empresa, 3),
                Char("@p1", almacen, 3),
                new SqlParameter("@p2", System.Data.SqlDbType.Int) { Value = porHueco }
            };
            var valores = new List<string>();
            for (int i = 0; i < lista.Count; i++)
            {
                int n = 3 + i * 3;
                parametros.Add(Char("@p" + n, Hueco(lista[i].Pasillo), 3));
                parametros.Add(Char("@p" + (n + 1), Hueco(lista[i].Fila), 3));
                parametros.Add(Char("@p" + (n + 2), Hueco(lista[i].Columna), 3));
                valores.Add($"(@p{n}, @p{n + 1}, @p{n + 2})");
            }
            return baseDeDatos.SqlQuery<ProductoEnHueco>(string.Format(SQL_PRODUCTOS_EN_HUECOS, string.Join(", ", valores)), parametros.ToArray())
                .ToListAsync();
        }

        /// <summary>En la tabla las tres partes van con tres cifras (001/002/004).</summary>
        private static string Hueco(string parte)
        {
            string limpio = parte?.Trim() ?? string.Empty;
            return limpio.Length > 0 && limpio.Length < 3 && limpio.All(char.IsDigit) ? limpio.PadLeft(3, '0') : limpio;
        }

        private static SqlParameter Char(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, System.Data.SqlDbType.Char, longitud) { Value = (object)valor ?? DBNull.Value };
        }

        public void Dispose()
        {
            contextoPropio?.Dispose();
        }
    }
}
