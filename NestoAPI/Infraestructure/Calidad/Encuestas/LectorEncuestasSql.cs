using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Encuestas
{
    /// <summary>Las candidatas de un rango de días y lo que no se ha podido leer.</summary>
    public class LecturaCandidatasEncuesta
    {
        public List<CandidataEncuesta> Candidatas { get; set; } = new List<CandidataEncuesta>();
        /// <summary>Fuentes sin leer porque falta su tabla («ProblemaReenvioEntregado (falta dbo.EnviosAgenciaReenvios)»).</summary>
        public List<string> FuentesSinLeer { get; set; } = new List<string>();
    }

    /// <summary>Lo que hace falta saber de los clientes candidatos para el canal y la situación (baja, impago).</summary>
    public class ContextoClientesEncuesta
    {
        /// <summary>Clientes con la app de clientes y avisos aceptados (FuentePushCampanaSql.FiltroDestinatarios).</summary>
        public HashSet<string> ClientesConApp { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>«cliente|contacto» → correo (el primero válido de sus personas de contacto activas).</summary>
        public Dictionary<string, string> CorreoPorContacto { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>cliente → un correo válido de cualquiera de sus contactos (respaldo).</summary>
        public Dictionary<string, string> CorreoPorCliente { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Las bajas de CorreosPostCompraBajas (null si no se han podido leer).</summary>
        public BajasCorreosPostCompra Bajas { get; set; } = new BajasCorreosPostCompra();
        /// <summary>cliente → fecha del impagado más antiguo que sigue sin liquidar.</summary>
        public Dictionary<string, DateTime> ImpagoPendienteDesde { get; set; } = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        public List<string> Avisos { get; set; } = new List<string>();
        /// <summary>Calculado una vez por <see cref="ReglaCanalEncuesta.ClientesDeBaja"/> (null = sin calcular).</summary>
        public HashSet<string> ClientesDeBaja { get; set; }
    }

    /// <summary>Lo que el job ha ido guardando.</summary>
    public class LecturaSombraEncuestas
    {
        public List<CandidataEncuesta> Encuestas { get; set; } = new List<CandidataEncuesta>();
        public List<KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>> Descartes { get; set; } =
            new List<KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>>();
        /// <summary>Días procesados con su número de candidatas.</summary>
        public Dictionary<DateTime, int> CandidatasPorDia { get; set; } = new Dictionary<DateTime, int>();
        /// <summary>El intervalo con el que se decidió el último día.</summary>
        public int? UltimoIntervalo { get; set; }
    }

    public interface ILectorEncuestas
    {
        /// <summary>dbo.CalidadEncuestas, dbo.CalidadEncuestasDescartes y dbo.CalidadEncuestasDias (Scripts/Issue914e_CalidadEncuestas.sql).</summary>
        bool ExistenTablas();
        /// <summary>Candidatas con DÍA de candidatura en [<paramref name="desde"/>, <paramref name="hasta"/>).</summary>
        LecturaCandidatasEncuesta LeerCandidatas(string empresa, DateTime desde, DateTime hasta, ParametrosFuentesEncuesta parametros);
        ContextoClientesEncuesta LeerContexto(string empresa, IReadOnlyCollection<string> clientes);
        /// <summary>Las encuestas decididas (sombra o no) con día desde <paramref name="desde"/>; <paramref name="hoy"/> para saber si se ignoraron.</summary>
        List<EncuestaHistorial> LeerHistorial(string empresa, DateTime desde, DateTime hoy);
        bool DiaProcesado(string empresa, DateTime dia);
        void GuardarDia(string empresa, DateTime dia, int candidatas, int intervaloDias, IReadOnlyList<DecisionEncuestaCliente> decisiones);
        LecturaSombraEncuestas LeerSombra(string empresa, DateTime desde, DateTime hasta);
    }

    /// <summary>
    /// NestoAPI#914 (encuestas, E1): SOLO LECTURA de las fuentes (READ UNCOMMITTED + NOLOCK, una consulta por fuente para todo el rango, sin
    /// N+1) y la escritura de lo decidido en las tablas de Scripts/Issue914e_CalidadEncuestas.sql (fuera del EDMX). Conexión propia sin alistar.
    /// </summary>
    public class LectorEncuestasSql : ILectorEncuestas
    {
        internal const string NOMBRE_APLICACION = "NestoAPI (encuestas #914)";
        internal const int TIMEOUT_SEGUNDOS = 180;
        internal const int LOTE_CLIENTES = 500;

        internal const string SQL_TABLAS_ENCUESTAS = @"
SELECT CAST(CASE WHEN OBJECT_ID('dbo.CalidadEncuestas', 'U') IS NULL OR OBJECT_ID('dbo.CalidadEncuestasDescartes', 'U') IS NULL
                   OR OBJECT_ID('dbo.CalidadEncuestasDias', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_EXISTE_TABLA = "SELECT CAST(CASE WHEN OBJECT_ID(@tabla, 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        /// <summary>Correos de las personas de contacto activas de un lote de clientes. {0} = la lista de parámetros.</summary>
        internal const string SQL_CORREOS = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT RTRIM(p.[NºCliente]) AS Cliente, RTRIM(p.Contacto) AS Contacto, LTRIM(RTRIM(p.[CorreoElectrónico])) AS Correo
FROM PersonasContactoCliente p WITH (NOLOCK)
WHERE p.Empresa = @empresa AND p.[NºCliente] IN ({0}) AND p.Estado >= 0 AND p.[CorreoElectrónico] LIKE '%@%'
ORDER BY p.[NºCliente], p.Contacto, p.[Número];";

        /// <summary>
        /// Impago EN GESTIÓN = un impagado sin liquidar: ExtractoCliente TipoApunte 4 (como ModeloImpagados: Importe &gt; 0 y el apunte de
        /// «Gastos Impagado…» no cuenta) que sigue con ImportePdte ≠ 0. Con la fecha del más antiguo. Pocos clientes: una consulta.
        /// </summary>
        internal const string SQL_IMPAGOS = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT RTRIM(e.[Número]) AS Cliente, MIN(e.Fecha) AS Desde
FROM ExtractoCliente e WITH (NOLOCK)
WHERE e.Empresa = @empresa AND e.TipoApunte = '4' AND e.Importe > 0 AND e.ImportePdte <> 0 AND ISNULL(e.Concepto, '') NOT LIKE 'Gastos Impagado%'
GROUP BY e.[Número];";

        internal const string SQL_HISTORIAL = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT RTRIM(Cliente) AS Cliente, CAST(Fecha AS datetime) AS Dia, RTRIM(Tema) AS Tema, RTRIM(ClaveTema) AS ClaveTema, EnviadaEl, RespondidaEl
FROM dbo.CalidadEncuestas WITH (NOLOCK)
WHERE Empresa = @empresa AND Fecha >= @desde;";

        internal const string SQL_DIA_PROCESADO = "SELECT COUNT(*) FROM dbo.CalidadEncuestasDias WITH (NOLOCK) WHERE Empresa = @empresa AND Fecha = @dia";

        internal const string SQL_INSERTAR_ENCUESTA = @"
INSERT INTO dbo.CalidadEncuestas (Empresa, Fecha, Cliente, Contacto, Tema, ClaveTema, Origen, Clase, Motivo, FechaEvento, Pedido, Factura, Envio,
    EventoId, Producto, NombreProducto, Vendedor, NombreVendedor, AmbitoVendedor, Profesora, CanalPrevisto, Correo, IntervaloDias, Sombra)
VALUES (@empresa, @dia, @cliente, @contacto, @tema, @claveTema, @origen, @clase, @motivo, @fechaEvento, @pedido, @factura, @envio,
    @eventoId, @producto, @nombreProducto, @vendedor, @nombreVendedor, @ambitoVendedor, @profesora, @canal, @correo, @intervalo, 1);";

        internal const string SQL_INSERTAR_DESCARTE = @"
INSERT INTO dbo.CalidadEncuestasDescartes (Empresa, Fecha, Cliente, Contacto, Tema, ClaveTema, Origen, Motivo, FechaEvento, Pedido, Vendedor, CanalPrevisto)
VALUES (@empresa, @dia, @cliente, @contacto, @tema, @claveTema, @origen, @motivo, @fechaEvento, @pedido, @vendedor, @canal);";

        internal const string SQL_INSERTAR_DIA = @"
INSERT INTO dbo.CalidadEncuestasDias (Empresa, Fecha, Candidatas, Encuestas, Descartes, IntervaloDias)
VALUES (@empresa, @dia, @candidatas, @encuestas, @descartes, @intervalo);";

        internal const string SQL_SOMBRA = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT RTRIM(Cliente) AS Cliente, RTRIM(Contacto) AS Contacto, CAST(Fecha AS datetime) AS Dia, RTRIM(Tema) AS Tema, RTRIM(Origen) AS Origen,
       RTRIM(Clase) AS Clase, Motivo, FechaEvento, Pedido, RTRIM(Factura) AS Factura, Envio, EventoId, RTRIM(Producto) AS Producto,
       RTRIM(Vendedor) AS Vendedor, NombreVendedor, RTRIM(AmbitoVendedor) AS AmbitoVendedor, Profesora, RTRIM(CanalPrevisto) AS CanalPrevisto
FROM dbo.CalidadEncuestas WITH (NOLOCK)
WHERE Empresa = @empresa AND Fecha >= @desde AND Fecha < @hasta;
SELECT RTRIM(Cliente) AS Cliente, RTRIM(Contacto) AS Contacto, CAST(Fecha AS datetime) AS Dia, RTRIM(Tema) AS Tema, RTRIM(Origen) AS Origen,
       RTRIM(Motivo) AS Motivo, FechaEvento, Pedido, RTRIM(Vendedor) AS Vendedor, RTRIM(CanalPrevisto) AS CanalPrevisto
FROM dbo.CalidadEncuestasDescartes WITH (NOLOCK)
WHERE Empresa = @empresa AND Fecha >= @desde AND Fecha < @hasta;
SELECT CAST(Fecha AS datetime) AS Dia, Candidatas, IntervaloDias
FROM dbo.CalidadEncuestasDias WITH (NOLOCK)
WHERE Empresa = @empresa AND Fecha >= @desde AND Fecha < @hasta
ORDER BY Fecha;";

        private readonly string cadenaConexion;
        private readonly Func<NVEntities> contexto;
        private readonly IReadOnlyList<IFuenteCandidatosEncuesta> fuentes;

        public LectorEncuestasSql(string cadenaConexion, Func<NVEntities> contexto = null, IReadOnlyList<IFuenteCandidatosEncuesta> fuentes = null)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = RepositorioSombraModoServicioSql.SinAlistar(cadenaConexion);
            this.contexto = contexto ?? (() => new NVEntities());
            this.fuentes = fuentes ?? FuentesCandidatosEncuesta.Todas();
        }

        public static LectorEncuestasSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new LectorEncuestasSql(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static SqlCommand Comando(SqlConnection conexion, string sql, SqlTransaction transaccion = null) =>
            new SqlCommand(sql, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS };

        public bool ExistenTablas()
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_TABLAS_ENCUESTAS))
            {
                return Convert.ToBoolean(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        /// <summary>La ventana de fechas del EVENTO de una fuente para los días [desde, hasta): se resta su retraso.</summary>
        internal static Tuple<DateTime, DateTime> VentanaEvento(DateTime desde, DateTime hasta, int retraso) =>
            Tuple.Create(desde.Date.AddDays(-retraso), hasta.Date.AddDays(-retraso));

        public LecturaCandidatasEncuesta LeerCandidatas(string empresa, DateTime desde, DateTime hasta, ParametrosFuentesEncuesta parametros)
        {
            var lectura = new LecturaCandidatasEncuesta();
            using (SqlConnection conexion = Abrir())
            {
                foreach (IFuenteCandidatosEncuesta fuente in fuentes)
                {
                    if (!string.IsNullOrWhiteSpace(fuente.TablaNecesaria) && !ExisteTabla(conexion, fuente.TablaNecesaria))
                    {
                        lectura.FuentesSinLeer.Add($"{fuente.Nombre} (falta {fuente.TablaNecesaria})");
                        continue;
                    }
                    int retraso = fuente.RetrasoDias(parametros);
                    Tuple<DateTime, DateTime> ventana = VentanaEvento(desde, hasta, retraso);
                    using (SqlCommand comando = Comando(conexion, fuente.Sql))
                    {
                        comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                        comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = ventana.Item1;
                        comando.Parameters.Add("@hasta", SqlDbType.DateTime).Value = ventana.Item2;
                        foreach (KeyValuePair<string, object> extra in fuente.ParametrosExtra ?? new Dictionary<string, object>())
                        {
                            _ = comando.Parameters.AddWithValue(extra.Key, extra.Value ?? DBNull.Value);
                        }
                        using (SqlDataReader r = comando.ExecuteReader())
                        {
                            lectura.Candidatas.AddRange(fuente.Construir(r, empresa, retraso)
                                .Where(c => c.Dia >= desde.Date && c.Dia < hasta.Date));
                        }
                    }
                }
            }
            return lectura;
        }

        private static bool ExisteTabla(SqlConnection conexion, string tabla)
        {
            using (SqlCommand comando = Comando(conexion, SQL_EXISTE_TABLA))
            {
                comando.Parameters.Add("@tabla", SqlDbType.NVarChar, 256).Value = tabla;
                return Convert.ToBoolean(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        public ContextoClientesEncuesta LeerContexto(string empresa, IReadOnlyCollection<string> clientes)
        {
            var ctx = new ContextoClientesEncuesta();
            List<string> lista = (clientes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // La app: el MISMO criterio que la push de campaña (FuentePushCampanaSql.FiltroDestinatarios). Tabla pequeña: una lectura.
            using (NVEntities db = contexto())
            {
                foreach (string c in db.DispositivosNotificaciones.Where(FuentePushCampanaSql.FiltroDestinatarios(empresa))
                    .Select(d => d.Cliente).Distinct().ToList())
                {
                    if (!string.IsNullOrWhiteSpace(c))
                    {
                        _ = ctx.ClientesConApp.Add(c.Trim());
                    }
                }
            }

            using (SqlConnection conexion = Abrir())
            {
                for (int i = 0; i < lista.Count; i += LOTE_CLIENTES)
                {
                    List<string> lote = lista.Skip(i).Take(LOTE_CLIENTES).ToList();
                    string nombres = string.Join(", ", lote.Select((_, n) => "@c" + n.ToString(CultureInfo.InvariantCulture)));
                    using (SqlCommand comando = Comando(conexion, string.Format(CultureInfo.InvariantCulture, SQL_CORREOS, nombres)))
                    {
                        comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                        for (int n = 0; n < lote.Count; n++)
                        {
                            comando.Parameters.Add("@c" + n.ToString(CultureInfo.InvariantCulture), SqlDbType.VarChar, 10).Value = lote[n];
                        }
                        using (SqlDataReader r = comando.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                AnadirCorreo(ctx, r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2));
                            }
                        }
                    }
                }

                using (SqlCommand comando = Comando(conexion, SQL_IMPAGOS))
                {
                    comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                    using (SqlDataReader r = comando.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            if (!r.IsDBNull(0) && !r.IsDBNull(1))
                            {
                                ctx.ImpagoPendienteDesde[r.GetString(0).Trim()] = r.GetDateTime(1);
                            }
                        }
                    }
                }

                // Las bajas de comunicaciones: CorreosPostCompraBajas (#532), la misma lectura que el recordatorio de reposición.
                try
                {
                    var filas = new List<RepositorioRecordatorioReposicionSql.FilaBaja>();
                    if (ExisteTabla(conexion, "dbo.CorreosPostCompraBajas"))
                    {
                        using (SqlCommand comando = Comando(conexion, RepositorioRecordatorioReposicionSql.SQL_BAJAS))
                        {
                            comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                            using (SqlDataReader r = comando.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    filas.Add(new RepositorioRecordatorioReposicionSql.FilaBaja
                                    {
                                        Cliente = r.IsDBNull(0) ? null : r.GetString(0),
                                        Email = r.IsDBNull(1) ? null : r.GetString(1),
                                        Tipo = r.IsDBNull(2) ? null : r.GetString(2)
                                    });
                                }
                            }
                        }
                    }
                    else
                    {
                        ctx.Avisos.Add("Falta la tabla dbo.CorreosPostCompraBajas: no se han podido mirar las bajas de comunicaciones.");
                    }
                    ctx.Bajas = RepositorioRecordatorioReposicionSql.ConstruirBajas(filas);
                }
                catch (Exception ex)
                {
                    ctx.Bajas = null;
                    ctx.Avisos.Add("No se han podido leer las bajas de comunicaciones (CorreosPostCompraBajas): " + ex.Message);
                }
            }
            return ctx;
        }

        /// <summary>El primer correo VÁLIDO de cada contacto y de cada cliente (puro, para los tests).</summary>
        internal static void AnadirCorreo(ContextoClientesEncuesta ctx, string cliente, string contacto, string correo)
        {
            if (string.IsNullOrWhiteSpace(cliente) || !ComercialesPedido.EsCorreoValido(correo?.Trim()))
            {
                return;
            }
            string limpio = correo.Trim();
            string clave = ReglaCanalEncuesta.Clave(cliente, contacto);
            if (!ctx.CorreoPorContacto.ContainsKey(clave))
            {
                ctx.CorreoPorContacto[clave] = limpio;
            }
            if (!ctx.CorreoPorCliente.ContainsKey(cliente.Trim()))
            {
                ctx.CorreoPorCliente[cliente.Trim()] = limpio;
            }
        }

        public List<EncuestaHistorial> LeerHistorial(string empresa, DateTime desde, DateTime hoy)
        {
            var historial = new List<EncuestaHistorial>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_HISTORIAL))
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde.Date;
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    while (r.Read())
                    {
                        if (!Enum.TryParse(r.GetString(2), out TemaEncuesta tema))
                        {
                            continue;
                        }
                        historial.Add(new EncuestaHistorial
                        {
                            Cliente = r.GetString(0).Trim(),
                            Dia = r.GetDateTime(1),
                            Tema = tema,
                            ClaveTema = r.GetString(3),
                            Ignorada = ReglaEncuestaIgnorada.EsIgnorada(r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4),
                                r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5), hoy)
                        });
                    }
                }
            }
            return historial;
        }

        public bool DiaProcesado(string empresa, DateTime dia)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_DIA_PROCESADO))
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                comando.Parameters.Add("@dia", SqlDbType.Date).Value = dia.Date;
                return Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        public void GuardarDia(string empresa, DateTime dia, int candidatas, int intervaloDias, IReadOnlyList<DecisionEncuestaCliente> decisiones)
        {
            List<DecisionEncuestaCliente> lista = (decisiones ?? new List<DecisionEncuestaCliente>()).ToList();
            using (SqlConnection conexion = Abrir())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            {
                foreach (DecisionEncuestaCliente d in lista.Where(d => d.Elegida != null))
                {
                    CandidataEncuesta c = d.Elegida;
                    using (SqlCommand comando = Comando(conexion, SQL_INSERTAR_ENCUESTA, transaccion))
                    {
                        comando.Parameters.Add("@motivo", SqlDbType.NVarChar, 400).Value = Nulo(Recortar(c.Motivo, 400));
                        ParametrosComunes(comando, empresa, dia, c);
                        comando.Parameters.Add("@clase", SqlDbType.VarChar, 20).Value = c.Clase.ToString();
                        comando.Parameters.Add("@factura", SqlDbType.VarChar, 20).Value = Nulo(Recortar(c.Factura, 20));
                        comando.Parameters.Add("@envio", SqlDbType.Int).Value = (object)c.Envio ?? DBNull.Value;
                        comando.Parameters.Add("@eventoId", SqlDbType.Int).Value = (object)c.EventoId ?? DBNull.Value;
                        comando.Parameters.Add("@producto", SqlDbType.VarChar, 20).Value = Nulo(Recortar(c.Producto, 20));
                        comando.Parameters.Add("@nombreProducto", SqlDbType.NVarChar, 100).Value = Nulo(Recortar(c.NombreProducto, 100));
                        comando.Parameters.Add("@nombreVendedor", SqlDbType.NVarChar, 100).Value = Nulo(Recortar(c.NombreVendedor, 100));
                        comando.Parameters.Add("@ambitoVendedor", SqlDbType.VarChar, 20).Value = Nulo(c.AmbitoVendedor);
                        comando.Parameters.Add("@profesora", SqlDbType.NVarChar, 100).Value = Nulo(Recortar(c.Profesora, 100));
                        comando.Parameters.Add("@correo", SqlDbType.VarChar, 255).Value = Nulo(Recortar(c.Correo, 255));
                        comando.Parameters.Add("@intervalo", SqlDbType.Int).Value = d.IntervaloDias;
                        _ = comando.ExecuteNonQuery();
                    }
                }
                int descartes = 0;
                foreach (KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta> descarte in lista.SelectMany(d => d.Descartes))
                {
                    descartes++;
                    using (SqlCommand comando = Comando(conexion, SQL_INSERTAR_DESCARTE, transaccion))
                    {
                        ParametrosComunes(comando, empresa, dia, descarte.Key);
                        comando.Parameters["@motivo"].Value = descarte.Value.ToString();
                        _ = comando.ExecuteNonQuery();
                    }
                }
                using (SqlCommand comando = Comando(conexion, SQL_INSERTAR_DIA, transaccion))
                {
                    comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@dia", SqlDbType.Date).Value = dia.Date;
                    comando.Parameters.Add("@candidatas", SqlDbType.Int).Value = candidatas;
                    comando.Parameters.Add("@encuestas", SqlDbType.Int).Value = lista.Count(d => d.Elegida != null);
                    comando.Parameters.Add("@descartes", SqlDbType.Int).Value = descartes;
                    comando.Parameters.Add("@intervalo", SqlDbType.Int).Value = intervaloDias;
                    _ = comando.ExecuteNonQuery();
                }
                transaccion.Commit();
            }
        }

        /// <summary>Los parámetros que comparten la encuesta y el descarte (en el descarte, @motivo es el del descarte).</summary>
        private static void ParametrosComunes(SqlCommand comando, string empresa, DateTime dia, CandidataEncuesta c)
        {
            comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
            comando.Parameters.Add("@dia", SqlDbType.Date).Value = dia.Date;
            comando.Parameters.Add("@cliente", SqlDbType.VarChar, 10).Value = Recortar(c.Cliente, 10);
            comando.Parameters.Add("@contacto", SqlDbType.VarChar, 3).Value = Recortar(c.Contacto ?? "0", 3);
            comando.Parameters.Add("@tema", SqlDbType.VarChar, 30).Value = c.Tema.ToString();
            comando.Parameters.Add("@claveTema", SqlDbType.VarChar, 60).Value = Recortar(c.ClaveTema, 60);
            comando.Parameters.Add("@origen", SqlDbType.VarChar, 50).Value = Recortar(c.Origen ?? string.Empty, 50);
            comando.Parameters.Add("@fechaEvento", SqlDbType.DateTime).Value = c.FechaEvento;
            comando.Parameters.Add("@pedido", SqlDbType.Int).Value = (object)c.Pedido ?? DBNull.Value;
            comando.Parameters.Add("@vendedor", SqlDbType.VarChar, 10).Value = Nulo(Recortar(c.Vendedor, 10));
            comando.Parameters.Add("@canal", SqlDbType.VarChar, 10).Value = Recortar(c.Canal ?? CanalesEncuesta.SIN_CANAL, 10);
            if (!comando.Parameters.Contains("@motivo"))
            {
                comando.Parameters.Add("@motivo", SqlDbType.VarChar, 30).Value = DBNull.Value;
            }
        }

        public LecturaSombraEncuestas LeerSombra(string empresa, DateTime desde, DateTime hasta)
        {
            var lectura = new LecturaSombraEncuestas();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_SOMBRA))
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde.Date;
                comando.Parameters.Add("@hasta", SqlDbType.Date).Value = hasta.Date;
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    while (r.Read())
                    {
                        if (!Enum.TryParse(Cadena(r, "Tema"), out TemaEncuesta tema))
                        {
                            continue;
                        }
                        _ = Enum.TryParse(Cadena(r, "Clase"), out ClaseCandidataEncuesta clase);
                        lectura.Encuestas.Add(new CandidataEncuesta
                        {
                            Empresa = empresa,
                            Cliente = Cadena(r, "Cliente"),
                            Contacto = Cadena(r, "Contacto"),
                            Dia = (DateTime)r["Dia"],
                            Tema = tema,
                            Origen = Cadena(r, "Origen"),
                            Clase = clase,
                            Motivo = Cadena(r, "Motivo"),
                            FechaEvento = (DateTime)r["FechaEvento"],
                            Pedido = EnteroNulo(r, "Pedido"),
                            Factura = Cadena(r, "Factura"),
                            Envio = EnteroNulo(r, "Envio"),
                            EventoId = EnteroNulo(r, "EventoId"),
                            Producto = Cadena(r, "Producto"),
                            Vendedor = Cadena(r, "Vendedor"),
                            NombreVendedor = Cadena(r, "NombreVendedor"),
                            AmbitoVendedor = Cadena(r, "AmbitoVendedor"),
                            Profesora = Cadena(r, "Profesora"),
                            Canal = Cadena(r, "CanalPrevisto")
                        });
                    }
                    _ = r.NextResult();
                    while (r.Read())
                    {
                        if (!Enum.TryParse(Cadena(r, "Tema"), out TemaEncuesta tema) || !Enum.TryParse(Cadena(r, "Motivo"), out MotivoDescarteEncuesta motivo))
                        {
                            continue;
                        }
                        lectura.Descartes.Add(new KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>(new CandidataEncuesta
                        {
                            Empresa = empresa,
                            Cliente = Cadena(r, "Cliente"),
                            Contacto = Cadena(r, "Contacto"),
                            Dia = (DateTime)r["Dia"],
                            Tema = tema,
                            Origen = Cadena(r, "Origen"),
                            FechaEvento = (DateTime)r["FechaEvento"],
                            Pedido = EnteroNulo(r, "Pedido"),
                            Vendedor = Cadena(r, "Vendedor"),
                            Canal = Cadena(r, "CanalPrevisto")
                        }, motivo));
                    }
                    _ = r.NextResult();
                    while (r.Read())
                    {
                        lectura.CandidatasPorDia[((DateTime)r["Dia"]).Date] = Convert.ToInt32(r["Candidatas"], CultureInfo.InvariantCulture);
                        lectura.UltimoIntervalo = Convert.ToInt32(r["IntervaloDias"], CultureInfo.InvariantCulture);
                    }
                }
            }
            return lectura;
        }

        private static string Cadena(IDataRecord r, string columna)
        {
            object v = r[columna];
            return v == null || v == DBNull.Value ? null : Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim();
        }

        private static int? EnteroNulo(IDataRecord r, string columna)
        {
            object v = r[columna];
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        private static object Nulo(string valor) => string.IsNullOrWhiteSpace(valor) ? (object)DBNull.Value : valor;

        private static string Recortar(string valor, int largo)
        {
            string limpio = valor?.Trim();
            return limpio == null || limpio.Length <= largo ? limpio : limpio.Substring(0, largo);
        }
    }

    /// <summary>
    /// El canal previsto (Carlos 27/11/26): push si el cliente tiene la app de clientes con avisos aceptados (el criterio de destinatarios de
    /// la push de campaña); si no, correo (el de ese contacto o, si no tiene, el de otro contacto del cliente); si tampoco, sin canal. Nunca
    /// los dos. Y la situación del cliente para el gobernador. PURA.
    /// </summary>
    public static class ReglaCanalEncuesta
    {
        public static string Clave(string cliente, string contacto) => $"{cliente?.Trim()}|{(string.IsNullOrWhiteSpace(contacto) ? "0" : contacto.Trim())}";

        public static string Correo(ContextoClientesEncuesta ctx, string cliente, string contacto)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(cliente))
            {
                return null;
            }
            return ctx.CorreoPorContacto.TryGetValue(Clave(cliente, contacto), out string correo) ? correo
                : ctx.CorreoPorCliente.TryGetValue(cliente.Trim(), out string otro) ? otro : null;
        }

        public static string Canal(ContextoClientesEncuesta ctx, string cliente, string contacto)
        {
            if (ctx != null && !string.IsNullOrWhiteSpace(cliente) && ctx.ClientesConApp.Contains(cliente.Trim()))
            {
                return CanalesEncuesta.PUSH;
            }
            return Correo(ctx, cliente, contacto) != null ? CanalesEncuesta.CORREO : CanalesEncuesta.SIN_CANAL;
        }

        /// <summary>Pone el canal y el correo en la candidata.</summary>
        public static void Asignar(ContextoClientesEncuesta ctx, CandidataEncuesta c)
        {
            c.Canal = Canal(ctx, c.Cliente, c.Contacto);
            c.Correo = c.Canal == CanalesEncuesta.CORREO ? Correo(ctx, c.Cliente, c.Contacto) : null;
        }

        /// <summary>
        /// Los clientes de baja de TODO en CorreosPostCompraBajas (Tipo NULL): por el cliente o por cualquiera de sus correos. Se calcula una vez
        /// por contexto.
        /// </summary>
        public static HashSet<string> ClientesDeBaja(ContextoClientesEncuesta ctx)
        {
            if (ctx.ClientesDeBaja != null)
            {
                return ctx.ClientesDeBaja;
            }
            var deBaja = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ctx.Bajas != null)
            {
                foreach (string cliente in ctx.Bajas.Clientes)
                {
                    _ = deBaja.Add(cliente.Trim());
                }
                foreach (KeyValuePair<string, string> correo in ctx.CorreoPorContacto)
                {
                    if (ctx.Bajas.Incluye(null, correo.Value))
                    {
                        _ = deBaja.Add(correo.Key.Substring(0, correo.Key.IndexOf('|')));
                    }
                }
                foreach (KeyValuePair<string, string> correo in ctx.CorreoPorCliente)
                {
                    if (ctx.Bajas.Incluye(null, correo.Value))
                    {
                        _ = deBaja.Add(correo.Key);
                    }
                }
            }
            ctx.ClientesDeBaja = deBaja;
            return deBaja;
        }

        /// <summary>
        /// La situación ese día: de baja si el cliente o alguno de sus correos tiene baja de TODO en CorreosPostCompraBajas (Tipo NULL); impago
        /// en gestión si tiene un impagado sin liquidar con fecha ≤ ese día.
        /// </summary>
        public static SituacionClienteEncuesta Situacion(ContextoClientesEncuesta ctx, string cliente, DateTime dia)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(cliente))
            {
                return new SituacionClienteEncuesta();
            }
            string limpio = cliente.Trim();
            bool deBaja = ClientesDeBaja(ctx).Contains(limpio);
            bool impago = ctx.ImpagoPendienteDesde.TryGetValue(limpio, out DateTime desde) && desde.Date <= dia.Date;
            return new SituacionClienteEncuesta { DeBaja = deBaja, ImpagoEnGestion = impago };
        }
    }
}
