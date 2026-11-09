using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// <see cref="ILectorEdicionesPrecioMedio"/> con ADO.NET sobre <c>LogModificaciones</c> (Issue #547). SOLO LECTURA y
    /// con <c>WITH (NOLOCK)</c>. Solo se llama para los productos que salen «Distinto» por la media (unos pocos por pasada):
    /// - el inicio de la última pasada del SP de la empresa = el <c>MAX(Fecha)</c> de sus filas
    ///   <c>Procedimiento LIKE 'prdActualizarPreciosMedios%'</c> (todas llevan la hora de inicio: 00:30:01); va por el
    ///   índice agrupado (Fecha) hacia atrás, ~80 ms;
    /// - las filas de <c>Productos.PrecioMedio</c> del producto, por el índice (Columna, Numero).
    /// </summary>
    public class LectorEdicionesPrecioMedioSql : ILectorEdicionesPrecioMedio
    {
        private const int TIMEOUT_SEGUNDOS = 60;

        // @sistema0..N: EdicionesPrecioMedio.USUARIOS_DEL_SISTEMA. La edición cuenta si después nadie del sistema la ha
        // vuelto a escribir (si el SP o la API la han pisado, la diferencia ya no la explica la edición).
        internal static readonly string SQL_ULTIMA_EDICION = @"
DECLARE @desde datetime = (SELECT MAX(Fecha) FROM LogModificaciones WITH (NOLOCK)
                           WHERE Empresa = @empresa AND Procedimiento LIKE 'prdActualizarPreciosMedios%');
IF @desde IS NULL SET @desde = DATEADD(day, -@diasSinPasada, GETDATE());

SELECT TOP 1 RTRIM(m.Usuario) AS Usuario, m.Fecha, LTRIM(RTRIM(m.ValorAntiguo)) AS ValorAntiguo
FROM LogModificaciones m WITH (NOLOCK)
WHERE m.Columna = 'PrecioMedio' AND m.Numero = @producto AND m.Tabla = 'Productos' AND m.Empresa = @empresa
  AND m.Fecha > @desde AND m.Usuario NOT IN (" + ListaSistema() + @")
  AND NOT EXISTS (SELECT 1 FROM LogModificaciones s WITH (NOLOCK)
                  WHERE s.Columna = 'PrecioMedio' AND s.Numero = @producto AND s.Tabla = 'Productos' AND s.Empresa = @empresa
                    AND s.Fecha > m.Fecha AND s.Usuario IN (" + ListaSistema() + @"))
ORDER BY m.Fecha DESC";

        private readonly string cadenaConexion;

        public LectorEdicionesPrecioMedioSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion ?? throw new ArgumentNullException(nameof(cadenaConexion));
        }

        /// <summary>La misma cadena que la sombra (NestoConnection con nombre de aplicación propio).</summary>
        public static LectorEdicionesPrecioMedioSql DesdeConfiguracion()
        {
            return new LectorEdicionesPrecioMedioSql(RepositorioPreciosMediosSql.CadenaDesdeConfiguracion());
        }

        public EdicionManualPrecioMedio UltimaEdicionAMano(string empresa, string producto)
        {
            using (SqlConnection conexion = new SqlConnection(cadenaConexion))
            using (SqlCommand comando = new SqlCommand(SQL_ULTIMA_EDICION, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                conexion.Open();
                // VarChar: las columnas son char; un NVarChar obligaría a convertir la columna y perdería el índice.
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = (object)empresa?.Trim() ?? DBNull.Value;
                comando.Parameters.Add("@producto", SqlDbType.VarChar, 50).Value = (object)producto?.Trim() ?? DBNull.Value;
                comando.Parameters.Add("@diasSinPasada", SqlDbType.Int).Value = EdicionesPrecioMedio.DIAS_SIN_PASADA;
                for (int i = 0; i < EdicionesPrecioMedio.USUARIOS_DEL_SISTEMA.Count; i++)
                {
                    comando.Parameters.Add("@sistema" + i, SqlDbType.VarChar, 30).Value = EdicionesPrecioMedio.USUARIOS_DEL_SISTEMA[i];
                }
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    if (!lector.Read())
                    {
                        return null;
                    }
                    return new EdicionManualPrecioMedio
                    {
                        Usuario = lector.IsDBNull(0) ? null : lector.GetString(0),
                        Fecha = lector.GetDateTime(1),
                        ValorAntiguo = lector.IsDBNull(2) ? null : lector.GetString(2)
                    };
                }
            }
        }

        private static string ListaSistema()
        {
            return string.Join(", ", Enumerable.Range(0, EdicionesPrecioMedio.USUARIOS_DEL_SISTEMA.Count).Select(i => "@sistema" + i));
        }
    }
}
