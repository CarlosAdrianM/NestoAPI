using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>NestoAPI#603 (corte 5): qué ha pasado con cada vendedor de la lista.</summary>
    public static class EstadosRecordatorioSugerencias
    {
        /// <summary>Se le ha mandado el aviso.</summary>
        public const string AVISADO = "Avisado";
        /// <summary>Con soloListar: se le mandaría el aviso.</summary>
        public const string POR_AVISAR = "PorAvisar";
        /// <summary>Ha atendido alguna sugerencia en los últimos 7 días laborables: no se le dice nada.</summary>
        public const string USA_LA_LISTA = "UsaLaLista";
        /// <summary>Ya se le avisó hace menos de una semana.</summary>
        public const string YA_AVISADO = "YaAvisadoEstaSemana";
        /// <summary>No hay nadie de su cartera esperando: no hay nada que contarle.</summary>
        public const string SIN_PENDIENTES = "SinPendientes";
        /// <summary>Ningún usuario tiene este vendedor en el parámetro Vendedor.</summary>
        public const string SIN_USUARIO = "SinUsuario";
        /// <summary>Tiene usuarios, pero ninguno con rapports en los últimos 90 días (p. ej. ya no está en la empresa).</summary>
        public const string SIN_USUARIO_ACTIVO = "SinUsuarioActivo";
        /// <summary>No se pudo mandar a ninguno de sus usuarios (queda en ELMAH).</summary>
        public const string ERROR = "Error";
    }

    /// <summary>NestoAPI#603 (corte 5): una fila de la respuesta de POST api/Clientes/SugerenciasContacto/Recordar.</summary>
    public class ResultadoRecordatorioSugerenciasDTO
    {
        public string Vendedor { get; set; }
        /// <summary>Usuarios del vendedor con actividad reciente (a los que llega el aviso).</summary>
        public List<string> Usuarios { get; set; } = new List<string>();
        /// <summary>Usuarios con el parámetro Vendedor pero sin rapports en los últimos 90 días: no se les avisa.</summary>
        public List<string> UsuariosInactivos { get; set; } = new List<string>();
        public string Estado { get; set; }
        /// <summary>Sugerencias atendidas desde <see cref="Desde"/>.</summary>
        public int Atendidas { get; set; }
        public DateTime Desde { get; set; }
        public DateTime? UltimoAviso { get; set; }
        public int PendientesMaxima { get; set; }
        public int PendientesAlta { get; set; }
        public int PendientesResto { get; set; }
        /// <summary>El texto del aviso (también con soloListar).</summary>
        public string Texto { get; set; }
    }

    /// <summary>NestoAPI#603 (corte 5): cuántos clientes de su cartera esperan, por prioridad.</summary>
    public class PendientesVendedor
    {
        public int Maxima { get; set; }
        public int Alta { get; set; }
        public int Media { get; set; }
        public int Baja { get; set; }
        public int Total => Maxima + Alta + Media + Baja;
    }

    public interface IRepositorioRecordatorioSugerencias
    {
        /// <summary>ParametrosUsuario «(defecto)», empresa 1, clave SugerenciasContactoAvisarA. Null si no hay fila.</summary>
        Task<string> LeerListaAvisar();
        /// <summary>Usuarios (sin dominio) con el parámetro Vendedor = <paramref name="vendedor"/>.</summary>
        Task<List<string>> LeerUsuarios(string vendedor);
        /// <summary>De <paramref name="usuarios"/> (sin dominio), los que tienen algún rapport (SeguimientoCliente.Usuario,
        /// con o sin dominio) desde <paramref name="desde"/>.</summary>
        Task<List<string>> FiltrarUsuariosActivos(IEnumerable<string> usuarios, DateTime desde);
        Task<int> ContarAtendidas(string vendedor, DateTime desde);
        Task<DateTime?> LeerUltimoAviso(string vendedor);
        Task GuardarAviso(string vendedor, IEnumerable<string> usuarios, DateTime fecha, string texto);
        Task<PendientesVendedor> LeerPendientes(string vendedor, DateTime hoy);
    }

    public interface IRecordatorioSugerenciasContacto
    {
        Task<List<ResultadoRecordatorioSugerenciasDTO>> Ejecutar(bool soloListar);
    }

    /// <summary>
    /// NestoAPI#603 (corte 5): un aviso en la campana de Nesto a los vendedores de la lista (parámetro
    /// SugerenciasContactoAvisarA) que en los últimos 7 días laborables no han atendido ninguna sugerencia (ni abierto la
    /// lista), con el número real de clientes que les esperan. Como mucho uno por semana a cada uno. A los que usan la
    /// lista no se les dice nada.
    /// </summary>
    public class RecordatorioSugerenciasContacto : IRecordatorioSugerenciasContacto
    {
        public const string TIPO_NOTIFICACION = "RecordatorioSugerenciasContacto";
        public const int DIAS_LABORABLES_SIN_USO = 7;
        public const int DIAS_ENTRE_AVISOS = 7;
        /// <summary>Solo se avisa a usuarios con algún rapport en estos días (Elena seguía con Vendedor = PA sin estar ya).</summary>
        public const int DIAS_ACTIVIDAD_USUARIO = 90;
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IRepositorioRecordatorioSugerencias repositorio;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<DateTime> reloj;
        private readonly Func<DateTime, bool> esLaborable;

        public RecordatorioSugerenciasContacto()
            : this(new RepositorioRecordatorioSugerenciasSql(), new ServicioNotificacionesPush(), () => DateTime.Now,
                  d => !GestorFestivos.EsFestivo(d, Constantes.Almacenes.ALGETE))
        {
        }

        internal RecordatorioSugerenciasContacto(IRepositorioRecordatorioSugerencias repositorio, IServicioNotificacionesPush notificaciones,
            Func<DateTime> reloj, Func<DateTime, bool> esLaborable)
        {
            this.repositorio = repositorio;
            this.notificaciones = notificaciones;
            this.reloj = reloj;
            this.esLaborable = esLaborable;
        }

        /// <summary>Los vendedores de la lista, en mayúsculas y sin repetir. Vacía (o «0») = nadie.</summary>
        public static List<string> Lista(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor) || valor.Trim() == "0")
            {
                return new List<string>();
            }
            return valor.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim().ToUpperInvariant())
                .Where(v => v.Length > 0)
                .Distinct()
                .ToList();
        }

        /// <summary>El primer día de la ventana: el 7.º día laborable anterior a hoy (hoy no cuenta).</summary>
        public static DateTime InicioVentana(DateTime hoy, Func<DateTime, bool> esLaborable)
        {
            DateTime dia = hoy.Date;
            int contados = 0;
            for (int i = 0; i < 60 && contados < DIAS_LABORABLES_SIN_USO; i++)
            {
                dia = dia.AddDays(-1);
                if (dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && esLaborable(dia))
                {
                    contados++;
                }
            }
            return dia;
        }

        public static string Titulo => "Clientes para contactar";

        /// <summary>El aviso con el dato concreto de su cartera, o null si no hay nadie esperando.</summary>
        public static string Texto(PendientesVendedor p)
        {
            if (p == null || p.Total <= 0)
            {
                return null;
            }
            string dato;
            if (p.Maxima > 0 && p.Alta > 0)
            {
                dato = $"Tienes {Clientes(p.Maxima)} de prioridad Máxima y {p.Alta} de prioridad Alta";
            }
            else if (p.Maxima > 0)
            {
                dato = $"Tienes {Clientes(p.Maxima)} de prioridad Máxima";
            }
            else if (p.Alta > 0)
            {
                dato = $"Tienes {Clientes(p.Alta)} de prioridad Alta";
            }
            else
            {
                dato = $"Tienes {Clientes(p.Total)} de tu cartera";
            }
            return $"{dato} esperando en Rapports › Clientes para contactar. La lista te dice a quién llamar primero y por qué.";
        }

        private static string Clientes(int n) => n == 1 ? "1 cliente" : $"{n} clientes";

        public async Task<List<ResultadoRecordatorioSugerenciasDTO>> Ejecutar(bool soloListar)
        {
            DateTime ahora = reloj();
            DateTime hoy = ahora.Date;
            DateTime desde = InicioVentana(hoy, esLaborable);
            List<string> vendedores = Lista(await repositorio.LeerListaAvisar().ConfigureAwait(false));
            var resultados = new List<ResultadoRecordatorioSugerenciasDTO>();

            foreach (string vendedor in vendedores)
            {
                var resultado = new ResultadoRecordatorioSugerenciasDTO { Vendedor = vendedor, Desde = desde };
                resultados.Add(resultado);
                try
                {
                    resultado.Usuarios = (await repositorio.LeerUsuarios(vendedor).ConfigureAwait(false) ?? new List<string>())
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Select(u => u.Substring(u.IndexOf('\\') + 1).Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    resultado.Atendidas = await repositorio.ContarAtendidas(vendedor, desde).ConfigureAwait(false);
                    if (resultado.Atendidas > 0)
                    {
                        resultado.Estado = EstadosRecordatorioSugerencias.USA_LA_LISTA;
                        continue;
                    }
                    resultado.UltimoAviso = await repositorio.LeerUltimoAviso(vendedor).ConfigureAwait(false);
                    if (resultado.UltimoAviso.HasValue && resultado.UltimoAviso.Value.Date > hoy.AddDays(-DIAS_ENTRE_AVISOS))
                    {
                        resultado.Estado = EstadosRecordatorioSugerencias.YA_AVISADO;
                        continue;
                    }
                    if (!resultado.Usuarios.Any())
                    {
                        resultado.Estado = EstadosRecordatorioSugerencias.SIN_USUARIO;
                        continue;
                    }
                    List<string> activos = await repositorio.FiltrarUsuariosActivos(resultado.Usuarios, hoy.AddDays(-DIAS_ACTIVIDAD_USUARIO)).ConfigureAwait(false)
                        ?? new List<string>();
                    resultado.UsuariosInactivos = resultado.Usuarios.Where(u => !activos.Contains(u, StringComparer.OrdinalIgnoreCase)).ToList();
                    resultado.Usuarios = resultado.Usuarios.Where(u => activos.Contains(u, StringComparer.OrdinalIgnoreCase)).ToList();
                    if (!resultado.Usuarios.Any())
                    {
                        resultado.Estado = EstadosRecordatorioSugerencias.SIN_USUARIO_ACTIVO;
                        continue;
                    }
                    PendientesVendedor pendientes = await repositorio.LeerPendientes(vendedor, hoy).ConfigureAwait(false) ?? new PendientesVendedor();
                    resultado.PendientesMaxima = pendientes.Maxima;
                    resultado.PendientesAlta = pendientes.Alta;
                    resultado.PendientesResto = pendientes.Media + pendientes.Baja;
                    resultado.Texto = Texto(pendientes);
                    if (resultado.Texto == null)
                    {
                        resultado.Estado = EstadosRecordatorioSugerencias.SIN_PENDIENTES;
                        continue;
                    }
                    if (soloListar)
                    {
                        resultado.Estado = EstadosRecordatorioSugerencias.POR_AVISAR;
                        continue;
                    }
                    resultado.Estado = await Avisar(vendedor, resultado.Usuarios, resultado.Texto, ahora).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    resultado.Estado = EstadosRecordatorioSugerencias.ERROR;
                    ElmahHelper.Log(new Exception($"[Recordatorio sugerencias #603] {vendedor}: {ex.Message}", ex), "Sistema (recordatorio de sugerencias)");
                }
            }
            return resultados;
        }

        private async Task<string> Avisar(string vendedor, List<string> usuarios, string texto, DateTime ahora)
        {
            var notificacion = new NotificacionPushDTO
            {
                Titulo = Titulo,
                Cuerpo = texto,
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_NOTIFICACION, ["vendedor"] = vendedor }
            };
            var avisados = new List<string>();
            foreach (string usuario in usuarios)
            {
                try
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(DOMINIO + usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                    avisados.Add(usuario);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Recordatorio sugerencias #603] No se pudo avisar a {usuario} ({vendedor}): {ex.Message}", ex),
                        "Sistema (recordatorio de sugerencias)");
                }
            }
            if (!avisados.Any())
            {
                return EstadosRecordatorioSugerencias.ERROR;
            }
            await repositorio.GuardarAviso(vendedor, avisados, ahora, texto).ConfigureAwait(false);
            return EstadosRecordatorioSugerencias.AVISADO;
        }
    }

    /// <summary>NestoAPI#603 (corte 5): el job de Hangfire «recordatorio-sugerencias-contacto» (lunes a viernes, 9:30).</summary>
    public static class RecordatorioSugerenciasContactoJobsService
    {
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static async Task Procesar()
        {
            try
            {
                if (GestorFestivos.EsFestivo(DateTime.Today, Constantes.Almacenes.ALGETE))
                {
                    return;
                }
                _ = await new RecordatorioSugerenciasContacto().Ejecutar(soloListar: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Recordatorio sugerencias #603] Error en el job: " + ex.Message, ex), "Sistema (recordatorio de sugerencias)");
                throw;
            }
        }
    }

    /// <summary>NestoAPI#603 (corte 5): lecturas y registro del recordatorio (SqlClient) y los pendientes del motor.</summary>
    public class RepositorioRecordatorioSugerenciasSql : IRepositorioRecordatorioSugerencias
    {
        private const int TIMEOUT_SEGUNDOS = 30;

        internal const string SQL_USUARIOS = @"
            SELECT DISTINCT RTRIM(p.Usuario) FROM ParametrosUsuario p WITH (NOLOCK)
            WHERE p.Empresa = '1' AND p.Clave = 'Vendedor' AND RTRIM(p.Valor) = @Vendedor AND p.Usuario <> '(defecto)';";

        internal const string SQL_ATENDIDAS = @"
            SELECT COUNT(*) FROM dbo.SugerenciasContacto WITH (NOLOCK)
            WHERE Vendedor = @Vendedor AND Fecha >= @Desde AND Atendida = 1;";

        internal const string SQL_ULTIMO_AVISO = @"
            SELECT MAX(Fecha) FROM dbo.SugerenciasContactoRecordatorios WITH (NOLOCK) WHERE Vendedor = @Vendedor;";

        internal const string SQL_GUARDAR_AVISO = @"
            INSERT INTO dbo.SugerenciasContactoRecordatorios (Fecha, Vendedor, Usuarios, Texto)
            VALUES (@Fecha, @Vendedor, @Usuarios, @Texto);";

        public Task<string> LeerListaAvisar()
        {
            return Task.Run(() => new LectorParametrosUsuario().LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.SUGERENCIAS_CONTACTO_AVISAR_A));
        }

        public Task<List<string>> LeerUsuarios(string vendedor)
        {
            return Task.Run(() =>
            {
                var usuarios = new List<string>();
                using (SqlConnection conexion = ConexionSql.Abrir())
                using (var comando = new SqlCommand(SQL_USUARIOS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            usuarios.Add(lector[0].ToString().Trim());
                        }
                    }
                }
                return usuarios;
            });
        }

        public Task<List<string>> FiltrarUsuariosActivos(IEnumerable<string> usuarios, DateTime desde)
        {
            List<string> lista = (usuarios ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct().ToList();
            if (!lista.Any())
            {
                return Task.FromResult(new List<string>());
            }
            return Task.Run(() =>
            {
                var activos = new List<string>();
                string parametros = string.Join(", ", lista.Select((u, i) => "@U" + i));
                string sql = @"
                    SELECT DISTINCT RTRIM(SUBSTRING(s.Usuario, CHARINDEX('\', s.Usuario) + 1, 100))
                    FROM SeguimientoCliente s WITH (NOLOCK)
                    WHERE s.Fecha >= @Desde AND RTRIM(SUBSTRING(s.Usuario, CHARINDEX('\', s.Usuario) + 1, 100)) IN (" + parametros + ");";
                using (SqlConnection conexion = ConexionSql.Abrir())
                using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Desde", SqlDbType.DateTime) { Value = desde.Date });
                    for (int i = 0; i < lista.Count; i++)
                    {
                        _ = comando.Parameters.Add(new SqlParameter("@U" + i, SqlDbType.VarChar, 30) { Value = lista[i] });
                    }
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            string activo = lector[0].ToString().Trim();
                            // La BD no distingue mayúsculas: se devuelve el usuario tal como venía en la lista.
                            activos.AddRange(lista.Where(u => string.Equals(u, activo, StringComparison.OrdinalIgnoreCase)));
                        }
                    }
                }
                return activos.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            });
        }

        public Task<int> ContarAtendidas(string vendedor, DateTime desde)
        {
            return Task.Run(() =>
            {
                using (SqlConnection conexion = ConexionSql.Abrir())
                using (var comando = new SqlCommand(SQL_ATENDIDAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Desde", SqlDbType.DateTime) { Value = desde.Date });
                    return Convert.ToInt32(comando.ExecuteScalar());
                }
            });
        }

        public Task<DateTime?> LeerUltimoAviso(string vendedor)
        {
            return Task.Run(() =>
            {
                using (SqlConnection conexion = ConexionSql.Abrir())
                using (var comando = new SqlCommand(SQL_ULTIMO_AVISO, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    object valor = comando.ExecuteScalar();
                    return valor == null || valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor);
                }
            });
        }

        public Task GuardarAviso(string vendedor, IEnumerable<string> usuarios, DateTime fecha, string texto)
        {
            return Task.Run(() =>
            {
                using (SqlConnection conexion = ConexionSql.Abrir())
                using (var comando = new SqlCommand(SQL_GUARDAR_AVISO, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    string lista = string.Join(",", usuarios ?? Enumerable.Empty<string>());
                    _ = comando.Parameters.Add(new SqlParameter("@Fecha", SqlDbType.DateTime) { Value = fecha });
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.Char, 3) { Value = vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Usuarios", SqlDbType.VarChar, 200) { Value = lista.Length > 200 ? lista.Substring(0, 200) : lista });
                    _ = comando.Parameters.Add(new SqlParameter("@Texto", SqlDbType.NVarChar, 400) { Value = texto });
                    _ = comando.ExecuteNonQuery();
                }
            });
        }

        /// <summary>Lo mismo que cuenta el panel de ritmo (motor del corte 1), sin registrar ninguna sugerencia.</summary>
        public async Task<PendientesVendedor> LeerPendientes(string vendedor, DateTime hoy)
        {
            List<ClienteCarteraContacto> cartera = await new RepositorioCarteraContactoSql().LeerCartera(vendedor, hoy).ConfigureAwait(false);
            try
            {
                Dictionary<string, PrediccionContacto> predicciones = await new ProbabilidadesContactoModelo().Leer(vendedor, string.Empty, string.Empty).ConfigureAwait(false)
                    ?? new Dictionary<string, PrediccionContacto>();
                foreach (ClienteCarteraContacto cliente in cartera)
                {
                    if (predicciones.TryGetValue(cliente.Clave, out PrediccionContacto prediccion))
                    {
                        cliente.Probabilidad = prediccion.Probabilidad;
                    }
                }
            }
            catch (Exception ex)
            {
                // Sin modelo, la lista sale igual (sin Máxima), como en el GET.
                ElmahHelper.Log(new Exception($"[Recordatorio sugerencias #603] Sin probabilidades para {vendedor}: {ex.Message}", ex), "Sistema (recordatorio de sugerencias)");
            }
            List<SugerenciaContactoDTO> pendientes = new MotorSugerenciasContacto().Priorizar(cartera, hoy);
            return new PendientesVendedor
            {
                Maxima = pendientes.Count(p => p.Prioridad == PrioridadesContacto.MAXIMA),
                Alta = pendientes.Count(p => p.Prioridad == PrioridadesContacto.ALTA),
                Media = pendientes.Count(p => p.Prioridad == PrioridadesContacto.MEDIA),
                Baja = pendientes.Count(p => p.Prioridad == PrioridadesContacto.BAJA)
            };
        }
    }
}
