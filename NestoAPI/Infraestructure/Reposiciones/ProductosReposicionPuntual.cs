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
        internal const string SQL_FICHA = @"
SELECT RTRIM(pr.[Número]) AS Producto, RTRIM(pr.Nombre) AS Nombre, RTRIM(pr.CodBarras) AS CodigoBarras, 1 AS UnidadesPorLectura,
       CAST(ISNULL((SELECT SUM(e.Cantidad) FROM ExtractoProducto e
                    WHERE e.Empresa = pr.Empresa AND e.[Número] = pr.[Número] AND e.[Almacén] = @p1), 0) AS int) AS StockOrigen
FROM Productos pr
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

        private static async Task<ProductoReposicionPuntualDTO> LeerFicha(NVEntities db, string empresa, string origen, string producto)
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
}
