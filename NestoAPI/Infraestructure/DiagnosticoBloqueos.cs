using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#312: cuando un error es un timeout por bloqueo SQL, averigua QUIÉN está
    /// bloqueando para incluirlo en el mensaje. Así el primer usuario al que le dé el error
    /// puede llamar directamente al compañero que bloquea, sin esperar a que un administrador
    /// mire sp_lock (incidente real 16/07/26: sesión sleeping con transacción abierta reteniendo
    /// miles de locks y toda la oficina encolada).
    /// </summary>
    public class DiagnosticoBloqueos
    {
        /// <summary>
        /// -2 = timeout de comando (lo que ven los usuarios como "No se pudo contabilizar");
        /// 1222 = lock request time out; 1205 = interbloqueo (deadlock, caso real 17/07/26: en un
        /// interbloqueo SQL Server mata a la víctima pero el GANADOR sigue vivo con su transacción
        /// abierta, así que las DMVs aún pueden identificarlo — sobre todo con el fallback de
        /// sesión sleeping con transacción antigua). La SqlException viene anidada en los inners.
        /// </summary>
        public static bool EsErrorDeBloqueo(Exception exception)
        {
            for (Exception actual = exception; actual != null; actual = actual.InnerException)
            {
                if (actual is SqlException sqlException &&
                    (sqlException.Number == -2 || sqlException.Number == 1222 || sqlException.Number == 1205))
                {
                    return true;
                }
            }
            return false;
        }

        // Cabezas de bloqueo (bloquean y no están bloqueadas) y, como fallback, sesiones
        // sleeping con transacción abierta antigua (>15s): el caso real del 16/07/26, donde la
        // cola ya se había vaciado por timeouts pero los locks seguían retenidos.
        private const string CONSULTA = @"
SELECT TOP 3
    s.session_id AS SessionId,
    s.login_name AS LoginName,
    s.program_name AS ProgramName,
    COALESCE(at.transaction_begin_time, s.last_request_start_time) AS Desde,
    REPLACE(CAST(s.context_info AS varchar(128)), CHAR(0), '') AS ContextInfo,
    bloqueados.N AS Bloqueados
FROM sys.dm_exec_sessions s
LEFT JOIN sys.dm_tran_session_transactions st ON st.session_id = s.session_id
LEFT JOIN sys.dm_tran_active_transactions at ON at.transaction_id = st.transaction_id
OUTER APPLY (SELECT COUNT(*) AS N FROM sys.dm_exec_requests r WHERE r.blocking_session_id = s.session_id) bloqueados
WHERE s.is_user_process = 1
  AND s.session_id <> @@SPID
  AND (bloqueados.N > 0
       OR (s.status = 'sleeping' AND at.transaction_begin_time < DATEADD(second, -15, GETDATE())))
  AND NOT EXISTS (SELECT 1 FROM sys.dm_exec_requests rb WHERE rb.session_id = s.session_id AND rb.blocking_session_id > 0)
ORDER BY bloqueados.N DESC, at.transaction_begin_time ASC";

        /// <summary>
        /// Resultado del diagnóstico: o bien identifica bloqueadores (mensaje para el usuario),
        /// o bien explica POR QUÉ no los identificó (solo para la ficha de ELMAH). NestoAPI#321:
        /// el diagnóstico estuvo semanas devolviendo null en silencio (faltaba VIEW SERVER STATE)
        /// y nadie lo supo; nunca más puede quedarse mudo.
        /// </summary>
        public class ResultadoDiagnostico
        {
            /// <summary>Mensaje para el usuario ("Puede estar bloqueado por..."), o null.</summary>
            public string Bloqueadores { get; set; }
            /// <summary>Si no hay bloqueadores, el motivo (permisos, 0 candidatos, fallo). Solo ELMAH.</summary>
            public string MotivoSinBloqueadores { get; set; }
        }

        private const string CONSULTA_SESIONES_VISIBLES =
            "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE is_user_process = 1";

        /// <summary>
        /// Devuelve una descripción de los bloqueadores actuales o, si no los identifica, el motivo
        /// para la ficha de ELMAH. NUNCA lanza: no debe empeorar el error original.
        /// </summary>
        public static ResultadoDiagnostico DescribirBloqueadores()
        {
            try
            {
                using (Models.NVEntities db = new Models.NVEntities())
                {
                    db.Database.CommandTimeout = 3;
                    List<FilaBloqueador> filas = db.Database.SqlQuery<FilaBloqueador>(CONSULTA).ToList();
                    string bloqueadores = FormatearBloqueadores(filas);
                    if (bloqueadores != null)
                    {
                        return new ResultadoDiagnostico { Bloqueadores = bloqueadores };
                    }
                    int sesionesVisibles = db.Database.SqlQuery<int>(CONSULTA_SESIONES_VISIBLES).First();
                    return new ResultadoDiagnostico { MotivoSinBloqueadores = InterpretarSinBloqueadores(sesionesVisibles) };
                }
            }
            catch (Exception ex)
            {
                return new ResultadoDiagnostico
                {
                    MotivoSinBloqueadores = $"La consulta de diagnóstico de bloqueos falló: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// NestoAPI#321: sin VIEW SERVER STATE las DMVs no dan error, simplemente solo devuelven la
        /// sesión propia, y el diagnóstico parecía "sin bloqueadores" cuando en realidad estaba ciego.
        /// </summary>
        internal static string InterpretarSinBloqueadores(int sesionesVisibles)
        {
            return sesionesVisibles <= 1
                ? "El login del API solo ve su propia sesión en sys.dm_exec_sessions: falta GRANT VIEW SERVER STATE (permiso de servidor) para poder identificar al bloqueador."
                : $"Sin bloqueadores activos entre las {sesionesVisibles} sesiones visibles: el bloqueo pudo liberarse antes de ejecutar el diagnóstico.";
        }

        internal static string FormatearBloqueadores(IList<FilaBloqueador> bloqueadores)
        {
            if (bloqueadores == null || !bloqueadores.Any())
            {
                return null;
            }

            IEnumerable<string> partes = bloqueadores.Select(b =>
            {
                // Conexiones de la API: el usuario real viene en CONTEXT_INFO (#286).
                // Conexiones directas de Nesto: el login de dominio ya es el usuario.
                string usuario = !string.IsNullOrWhiteSpace(b.ContextInfo) ? b.ContextInfo.Trim() : b.LoginName?.Trim();
                List<string> detalles = new List<string>();
                if (!string.IsNullOrWhiteSpace(b.ProgramName))
                {
                    detalles.Add(b.ProgramName.Trim());
                }
                if (b.Desde.HasValue)
                {
                    detalles.Add($"transacción abierta desde {b.Desde:HH:mm}");
                }
                if (b.Bloqueados > 0)
                {
                    detalles.Add($"bloquea a {b.Bloqueados} sesión(es)");
                }
                return detalles.Any() ? $"{usuario} ({string.Join(", ", detalles)})" : usuario;
            });

            return "Puede estar bloqueado por: " + string.Join(" | ", partes) + ". Si persiste, avisa a ese usuario para que termine o cierre la operación que tiene a medias.";
        }

        internal class FilaBloqueador
        {
            public short SessionId { get; set; }
            public string LoginName { get; set; }
            public string ProgramName { get; set; }
            public DateTime? Desde { get; set; }
            public string ContextInfo { get; set; }
            public int Bloqueados { get; set; }
        }
    }
}
