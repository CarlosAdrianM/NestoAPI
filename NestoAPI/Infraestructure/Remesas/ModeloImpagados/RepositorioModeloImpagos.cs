using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Remesas.ModeloImpagados
{
    /// <summary>NestoAPI#896: una fila de dbo.RemesasModeloEntrenamientos (métricas de VALIDACIÓN de modelo y regla).</summary>
    public class EntrenamientoModeloImpagos
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public int Ejemplos { get; set; }
        public int EjemplosEntrenamiento { get; set; }
        public int EjemplosValidacion { get; set; }
        public DateTime? ValidacionDesde { get; set; }
        public int? DevueltosValidacion { get; set; }
        public double? AucModelo { get; set; }
        public double? AucRegla { get; set; }
        public double? Captado5Modelo { get; set; }
        public double? Captado5Regla { get; set; }
        public double? Captado10Modelo { get; set; }
        public double? Captado10Regla { get; set; }
        public double? CalibracionAltosModelo { get; set; }
        public double? CalibracionAltosRegla { get; set; }
        /// <summary>JSON de los cuatro tramos (previsto frente a real) del modelo en validación.</summary>
        public string TramosModelo { get; set; }
        public string TramosRegla { get; set; }
        public double? PlattA { get; set; }
        public double? PlattB { get; set; }
        public string Version { get; set; }
        public string Algoritmo { get; set; }
        /// <summary>Su modelo es el que está en uso (solo una fila a la vez).</summary>
        public bool ModeloActivo { get; set; }
        public string Motivo { get; set; }
        public string Origen { get; set; }
        public int? Segundos { get; set; }

        internal MetricasImpagos MetricasModelo() => AucModelo.HasValue
            ? new MetricasImpagos { Devueltos = DevueltosValidacion ?? 0, Recibos = EjemplosValidacion, Auc = AucModelo, Captado5 = Captado5Modelo, Captado10 = Captado10Modelo, CalibracionAltos = CalibracionAltosModelo }
            : null;
    }

    /// <summary>Recibos girados e impagados leídos de ExtractoCliente (sin enlazar todavía).</summary>
    public class HistoricoImpagos
    {
        public List<ReciboGirado> Recibos { get; set; } = new List<ReciboGirado>();
        public List<ImpagadoLeido> Impagados { get; set; } = new List<ImpagadoLeido>();
    }

    /// <summary>Lo que se apunta de cada efecto de una remesa en dbo.RemesasPrediccionesImpago.</summary>
    public class FilaPrediccionImpago
    {
        public string Empresa { get; set; }
        public int Remesa { get; set; }
        public int NumOrden { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public decimal Importe { get; set; }
        public DateTime FechaGiro { get; set; }
        public DateTime? FechaVto { get; set; }
        public string VersionModelo { get; set; }
        public double? Probabilidad { get; set; }
        public double? ProbabilidadRegla { get; set; }
        /// <summary>JSON con los rasgos del recibo ese día.</summary>
        public string Rasgos { get; set; }
        public string MotivosRiesgo { get; set; }
    }

    public class CuentaSombraImpagos
    {
        public int Predicciones { get; set; }
        public int Pendientes { get; set; }
        public DateTime? PrimeraPrediccion { get; set; }
    }

    public interface IRepositorioModeloImpagos
    {
        bool TablaEntrenamientosExiste();
        bool TablaPrediccionesExiste();
        /// <summary>TODOS los recibos girados (facturados desde <paramref name="desde"/>) y los impagados desde ese día. SOLO de noche.</summary>
        HistoricoImpagos LeerHistorico(DateTime desde);
        /// <summary>Lo mismo, solo de esos clientes de esa empresa (para predecir una remesa).</summary>
        HistoricoImpagos LeerHistorialClientes(string empresa, IReadOnlyCollection<string> clientes, DateTime desde);
        /// <summary>
        /// El nivel general de los últimos días (para predecir de día sin leer el histórico): recibos girados por día de las remesas
        /// de los últimos 45 días e impagados por día del último mes.
        /// </summary>
        NivelGeneralImpagos LeerNivelGeneral(DateTime hoy);
        /// <summary>Los efectos (cartera) que lleva la remesa.</summary>
        List<ReciboGirado> LeerEfectosRemesa(string empresa, int remesa);
        /// <summary>
        /// Los efectos de cartera con esos Nº de orden (por tandas), aunque no estén girados: los candidatos a una remesa. Trae el nombre
        /// del cliente (<see cref="ReciboGirado.Nombre"/>). Solo lee.
        /// </summary>
        List<ReciboGirado> LeerEfectosCartera(string empresa, IReadOnlyCollection<int> numerosOrden);
        /// <summary>Inserta las que no estén ya (por empresa + remesa + Nº de orden). Devuelve cuántas ha insertado.</summary>
        int GuardarPredicciones(IReadOnlyCollection<FilaPrediccionImpago> filas);
        /// <summary>Si <see cref="EntrenamientoModeloImpagos.ModeloActivo"/>, deja las demás filas a 0 en la misma transacción.</summary>
        void RegistrarEntrenamiento(EntrenamientoModeloImpagos fila);
        EntrenamientoModeloImpagos UltimoEntrenamiento();
        EntrenamientoModeloImpagos UltimoActivo();
        /// <summary>Ids de predicciones sin resultado, por orden, a partir de <paramref name="despuesDe"/>.</summary>
        List<long> LeerPendientes(long despuesDe, int cuantas);
        /// <summary>
        /// Resuelve las pendientes de ese rango de Id: devuelta si ya hay impagado suyo en los 45 días siguientes al giro; no devuelta si
        /// ya han pasado. Devuelve cuántas ha resuelto.
        /// </summary>
        int Resolver(long desdeId, long hastaId, DateTime hoy);
        List<PrediccionResueltaImpago> LeerResueltas(DateTime desde);
        CuentaSombraImpagos LeerCuentaSombra();
    }

    /// <summary>NestoAPI#896: <see cref="IRepositorioModeloImpagos"/> con ADO.NET (las dos tablas van fuera del EDMX).</summary>
    public class RepositorioModeloImpagos : IRepositorioModeloImpagos
    {
        internal const string NOMBRE_APLICACION = "NestoAPI-ModeloImpagos";
        private const int TIMEOUT_LECTURA = 60;
        /// <summary>La lectura del histórico es de noche: margen holgado.</summary>
        private const int TIMEOUT_HISTORICO = 600;
        private const int CLIENTES_POR_TANDA = 500;

        internal const string SQL_EXISTE_ENTRENAMIENTOS = "SELECT CASE WHEN OBJECT_ID('dbo.RemesasModeloEntrenamientos', 'U') IS NULL THEN 0 ELSE 1 END";
        internal const string SQL_EXISTE_PREDICCIONES = "SELECT CASE WHEN OBJECT_ID('dbo.RemesasPrediccionesImpago', 'U') IS NULL THEN 0 ELSE 1 END";

        /// <summary>
        /// Recibo girado = cartera (TipoApunte 2) con Remesa. El día del giro sale de la remesa (<see cref="ReglaFechaGiro"/>): la Fecha
        /// del apunte es la de la factura. La entidad, de la ficha bancaria del efecto.
        /// </summary>
        internal const string SQL_RECIBOS = SQL_COLUMNAS_RECIBO + SQL_ORIGEN_RECIBO + @"
WHERE e.Remesa IS NOT NULL AND e.TipoApunte = '2' AND e.Importe > 0";

        /// <summary>Las columnas de un recibo (las que lee <see cref="LeerRecibos"/>, en este orden).</summary>
        private const string SQL_COLUMNAS_RECIBO = @"
SELECT RTRIM(e.Empresa), e.[Nº Orden], RTRIM(e.[Número]), RTRIM(ISNULL(e.Contacto, '')), CAST(CAST(e.Fecha AS date) AS datetime),
       CAST(CAST(e.FechaVto AS date) AS datetime), CAST(CAST(r.Fecha AS date) AS datetime), RTRIM(e.[Nº Documento]), RTRIM(ISNULL(e.Efecto, '')),
       e.Importe, e.Remesa, RTRIM(ISNULL(e.Vendedor, '')), RTRIM(ISNULL(e.FormaVenta, '')), RTRIM(ISNULL(e.CCC, '')), RTRIM(ISNULL(c.Entidad, ''))";

        private const string SQL_ORIGEN_RECIBO = @"
FROM ExtractoCliente e WITH (NOLOCK)
LEFT JOIN Remesas r WITH (NOLOCK) ON r.Empresa = e.Empresa AND r.[Número] = e.Remesa
LEFT JOIN CCC c WITH (NOLOCK) ON c.Empresa = e.Empresa AND c.Cliente = e.[Número] AND c.Contacto = e.Contacto AND c.[Número] = e.CCC";

        /// <summary>
        /// 27/11/26: los efectos de cartera por Nº de orden (los candidatos a remesa, aún sin girar), con las mismas columnas que un recibo
        /// girado y el nombre del cliente detrás. Sin filtro de remesa: todavía no la tienen.
        /// </summary>
        internal const string SQL_EFECTOS_CARTERA = SQL_COLUMNAS_RECIBO + @", RTRIM(ISNULL(cl.Nombre, ''))" + SQL_ORIGEN_RECIBO + @"
LEFT JOIN Clientes cl WITH (NOLOCK) ON cl.Empresa = e.Empresa AND cl.[Nº Cliente] = e.[Número] AND cl.Contacto = e.Contacto
WHERE e.TipoApunte = '2' AND e.Empresa = @empresa";

        private const int EFECTOS_POR_TANDA = 500;

        /// <summary>Para entrenar y para la historia: solo los recibos bancarios (FormaPago RCB), como en el análisis.</summary>
        internal const string SQL_FILTRO_HISTORICO = " AND e.FormaPago = 'RCB' AND e.Fecha >= @desde";

        /// <summary>Impagado = TipoApunte 4 cuyo Concepto es el motivo (el apunte de «Gastos Impagado…» no cuenta).</summary>
        internal const string SQL_IMPAGADOS = @"
SELECT RTRIM(e.Empresa), RTRIM(e.[Número]), CAST(CAST(e.Fecha AS date) AS datetime), RTRIM(e.[Nº Documento]), RTRIM(ISNULL(e.Efecto, '')), RTRIM(ISNULL(e.Concepto, ''))
FROM ExtractoCliente e WITH (NOLOCK)
WHERE e.TipoApunte = '4' AND e.Importe > 0 AND ISNULL(e.Concepto, '') NOT LIKE 'Gastos Impagado%' AND e.Fecha >= @desde";

        /// <summary>Recibos girados por día de giro, entrando por las remesas recientes (no recorre el extracto).</summary>
        internal const string SQL_RECIBOS_POR_DIA = @"
SELECT CAST(CAST(r.Fecha AS date) AS datetime), COUNT(*)
FROM Remesas r WITH (NOLOCK)
INNER JOIN ExtractoCliente e WITH (NOLOCK) ON e.Empresa = r.Empresa AND e.Remesa = r.[Número]
WHERE r.Fecha >= @desde AND e.TipoApunte = '2' AND e.FormaPago = 'RCB' AND e.Importe > 0
GROUP BY CAST(r.Fecha AS date)";

        internal const string SQL_IMPAGADOS_POR_DIA = @"
SELECT CAST(CAST(e.Fecha AS date) AS datetime), COUNT(*)
FROM ExtractoCliente e WITH (NOLOCK)
WHERE e.TipoApunte = '4' AND e.Importe > 0 AND ISNULL(e.Concepto, '') NOT LIKE 'Gastos Impagado%' AND e.Fecha >= @desde
GROUP BY CAST(e.Fecha AS date)";

        internal const string SQL_INSERTAR_PREDICCION = @"
IF NOT EXISTS (SELECT 1 FROM dbo.RemesasPrediccionesImpago WHERE Empresa = @empresa AND Remesa = @remesa AND NumOrden = @orden)
    INSERT INTO dbo.RemesasPrediccionesImpago (Empresa, Remesa, NumOrden, Cliente, Contacto, Documento, Efecto, Importe, FechaGiro, FechaVto,
        VersionModelo, Probabilidad, ProbabilidadRegla, Rasgos, MotivosRiesgo)
    VALUES (@empresa, @remesa, @orden, @cliente, @contacto, @documento, @efecto, @importe, @giro, @vto, @version, @probabilidad, @regla, @rasgos, @motivos);";

        internal const string SQL_INSERTAR_ENTRENAMIENTO = @"
IF @activo = 1
    UPDATE dbo.RemesasModeloEntrenamientos SET ModeloActivo = 0 WHERE ModeloActivo = 1;
INSERT INTO dbo.RemesasModeloEntrenamientos (Fecha, Ejemplos, EjemplosEntrenamiento, EjemplosValidacion, ValidacionDesde, DevueltosValidacion,
    AucModelo, AucRegla, Captado5Modelo, Captado5Regla, Captado10Modelo, Captado10Regla, CalibracionAltosModelo, CalibracionAltosRegla,
    TramosModelo, TramosRegla, PlattA, PlattB, Version, Algoritmo, ModeloActivo, Motivo, Origen, Segundos)
VALUES (@fecha, @ejemplos, @entrenamiento, @validacion, @validacionDesde, @devueltos, @aucModelo, @aucRegla, @c5Modelo, @c5Regla, @c10Modelo, @c10Regla,
    @calModelo, @calRegla, @tramosModelo, @tramosRegla, @plattA, @plattB, @version, @algoritmo, @activo, @motivo, @origen, @segundos);";

        internal const string SQL_COLUMNAS = @"Id, Fecha, Ejemplos, EjemplosEntrenamiento, EjemplosValidacion, CAST(ValidacionDesde AS datetime), DevueltosValidacion,
    AucModelo, AucRegla, Captado5Modelo, Captado5Regla, Captado10Modelo, Captado10Regla, CalibracionAltosModelo, CalibracionAltosRegla,
    TramosModelo, TramosRegla, PlattA, PlattB, Version, Algoritmo, ModeloActivo, Motivo, Origen, Segundos";

        internal const string SQL_ULTIMO = "SELECT TOP 1 " + SQL_COLUMNAS + " FROM dbo.RemesasModeloEntrenamientos WITH (NOLOCK) ORDER BY Id DESC";
        internal const string SQL_ULTIMO_ACTIVO = "SELECT TOP 1 " + SQL_COLUMNAS + " FROM dbo.RemesasModeloEntrenamientos WITH (NOLOCK) WHERE ModeloActivo = 1 ORDER BY Id DESC";

        internal const string SQL_PENDIENTES = "SELECT TOP (@cuantas) Id FROM dbo.RemesasPrediccionesImpago WITH (NOLOCK) WHERE Devuelto IS NULL AND Id > @despues ORDER BY Id";

        /// <summary>
        /// Primero las devueltas (el primer impagado del mismo cliente, documento y efecto entre el día del giro y 45 días después) y
        /// después, de las que quedan, las que ya han cumplido los 45 días: no devueltas. Idempotente (solo toca las pendientes).
        /// </summary>
        internal const string SQL_RESOLVER = @"
UPDATE p SET Devuelto = 1, FechaDevolucion = i.Fecha, MotivoDevolucion = i.Concepto, ResueltoEl = GETDATE()
FROM dbo.RemesasPrediccionesImpago p
CROSS APPLY (SELECT TOP 1 CAST(e.Fecha AS date) AS Fecha, LEFT(RTRIM(ISNULL(e.Concepto, '')), 60) AS Concepto
             FROM ExtractoCliente e WITH (NOLOCK)
             WHERE e.Empresa = p.Empresa AND e.[Número] = p.Cliente AND e.TipoApunte = '4' AND e.[Nº Documento] = p.Documento
               AND ISNULL(e.Efecto, '') = ISNULL(p.Efecto, '') AND e.Importe > 0 AND ISNULL(e.Concepto, '') NOT LIKE 'Gastos Impagado%'
               AND e.Fecha >= CAST(p.FechaGiro AS datetime) AND e.Fecha < DATEADD(day, @dias + 1, CAST(p.FechaGiro AS datetime))
             ORDER BY e.Fecha, e.[Nº Orden]) i
WHERE p.Devuelto IS NULL AND p.Id BETWEEN @desde AND @hasta;
UPDATE dbo.RemesasPrediccionesImpago SET Devuelto = 0, ResueltoEl = GETDATE()
WHERE Devuelto IS NULL AND Id BETWEEN @desde AND @hasta AND CAST(FechaGiro AS datetime) < DATEADD(day, -@dias, @hoy);";

        internal const string SQL_RESUELTAS = @"
SELECT CAST(FechaGiro AS datetime), Probabilidad, ProbabilidadRegla, Devuelto
FROM dbo.RemesasPrediccionesImpago WITH (NOLOCK) WHERE Devuelto IS NOT NULL AND FechaGiro >= @desde";

        internal const string SQL_CUENTA = @"
SELECT COUNT(*), SUM(CASE WHEN Devuelto IS NULL THEN 1 ELSE 0 END), MIN(FechaPrediccion) FROM dbo.RemesasPrediccionesImpago WITH (NOLOCK)";

        private readonly string cadenaConexion;

        public RepositorioModeloImpagos(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = RepositorioSombraModoServicioSql.SinAlistar(cadenaConexion);
        }

        public static RepositorioModeloImpagos DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioModeloImpagos(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        public bool TablaEntrenamientosExiste() => Existe(SQL_EXISTE_ENTRENAMIENTOS);

        public bool TablaPrediccionesExiste() => Existe(SQL_EXISTE_PREDICCIONES);

        public HistoricoImpagos LeerHistorico(DateTime desde)
        {
            var historico = new HistoricoImpagos();
            using (SqlConnection conexion = Abrir())
            {
                using (var comando = new SqlCommand(SQL_RECIBOS + SQL_FILTRO_HISTORICO, conexion) { CommandTimeout = TIMEOUT_HISTORICO })
                {
                    comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde.Date;
                    LeerRecibos(comando, historico.Recibos);
                }
                using (var comando = new SqlCommand(SQL_IMPAGADOS, conexion) { CommandTimeout = TIMEOUT_HISTORICO })
                {
                    comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde.Date;
                    LeerImpagados(comando, historico.Impagados);
                }
            }
            historico.Recibos = SinRepetir(historico.Recibos);
            return historico;
        }

        public HistoricoImpagos LeerHistorialClientes(string empresa, IReadOnlyCollection<string> clientes, DateTime desde)
        {
            var historico = new HistoricoImpagos();
            List<string> lista = (clientes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (lista.Count == 0)
            {
                return historico;
            }
            using (SqlConnection conexion = Abrir())
            {
                for (int inicio = 0; inicio < lista.Count; inicio += CLIENTES_POR_TANDA)
                {
                    List<string> tanda = lista.Skip(inicio).Take(CLIENTES_POR_TANDA).ToList();
                    string filtro = " AND e.Empresa = @empresa AND e.[Número] IN (" + string.Join(", ", tanda.Select((c, i) => "@c" + i)) + ")";
                    using (var comando = new SqlCommand(SQL_RECIBOS + SQL_FILTRO_HISTORICO + filtro, conexion) { CommandTimeout = TIMEOUT_LECTURA })
                    {
                        ParametrosClientes(comando, empresa, tanda, desde);
                        LeerRecibos(comando, historico.Recibos);
                    }
                    using (var comando = new SqlCommand(SQL_IMPAGADOS + filtro, conexion) { CommandTimeout = TIMEOUT_LECTURA })
                    {
                        ParametrosClientes(comando, empresa, tanda, desde);
                        LeerImpagados(comando, historico.Impagados);
                    }
                }
            }
            historico.Recibos = SinRepetir(historico.Recibos);
            return historico;
        }

        public NivelGeneralImpagos LeerNivelGeneral(DateTime hoy)
        {
            using (SqlConnection conexion = Abrir())
            {
                return new NivelGeneralImpagos(
                    LeerPorDia(conexion, SQL_RECIBOS_POR_DIA, hoy.Date.AddDays(-45)),
                    LeerPorDia(conexion, SQL_IMPAGADOS_POR_DIA, hoy.Date.AddDays(-31)));
            }
        }

        private static Dictionary<DateTime, int> LeerPorDia(SqlConnection conexion, string sql, DateTime desde)
        {
            var porDia = new Dictionary<DateTime, int>();
            using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_LECTURA })
            {
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        if (!lector.IsDBNull(0))
                        {
                            porDia[lector.GetDateTime(0).Date] = Convert.ToInt32(lector.GetValue(1));
                        }
                    }
                }
            }
            return porDia;
        }

        public List<ReciboGirado> LeerEfectosRemesa(string empresa, int remesa)
        {
            var recibos = new List<ReciboGirado>();
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_RECIBOS + " AND e.Empresa = @empresa AND e.Remesa = @remesa", conexion) { CommandTimeout = TIMEOUT_LECTURA })
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = (empresa ?? string.Empty).Trim();
                comando.Parameters.Add("@remesa", SqlDbType.Int).Value = remesa;
                LeerRecibos(comando, recibos);
            }
            return SinRepetir(recibos);
        }

        public List<ReciboGirado> LeerEfectosCartera(string empresa, IReadOnlyCollection<int> numerosOrden)
        {
            var recibos = new List<ReciboGirado>();
            List<int> lista = (numerosOrden ?? new List<int>()).Distinct().OrderBy(n => n).ToList();
            if (lista.Count == 0)
            {
                return recibos;
            }
            using (SqlConnection conexion = Abrir())
            {
                for (int inicio = 0; inicio < lista.Count; inicio += EFECTOS_POR_TANDA)
                {
                    List<int> tanda = lista.Skip(inicio).Take(EFECTOS_POR_TANDA).ToList();
                    string filtro = " AND e.[Nº Orden] IN (" + string.Join(", ", tanda.Select((n, i) => "@o" + i)) + ")";
                    using (var comando = new SqlCommand(SQL_EFECTOS_CARTERA + filtro, conexion) { CommandTimeout = TIMEOUT_LECTURA })
                    {
                        comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = (empresa ?? string.Empty).Trim();
                        for (int i = 0; i < tanda.Count; i++)
                        {
                            comando.Parameters.Add("@o" + i, SqlDbType.Int).Value = tanda[i];
                        }
                        LeerRecibos(comando, recibos);
                    }
                }
            }
            return SinRepetir(recibos);
        }

        public int GuardarPredicciones(IReadOnlyCollection<FilaPrediccionImpago> filas)
        {
            if (filas == null || filas.Count == 0)
            {
                return 0;
            }
            int insertadas = 0;
            using (SqlConnection conexion = Abrir())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            {
                foreach (FilaPrediccionImpago f in filas)
                {
                    using (var comando = new SqlCommand(SQL_INSERTAR_PREDICCION, conexion, transaccion) { CommandTimeout = TIMEOUT_LECTURA })
                    {
                        comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = (f.Empresa ?? string.Empty).Trim();
                        comando.Parameters.Add("@remesa", SqlDbType.Int).Value = f.Remesa;
                        comando.Parameters.Add("@orden", SqlDbType.Int).Value = f.NumOrden;
                        comando.Parameters.Add("@cliente", SqlDbType.Char, 10).Value = Recortar(f.Cliente, 10) ?? string.Empty;
                        comando.Parameters.Add("@contacto", SqlDbType.Char, 3).Value = (object)Recortar(f.Contacto, 3) ?? DBNull.Value;
                        comando.Parameters.Add("@documento", SqlDbType.Char, 10).Value = Recortar(f.Documento, 10) ?? string.Empty;
                        comando.Parameters.Add("@efecto", SqlDbType.Char, 3).Value = (object)Recortar(f.Efecto, 3) ?? DBNull.Value;
                        comando.Parameters.Add("@importe", SqlDbType.Money).Value = f.Importe;
                        comando.Parameters.Add("@giro", SqlDbType.Date).Value = f.FechaGiro.Date;
                        comando.Parameters.Add("@vto", SqlDbType.Date).Value = (object)f.FechaVto?.Date ?? DBNull.Value;
                        comando.Parameters.Add("@version", SqlDbType.VarChar, 40).Value = (object)Recortar(f.VersionModelo, 40) ?? DBNull.Value;
                        comando.Parameters.Add("@probabilidad", SqlDbType.Float).Value = (object)f.Probabilidad ?? DBNull.Value;
                        comando.Parameters.Add("@regla", SqlDbType.Float).Value = (object)f.ProbabilidadRegla ?? DBNull.Value;
                        comando.Parameters.Add("@rasgos", SqlDbType.NVarChar, -1).Value = (object)f.Rasgos ?? DBNull.Value;
                        comando.Parameters.Add("@motivos", SqlDbType.NVarChar, 500).Value = (object)Recortar(f.MotivosRiesgo, 500) ?? DBNull.Value;
                        int filasAfectadas = comando.ExecuteNonQuery();
                        if (filasAfectadas > 0)
                        {
                            insertadas++;
                        }
                    }
                }
                transaccion.Commit();
            }
            return insertadas;
        }

        public void RegistrarEntrenamiento(EntrenamientoModeloImpagos f)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            using (var comando = new SqlCommand(SQL_INSERTAR_ENTRENAMIENTO, conexion, transaccion) { CommandTimeout = TIMEOUT_LECTURA })
            {
                comando.Parameters.Add("@fecha", SqlDbType.DateTime).Value = f.Fecha;
                comando.Parameters.Add("@ejemplos", SqlDbType.Int).Value = f.Ejemplos;
                comando.Parameters.Add("@entrenamiento", SqlDbType.Int).Value = f.EjemplosEntrenamiento;
                comando.Parameters.Add("@validacion", SqlDbType.Int).Value = f.EjemplosValidacion;
                comando.Parameters.Add("@validacionDesde", SqlDbType.Date).Value = (object)f.ValidacionDesde?.Date ?? DBNull.Value;
                comando.Parameters.Add("@devueltos", SqlDbType.Int).Value = (object)f.DevueltosValidacion ?? DBNull.Value;
                Doble(comando, "@aucModelo", f.AucModelo);
                Doble(comando, "@aucRegla", f.AucRegla);
                Doble(comando, "@c5Modelo", f.Captado5Modelo);
                Doble(comando, "@c5Regla", f.Captado5Regla);
                Doble(comando, "@c10Modelo", f.Captado10Modelo);
                Doble(comando, "@c10Regla", f.Captado10Regla);
                Doble(comando, "@calModelo", f.CalibracionAltosModelo);
                Doble(comando, "@calRegla", f.CalibracionAltosRegla);
                comando.Parameters.Add("@tramosModelo", SqlDbType.NVarChar, -1).Value = (object)f.TramosModelo ?? DBNull.Value;
                comando.Parameters.Add("@tramosRegla", SqlDbType.NVarChar, -1).Value = (object)f.TramosRegla ?? DBNull.Value;
                Doble(comando, "@plattA", f.PlattA);
                Doble(comando, "@plattB", f.PlattB);
                comando.Parameters.Add("@version", SqlDbType.VarChar, 20).Value = (object)Recortar(f.Version, 20) ?? DBNull.Value;
                comando.Parameters.Add("@algoritmo", SqlDbType.VarChar, 100).Value = (object)Recortar(f.Algoritmo, 100) ?? DBNull.Value;
                comando.Parameters.Add("@activo", SqlDbType.Bit).Value = f.ModeloActivo;
                comando.Parameters.Add("@motivo", SqlDbType.NVarChar, 1000).Value = (object)Recortar(f.Motivo, 1000) ?? DBNull.Value;
                comando.Parameters.Add("@origen", SqlDbType.NVarChar, 100).Value = (object)Recortar(f.Origen, 100) ?? DBNull.Value;
                comando.Parameters.Add("@segundos", SqlDbType.Int).Value = (object)f.Segundos ?? DBNull.Value;
                _ = comando.ExecuteNonQuery();
                transaccion.Commit();
            }
        }

        public EntrenamientoModeloImpagos UltimoEntrenamiento() => LeerUno(SQL_ULTIMO);

        public EntrenamientoModeloImpagos UltimoActivo() => LeerUno(SQL_ULTIMO_ACTIVO);

        public List<long> LeerPendientes(long despuesDe, int cuantas)
        {
            var ids = new List<long>();
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_PENDIENTES, conexion) { CommandTimeout = TIMEOUT_LECTURA })
            {
                comando.Parameters.Add("@cuantas", SqlDbType.Int).Value = cuantas;
                comando.Parameters.Add("@despues", SqlDbType.BigInt).Value = despuesDe;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        ids.Add(Convert.ToInt64(lector.GetValue(0)));
                    }
                }
            }
            return ids;
        }

        public int Resolver(long desdeId, long hastaId, DateTime hoy)
        {
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_RESOLVER, conexion) { CommandTimeout = TIMEOUT_HISTORICO })
            {
                comando.Parameters.Add("@desde", SqlDbType.BigInt).Value = desdeId;
                comando.Parameters.Add("@hasta", SqlDbType.BigInt).Value = hastaId;
                comando.Parameters.Add("@dias", SqlDbType.Int).Value = ConstructorRasgosImpagos.DIAS_ETIQUETA;
                comando.Parameters.Add("@hoy", SqlDbType.DateTime).Value = hoy.Date;
                return Math.Max(0, comando.ExecuteNonQuery());
            }
        }

        public List<PrediccionResueltaImpago> LeerResueltas(DateTime desde)
        {
            var filas = new List<PrediccionResueltaImpago>();
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_RESUELTAS, conexion) { CommandTimeout = TIMEOUT_LECTURA })
            {
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde.Date;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        filas.Add(new PrediccionResueltaImpago
                        {
                            FechaGiro = lector.GetDateTime(0),
                            Probabilidad = DobleDe(lector, 1),
                            ProbabilidadRegla = DobleDe(lector, 2),
                            Devuelto = lector.GetBoolean(3)
                        });
                    }
                }
            }
            return filas;
        }

        public CuentaSombraImpagos LeerCuentaSombra()
        {
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_CUENTA, conexion) { CommandTimeout = TIMEOUT_LECTURA })
            using (SqlDataReader lector = comando.ExecuteReader())
            {
                if (!lector.Read())
                {
                    return new CuentaSombraImpagos();
                }
                return new CuentaSombraImpagos
                {
                    Predicciones = lector.IsDBNull(0) ? 0 : Convert.ToInt32(lector.GetValue(0)),
                    Pendientes = lector.IsDBNull(1) ? 0 : Convert.ToInt32(lector.GetValue(1)),
                    PrimeraPrediccion = lector.IsDBNull(2) ? (DateTime?)null : lector.GetDateTime(2)
                };
            }
        }

        private static void LeerRecibos(SqlCommand comando, List<ReciboGirado> destino)
        {
            using (SqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    DateTime factura = lector.GetDateTime(4);
                    DateTime? vto = lector.IsDBNull(5) ? (DateTime?)null : lector.GetDateTime(5);
                    DateTime? remesa = lector.IsDBNull(6) ? (DateTime?)null : lector.GetDateTime(6);
                    destino.Add(new ReciboGirado
                    {
                        Empresa = Cadena(lector, 0),
                        NumOrden = Convert.ToInt32(lector.GetValue(1)),
                        Cliente = Cadena(lector, 2),
                        Contacto = Cadena(lector, 3),
                        FechaFactura = factura,
                        FechaVto = vto,
                        FechaGiro = ReglaFechaGiro.Calcular(factura, vto, remesa),
                        Documento = Cadena(lector, 7),
                        Efecto = Cadena(lector, 8),
                        Importe = lector.IsDBNull(9) ? 0 : Convert.ToDouble(lector.GetValue(9)),
                        Remesa = lector.IsDBNull(10) ? (int?)null : Convert.ToInt32(lector.GetValue(10)),
                        Vendedor = Cadena(lector, 11),
                        FormaVenta = Cadena(lector, 12),
                        Ccc = Cadena(lector, 13),
                        Entidad = Cadena(lector, 14),
                        Nombre = lector.FieldCount > 15 ? Cadena(lector, 15) : null
                    });
                }
            }
        }

        private static void LeerImpagados(SqlCommand comando, List<ImpagadoLeido> destino)
        {
            using (SqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    destino.Add(new ImpagadoLeido
                    {
                        Empresa = Cadena(lector, 0),
                        Cliente = Cadena(lector, 1),
                        Fecha = lector.GetDateTime(2),
                        Documento = Cadena(lector, 3),
                        Efecto = Cadena(lector, 4),
                        Concepto = Cadena(lector, 5)
                    });
                }
            }
        }

        /// <summary>Si la remesa tuviera más de una fila, el recibo saldría repetido: uno por empresa y Nº de orden.</summary>
        private static List<ReciboGirado> SinRepetir(List<ReciboGirado> recibos)
            => recibos.GroupBy(r => r.Empresa + "|" + r.NumOrden, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        private static void ParametrosClientes(SqlCommand comando, string empresa, List<string> clientes, DateTime desde)
        {
            comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde.Date;
            comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = (empresa ?? string.Empty).Trim();
            for (int i = 0; i < clientes.Count; i++)
            {
                comando.Parameters.Add("@c" + i, SqlDbType.Char, 10).Value = clientes[i];
            }
        }

        private EntrenamientoModeloImpagos LeerUno(string sql)
        {
            if (!TablaEntrenamientosExiste())
            {
                return null;
            }
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_LECTURA })
            using (SqlDataReader lector = comando.ExecuteReader())
            {
                if (!lector.Read())
                {
                    return null;
                }
                return new EntrenamientoModeloImpagos
                {
                    Id = lector.GetInt32(0),
                    Fecha = lector.GetDateTime(1),
                    Ejemplos = lector.GetInt32(2),
                    EjemplosEntrenamiento = lector.GetInt32(3),
                    EjemplosValidacion = lector.GetInt32(4),
                    ValidacionDesde = lector.IsDBNull(5) ? (DateTime?)null : lector.GetDateTime(5),
                    DevueltosValidacion = lector.IsDBNull(6) ? (int?)null : lector.GetInt32(6),
                    AucModelo = DobleDe(lector, 7),
                    AucRegla = DobleDe(lector, 8),
                    Captado5Modelo = DobleDe(lector, 9),
                    Captado5Regla = DobleDe(lector, 10),
                    Captado10Modelo = DobleDe(lector, 11),
                    Captado10Regla = DobleDe(lector, 12),
                    CalibracionAltosModelo = DobleDe(lector, 13),
                    CalibracionAltosRegla = DobleDe(lector, 14),
                    TramosModelo = Cadena(lector, 15),
                    TramosRegla = Cadena(lector, 16),
                    PlattA = DobleDe(lector, 17),
                    PlattB = DobleDe(lector, 18),
                    Version = Cadena(lector, 19),
                    Algoritmo = Cadena(lector, 20),
                    ModeloActivo = lector.GetBoolean(21),
                    Motivo = Cadena(lector, 22),
                    Origen = Cadena(lector, 23),
                    Segundos = lector.IsDBNull(24) ? (int?)null : lector.GetInt32(24)
                };
            }
        }

        private bool Existe(string sql)
        {
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_LECTURA })
            {
                return Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static void Doble(SqlCommand comando, string nombre, double? valor)
            => comando.Parameters.Add(nombre, SqlDbType.Float).Value = valor.HasValue && !double.IsNaN(valor.Value) && !double.IsInfinity(valor.Value) ? (object)valor.Value : DBNull.Value;

        private static string Cadena(SqlDataReader lector, int i) => lector.IsDBNull(i) ? null : Convert.ToString(lector.GetValue(i))?.Trim();

        private static double? DobleDe(SqlDataReader lector, int i) => lector.IsDBNull(i) ? (double?)null : Convert.ToDouble(lector.GetValue(i));

        private static string Recortar(string valor, int longitud)
        {
            string limpio = valor?.Trim();
            return limpio == null || limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
