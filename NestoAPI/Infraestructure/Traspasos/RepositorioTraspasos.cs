using NestoAPI.Models;
using NestoAPI.Models.Traspasos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Traspasos
{
    /// <summary>
    /// NestoAPI#553: acceso a BD de los traspasos entre almacenes. De momento solo lee la propuesta.
    /// </summary>
    public interface IRepositorioTraspasos
    {
        /// <summary>
        /// Llama a prdRellenarReposicionStock. Si el destino tiene una reposición anterior sin
        /// contabilizar lanza <see cref="ReposicionPendienteException"/>.
        /// </summary>
        Task<List<LineaReposicionStockSP>> LeerPropuesta(string empresa, string origen, string destino);
    }

    /// <summary>
    /// NestoAPI#553: el SP hace raiserror cuando en PreExtrProducto hay líneas con NºTraspaso del
    /// almacén destino (salvo el diario 'RepoEscond'). Se traduce a 409 en el controlador.
    /// </summary>
    public class ReposicionPendienteException : Exception
    {
        public const string MENSAJE = "No se puede calcular la reposición: el almacén destino tiene una reposición anterior pendiente de contabilizar. Contabilízala (o bórrala) antes de generar otra.";

        public ReposicionPendienteException(Exception inner) : base(MENSAJE, inner)
        {
        }
    }

    public class RepositorioTraspasos : IRepositorioTraspasos
    {
        // Texto del raiserror de prdRellenarReposicionStock (sin tildes en el trozo que se compara
        // para no depender de la intercalación ni de la codificación del mensaje).
        internal const string TEXTO_ERROR_REPOSICION_PENDIENTE = "pendiente de contabilizar";

        public async Task<List<LineaReposicionStockSP>> LeerPropuesta(string empresa, string origen, string destino)
        {
            using (var db = new NVEntities())
            {
                db.Database.CommandTimeout = 120; // El SP recorre un cursor sobre los pendientes
                try
                {
                    // Parámetros siempre parametrizados (#553, seguridad): nunca concatenar.
                    return await db.Database
                        .SqlQuery<LineaReposicionStockSP>(
                            "prdRellenarReposicionStock @Empresa, @AlmacenOrigen, @AlmacenDestino",
                            new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = empresa },
                            new SqlParameter("@AlmacenOrigen", SqlDbType.Char, 3) { Value = origen },
                            new SqlParameter("@AlmacenDestino", SqlDbType.Char, 3) { Value = destino })
                        .ToListAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (EsErrorReposicionPendiente(ex))
                {
                    throw new ReposicionPendienteException(ex);
                }
            }
        }

        /// <summary>¿Alguna excepción de la cadena es el raiserror de «reposición anterior pendiente»?</summary>
        internal static bool EsErrorReposicionPendiente(Exception ex)
        {
            for (Exception actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual is SqlException && EsMensajeReposicionPendiente(actual.Message))
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool EsMensajeReposicionPendiente(string mensaje)
        {
            return mensaje != null
                && mensaje.IndexOf(TEXTO_ERROR_REPOSICION_PENDIENTE, StringComparison.OrdinalIgnoreCase) >= 0
                && mensaje.IndexOf("reposici", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
