using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    // Ariadna#63 (sugerencias 872 y 873): el histórico de pickings y packings («saber si algo se picó o no»). Sale de las mismas
    // tablas que Salidas, Picking y Packing: PreparacionEscaneos (lecturas y faltas, fases PICK y PACK), PreparacionSalidasTerminadas,
    // EnviosAgenciaBultos (las cajas con su foto) y LinPedidoVta.Picking. Solo hay histórico de lo hecho en Ariadna: un picking
    // hecho en papel no tiene lecturas.

    public class FilaActividadHistorico
    {
        /// <summary>El picking o el pedido.</summary>
        public int Numero { get; set; }
        public DateTime Fecha { get; set; }
    }

    public class FilaLineaHistorico
    {
        public int Picking { get; set; }
        public int Pedido { get; set; }
        public int LineaPedido { get; set; }
        public string Cliente { get; set; }
        public string NombreCliente { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Cantidad { get; set; }
    }

    public class FilaLecturaHistorico
    {
        public int Picking { get; set; }
        public string Producto { get; set; }
        public string Usuario { get; set; }
        public int Leidas { get; set; }
        public int Faltas { get; set; }
        public DateTime Primera { get; set; }
        public DateTime Ultima { get; set; }
    }

    public class FilaTerminadaHistorico
    {
        public int Numero { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
    }

    public class FilaResumenPackingHistorico
    {
        public int Pedido { get; set; }
        public int Cajas { get; set; }
        public string UsuarioCajas { get; set; }
        public string UsuarioLecturas { get; set; }
        public int BultosEnvio { get; set; }
    }

    public class FilaContenidoPackingHistorico
    {
        public int Bulto { get; set; }
        public int Picking { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Usuario { get; set; }
        public int Cantidad { get; set; }
        public DateTime Ultima { get; set; }
    }

    public interface IRepositorioHistoricoAlmacen
    {
        /// <summary>Pickings con lecturas PICK o terminados en [desde, hasta), con la primera fecha; filtrados por pedido y producto.</summary>
        Task<List<FilaActividadHistorico>> LeerPickingsConActividad(string empresa, DateTime desde, DateTime hasta, int? pedido, string producto, int maximo);
        Task<List<FilaLineaHistorico>> LeerLineasPickings(string empresa, IReadOnlyCollection<int> pickings);
        Task<List<FilaLecturaHistorico>> LeerLecturasPickings(string empresa, IReadOnlyCollection<int> pickings);
        Task<List<FilaTerminadaHistorico>> LeerPickingsTerminados(string empresa, IReadOnlyCollection<int> pickings);
        /// <summary>Pedidos con cajas o lecturas PACK en [desde, hasta), con la primera fecha.</summary>
        Task<List<FilaActividadHistorico>> LeerPackingsConActividad(string empresa, DateTime desde, DateTime hasta, int? pedido, int maximo);
        Task<List<FilaResumenPackingHistorico>> LeerResumenPackings(string empresa, IReadOnlyCollection<int> pedidos);
        Task<List<FilaContenidoPackingHistorico>> LeerContenidoPacking(string empresa, int pedido);
    }

    /// <summary>
    /// Por SQL con parámetros, como <see cref="RepositorioPreparacionAlmacen"/>. Todas las consultas van acotadas: por fechas (máx.
    /// 31 días, índices IX_PreparacionEscaneos_Fecha y IX_EnviosAgenciaBultos_Fecha de Scripts/Ariadna63_VerHistoricoAlmacen.sql; sin
    /// ellos son tablas pequeñas de evidencia), por picking (índice Empresa, Picking de LinPedidoVta e IX_PreparacionEscaneos_Origen)
    /// o por pedido (IX_PreparacionEscaneos_Pedido, UQ_EnviosAgenciaBultos_Bulto). Sin bloqueos: solo lectura de evidencia.
    /// </summary>
    public class RepositorioHistoricoAlmacenSql : IRepositorioHistoricoAlmacen, IDisposable
    {
        internal const string SQL_PICKINGS_CON_ACTIVIDAD = @"
WITH act AS (
    SELECT e.NumeroOrigen AS Numero, MIN(e.FechaEscaneo) AS Fecha
    FROM dbo.PreparacionEscaneos e
    WHERE e.Empresa = @empresa AND e.Fase = 'PICK' AND e.TipoOrigen = 'PICK' AND e.FechaEscaneo >= @desde AND e.FechaEscaneo < @hasta
    GROUP BY e.NumeroOrigen
    UNION ALL
    SELECT t.NumeroOrigen, t.Fecha
    FROM dbo.PreparacionSalidasTerminadas t
    WHERE t.Empresa = @empresa AND t.TipoOrigen = 'PICK' AND t.Fecha >= @desde AND t.Fecha < @hasta
)
SELECT TOP (@maximo) a.Numero, MIN(a.Fecha) AS Fecha
FROM act a
WHERE (@pedido IS NULL OR EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.Empresa = @empresa AND l.[Número] = @pedido AND l.Picking = a.Numero))
  AND (@producto IS NULL OR EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.Empresa = @empresa AND l.Picking = a.Numero AND l.Producto = @producto))
GROUP BY a.Numero
ORDER BY MIN(a.Fecha) DESC";

        // Las líneas de producto del picking en cualquier estado (las ya albaranadas conservan el número de picking), con la
        // misma cantidad que el packing (lo que el cliente no recoge) y sin los ficticios.
        internal const string SQL_LINEAS_PICKINGS = @"
SELECT l.Picking AS Picking, l.[Número] AS Pedido, l.[Nº Orden] AS LineaPedido, RTRIM(l.[Nº Cliente]) AS Cliente,
       RTRIM(cl.Nombre) AS NombreCliente, RTRIM(l.Producto) AS Producto, RTRIM(ISNULL(p.Nombre, l.Texto)) AS Nombre,
       CAST(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS int) AS Cantidad
FROM LinPedidoVta l
     LEFT JOIN Clientes cl ON cl.Empresa = l.Empresa AND cl.[Nº Cliente] = l.[Nº Cliente] AND cl.Contacto = l.Contacto
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.Empresa = @empresa AND l.Picking IN ({0}) AND l.TipoLinea = 1 AND ISNULL(p.Ficticio, 0) = 0
      AND ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) <> 0
ORDER BY l.Picking, l.[Número], l.[Nº Orden]";

        internal const string SQL_LECTURAS_PICKINGS = @"
SELECT e.NumeroOrigen AS Picking, RTRIM(e.Producto) AS Producto, RTRIM(e.Usuario) AS Usuario,
       CAST(SUM(CASE WHEN e.Metodo <> 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Leidas,
       CAST(SUM(CASE WHEN e.Metodo = 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Faltas,
       MIN(e.FechaEscaneo) AS Primera, MAX(e.FechaEscaneo) AS Ultima
FROM dbo.PreparacionEscaneos e
WHERE e.Empresa = @empresa AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen IN ({0}) AND e.Fase = 'PICK'
GROUP BY e.NumeroOrigen, e.Producto, e.Usuario";

        internal const string SQL_PICKINGS_TERMINADOS = @"
SELECT t.NumeroOrigen AS Numero, RTRIM(t.Usuario) AS Usuario, t.Fecha
FROM dbo.PreparacionSalidasTerminadas t
WHERE t.Empresa = @empresa AND t.TipoOrigen = 'PICK' AND t.NumeroOrigen IN ({0})";

        internal const string SQL_PACKINGS_CON_ACTIVIDAD = @"
WITH act AS (
    SELECT b.Pedido AS Numero, MIN(b.FechaRegistro) AS Fecha
    FROM dbo.EnviosAgenciaBultos b
    WHERE b.Empresa = @empresa AND b.FechaRegistro >= @desde AND b.FechaRegistro < @hasta AND (@pedido IS NULL OR b.Pedido = @pedido)
    GROUP BY b.Pedido
    UNION ALL
    SELECT e.Pedido, MIN(e.FechaEscaneo)
    FROM dbo.PreparacionEscaneos e
    WHERE e.Empresa = @empresa AND e.Fase = 'PACK' AND e.Pedido IS NOT NULL AND e.FechaEscaneo >= @desde AND e.FechaEscaneo < @hasta
          AND (@pedido IS NULL OR e.Pedido = @pedido)
    GROUP BY e.Pedido
)
SELECT TOP (@maximo) a.Numero, MIN(a.Fecha) AS Fecha
FROM act a
GROUP BY a.Numero
ORDER BY MIN(a.Fecha) DESC";

        internal const string SQL_RESUMEN_PACKINGS = @"
SELECT x.Pedido,
       CAST((SELECT COUNT(DISTINCT b.Bulto) FROM dbo.EnviosAgenciaBultos b WHERE b.Empresa = @empresa AND b.Pedido = x.Pedido) AS int) AS Cajas,
       (SELECT TOP 1 RTRIM(b.Usuario) FROM dbo.EnviosAgenciaBultos b WHERE b.Empresa = @empresa AND b.Pedido = x.Pedido
        GROUP BY b.Usuario ORDER BY COUNT(*) DESC, MAX(b.FechaRegistro) DESC) AS UsuarioCajas,
       (SELECT TOP 1 RTRIM(e.Usuario) FROM dbo.PreparacionEscaneos e WHERE e.Empresa = @empresa AND e.Pedido = x.Pedido AND e.Fase = 'PACK'
        GROUP BY e.Usuario ORDER BY COUNT(*) DESC, MAX(e.FechaEscaneo) DESC) AS UsuarioLecturas,
       CAST(ISNULL((SELECT TOP 1 ea.Bultos FROM EnviosAgencia ea WHERE ea.Empresa = @empresa AND ea.Pedido = x.Pedido AND ea.Estado >= 0
        ORDER BY ea.Numero DESC), 0) AS int) AS BultosEnvio
FROM (VALUES {0}) x(Pedido)";

        internal const string SQL_CONTENIDO_PACKING = @"
SELECT CAST(ISNULL(e.Bulto, 0) AS int) AS Bulto, e.NumeroOrigen AS Picking, RTRIM(e.Producto) AS Producto, RTRIM(p.Nombre) AS Nombre,
       RTRIM(e.Usuario) AS Usuario, CAST(SUM(CASE WHEN e.Metodo <> 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Cantidad,
       MAX(e.FechaEscaneo) AS Ultima
FROM dbo.PreparacionEscaneos e
     LEFT JOIN Productos p ON p.Empresa = e.Empresa AND p.[Número] = e.Producto
WHERE e.Empresa = @empresa AND e.Pedido = @pedido AND e.Fase = 'PACK'
GROUP BY e.Bulto, e.NumeroOrigen, e.Producto, p.Nombre, e.Usuario";

        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioHistoricoAlmacenSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public static RepositorioHistoricoAlmacenSql ConContextoPropio() => new RepositorioHistoricoAlmacenSql(new NVEntities()) { contextoPropio = true };

        public Task<List<FilaActividadHistorico>> LeerPickingsConActividad(string empresa, DateTime desde, DateTime hasta, int? pedido, string producto, int maximo)
            => db.Database.SqlQuery<FilaActividadHistorico>(SQL_PICKINGS_CON_ACTIVIDAD,
                Empresa(empresa), Fecha("@desde", desde), Fecha("@hasta", hasta), Entero("@pedido", pedido),
                new SqlParameter("@producto", SqlDbType.Char, 15) { Value = (object)producto ?? DBNull.Value },
                Entero("@maximo", maximo)).ToListAsync();

        public Task<List<FilaLineaHistorico>> LeerLineasPickings(string empresa, IReadOnlyCollection<int> pickings)
            => PorLista<FilaLineaHistorico>(SQL_LINEAS_PICKINGS, empresa, pickings);

        public Task<List<FilaLecturaHistorico>> LeerLecturasPickings(string empresa, IReadOnlyCollection<int> pickings)
            => PorLista<FilaLecturaHistorico>(SQL_LECTURAS_PICKINGS, empresa, pickings);

        public Task<List<FilaTerminadaHistorico>> LeerPickingsTerminados(string empresa, IReadOnlyCollection<int> pickings)
            => PorLista<FilaTerminadaHistorico>(SQL_PICKINGS_TERMINADOS, empresa, pickings);

        public Task<List<FilaActividadHistorico>> LeerPackingsConActividad(string empresa, DateTime desde, DateTime hasta, int? pedido, int maximo)
            => db.Database.SqlQuery<FilaActividadHistorico>(SQL_PACKINGS_CON_ACTIVIDAD,
                Empresa(empresa), Fecha("@desde", desde), Fecha("@hasta", hasta), Entero("@pedido", pedido), Entero("@maximo", maximo)).ToListAsync();

        public Task<List<FilaResumenPackingHistorico>> LeerResumenPackings(string empresa, IReadOnlyCollection<int> pedidos)
        {
            if (pedidos == null || pedidos.Count == 0)
            {
                return Task.FromResult(new List<FilaResumenPackingHistorico>());
            }
            string valores = string.Join(", ", pedidos.Distinct().Select(p => "(" + p.ToString(CultureInfo.InvariantCulture) + ")"));
            return db.Database.SqlQuery<FilaResumenPackingHistorico>(string.Format(CultureInfo.InvariantCulture, SQL_RESUMEN_PACKINGS, valores),
                Empresa(empresa)).ToListAsync();
        }

        public Task<List<FilaContenidoPackingHistorico>> LeerContenidoPacking(string empresa, int pedido)
            => db.Database.SqlQuery<FilaContenidoPackingHistorico>(SQL_CONTENIDO_PACKING, Empresa(empresa), Entero("@pedido", pedido)).ToListAsync();

        // Los números van en el texto: son enteros (no hay inyección posible) y así el plan usa el índice con un IN normal.
        private Task<List<T>> PorLista<T>(string sql, string empresa, IReadOnlyCollection<int> numeros)
        {
            if (numeros == null || numeros.Count == 0)
            {
                return Task.FromResult(new List<T>());
            }
            string lista = string.Join(", ", numeros.Distinct().Select(n => n.ToString(CultureInfo.InvariantCulture)));
            return db.Database.SqlQuery<T>(string.Format(CultureInfo.InvariantCulture, sql, lista), Empresa(empresa)).ToListAsync();
        }

        private static SqlParameter Empresa(string empresa) => new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO };

        private static SqlParameter Fecha(string nombre, DateTime valor) => new SqlParameter(nombre, SqlDbType.DateTime) { Value = valor };

        private static SqlParameter Entero(string nombre, int? valor) => new SqlParameter(nombre, SqlDbType.Int) { Value = (object)valor ?? DBNull.Value };

        public void Dispose()
        {
            if (contextoPropio)
            {
                db.Dispose();
            }
        }
    }

    /// <summary>Quién ve el histórico: Dirección, Informática, o el parámetro VerHistoricoAlmacen = "1" en su PROPIA fila.</summary>
    public static class PermisosHistoricoAlmacen
    {
        public const string PARAMETRO = "VerHistoricoAlmacen";

        public static bool PuedeVer(IPrincipal usuario, ILectorParametrosUsuario parametros)
        {
            if (usuario?.Identity == null || !usuario.Identity.IsAuthenticated)
            {
                return false;
            }
            if (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA))
            {
                return true;
            }
            string nombre = ServicioHistoricoAlmacen.SinDominio(usuario.Identity.Name);
            if (nombre == null || parametros == null)
            {
                return false;
            }
            // Sin heredar de «(defecto)»: el permiso es de la persona, no de todos.
            return parametros.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, nombre, PARAMETRO)?.Trim() == "1";
        }
    }

    public interface IServicioHistoricoAlmacen
    {
        Task<List<PickingHistoricoDTO>> LeerPickings(string empresa, DateTime desde, DateTime hasta, int? pedido, string producto);
        /// <summary>Null si el picking no tiene líneas ni nada hecho en Ariadna.</summary>
        Task<PickingHistoricoDetalleDTO> LeerPicking(string empresa, int picking);
        Task<List<PackingHistoricoDTO>> LeerPackings(string empresa, DateTime desde, DateTime hasta, int? pedido);
        /// <summary>Null si el pedido no tiene ni cajas ni lecturas de packing.</summary>
        Task<PackingHistoricoDetalleDTO> LeerPacking(string empresa, int pedido);
    }

    public class ServicioHistoricoAlmacen : IServicioHistoricoAlmacen
    {
        public const int MAXIMO_DIAS = 31;
        /// <summary>Tope de filas de la lista: con 31 días hay de sobra (unos pocos pickings al día).</summary>
        public const int MAXIMO_FILAS = 500;
        public const string MENSAJE_SIN_PERMISO = "Su usuario no tiene permiso para ver el histórico de pickings y packings. " +
            "Pídaselo a Dirección (parámetro VerHistoricoAlmacen).";

        private readonly IRepositorioHistoricoAlmacen repositorio;
        private readonly Func<int, Task<List<BultoAlmacenDTO>>> bultosDelPedido;

        /// <param name="bultosDelPedido">
        /// Las cajas de un pedido con el enlace público de su foto (<see cref="IServicioPreparacionAlmacen.LeerBultos"/>, empresa 1).
        /// </param>
        public ServicioHistoricoAlmacen(IRepositorioHistoricoAlmacen repositorio, Func<int, Task<List<BultoAlmacenDTO>>> bultosDelPedido)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.bultosDelPedido = bultosDelPedido ?? (_ => Task.FromResult(new List<BultoAlmacenDTO>()));
        }

        /// <summary>
        /// Las fechas de la lista: [desde, hasta] por días enteros, como mucho <see cref="MAXIMO_DIAS"/>. Sin «hasta», hoy; sin
        /// «desde», el mismo día que «hasta». Null si valen; si no, el motivo (de usted).
        /// </summary>
        public static string ValidarFechas(DateTime? desde, DateTime? hasta, DateTime hoy, out DateTime inicio, out DateTime finExclusivo)
        {
            DateTime fin = (hasta ?? hoy).Date;
            inicio = (desde ?? fin).Date;
            finExclusivo = fin.AddDays(1);
            if (inicio > fin)
            {
                return "La fecha «desde» no puede ser posterior a «hasta».";
            }
            if ((fin - inicio).TotalDays + 1 > MAXIMO_DIAS)
            {
                return $"Como mucho se pueden consultar {MAXIMO_DIAS} días seguidos: acorte las fechas.";
            }
            return null;
        }

        public async Task<List<PickingHistoricoDTO>> LeerPickings(string empresa, DateTime desde, DateTime hasta, int? pedido, string producto)
        {
            List<FilaActividadHistorico> actividad = await repositorio.LeerPickingsConActividad(empresa, desde, hasta, pedido,
                string.IsNullOrWhiteSpace(producto) ? null : producto.Trim(), MAXIMO_FILAS).ConfigureAwait(false) ?? new List<FilaActividadHistorico>();
            if (actividad.Count == 0)
            {
                return new List<PickingHistoricoDTO>();
            }
            List<int> pickings = actividad.Select(a => a.Numero).Distinct().ToList();
            // Un DbContext no admite dos consultas a la vez: una detrás de otra.
            List<FilaLineaHistorico> todasLineas = await repositorio.LeerLineasPickings(empresa, pickings).ConfigureAwait(false)
                ?? new List<FilaLineaHistorico>();
            List<FilaLecturaHistorico> todasLecturas = await repositorio.LeerLecturasPickings(empresa, pickings).ConfigureAwait(false)
                ?? new List<FilaLecturaHistorico>();
            List<FilaTerminadaHistorico> todosTerminados = await repositorio.LeerPickingsTerminados(empresa, pickings).ConfigureAwait(false)
                ?? new List<FilaTerminadaHistorico>();

            return actividad
                .GroupBy(a => a.Numero)
                .Select(g =>
                {
                    int numero = g.Key;
                    List<FilaLineaHistorico> suyas = todasLineas.Where(l => l.Picking == numero).ToList();
                    List<FilaLecturaHistorico> leidas = todasLecturas.Where(l => l.Picking == numero).ToList();
                    FilaTerminadaHistorico terminado = todosTerminados.FirstOrDefault(t => t.Numero == numero);
                    List<LineaPickingHistoricoDTO> repartidas = Repartir(suyas, leidas);
                    return new PickingHistoricoDTO
                    {
                        Picking = numero,
                        Fecha = g.Min(a => a.Fecha),
                        Usuario = UsuarioPicking(terminado, leidas),
                        Pedidos = suyas.Select(l => l.Pedido).Distinct().OrderBy(p => p).ToList(),
                        Lineas = repartidas.Count,
                        LineasRecogidas = repartidas.Count(l => l.Cantidad > 0 && l.Recogida >= l.Cantidad),
                        Terminado = terminado != null
                    };
                })
                .OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Picking)
                .ToList();
        }

        public async Task<PickingHistoricoDetalleDTO> LeerPicking(string empresa, int picking)
        {
            var uno = new[] { picking };
            List<FilaLineaHistorico> lineas = await repositorio.LeerLineasPickings(empresa, uno).ConfigureAwait(false) ?? new List<FilaLineaHistorico>();
            List<FilaLecturaHistorico> lecturas = await repositorio.LeerLecturasPickings(empresa, uno).ConfigureAwait(false) ?? new List<FilaLecturaHistorico>();
            FilaTerminadaHistorico terminado = (await repositorio.LeerPickingsTerminados(empresa, uno).ConfigureAwait(false))?.FirstOrDefault();
            if (lineas.Count == 0 && lecturas.Count == 0 && terminado == null)
            {
                return null;
            }
            DateTime? fecha = lecturas.Count > 0 ? lecturas.Min(l => l.Primera) : terminado?.Fecha;
            return new PickingHistoricoDetalleDTO
            {
                Picking = picking,
                Fecha = fecha,
                Usuario = UsuarioPicking(terminado, lecturas),
                Terminado = terminado != null,
                Lineas = Repartir(lineas.Where(l => l.Picking == picking).ToList(), lecturas)
            };
        }

        public async Task<List<PackingHistoricoDTO>> LeerPackings(string empresa, DateTime desde, DateTime hasta, int? pedido)
        {
            List<FilaActividadHistorico> actividad = await repositorio.LeerPackingsConActividad(empresa, desde, hasta, pedido, MAXIMO_FILAS)
                .ConfigureAwait(false) ?? new List<FilaActividadHistorico>();
            if (actividad.Count == 0)
            {
                return new List<PackingHistoricoDTO>();
            }
            List<int> pedidos = actividad.Select(a => a.Numero).Distinct().ToList();
            List<FilaResumenPackingHistorico> resumen = await repositorio.LeerResumenPackings(empresa, pedidos).ConfigureAwait(false)
                ?? new List<FilaResumenPackingHistorico>();
            return actividad
                .GroupBy(a => a.Numero)
                .Select(g =>
                {
                    FilaResumenPackingHistorico r = resumen.FirstOrDefault(x => x.Pedido == g.Key);
                    return new PackingHistoricoDTO
                    {
                        Pedido = g.Key,
                        Fecha = g.Min(a => a.Fecha),
                        Usuario = SinDominio(r?.UsuarioCajas) ?? SinDominio(r?.UsuarioLecturas),
                        Cajas = r?.Cajas ?? 0,
                        Bultos = r?.BultosEnvio ?? 0
                    };
                })
                .OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Pedido)
                .ToList();
        }

        public async Task<PackingHistoricoDetalleDTO> LeerPacking(string empresa, int pedido)
        {
            List<BultoAlmacenDTO> bultos = await bultosDelPedido(pedido).ConfigureAwait(false) ?? new List<BultoAlmacenDTO>();
            List<FilaContenidoPackingHistorico> contenido = await repositorio.LeerContenidoPacking(empresa, pedido).ConfigureAwait(false)
                ?? new List<FilaContenidoPackingHistorico>();
            if (bultos.Count == 0 && contenido.Count == 0)
            {
                return null;
            }
            FilaResumenPackingHistorico resumen = (await repositorio.LeerResumenPackings(empresa, new[] { pedido }).ConfigureAwait(false))?.FirstOrDefault();

            var cajas = new List<CajaPackingHistoricoDTO>();
            foreach (var numero in bultos.Select(b => new { b.Bulto, b.Picking })
                .Concat(contenido.Select(c => new { c.Bulto, c.Picking }))
                .Distinct()
                .OrderBy(b => b.Bulto).ThenBy(b => b.Picking))
            {
                BultoAlmacenDTO bulto = bultos.FirstOrDefault(b => b.Bulto == numero.Bulto && b.Picking == numero.Picking);
                List<FilaContenidoPackingHistorico> suyo = contenido.Where(c => c.Bulto == numero.Bulto && c.Picking == numero.Picking).ToList();
                List<ContenidoCajaHistoricoDTO> productos = suyo
                    .GroupBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new ContenidoCajaHistoricoDTO
                    {
                        Producto = p.Key,
                        Nombre = p.Select(x => x.Nombre).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                        Cantidad = p.Sum(x => x.Cantidad),
                        Usuario = SinDominio(p.OrderByDescending(x => x.Cantidad).ThenByDescending(x => x.Ultima).First().Usuario),
                        FechaHora = p.Max(x => x.Ultima)
                    })
                    .Where(p => p.Cantidad > 0)
                    .OrderBy(p => p.Producto, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                FilaContenidoPackingHistorico ultima = suyo.OrderByDescending(c => c.Ultima).FirstOrDefault();
                cajas.Add(new CajaPackingHistoricoDTO
                {
                    Bulto = numero.Bulto,
                    Picking = numero.Picking,
                    Usuario = SinDominio(bulto?.Usuario) ?? SinDominio(ultima?.Usuario),
                    FechaHora = bulto?.FechaFoto ?? ultima?.Ultima,
                    TieneFoto = bulto?.TieneFoto == true,
                    UrlFoto = bulto?.RutaFotoPublica,
                    Lineas = productos
                });
            }

            DateTime? primeraLectura = contenido.Count > 0 ? contenido.Min(c => c.Ultima) : (DateTime?)null;
            DateTime? primeraFoto = bultos.Where(b => b.FechaFoto.HasValue).Select(b => b.FechaFoto).DefaultIfEmpty(null).Min();
            return new PackingHistoricoDetalleDTO
            {
                Pedido = pedido,
                Fecha = new[] { primeraLectura, primeraFoto }.Where(f => f.HasValue).DefaultIfEmpty(null).Min(),
                Usuario = SinDominio(resumen?.UsuarioCajas) ?? SinDominio(resumen?.UsuarioLecturas),
                Bultos = resumen?.BultosEnvio ?? 0,
                Cajas = cajas
            };
        }

        /// <summary>
        /// Reparte lo leído de cada producto (picking por ola: se lee por producto, no por pedido) entre sus líneas en orden de
        /// pedido y línea; después, lo dado por falta entre lo que queda. Las líneas salen en ese mismo orden.
        /// </summary>
        internal static List<LineaPickingHistoricoDTO> Repartir(IReadOnlyCollection<FilaLineaHistorico> lineas, IReadOnlyCollection<FilaLecturaHistorico> lecturas)
        {
            var resultado = new List<LineaPickingHistoricoDTO>();
            var porProducto = (lecturas ?? new List<FilaLecturaHistorico>())
                .GroupBy(l => l.Producto?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new
                {
                    Leidas = Math.Max(0, g.Sum(x => x.Leidas)),
                    Faltas = Math.Max(0, g.Sum(x => x.Faltas)),
                    Usuario = SinDominio(g.GroupBy(x => x.Usuario, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(u => u.Sum(x => x.Leidas)).ThenByDescending(u => u.Max(x => x.Ultima)).First().Key),
                    Ultima = g.Max(x => x.Ultima)
                }, StringComparer.OrdinalIgnoreCase);
            var quedanLeidas = porProducto.ToDictionary(p => p.Key, p => p.Value.Leidas, StringComparer.OrdinalIgnoreCase);
            var quedanFaltas = porProducto.ToDictionary(p => p.Key, p => p.Value.Faltas, StringComparer.OrdinalIgnoreCase);

            foreach (FilaLineaHistorico linea in (lineas ?? new List<FilaLineaHistorico>()).OrderBy(l => l.Pedido).ThenBy(l => l.LineaPedido))
            {
                string producto = linea.Producto?.Trim() ?? string.Empty;
                int cantidad = Math.Max(0, linea.Cantidad);
                int recogida = 0;
                int falta = 0;
                if (quedanLeidas.TryGetValue(producto, out int leidas))
                {
                    recogida = Math.Min(cantidad, leidas);
                    quedanLeidas[producto] = leidas - recogida;
                    falta = Math.Min(cantidad - recogida, quedanFaltas[producto]);
                    quedanFaltas[producto] -= falta;
                }
                porProducto.TryGetValue(producto, out var leido);
                resultado.Add(new LineaPickingHistoricoDTO
                {
                    Pedido = linea.Pedido,
                    Cliente = linea.Cliente,
                    NombreCliente = linea.NombreCliente,
                    Producto = producto,
                    Nombre = linea.Nombre,
                    Cantidad = linea.Cantidad,
                    Recogida = recogida,
                    Falta = falta > 0,
                    Usuario = leido?.Usuario,
                    FechaHora = leido?.Ultima
                });
            }
            return resultado;
        }

        /// <summary>Quien lo terminó; si no, quien más ha leído (y, a igualdad, el último).</summary>
        private static string UsuarioPicking(FilaTerminadaHistorico terminado, IEnumerable<FilaLecturaHistorico> lecturas)
        {
            string quienTermino = SinDominio(terminado?.Usuario);
            if (quienTermino != null)
            {
                return quienTermino;
            }
            return SinDominio((lecturas ?? Enumerable.Empty<FilaLecturaHistorico>())
                .GroupBy(l => l.Usuario, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Sum(x => Math.Abs(x.Leidas) + Math.Abs(x.Faltas))).ThenByDescending(g => g.Max(x => x.Ultima))
                .FirstOrDefault()?.Key);
        }

        /// <summary>«Alfredo» a partir de «NUEVAVISION\Alfredo». Null si viene vacío.</summary>
        public static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 && barra < limpio.Length - 1 ? limpio.Substring(barra + 1) : limpio;
        }
    }
}
