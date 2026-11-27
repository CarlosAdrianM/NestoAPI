using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Tablero
{
    /// <summary>Una fila de dbo.CalidadKpiSemanas: el valor de un KPI para un dueño en una semana cerrada.</summary>
    public class SemanaKpiGuardada
    {
        public string Kpi { get; set; }
        public TipoDuenoKpi TipoDueno { get; set; }
        public string Dueno { get; set; }
        public DateTime Semana { get; set; }
        public decimal? Valor { get; set; }
        public decimal? Numerador { get; set; }
        public decimal? Denominador { get; set; }
        public string Detalle { get; set; }
        public DateTime CalculadoEl { get; set; }
    }

    public interface IRepositorioTablero
    {
        /// <summary>Existen dbo.CalidadKpiSemanas y dbo.CalidadKpiUmbrales (Scripts/Issue914t_CalidadTablero.sql).</summary>
        bool TablasExisten();

        /// <summary>Sustituye las filas del KPI en esa semana por estas (en una transacción): un dueño que ya no sale, desaparece.</summary>
        void GuardarSemana(string kpi, DateTime lunes, IReadOnlyList<ValorKpi> valores, DateTime calculadoEl);

        /// <summary>Las semanas guardadas en [desde, hasta), del KPI (null = todos), del tipo (null = todos) y del dueño (null = todos).</summary>
        List<SemanaKpiGuardada> LeerSemanas(string kpi, TipoDuenoKpi? tipo, string dueno, DateTime desde, DateTime hasta);

        /// <summary>Los lunes que ya tienen alguna fila del KPI desde <paramref name="desde"/>.</summary>
        HashSet<DateTime> SemanasGuardadas(string kpi, DateTime desde);

        /// <summary>Todos los umbrales (con su histórico), del KPI o de todos.</summary>
        List<UmbralesKpi> LeerUmbrales(string kpi = null);

        /// <summary>Guarda el umbral (si ya hay uno con el mismo VigenteDesde, lo cambia) y deja el rastro en Modificaciones, en una transacción.</summary>
        void AprobarUmbrales(UmbralesKpi umbrales, string rastroAnterior, string rastroNuevo);
    }

    /// <summary>NestoAPI#914: dbo.CalidadKpiSemanas y dbo.CalidadKpiUmbrales (fuera del EDMX). Lecturas con NOLOCK; escrituras, en transacción.</summary>
    public class RepositorioTableroSql : IRepositorioTablero
    {
        internal const string NOMBRE_APLICACION = "NestoAPI (tablero de KPI #914)";
        internal const int TIMEOUT_SEGUNDOS = 60;
        public const string TABLA_SEMANAS = "dbo.CalidadKpiSemanas";
        public const string TABLA_UMBRALES = "dbo.CalidadKpiUmbrales";

        internal const string SQL_TABLAS = @"
SELECT CAST(CASE WHEN OBJECT_ID('dbo.CalidadKpiSemanas', 'U') IS NULL OR OBJECT_ID('dbo.CalidadKpiUmbrales', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_BORRAR_SEMANA = "DELETE FROM dbo.CalidadKpiSemanas WHERE Kpi = @Kpi AND Semana = @Semana;";

        internal const string SQL_INSERTAR_SEMANA = @"
INSERT INTO dbo.CalidadKpiSemanas (Kpi, TipoDueno, Dueno, Semana, Valor, Numerador, Denominador, Detalle, CalculadoEl)
VALUES (@Kpi, @TipoDueno, @Dueno, @Semana, @Valor, @Numerador, @Denominador, @Detalle, @CalculadoEl);";

        internal const string SQL_LEER_SEMANAS = @"
SELECT Kpi, TipoDueno, Dueno, Semana, Valor, Numerador, Denominador, Detalle, CalculadoEl
FROM dbo.CalidadKpiSemanas WITH (NOLOCK)
WHERE Semana >= @Desde AND Semana < @Hasta
  AND (@Kpi IS NULL OR Kpi = @Kpi) AND (@TipoDueno IS NULL OR TipoDueno = @TipoDueno) AND (@Dueno IS NULL OR Dueno = @Dueno)
ORDER BY Kpi, TipoDueno, Dueno, Semana DESC";

        internal const string SQL_SEMANAS_GUARDADAS = "SELECT DISTINCT Semana FROM dbo.CalidadKpiSemanas WITH (NOLOCK) WHERE Kpi = @Kpi AND Semana >= @Desde";

        internal const string SQL_LEER_UMBRALES = @"
SELECT Kpi, TipoDueno, CortePoorFair, CorteFairGood, CorteGoodFantastic, VigenteDesde, Usuario, FechaModificacion
FROM dbo.CalidadKpiUmbrales WITH (NOLOCK)
WHERE (@Kpi IS NULL OR Kpi = @Kpi)
ORDER BY Kpi, TipoDueno, VigenteDesde";

        internal const string SQL_GUARDAR_UMBRAL = @"
IF EXISTS (SELECT 1 FROM dbo.CalidadKpiUmbrales WHERE Kpi = @Kpi AND TipoDueno = @TipoDueno AND VigenteDesde = @VigenteDesde)
    UPDATE dbo.CalidadKpiUmbrales
    SET CortePoorFair = @CortePoorFair, CorteFairGood = @CorteFairGood, CorteGoodFantastic = @CorteGoodFantastic, Usuario = @Usuario, FechaModificacion = GETDATE()
    WHERE Kpi = @Kpi AND TipoDueno = @TipoDueno AND VigenteDesde = @VigenteDesde;
ELSE
    INSERT INTO dbo.CalidadKpiUmbrales (Kpi, TipoDueno, CortePoorFair, CorteFairGood, CorteGoodFantastic, VigenteDesde, Usuario, FechaModificacion)
    VALUES (@Kpi, @TipoDueno, @CortePoorFair, @CorteFairGood, @CorteGoodFantastic, @VigenteDesde, @Usuario, GETDATE());
INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @Usuario);";

        private readonly string cadenaConexion;

        public RepositorioTableroSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion;
        }

        public static RepositorioTableroSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioTableroSql(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        public bool TablasExisten()
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_TABLAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                conexion.Open();
                return Convert.ToBoolean(comando.ExecuteScalar());
            }
        }

        public void GuardarSemana(string kpi, DateTime lunes, IReadOnlyList<ValorKpi> valores, DateTime calculadoEl)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                conexion.Open();
                using (SqlTransaction transaccion = conexion.BeginTransaction())
                {
                    using (var borrar = new SqlCommand(SQL_BORRAR_SEMANA, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                    {
                        _ = borrar.Parameters.Add(new SqlParameter("@Kpi", SqlDbType.VarChar, 40) { Value = kpi });
                        _ = borrar.Parameters.Add(new SqlParameter("@Semana", SqlDbType.Date) { Value = lunes.Date });
                        _ = borrar.ExecuteNonQuery();
                    }
                    foreach (ValorKpi v in (valores ?? new List<ValorKpi>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Dueno)))
                    {
                        using (var insertar = new SqlCommand(SQL_INSERTAR_SEMANA, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                        {
                            _ = insertar.Parameters.Add(new SqlParameter("@Kpi", SqlDbType.VarChar, 40) { Value = kpi });
                            _ = insertar.Parameters.Add(new SqlParameter("@TipoDueno", SqlDbType.VarChar, 12) { Value = v.TipoDueno.ToString() });
                            _ = insertar.Parameters.Add(new SqlParameter("@Dueno", SqlDbType.VarChar, 30) { Value = Recortar(v.Dueno.Trim(), 30) });
                            _ = insertar.Parameters.Add(new SqlParameter("@Semana", SqlDbType.Date) { Value = lunes.Date });
                            _ = insertar.Parameters.Add(Decimal("@Valor", v.Valor));
                            _ = insertar.Parameters.Add(Decimal("@Numerador", v.Numerador));
                            _ = insertar.Parameters.Add(Decimal("@Denominador", v.Denominador));
                            _ = insertar.Parameters.Add(new SqlParameter("@Detalle", SqlDbType.NVarChar, 400) { Value = (object)Recortar(v.Detalle, 400) ?? DBNull.Value });
                            _ = insertar.Parameters.Add(new SqlParameter("@CalculadoEl", SqlDbType.DateTime) { Value = calculadoEl });
                            _ = insertar.ExecuteNonQuery();
                        }
                    }
                    transaccion.Commit();
                }
            }
        }

        public List<SemanaKpiGuardada> LeerSemanas(string kpi, TipoDuenoKpi? tipo, string dueno, DateTime desde, DateTime hasta)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_LEER_SEMANAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                _ = comando.Parameters.Add(new SqlParameter("@Desde", SqlDbType.Date) { Value = desde.Date });
                _ = comando.Parameters.Add(new SqlParameter("@Hasta", SqlDbType.Date) { Value = hasta.Date });
                _ = comando.Parameters.Add(new SqlParameter("@Kpi", SqlDbType.VarChar, 40) { Value = (object)kpi ?? DBNull.Value });
                _ = comando.Parameters.Add(new SqlParameter("@TipoDueno", SqlDbType.VarChar, 12) { Value = (object)tipo?.ToString() ?? DBNull.Value });
                _ = comando.Parameters.Add(new SqlParameter("@Dueno", SqlDbType.VarChar, 30) { Value = (object)dueno?.Trim() ?? DBNull.Value });
                conexion.Open();
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    return LeerFilasSemanas(r);
                }
            }
        }

        public HashSet<DateTime> SemanasGuardadas(string kpi, DateTime desde)
        {
            var semanas = new HashSet<DateTime>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_SEMANAS_GUARDADAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                _ = comando.Parameters.Add(new SqlParameter("@Kpi", SqlDbType.VarChar, 40) { Value = kpi });
                _ = comando.Parameters.Add(new SqlParameter("@Desde", SqlDbType.Date) { Value = desde.Date });
                conexion.Open();
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    while (r.Read())
                    {
                        _ = semanas.Add(r.GetDateTime(0).Date);
                    }
                }
            }
            return semanas;
        }

        public List<UmbralesKpi> LeerUmbrales(string kpi = null)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_LEER_UMBRALES, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                _ = comando.Parameters.Add(new SqlParameter("@Kpi", SqlDbType.VarChar, 40) { Value = (object)kpi ?? DBNull.Value });
                conexion.Open();
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    return LeerFilasUmbrales(r);
                }
            }
        }

        public void AprobarUmbrales(UmbralesKpi u, string rastroAnterior, string rastroNuevo)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                conexion.Open();
                using (SqlTransaction transaccion = conexion.BeginTransaction())
                using (var comando = new SqlCommand(SQL_GUARDAR_UMBRAL, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Kpi", SqlDbType.VarChar, 40) { Value = u.Kpi });
                    _ = comando.Parameters.Add(new SqlParameter("@TipoDueno", SqlDbType.VarChar, 12) { Value = u.TipoDueno.ToString() });
                    _ = comando.Parameters.Add(Decimal("@CortePoorFair", u.CortePoorFair));
                    _ = comando.Parameters.Add(Decimal("@CorteFairGood", u.CorteFairGood));
                    _ = comando.Parameters.Add(Decimal("@CorteGoodFantastic", u.CorteGoodFantastic));
                    _ = comando.Parameters.Add(new SqlParameter("@VigenteDesde", SqlDbType.Date) { Value = u.VigenteDesde.Date });
                    _ = comando.Parameters.Add(new SqlParameter("@Usuario", SqlDbType.NVarChar, 50) { Value = Recortar(u.Usuario, 50) ?? UsuarioAuditoriaHelper.DESCONOCIDO });
                    _ = comando.Parameters.Add(new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = "CalidadKpiUmbrales" });
                    _ = comando.Parameters.Add(new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = (object)rastroAnterior ?? DBNull.Value });
                    _ = comando.Parameters.Add(new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1) { Value = (object)rastroNuevo ?? DBNull.Value });
                    _ = comando.ExecuteNonQuery();
                    transaccion.Commit();
                }
            }
        }

        internal static List<SemanaKpiGuardada> LeerFilasSemanas(IDataReader r)
        {
            var filas = new List<SemanaKpiGuardada>();
            while (r.Read())
            {
                if (!Enum.TryParse(Convert.ToString(r["TipoDueno"])?.Trim(), true, out TipoDuenoKpi tipo))
                {
                    continue;
                }
                filas.Add(new SemanaKpiGuardada
                {
                    Kpi = Convert.ToString(r["Kpi"])?.Trim(),
                    TipoDueno = tipo,
                    Dueno = Convert.ToString(r["Dueno"])?.Trim(),
                    Semana = Convert.ToDateTime(r["Semana"]).Date,
                    Valor = DecimalONulo(r["Valor"]),
                    Numerador = DecimalONulo(r["Numerador"]),
                    Denominador = DecimalONulo(r["Denominador"]),
                    Detalle = r["Detalle"] is DBNull ? null : Convert.ToString(r["Detalle"]),
                    CalculadoEl = Convert.ToDateTime(r["CalculadoEl"])
                });
            }
            return filas;
        }

        internal static List<UmbralesKpi> LeerFilasUmbrales(IDataReader r)
        {
            var filas = new List<UmbralesKpi>();
            while (r.Read())
            {
                if (!Enum.TryParse(Convert.ToString(r["TipoDueno"])?.Trim(), true, out TipoDuenoKpi tipo))
                {
                    continue;
                }
                filas.Add(new UmbralesKpi
                {
                    Kpi = Convert.ToString(r["Kpi"])?.Trim(),
                    TipoDueno = tipo,
                    CortePoorFair = Convert.ToDecimal(r["CortePoorFair"]),
                    CorteFairGood = Convert.ToDecimal(r["CorteFairGood"]),
                    CorteGoodFantastic = Convert.ToDecimal(r["CorteGoodFantastic"]),
                    VigenteDesde = Convert.ToDateTime(r["VigenteDesde"]).Date,
                    Usuario = r["Usuario"] is DBNull ? null : Convert.ToString(r["Usuario"])?.Trim(),
                    FechaModificacion = r["FechaModificacion"] is DBNull ? (DateTime?)null : Convert.ToDateTime(r["FechaModificacion"])
                });
            }
            return filas;
        }

        private static decimal? DecimalONulo(object valor) => valor is DBNull || valor == null ? (decimal?)null : Convert.ToDecimal(valor);

        private static SqlParameter Decimal(string nombre, decimal? valor) =>
            new SqlParameter(nombre, SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = (object)valor ?? DBNull.Value };

        private static string Recortar(string texto, int maximo) =>
            string.IsNullOrEmpty(texto) || texto.Length <= maximo ? texto : texto.Substring(0, maximo);
    }
}
