using Elmah;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#709: una fila de PedidosOrigenPush (el pedido de la app se hizo tras abrirla desde una push).</summary>
    public class FilaOrigenPushPedido
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Tipo { get; set; }
        public string Campana { get; set; }
        public DateTime? FechaApertura { get; set; }
        public DateTime FechaPedido { get; set; }
    }

    /// <summary>NestoAPI#709: un pedido de la app del periodo del informe, con su push (Tipo null = sin push).</summary>
    public class FilaPedidoAppOrigenPush
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime FechaPedido { get; set; }
        public string Tipo { get; set; }
        public string Campana { get; set; }
        public decimal BaseImponible { get; set; }
    }

    /// <summary>NestoAPI#709: la tabla PedidosOrigenPush (Scripts/Issue709_PedidosOrigenPush.sql) y la lectura del informe.</summary>
    public interface IRepositorioOrigenPushPedidos
    {
        /// <summary>¿Se ha lanzado Scripts/Issue709_PedidosOrigenPush.sql?</summary>
        Task<bool> TablaExiste();
        Task Insertar(FilaOrigenPushPedido fila);
        /// <summary>
        /// Los pedidos de la app con fecha entre <paramref name="desde"/> (incluido) y <paramref name="hasta"/> (excluido), cada uno con su
        /// push si la tiene. Sin la tabla, todos sin push.
        /// </summary>
        Task<List<FilaPedidoAppOrigenPush>> LeerPedidosApp(DateTime desde, DateTime hasta);
    }

    /// <summary>NestoAPI#709: SQL a mano, fuera del EDMX, como CorreosPostCompraRecomendaciones (#707): sin la tabla, la API no se cae.</summary>
    public class RepositorioOrigenPushPedidosSql : IRepositorioOrigenPushPedidos
    {
        internal const int LONGITUD_TIPO = 40;
        internal const int LONGITUD_CAMPANA = 60;

        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.PedidosOrigenPush', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        // Un pedido por fila. Si se reintenta el mismo pedido (no debería), no se duplica.
        internal const string SQL_INSERTAR = @"
IF NOT EXISTS (SELECT 1 FROM dbo.PedidosOrigenPush WHERE Empresa = @empresa AND Pedido = @pedido)
    INSERT INTO dbo.PedidosOrigenPush (Empresa, Pedido, Tipo, Campana, FechaApertura, FechaPedido)
    VALUES (@empresa, @pedido, @tipo, @campana, @fechaApertura, @fechaPedido)";

        // Pedidos de la app: los que crea el cliente desde NestoTiendas se guardan a nombre de APP\<cliente> (ConstructorPedidoCliente.
        // UsuarioDelPedido) y con las líneas en forma de venta APP (salvo El Edén). Se cuenta cualquiera de las dos marcas, y también
        // cualquier pedido con fila en PedidosOrigenPush. Base imponible de las líneas desde pendiente (sin presupuestos, -3).
        private const string SQL_PEDIDOS_APP_FROM = @"
FROM dbo.CabPedidoVta cab WITH (NOLOCK)
INNER JOIN dbo.LinPedidoVta l WITH (NOLOCK) ON l.Empresa = cab.Empresa AND l.Número = cab.Número";

        private static readonly string SQL_PEDIDOS_APP_WHERE = @"
WHERE cab.Fecha >= @desde AND cab.Fecha < @hasta AND l.Estado >= " + Constantes.EstadosLineaVenta.PENDIENTE;

        internal static readonly string SQL_PEDIDOS_APP_CON_TABLA = @"
SELECT RTRIM(cab.Empresa) AS Empresa, cab.Número AS Pedido, MAX(cab.Fecha) AS FechaPedido, op.Tipo, op.Campana,
       CAST(ISNULL(SUM(l.[Base Imponible]), 0) AS decimal(19, 4)) AS BaseImponible" + SQL_PEDIDOS_APP_FROM + @"
LEFT JOIN dbo.PedidosOrigenPush op WITH (NOLOCK) ON op.Empresa = cab.Empresa AND op.Pedido = cab.Número" + SQL_PEDIDOS_APP_WHERE + @"
GROUP BY cab.Empresa, cab.Número, op.Tipo, op.Campana
HAVING MAX(CASE WHEN cab.Usuario LIKE 'APP\%' OR l.[Forma Venta] = 'APP' OR op.Pedido IS NOT NULL THEN 1 ELSE 0 END) = 1";

        internal static readonly string SQL_PEDIDOS_APP_SIN_TABLA = @"
SELECT RTRIM(cab.Empresa) AS Empresa, cab.Número AS Pedido, MAX(cab.Fecha) AS FechaPedido,
       CAST(NULL AS varchar(40)) AS Tipo, CAST(NULL AS varchar(60)) AS Campana,
       CAST(ISNULL(SUM(l.[Base Imponible]), 0) AS decimal(19, 4)) AS BaseImponible" + SQL_PEDIDOS_APP_FROM + SQL_PEDIDOS_APP_WHERE + @"
GROUP BY cab.Empresa, cab.Número
HAVING MAX(CASE WHEN cab.Usuario LIKE 'APP\%' OR l.[Forma Venta] = 'APP' THEN 1 ELSE 0 END) = 1";

        public async Task<bool> TablaExiste()
        {
            using (var db = new NVEntities())
            {
                return await db.Database.SqlQuery<bool>(SQL_EXISTE).FirstAsync().ConfigureAwait(false);
            }
        }

        public async Task Insertar(FilaOrigenPushPedido fila)
        {
            if (fila == null)
            {
                return;
            }
            using (var db = new NVEntities())
            {
                await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR,
                    Texto("@empresa", SqlDbType.Char, fila.Empresa, 3),
                    new SqlParameter("@pedido", SqlDbType.Int) { Value = fila.Pedido },
                    Texto("@tipo", SqlDbType.VarChar, fila.Tipo, LONGITUD_TIPO),
                    Texto("@campana", SqlDbType.VarChar, fila.Campana, LONGITUD_CAMPANA),
                    new SqlParameter("@fechaApertura", SqlDbType.DateTime) { Value = (object)fila.FechaApertura ?? DBNull.Value },
                    new SqlParameter("@fechaPedido", SqlDbType.DateTime) { Value = fila.FechaPedido }).ConfigureAwait(false);
            }
        }

        public async Task<List<FilaPedidoAppOrigenPush>> LeerPedidosApp(DateTime desde, DateTime hasta)
        {
            using (var db = new NVEntities())
            {
                bool existe = await db.Database.SqlQuery<bool>(SQL_EXISTE).FirstAsync().ConfigureAwait(false);
                return await db.Database.SqlQuery<FilaPedidoAppOrigenPush>(existe ? SQL_PEDIDOS_APP_CON_TABLA : SQL_PEDIDOS_APP_SIN_TABLA,
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde },
                    new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta }).ToListAsync().ConfigureAwait(false);
            }
        }

        private static SqlParameter Texto(string nombre, SqlDbType tipo, string valor, int largo)
        {
            string limpio = valor?.Trim();
            if (limpio != null && limpio.Length > largo)
            {
                limpio = limpio.Substring(0, largo);
            }
            return new SqlParameter(nombre, tipo, largo) { Value = string.IsNullOrEmpty(limpio) ? (object)DBNull.Value : limpio };
        }
    }

    /// <summary>
    /// NestoAPI#709 (TNV#95): guarda con qué push se abrió la app antes de un pedido. Best-effort: sin datos de push no hace nada; sin
    /// tabla o con un fallo al insertar, un aviso a ELMAH (uno por proceso) y el pedido sigue creado como si nada.
    /// </summary>
    public static class RegistroOrigenPushPedido
    {
        /// <summary>La app olvida la push a los 7 días; aquí se deja un día de margen por relojes y zonas horarias.</summary>
        internal const int DIAS_VALIDEZ = 8;

        private static int _avisadoSinTabla;
        private static int _avisadoFallo;

        /// <summary>
        /// La fila a guardar, o null si la petición no trae push (o la push es de hace más de <see cref="DIAS_VALIDEZ"/> días). Una
        /// fecha de apertura en el futuro (reloj del móvil mal) no se cree: se guarda sin fecha.
        /// </summary>
        public static FilaOrigenPushPedido ConstruirFila(PedidoClienteRequest peticion, string empresa, int pedido, DateTime fechaPedido)
        {
            string tipo = peticion?.OrigenPushTipo?.Trim();
            if (string.IsNullOrEmpty(tipo) || pedido <= 0)
            {
                return null;
            }
            DateTime? apertura = peticion.OrigenPushFecha;
            if (apertura.HasValue)
            {
                if (apertura.Value < fechaPedido.AddDays(-DIAS_VALIDEZ))
                {
                    return null;
                }
                if (apertura.Value > fechaPedido.AddHours(1))
                {
                    apertura = null;
                }
            }
            string campana = peticion.OrigenPushCampana?.Trim();
            return new FilaOrigenPushPedido
            {
                Empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(),
                Pedido = pedido,
                Tipo = tipo,
                Campana = string.IsNullOrEmpty(campana) ? null : campana,
                FechaApertura = apertura,
                FechaPedido = fechaPedido
            };
        }

        /// <summary>
        /// Guarda el origen del pedido. Nunca lanza: devuelve true si lo ha guardado. <paramref name="registrarError"/> es para los tests
        /// (por defecto, ELMAH).
        /// </summary>
        public static async Task<bool> Registrar(IRepositorioOrigenPushPedidos repositorio, PedidoClienteRequest peticion, string empresa,
            int pedido, DateTime fechaPedido, Action<Exception> registrarError = null)
        {
            Action<Exception> avisar = registrarError ?? (ex => ErrorLog.GetDefault(null)?.Log(new Error(ex)));
            try
            {
                FilaOrigenPushPedido fila = ConstruirFila(peticion, empresa, pedido, fechaPedido);
                if (fila == null || repositorio == null)
                {
                    return false;
                }
                if (!await repositorio.TablaExiste().ConfigureAwait(false))
                {
                    if (registrarError != null || Interlocked.Exchange(ref _avisadoSinTabla, 1) == 0)
                    {
                        avisar(new Exception("[OrigenPush] NestoAPI#709: no existe la tabla PedidosOrigenPush (falta lanzar " +
                            "Scripts/Issue709_PedidosOrigenPush.sql). Los pedidos se crean, pero no se guarda la push que los trajo."));
                    }
                    return false;
                }
                await repositorio.Insertar(fila).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (registrarError != null || Interlocked.Exchange(ref _avisadoFallo, 1) == 0)
                    {
                        avisar(new Exception($"[OrigenPush] NestoAPI#709: no se ha podido guardar la push del pedido {pedido}. " +
                            $"El pedido está creado. {ex.Message}", ex));
                    }
                }
                catch
                {
                    // Ni el aviso puede afectar al pedido
                }
                return false;
            }
        }
    }

    /// <summary>NestoAPI#709: pedidos y base imponible de un grupo (una push concreta o «sin push»).</summary>
    public class GrupoOrigenPushDTO
    {
        /// <summary>El <c>tipo</c> de la push (reposicion, chequeregalo…); null en los pedidos de la app sin push.</summary>
        public string Tipo { get; set; }
        public string Campana { get; set; }
        public bool SinPush => Tipo == null;
        public int Pedidos { get; set; }
        public decimal BaseImponible { get; set; }
    }

    /// <summary>NestoAPI#709: una semana (de lunes a domingo) del informe.</summary>
    public class SemanaOrigenPushDTO
    {
        public DateTime Lunes { get; set; }
        public int PedidosApp { get; set; }
        public int PedidosConPush { get; set; }
        public decimal BaseImponibleApp { get; set; }
        public decimal BaseImponibleConPush { get; set; }
        public List<GrupoOrigenPushDTO> Grupos { get; set; } = new List<GrupoOrigenPushDTO>();
    }

    /// <summary>NestoAPI#709: GET api/PedidosCliente/OrigenPush/Informe.</summary>
    public class InformeOrigenPushDTO
    {
        public DateTime Desde { get; set; }
        /// <summary>Exclusivo.</summary>
        public DateTime Hasta { get; set; }
        public List<SemanaOrigenPushDTO> Semanas { get; set; } = new List<SemanaOrigenPushDTO>();
        /// <summary>Del periodo entero, por tipo y campaña, con los pedidos de la app sin push al final.</summary>
        public List<GrupoOrigenPushDTO> Totales { get; set; } = new List<GrupoOrigenPushDTO>();
    }

    /// <summary>NestoAPI#709: el agregado del informe de pedidos por push (puro, para poder probarlo).</summary>
    public static class InformeOrigenPushPedidos
    {
        internal const int SEMANAS_POR_DEFECTO = 12;

        public static DateTime LunesDe(DateTime fecha)
        {
            int dias = ((int)fecha.DayOfWeek + 6) % 7; // lunes = 0
            return fecha.Date.AddDays(-dias);
        }

        public static InformeOrigenPushDTO Calcular(IEnumerable<FilaPedidoAppOrigenPush> filas, DateTime desde, DateTime hasta)
        {
            List<FilaPedidoAppOrigenPush> lista = (filas ?? Enumerable.Empty<FilaPedidoAppOrigenPush>())
                .Where(f => f != null)
                .Select(f => new FilaPedidoAppOrigenPush
                {
                    Empresa = f.Empresa?.Trim(),
                    Pedido = f.Pedido,
                    FechaPedido = f.FechaPedido,
                    Tipo = string.IsNullOrWhiteSpace(f.Tipo) ? null : f.Tipo.Trim(),
                    Campana = string.IsNullOrWhiteSpace(f.Campana) ? null : f.Campana.Trim(),
                    BaseImponible = f.BaseImponible
                })
                .ToList();

            var informe = new InformeOrigenPushDTO
            {
                Desde = desde,
                Hasta = hasta,
                Totales = Agrupar(lista)
            };
            informe.Semanas = lista
                .GroupBy(f => LunesDe(f.FechaPedido))
                .OrderBy(g => g.Key)
                .Select(g => new SemanaOrigenPushDTO
                {
                    Lunes = g.Key,
                    PedidosApp = g.Count(),
                    PedidosConPush = g.Count(f => f.Tipo != null),
                    BaseImponibleApp = g.Sum(f => f.BaseImponible),
                    BaseImponibleConPush = g.Where(f => f.Tipo != null).Sum(f => f.BaseImponible),
                    Grupos = Agrupar(g)
                })
                .ToList();
            return informe;
        }

        private static List<GrupoOrigenPushDTO> Agrupar(IEnumerable<FilaPedidoAppOrigenPush> filas)
        {
            return filas
                .GroupBy(f => new { f.Tipo, f.Campana })
                .Select(g => new GrupoOrigenPushDTO
                {
                    Tipo = g.Key.Tipo,
                    Campana = g.Key.Campana,
                    Pedidos = g.Count(),
                    BaseImponible = g.Sum(f => f.BaseImponible)
                })
                .OrderBy(g => g.Tipo == null ? 1 : 0)
                .ThenByDescending(g => g.Pedidos)
                .ThenBy(g => g.Tipo)
                .ThenBy(g => g.Campana)
                .ToList();
        }
    }
}
