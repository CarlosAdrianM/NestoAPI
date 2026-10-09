using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    // ------------------------------------------------------------------------------------------------------------------
    // Sugerencia 564 (Paloma, 09/10/26): imprimir la reposición para prepararla (o comprobarla al recibirla) a mano,
    // mientras las tiendas no tienen Ariadna. Una lista en papel con lo que hay que coger, ordenada para recorrer la tienda.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Una línea del listado impreso de una reposición.</summary>
    public class LineaListadoReposicionDTO
    {
        public string Producto { get; set; }
        public string CodigoBarras { get; set; }
        public string Descripcion { get; set; }
        public int Cantidad { get; set; }
        /// <summary>Familia (Familias.Descripción): con ella se ordena lo que no tiene ubicación.</summary>
        public string Familia { get; set; }
        /// <summary>El hueco en el almacén del listado (Ubicaciones, estado 0); null si no tiene (las tiendas, casi siempre).</summary>
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }

        /// <summary>«002/004/001» o null.</summary>
        public string Ubicacion => string.IsNullOrWhiteSpace(Pasillo) ? null
            : $"{Pasillo.Trim()}/{Fila?.Trim()}/{Columna?.Trim()}";
    }

    /// <summary>Lo que se imprime de una reposición: la cabecera y las líneas ya ordenadas para recorrer el almacén.</summary>
    public class ListadoReposicionDTO
    {
        /// <summary>true: lo que la tienda tiene que preparar (enviar); false: lo que llega (recibir).</summary>
        public bool EsEnvio { get; set; }
        public string Empresa { get; set; }
        public string Origen { get; set; }
        public string NombreOrigen { get; set; }
        public string Destino { get; set; }
        public string NombreDestino { get; set; }
        /// <summary>El almacén cuyas ubicaciones salen en el listado: el origen al enviar, el destino al recibir.</summary>
        public string AlmacenUbicaciones { get; set; }
        /// <summary>Número de traspaso. Null mientras se prepara en la tienda (lo pone Terminar).</summary>
        public int? NumTraspaso { get; set; }
        /// <summary>El día de la reposición (PreExtrProducto.Fecha).</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>NestoAPI#577: el corte con el que la rellenó el proceso automático; null si se hizo a mano o no se sabe.</summary>
        public DateTime? FechaCorte { get; set; }
        public List<LineaListadoReposicionDTO> Lineas { get; set; } = new List<LineaListadoReposicionDTO>();

        public int Unidades => (Lineas ?? new List<LineaListadoReposicionDTO>()).Sum(l => l.Cantidad);

        /// <summary>«Alcobendas (ALC) → Algete (ALG)».</summary>
        public string Ruta => $"{Almacen(NombreOrigen, Origen)} → {Almacen(NombreDestino, Destino)}";

        public string Titulo => EsEnvio ? "Reposición para preparar" : "Reposición para recibir";

        private static string Almacen(string nombre, string codigo)
        {
            string limpio = string.IsNullOrWhiteSpace(codigo) ? "?" : codigo.Trim();
            return string.IsNullOrWhiteSpace(nombre) ? limpio : $"{nombre.Trim()} ({limpio})";
        }
    }

    /// <summary>Familia y hueco de un producto en un almacén.</summary>
    public class FichaListadoReposicion
    {
        public string Producto { get; set; }
        public string Familia { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
    }

    public interface IRepositorioListadoReposicion
    {
        /// <summary>Familia de cada producto y su hueco en el almacén (el primero del recorrido), si tiene.</summary>
        Task<List<FichaListadoReposicion>> LeerFichas(string empresa, string almacen, IReadOnlyCollection<string> productos);
        /// <summary>El corte de la reposición abierta (sin número) de esa ruta. Null si no hay cabecera o se hizo a mano.</summary>
        Task<DateTime?> LeerFechaCorteEnPreparacion(string empresa, string origen, string destino);
        /// <summary>El corte del traspaso. Null si no hay cabecera (Nesto viejo) o se hizo a mano.</summary>
        Task<DateTime?> LeerFechaCorteTraspaso(string empresa, int traspaso);
        Task<string> LeerNombreAlmacen(string empresa, string almacen);
    }

    public class RepositorioListadoReposicionSql : IRepositorioListadoReposicion
    {
        /// <summary>Muy por debajo del límite de 2.100 parámetros de SQL Server.</summary>
        internal const int PRODUCTOS_POR_CONSULTA = 500;

        // El hueco, como las alternativas de CambioHuecoPicking: lo libre con hueco (estado 0) que tiene unidades, el primero
        // en el orden del recorrido (pasillo, columna, fila). En la empresa y en su espejo, como prdUbicarReposicion.
        internal const string SQL_FICHAS = @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(f.[Descripción]) AS Familia,
       RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Fila) AS Fila, RTRIM(u.Columna) AS Columna
FROM Productos p
     LEFT JOIN Familias f ON f.Empresa = p.Empresa AND f.[Número] = p.Familia
     OUTER APPLY (SELECT TOP 1 x.Pasillo, x.Fila, x.Columna
                  FROM Ubicaciones x
                  WHERE x.Empresa IN (@p0, @p1) AND x.[Almacén] = @p2 AND x.[Número] = p.[Número] AND x.Estado = 0
                        AND x.Pasillo IS NOT NULL AND x.Fila IS NOT NULL AND x.Columna IS NOT NULL
                  GROUP BY x.Pasillo, x.Fila, x.Columna
                  HAVING SUM(x.Cantidad) > 0
                  ORDER BY x.Pasillo, x.Columna, x.Fila) u
WHERE p.Empresa = @p0 AND p.[Número] IN ({0})";

        // La cabecera abierta de la ruta, la misma que numera Terminar (RegistroReposicionesTraspasosSql.SQL_NUMERAR_ABIERTA)
        internal const string SQL_CORTE_EN_PREPARACION = @"
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
    SELECT TOP 1 r.FechaCorte FROM dbo.ReposicionesTraspasos r
    WHERE r.Empresa = @p0 AND r.Origen = @p1 AND r.Destino = @p2 AND r.NumTraspaso IS NULL AND r.FechaPreparada IS NULL
          AND r.Omitida IS NULL
    ORDER BY r.FechaCreacion DESC, r.Id DESC
ELSE
    SELECT CAST(NULL AS datetime) AS FechaCorte";

        internal const string SQL_CORTE_TRASPASO = @"
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
    SELECT TOP 1 r.FechaCorte FROM dbo.ReposicionesTraspasos r WHERE r.Empresa = @p0 AND r.NumTraspaso = @p1 ORDER BY r.Id DESC
ELSE
    SELECT CAST(NULL AS datetime) AS FechaCorte";

        private readonly NVEntities db;

        public RepositorioListadoReposicionSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<FichaListadoReposicion>> LeerFichas(string empresa, string almacen, IReadOnlyCollection<string> productos)
        {
            var resultado = new List<FichaListadoReposicion>();
            List<string> todos = (productos ?? new List<string>()).ToList();
            for (int desde = 0; desde < todos.Count; desde += PRODUCTOS_POR_CONSULTA)
            {
                List<string> tanda = todos.Skip(desde).Take(PRODUCTOS_POR_CONSULTA).ToList();
                // Tipados como las columnas (char): con nvarchar SQL Server convertiría cada fila y no usaría el índice
                var parametros = new List<object>
                {
                    new SqlParameter("@p0", SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@p1", SqlDbType.Char, 3) { Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO },
                    new SqlParameter("@p2", SqlDbType.Char, 3) { Value = almacen }
                };
                for (int i = 0; i < tanda.Count; i++)
                {
                    parametros.Add(new SqlParameter("@p" + (i + 3), SqlDbType.Char, 15) { Value = tanda[i] });
                }
                string lista = string.Join(", ", Enumerable.Range(3, tanda.Count).Select(i => "@p" + i));
                resultado.AddRange(await db.Database.SqlQuery<FichaListadoReposicion>(string.Format(SQL_FICHAS, lista), parametros.ToArray())
                    .ToListAsync().ConfigureAwait(false));
            }
            return resultado;
        }

        public async Task<DateTime?> LeerFechaCorteEnPreparacion(string empresa, string origen, string destino)
        {
            return await db.Database.SqlQuery<DateTime?>(SQL_CORTE_EN_PREPARACION,
                    RegistroReposicionesTraspasosSql.Char("@p0", empresa, 3), RegistroReposicionesTraspasosSql.Char("@p1", origen, 3),
                    RegistroReposicionesTraspasosSql.Char("@p2", destino, 3))
                .FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public async Task<DateTime?> LeerFechaCorteTraspaso(string empresa, int traspaso)
        {
            return await db.Database.SqlQuery<DateTime?>(SQL_CORTE_TRASPASO,
                    RegistroReposicionesTraspasosSql.Char("@p0", empresa, 3), RegistroReposicionesTraspasosSql.EnteroONulo("@p1", traspaso))
                .FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public Task<string> LeerNombreAlmacen(string empresa, string almacen)
        {
            return RepositorioPreparacionAlmacen.LeerNombreAlmacen(db.Database, empresa, almacen);
        }
    }

    public interface IServicioListadoReposicion
    {
        /// <summary>Lo que el origen tiene en preparación para mandar (sin las líneas a 0). Null si no tiene ninguna.</summary>
        Task<ListadoReposicionDTO> LeerEnPreparacion(string empresa, string origen);
        /// <summary>Lo que llega al almacén con ese traspaso. Null si no está pendiente de recibir ahí.</summary>
        Task<ListadoReposicionDTO> LeerRecepcion(string empresa, string almacen, int traspaso);
    }

    /// <summary>
    /// Sugerencia 564: junta lo que ya leen las pantallas de enviar y recibir (las mismas consultas) con la familia y el
    /// hueco de cada producto, y lo ordena para recorrer el almacén. La cabecera de trazabilidad (NestoAPI#577) da el corte.
    /// </summary>
    public class ServicioListadoReposicion : IServicioListadoReposicion
    {
        private readonly IServicioPreparacionReposicion preparacion;
        private readonly PreparacionAlmacen.IServicioRecepcionReposiciones recepcion;
        private readonly IRepositorioListadoReposicion repositorio;

        public ServicioListadoReposicion(NVEntities db)
            : this(new ServicioPreparacionReposicion(db),
                  new ServicioRecepcionReposiciones(new RepositorioRecepcionReposiciones(db)),
                  new RepositorioListadoReposicionSql(db))
        {
        }

        internal ServicioListadoReposicion(IServicioPreparacionReposicion preparacion,
            PreparacionAlmacen.IServicioRecepcionReposiciones recepcion, IRepositorioListadoReposicion repositorio)
        {
            this.preparacion = preparacion ?? throw new ArgumentNullException(nameof(preparacion));
            this.recepcion = recepcion ?? throw new ArgumentNullException(nameof(recepcion));
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<ListadoReposicionDTO> LeerEnPreparacion(string empresa, string origen)
        {
            empresa = Limpio(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            origen = Limpio(origen)?.ToUpperInvariant();
            ReposicionEnPreparacionDTO reposicion = await preparacion.LeerEnPreparacion(empresa, origen).ConfigureAwait(false);
            if (reposicion?.Lineas == null || !reposicion.Lineas.Any())
            {
                return null;
            }
            var listado = new ListadoReposicionDTO
            {
                EsEnvio = true,
                Empresa = empresa,
                Origen = Limpio(reposicion.Origen) ?? origen,
                NombreOrigen = Limpio(reposicion.NombreOrigen),
                Destino = Limpio(reposicion.Destino),
                NombreDestino = Limpio(reposicion.NombreDestino),
                NumTraspaso = reposicion.NumTraspaso,
                Fecha = reposicion.Fecha == default(DateTime) ? (DateTime?)null : reposicion.Fecha,
                // A 0 no se manda (se borra al terminar): no se imprime
                Lineas = reposicion.Lineas.Where(l => l.Cantidad > 0).Select(l => new LineaListadoReposicionDTO
                {
                    Producto = Limpio(l.Producto),
                    CodigoBarras = Limpio(l.CodigoBarras),
                    Descripcion = Limpio(l.Nombre),
                    Cantidad = l.Cantidad
                }).ToList()
            };
            listado.AlmacenUbicaciones = listado.Origen;
            listado.FechaCorte = await SinFallar(() => repositorio.LeerFechaCorteEnPreparacion(empresa, listado.Origen, listado.Destino)).ConfigureAwait(false);
            await Completar(listado).ConfigureAwait(false);
            return listado;
        }

        public async Task<ListadoReposicionDTO> LeerRecepcion(string empresa, string almacen, int traspaso)
        {
            empresa = Limpio(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            almacen = Limpio(almacen)?.ToUpperInvariant();
            RecepcionReposicionDTO esperada = await recepcion.LeerRecepcion(empresa, almacen, traspaso).ConfigureAwait(false);
            if (esperada?.Lineas == null || !esperada.Lineas.Any())
            {
                return null;
            }
            List<ReposicionPendienteDTO> pendientes = await SinFallar(() => recepcion.LeerPendientes(empresa, almacen)).ConfigureAwait(false);
            ReposicionPendienteDTO pendiente = (pendientes ?? new List<ReposicionPendienteDTO>()).FirstOrDefault(p => p.Traspaso == traspaso);
            var listado = new ListadoReposicionDTO
            {
                EsEnvio = false,
                Empresa = empresa,
                Origen = Limpio(pendiente?.Origen),
                NombreOrigen = Limpio(pendiente?.NombreOrigen),
                Destino = almacen,
                NombreDestino = Limpio(await SinFallar(() => repositorio.LeerNombreAlmacen(empresa, almacen)).ConfigureAwait(false)),
                AlmacenUbicaciones = almacen,
                NumTraspaso = traspaso,
                Fecha = pendiente?.Fecha,
                Lineas = esperada.Lineas.Where(l => l.Cantidad > 0).Select(l => new LineaListadoReposicionDTO
                {
                    Producto = Limpio(l.Producto),
                    CodigoBarras = Limpio(l.CodigoBarras),
                    Descripcion = Limpio(l.Descripcion),
                    Cantidad = l.Cantidad
                }).ToList()
            };
            listado.FechaCorte = await SinFallar(() => repositorio.LeerFechaCorteTraspaso(empresa, traspaso)).ConfigureAwait(false);
            await Completar(listado).ConfigureAwait(false);
            return listado;
        }

        /// <summary>Familia y hueco de cada línea, y el orden del recorrido.</summary>
        private async Task Completar(ListadoReposicionDTO listado)
        {
            List<string> productos = listado.Lineas.Select(l => l.Producto).Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<FichaListadoReposicion> fichas = productos.Count == 0 ? new List<FichaListadoReposicion>()
                : await repositorio.LeerFichas(listado.Empresa, listado.AlmacenUbicaciones, productos).ConfigureAwait(false);
            Dictionary<string, FichaListadoReposicion> porProducto = (fichas ?? new List<FichaListadoReposicion>())
                .Where(f => !string.IsNullOrWhiteSpace(f?.Producto))
                .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (LineaListadoReposicionDTO linea in listado.Lineas)
            {
                if (linea.Producto != null && porProducto.TryGetValue(linea.Producto, out FichaListadoReposicion ficha))
                {
                    linea.Familia = Limpio(ficha.Familia);
                    linea.Pasillo = Limpio(ficha.Pasillo);
                    linea.Fila = Limpio(ficha.Fila);
                    linea.Columna = Limpio(ficha.Columna);
                }
            }
            listado.Lineas = OrdenarParaRecorrer(listado.Lineas);
        }

        /// <summary>
        /// El orden del papel: primero lo que tiene hueco, en el orden del recorrido (pasillo, columna, fila, como las
        /// alternativas del picking); después lo que no tiene (las tiendas, casi todo), por familia y descripción (sin familia,
        /// al final). Empates, por referencia.
        /// </summary>
        public static List<LineaListadoReposicionDTO> OrdenarParaRecorrer(IEnumerable<LineaListadoReposicionDTO> lineas)
        {
            StringComparer texto = StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true);
            return (lineas ?? Enumerable.Empty<LineaListadoReposicionDTO>())
                .Where(l => l != null)
                .OrderBy(l => l.Ubicacion == null ? 1 : 0)
                .ThenBy(l => l.Ubicacion == null ? string.Empty : l.Pasillo, StringComparer.Ordinal)
                .ThenBy(l => l.Ubicacion == null ? string.Empty : l.Columna ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(l => l.Ubicacion == null ? string.Empty : l.Fila ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(l => string.IsNullOrWhiteSpace(l.Familia) ? 1 : 0)
                .ThenBy(l => l.Familia ?? string.Empty, texto)
                .ThenBy(l => l.Descripcion ?? string.Empty, texto)
                .ThenBy(l => l.Producto ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Lo accesorio (el corte, el nombre del almacén, el origen) no impide imprimir.</summary>
        private static async Task<T> SinFallar<T>(Func<Task<T>> leer)
        {
            try
            {
                return await leer().ConfigureAwait(false);
            }
            catch (Exception)
            {
                return default(T);
            }
        }

        private static string Limpio(string texto)
        {
            return string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        }
    }
}
