using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Un producto de una reposición pendiente de entrar en el almacén de destino.</summary>
    public class FilaReposicion
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public int Cantidad { get; set; }
    }

    public interface IRepositorioRecepcionReposiciones
    {
        Task<List<ReposicionPendienteDTO>> LeerPendientes(string empresa, string almacen);
        Task<List<FilaReposicion>> LeerLineas(string empresa, string almacen, int traspaso);
    }

    /// <summary>
    /// NestoAPI#553: lecturas de las reposiciones que están de camino a un almacén. Una reposición
    /// pendiente de entrar = filas de PreExtrProducto en el diario de entrada de reposiciones del
    /// destino (Almacenes.DiarioEntradaRep) con NºTraspaso, todavía sin contabilizar. Solo lectura:
    /// contabilizar la entrada sigue siendo cosa de Nesto viejo.
    /// </summary>
    public class RepositorioRecepcionReposiciones : IRepositorioRecepcionReposiciones
    {
        private readonly Database baseDeDatos;

        public RepositorioRecepcionReposiciones(NVEntities db) : this(db.Database)
        {
        }

        internal RepositorioRecepcionReposiciones(Database baseDeDatos)
        {
            this.baseDeDatos = baseDeDatos;
        }

        private const string ORIGEN_Y_FILTRO = @"
FROM PreExtrProducto p
     JOIN Almacenes a ON a.Empresa = p.Empresa AND a.[Número] = p.[Almacén] AND a.DiarioEntradaRep = p.Diario
WHERE p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.[NºTraspaso] > 0";

        internal const string SQL_PENDIENTES = @"
SELECT p.[NºTraspaso] AS Traspaso, COUNT(*) AS Lineas, CAST(SUM(p.Cantidad) AS int) AS Unidades,
       MIN(p.Fecha) AS Fecha, RTRIM(MAX(p.Usuario)) AS Usuario" + ORIGEN_Y_FILTRO + @"
GROUP BY p.[NºTraspaso]
ORDER BY p.[NºTraspaso]";

        internal const string SQL_LINEAS = @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(MAX(pr.Nombre)) AS Descripcion, RTRIM(MAX(pr.CodBarras)) AS CodigoBarras,
       CAST(SUM(p.Cantidad) AS int) AS Cantidad
FROM PreExtrProducto p
     JOIN Almacenes a ON a.Empresa = p.Empresa AND a.[Número] = p.[Almacén] AND a.DiarioEntradaRep = p.Diario
     LEFT JOIN Productos pr ON pr.Empresa = p.Empresa AND pr.[Número] = p.[Número]
WHERE p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.[NºTraspaso] = @p2
GROUP BY p.[Número]
HAVING SUM(p.Cantidad) <> 0
ORDER BY p.[Número]";

        public Task<List<ReposicionPendienteDTO>> LeerPendientes(string empresa, string almacen)
        {
            return baseDeDatos.SqlQuery<ReposicionPendienteDTO>(SQL_PENDIENTES, empresa, almacen).ToListAsync();
        }

        public Task<List<FilaReposicion>> LeerLineas(string empresa, string almacen, int traspaso)
        {
            return baseDeDatos.SqlQuery<FilaReposicion>(SQL_LINEAS, empresa, almacen, traspaso).ToListAsync();
        }
    }

    public interface IServicioRecepcionReposiciones
    {
        Task<List<ReposicionPendienteDTO>> LeerPendientes(string empresa, string almacen);
        /// <summary>Null si no hay ninguna reposición pendiente con ese número para ese almacén.</summary>
        Task<RecepcionReposicionDTO> LeerRecepcion(string empresa, string almacen, int traspaso);
        /// <summary>Compara lo contado con lo enviado. No guarda nada. Null si no existe la reposición.</summary>
        Task<ResultadoRecepcionReposicionDTO> Casar(string empresa, string almacen, int traspaso, IEnumerable<LecturaRecepcionDTO> lecturas);
    }

    /// <summary>
    /// NestoAPI#553 (fase 3, lectura): recibir en la tienda (o en Algete) una reposición leyendo los
    /// productos. Hoy las diferencias entre lo enviado y lo recibido no se registran (1 descuadre
    /// apuntado en 1.212 traspasos); este corte las hace visibles, sin tocar todavía la contabilización.
    /// </summary>
    public class ServicioRecepcionReposiciones : IServicioRecepcionReposiciones, IDisposable
    {
        private readonly IRepositorioRecepcionReposiciones repositorio;
        private readonly NVEntities dbPropio;

        public ServicioRecepcionReposiciones()
        {
            dbPropio = new NVEntities();
            repositorio = new RepositorioRecepcionReposiciones(dbPropio);
        }

        internal ServicioRecepcionReposiciones(IRepositorioRecepcionReposiciones repositorio)
        {
            this.repositorio = repositorio;
        }

        public Task<List<ReposicionPendienteDTO>> LeerPendientes(string empresa, string almacen)
        {
            return repositorio.LeerPendientes(empresa, almacen);
        }

        public async Task<RecepcionReposicionDTO> LeerRecepcion(string empresa, string almacen, int traspaso)
        {
            List<FilaReposicion> filas = await repositorio.LeerLineas(empresa, almacen, traspaso).ConfigureAwait(false);
            if (!filas.Any())
            {
                return null;
            }

            HashSet<string> duplicados = CasadorEscaneos.CodigosDuplicados(
                filas.Select(f => new KeyValuePair<string, string>(f.Producto, f.CodigoBarras)));

            return new RecepcionReposicionDTO
            {
                Empresa = empresa,
                Almacen = almacen,
                Traspaso = traspaso,
                Lineas = filas.Select(f =>
                {
                    string codigo = string.IsNullOrWhiteSpace(f.CodigoBarras) ? null : f.CodigoBarras.Trim();
                    return new LineaReposicionDTO
                    {
                        Producto = f.Producto?.Trim(),
                        Descripcion = f.Descripcion?.Trim(),
                        CodigoBarras = codigo,
                        SinCodigo = codigo == null,
                        CodigoDuplicado = codigo != null && duplicados.Contains(codigo),
                        Cantidad = f.Cantidad
                    };
                }).ToList()
            };
        }

        public async Task<ResultadoRecepcionReposicionDTO> Casar(string empresa, string almacen, int traspaso,
            IEnumerable<LecturaRecepcionDTO> lecturas)
        {
            List<FilaReposicion> filas = await repositorio.LeerLineas(empresa, almacen, traspaso).ConfigureAwait(false);
            if (!filas.Any())
            {
                return null;
            }

            List<DiferenciaPreparacionDTO> diferencias = CasadorEscaneos.Casar(
                filas.Select(f => new CasadorEscaneos.Cantidad { Producto = f.Producto, Descripcion = f.Descripcion, Unidades = f.Cantidad }),
                (lecturas ?? Enumerable.Empty<LecturaRecepcionDTO>())
                    .Where(l => !string.IsNullOrWhiteSpace(l?.Producto))
                    .Select(l => new CasadorEscaneos.Cantidad { Producto = l.Producto, Unidades = l.Cantidad }));

            return new ResultadoRecepcionReposicionDTO
            {
                Traspaso = traspaso,
                Productos = diferencias,
                Cuadra = CasadorEscaneos.EstaCompleto(diferencias)
            };
        }

        public void Dispose()
        {
            dbPropio?.Dispose();
        }
    }
}
