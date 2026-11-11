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
        /// <summary>Menos candidatos que el tamaño del bloque (o ninguno): no se lanza y se avisa.</summary>
        Incompleto,
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
    /// <item>Con menos de <see cref="TAMANO_BLOQUE"/> candidatos, ninguno, sin tabla o si falla: no lanza nada, lo deja en ELMAH y
    /// avisa a Dirección en la campana de Nesto (una vez: el job corre una vez al día y nunca lanza excepción, así que Hangfire no reintenta).</item>
    /// </list>
    /// </summary>
    public class EmbudoBloqueDiarioJobsService
    {
        public const string JOB_ID = "embudo-bloque-diario";
        /// <summary>Todos los días a las 7:00 (hora de Madrid).</summary>
        public const string CRON = "0 7 * * *";
        public const string PARAMETRO_INTERRUPTOR = "EmbudoBloqueDiario";
        public const int TAMANO_BLOQUE = ServicioEmbudoClientes.MAXIMO_POR_BLOQUE;
        public const string TIPO_NOTIFICACION = "EmbudoBloqueDiario";
        internal const string USUARIO = "NestoAPI (embudo diario)";
        internal const string USUARIO_DEFECTO = "(defecto)";
        internal const string DOMINIO = "NUEVAVISION\\";

        private readonly IRepositorioClientesEmbudo repositorio;
        private readonly Func<bool> encendido;
        private readonly Func<List<string>> usuariosDireccion;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Action<Exception> registrar;

        public EmbudoBloqueDiarioJobsService() : this(null)
        {
        }

        internal EmbudoBloqueDiarioJobsService(IRepositorioClientesEmbudo repositorio, Func<bool> encendido = null,
            Func<List<string>> usuariosDireccion = null, IServicioNotificacionesPush notificaciones = null, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? new RepositorioClientesEmbudoSql();
            this.encendido = encendido ?? InterruptorEncendido;
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

        /// <summary>«1» en ParametrosUsuario (empresa 1, «(defecto)», <see cref="PARAMETRO_INTERRUPTOR"/>). Sin fila o si falla, apagado.</summary>
        internal static bool InterruptorEncendido()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    string valor = db.ParametrosUsuario
                        .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == USUARIO_DEFECTO && p.Clave == PARAMETRO_INTERRUPTOR)
                        .Select(p => p.Valor)
                        .FirstOrDefault();
                    return valor?.Trim() == "1";
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Embudo diario] No se ha podido leer el interruptor {PARAMETRO_INTERRUPTOR}: no se lanza. {ex.Message}", ex));
                return false;
            }
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
            if (!encendido())
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
                    new PeticionRepublicarEmbudo { Bloque = bloque, Maximo = TAMANO_BLOQUE, Ensayo = true }, USUARIO).ConfigureAwait(false);
                if (ensayo.ClientesEncolados < TAMANO_BLOQUE)
                {
                    await NoLanzado(hoy, bloque, ensayo.ClientesEncolados == 0
                        ? "no queda ningún cliente que cumpla las reglas del embudo"
                        : $"solo quedan {ensayo.ClientesEncolados} clientes que cumplen las reglas del embudo (el bloque es de {TAMANO_BLOQUE})").ConfigureAwait(false);
                    return ResultadoBloqueDiarioEmbudo.Incompleto;
                }
                ResultadoRepublicarEmbudo lanzado = await servicio.Republicar(
                    new PeticionRepublicarEmbudo { Bloque = bloque, Maximo = TAMANO_BLOQUE, Ensayo = false }, USUARIO).ConfigureAwait(false);
                if (lanzado.ClientesEncolados < TAMANO_BLOQUE)
                {
                    // Otro los ha metido entre el ensayo y el lanzamiento: el bloque sale con los que han entrado
                    Registrar(new Exception($"[Embudo diario] El bloque {bloque} ha salido con {lanzado.ClientesEncolados} clientes en vez de {TAMANO_BLOQUE}."));
                }
                return ResultadoBloqueDiarioEmbudo.Lanzado;
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
}
