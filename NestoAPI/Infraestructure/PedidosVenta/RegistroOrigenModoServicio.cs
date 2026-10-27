using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#704 (caso 929152): de dónde sale el modo de servicio que manda el cliente en <see cref="PedidoVentaDTO.OrigenModoServicio"/>.
    /// Null = cliente antiguo (no lo manda).
    /// </summary>
    public static class OrigenesModoServicio
    {
        /// <summary>Lo eligió el vendedor en el selector.</summary>
        public const string USUARIO = "Usuario";
        /// <summary>La sugerencia del servidor (POST api/PedidosVenta/ModoServicioSugerido, #506), aplicada sin que el vendedor la tocara.</summary>
        public const string SUGERIDO = "Sugerido";
        /// <summary>El parámetro ModoServicioPorDefecto del usuario o el defecto del cliente.</summary>
        public const string POR_DEFECTO = "PorDefecto";
        /// <summary>Corregido por el cliente tras un rechazo de #518 o porque el elegido dejó de tener sentido (#187).</summary>
        public const string CORREGIDO = "Corregido";
        public const int LARGO_MAXIMO = 20;

        public static readonly string[] Conocidos = { USUARIO, SUGERIDO, POR_DEFECTO, CORREGIDO };

        /// <summary>
        /// El valor canónico (sin distinguir caja ni espacios) o, si no es uno de los conocidos, lo que vino recortado a
        /// <see cref="LARGO_MAXIMO"/> (se guarda igual: así se ve qué manda un cliente nuevo). Vacío o null: null (cliente antiguo).
        /// </summary>
        public static string Normalizar(string origen)
        {
            string limpio = origen?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            string conocido = Conocidos.FirstOrDefault(c => string.Equals(c, limpio, StringComparison.OrdinalIgnoreCase));
            if (conocido != null)
            {
                return conocido;
            }
            return limpio.Length > LARGO_MAXIMO ? limpio.Substring(0, LARGO_MAXIMO) : limpio;
        }

        public static bool EsConocido(string origen) => Conocidos.Contains(Normalizar(origen));
    }

    /// <summary>NestoAPI#704: lo que mandó el cliente sobre el modo, copiado ANTES de normalizar (NormalizarAlCrear lo pisa).</summary>
    public class EnvioModoServicio
    {
        /// <summary>El modo tal como vino en el DTO (null = el cliente no lo manda y nace en el sugerido o el defecto).</summary>
        public byte? ModoEnviado { get; set; }
        public bool ServirJuntoEnviado { get; set; }
        /// <summary>El origen que dice el cliente, canónico (<see cref="OrigenesModoServicio.Normalizar"/>).</summary>
        public string Origen { get; set; }
        /// <summary>La sugerencia que el cliente tenía en pantalla al grabar (lo que le dio ModoServicioSugerido), si la manda.</summary>
        public byte? ModoSugeridoCliente { get; set; }
    }

    /// <summary>NestoAPI#704: una fila de <c>PedidosModoServicioOrigen</c>.</summary>
    public class FilaOrigenModoServicio
    {
        public DateTime Fecha { get; set; }
        /// <summary><see cref="RegistroOrigenModoServicio.OPERACION_CREAR"/> o <see cref="RegistroOrigenModoServicio.OPERACION_MODIFICAR"/>.</summary>
        public string Operacion { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Usuario { get; set; }
        /// <summary>La cabecera X-Aplicacion o lo que se infiere del usuario (Nesto, NestoApp, TiendasNuevaVision, Desconocida).</summary>
        public string Aplicacion { get; set; }
        public string Origen { get; set; }
        public byte? ModoEnviado { get; set; }
        public bool ServirJuntoEnviado { get; set; }
        public byte? ModoSugeridoCliente { get; set; }
        /// <summary>El modo efectivo con el que ha quedado grabado el pedido.</summary>
        public byte ModoGuardado { get; set; }
        /// <summary>Al modificar: el modo efectivo que tenía antes. Al crear: null.</summary>
        public byte? ModoAnterior { get; set; }
        /// <summary>Lo que el servidor sugería con el stock de ese momento (null si no se calculó o falló).</summary>
        public byte? ModoSugeridoServidor { get; set; }
        /// <summary>Los modos permitidos del servidor en ese momento, «1,2,4».</summary>
        public string PermitidosServidor { get; set; }
        public string MotivoServidor { get; set; }

        /// <summary>El servidor no corrigió nada: el modo grabado es el que mandó el cliente (o el cliente no mandó ninguno).</summary>
        public bool CoincideConElEnviado => !ModoEnviado.HasValue || ModoEnviado.Value == ModoGuardado;
    }

    /// <summary>NestoAPI#704: la escritura de <c>PedidosModoServicioOrigen</c>.</summary>
    public interface IRepositorioOrigenModoServicio
    {
        /// <summary>Si ya se ha creado la tabla (Scripts/Issue704_PedidosModoServicioOrigen.sql).</summary>
        bool TablaExiste();
        void Guardar(FilaOrigenModoServicio fila);
    }

    /// <summary>
    /// NestoAPI#704: al crear un pedido (y al modificarlo si cambia el modo efectivo) deja en <c>PedidosModoServicioOrigen</c> una fila
    /// con el modo que mandó el cliente, de dónde salía (<see cref="OrigenesModoServicio"/>), el que se grabó y el que el servidor
    /// sugería en ese momento. Con eso, ante otro «yo puse todo junto» (pedido 929152), se mira la fila y se sabe. Garantías:
    /// <list type="bullet">
    /// <item>No cambia nada de la respuesta: copia lo que necesita en el hilo de la petición y escribe en segundo plano.</item>
    /// <item>Best-effort: nunca lanza. Sin la tabla o con un fallo al insertar, un aviso a ELMAH por proceso y el pedido queda igual.</item>
    /// <item>No depende de ningún interruptor: la sombra del sugeridor (#563) va aparte, apagada salvo parámetro y con lecturas de stock.</item>
    /// </list>
    /// </summary>
    public static class RegistroOrigenModoServicio
    {
        public const string OPERACION_CREAR = "Crear";
        public const string OPERACION_MODIFICAR = "Modificar";

        // Sustituibles en los tests.
        internal static Func<IRepositorioOrigenModoServicio> CrearRepositorio { get; set; } = () => RepositorioOrigenModoServicioSql.DesdeConfiguracion();
        internal static Action<Action> Lanzador { get; set; } = LanzarEnSegundoPlano;
        internal static Action<Exception> Registrador { get; set; } = ex => ElmahHelper.Log(ex, "NestoAPI#704");
        internal static Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        private static int avisadoSinTabla;
        private static int avisadoFallo;

        /// <summary>Para los tests: vuelve a avisar a ELMAH la próxima vez.</summary>
        internal static void ReiniciarAvisos()
        {
            avisadoSinTabla = 0;
            avisadoFallo = 0;
        }

        /// <summary>Lo que manda el cliente sobre el modo, ANTES de que la API lo normalice o lo corrija. Nunca lanza.</summary>
        public static EnvioModoServicio Capturar(PedidoVentaDTO pedido)
        {
            try
            {
                return new EnvioModoServicio
                {
                    ModoEnviado = pedido?.modoServicio,
                    ServirJuntoEnviado = pedido?.servirJunto ?? false,
                    Origen = OrigenesModoServicio.Normalizar(pedido?.OrigenModoServicio),
                    ModoSugeridoCliente = pedido?.ModoServicioSugerido
                };
            }
            catch
            {
                return new EnvioModoServicio();
            }
        }

        /// <summary>
        /// Lanza el registro en segundo plano. Nunca lanza excepciones ni espera a nada. Al modificar, solo si el modo efectivo
        /// cambia (<paramref name="modoAnterior"/> distinto del grabado).
        /// </summary>
        /// <param name="pedido">El pedido ya grabado (su modoServicio es el efectivo con el que ha quedado).</param>
        /// <param name="envio">Lo capturado antes de normalizar (<see cref="Capturar"/>).</param>
        /// <param name="sugerenciaServidor">La sugerencia que calculó el servidor al grabar, o null si no la hubo.</param>
        /// <param name="modoAnterior">Al modificar, el modo efectivo que tenía el pedido; al crear, null.</param>
        public static void Registrar(string operacion, PedidoVentaDTO pedido, EnvioModoServicio envio, SugeridorModoServicio.Sugerencia sugerenciaServidor,
            byte? modoAnterior, string cabeceraAplicacion, string nombrePrincipal)
        {
            try
            {
                FilaOrigenModoServicio fila = Construir(operacion, pedido, envio, sugerenciaServidor, modoAnterior, cabeceraAplicacion, nombrePrincipal, Ahora());
                if (fila == null)
                {
                    return;
                }
                Lanzador(() => Procesar(fila));
            }
            catch (Exception ex)
            {
                Avisar(ref avisadoFallo, new Exception($"[Origen modo de servicio #704] No se ha podido preparar el registro del pedido {pedido?.numero}. " +
                    $"El pedido está grabado igual. {ex.Message}", ex));
            }
        }

        /// <summary>La fila, o null si no hay nada que registrar (sin pedido grabado; al modificar sin cambio de modo). Puro.</summary>
        internal static FilaOrigenModoServicio Construir(string operacion, PedidoVentaDTO pedido, EnvioModoServicio envio,
            SugeridorModoServicio.Sugerencia sugerenciaServidor, byte? modoAnterior, string cabeceraAplicacion, string nombrePrincipal, DateTime ahora)
        {
            if (pedido == null || pedido.numero <= 0)
            {
                return null;
            }
            byte modoGuardado = Constantes.Pedidos.ModosServicio.Efectivo(pedido.modoServicio, pedido.servirJunto);
            if (operacion == OPERACION_MODIFICAR && modoAnterior.HasValue && modoAnterior.Value == modoGuardado)
            {
                return null; // el modo no cambia: no hay nada que explicar
            }
            envio = envio ?? Capturar(pedido);
            return new FilaOrigenModoServicio
            {
                Fecha = ahora,
                Operacion = operacion,
                Empresa = string.IsNullOrWhiteSpace(pedido.empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : pedido.empresa.Trim(),
                Pedido = pedido.numero,
                Cliente = pedido.cliente?.Trim(),
                Contacto = pedido.contacto?.Trim(),
                Usuario = pedido.Usuario?.Trim(),
                Aplicacion = Anomalias.AplicacionesOrigen.Inferir(cabeceraAplicacion, pedido.Usuario, nombrePrincipal),
                Origen = envio.Origen,
                ModoEnviado = envio.ModoEnviado,
                ServirJuntoEnviado = envio.ServirJuntoEnviado,
                ModoSugeridoCliente = envio.ModoSugeridoCliente,
                ModoGuardado = modoGuardado,
                ModoAnterior = modoAnterior,
                ModoSugeridoServidor = sugerenciaServidor?.Modo,
                PermitidosServidor = sugerenciaServidor == null ? null : Lista(sugerenciaServidor.ModosPermitidos),
                MotivoServidor = sugerenciaServidor?.Motivo
            };
        }

        /// <summary>«1,2,4» (vacío si no hay ninguno).</summary>
        internal static string Lista(IEnumerable<byte> modos) =>
            string.Join(",", (modos ?? Enumerable.Empty<byte>()).Distinct().OrderBy(m => m).Select(m => m.ToString()));

        /// <summary>El trabajo (en segundo plano). Devuelve si se guardó (para los tests); nunca lanza.</summary>
        internal static bool Procesar(FilaOrigenModoServicio fila)
        {
            try
            {
                if (fila == null)
                {
                    return false;
                }
                IRepositorioOrigenModoServicio repositorio = CrearRepositorio();
                if (repositorio == null || !repositorio.TablaExiste())
                {
                    Avisar(ref avisadoSinTabla, new Exception("[Origen modo de servicio #704] No existe la tabla PedidosModoServicioOrigen (falta lanzar " +
                        "Scripts/Issue704_PedidosModoServicioOrigen.sql). Los pedidos se graban, pero no se registra de dónde sale su modo de servicio."));
                    return false;
                }
                repositorio.Guardar(fila);
                return true;
            }
            catch (Exception ex)
            {
                Avisar(ref avisadoFallo, new Exception($"[Origen modo de servicio #704] No se ha podido registrar el origen del modo del pedido {fila?.Pedido}. " +
                    $"El pedido está grabado igual. {ex.Message}", ex));
                return false;
            }
        }

        private static void Avisar(ref int bandera, Exception ex)
        {
            try
            {
                if (Interlocked.Exchange(ref bandera, 1) == 0)
                {
                    Registrador(ex);
                }
            }
            catch
            {
                // Ni el aviso puede romper nada
            }
        }

        private static void LanzarEnSegundoPlano(Action trabajo)
        {
            if (HostingEnvironment.IsHosted)
            {
                HostingEnvironment.QueueBackgroundWorkItem(_ => trabajo());
            }
            else
            {
                _ = Task.Run(trabajo);
            }
        }
    }

    /// <summary>
    /// NestoAPI#704: <see cref="IRepositorioOrigenModoServicio"/> con ADO.NET sobre NestoConnection (la tabla no está en el EDMX, como
    /// PedidosOrigenPush #709 y PedidosAnomalias #715), con <c>Enlist=false</c> (#687: nunca se alista en la transacción ambiente).
    /// </summary>
    public class RepositorioOrigenModoServicioSql : IRepositorioOrigenModoServicio
    {
        internal const string NOMBRE_APLICACION = "NestoAPI OrigenModoServicio";
        internal const int TIMEOUT_SEGUNDOS = 10;
        internal static readonly TimeSpan REINTENTO_SIN_TABLA = TimeSpan.FromMinutes(10);

        internal const string SQL_EXISTE = "SELECT CASE WHEN OBJECT_ID('dbo.PedidosModoServicioOrigen', 'U') IS NULL THEN 0 ELSE 1 END";

        internal const string SQL_GUARDAR = @"
INSERT INTO dbo.PedidosModoServicioOrigen (Fecha, Operacion, Empresa, Pedido, Cliente, Contacto, Usuario, Aplicacion, Origen,
    ModoEnviado, ServirJuntoEnviado, ModoSugeridoCliente, ModoGuardado, ModoAnterior, ModoSugeridoServidor, PermitidosServidor, MotivoServidor)
VALUES (@fecha, @operacion, @empresa, @pedido, @cliente, @contacto, @usuario, @aplicacion, @origen,
    @modoEnviado, @servirJuntoEnviado, @modoSugeridoCliente, @modoGuardado, @modoAnterior, @modoSugeridoServidor, @permitidosServidor, @motivoServidor)";

        private static readonly object cerrojo = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;

        private readonly string cadenaConexion;

        public RepositorioOrigenModoServicioSql(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = new SqlConnectionStringBuilder(cadenaConexion) { Enlist = false }.ConnectionString;
        }

        public static RepositorioOrigenModoServicioSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioOrigenModoServicioSql(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        /// <summary>Para los tests: olvida la caché de «existe la tabla».</summary>
        internal static void OlvidarCache()
        {
            lock (cerrojo)
            {
                existe = false;
                ultimaComprobacion = DateTime.MinValue;
            }
        }

        /// <summary>Cuando existe, no se vuelve a preguntar; cuando no, se reintenta cada <see cref="REINTENTO_SIN_TABLA"/>.</summary>
        public bool TablaExiste()
        {
            lock (cerrojo)
            {
                if (existe || DateTime.Now - ultimaComprobacion < REINTENTO_SIN_TABLA)
                {
                    return existe;
                }
            }
            bool hay;
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_EXISTE))
            {
                hay = Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
            lock (cerrojo)
            {
                existe = hay;
                ultimaComprobacion = DateTime.Now;
            }
            return hay;
        }

        public void Guardar(FilaOrigenModoServicio fila)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_GUARDAR))
            {
                comando.Parameters.Add("@fecha", SqlDbType.DateTime).Value = fila.Fecha == default(DateTime) ? DateTime.Now : fila.Fecha;
                Texto(comando, "@operacion", fila.Operacion, 10);
                Texto(comando, "@empresa", fila.Empresa, 3);
                comando.Parameters.Add("@pedido", SqlDbType.Int).Value = fila.Pedido;
                Texto(comando, "@cliente", fila.Cliente, 10);
                Texto(comando, "@contacto", fila.Contacto, 3);
                Texto(comando, "@usuario", fila.Usuario, 50);
                Texto(comando, "@aplicacion", fila.Aplicacion, 20);
                Texto(comando, "@origen", fila.Origen, OrigenesModoServicio.LARGO_MAXIMO);
                TinyInt(comando, "@modoEnviado", fila.ModoEnviado);
                comando.Parameters.Add("@servirJuntoEnviado", SqlDbType.Bit).Value = fila.ServirJuntoEnviado;
                TinyInt(comando, "@modoSugeridoCliente", fila.ModoSugeridoCliente);
                comando.Parameters.Add("@modoGuardado", SqlDbType.TinyInt).Value = fila.ModoGuardado;
                TinyInt(comando, "@modoAnterior", fila.ModoAnterior);
                TinyInt(comando, "@modoSugeridoServidor", fila.ModoSugeridoServidor);
                Texto(comando, "@permitidosServidor", fila.PermitidosServidor, 10);
                Texto(comando, "@motivoServidor", fila.MotivoServidor, 400);
                _ = comando.ExecuteNonQuery();
            }
        }

        private static void Texto(SqlCommand comando, string nombre, string valor, int largo)
        {
            string limpio = valor?.Trim();
            if (!string.IsNullOrEmpty(limpio) && limpio.Length > largo)
            {
                limpio = limpio.Substring(0, largo);
            }
            comando.Parameters.Add(nombre, SqlDbType.VarChar, largo).Value = string.IsNullOrEmpty(limpio) ? (object)DBNull.Value : limpio;
        }

        private static void TinyInt(SqlCommand comando, string nombre, byte? valor) =>
            comando.Parameters.Add(nombre, SqlDbType.TinyInt).Value = valor.HasValue ? (object)valor.Value : DBNull.Value;

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static SqlCommand Comando(SqlConnection conexion, string sql) => new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS };
    }
}
