using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Proyectos
{
    /// <summary>NestoAPI#901: filtro de GET api/Proyectos.</summary>
    public class FiltroProyectos
    {
        public string Empresa { get; set; }
        public bool SoloAbiertos { get; set; }
        public string Tipo { get; set; }
        /// <summary>Texto en código o nombre.</summary>
        public string Filtro { get; set; }
        public string Producto { get; set; }
        public IList<int> Ids { get; set; }
    }

    /// <summary>NestoAPI#901: filtro de GET api/Proyectos/ApuntesPorImputar (ya validado).</summary>
    public class FiltroApuntesProyecto
    {
        public string Empresa { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        /// <summary>Prefijos de cuenta («6», «7», «623»…).</summary>
        public IList<string> Cuentas { get; set; } = new List<string>();
        /// <summary>Número de proveedor (o de cliente) del asiento.</summary>
        public string Tercero { get; set; }
        public string Documento { get; set; }
        public string Texto { get; set; }
        public bool SoloPendientes { get; set; }
        public int Pagina { get; set; }
        public int Tope { get; set; }
    }

    public class LecturaApuntesProyecto
    {
        public List<FilaApunteContable> Apuntes { get; set; } = new List<FilaApunteContable>();
        public List<FilaImputacion> Imputaciones { get; set; } = new List<FilaImputacion>();
        public bool HayMas { get; set; }
    }

    public class ResultadoCrearProyecto
    {
        public bool CodigoRepetido { get; set; }
        public FilaProyecto Proyecto { get; set; }
    }

    public class ResultadoCambioProyecto
    {
        public DecisionCambioProyecto Decision { get; set; }
        public bool CodigoRepetido { get; set; }
    }

    /// <summary>NestoAPI#901: otra persona está repartiendo el mismo apunte a la vez.</summary>
    public class ApunteOcupadoException : Exception
    {
        public ApunteOcupadoException(int numeroOrden) : base($"El apunte {numeroOrden} lo está repartiendo otra persona ahora mismo: vuelva a intentarlo en unos segundos.") { }
    }

    /// <summary>NestoAPI#901: acceso a dbo.Proyectos y dbo.ProyectosImputaciones (fuera del EDMX) y lectura de Contabilidad.</summary>
    public interface IRepositorioProyectos
    {
        Task<bool> TablasExisten();
        Task<List<FilaProyecto>> LeerProyectos(FiltroProyectos filtro);
        Task<FilaProyecto> LeerProyecto(int id);
        Task<List<ResumenImputacionesProyecto>> LeerResumen(IList<int> ids);
        Task<List<FilaImputacion>> LeerImputacionesDeProyectos(IList<int> ids);
        Task<ResultadoCrearProyecto> Crear(FilaProyecto proyecto, string usuario);
        /// <summary>Lee el proyecto bloqueado y cuántas imputaciones tiene, decide con <paramref name="decidir"/> y escribe en la misma transacción.</summary>
        Task<ResultadoCambioProyecto> Cambiar(int id, Func<FilaProyecto, int, DecisionCambioProyecto> decidir, string usuario);
        Task<LecturaApuntesProyecto> LeerApuntesPorImputar(FiltroApuntesProyecto filtro);
        /// <summary>El apunte (null si no existe) y sus imputaciones (con el proyecto).</summary>
        Task<Tuple<FilaApunteContable, List<FilaImputacion>>> LeerApunte(string empresa, int numeroOrden);
        /// <summary>
        /// Con sp_getapplock «ProyectoImputacionApunte:{empresa}:{nº orden}» en una transacción: lee apunte, imputaciones y proyectos
        /// (bloqueados), decide con <paramref name="decidir"/> y, si procede, borra, inserta y deja el rastro. Todo o nada.
        /// </summary>
        Task<DecisionImputaciones> SustituirImputaciones(string empresa, int numeroOrden, IList<int> proyectosPedidos,
            Func<ContextoImputacionApunte, DecisionImputaciones> decidir, string usuario);
    }

    public class RepositorioProyectos : IRepositorioProyectos
    {
        private const string NOMBRE_APLICACION = "NestoAPI Proyectos";
        internal const string TABLA_RASTRO_PROYECTO = "Proyecto";
        internal const string TABLA_RASTRO_IMPUTACION = "ProyectoImputacion";

        internal const string SQL_TABLAS = "SELECT CASE WHEN OBJECT_ID(N'dbo.Proyectos', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.ProyectosImputaciones', N'U') IS NOT NULL THEN 1 ELSE 0 END";

        internal const string COLUMNAS_PROYECTO = "p.Id, p.Empresa, p.Codigo, p.Nombre, p.Tipo, p.FechaDesde, p.FechaHasta, p.Estado, p.Producto, p.EventoId, " +
            "p.PresupuestoIngresos, p.PresupuestoGastos, p.Comentarios, p.Usuario, p.FechaAlta, p.UsuarioModificacion, p.FechaModificacion";

        internal const string COLUMNAS_IMPUTACION = "pi.Id, pi.ProyectoId, pi.Empresa, pi.NumeroOrdenApunte, pi.Importe, pi.Cuenta, pi.NombreCuenta, pi.FechaApunte, " +
            "pi.Concepto, pi.Documento, pi.Asiento, pi.Debe, pi.Haber, pi.Tercero, pi.TipoTercero, pi.NombreTercero, pi.Comentario, pi.Usuario, pi.Fecha, " +
            "p.Codigo AS CodigoProyecto, p.Nombre AS NombreProyecto, p.Estado AS EstadoProyecto";

        /// <summary>
        /// El apunte con el nombre de la cuenta y el tercero del asiento: el primer ExtractoProveedor o ExtractoCliente del MISMO asiento
        /// (en la 7xx primero el cliente, en las demás el proveedor). Sin asiento, sin tercero.
        /// </summary>
        internal const string SQL_APUNTE_DESDE = @"
FROM dbo.Contabilidad c {0}
LEFT JOIN dbo.PlanCuentas pc WITH (NOLOCK) ON pc.Empresa = c.Empresa AND pc.[Nº Cuenta] = c.[Nº Cuenta]
OUTER APPLY (
    SELECT TOP 1 x.Tercero, x.TipoTercero, x.NombreTercero FROM (
        SELECT * FROM (SELECT TOP 1 ep.Número AS Tercero, N'Proveedor' AS TipoTercero, pr.Nombre AS NombreTercero
            FROM dbo.ExtractoProveedor ep WITH (NOLOCK)
            LEFT JOIN dbo.Proveedores pr WITH (NOLOCK) ON pr.Empresa = ep.Empresa AND pr.Número = ep.Número AND pr.Contacto = ep.Contacto
            WHERE c.Asiento IS NOT NULL AND ep.Empresa = c.Empresa AND ep.Asiento = c.Asiento ORDER BY ep.NºOrden) a
        UNION ALL
        SELECT * FROM (SELECT TOP 1 ec.Número AS Tercero, N'Cliente' AS TipoTercero, cl.Nombre AS NombreTercero
            FROM dbo.ExtractoCliente ec WITH (NOLOCK)
            LEFT JOIN dbo.Clientes cl WITH (NOLOCK) ON cl.Empresa = ec.Empresa AND cl.[Nº Cliente] = ec.Número AND cl.Contacto = ec.Contacto
            WHERE c.Asiento IS NOT NULL AND ec.Empresa = c.Empresa AND ec.Asiento = c.Asiento ORDER BY ec.[Nº Orden]) b
    ) x
    ORDER BY CASE WHEN x.TipoTercero = CASE WHEN c.[Nº Cuenta] LIKE '7%' THEN N'Cliente' ELSE N'Proveedor' END THEN 0 ELSE 1 END
) t";

        internal const string SQL_APUNTE_COLUMNAS = "c.Empresa, c.[Nº Orden] AS NumeroOrden, c.[Nº Cuenta] AS Cuenta, pc.Concepto AS NombreCuenta, c.Fecha, " +
            "c.Concepto, c.[Nº Documento] AS Documento, c.Asiento, c.Diario, c.Debe, c.Haber, t.Tercero, t.TipoTercero, t.NombreTercero";

        /// <summary>Una sola ida y vuelta: la página de apuntes (topada) y las imputaciones de esos apuntes.</summary>
        internal static string SqlApuntesPorImputar(int numeroCuentas) => @"
SET NOCOUNT ON;
CREATE TABLE #a (Empresa char(3) NOT NULL, NumeroOrden int NOT NULL, Cuenta char(10) NULL, NombreCuenta char(75) NULL, Fecha datetime NOT NULL,
    Concepto char(50) NULL, Documento char(10) NULL, Asiento int NULL, Diario char(10) NULL, Debe money NOT NULL, Haber money NOT NULL,
    Tercero char(10) NULL, TipoTercero nvarchar(10) NULL, NombreTercero char(50) NULL, Imputado decimal(18,2) NOT NULL);
INSERT INTO #a
SELECT " + SQL_APUNTE_COLUMNAS + @", ISNULL(i.Imputado, 0)
" + string.Format(CultureInfo.InvariantCulture, SQL_APUNTE_DESDE, "WITH (NOLOCK)") + @"
OUTER APPLY (SELECT SUM(pi.Importe) AS Imputado FROM dbo.ProyectosImputaciones pi WITH (NOLOCK)
             WHERE pi.Empresa = c.Empresa AND pi.NumeroOrdenApunte = c.[Nº Orden]) i
WHERE c.Empresa = @empresa AND c.Fecha >= @desde AND c.Fecha < @hasta
  AND (" + string.Join(" OR ", Enumerable.Range(0, Math.Max(1, numeroCuentas)).Select(n => $"c.[Nº Cuenta] LIKE @cuenta{n}")) + @")
  AND (@documento IS NULL OR c.[Nº Documento] LIKE @documento)
  AND (@texto IS NULL OR c.Concepto LIKE @texto)
  AND (@tercero IS NULL
       OR EXISTS (SELECT 1 FROM dbo.ExtractoProveedor ep2 WITH (NOLOCK) WHERE ep2.Empresa = c.Empresa AND ep2.Asiento = c.Asiento AND ep2.Número = @tercero)
       OR EXISTS (SELECT 1 FROM dbo.ExtractoCliente ec2 WITH (NOLOCK) WHERE ec2.Empresa = c.Empresa AND ec2.Asiento = c.Asiento AND ec2.Número = @tercero))
  AND (@soloPendientes = 0 OR ABS(c.Debe - c.Haber) - ISNULL(i.Imputado, 0) > 0.01)
ORDER BY c.Fecha DESC, c.[Nº Orden] DESC
OFFSET @saltar ROWS FETCH NEXT @filas ROWS ONLY;
SELECT * FROM #a ORDER BY Fecha DESC, NumeroOrden DESC;
SELECT " + COLUMNAS_IMPUTACION + @"
FROM dbo.ProyectosImputaciones pi WITH (NOLOCK)
JOIN #a ON #a.Empresa = pi.Empresa AND #a.NumeroOrden = pi.NumeroOrdenApunte
JOIN dbo.Proyectos p WITH (NOLOCK) ON p.Id = pi.ProyectoId
ORDER BY pi.NumeroOrdenApunte, p.Codigo;
DROP TABLE #a;";

        private static readonly object bloqueo = new object();
        private static DateTime tablasComprobadasHasta = DateTime.MinValue;

        private readonly string cadenaConexion;

        public RepositorioProyectos(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = cadenaConexion;
        }

        public static RepositorioProyectos DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioProyectos(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        private async Task<SqlConnection> Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            await conexion.OpenAsync().ConfigureAwait(false);
            return conexion;
        }

        public async Task<bool> TablasExisten()
        {
            lock (bloqueo)
            {
                if (DateTime.Now < tablasComprobadasHasta)
                {
                    return true;
                }
            }
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            using (var comando = new SqlCommand(SQL_TABLAS, conexion))
            {
                bool hay = Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture) == 1;
                if (hay)
                {
                    lock (bloqueo)
                    {
                        tablasComprobadasHasta = DateTime.Now.AddMinutes(10);
                    }
                }
                return hay;
            }
        }

        // ------------------------------------------------------------------ Proyectos

        public async Task<List<FilaProyecto>> LeerProyectos(FiltroProyectos filtro)
        {
            var sql = new System.Text.StringBuilder($"SELECT TOP 500 {COLUMNAS_PROYECTO} FROM dbo.Proyectos p WITH (NOLOCK) WHERE p.Empresa = @empresa");
            var parametros = new List<SqlParameter> { new SqlParameter("@empresa", filtro.Empresa ?? Models.Constantes.Empresas.EMPRESA_POR_DEFECTO) };
            if (filtro.SoloAbiertos)
            {
                sql.Append(" AND p.Estado = N'Abierto'");
            }
            if (!string.IsNullOrWhiteSpace(filtro.Tipo))
            {
                sql.Append(" AND p.Tipo = @tipo");
                parametros.Add(new SqlParameter("@tipo", SqlDbType.NVarChar, 20) { Value = filtro.Tipo });
            }
            if (!string.IsNullOrWhiteSpace(filtro.Filtro))
            {
                sql.Append(" AND (p.Codigo LIKE @filtro OR p.Nombre LIKE @filtro)");
                parametros.Add(new SqlParameter("@filtro", SqlDbType.NVarChar, 120) { Value = "%" + Like(filtro.Filtro.Trim()) + "%" });
            }
            if (!string.IsNullOrWhiteSpace(filtro.Producto))
            {
                sql.Append(" AND p.Producto = @producto");
                parametros.Add(new SqlParameter("@producto", SqlDbType.NVarChar, 15) { Value = filtro.Producto.Trim() });
            }
            if (filtro.Ids != null && filtro.Ids.Count > 0)
            {
                sql.Append(" AND p.Id IN (" + string.Join(",", filtro.Ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")");
            }
            sql.Append(" ORDER BY p.FechaDesde DESC, p.Codigo");
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            using (var comando = new SqlCommand(sql.ToString(), conexion))
            {
                comando.Parameters.AddRange(parametros.ToArray());
                return await LeerLista(comando, LeerFilaProyecto).ConfigureAwait(false);
            }
        }

        public async Task<FilaProyecto> LeerProyecto(int id)
        {
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            using (var comando = new SqlCommand($"SELECT {COLUMNAS_PROYECTO} FROM dbo.Proyectos p WITH (NOLOCK) WHERE p.Id = @id", conexion))
            {
                comando.Parameters.AddWithValue("@id", id);
                return (await LeerLista(comando, LeerFilaProyecto).ConfigureAwait(false)).FirstOrDefault();
            }
        }

        public async Task<List<ResumenImputacionesProyecto>> LeerResumen(IList<int> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return new List<ResumenImputacionesProyecto>();
            }
            string sql = "SELECT pi.ProyectoId, pi.Cuenta, CASE WHEN pi.Debe >= pi.Haber THEN 1 ELSE -1 END AS Lado, SUM(pi.Importe) AS Importe, COUNT(*) AS Numero " +
                "FROM dbo.ProyectosImputaciones pi WITH (NOLOCK) WHERE pi.ProyectoId IN (" + string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ") " +
                "GROUP BY pi.ProyectoId, pi.Cuenta, CASE WHEN pi.Debe >= pi.Haber THEN 1 ELSE -1 END";
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            using (var comando = new SqlCommand(sql, conexion))
            {
                return await LeerLista(comando, r => new ResumenImputacionesProyecto
                {
                    ProyectoId = r.GetInt32(0),
                    Cuenta = Texto(r, "Cuenta"),
                    Lado = r.GetInt32(2),
                    Importe = r.GetDecimal(3),
                    Numero = r.GetInt32(4)
                }).ConfigureAwait(false);
            }
        }

        public async Task<List<FilaImputacion>> LeerImputacionesDeProyectos(IList<int> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return new List<FilaImputacion>();
            }
            string sql = $"SELECT {COLUMNAS_IMPUTACION} FROM dbo.ProyectosImputaciones pi WITH (NOLOCK) JOIN dbo.Proyectos p WITH (NOLOCK) ON p.Id = pi.ProyectoId " +
                "WHERE pi.ProyectoId IN (" + string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ") ORDER BY pi.FechaApunte, pi.NumeroOrdenApunte";
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            using (var comando = new SqlCommand(sql, conexion))
            {
                return await LeerLista(comando, LeerFilaImputacion).ConfigureAwait(false);
            }
        }

        public async Task<ResultadoCrearProyecto> Crear(FilaProyecto proyecto, string usuario)
        {
            const string sql = @"INSERT INTO dbo.Proyectos (Empresa, Codigo, Nombre, Tipo, FechaDesde, FechaHasta, Estado, Producto, EventoId,
    PresupuestoIngresos, PresupuestoGastos, Comentarios, Usuario, FechaAlta)
