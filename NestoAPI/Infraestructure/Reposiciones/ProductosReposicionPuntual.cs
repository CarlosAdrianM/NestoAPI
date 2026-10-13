using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// Sugerencia 581: un producto que la tienda puede meter en una reposición puntual a Algete, con lo que hace falta para
    /// enseñarlo antes de mandarlo (GET api/Reposiciones/Puntual/Producto).
    /// </summary>
    public class ProductoReposicionPuntualDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>El código de barras principal de la ficha (Productos.CodBarras).</summary>
        public string CodigoBarras { get; set; }
        /// <summary>
        /// Las unidades que vale una lectura del código leído (NestoAPI#605: el de una caja de 6 vale 6). 1 si se ha buscado
        /// por la referencia.
        /// </summary>
        public int UnidadesPorLectura { get; set; } = 1;
        /// <summary>Stock del producto en la tienda (ExtractoProducto del almacén de origen).</summary>
        public int StockOrigen { get; set; }
        /// <summary>
        /// Sugerencia 581 (Carlos, 13/10/26): lo que la tienda necesita para lo suyo y no puede mandar: sus líneas de pedido
        /// pendientes en ese almacén (Estado -1 a 1, empresa y su espejo) y lo que ya ha salido numerado sin contabilizar. El
        /// mismo criterio que la propuesta de reposición (prdRellenarReposicionStock2) para el stock del origen.
        /// </summary>
        public int Comprometido { get; set; }
        /// <summary>Sugerencia 581: lo que de verdad se puede mandar: StockOrigen − Comprometido (nunca negativo).</summary>
        public int Disponible { get; set; }
    }

    public interface IProductosReposicionPuntual
    {
        /// <summary>
        /// El producto con esa referencia o, si no hay ninguno, los que tienen ese código de barras (cualquiera de sus
        /// códigos activos, NestoAPI#605), con el stock de <paramref name="origen"/>. Vacía si no hay ninguno; varios si el
        /// código lo comparten varios productos (que elija).
        /// </summary>
        Task<List<ProductoReposicionPuntualDTO>> Buscar(string empresa, string origen, string codigo);
    }

    /// <summary>Sugerencia 581: buscar lo que se lee (o teclea) en la pantalla de la reposición puntual.</summary>
    public class ProductosReposicionPuntual : IProductosReposicionPuntual
    {
        // Ficha y stock en la tienda (índice extractoproducto11: Empresa, Número, Almacén, Cantidad). Solo productos activos.
        // Sugerencia 581: y lo comprometido (pedidos pendientes de la tienda y salidas numeradas sin contabilizar), con lo que
        // queda libre para mandar. Comprobada contra NV el 13/10/26 (94 ms).
        internal const string SQL_FICHA = @"
SELECT RTRIM(pr.[Número]) AS Producto, RTRIM(pr.Nombre) AS Nombre, RTRIM(pr.CodBarras) AS CodigoBarras, 1 AS UnidadesPorLectura,
       s.StockOrigen, c.Comprometido,
       CASE WHEN s.StockOrigen - c.Comprometido > 0 THEN s.StockOrigen - c.Comprometido ELSE 0 END AS Disponible
FROM Productos pr WITH (NOLOCK)
     CROSS APPLY (SELECT CAST(ISNULL((SELECT SUM(e.Cantidad) FROM ExtractoProducto e WITH (NOLOCK)
                                      WHERE e.Empresa = pr.Empresa AND e.[Número] = pr.[Número] AND e.[Almacén] = @p1), 0) AS int) AS StockOrigen) s
     CROSS APPLY (SELECT CAST(ISNULL((SELECT SUM(l.Cantidad) FROM LinPedidoVta l WITH (NOLOCK)
                                      WHERE l.Empresa IN (pr.Empresa, (SELECT em.[IVA por defecto] FROM Empresas em WITH (NOLOCK) WHERE em.[Número] = pr.Empresa))
                                        AND l.Producto = pr.[Número] AND l.[Almacén] = @p1 AND l.Estado BETWEEN -1 AND 1), 0)
                            + ISNULL((SELECT -SUM(p.Cantidad) FROM PreExtrProducto p WITH (NOLOCK)
                                      WHERE p.Empresa = pr.Empresa AND p.[Número] = pr.[Número] AND p.[Almacén] = @p1
                                        AND ISNULL(p.[NºTraspaso], 0) <> 0 AND p.Cantidad < 0), 0) AS int) AS Comprometido) c
WHERE pr.Empresa = @p0 AND pr.[Número] = @p2 AND pr.Estado >= 0";

        private readonly Func<string, string, string, Task<ProductoReposicionPuntualDTO>> leerFicha;
        private readonly Func<string, string, Task<List<ProductoPorCodigoBarrasDTO>>> porCodigoBarras;

        public ProductosReposicionPuntual(NVEntities db)
            : this((empresa, origen, producto) => LeerFicha(db, empresa, origen, producto),
                   (empresa, codigo) => new ServicioCodigosBarras(new RepositorioCodigosBarras(db)).BuscarPorCodigo(empresa, codigo))
        {
        }

        /// <param name="leerFicha">(empresa, origen, referencia) → la ficha con el stock del origen; null si no existe.</param>
        /// <param name="porCodigoBarras">(empresa, código) → los productos con ese código de barras (NestoAPI#605).</param>
        internal ProductosReposicionPuntual(Func<string, string, string, Task<ProductoReposicionPuntualDTO>> leerFicha,
            Func<string, string, Task<List<ProductoPorCodigoBarrasDTO>>> porCodigoBarras)
        {
            this.leerFicha = leerFicha ?? throw new ArgumentNullException(nameof(leerFicha));
            this.porCodigoBarras = porCodigoBarras ?? throw new ArgumentNullException(nameof(porCodigoBarras));
        }

        public async Task<List<ProductoReposicionPuntualDTO>> Buscar(string empresa, string origen, string codigo)
        {
            string limpio = ServicioCodigosBarras.Limpiar(codigo);
            var encontrados = new List<ProductoReposicionPuntualDTO>();
            if (limpio == null)
            {
                return encontrados;
            }
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            origen = origen?.Trim().ToUpperInvariant();

            // Primero la referencia (es lo que se teclea); el código de barras, si no es una referencia
            if (limpio.Length <= 15)
            {
                ProductoReposicionPuntualDTO porReferencia = await leerFicha(empresa, origen, limpio).ConfigureAwait(false);
                if (porReferencia != null)
                {
                    encontrados.Add(porReferencia);
                    return encontrados;
                }
            }
            foreach (ProductoPorCodigoBarrasDTO conCodigo in (await porCodigoBarras(empresa, limpio).ConfigureAwait(false)) ?? new List<ProductoPorCodigoBarrasDTO>())
            {
                ProductoReposicionPuntualDTO ficha = await leerFicha(empresa, origen, conCodigo.Producto?.Trim()).ConfigureAwait(false);
                if (ficha != null)
                {
                    ficha.UnidadesPorLectura = Math.Max(1, conCodigo.Cantidad);
                    encontrados.Add(ficha);
                }
            }
            return encontrados;
        }

        internal static async Task<ProductoReposicionPuntualDTO> LeerFicha(NVEntities db, string empresa, string origen, string producto)
        {
            if (string.IsNullOrWhiteSpace(producto) || producto.Length > 15)
            {
                return null;
            }
            return await db.Database.SqlQuery<ProductoReposicionPuntualDTO>(SQL_FICHA,
                    new SqlParameter("@p0", SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@p1", SqlDbType.Char, 3) { Value = (object)origen ?? DBNull.Value },
                    new SqlParameter("@p2", SqlDbType.Char, 15) { Value = producto })
                .FirstOrDefaultAsync().ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Sugerencia 581 (Carlos, 13/10/26): lo que una tienda puede mandar de cada producto en una reposición puntual sin
    /// dejar sin servir sus propios pedidos (<see cref="ProductoReposicionPuntualDTO.Disponible"/>).
    /// </summary>
    public interface IStockLibreReposicionPuntual
    {
        /// <summary>Producto → su ficha con stock, comprometido y disponible en <paramref name="origen"/>. Los que no existen no vienen.</summary>
        Task<Dictionary<string, ProductoReposicionPuntualDTO>> Leer(string empresa, string origen, IEnumerable<string> productos);
    }

    /// <summary>Sin comprobar lo comprometido (pruebas que no lo miran): no viene ninguno.</summary>
    public sealed class SinStockLibreReposicionPuntual : IStockLibreReposicionPuntual
    {
        public static readonly SinStockLibreReposicionPuntual Instancia = new SinStockLibreReposicionPuntual();

        private SinStockLibreReposicionPuntual()
        {
        }

        public Task<Dictionary<string, ProductoReposicionPuntualDTO>> Leer(string empresa, string origen, IEnumerable<string> productos) =>
            Task.FromResult(new Dictionary<string, ProductoReposicionPuntualDTO>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>La misma consulta que la pantalla (<see cref="ProductosReposicionPuntual.SQL_FICHA"/>), producto a producto (una puntual tiene pocos).</summary>
    public class StockLibreReposicionPuntualSql : IStockLibreReposicionPuntual
    {
        private readonly NVEntities db;

        public StockLibreReposicionPuntualSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<Dictionary<string, ProductoReposicionPuntualDTO>> Leer(string empresa, string origen, IEnumerable<string> productos)
        {
            var leidos = new Dictionary<string, ProductoReposicionPuntualDTO>(StringComparer.OrdinalIgnoreCase);
            foreach (string producto in (productos ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ProductoReposicionPuntualDTO ficha = await ProductosReposicionPuntual.LeerFicha(db, empresa, origen, producto).ConfigureAwait(false);
                if (ficha != null)
                {
                    leidos[producto] = ficha;
                }
            }
            return leidos;
        }
    }
}
