using System;
using System.Configuration;
using System.Data.SqlClient;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>
    /// Sugerencia 1478 (Manuel, 26/11/26): en la ficha de producto de Nesto, pasar a la referencia anterior o a la siguiente que
    /// exista sin teclear el número. La API dice cuáles son; Nesto solo pinta dos flechas.
    /// </summary>
    public class VecinosProductoDTO
    {
        public string Producto { get; set; }
        /// <summary>La referencia anterior que existe; null si es la primera.</summary>
        public string Anterior { get; set; }
        /// <summary>La referencia siguiente que existe; null si es la última.</summary>
        public string Siguiente { get; set; }
    }

    /// <summary>
    /// LA REGLA (pura): una referencia NUMÉRICA (solo cifras, de 1 a 9: casi todas) tiene por vecinas la numérica inmediatamente
    /// menor y la inmediatamente mayor, por su VALOR (la 9999 va antes de la 10000) y saltando los huecos; una que no es numérica
    /// (MF…, IMPE…) las tiene, por orden alfabético, entre las que tampoco lo son. Cuentan todas las fichas de la empresa, también
    /// las anuladas: son las que hay.
    /// </summary>
    public static class ReglaVecinosProducto
    {
        public const int CIFRAS_MAXIMAS = 9;

        public static bool EsNumerica(string referencia)
        {
            string limpia = referencia?.Trim();
            if (string.IsNullOrEmpty(limpia) || limpia.Length > CIFRAS_MAXIMAS)
            {
                return false;
            }
            foreach (char c in limpia)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }
            return true;
        }
    }

    public interface ILectorVecinosProducto
    {
        /// <summary>Null si el producto no existe en esa empresa.</summary>
        VecinosProductoDTO Leer(string empresa, string producto);
    }

    /// <summary>Solo lectura, una ida a la BD.</summary>
    public class LectorVecinosProductoSql : ILectorVecinosProducto
    {
        // Las numéricas: solo cifras (ni signo ni espacios en medio) y que quepan en un int.
        private const string FILTRO_NUMERICA = "RTRIM(p.Número) NOT LIKE '%[^0-9]%' AND LEN(RTRIM(p.Número)) BETWEEN 1 AND 9";
        // Sin TRY_CONVERT (la BD va con un nivel de compatibilidad que no lo tiene): el CASE no convierte lo que no es numérico.
        private const string VALOR = "CASE WHEN " + FILTRO_NUMERICA + " THEN CONVERT(int, RTRIM(p.Número)) END";

        internal const string SQL_NUMERICA = @"
SELECT
  (SELECT TOP 1 RTRIM(p.Número) FROM Productos p WITH (NOLOCK)
    WHERE p.Empresa = @empresa AND " + VALOR + @" < @valor
    ORDER BY " + VALOR + @" DESC) AS Anterior,
  (SELECT TOP 1 RTRIM(p.Número) FROM Productos p WITH (NOLOCK)
    WHERE p.Empresa = @empresa AND " + VALOR + @" > @valor
    ORDER BY " + VALOR + @") AS Siguiente,
  (SELECT TOP 1 RTRIM(p.Número) FROM Productos p WITH (NOLOCK) WHERE p.Empresa = @empresa AND p.Número = @producto) AS Producto";

        internal const string SQL_NO_NUMERICA = @"
SELECT
  (SELECT TOP 1 RTRIM(p.Número) FROM Productos p WITH (NOLOCK)
    WHERE p.Empresa = @empresa AND NOT (" + FILTRO_NUMERICA + @") AND RTRIM(p.Número) < @producto
    ORDER BY RTRIM(p.Número) DESC) AS Anterior,
  (SELECT TOP 1 RTRIM(p.Número) FROM Productos p WITH (NOLOCK)
    WHERE p.Empresa = @empresa AND NOT (" + FILTRO_NUMERICA + @") AND RTRIM(p.Número) > @producto
    ORDER BY RTRIM(p.Número)) AS Siguiente,
  (SELECT TOP 1 RTRIM(p.Número) FROM Productos p WITH (NOLOCK) WHERE p.Empresa = @empresa AND p.Número = @producto) AS Producto";

        private readonly string cadenaConexion;

        public LectorVecinosProductoSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion ?? throw new ArgumentNullException(nameof(cadenaConexion));
        }

        public static LectorVecinosProductoSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new LectorVecinosProductoSql(origen.ConnectionString);
        }

        public VecinosProductoDTO Leer(string empresa, string producto)
        {
            string referencia = producto?.Trim();
            if (string.IsNullOrEmpty(referencia))
            {
                return null;
            }
            bool numerica = ReglaVecinosProducto.EsNumerica(referencia);
            using (SqlConnection conexion = new SqlConnection(cadenaConexion))
            using (SqlCommand comando = new SqlCommand(numerica ? SQL_NUMERICA : SQL_NO_NUMERICA, conexion))
            {
                comando.Parameters.AddWithValue("@empresa", empresa);
                comando.Parameters.AddWithValue("@producto", referencia);
                if (numerica)
                {
                    comando.Parameters.AddWithValue("@valor", int.Parse(referencia));
                }
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    if (!lector.Read() || lector.IsDBNull(2))
                    {
                        return null;
                    }
                    return new VecinosProductoDTO
                    {
                        Producto = lector.GetString(2),
                        Anterior = lector.IsDBNull(0) ? null : lector.GetString(0),
                        Siguiente = lector.IsDBNull(1) ? null : lector.GetString(1)
                    };
                }
            }
        }
    }
}
