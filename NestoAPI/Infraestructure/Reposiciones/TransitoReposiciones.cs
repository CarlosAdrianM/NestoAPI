using NestoAPI.Infraestructure.Exceptions;
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
    /// Nesto#510: unidades de un producto que viajan HACIA un almacén en una reposición. Nesto lo enseña antes de cambiar
    /// el almacén de un pedido entero: puede que esa reposición se pidiera precisamente para ese pedido.
    /// </summary>
    public class ProductoEnTransitoDTO
    {
        public string Producto { get; set; }
        public int Unidades { get; set; }
        /// <summary>Número de traspaso; null si la reposición aún está en preparación en el origen.</summary>
        public int? Traspaso { get; set; }
        /// <summary>Almacén de donde sale (PreExtrProducto.Delegación).</summary>
        public string Origen { get; set; }
    }

    public interface IRepositorioTransitoReposiciones
    {
        Task<List<ProductoEnTransitoDTO>> LeerEnTransito(string empresa, string almacen, IReadOnlyList<string> productos);
    }

    public class RepositorioTransitoReposicionesSql : IRepositorioTransitoReposiciones
    {
        private readonly NVEntities db;

        public RepositorioTransitoReposicionesSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Dos casos, los mismos que leen la recepción (RecepcionReposicionesAlmacen) y la preparación
        /// (PreparacionReposicion.SQL_LINEAS_EN_PREPARACION):
        /// <list type="bullet">
        /// <item>Pendiente de recibir: con NºTraspaso, en el DiarioEntradaRep del almacén destino, sin contabilizar.</item>
        /// <item>En preparación: sin NºTraspaso, en el DiarioSalidaRep de otro almacén (el origen) con Almacén = destino y
        /// Estado ≥ 0.</item>
        /// </list>
        /// Solo cantidades positivas (la salida negativa del origen va al almacén de origen y no entra). El origen es la
        /// Delegación de la línea (así la graban Nesto viejo y la API). {0} = la lista de parámetros de productos.
        /// </summary>
        internal const string SQL_EN_TRANSITO = @"
SELECT RTRIM(p.[Número]) AS Producto, CAST(SUM(p.Cantidad) AS int) AS Unidades,
       CAST(NULLIF(ISNULL(p.[NºTraspaso], 0), 0) AS int) AS Traspaso, RTRIM(p.[Delegación]) AS Origen
FROM PreExtrProducto p
WHERE p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.Cantidad > 0 AND p.[Número] IN ({0})
  AND ((ISNULL(p.[NºTraspaso], 0) > 0
        AND EXISTS (SELECT 1 FROM Almacenes d WHERE d.Empresa = p.Empresa AND d.[Número] = p.[Almacén] AND d.DiarioEntradaRep = p.Diario))
    OR (ISNULL(p.[NºTraspaso], 0) = 0 AND p.Estado >= 0
        AND EXISTS (SELECT 1 FROM Almacenes o WHERE o.Empresa = p.Empresa AND o.[Número] <> p.[Almacén] AND o.DiarioSalidaRep = p.Diario)))
GROUP BY p.[Número], p.[NºTraspaso], p.[Delegación]
ORDER BY p.[Número], p.[NºTraspaso]";

        internal static string Sql(int numeroProductos)
        {
            return string.Format(SQL_EN_TRANSITO, string.Join(", ", Enumerable.Range(0, numeroProductos).Select(i => "@prod" + i)));
        }

        public Task<List<ProductoEnTransitoDTO>> LeerEnTransito(string empresa, string almacen, IReadOnlyList<string> productos)
        {
            var parametros = new List<object>
            {
                new SqlParameter("@p0", SqlDbType.Char, 3) { Value = empresa },
                new SqlParameter("@p1", SqlDbType.Char, 3) { Value = almacen }
            };
            for (int i = 0; i < productos.Count; i++)
            {
                parametros.Add(new SqlParameter("@prod" + i, SqlDbType.Char, 15) { Value = productos[i] });
            }
            return db.Database.SqlQuery<ProductoEnTransitoDTO>(Sql(productos.Count), parametros.ToArray()).ToListAsync();
        }
    }

    public interface IServicioTransitoReposiciones
    {
        Task<List<ProductoEnTransitoDTO>> LeerEnTransito(string empresa, string almacen, string productos);
    }

    public class ServicioTransitoReposiciones : IServicioTransitoReposiciones
    {
        /// <summary>Un pedido no tiene tantas líneas; el tope evita una consulta con miles de parámetros.</summary>
        public const int MAXIMO_PRODUCTOS = 500;

        private readonly IRepositorioTransitoReposiciones repositorio;

        public ServicioTransitoReposiciones(NVEntities db) : this(new RepositorioTransitoReposicionesSql(db))
        {
        }

        internal ServicioTransitoReposiciones(IRepositorioTransitoReposiciones repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <param name="productos">Lista separada por comas («45146,45148»).</param>
        public async Task<List<ProductoEnTransitoDTO>> LeerEnTransito(string empresa, string almacen, string productos)
        {
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            if (string.IsNullOrWhiteSpace(almacen))
            {
                throw new NestoBusinessException("Falta el almacén.");
            }
            almacen = almacen.Trim().ToUpperInvariant();
            List<string> lista = (productos ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!lista.Any())
            {
                return new List<ProductoEnTransitoDTO>();
            }
            if (lista.Count > MAXIMO_PRODUCTOS)
            {
                throw new NestoBusinessException($"Demasiados productos ({lista.Count}): como mucho {MAXIMO_PRODUCTOS}.");
            }

            List<ProductoEnTransitoDTO> filas = await repositorio.LeerEnTransito(empresa, almacen, lista).ConfigureAwait(false);
            foreach (ProductoEnTransitoDTO fila in filas)
            {
                fila.Producto = fila.Producto?.Trim();
                fila.Origen = fila.Origen?.Trim();
            }
            return filas.Where(f => f.Unidades > 0).ToList();
        }
    }
}
