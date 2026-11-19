using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#881 (corte 3): una fila de dbo.RepartoParadasTiempos: una parada cerrada (entrega, incidencia, recogida, cobro, reposición)
    /// con lo que hace falta para medir cuánto se tarda de verdad. La hora y la posición de la parada ANTERIOR del mismo repartidor ese día
    /// las pone la propia inserción (con ellas: intervalo − conducción = minutos reales de parada).
    /// </summary>
    public class TiempoParadaReparto
    {
        public const string RESULTADO_ENTREGADA = "Entregada";
        public const string RESULTADO_INCIDENCIA = "Incidencia";
        public const string RESULTADO_RECOGIDA = "Recogida";
        /// <summary>El último bulto R de la reposición de una tienda (se recoge en la tienda).</summary>
        public const string TIPO_RECOGIDA_REPOSICION = "RecogidaReposicion";

        public long Id { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        public string Repartidor { get; set; }
        public DateTime Dia { get; set; }
        /// <summary>Envio, NotaEntrega, NotaRecogida, NotaCobro, Reposicion o RecogidaReposicion.</summary>
        public string TipoParada { get; set; }
        public string Resultado { get; set; }
        public int? EnvioId { get; set; }
        public int? Pedido { get; set; }
        public int? Traspaso { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string CodigoPostal { get; set; }
        public int? Bultos { get; set; }
        /// <summary>Efectivo, Enlace, Tarjeta, NoCobrado… (null si no lleva cobro).</summary>
        public string FormaCobro { get; set; }
        public decimal? ImporteCobro { get; set; }
        public int? TipoServicioTecnicoId { get; set; }
        public string ServicioTecnico { get; set; }
        public int? MinutosServicioTecleados { get; set; }
        public DateTime? FechaHoraIr { get; set; }
        public DateTime? FechaHoraLlegada { get; set; }
        public DateTime FechaHoraFin { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        /// <summary>Los rellena la inserción (la parada anterior del mismo repartidor ese día).</summary>
        public DateTime? FechaHoraAnterior { get; set; }
        public decimal? LatitudAnterior { get; set; }
        public decimal? LongitudAnterior { get; set; }
        /// <summary>Los que daba la regla (<see cref="MinutosParadaReparto"/>) para esta parada.</summary>
        public decimal? MinutosPrevistos { get; set; }
        public DateTime FechaRegistro { get; set; }
    }

    /// <summary>NestoAPI#881 (corte 3): una foto de la previsión de fin de ruta (dbo.RepartoEstimaciones), para medir previsto contra real.</summary>
    public class FotoEstimacionReparto
    {
        public const string CALCULO_ESTIMACION = "Estimacion";
        public const string CALCULO_ORDEN_SUGERIDO = "OrdenSugerido";

        public string Repartidor { get; set; }
        /// <summary>La del servidor (la que cuenta para «una cada 10 minutos»).</summary>
        public DateTime FechaHora { get; set; }
        public DateTime HoraSalida { get; set; }
        public int NumeroParadas { get; set; }
        public int MinutosConduccion { get; set; }
        public int MinutosParadas { get; set; }
        public int MinutosDescarga { get; set; }
        public decimal Km { get; set; }
        public DateTime FinEstimado { get; set; }
        public DateTime? HoraCierre { get; set; }
        public bool PasaDelCierre { get; set; }
        public bool Aproximada { get; set; }
        public string Motor { get; set; }
        /// <summary>«Posicion» (salió de la posición de la PDA) o «Almacen».</summary>
        public string OrigenRuta { get; set; }
        public string Destino { get; set; }
        /// <summary>«Estimacion» u «OrdenSugerido».</summary>
        public string Calculo { get; set; }
        /// <summary>«Regla» o «Modelo»: quién dio los minutos de las paradas.</summary>
        public string MinutosDe { get; set; }
    }

    /// <summary>NestoAPI#881 (corte 3): las tablas de tiempos reales y de fotos (fuera del EDMX, ADO).</summary>
    public interface IRepositorioTiemposReparto
    {
        bool TablaTiemposExiste();
        bool TablaEstimacionesExiste();
        /// <summary>Idempotente por la clave: si ya está, no hace nada. Devuelve 1 si la ha insertado.</summary>
        Task<int> RegistrarTiempo(TiempoParadaReparto fila);
        /// <summary>Si hay otra del mismo repartidor dada de alta hace menos de <paramref name="minutos"/>, la actualiza; si no, inserta.</summary>
        Task RegistrarEstimacion(FotoEstimacionReparto foto, int minutos);
        /// <summary>Las paradas desde ese día (para el modelo; de noche).</summary>
        List<TiempoParadaReparto> LeerTiempos(DateTime desde);
    }

    public class RepositorioTiemposReparto : IRepositorioTiemposReparto
    {
        public const string TABLA_TIEMPOS = "RepartoParadasTiempos";
        public const string TABLA_ESTIMACIONES = "RepartoEstimaciones";
        internal const string NOMBRE_APLICACION = "NestoAPI-TiemposReparto";
        private const int TIMEOUT = 30;
        private const int TIMEOUT_LECTURA_NOCHE = 600;

        private static readonly Dictionary<string, bool> existe = new Dictionary<string, bool>();
        private static readonly object bloqueo = new object();

        private readonly string cadenaConexion;

        public RepositorioTiemposReparto(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = cadenaConexion;
        }

        public static RepositorioTiemposReparto DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioTiemposReparto(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        public bool TablaTiemposExiste() => Existe(TABLA_TIEMPOS);

        public bool TablaEstimacionesExiste() => Existe(TABLA_ESTIMACIONES);

        /// <summary>Solo se recuerda el «sí» (si el script se lanza con la API arrancada, la siguiente vez ya lo ve). Ante error, no.</summary>
        private bool Existe(string tabla)
        {
            lock (bloqueo)
            {
                if (existe.TryGetValue(tabla, out bool si) && si)
                {
                    return true;
                }
            }
            try
            {
                using (SqlConnection conexion = Abrir())
                using (var comando = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@tabla, 'U') IS NULL THEN 0 ELSE 1 END", conexion) { CommandTimeout = TIMEOUT })
                {
                    comando.Parameters.Add("@tabla", SqlDbType.NVarChar, 200).Value = "dbo." + tabla;
                    bool hay = Convert.ToInt32(comando.ExecuteScalar()) == 1;
                    if (hay)
                    {
                        lock (bloqueo)
                        {
                            existe[tabla] = true;
                        }
                    }
                    return hay;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal const string SQL_INSERTAR_TIEMPO = @"
IF NOT EXISTS (SELECT 1 FROM dbo." + TABLA_TIEMPOS + @" WHERE ClaveIdempotencia = @Clave)
INSERT INTO dbo." + TABLA_TIEMPOS + @" (ClaveIdempotencia, Repartidor, Dia, TipoParada, Resultado, EnvioId, Pedido, Traspaso, Empresa, Cliente, Contacto,
    CodigoPostal, Bultos, FormaCobro, ImporteCobro, TipoServicioTecnicoId, ServicioTecnico, MinutosServicioTecleados, FechaHoraIr, FechaHoraLlegada,
    FechaHoraFin, Latitud, Longitud, FechaHoraAnterior, LatitudAnterior, LongitudAnterior, MinutosPrevistos, FechaRegistro)
SELECT @Clave, @Repartidor, @Dia, @TipoParada, @Resultado, @EnvioId, @Pedido, @Traspaso, @Empresa, @Cliente, @Contacto,
    @CodigoPostal, @Bultos, @FormaCobro, @ImporteCobro, @TipoServicioTecnicoId, @ServicioTecnico, @MinutosTecleados, @FechaHoraIr, @FechaHoraLlegada,
    @FechaHoraFin, @Latitud, @Longitud, a.FechaHoraFin, a.Latitud, a.Longitud, @MinutosPrevistos, GETDATE()
FROM (SELECT 1 AS Uno) d
OUTER APPLY (SELECT TOP 1 t.FechaHoraFin, t.Latitud, t.Longitud FROM dbo." + TABLA_TIEMPOS + @" t WITH (UPDLOCK, HOLDLOCK)
             WHERE t.Repartidor = @Repartidor AND t.Dia = @Dia AND t.FechaHoraFin < @FechaHoraFin
             ORDER BY t.FechaHoraFin DESC, t.Id DESC) a;";

        public async Task<int> RegistrarTiempo(TiempoParadaReparto f)
        {
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_INSERTAR_TIEMPO, conexion) { CommandTimeout = TIMEOUT })
            {
                comando.Parameters.Add("@Clave", SqlDbType.UniqueIdentifier).Value = f.ClaveIdempotencia;
                comando.Parameters.Add("@Repartidor", SqlDbType.VarChar, 50).Value = Recortar(f.Repartidor, 50) ?? string.Empty;
                comando.Parameters.Add("@Dia", SqlDbType.Date).Value = f.Dia.Date;
                comando.Parameters.Add("@TipoParada", SqlDbType.VarChar, 20).Value = Recortar(f.TipoParada, 20) ?? string.Empty;
                comando.Parameters.Add("@Resultado", SqlDbType.VarChar, 12).Value = Recortar(f.Resultado, 12) ?? string.Empty;
                Entero(comando, "@EnvioId", f.EnvioId);
                Entero(comando, "@Pedido", f.Pedido);
                Entero(comando, "@Traspaso", f.Traspaso);
                Texto(comando, "@Empresa", f.Empresa, 3);
                Texto(comando, "@Cliente", f.Cliente, 10);
                Texto(comando, "@Contacto", f.Contacto, 3);
                Texto(comando, "@CodigoPostal", f.CodigoPostal, 10);
                Entero(comando, "@Bultos", f.Bultos);
                Texto(comando, "@FormaCobro", f.FormaCobro, 20);
                Decimal(comando, "@ImporteCobro", f.ImporteCobro, 12, 2);
                Entero(comando, "@TipoServicioTecnicoId", f.TipoServicioTecnicoId);
                comando.Parameters.Add("@ServicioTecnico", SqlDbType.NVarChar, 50).Value = (object)Recortar(f.ServicioTecnico, 50) ?? DBNull.Value;
                Entero(comando, "@MinutosTecleados", f.MinutosServicioTecleados);
                Fecha(comando, "@FechaHoraIr", f.FechaHoraIr);
                Fecha(comando, "@FechaHoraLlegada", f.FechaHoraLlegada);
                comando.Parameters.Add("@FechaHoraFin", SqlDbType.DateTime).Value = f.FechaHoraFin;
                Decimal(comando, "@Latitud", f.Latitud, 9, 6);
                Decimal(comando, "@Longitud", f.Longitud, 9, 6);
                Decimal(comando, "@MinutosPrevistos", f.MinutosPrevistos, 6, 1);
                try
                {
                    return await comando.ExecuteNonQueryAsync().ConfigureAwait(false) > 0 ? 1 : 0;
                }
                catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                {
                    return 0; // otra petición con la misma clave a la vez
                }
            }
        }

        internal const string SQL_ESTIMACION = @"
DECLARE @Id int = (SELECT TOP 1 Id FROM dbo." + TABLA_ESTIMACIONES + @" WITH (UPDLOCK, HOLDLOCK)
                   WHERE Repartidor = @Repartidor AND FechaAlta >= DATEADD(MINUTE, -@Minutos, @FechaHora) ORDER BY Id DESC);
IF @Id IS NOT NULL
    UPDATE dbo." + TABLA_ESTIMACIONES + @" SET FechaHora = @FechaHora, Dia = @Dia, HoraSalida = @HoraSalida, NumeroParadas = @NumeroParadas,
        MinutosConduccion = @MinutosConduccion, MinutosParadas = @MinutosParadas, MinutosDescarga = @MinutosDescarga, Km = @Km,
        FinEstimado = @FinEstimado, HoraCierre = @HoraCierre, PasaDelCierre = @PasaDelCierre, Aproximada = @Aproximada, Motor = @Motor,
        OrigenRuta = @OrigenRuta, Destino = @Destino, Calculo = @Calculo, MinutosDe = @MinutosDe, Actualizaciones = Actualizaciones + 1
    WHERE Id = @Id;
ELSE
    INSERT INTO dbo." + TABLA_ESTIMACIONES + @" (Repartidor, FechaAlta, FechaHora, Dia, HoraSalida, NumeroParadas, MinutosConduccion, MinutosParadas,
        MinutosDescarga, Km, FinEstimado, HoraCierre, PasaDelCierre, Aproximada, Motor, OrigenRuta, Destino, Calculo, MinutosDe, Actualizaciones)
    VALUES (@Repartidor, @FechaHora, @FechaHora, @Dia, @HoraSalida, @NumeroParadas, @MinutosConduccion, @MinutosParadas, @MinutosDescarga, @Km,
        @FinEstimado, @HoraCierre, @PasaDelCierre, @Aproximada, @Motor, @OrigenRuta, @Destino, @Calculo, @MinutosDe, 0);";

        public async Task RegistrarEstimacion(FotoEstimacionReparto f, int minutos)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            using (var comando = new SqlCommand(SQL_ESTIMACION, conexion, transaccion) { CommandTimeout = TIMEOUT })
            {
                comando.Parameters.Add("@Repartidor", SqlDbType.VarChar, 50).Value = Recortar(f.Repartidor, 50) ?? string.Empty;
                comando.Parameters.Add("@Minutos", SqlDbType.Int).Value = minutos;
                comando.Parameters.Add("@FechaHora", SqlDbType.DateTime).Value = f.FechaHora;
                comando.Parameters.Add("@Dia", SqlDbType.Date).Value = f.HoraSalida.Date;
                comando.Parameters.Add("@HoraSalida", SqlDbType.DateTime).Value = f.HoraSalida;
                comando.Parameters.Add("@NumeroParadas", SqlDbType.Int).Value = f.NumeroParadas;
                comando.Parameters.Add("@MinutosConduccion", SqlDbType.Int).Value = f.MinutosConduccion;
                comando.Parameters.Add("@MinutosParadas", SqlDbType.Int).Value = f.MinutosParadas;
                comando.Parameters.Add("@MinutosDescarga", SqlDbType.Int).Value = f.MinutosDescarga;
                Decimal(comando, "@Km", f.Km, 8, 1);
                comando.Parameters.Add("@FinEstimado", SqlDbType.DateTime).Value = f.FinEstimado;
                Fecha(comando, "@HoraCierre", f.HoraCierre);
                comando.Parameters.Add("@PasaDelCierre", SqlDbType.Bit).Value = f.PasaDelCierre;
                comando.Parameters.Add("@Aproximada", SqlDbType.Bit).Value = f.Aproximada;
                Texto(comando, "@Motor", f.Motor, 20);
                Texto(comando, "@OrigenRuta", f.OrigenRuta, 20);
                Texto(comando, "@Destino", f.Destino, 20);
                Texto(comando, "@Calculo", f.Calculo, 20);
                Texto(comando, "@MinutosDe", f.MinutosDe, 10);
                _ = await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                transaccion.Commit();
            }
        }

        internal const string SQL_LEER_TIEMPOS = @"
SELECT Id, ClaveIdempotencia, RTRIM(Repartidor), Dia, RTRIM(TipoParada), RTRIM(Resultado), EnvioId, Pedido, Traspaso, RTRIM(Empresa), RTRIM(Cliente),
    RTRIM(Contacto), RTRIM(CodigoPostal), Bultos, RTRIM(FormaCobro), ImporteCobro, TipoServicioTecnicoId, ServicioTecnico, MinutosServicioTecleados,
    FechaHoraIr, FechaHoraLlegada, FechaHoraFin, Latitud, Longitud, FechaHoraAnterior, LatitudAnterior, LongitudAnterior, MinutosPrevistos, FechaRegistro
FROM dbo." + TABLA_TIEMPOS + @" WITH (NOLOCK) WHERE Dia >= @desde ORDER BY Dia, Repartidor, FechaHoraFin, Id";

        public List<TiempoParadaReparto> LeerTiempos(DateTime desde)
        {
            var filas = new List<TiempoParadaReparto>();
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_LEER_TIEMPOS, conexion) { CommandTimeout = TIMEOUT_LECTURA_NOCHE })
            {
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde.Date;
                using (SqlDataReader l = comando.ExecuteReader())
                {
                    while (l.Read())
                    {
                        filas.Add(new TiempoParadaReparto
                        {
                            Id = Convert.ToInt64(l.GetValue(0)),
                            ClaveIdempotencia = l.GetGuid(1),
                            Repartidor = Cadena(l, 2),
                            Dia = l.GetDateTime(3),
                            TipoParada = Cadena(l, 4),
                            Resultado = Cadena(l, 5),
                            EnvioId = EnteroDe(l, 6),
                            Pedido = EnteroDe(l, 7),
                            Traspaso = EnteroDe(l, 8),
                            Empresa = Cadena(l, 9),
                            Cliente = Cadena(l, 10),
                            Contacto = Cadena(l, 11),
                            CodigoPostal = Cadena(l, 12),
                            Bultos = EnteroDe(l, 13),
                            FormaCobro = Cadena(l, 14),
                            ImporteCobro = l.IsDBNull(15) ? (decimal?)null : Convert.ToDecimal(l.GetValue(15)),
                            TipoServicioTecnicoId = EnteroDe(l, 16),
                            ServicioTecnico = Cadena(l, 17),
                            MinutosServicioTecleados = EnteroDe(l, 18),
                            FechaHoraIr = l.IsDBNull(19) ? (DateTime?)null : l.GetDateTime(19),
                            FechaHoraLlegada = l.IsDBNull(20) ? (DateTime?)null : l.GetDateTime(20),
                            FechaHoraFin = l.GetDateTime(21),
                            Latitud = l.IsDBNull(22) ? (decimal?)null : Convert.ToDecimal(l.GetValue(22)),
                            Longitud = l.IsDBNull(23) ? (decimal?)null : Convert.ToDecimal(l.GetValue(23)),
                            FechaHoraAnterior = l.IsDBNull(24) ? (DateTime?)null : l.GetDateTime(24),
                            LatitudAnterior = l.IsDBNull(25) ? (decimal?)null : Convert.ToDecimal(l.GetValue(25)),
                            LongitudAnterior = l.IsDBNull(26) ? (decimal?)null : Convert.ToDecimal(l.GetValue(26)),
                            MinutosPrevistos = l.IsDBNull(27) ? (decimal?)null : Convert.ToDecimal(l.GetValue(27)),
                            FechaRegistro = l.GetDateTime(28)
                        });
                    }
                }
            }
            return filas;
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static void Entero(SqlCommand c, string nombre, int? valor) => c.Parameters.Add(nombre, SqlDbType.Int).Value = (object)valor ?? DBNull.Value;

        private static void Fecha(SqlCommand c, string nombre, DateTime? valor) => c.Parameters.Add(nombre, SqlDbType.DateTime).Value = (object)valor ?? DBNull.Value;

        private static void Texto(SqlCommand c, string nombre, string valor, int longitud)
            => c.Parameters.Add(nombre, SqlDbType.VarChar, longitud).Value = (object)Recortar(valor, longitud) ?? DBNull.Value;

        private static void Decimal(SqlCommand c, string nombre, decimal? valor, byte precision, byte escala)
        {
            SqlParameter p = c.Parameters.Add(nombre, SqlDbType.Decimal);
            p.Precision = precision;
            p.Scale = escala;
            p.Value = valor.HasValue ? (object)Math.Round(valor.Value, escala) : DBNull.Value;
        }

        private static string Cadena(SqlDataReader l, int i) => l.IsDBNull(i) ? null : Convert.ToString(l.GetValue(i))?.Trim();

        private static int? EnteroDe(SqlDataReader l, int i) => l.IsDBNull(i) ? (int?)null : Convert.ToInt32(l.GetValue(i));

        private static string Recortar(string valor, int longitud)
        {
            string limpio = valor?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