VALUES (@empresa, @codigo, @nombre, @tipo, @desde, @hasta, N'Abierto', @producto, @evento, @presIngresos, @presGastos, @comentarios, @usuario, GETDATE());
SELECT CAST(SCOPE_IDENTITY() AS int);";
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    int id;
                    using (var comando = new SqlCommand(sql, conexion, transaccion))
                    {
                        ParametrosProyecto(comando, proyecto, usuario);
                        id = Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
                    }
                    FilaProyecto creado = proyecto.Copia();
                    creado.Id = id;
                    creado.Estado = ReglasProyectos.ESTADO_ABIERTO;
                    creado.Usuario = usuario;
                    creado.FechaAlta = DateTime.Now;
                    await Rastro(conexion, transaccion, TABLA_RASTRO_PROYECTO, "(nuevo)", JsonConvert.SerializeObject(creado), usuario).ConfigureAwait(false);
                    transaccion.Commit();
                    return new ResultadoCrearProyecto { Proyecto = creado };
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    Deshacer(transaccion);
                    return new ResultadoCrearProyecto { CodigoRepetido = true };
                }
                catch
                {
                    Deshacer(transaccion);
                    throw;
                }
            }
        }

        public async Task<ResultadoCambioProyecto> Cambiar(int id, Func<FilaProyecto, int, DecisionCambioProyecto> decidir, string usuario)
        {
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    FilaProyecto actual;
                    using (var comando = new SqlCommand($"SELECT {COLUMNAS_PROYECTO} FROM dbo.Proyectos p WITH (UPDLOCK, HOLDLOCK) WHERE p.Id = @id", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@id", id);
                        actual = (await LeerLista(comando, LeerFilaProyecto).ConfigureAwait(false)).FirstOrDefault();
                    }
                    int imputaciones = 0;
                    if (actual != null)
                    {
                        using (var comando = new SqlCommand("SELECT COUNT(*) FROM dbo.ProyectosImputaciones WITH (HOLDLOCK) WHERE ProyectoId = @id", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@id", id);
                            imputaciones = Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
                        }
                    }
                    DecisionCambioProyecto decision = decidir(actual, imputaciones);
                    if (decision.Error != null || decision.SinCambios)
                    {
                        transaccion.Rollback();
                        if (decision.Nuevo == null && decision.Error == null)
                        {
                            decision.Nuevo = actual;
                        }
                        return new ResultadoCambioProyecto { Decision = decision };
                    }
                    if (decision.Borrar)
                    {
                        using (var comando = new SqlCommand("DELETE FROM dbo.Proyectos WHERE Id = @id", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@id", id);
                            await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                        await Rastro(conexion, transaccion, TABLA_RASTRO_PROYECTO, JsonConvert.SerializeObject(actual), "(borrado)", usuario).ConfigureAwait(false);
                    }
                    else
                    {
                        const string sql = @"UPDATE dbo.Proyectos SET Codigo = @codigo, Nombre = @nombre, Tipo = @tipo, FechaDesde = @desde, FechaHasta = @hasta,
    Estado = @estado, Producto = @producto, EventoId = @evento, PresupuestoIngresos = @presIngresos, PresupuestoGastos = @presGastos,
    Comentarios = @comentarios, UsuarioModificacion = @usuario, FechaModificacion = GETDATE()
WHERE Id = @id";
                        using (var comando = new SqlCommand(sql, conexion, transaccion))
                        {
                            ParametrosProyecto(comando, decision.Nuevo, usuario);
                            comando.Parameters.Add(new SqlParameter("@estado", SqlDbType.NVarChar, 10) { Value = decision.Nuevo.Estado });
                            comando.Parameters.AddWithValue("@id", id);
                            await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                        decision.Nuevo.UsuarioModificacion = usuario;
                        decision.Nuevo.FechaModificacion = DateTime.Now;
                        await Rastro(conexion, transaccion, TABLA_RASTRO_PROYECTO, JsonConvert.SerializeObject(actual), JsonConvert.SerializeObject(decision.Nuevo), usuario)
                            .ConfigureAwait(false);
                    }
                    transaccion.Commit();
                    return new ResultadoCambioProyecto { Decision = decision };
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    Deshacer(transaccion);
                    return new ResultadoCambioProyecto { CodigoRepetido = true };
                }
                catch
                {
                    Deshacer(transaccion);
                    throw;
                }
            }
        }

        private static void ParametrosProyecto(SqlCommand comando, FilaProyecto p, string usuario)
        {
            comando.Parameters.Add(new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = p.Empresa });
            comando.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 20) { Value = p.Codigo });
            comando.Parameters.Add(new SqlParameter("@nombre", SqlDbType.NVarChar, 100) { Value = p.Nombre });
            comando.Parameters.Add(new SqlParameter("@tipo", SqlDbType.NVarChar, 20) { Value = p.Tipo });
            comando.Parameters.Add(new SqlParameter("@desde", SqlDbType.Date) { Value = p.FechaDesde.Date });
            comando.Parameters.Add(new SqlParameter("@hasta", SqlDbType.Date) { Value = (object)p.FechaHasta?.Date ?? DBNull.Value });
            comando.Parameters.Add(new SqlParameter("@producto", SqlDbType.NVarChar, 15) { Value = (object)p.Producto ?? DBNull.Value });
            comando.Parameters.Add(new SqlParameter("@evento", SqlDbType.Int) { Value = (object)p.EventoId ?? DBNull.Value });
            comando.Parameters.Add(new SqlParameter("@presIngresos", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object)p.PresupuestoIngresos ?? DBNull.Value });
            comando.Parameters.Add(new SqlParameter("@presGastos", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object)p.PresupuestoGastos ?? DBNull.Value });
            comando.Parameters.Add(new SqlParameter("@comentarios", SqlDbType.NVarChar, 1000) { Value = (object)p.Comentarios ?? DBNull.Value });
            comando.Parameters.Add(new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = usuario ?? string.Empty });
        }

        // ------------------------------------------------------------------ Apuntes

        public async Task<LecturaApuntesProyecto> LeerApuntesPorImputar(FiltroApuntesProyecto filtro)
        {
            var lectura = new LecturaApuntesProyecto();
            List<string> cuentas = filtro.Cuentas.Count > 0 ? filtro.Cuentas.ToList() : new List<string> { "6", "7" };
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            using (var comando = new SqlCommand(SqlApuntesPorImputar(cuentas.Count), conexion) { CommandTimeout = 60 })
            {
                comando.Parameters.Add(new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = filtro.Empresa });
                comando.Parameters.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = filtro.Desde.Date });
                comando.Parameters.Add(new SqlParameter("@hasta", SqlDbType.DateTime) { Value = filtro.Hasta.Date.AddDays(1) });
                for (int n = 0; n < cuentas.Count; n++)
                {
                    comando.Parameters.Add(new SqlParameter("@cuenta" + n, SqlDbType.VarChar, 11) { Value = cuentas[n] + "%" });
                }
                comando.Parameters.Add(new SqlParameter("@documento", SqlDbType.VarChar, 11) { Value = string.IsNullOrWhiteSpace(filtro.Documento) ? (object)DBNull.Value : Like(filtro.Documento.Trim()) + "%" });
                comando.Parameters.Add(new SqlParameter("@texto", SqlDbType.VarChar, 60) { Value = string.IsNullOrWhiteSpace(filtro.Texto) ? (object)DBNull.Value : "%" + Like(filtro.Texto.Trim()) + "%" });
                comando.Parameters.Add(new SqlParameter("@tercero", SqlDbType.VarChar, 10) { Value = string.IsNullOrWhiteSpace(filtro.Tercero) ? (object)DBNull.Value : filtro.Tercero.Trim() });
                comando.Parameters.Add(new SqlParameter("@soloPendientes", SqlDbType.Bit) { Value = filtro.SoloPendientes });
                comando.Parameters.Add(new SqlParameter("@saltar", SqlDbType.Int) { Value = filtro.Pagina * filtro.Tope });
                comando.Parameters.Add(new SqlParameter("@filas", SqlDbType.Int) { Value = filtro.Tope + 1 });
                using (SqlDataReader lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await lector.ReadAsync().ConfigureAwait(false))
                    {
                        FilaApunteContable apunte = LeerFilaApunte(lector);
                        apunte.Imputado = lector.GetDecimal(lector.GetOrdinal("Imputado"));
                        lectura.Apuntes.Add(apunte);
                    }
                    if (await lector.NextResultAsync().ConfigureAwait(false))
                    {
                        while (await lector.ReadAsync().ConfigureAwait(false))
                        {
                            lectura.Imputaciones.Add(LeerFilaImputacion(lector));
                        }
                    }
                }
            }
            if (lectura.Apuntes.Count > filtro.Tope)
            {
                lectura.HayMas = true;
                FilaApunteContable sobra = lectura.Apuntes[filtro.Tope];
                lectura.Apuntes.RemoveAt(filtro.Tope);
                lectura.Imputaciones.RemoveAll(i => i.NumeroOrdenApunte == sobra.NumeroOrden);
            }
            return lectura;
        }

        public async Task<Tuple<FilaApunteContable, List<FilaImputacion>>> LeerApunte(string empresa, int numeroOrden)
        {
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            {
                FilaApunteContable apunte = await LeerApunte(conexion, null, empresa, numeroOrden, "WITH (NOLOCK)").ConfigureAwait(false);
                List<FilaImputacion> imputaciones = await LeerImputacionesApunte(conexion, null, empresa, numeroOrden, "WITH (NOLOCK)").ConfigureAwait(false);
                return Tuple.Create(apunte, imputaciones);
            }
        }

        private static async Task<FilaApunteContable> LeerApunte(SqlConnection conexion, SqlTransaction transaccion, string empresa, int numeroOrden, string sugerencia)
        {
            string sql = "SELECT " + SQL_APUNTE_COLUMNAS + string.Format(CultureInfo.InvariantCulture, SQL_APUNTE_DESDE, sugerencia) +
                " WHERE c.Empresa = @empresa AND c.[Nº Orden] = @orden";
            using (var comando = new SqlCommand(sql, conexion, transaccion))
            {
                comando.Parameters.Add(new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa });
                comando.Parameters.AddWithValue("@orden", numeroOrden);
                return (await LeerLista(comando, LeerFilaApunte).ConfigureAwait(false)).FirstOrDefault();
            }
        }

        private static async Task<List<FilaImputacion>> LeerImputacionesApunte(SqlConnection conexion, SqlTransaction transaccion, string empresa, int numeroOrden, string sugerencia)
        {
            string sql = $"SELECT {COLUMNAS_IMPUTACION} FROM dbo.ProyectosImputaciones pi {sugerencia} JOIN dbo.Proyectos p WITH (NOLOCK) ON p.Id = pi.ProyectoId " +
                "WHERE pi.Empresa = @empresa AND pi.NumeroOrdenApunte = @orden ORDER BY p.Codigo";
            using (var comando = new SqlCommand(sql, conexion, transaccion))
            {
                comando.Parameters.Add(new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa });
                comando.Parameters.AddWithValue("@orden", numeroOrden);
                return await LeerLista(comando, LeerFilaImputacion).ConfigureAwait(false);
            }
        }

        public async Task<DecisionImputaciones> SustituirImputaciones(string empresa, int numeroOrden, IList<int> proyectosPedidos,
            Func<ContextoImputacionApunte, DecisionImputaciones> decidir, string usuario)
        {
            using (SqlConnection conexion = await Abrir().ConfigureAwait(false))
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    using (var comando = new SqlCommand("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', " +
                        "@LockOwner = 'Transaction', @LockTimeout = 15000; SELECT @r;", conexion, transaccion))
                    {
                        comando.Parameters.Add(new SqlParameter("@recurso", SqlDbType.NVarChar, 255) { Value = $"ProyectoImputacionApunte:{empresa}:{numeroOrden}" });
                        int r = Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
                        if (r < 0)
                        {
                            throw new ApunteOcupadoException(numeroOrden);
                        }
                    }
                    var contexto = new ContextoImputacionApunte
                    {
                        Apunte = await LeerApunte(conexion, transaccion, empresa, numeroOrden, string.Empty).ConfigureAwait(false),
                        Actuales = await LeerImputacionesApunte(conexion, transaccion, empresa, numeroOrden, string.Empty).ConfigureAwait(false)
                    };
                    List<int> ids = contexto.Actuales.Select(a => a.ProyectoId).Concat(proyectosPedidos ?? new List<int>()).Distinct().ToList();
                    if (ids.Count > 0)
                    {
                        using (var comando = new SqlCommand($"SELECT {COLUMNAS_PROYECTO} FROM dbo.Proyectos p WITH (UPDLOCK, HOLDLOCK) WHERE p.Id IN (" +
                            string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")", conexion, transaccion))
                        {
                            foreach (FilaProyecto p in await LeerLista(comando, LeerFilaProyecto).ConfigureAwait(false))
                            {
                                contexto.Proyectos[p.Id] = p;
                            }
                        }
                    }

                    DecisionImputaciones decision = decidir(contexto);
                    if (decision.Error != null || decision.SinCambios)
                    {
                        transaccion.Rollback();
                        return decision;
                    }
                    if (decision.IdsABorrar.Count > 0)
                    {
                        using (var comando = new SqlCommand("DELETE FROM dbo.ProyectosImputaciones WHERE Empresa = @empresa AND NumeroOrdenApunte = @orden AND Id IN (" +
                            string.Join(",", decision.IdsABorrar.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")", conexion, transaccion))
                        {
                            comando.Parameters.Add(new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa });
                            comando.Parameters.AddWithValue("@orden", numeroOrden);
                            await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                    }
                    foreach (FilaImputacion fila in decision.Insertar)
                    {
                        fila.Id = await Insertar(conexion, transaccion, fila).ConfigureAwait(false);
                    }
                    await Rastro(conexion, transaccion, TABLA_RASTRO_IMPUTACION,
                        TextoRastroReparto(empresa, numeroOrden, contexto.Actuales),
                        TextoRastroReparto(empresa, numeroOrden, decision.Resultado), usuario).ConfigureAwait(false);
                    transaccion.Commit();
                    return decision;
                }
                catch
                {
                    Deshacer(transaccion);
                    throw;
                }
            }
        }

        internal static string TextoRastroReparto(string empresa, int numeroOrden, IEnumerable<FilaImputacion> filas) => JsonConvert.SerializeObject(new
        {
            Empresa = empresa,
            NumeroOrdenApunte = numeroOrden,
            Imputaciones = (filas ?? Enumerable.Empty<FilaImputacion>()).Select(f => new
            {
                f.ProyectoId,
                Proyecto = f.CodigoProyecto?.Trim(),
                f.Importe,
                f.Comentario
            })
        });

        private static async Task<int> Insertar(SqlConnection conexion, SqlTransaction transaccion, FilaImputacion f)
        {
            const string sql = @"INSERT INTO dbo.ProyectosImputaciones (ProyectoId, Empresa, NumeroOrdenApunte, Importe, Cuenta, NombreCuenta, FechaApunte,
    Concepto, Documento, Asiento, Debe, Haber, Tercero, TipoTercero, NombreTercero, Comentario, Usuario, Fecha)
