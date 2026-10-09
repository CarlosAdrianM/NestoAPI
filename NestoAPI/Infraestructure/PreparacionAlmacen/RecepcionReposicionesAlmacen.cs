using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
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
        /// <summary>
        /// NestoAPI#577: si el traspaso está en el diario de entrada del almacén pero su salida del origen sigue sin
        /// contabilizar, el nombre del origen («Algete»); si no, null.
        /// </summary>
        Task<string> LeerOrigenSinSalir(string empresa, string almacen, int traspaso);
    }

    /// <summary>
    /// NestoAPI#577 (Carlos, 09/10/26): «La repo se rellena en Algete para enviar a Reina. Solo se debería ver en Algete en
    /// ese momento. Cuando se contabiliza, se deja de ver en Algete y se comienza a ver en Reina.» Desde Algete (con control
    /// de ubicaciones) la API cierra la reposición al crearla (para reservar los huecos): le pone número de traspaso y deja
    /// la entrada en el diario de entrada del destino (PendRepo, RepoAlgAlc…) mientras la salida negativa sigue en el
    /// diario de salida del origen («General») hasta que Ariadna termina la recogida y la contabiliza. En Nesto viejo la
    /// entrada no llegaba al destino hasta contabilizar la salida. Por eso, un traspaso NO está pendiente de recibir
    /// mientras queden en PreExtrProducto líneas suyas de salida (cantidad negativa) en el diario de salida de reposiciones
    /// (Almacenes.DiarioSalidaRep) del almacén de origen (Delegación de la entrada). Un único predicado para todos los
    /// lectores de «pendiente de recibir» (lista, líneas, terminar). PreExtrProducto es pequeña (~200 filas).
    /// </summary>
    internal static class SalidaReposicionSql
    {
        /// <summary>Para el WHERE de una consulta sobre las filas de ENTRADA con alias <c>p</c>.</summary>
        internal const string YA_HA_SALIDO = @"NOT EXISTS (SELECT 1 FROM PreExtrProducto s
         JOIN Almacenes so ON so.Empresa = s.Empresa AND so.[Número] = s.[Almacén] AND so.DiarioSalidaRep = s.Diario
    WHERE s.Empresa = p.Empresa AND s.[NºTraspaso] = p.[NºTraspaso] AND s.[Almacén] = p.[Delegación] AND s.Cantidad < 0)";

        internal const string SQL_ORIGEN_SIN_SALIR = @"
SELECT TOP 1 COALESCE(NULLIF(RTRIM(o.[Descripción]), ''), NULLIF(RTRIM(p.[Delegación]), ''), 'su almacén de origen')
FROM PreExtrProducto p
     JOIN Almacenes a ON a.Empresa = p.Empresa AND a.[Número] = p.[Almacén] AND a.DiarioEntradaRep = p.Diario
     LEFT JOIN Almacenes o ON o.Empresa = p.Empresa AND o.[Número] = p.[Delegación]
WHERE p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.[NºTraspaso] = @p2 AND NOT " + YA_HA_SALIDO;

        /// <summary>409: «La reposición 80932 todavía no ha salido de Algete».</summary>
        internal static NestoBusinessException NoHaSalido(int traspaso, string origen)
        {
            string de = string.IsNullOrWhiteSpace(origen) ? "su almacén de origen" : origen.Trim();
            return new NestoBusinessException($"La reposición {traspaso} todavía no ha salido de {de}: se podrá recibir cuando " +
                "terminen de prepararla allí.") { StatusCode = HttpStatusCode.Conflict };
        }
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

        // El almacén de origen va en Delegación de las filas de entrada (lo ponen igual Nesto viejo y POST api/Reposiciones;
        // comprobado con 80893 y 80905); su nombre, en Almacenes.Descripción («Alcobendas»).
        private const string ORIGEN_Y_FILTRO = @"
FROM PreExtrProducto p
     JOIN Almacenes a ON a.Empresa = p.Empresa AND a.[Número] = p.[Almacén] AND a.DiarioEntradaRep = p.Diario
     LEFT JOIN Almacenes o ON o.Empresa = p.Empresa AND o.[Número] = p.[Delegación]
WHERE p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.[NºTraspaso] > 0
  AND " + SalidaReposicionSql.YA_HA_SALIDO;

        internal const string SQL_PENDIENTES = @"
SELECT p.[NºTraspaso] AS Traspaso, COUNT(*) AS Lineas, CAST(SUM(p.Cantidad) AS int) AS Unidades,
       MIN(p.Fecha) AS Fecha, RTRIM(MAX(p.Usuario)) AS Usuario,
       RTRIM(MAX(p.[Delegación])) AS Origen, RTRIM(MAX(o.[Descripción])) AS NombreOrigen" + ORIGEN_Y_FILTRO + @"
GROUP BY p.[NºTraspaso]
ORDER BY p.[NºTraspaso]";

        internal const string SQL_LINEAS = @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(MAX(pr.Nombre)) AS Descripcion, RTRIM(MAX(pr.CodBarras)) AS CodigoBarras,
       CAST(SUM(p.Cantidad) AS int) AS Cantidad
FROM PreExtrProducto p
     JOIN Almacenes a ON a.Empresa = p.Empresa AND a.[Número] = p.[Almacén] AND a.DiarioEntradaRep = p.Diario
     LEFT JOIN Productos pr ON pr.Empresa = p.Empresa AND pr.[Número] = p.[Número]
WHERE p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.[NºTraspaso] = @p2
  AND " + SalidaReposicionSql.YA_HA_SALIDO + @"
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

        public Task<string> LeerOrigenSinSalir(string empresa, string almacen, int traspaso)
        {
            return baseDeDatos.SqlQuery<string>(SalidaReposicionSql.SQL_ORIGEN_SIN_SALIR, empresa, almacen, traspaso).FirstOrDefaultAsync();
        }
    }

    public interface IServicioRecepcionReposiciones
    {
        Task<List<ReposicionPendienteDTO>> LeerPendientes(string empresa, string almacen);
        /// <summary>Null si no hay ninguna reposición pendiente con ese número para ese almacén.</summary>
        Task<RecepcionReposicionDTO> LeerRecepcion(string empresa, string almacen, int traspaso);
        /// <summary>Compara lo contado con lo enviado. No guarda nada. Null si no existe la reposición.</summary>
        Task<ResultadoRecepcionReposicionDTO> Casar(string empresa, string almacen, int traspaso, IEnumerable<LecturaRecepcionDTO> lecturas);
        /// <summary>
        /// NestoAPI#577: lanza 409 («La reposición 80932 todavía no ha salido de Algete») si el traspaso va a ese almacén
        /// pero su salida del origen sigue sin contabilizar. Si no, no hace nada.
        /// </summary>
        Task ComprobarQueHaSalido(string empresa, string almacen, int traspaso);
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
            if (filas == null || !filas.Any())
            {
                await ComprobarQueHaSalido(empresa, almacen, traspaso).ConfigureAwait(false);
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
            if (filas == null || !filas.Any())
            {
                await ComprobarQueHaSalido(empresa, almacen, traspaso).ConfigureAwait(false);
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

        public async Task ComprobarQueHaSalido(string empresa, string almacen, int traspaso)
        {
            string origen = await repositorio.LeerOrigenSinSalir(empresa, almacen, traspaso).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(origen))
            {
                throw SalidaReposicionSql.NoHaSalido(traspaso, origen);
            }
        }

        public void Dispose()
        {
            dbPropio?.Dispose();
        }
    }
}
