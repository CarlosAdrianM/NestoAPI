using System.Data;
using System.Data.Common;
using System.Data.Entity.Infrastructure.Interception;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Issue #286: estampa el usuario real (JWT/Windows) en CONTEXT_INFO cada vez que EF abre una
    /// conexión, para que los diagnósticos de bloqueos puedan identificar a la persona: todas las
    /// conexiones del API entran en SQL Server con la cuenta de máquina del IIS y sp_who/DMVs no
    /// ven al usuario de ninguna otra forma. Se lee desde otra sesión con:
    ///   SELECT session_id, CONVERT(varchar(128), context_info) FROM sys.dm_exec_sessions
    ///
    /// Se registra UNA vez en Global.asax (DbInterception.Add) y cubre TODAS las conexiones de EF
    /// (NVEntities y ApplicationDbContext, peticiones web y jobs de Hangfire) sin tocar ningún
    /// call site: no hay que acordarse de nada al añadir código nuevo.
    ///
    /// IMPORTANTE: se estampa SIEMPRE, también cuando no hay usuario (petición anónima o job),
    /// porque las conexiones del pool conservan el CONTEXT_INFO de quien las usó antes y, si no
    /// se sobrescribiera, atribuiríamos bloqueos a la persona equivocada.
    /// </summary>
    public class UsuarioContextInfoInterceptor : IDbConnectionInterceptor
    {
        public void Opened(DbConnection connection, DbConnectionInterceptionContext interceptionContext)
        {
            if (connection == null || connection.State != ConnectionState.Open)
            {
                return;
            }
            try
            {
                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SET CONTEXT_INFO @usuario";
                    DbParameter parametro = command.CreateParameter();
                    parametro.ParameterName = "@usuario";
                    parametro.DbType = DbType.Binary;
                    parametro.Value = BytesUsuario(UsuarioActual());
                    _ = command.Parameters.Add(parametro);
                    _ = command.ExecuteNonQuery();
                }
            }
            catch
            {
                // El estampado es solo diagnóstico: nunca debe tirar la petición.
            }
        }

        // CONTEXT_INFO es varbinary(128): truncamos a 128 bytes y garantizamos al menos un byte
        // para sobrescribir siempre lo que la conexión del pool traiga de la petición anterior
        // (SET CONTEXT_INFO no admite binario vacío).
        internal static byte[] BytesUsuario(string usuario)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(usuario ?? string.Empty);
            if (bytes.Length == 0)
            {
                return new byte[1];
            }
            return bytes.Length <= 128 ? bytes : bytes.Take(128).ToArray();
        }

        internal static string UsuarioActual()
        {
            string usuario = HttpContext.Current?.User?.Identity?.Name;
            if (string.IsNullOrEmpty(usuario))
            {
                usuario = Thread.CurrentPrincipal?.Identity?.Name;
            }
            return usuario ?? string.Empty;
        }

        #region Resto de la interfaz IDbConnectionInterceptor (sin comportamiento)

        public void BeganTransaction(DbConnection connection, BeginTransactionInterceptionContext interceptionContext) { }
        public void BeginningTransaction(DbConnection connection, BeginTransactionInterceptionContext interceptionContext) { }
        public void Closed(DbConnection connection, DbConnectionInterceptionContext interceptionContext) { }
        public void Closing(DbConnection connection, DbConnectionInterceptionContext interceptionContext) { }
        public void ConnectionStringGetting(DbConnection connection, DbConnectionInterceptionContext<string> interceptionContext) { }
        public void ConnectionStringGot(DbConnection connection, DbConnectionInterceptionContext<string> interceptionContext) { }
        public void ConnectionStringSetting(DbConnection connection, DbConnectionPropertyInterceptionContext<string> interceptionContext) { }
        public void ConnectionStringSet(DbConnection connection, DbConnectionPropertyInterceptionContext<string> interceptionContext) { }
        public void ConnectionTimeoutGetting(DbConnection connection, DbConnectionInterceptionContext<int> interceptionContext) { }
        public void ConnectionTimeoutGot(DbConnection connection, DbConnectionInterceptionContext<int> interceptionContext) { }
        public void DatabaseGetting(DbConnection connection, DbConnectionInterceptionContext<string> interceptionContext) { }
        public void DatabaseGot(DbConnection connection, DbConnectionInterceptionContext<string> interceptionContext) { }
        public void DataSourceGetting(DbConnection connection, DbConnectionInterceptionContext<string> interceptionContext) { }
        public void DataSourceGot(DbConnection connection, DbConnectionInterceptionContext<string> interceptionContext) { }
        public void Disposed(DbConnection connection, DbConnectionInterceptionContext interceptionContext) { }
        public void Disposing(DbConnection connection, DbConnectionInterceptionContext interceptionContext) { }
        public void EnlistedTransaction(DbConnection connection, EnlistTransactionInterceptionContext interceptionContext) { }
        public void EnlistingTransaction(DbConnection connection, EnlistTransactionInterceptionContext interceptionContext) { }
        public void Opening(DbConnection connection, DbConnectionInterceptionContext interceptionContext) { }
        public void ServerVersionGetting(DbConnection connection, DbConnectionInterceptionContext<string> interceptionContext) { }
        public void ServerVersionGot(DbConnection connection, DbConnectionInterceptionContext<string> interceptionContext) { }
        public void StateGetting(DbConnection connection, DbConnectionInterceptionContext<ConnectionState> interceptionContext) { }
        public void StateGot(DbConnection connection, DbConnectionInterceptionContext<ConnectionState> interceptionContext) { }

        #endregion
    }
}