VALUES (@proyecto, @empresa, @orden, @importe, @cuenta, @nombreCuenta, @fechaApunte, @concepto, @documento, @asiento, @debe, @haber,
    @tercero, @tipoTercero, @nombreTercero, @comentario, @usuario, @fecha);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            using (var comando = new SqlCommand(sql, conexion, transaccion))
            {
                object Nulo(string texto, int largo) => string.IsNullOrEmpty(texto) ? (object)DBNull.Value : (texto.Length > largo ? texto.Substring(0, largo) : texto);
                comando.Parameters.AddWithValue("@proyecto", f.ProyectoId);
                comando.Parameters.Add(new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = f.Empresa });
                comando.Parameters.AddWithValue("@orden", f.NumeroOrdenApunte);
                comando.Parameters.Add(new SqlParameter("@importe", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = f.Importe });
                comando.Parameters.Add(new SqlParameter("@cuenta", SqlDbType.NVarChar, 10) { Value = f.Cuenta ?? string.Empty });
                comando.Parameters.Add(new SqlParameter("@nombreCuenta", SqlDbType.NVarChar, 75) { Value = Nulo(f.NombreCuenta, 75) });
                comando.Parameters.Add(new SqlParameter("@fechaApunte", SqlDbType.DateTime) { Value = f.FechaApunte });
                comando.Parameters.Add(new SqlParameter("@concepto", SqlDbType.NVarChar, 50) { Value = Nulo(f.Concepto, 50) });
                comando.Parameters.Add(new SqlParameter("@documento", SqlDbType.NVarChar, 10) { Value = Nulo(f.Documento, 10) });
                comando.Parameters.Add(new SqlParameter("@asiento", SqlDbType.Int) { Value = (object)f.Asiento ?? DBNull.Value });
                comando.Parameters.Add(new SqlParameter("@debe", SqlDbType.Money) { Value = f.Debe });
                comando.Parameters.Add(new SqlParameter("@haber", SqlDbType.Money) { Value = f.Haber });
                comando.Parameters.Add(new SqlParameter("@tercero", SqlDbType.NVarChar, 10) { Value = Nulo(f.Tercero, 10) });
                comando.Parameters.Add(new SqlParameter("@tipoTercero", SqlDbType.NVarChar, 10) { Value = Nulo(f.TipoTercero, 10) });
                comando.Parameters.Add(new SqlParameter("@nombreTercero", SqlDbType.NVarChar, 50) { Value = Nulo(f.NombreTercero, 50) });
                comando.Parameters.Add(new SqlParameter("@comentario", SqlDbType.NVarChar, 200) { Value = Nulo(f.Comentario, 200) });
                comando.Parameters.Add(new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = f.Usuario ?? string.Empty });
                comando.Parameters.Add(new SqlParameter("@fecha", SqlDbType.DateTime) { Value = f.Fecha == default ? DateTime.Now : f.Fecha });
                return Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
        }

        private static async Task Rastro(SqlConnection conexion, SqlTransaction transaccion, string tabla, string anterior, string nuevo, string usuario)
        {
            using (var comando = new SqlCommand("INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@tabla, @anterior, @nuevo, @usuario)", conexion, transaccion))
            {
                comando.Parameters.Add(new SqlParameter("@tabla", SqlDbType.NVarChar, 50) { Value = tabla });
                comando.Parameters.Add(new SqlParameter("@anterior", SqlDbType.NVarChar, -1) { Value = anterior ?? string.Empty });
                comando.Parameters.Add(new SqlParameter("@nuevo", SqlDbType.NVarChar, -1) { Value = nuevo ?? string.Empty });
                comando.Parameters.Add(new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = usuario ?? string.Empty });
                await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static void Deshacer(SqlTransaction transaccion)
        {
            try
            {
                transaccion.Rollback();
            }
            catch (InvalidOperationException)
            {
                // Ya deshecha (XACT_ABORT o la conexión cerrada).
            }
        }

        // ------------------------------------------------------------------ Lectura

        internal static string Like(string texto) => texto.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

        private static async Task<List<T>> LeerLista<T>(SqlCommand comando, Func<SqlDataReader, T> leer)
        {
            var lista = new List<T>();
            using (SqlDataReader lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await lector.ReadAsync().ConfigureAwait(false))
                {
                    lista.Add(leer(lector));
                }
            }
            return lista;
        }

        private static string Texto(SqlDataReader r, string columna)
        {
            int i = r.GetOrdinal(columna);
            return r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)?.Trim();
        }

        private static DateTime? FechaNula(SqlDataReader r, string columna)
        {
            int i = r.GetOrdinal(columna);
            return r.IsDBNull(i) ? (DateTime?)null : r.GetDateTime(i);
        }

        private static decimal? DecimalNulo(SqlDataReader r, string columna)
        {
            int i = r.GetOrdinal(columna);
            return r.IsDBNull(i) ? (decimal?)null : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);
        }

        private static int? EnteroNulo(SqlDataReader r, string columna)
        {
            int i = r.GetOrdinal(columna);
            return r.IsDBNull(i) ? (int?)null : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
        }

        private static FilaProyecto LeerFilaProyecto(SqlDataReader r) => new FilaProyecto
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            Empresa = Texto(r, "Empresa"),
            Codigo = Texto(r, "Codigo"),
            Nombre = Texto(r, "Nombre"),
            Tipo = Texto(r, "Tipo"),
            FechaDesde = r.GetDateTime(r.GetOrdinal("FechaDesde")),
            FechaHasta = FechaNula(r, "FechaHasta"),
            Estado = Texto(r, "Estado"),
            Producto = Texto(r, "Producto"),
            EventoId = EnteroNulo(r, "EventoId"),
            PresupuestoIngresos = DecimalNulo(r, "PresupuestoIngresos"),
            PresupuestoGastos = DecimalNulo(r, "PresupuestoGastos"),
            Comentarios = Texto(r, "Comentarios"),
            Usuario = Texto(r, "Usuario"),
            FechaAlta = r.GetDateTime(r.GetOrdinal("FechaAlta")),
            UsuarioModificacion = Texto(r, "UsuarioModificacion"),
            FechaModificacion = FechaNula(r, "FechaModificacion")
        };

        private static FilaApunteContable LeerFilaApunte(SqlDataReader r) => new FilaApunteContable
        {
            Empresa = Texto(r, "Empresa"),
            NumeroOrden = r.GetInt32(r.GetOrdinal("NumeroOrden")),
            Cuenta = Texto(r, "Cuenta"),
            NombreCuenta = Texto(r, "NombreCuenta"),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            Concepto = Texto(r, "Concepto"),
            Documento = Texto(r, "Documento"),
            Asiento = EnteroNulo(r, "Asiento"),
            Diario = Texto(r, "Diario"),
            Debe = DecimalNulo(r, "Debe") ?? 0,
            Haber = DecimalNulo(r, "Haber") ?? 0,
            Tercero = Texto(r, "Tercero"),
            TipoTercero = Texto(r, "TipoTercero"),
            NombreTercero = Texto(r, "NombreTercero")
        };

        private static FilaImputacion LeerFilaImputacion(SqlDataReader r) => new FilaImputacion
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            ProyectoId = r.GetInt32(r.GetOrdinal("ProyectoId")),
            Empresa = Texto(r, "Empresa"),
            NumeroOrdenApunte = r.GetInt32(r.GetOrdinal("NumeroOrdenApunte")),
            Importe = DecimalNulo(r, "Importe") ?? 0,
            Cuenta = Texto(r, "Cuenta"),
            NombreCuenta = Texto(r, "NombreCuenta"),
            FechaApunte = r.GetDateTime(r.GetOrdinal("FechaApunte")),
            Concepto = Texto(r, "Concepto"),
            Documento = Texto(r, "Documento"),
            Asiento = EnteroNulo(r, "Asiento"),
            Debe = DecimalNulo(r, "Debe") ?? 0,
            Haber = DecimalNulo(r, "Haber") ?? 0,
            Tercero = Texto(r, "Tercero"),
            TipoTercero = Texto(r, "TipoTercero"),
            NombreTercero = Texto(r, "NombreTercero"),
            Comentario = Texto(r, "Comentario"),
            Usuario = Texto(r, "Usuario"),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            CodigoProyecto = Texto(r, "CodigoProyecto"),
            NombreProyecto = Texto(r, "NombreProyecto"),
            EstadoProyecto = Texto(r, "EstadoProyecto")
        };
    }
}
