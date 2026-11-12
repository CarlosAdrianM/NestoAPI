using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega
{
    public interface IRepositorioFotosFechaEntrega
    {
        /// <summary>¿Se ha lanzado Scripts/FechasEntregaFotos.sql? Sin la tabla no se hace nada.</summary>
        bool TablaExiste();
        Task<List<int>> LeerPedidosPendientes(string empresa);
        /// <summary>La última foto de cada producto del pedido (clave: producto sin espacios).</summary>
        Task<Dictionary<string, FotoFechaEntrega>> LeerUltimas(string empresa, int pedido);
        Task Insertar(FotoFechaEntrega foto);
        Task Sustituir(int id, FotoFechaEntrega foto);
        Task<List<ErrorFechaEntregaFila>> LeerErrores(DateTime desde);
    }

    public class ResultadoPasadaFotosFechaEntrega
    {
        public int Pedidos { get; set; }
        public int Insertadas { get; set; }
        public int Sustituidas { get; set; }
        public int Errores { get; set; }
    }

    /// <summary>Fechas de entrega fiables (corte 1, en sombra): la pasada de madrugada que hace las fotos.</summary>
    public class ServicioFotosFechaEntrega
    {
        private readonly IRepositorioFotosFechaEntrega repositorio;
        private readonly Func<string, int, Task<FechaEntregaAgenciaDTO>> calcular;
        private readonly Action<Exception> registrar;

        public ServicioFotosFechaEntrega(IRepositorioFotosFechaEntrega repositorio, Func<string, int, Task<FechaEntregaAgenciaDTO>> calcular,
            Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.calcular = calcular ?? throw new ArgumentNullException(nameof(calcular));
            this.registrar = registrar ?? (_ => { });
        }

        public async Task<ResultadoPasadaFotosFechaEntrega> Pasada(string empresa, DateTime hoy)
        {
            var resultado = new ResultadoPasadaFotosFechaEntrega();
            if (!repositorio.TablaExiste())
            {
                return resultado;
            }
            List<int> pedidos = await repositorio.LeerPedidosPendientes(empresa).ConfigureAwait(false) ?? new List<int>();
            Exception primerError = null;
            foreach (int pedido in pedidos.Distinct())
            {
                resultado.Pedidos++;
                try
                {
                    FechaEntregaAgenciaDTO calculo = await calcular(empresa, pedido).ConfigureAwait(false);
                    List<FotoFechaEntrega> fotos = ReglasFotosFechaEntrega.Construir(empresa, pedido, calculo, hoy);
                    if (!fotos.Any())
                    {
                        continue;
                    }
                    Dictionary<string, FotoFechaEntrega> ultimas = await repositorio.LeerUltimas(empresa, pedido).ConfigureAwait(false)
                        ?? new Dictionary<string, FotoFechaEntrega>();
                    foreach (FotoFechaEntrega foto in fotos)
                    {
                        _ = ultimas.TryGetValue(foto.Producto, out FotoFechaEntrega ultima);
                        switch (ReglasFotosFechaEntrega.Decidir(ultima, foto))
                        {
                            case AccionFotoFechaEntrega.Insertar:
                                await repositorio.Insertar(foto).ConfigureAwait(false);
                                resultado.Insertadas++;
                                break;
                            case AccionFotoFechaEntrega.Sustituir:
                                await repositorio.Sustituir(ultima.Id, foto).ConfigureAwait(false);
                                resultado.Sustituidas++;
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    resultado.Errores++;
                    primerError = primerError ?? new Exception($"[Fechas de entrega en sombra] Pedido {pedido}: {ex.Message}", ex);
                }
            }
            if (primerError != null)
            {
                // Una sola entrada en ELMAH por pasada, con el número de pedidos que han fallado.
                registrar(new Exception($"{primerError.Message} ({resultado.Errores} pedidos con error de {resultado.Pedidos})", primerError.InnerException));
            }
            return resultado;
        }
    }

    public class RepositorioFotosFechaEntrega : IRepositorioFotosFechaEntrega
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.FechasEntregaFotos', 'U') IS NULL OR OBJECT_ID('dbo.vFechasEntregaError', 'V') IS NULL THEN 0 ELSE 1 END AS bit)";

        // Mismo filtro que la promesa en riesgo (#693): líneas de producto pendientes (-1 a 1). Pocos pedidos (unos 135 el 11/11/26).
        internal const string SQL_PEDIDOS_PENDIENTES = @"SELECT DISTINCT l.Número
FROM dbo.LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.Estado >= -1 AND l.Estado <= 1 AND l.TipoLinea = 1";

        internal const string COLUMNAS = "Id, RTRIM(Empresa) AS Empresa, Pedido, RTRIM(Producto) AS Producto, FechaFoto, Unidades, FechaSalida, Origen, Caso, " +
            "PedidoProveedor, FechaPrevistaProveedor, PrevistaVencida, FechaEntregaAgencia, FechaPrometidaAlCrear";

        internal const string SQL_ULTIMAS = "SELECT " + COLUMNAS + @" FROM (
  SELECT f.*, ROW_NUMBER() OVER (PARTITION BY f.Producto ORDER BY f.FechaFoto DESC, f.Id DESC) AS Orden
  FROM dbo.FechasEntregaFotos f WHERE f.Empresa = @empresa AND f.Pedido = @pedido) x WHERE x.Orden = 1";

        internal const string SQL_INSERTAR = @"INSERT INTO dbo.FechasEntregaFotos (Empresa, Pedido, Producto, FechaFoto, Unidades, FechaSalida, Origen, Caso,
  PedidoProveedor, FechaPrevistaProveedor, PrevistaVencida, FechaEntregaAgencia, FechaPrometidaAlCrear)
SELECT @empresa, @pedido, @producto, @fechaFoto, @unidades, @fechaSalida, @origen, @caso, @pedidoProveedor, @fechaPrevistaProveedor, @previstaVencida,
  @fechaEntregaAgencia, @fechaPrometidaAlCrear
WHERE NOT EXISTS (SELECT 1 FROM dbo.FechasEntregaFotos WHERE Empresa = @empresa AND Pedido = @pedido AND Producto = @producto AND FechaFoto = @fechaFoto)";

        internal const string SQL_SUSTITUIR = @"UPDATE dbo.FechasEntregaFotos SET Unidades = @unidades, FechaSalida = @fechaSalida, Origen = @origen, Caso = @caso,
  PedidoProveedor = @pedidoProveedor, FechaPrevistaProveedor = @fechaPrevistaProveedor, PrevistaVencida = @previstaVencida,
  FechaEntregaAgencia = @fechaEntregaAgencia, FechaPrometidaAlCrear = @fechaPrometidaAlCrear, FechaHora = GETDATE()
WHERE Id = @id";

        internal const string SQL_ERRORES = @"SELECT RTRIM(Empresa) AS Empresa, Pedido, RTRIM(Producto) AS Producto, OrigenPrimera, RTRIM(ProveedorPrimera) AS ProveedorPrimera,
  FechaSalidaPrimera, OrigenUltima, RTRIM(ProveedorUltima) AS ProveedorUltima, FechaSalidaUltima, Fotos, FechaAlbaran, ErrorPrimeraDias, ErrorUltimaDias
FROM dbo.vFechasEntregaError WHERE FechaAlbaran >= @desde";

        private readonly Func<NVEntities> crearContexto;

        public RepositorioFotosFechaEntrega(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() => new NVEntities());
        }

        public bool TablaExiste()
        {
            try
            {
                using (NVEntities db = crearContexto())
                {
                    return db.Database.SqlQuery<bool>(SQL_EXISTE).FirstOrDefault();
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<List<int>> LeerPedidosPendientes(string empresa)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<int>(SQL_PEDIDOS_PENDIENTES, Empresa(empresa)).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<Dictionary<string, FotoFechaEntrega>> LeerUltimas(string empresa, int pedido)
        {
            using (NVEntities db = crearContexto())
            {
                List<FotoFechaEntrega> filas = await db.Database.SqlQuery<FotoFechaEntrega>(SQL_ULTIMAS, Empresa(empresa),
                    new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido }).ToListAsync().ConfigureAwait(false);
                return filas.GroupBy(f => f.Producto?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            }
        }

        public async Task Insertar(FotoFechaEntrega foto)
        {
            using (NVEntities db = crearContexto())
            {
                List<SqlParameter> parametros = Parametros(foto);
                parametros.Add(new SqlParameter("@pedido", SqlDbType.Int) { Value = foto.Pedido });
                parametros.Add(new SqlParameter("@producto", SqlDbType.VarChar, 15) { Value = foto.Producto });
                parametros.Add(Fecha("@fechaFoto", foto.FechaFoto.Date));
                parametros.Add(Empresa(foto.Empresa));
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR, parametros.ToArray()).ConfigureAwait(false);
            }
        }

        public async Task Sustituir(int id, FotoFechaEntrega foto)
        {
            using (NVEntities db = crearContexto())
            {
                List<SqlParameter> parametros = Parametros(foto);
                parametros.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_SUSTITUIR, parametros.ToArray()).ConfigureAwait(false);
            }
        }

        public async Task<List<ErrorFechaEntregaFila>> LeerErrores(DateTime desde)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<ErrorFechaEntregaFila>(SQL_ERRORES, Fecha("@desde", desde.Date)).ToListAsync().ConfigureAwait(false);
            }
        }

        private static List<SqlParameter> Parametros(FotoFechaEntrega foto)
        {
            return new List<SqlParameter>
            {
                new SqlParameter("@unidades", SqlDbType.Int) { Value = foto.Unidades },
                Fecha("@fechaSalida", foto.FechaSalida),
                new SqlParameter("@origen", SqlDbType.VarChar, 20) { Value = (object)foto.Origen ?? DBNull.Value },
                new SqlParameter("@caso", SqlDbType.VarChar, 20) { Value = (object)foto.Caso ?? DBNull.Value },
                new SqlParameter("@pedidoProveedor", SqlDbType.Int) { Value = (object)foto.PedidoProveedor ?? DBNull.Value },
                Fecha("@fechaPrevistaProveedor", foto.FechaPrevistaProveedor),
                new SqlParameter("@previstaVencida", SqlDbType.Bit) { Value = foto.PrevistaVencida },
                Fecha("@fechaEntregaAgencia", foto.FechaEntregaAgencia),
                Fecha("@fechaPrometidaAlCrear", foto.FechaPrometidaAlCrear)
            };
        }

        private static SqlParameter Empresa(string empresa)
        {
            return new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim() };
        }

        private static SqlParameter Fecha(string nombre, DateTime? valor)
        {
            return new SqlParameter(nombre, SqlDbType.Date) { Value = valor.HasValue ? (object)valor.Value.Date : DBNull.Value };
        }
    }

    /// <summary>El job de Hangfire «fechas-entrega-fotos-sombra»: de lunes a sábado a las 4:50 (antes de la promesa en riesgo de las 5:40).</summary>
    public static class FotosFechaEntregaJobsService
    {
        public const string JOB_ID = "fechas-entrega-fotos-sombra";
        public const string CRON = "50 4 * * 1-6";

        public static async Task Pasada()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    // Sin el aviso a Compras de proveedor vencido: esta pasada no es «alguien esperando».
                    var fechas = new ServicioFechaEntregaAgencia(new RepositorioFechaEntregaAgencia(db), () => DateTime.Now, GestorFestivos.EsFestivo,
                        ex => ElmahHelper.Log(ex), avisadorProveedorVencido: null,
                        lectorCerteza: ServicioFechaEntregaAgencia.LectorCertezaPorDefecto()); // NestoAPI#853: para el caso EnTransito
                    var servicio = new ServicioFotosFechaEntrega(new RepositorioFotosFechaEntrega(), (empresa, pedido) => fechas.CalcularPedido(empresa, pedido),
                        ex => ElmahHelper.Log(ex, "Sistema (fechas de entrega)"));
                    _ = await servicio.Pasada(Constantes.Empresas.EMPRESA_POR_DEFECTO, DateTime.Today).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Fechas de entrega en sombra] Error en la pasada: {ex.Message}", ex), "Sistema (fechas de entrega)");
            }
        }
    }
}
