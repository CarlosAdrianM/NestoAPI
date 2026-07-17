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
        /// Devuelve una descripción de los bloqueadores actuales, o null si no hay o si el
        /// diagnóstico falla: NUNCA debe empeorar el error original.
        /// </summary>
        public static string DescribirBloqueadores()
        {
            try
            {
                using (Models.NVEntities db = new Models.NVEntities())
                {
                    db.Database.CommandTimeout = 3;
                    List<FilaBloqueador> filas = db.Database.SqlQuery<FilaBloqueador>(CONSULTA).ToList();
                    return FormatearBloqueadores(filas);
                }
            }
            catch
            {
                return null;
            }
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
