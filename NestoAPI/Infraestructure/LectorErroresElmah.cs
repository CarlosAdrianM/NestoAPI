using NestoAPI.Models;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Transactions;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#346: lectura (solo lectura) de dbo.ELMAH_Error, para saber si un error ya está registrado y no repetirlo
    /// después de un reciclaje del pool.
    /// </summary>
    public interface ILectorErroresElmah
    {
        /// <summary>True si ELMAH tiene un error con exactamente ese Message desde <paramref name="desdeUtc"/>.</summary>
        bool HayErrorConMensajeDesde(string mensaje, DateTime desdeUtc);
    }

    public class LectorErroresElmahSql : ILectorErroresElmah
    {
        /// <summary>ELMAH (SqlErrorLog) guarda el Message en un nvarchar(500): lo que pase de ahí se pierde.</summary>
        internal const int LONGITUD_MENSAJE_ELMAH = 500;

        internal static string ComoLoGuardaElmah(string mensaje)
        {
            string texto = mensaje ?? string.Empty;
            return texto.Length > LONGITUD_MENSAJE_ELMAH ? texto.Substring(0, LONGITUD_MENSAJE_ELMAH) : texto;
        }

        public bool HayErrorConMensajeDesde(string mensaje, DateTime desdeUtc)
        {
            // Fuera de la transacción del llamante (como ElmahHelper, #182): que una consulta de diagnóstico no la promocione ni la tumbe
            using (var sinTransaccion = new TransactionScope(TransactionScopeOption.Suppress))
            using (NVEntities db = new NVEntities())
            {
                db.Database.CommandTimeout = 5;
                // NOLOCK: es solo diagnóstico; no debe esperar a ELMAH mientras escribe
                bool hay = db.Database.SqlQuery<int>(
                    "SELECT TOP (1) 1 FROM dbo.ELMAH_Error WITH (NOLOCK) " +
                    "WHERE Application = @aplicacion AND TimeUtc >= @desde AND Message = @mensaje",
                    new SqlParameter("@aplicacion", SqlDbType.NVarChar, 60) { Value = "NestoAPI" },
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desdeUtc },
                    new SqlParameter("@mensaje", SqlDbType.NVarChar, LONGITUD_MENSAJE_ELMAH) { Value = ComoLoGuardaElmah(mensaje) })
                    .Any();
                sinTransaccion.Complete();
                return hay;
            }
        }
    }
}
