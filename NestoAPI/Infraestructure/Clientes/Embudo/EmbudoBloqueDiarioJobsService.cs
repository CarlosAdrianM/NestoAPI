using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Embudo
{
    public enum ResultadoBloqueDiarioEmbudo
    {
        /// <summary>El interruptor no está encendido (sin la fila, tampoco): no se hace nada.</summary>
        Apagado,
        /// <summary>Hoy ya hay un bloque (a mano o de otra pasada): no se lanza otro.</summary>
        YaLanzadoHoy,
        Lanzado,
        /// <summary>Menos candidatos que el tamaño del bloque (pero alguno): se lanza con los que hay, sin aviso.</summary>
        LanzadoIncompleto,
        /// <summary>Ningún candidato: no se lanza nada; solo queda en ELMAH, sin aviso.</summary>
        SinCandidatos,
        /// <summary>Sin la tabla o ha fallado algo: no se lanza y se avisa.</summary>
        Fallo
    }

    /// <summary>
    /// Embudo de Odoo (Carlos 11/11/26): TODOS los días (también sábados, domingos y festivos, a propósito: quiere datos de todos los
    /// días) a las 07:00 lanza el siguiente bloque de <see cref="TAMANO_BLOQUE"/> clientes por el MISMO camino que el lanzamiento a
    /// mano (<see cref="ServicioEmbudoClientes.Republicar"/> con sus valores por defecto), con el nombre correlativo NV-AAAA-MM-NN (NN
    /// vuelve a 01 cada mes). Odoo decide a qué hora salen los correos; aquí solo hace falta que el bloque llegue antes de las 08:00.
    /// <list type="bullet">
    /// <item>Interruptor en BD (los flags de Web.config se pierden al publicar): ParametrosUsuario empresa 1, usuario «(defecto)»,
    /// clave <see cref="PARAMETRO_INTERRUPTOR"/>. Solo con la fila y valor «1» (Scripts/EmbudoBloqueDiario_Interruptor.sql).</item>
    /// <item>Si hoy ya hay un bloque (p. ej. lanzado a mano), no lanza otro.</item>
    /// <item>Sin tabla o si falla la consulta o el lanzamiento: no lanza nada, lo deja en ELMAH y avisa a Dirección en la campana de
    /// Nesto (una vez: el job corre una vez al día y nunca lanza excepción, así que Hangfire no reintenta).</item>
    /// <item>Carlos (11/11/26): con menos de <see cref="TAMANO_BLOQUE"/> candidatos pero alguno, lanza el bloque con los que haya, sin
    /// aviso (agotada la cola entran unos 45 al mes); con ninguno, no lanza y solo lo deja en ELMAH, sin aviso.</item>
    /// </list>
    /// </summary>
    public class EmbudoBloqueDiarioJobsService
    {
        public const string JOB_ID = "embudo-bloque-diario";
        /// <summary>Todos los días a las 7:00 (hora de Madrid).</summary>
        public const string CRON = "0 7 * * *";
        public const string PARAMETRO_INTERRUPTOR = "EmbudoBloqueDiario";
        /// <summary>Carlos (11/11/26): el tamaño del bloque, en BD para subirlo sin publicar. Sin fila o no válido, <see cref="TAMANO_BLOQUE"/>.</summary>
        public const string PARAMETRO_TAMANO = "EmbudoTamanoBloque";
        /// <summary>El tamaño por defecto.</summary>
        public const int TAMANO_BLOQUE = ServicioEmbudoClientes.MAXIMO_POR_BLOQUE;
        /// <summary>Límite de cordura: por encima se recorta a este y se registra.</summary>
        /// <remarks>Carlos (11/11/26): 100, el envío sale por la cuenta de Office 365 de la empresa y no debe pasar de 100 al día.</remarks>
        public const int TAMANO_MAXIMO = 100;
        public const string TIPO_NOTIFICACION = "EmbudoBloqueDiario";
        internal const string USUARIO = "NestoAPI (embudo diario)";
        internal const string USUARIO_DEFECTO = "(defecto)";
        internal const string DOMINIO = "NUEVAVISION\\";

        private readonly IRepositorioClientesEmbudo repositorio;
        private readonly IParametrosEmbudoDiario parametros;
        private readonly Func<List<string>> usuariosDireccion;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Action<Exception> registrar;

        public EmbudoBloqueDiarioJobsService() : this(null)
        {
        }

        internal EmbudoBloqueDiarioJobsService(IRepositorioClientesEmbudo repositorio, IParametrosEmbudoDiario parametros = null,
            Func<List<string>> usuariosDireccion = null, IServicioNotificacionesPush notificaciones = null, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? new RepositorioClientesEmbudoSql();
            this.parametros = parametros ?? new ParametrosEmbudoDiarioEF();
            this.usuariosDireccion = usuariosDireccion ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.DIRECCION));
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>Punto de entrada de Hangfire.</summary>
        public static async Task Lanzar()
        {
            _ = await new EmbudoBloqueDiarioJobsService().LanzarAsync(DateTime.Today).ConfigureAwait(false);
        }

        /// <summary>La zona horaria del job: Madrid (la del servidor si Windows no la tiene).</summary>
        public static TimeZoneInfo ZonaMadrid()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Local;
            }
        }

        /// <summary>«1» en el parámetro <see cref="PARAMETRO_INTERRUPTOR"/>. Sin fila, apagado.</summary>
        internal static bool EstaEncendido(string valor) => valor?.Trim() == "1";

        /// <summary>
        /// El tamaño del bloque a partir del parámetro <see cref="PARAMETRO_TAMANO"/>: sin fila, no numérico o menor que 1,
        /// <see cref="TAMANO_BLOQUE"/>; por encima de <see cref="TAMANO_MAXIMO"/>, se recorta y se registra.
        /// </summary>
        internal static int TamanoBloque(string valor, Action<Exception> registrar = null)
        {
            if (!int.TryParse(valor?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tamano) || tamano < 1)
            {
                if (!string.IsNullOrWhiteSpace(valor))
                {
                    registrar?.Invoke(new Exception($"[Embudo diario] El parámetro {PARAMETRO_TAMANO} vale «{valor.Trim()}», que no es un tamaño válido: se usa {TAMANO_BLOQUE}."));
                }
                return TAMANO_BLOQUE;
            }
            if (tamano > TAMANO_MAXIMO)
            {
                registrar?.Invoke(new Exception($"[Embudo diario] El parámetro {PARAMETRO_TAMANO} vale {tamano}: se recorta a {TAMANO_MAXIMO}."));
                return TAMANO_MAXIMO;
            }
            return tamano;
        }

        /// <summary>«NV-2026-11-03»: el siguiente del mes de <paramref name="hoy"/> a los que ya hay (el número más alto + 1).</summary>
        internal static string SiguienteBloque(DateTime hoy, IEnumerable<string> existentes)
        {
            string prefijo = $"NV-{hoy:yyyy}-{hoy:MM}-";
            int ultimo = (existentes ?? Enumerable.Empty<string>())
                .Select(b => b?.Trim())
                .Where(b => b != null && b.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
                .Select(b => int.TryParse(b.Substring(prefijo.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();
            return prefijo + (ultimo + 1).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Nunca lanza.</summary>
        public async Task<ResultadoBloqueDiarioEmbudo> LanzarAsync(DateTime hoy)
        {
            hoy = hoy.Date;
            bool encendido;
            int tamano;
            try
            {
                encendido = EstaEncendido(parametros.Leer(PARAMETRO_INTERRUPTOR));
                tamano = encendido ? TamanoBloque(parametros.Leer(PARAMETRO_TAMANO), Registrar) : TAMANO_BLOQUE;
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[Embudo diario] No se han podido leer los parámetros {PARAMETRO_INTERRUPTOR}/{PARAMETRO_TAMANO}: no se lanza. {ex.Message}", ex));
                return ResultadoBloqueDiarioEmbudo.Apagado;
            }
            if (!encendido)
            {
                return ResultadoBloqueDiarioEmbudo.Apagado;
            }
            string bloque = null;
            try
            {
                if (!await repositorio.HayTabla().ConfigureAwait(false))
                {
                    await NoLanzado(hoy, null, ServicioEmbudoClientes.MensajeSinTabla).ConfigureAwait(false);
                    return ResultadoBloqueDiarioEmbudo.Fallo;
                }
                List<BloqueEmbudo> bloques = await repositorio.LeerBloquesDesde(new DateTime(hoy.Year, hoy.Month, 1)).ConfigureAwait(false)
                    ?? new List<BloqueEmbudo>();
                if (bloques.Any(b => b != null && b.Fecha.Date == hoy
                    && !string.Equals(b.Bloque?.Trim(), RepositorioClientesEmbudoSql.BLOQUE_SIEMBRA, StringComparison.OrdinalIgnoreCase)))
                {
                    return ResultadoBloqueDiarioEmbudo.YaLanzadoHoy;
                }
                bloque = SiguienteBloque(hoy, bloques.Select(b => b?.Bloque));

                // El mismo camino que el lanzamiento a mano: primero el ensayo (cuántos entrarían) y, si llena el bloque, de verdad
                var servicio = new ServicioEmbudoClientes(repositorio);
                ResultadoRepublicarEmbudo ensayo = await servicio.Republicar(
                    new PeticionRepublicarEmbudo { Bloque = bloque, Maximo = tamano, Ensayo = true }, USUARIO, TAMANO_MAXIMO).ConfigureAwait(false);
                if (ensayo.ClientesEncolados == 0)
                {
                    // Carlos (11/11/26): cola agotada un día cualquiera: solo el registro, sin aviso
                    Registrar(new Exception($"[Embudo diario] Hoy ({hoy:dd/MM/yyyy}) no hay ningún cliente nuevo para el embudo: no se lanza el bloque {bloque}."));
                    return ResultadoBloqueDiarioEmbudo.SinCandidatos;
                }
                // Con menos que el tamaño (cuando se agote la cola entran unos 45 al mes), se lanza con los que haya, sin aviso
                ResultadoRepublicarEmbudo lanzado = await servicio.Republicar(
                    new PeticionRepublicarEmbudo { Bloque = bloque, Maximo = tamano, Ensayo = false }, USUARIO, TAMANO_MAXIMO).ConfigureAwait(false);
                return lanzado.ClientesEncolados >= tamano ? ResultadoBloqueDiarioEmbudo.Lanzado : ResultadoBloqueDiarioEmbudo.LanzadoIncompleto;
            }
            catch (Exception ex)
            {
                await NoLanzado(hoy, bloque, $"ha fallado: {ex.Message}", ex).ConfigureAwait(false);
                return ResultadoBloqueDiarioEmbudo.Fallo;
            }
        }

        private async Task NoLanzado(DateTime hoy, string bloque, string motivo, Exception ex = null)
        {
            string texto = $"[Embudo diario] No se ha lanzado el bloque {bloque ?? "de hoy"} del embudo de Odoo del {hoy:dd/MM/yyyy}: {motivo}";
            Registrar(ex == null ? new Exception(texto) : new Exception(texto, ex));
            try
            {
                NotificacionPushDTO notificacion = CrearNotificacion(hoy, bloque, motivo);
                List<string> usuarios = (usuariosDireccion() ?? new List<string>())
                    .Select(u => u?.Trim())
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (string usuario in usuarios)
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
            }
            catch (Exception exAviso)
            {
                Registrar(new Exception($"[Embudo diario] No se ha podido avisar a Dirección: {exAviso.Message}", exAviso));
            }
        }

        internal static NotificacionPushDTO CrearNotificacion(DateTime hoy, string bloque, string motivo) => new NotificacionPushDTO
        {
            Titulo = "Hoy no ha salido el bloque del embudo",
            Cuerpo = $"El bloque {bloque ?? "de hoy"} del embudo de clientes de Odoo no se ha lanzado: {motivo?.Trim().TrimEnd('.')}. " +
                "No se ha enviado nada a Odoo. Mañana a las 7:00 se vuelve a intentar.",
            Tipo = TIPO_NOTIFICACION,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["fecha"] = hoy.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["bloque"] = bloque ?? string.Empty
            }
        };

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(ex);
            }
            catch
            {
                // El registro nunca tumba el job
            }
        }
    }

    /// <summary>
    /// Embudo diario: los parámetros globales del job (ParametrosUsuario, empresa 1, usuario «(defecto)»), en BD para cambiarlos sin
    /// publicar (los flags del Web.config se pierden al publicar).
    /// </summary>
    public interface IParametrosEmbudoDiario
    {
        /// <summary>El valor (sin el relleno del char) o null si no hay fila.</summary>
        string Leer(string clave);
        /// <summary>Crea la fila si no existe o la cambia; deja quién y cuándo (Usuario2, Fecha Modificación) y una fila en Modificaciones.</summary>
        Task Escribir(string clave, string valor, string usuario);
    }

    public class ParametrosEmbudoDiarioEF : IParametrosEmbudoDiario
    {
        internal const string USUARIO_DEFECTO = "(defecto)";

        public string Leer(string clave)
        {
            using (var db = new NVEntities())
            {
                string valor = db.ParametrosUsuario
                    .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == USUARIO_DEFECTO && p.Clave == clave)
                    .Select(p => p.Valor)
                    .FirstOrDefault();
                return valor?.Trim();
            }
        }

        public async Task Escribir(string clave, string valor, string usuario)
        {
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario) ?? "NestoAPI";
            using (var db = new NVEntities())
            {
                ParametroUsuario fila = db.ParametrosUsuario
                    .FirstOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == USUARIO_DEFECTO && p.Clave == clave);
                string anterior = fila?.Valor?.Trim();
                if (fila == null)
                {
                    _ = db.ParametrosUsuario.Add(new ParametroUsuario
                    {
                        Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Usuario = USUARIO_DEFECTO,
                        Clave = clave,
                        Valor = valor,
                        Usuario2 = Recortar(quien, 30),
                        Fecha_Modificación = DateTime.Now
                    });
                }
                else
                {
                    fila.Valor = valor;
                    fila.Usuario2 = Recortar(quien, 30);
                    fila.Fecha_Modificación = DateTime.Now;
                }
                _ = db.Modificaciones.Add(new Modificacion
                {
                    Tabla = "ParametrosUsuario",
                    Anterior = $"{clave} (defecto) = {anterior ?? "(sin fila)"}",
                    Nuevo = $"{clave} (defecto) = {valor}",
                    Usuario = Recortar(quien, 50)
                });
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private static string Recortar(string texto, int largo) => texto == null || texto.Length <= largo ? texto : texto.Substring(0, largo);
    }

    /// <summary>GET/PUT api/Clientes/Embudo/Configuracion.</summary>
    public class ConfiguracionEmbudoDiarioDTO
    {
        public bool Encendido { get; set; }
        public int TamanoBloque { get; set; }
        public string UltimoBloque { get; set; }
        public DateTime? FechaUltimoBloque { get; set; }
    }

    public class CambioConfiguracionEmbudoDiarioDTO
    {
        public bool? Encendido { get; set; }
        public int? TamanoBloque { get; set; }
    }

    /// <summary>
    /// Embudo diario (Carlos 11/11/26): el interruptor y el tamaño del bloque se ven y se cambian por la API (no con SQL a mano). Los
    /// candidatos pendientes no se dan: contarlos es la consulta pesada del embudo.
    /// </summary>
    public class ServicioConfiguracionEmbudoDiario
    {
        private readonly IParametrosEmbudoDiario parametros;
        private readonly IRepositorioClientesEmbudo repositorio;

        public ServicioConfiguracionEmbudoDiario(IParametrosEmbudoDiario parametros, IRepositorioClientesEmbudo repositorio)
        {
            this.parametros = parametros ?? throw new ArgumentNullException(nameof(parametros));
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<ConfiguracionEmbudoDiarioDTO> Leer()
        {
            var configuracion = new ConfiguracionEmbudoDiarioDTO
            {
                Encendido = EmbudoBloqueDiarioJobsService.EstaEncendido(parametros.Leer(EmbudoBloqueDiarioJobsService.PARAMETRO_INTERRUPTOR)),
                TamanoBloque = EmbudoBloqueDiarioJobsService.TamanoBloque(parametros.Leer(EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO))
            };
            if (await repositorio.HayTabla().ConfigureAwait(false))
            {
                BloqueEmbudo ultimo = (await repositorio.LeerBloquesDesde(new DateTime(2000, 1, 1)).ConfigureAwait(false) ?? new List<BloqueEmbudo>())
                    .Where(b => b != null && !string.Equals(b.Bloque?.Trim(), RepositorioClientesEmbudoSql.BLOQUE_SIEMBRA, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(b => b.Fecha)
                    .FirstOrDefault();
                configuracion.UltimoBloque = ultimo?.Bloque?.Trim();
                configuracion.FechaUltimoBloque = ultimo?.Fecha;
            }
            return configuracion;
        }

        /// <exception cref="PeticionEmbudoNoValidaException">Sin nada que cambiar o tamaño fuera de 1..<see cref="EmbudoBloqueDiarioJobsService.TAMANO_MAXIMO"/>.</exception>
        public async Task<ConfiguracionEmbudoDiarioDTO> Cambiar(CambioConfiguracionEmbudoDiarioDTO cambio, string usuario)
        {
            if (cambio == null || (!cambio.Encendido.HasValue && !cambio.TamanoBloque.HasValue))
            {
                throw new PeticionEmbudoNoValidaException("Indique Encendido o TamanoBloque.");
            }
            if (cambio.TamanoBloque.HasValue && (cambio.TamanoBloque.Value < 1 || cambio.TamanoBloque.Value > EmbudoBloqueDiarioJobsService.TAMANO_MAXIMO))
            {
                throw new PeticionEmbudoNoValidaException($"El tamaño del bloque tiene que estar entre 1 y {EmbudoBloqueDiarioJobsService.TAMANO_MAXIMO}.");
            }
            if (cambio.Encendido.HasValue)
            {
                await parametros.Escribir(EmbudoBloqueDiarioJobsService.PARAMETRO_INTERRUPTOR, cambio.Encendido.Value ? "1" : "0", usuario).ConfigureAwait(false);
            }
            if (cambio.TamanoBloque.HasValue)
            {
                await parametros.Escribir(EmbudoBloqueDiarioJobsService.PARAMETRO_TAMANO,
                    cambio.TamanoBloque.Value.ToString(CultureInfo.InvariantCulture), usuario).ConfigureAwait(false);
            }
            return await Leer().ConfigureAwait(false);
        }
    }
}
