using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// NestoAPI#343 (SOLO diagnóstico, no cambia nada de la facturación): prdCrearFacturaVta falla de vez en cuando con
    /// «Está intentando contabilizar un gasto sin centro de coste, delegación o departamento» y el reintento, sin tocar
    /// nada, entra. El chequeo (prdContabilizar) mira TODO el diario _FRAVTA de la empresa en PreContabilidad, no solo
    /// los apuntes de esta factura, y cuando llega el error esos apuntes ya se han revertido: no queda rastro de qué fila
    /// lo provocó. Esta clase, en el momento del fallo y por una conexión NUEVA fuera de cualquier transacción, fotografía
    /// lo que hay en PreContabilidad (_FRAVTA, NOLOCK), las líneas de cuenta contable del pedido, la serie de la
    /// cabecera, si hubo corrección de serie del gate Verifactu #39 en esta llamada y qué sesiones están escribiendo en
    /// PreContabilidad, y lo deja en UN error informativo de ELMAH. Nunca lanza.
    /// </summary>
    public class DiagnosticoGastoSinCentroCoste
    {
        internal const string TEXTO_ERROR = "gasto sin centro de coste";
        internal const string DIARIO_FACTURAS_VENTA = "_FRAVTA";
        internal const string PREFIJO_MENSAJE = "[Diagnóstico NestoAPI#343]";

        private readonly ILectorEstadoGastoSinCentroCoste lector;
        private readonly ILogService log;

        public DiagnosticoGastoSinCentroCoste() : this(null, null)
        {
        }

        internal DiagnosticoGastoSinCentroCoste(ILectorEstadoGastoSinCentroCoste lector, ILogService log)
        {
            this.lector = lector ?? new LectorEstadoGastoSinCentroCosteSql();
            this.log = log ?? new ElmahLogService();
        }

        /// <summary>
        /// ¿Es el error del chequeo de centro de coste? Vale para el texto de prdContabilizar («Está intentando
        /// contabilizar un gasto sin centro de coste, delegación o departamento») y para el de
        /// prdComprobarCamposNecesarios («...un gasto sin centro de coste,delegación...»). Recorre los inner.
        /// </summary>
        internal static bool EsErrorGastoSinCentroCoste(Exception ex)
        {
            for (Exception actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual.Message != null && actual.Message.IndexOf(TEXTO_ERROR, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Si el error es el de centro de coste, lee el estado y lo registra en ELMAH (un solo error). Devuelve si lo ha
        /// registrado. NUNCA lanza: un fallo aquí no puede cambiar lo que ve el que factura.
        /// </summary>
        internal async Task<bool> RegistrarSiProcede(Exception error, ContextoFalloGastoSinCentroCoste contexto)
        {
            try
            {
                if (!EsErrorGastoSinCentroCoste(error) || contexto == null)
                {
                    return false;
                }

                EstadoGastoSinCentroCoste estado;
                try
                {
                    estado = await lector.Leer(contexto.Empresa, contexto.Pedido).ConfigureAwait(false);
                }
                catch (Exception exLectura)
                {
                    estado = new EstadoGastoSinCentroCoste { ErrorGeneral = exLectura.Message };
                }

                log.LogError(ConstruirInforme(contexto, estado ?? new EstadoGastoSinCentroCoste()), error);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>El texto del error informativo. Pura: se testea sin BD.</summary>
        internal static string ConstruirInforme(ContextoFalloGastoSinCentroCoste contexto, EstadoGastoSinCentroCoste estado)
        {
            List<PreContabilidad> filas = estado.PreContabilidadDiario ?? new List<PreContabilidad>();
            HashSet<int> incumplen = new HashSet<int>(filas.Where(IncumpleRegla).Select(f => f.Nº_Orden));
            string empresa = contexto.Empresa?.Trim();

            StringBuilder sb = new StringBuilder();
            _ = sb.Append($"{PREFIJO_MENSAJE} Gasto sin centro de coste al facturar el pedido {contexto.Pedido} (empresa {empresa}): ");
            _ = sb.Append(estado.ErrorPreContabilidad == null
                ? $"{filas.Count} fila(s) en PreContabilidad {DIARIO_FACTURAS_VENTA}, {incumplen.Count} incumplen la regla de CamposNecesarios"
                : "no se pudo leer PreContabilidad");
            _ = sb.Append(contexto.HuboCorreccionSerie
                ? $"; serie corregida {contexto.SerieAntesCorreccion?.Trim()}→{contexto.SerieDespuesCorreccion?.Trim()} a las {Hora(contexto.HoraCorreccionSerie)}"
                : "; sin corrección de serie en esta llamada");
            _ = sb.AppendLine(".");
            _ = sb.AppendLine("Solo diagnóstico: la facturación sigue igual (el que factura ve el error de siempre).");
            _ = sb.AppendLine();

            _ = sb.AppendLine("== Llamada ==");
            _ = sb.AppendLine($"Usuario: {contexto.Usuario?.Trim()}");
            _ = sb.AppendLine($"Serie al empezar: {contexto.SerieAlEmpezar?.Trim()} | serie actual en CabPedidoVta (NOLOCK): {estado.SerieCabecera?.Trim() ?? "(no leída)"}{Error(estado.ErrorSerie)}");
            _ = sb.AppendLine(contexto.HuboCorreccionSerie
                ? $"Corrección de serie (gate Verifactu #39): SÍ, {contexto.SerieAntesCorreccion?.Trim()}→{contexto.SerieDespuesCorreccion?.Trim()} a las {Hora(contexto.HoraCorreccionSerie)}"
                : "Corrección de serie (gate Verifactu #39): no");
            _ = sb.AppendLine($"Inicio de prdCrearFacturaVta (último intento): {Hora(contexto.HoraInicioProcedimiento)} | fallo: {Hora(contexto.HoraFallo)} | captura: {Hora(estado.HoraCaptura)}");
            _ = sb.AppendLine();

            _ = sb.AppendLine($"== PreContabilidad, diario {DIARIO_FACTURAS_VENTA} (todas las empresas, NOLOCK: incluye filas aún sin confirmar de otras sesiones) =={Error(estado.ErrorPreContabilidad)}");
            if (!filas.Any() && estado.ErrorPreContabilidad == null)
            {
                _ = sb.AppendLine("(vacío: los apuntes de esta factura ya se han revertido y no hay otros)");
            }
            foreach (PreContabilidad f in filas)
            {
                _ = sb.AppendLine(
                    $"{(incumplen.Contains(f.Nº_Orden) ? "INCUMPLE " : "         ")}nº orden {f.Nº_Orden} | emp {f.Empresa?.Trim()} | tc {f.TipoCuenta?.Trim()} | " +
                    $"cta {f.Nº_Cuenta?.Trim()} | contrap {f.Contrapartida?.Trim() ?? "NULL"} | D {Importe(f.Debe)} H {Importe(f.Haber)} | " +
                    $"doc {f.Nº_Documento?.Trim()} | asiento {f.Asiento} | deleg {Valor(f.Delegación)} cc {Valor(f.CentroCoste)} dep {Valor(f.Departamento)} | " +
                    $"'{f.Concepto?.Trim()}' | {f.Usuario?.Trim()} | {Hora(f.Fecha_Modificación)}");
            }
            if (estado.ResumenDiarios != null && estado.ResumenDiarios.Any())
            {
                _ = sb.AppendLine($"Resumen de PreContabilidad por empresa/diario: {string.Join(" · ", estado.ResumenDiarios)}");
            }
            _ = sb.AppendLine();

            _ = sb.AppendLine($"== Líneas de cuenta contable (TipoLinea=2) del pedido {contexto.Pedido} (NOLOCK) =={Error(estado.ErrorLineas)}");
            List<LinPedidoVta> lineas = estado.LineasCuentaContable ?? new List<LinPedidoVta>();
            if (!lineas.Any() && estado.ErrorLineas == null)
            {
                _ = sb.AppendLine("(ninguna)");
            }
            foreach (LinPedidoVta l in lineas)
            {
                _ = sb.AppendLine(
                    $"nº orden {l.Nº_Orden} | {l.Producto?.Trim()} | bruto {Importe(l.Bruto)} | estado {l.Estado} | fra {l.Nº_Factura?.Trim() ?? "-"} | " +
                    $"deleg {Valor(l.Delegación)} cc {Valor(l.CentroCoste)} dep {Valor(l.Departamento)} | modif {Hora(l.Fecha_Modificación)}");
            }
            _ = sb.AppendLine();

            _ = sb.AppendLine($"== Sesiones escribiendo en PreContabilidad o ejecutando sus procedimientos =={Error(estado.ErrorSesiones)}");
            List<string> sesiones = estado.Sesiones ?? new List<string>();
            if (!sesiones.Any() && estado.ErrorSesiones == null)
            {
                _ = sb.AppendLine("(ninguna en el momento de la captura)");
            }
            foreach (string s in sesiones)
            {
                _ = sb.AppendLine(s);
            }

            if (estado.ErrorGeneral != null)
            {
                _ = sb.AppendLine();
                _ = sb.AppendLine($"La captura falló: {estado.ErrorGeneral}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// La regla de prdContabilizar sobre una fila: tipocuenta 1, cuenta o contrapartida de gasto (620-639) y
        /// centro de coste, delegación o departamento NULL. Reutiliza la réplica de ContabilidadService.
        /// </summary>
        internal static bool IncumpleRegla(PreContabilidad fila)
        {
            return fila != null
                && fila.TipoCuenta?.Trim() == "1"
                && ContabilidadService.ErroresCamposNecesariosDiario(new[] { fila }).Any();
        }

        private static string Hora(DateTime? hora) => hora.HasValue ? hora.Value.ToString("dd/MM/yy HH:mm:ss.fff", CultureInfo.InvariantCulture) : "-";
        private static string Importe(decimal importe) => importe.ToString("0.00##", CultureInfo.InvariantCulture);
        private static string Valor(string valor) => valor == null ? "NULL" : $"'{valor.Trim()}'";
        private static string Error(string error) => error == null ? string.Empty : $" [no se pudo leer: {error}]";
    }

    /// <summary>NestoAPI#343: lo que sabe CrearFactura de la llamada que ha fallado.</summary>
    internal class ContextoFalloGastoSinCentroCoste
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Usuario { get; set; }
        public string SerieAlEmpezar { get; set; }
        public bool HuboCorreccionSerie { get; set; }
        public string SerieAntesCorreccion { get; set; }
        public string SerieDespuesCorreccion { get; set; }
        public DateTime? HoraCorreccionSerie { get; set; }
        public DateTime? HoraInicioProcedimiento { get; set; }
        public DateTime? HoraFallo { get; set; }
    }

    /// <summary>NestoAPI#343: la foto de la BD en el momento del fallo. Cada parte lleva su error si no se pudo leer.</summary>
    internal class EstadoGastoSinCentroCoste
    {
        public DateTime? HoraCaptura { get; set; }
        public List<PreContabilidad> PreContabilidadDiario { get; set; }
        public string ErrorPreContabilidad { get; set; }
        public List<string> ResumenDiarios { get; set; }
        public List<LinPedidoVta> LineasCuentaContable { get; set; }
        public string ErrorLineas { get; set; }
        public string SerieCabecera { get; set; }
        public string ErrorSerie { get; set; }
        public List<string> Sesiones { get; set; }
        public string ErrorSesiones { get; set; }
        public string ErrorGeneral { get; set; }
    }

    internal interface ILectorEstadoGastoSinCentroCoste
    {
        Task<EstadoGastoSinCentroCoste> Leer(string empresa, int pedido);
    }

    /// <summary>
    /// NestoAPI#343: lectura por una conexión PROPIA (Application Name aparte = pool aparte, Enlist=false y
    /// TransactionScope Suppress: nunca entra en la transacción de la facturación ni hereda nada de su conexión).
    /// Todo con NOLOCK y sin SET de aislamiento (no se deja nada raro en el pool). Cada consulta va en su propio
    /// try: si una falla (p. ej. permisos de las DMVs), las demás se registran igual.
    /// </summary>
    internal class LectorEstadoGastoSinCentroCosteSql : ILectorEstadoGastoSinCentroCoste
    {
        private const int TIMEOUT_SEGUNDOS = 5;

        internal const string SQL_PRECONTABILIDAD = @"
SELECT TOP (300) p.[Nº Orden], p.Empresa, p.TipoCuenta, p.[Nº Cuenta], p.Contrapartida, p.Debe, p.Haber, p.[Nº Documento],
    p.Asiento, p.Delegación, p.CentroCoste, p.Departamento, p.Concepto, p.Usuario, p.[Fecha Modificación], p.Diario
FROM PreContabilidad p WITH (NOLOCK)
WHERE p.Diario = @Diario
ORDER BY p.[Nº Orden]";

        internal const string SQL_RESUMEN_DIARIOS = @"
SELECT TOP (50) ISNULL(RTRIM(p.Empresa), '(null)') + '/' + ISNULL(RTRIM(p.Diario), '(null)') + ': ' + CAST(COUNT(*) AS varchar(10))
FROM PreContabilidad p WITH (NOLOCK)
GROUP BY p.Empresa, p.Diario
ORDER BY COUNT(*) DESC";

        internal const string SQL_LINEAS = @"
SELECT l.[Nº Orden], l.Producto, l.Bruto, l.Estado, l.[Nº Factura], l.Delegación, l.CentroCoste, l.Departamento, l.[Fecha Modificación]
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @Empresa AND l.Número = @Pedido AND l.TipoLinea = 2
ORDER BY l.[Nº Orden]";

        internal const string SQL_SERIE = @"
SELECT c.Serie FROM CabPedidoVta c WITH (NOLOCK) WHERE c.Empresa = @Empresa AND c.Número = @Pedido";

        // Sesiones con locks de escritura en PreContabilidad, o ejecutando algo que la toca (los procedimientos que
        // escriben en ella o una sentencia que la nombra). Usuario real de la API en CONTEXT_INFO (#286).
        internal const string SQL_SESIONES = @"
WITH Locks AS (
    SELECT tl.request_session_id AS SessionId, COUNT(*) AS N
    FROM sys.dm_tran_locks tl
    WHERE tl.resource_database_id = DB_ID()
      AND tl.request_mode IN ('X', 'IX', 'SIX', 'U', 'IU', 'UIX')
      AND ((tl.resource_type = 'OBJECT' AND tl.resource_associated_entity_id = OBJECT_ID('dbo.PreContabilidad'))
        OR (tl.resource_type IN ('KEY', 'PAGE', 'RID', 'HOBT')
            AND tl.resource_associated_entity_id IN (SELECT pa.hobt_id FROM sys.partitions pa WHERE pa.object_id = OBJECT_ID('dbo.PreContabilidad'))))
    GROUP BY tl.request_session_id
)
SELECT TOP (20)
    'sesión ' + CAST(s.session_id AS varchar(10))
    + ' | ' + ISNULL(RTRIM(s.login_name), '') + ' | ' + ISNULL(RTRIM(s.host_name), '') + ' | ' + ISNULL(RTRIM(s.program_name), '')
    + ' | usuario API ' + ISNULL(NULLIF(REPLACE(CAST(s.context_info AS varchar(128)), CHAR(0), ''), ''), '-')
    + ' | ' + s.status + ' | aislamiento ' + CAST(s.transaction_isolation_level AS varchar(2))
    + ' | trans abiertas ' + CAST(s.open_transaction_count AS varchar(5))
    + ' | trans desde ' + ISNULL(CONVERT(varchar(23), at.transaction_begin_time, 121), '-')
    + ' | locks escritura PreContabilidad ' + CAST(ISNULL(lk.N, 0) AS varchar(10))
    + ' | ' + ISNULL(r.command, '(sin petición)') + ISNULL(' espera ' + r.wait_type, '') + ISNULL(' bloqueada por ' + CAST(NULLIF(r.blocking_session_id, 0) AS varchar(10)), '')
    + ' | ' + ISNULL(OBJECT_NAME(t.objectid, t.dbid) + ': ', '') + ISNULL(LEFT(REPLACE(REPLACE(t.text, CHAR(13), ' '), CHAR(10), ' '), 300), '')
FROM sys.dm_exec_sessions s
LEFT JOIN Locks lk ON lk.SessionId = s.session_id
LEFT JOIN sys.dm_exec_requests r ON r.session_id = s.session_id
LEFT JOIN sys.dm_exec_connections c ON c.session_id = s.session_id
LEFT JOIN sys.dm_tran_session_transactions st ON st.session_id = s.session_id
LEFT JOIN sys.dm_tran_active_transactions at ON at.transaction_id = st.transaction_id
OUTER APPLY sys.dm_exec_sql_text(COALESCE(r.sql_handle, c.most_recent_sql_handle)) t
WHERE s.is_user_process = 1
  AND s.session_id <> @@SPID
  AND (lk.N > 0
       OR (r.session_id IS NOT NULL
           AND (OBJECT_NAME(t.objectid, t.dbid) IN ('prdCrearFacturaVta', 'prdContabilizar', 'prdContabilizarVentaInmovilizado')
                OR t.text LIKE '%PreContabilidad%')))
ORDER BY ISNULL(lk.N, 0) DESC, s.session_id";

        public async Task<EstadoGastoSinCentroCoste> Leer(string empresa, int pedido)
        {
            EstadoGastoSinCentroCoste estado = new EstadoGastoSinCentroCoste { HoraCaptura = DateTime.Now };
            using (TransactionScope sinTransaccion = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            using (SqlConnection conexion = new SqlConnection(CadenaDiagnostico()))
            {
                await conexion.OpenAsync().ConfigureAwait(false);

                try
                {
                    estado.PreContabilidadDiario = await LeerPreContabilidad(conexion).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    estado.ErrorPreContabilidad = ex.Message;
                }

                try
                {
                    estado.ResumenDiarios = await LeerTextos(conexion, SQL_RESUMEN_DIARIOS, null, null).ConfigureAwait(false);
                }
                catch
                {
                    // El resumen es accesorio.
                }

                try
                {
                    estado.LineasCuentaContable = await LeerLineas(conexion, empresa, pedido).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    estado.ErrorLineas = ex.Message;
                }

                try
                {
                    using (SqlCommand comando = Comando(conexion, SQL_SERIE, empresa, pedido))
                    {
                        object serie = await comando.ExecuteScalarAsync().ConfigureAwait(false);
                        estado.SerieCabecera = serie == null || serie is DBNull ? null : serie.ToString();
                    }
                }
                catch (Exception ex)
                {
                    estado.ErrorSerie = ex.Message;
                }

                try
                {
                    estado.Sesiones = await LeerTextos(conexion, SQL_SESIONES, null, null).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    estado.ErrorSesiones = ex.Message;
                }

                sinTransaccion.Complete();
            }
            return estado;
        }

        /// <summary>
        /// Misma BD que la API (NestoConnection, como DiagnosticoBloqueos) con Application Name propio (pool aparte),
        /// Enlist=false (nunca se alista en una transacción ambiente) y Connect Timeout corto.
        /// </summary>
        private static string CadenaDiagnostico()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            SqlConnectionStringBuilder constructor = new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = "NestoAPI-Diagnostico343",
                Enlist = false,
                ConnectTimeout = 3,
                MultipleActiveResultSets = false
            };
            return constructor.ConnectionString;
        }

        private static SqlCommand Comando(SqlConnection conexion, string sql, string empresa, int? pedido)
        {
            SqlCommand comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS };
            if (empresa != null)
            {
                _ = comando.Parameters.AddWithValue("@Empresa", empresa);
            }
            if (pedido.HasValue)
            {
                _ = comando.Parameters.AddWithValue("@Pedido", pedido.Value);
            }
            return comando;
        }

        private static async Task<List<PreContabilidad>> LeerPreContabilidad(SqlConnection conexion)
        {
            List<PreContabilidad> filas = new List<PreContabilidad>();
            using (SqlCommand comando = Comando(conexion, SQL_PRECONTABILIDAD, null, null))
            {
                _ = comando.Parameters.AddWithValue("@Diario", DiagnosticoGastoSinCentroCoste.DIARIO_FACTURAS_VENTA);
                using (SqlDataReader r = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await r.ReadAsync().ConfigureAwait(false))
                    {
                        filas.Add(new PreContabilidad
                        {
                            Nº_Orden = Convert.ToInt32(r[0]),
                            Empresa = Texto(r[1]),
                            TipoCuenta = Texto(r[2]),
                            Nº_Cuenta = Texto(r[3]),
                            Contrapartida = Texto(r[4]),
                            Debe = r[5] is DBNull ? 0 : Convert.ToDecimal(r[5]),
                            Haber = r[6] is DBNull ? 0 : Convert.ToDecimal(r[6]),
                            Nº_Documento = Texto(r[7]),
                            Asiento = r[8] is DBNull ? (int?)null : Convert.ToInt32(r[8]),
                            Delegación = Texto(r[9]),
                            CentroCoste = Texto(r[10]),
                            Departamento = Texto(r[11]),
                            Concepto = Texto(r[12]),
                            Usuario = Texto(r[13]),
                            Fecha_Modificación = r[14] is DBNull ? DateTime.MinValue : Convert.ToDateTime(r[14]),
                            Diario = Texto(r[15])
                        });
                    }
                }
            }
            return filas;
        }

        private static async Task<List<LinPedidoVta>> LeerLineas(SqlConnection conexion, string empresa, int pedido)
        {
            List<LinPedidoVta> lineas = new List<LinPedidoVta>();
            using (SqlCommand comando = Comando(conexion, SQL_LINEAS, empresa, pedido))
            using (SqlDataReader r = await comando.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await r.ReadAsync().ConfigureAwait(false))
                {
                    lineas.Add(new LinPedidoVta
                    {
                        Nº_Orden = Convert.ToInt32(r[0]),
                        Producto = Texto(r[1]),
                        Bruto = r[2] is DBNull ? 0 : Convert.ToDecimal(r[2]),
                        Estado = r[3] is DBNull ? (short)0 : Convert.ToInt16(r[3]),
                        Nº_Factura = Texto(r[4]),
                        Delegación = Texto(r[5]),
                        CentroCoste = Texto(r[6]),
                        Departamento = Texto(r[7]),
                        Fecha_Modificación = r[8] is DBNull ? DateTime.MinValue : Convert.ToDateTime(r[8])
                    });
                }
            }
            return lineas;
        }

        private static async Task<List<string>> LeerTextos(SqlConnection conexion, string sql, string empresa, int? pedido)
        {
            List<string> textos = new List<string>();
            using (SqlCommand comando = Comando(conexion, sql, empresa, pedido))
            using (SqlDataReader r = await comando.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await r.ReadAsync().ConfigureAwait(false))
                {
                    textos.Add(Texto(r[0]));
                }
            }
            return textos;
        }

        private static string Texto(object valor) => valor == null || valor is DBNull ? null : valor.ToString();
    }
}
