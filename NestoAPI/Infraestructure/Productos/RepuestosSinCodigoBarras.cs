using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>NestoAPI#762: un producto repuesto en los últimos meses que no tiene ningún código de barras válido.</summary>
    public class ProductoRepuestoSinCodigoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        /// <summary>En cuántas reposiciones (traspasos) ha entrado.</summary>
        public int Reposiciones { get; set; }
        /// <summary>Unidades que han entrado por reposición en el periodo.</summary>
        public int Unidades { get; set; }
        public DateTime? Ultima { get; set; }
    }

    public interface IRepositorioRepuestosSinCodigo
    {
        /// <summary>Los productos activos (no ficticios) que han entrado por reposición desde <paramref name="desde"/>, con o sin código.</summary>
        Task<List<ProductoRepuestoSinCodigoDTO>> LeerRepuestos(string empresa, DateTime desde);
    }

    /// <summary>
    /// NestoAPI#762 (punto 7, limpieza inicial): los productos repuestos en los últimos meses sin código de barras, para darles
    /// su código interno e imprimir sus etiquetas de una vez. «Sin código» = sin ningún GTIN válido activo
    /// (<see cref="CompletadorCodigosBarras.Juntar"/>, la misma regla que Ariadna al leer).
    /// </summary>
    public class InformeRepuestosSinCodigo
    {
        public const int MESES_POR_DEFECTO = 3;
        public const int MESES_MAXIMO = 24;

        private readonly IRepositorioRepuestosSinCodigo repuestos;
        private readonly IRepositorioCodigosBarras codigos;

        public InformeRepuestosSinCodigo(IRepositorioRepuestosSinCodigo repuestos, IRepositorioCodigosBarras codigos)
        {
            this.repuestos = repuestos ?? throw new ArgumentNullException(nameof(repuestos));
            this.codigos = codigos ?? throw new ArgumentNullException(nameof(codigos));
        }

        /// <summary>Los de más unidades primero (los que antes hay que etiquetar).</summary>
        public async Task<List<ProductoRepuestoSinCodigoDTO>> Leer(string empresa, int meses, DateTime hoy)
        {
            if (meses < 1 || meses > MESES_MAXIMO)
            {
                throw new ArgumentException($"Los meses tienen que ir de 1 a {MESES_MAXIMO}.");
            }
            List<ProductoRepuestoSinCodigoDTO> todos = await repuestos.LeerRepuestos(empresa, hoy.Date.AddMonths(-meses)).ConfigureAwait(false)
                ?? new List<ProductoRepuestoSinCodigoDTO>();
            if (todos.Count == 0)
            {
                return todos;
            }
            Dictionary<string, List<string>> activos = await codigos.CodigosActivos(empresa, todos.Select(t => t.Producto).ToList()).ConfigureAwait(false)
                ?? new Dictionary<string, List<string>>();
            return todos
                .Where(t => !string.IsNullOrWhiteSpace(t.Producto))
                .Where(t => CompletadorCodigosBarras.Juntar(null, activos.TryGetValue(t.Producto.Trim(), out List<string> suyos) ? suyos : null).Count == 0)
                .OrderByDescending(t => t.Unidades)
                .ThenBy(t => t.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public class RepositorioRepuestosSinCodigoSql : IRepositorioRepuestosSinCodigo
    {
        // Las ENTRADAS por reposición (cantidad positiva con número de traspaso), sin bloquear: es un informe
        internal const string SQL_REPUESTOS = @"
SELECT RTRIM(e.[Número]) AS Producto, RTRIM(MAX(p.Nombre)) AS Nombre, RTRIM(MAX(p.Familia)) AS Familia,
       COUNT(DISTINCT e.[NºTraspaso]) AS Reposiciones, CAST(SUM(e.Cantidad) AS int) AS Unidades, MAX(e.Fecha) AS Ultima
FROM ExtractoProducto e WITH (NOLOCK)
     JOIN Productos p WITH (NOLOCK) ON p.Empresa = e.Empresa AND p.[Número] = e.[Número]
WHERE e.Empresa = @empresa AND e.Fecha >= @desde AND e.[NºTraspaso] IS NOT NULL AND e.[NºTraspaso] <> 0 AND e.Cantidad > 0
  AND p.Estado >= 0 AND ISNULL(p.Ficticio, 0) = 0
GROUP BY e.[Número]";

        public async Task<List<ProductoRepuestoSinCodigoDTO>> LeerRepuestos(string empresa, DateTime desde)
        {
            using (var db = new NVEntities())
            {
                db.Database.CommandTimeout = 120;
                return await db.Database.SqlQuery<ProductoRepuestoSinCodigoDTO>(SQL_REPUESTOS,
                        new SqlParameter("@empresa", System.Data.SqlDbType.Char, 3) { Value = empresa },
                        new SqlParameter("@desde", System.Data.SqlDbType.DateTime) { Value = desde })
                    .ToListAsync().ConfigureAwait(false);
            }
        }
    }
}
