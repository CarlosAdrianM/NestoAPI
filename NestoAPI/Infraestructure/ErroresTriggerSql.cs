using System;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#473: los triggers de validación de la base de datos (IBAN con dígito de control
    /// incorrecto, CIF/NIF repetido...) cortan con RAISERROR/THROW y un texto pensado para leerse.
    /// Hasta ahora ese texto se perdía dentro de un 500 genérico (y de un error rojo en ELMAH):
    /// el usuario reintentaba a ciegas (ocho veces en un minuto, 08/09/26) sin saber qué corregir.
    ///
    /// Esta clase reconoce ese caso y devuelve el mensaje del trigger, para que el controller lo
    /// convierta en un 400 con el motivo, igual que hace con las validaciones de negocio propias.
    /// Solo reconoce el error DE USUARIO (número 50000); un timeout, un deadlock o una FK rota
    /// siguen siendo 500 y siguen llegando a ELMAH.
    /// </summary>
    public static class ErroresTriggerSql
    {
        /// <summary>RAISERROR/THROW sin número propio: lo que usan los triggers de esta base.</summary>
        public const int NUMERO_ERROR_DE_USUARIO = 50000;

        /// <summary>
        /// El mensaje del trigger de validación que hay dentro de <paramref name="ex"/> (a
        /// cualquier profundidad de InnerException), o null si la excepción no es eso.
        /// </summary>
        public static string MensajeDeValidacion(Exception ex)
        {
            SqlException sql = EncontrarSqlException(ex);
            if (sql == null)
            {
                return null;
            }
            // Un RAISERROR dentro de un trigger llega acompañado del 3609 ("La transacción terminó
            // en el desencadenador. Se anuló el lote."). Ese no le dice nada al usuario: se coge
            // solo el de usuario.
            SqlError deUsuario = sql.Errors.Cast<SqlError>().FirstOrDefault(e => e.Number == NUMERO_ERROR_DE_USUARIO);
            string mensaje = deUsuario?.Message?.Trim();
            return string.IsNullOrEmpty(mensaje) ? null : mensaje;
        }

        private static SqlException EncontrarSqlException(Exception ex)
        {
            for (Exception actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual is SqlException sql)
                {
                    return sql;
                }
            }
            return null;
        }
    }
}
