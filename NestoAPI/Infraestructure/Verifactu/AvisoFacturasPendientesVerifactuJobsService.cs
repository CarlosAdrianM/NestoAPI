using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// NestoAPI#522: recordatorio diario a administración, en la campana de Nesto (buzón + SignalR), mientras
    /// haya facturas pendientes de Verifactu o incorrectas en la AEAT. Al pulsarlo, Nesto abre la ventana de
    /// facturas pendientes de Verifactu (tipo <see cref="TIPO_NOTIFICACION"/>). Sin nada pendiente no avisa.
    /// No cuentan las facturas «sin datos fiscales» (camino viejo #348): no hay nada que administración pueda
    /// hacer con ellas y encenderían la campana todos los días. Siguen saliendo en la ventana.
    /// Destinatarios: los miembros directos del grupo Administración del dominio o, si está configurada, la lista del
    /// parámetro <see cref="CLAVE_USUARIOS"/> (usuarios separados por comas, sin dominio).
    /// Interruptor: <see cref="CLAVE_ACTIVO"/> (true/false; si no está, activo). Además, sin Verifactu
    /// habilitado no hace nada.
    /// </summary>
    public class AvisoFacturasPendientesVerifactuJobsService
    {
        internal const string TIPO_NOTIFICACION = "FacturasPendientesVerifactu";
        internal const string CLAVE_ACTIVO = "Verifactu:AvisoDiarioPendientes";
        internal const string CLAVE_USUARIOS = "Verifactu:UsuariosAvisoPendientes";
        internal const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioFacturasPendientesVerifactu servicioPendientes;
        private readonly IServicioNotificacionesPush servicioNotificaciones;
        private readonly IServicioVerifactu servicioVerifactu;
        private readonly Func<List<string>> usuariosAdministracion;
        private readonly Func<string, string> leerParametro;

        public AvisoFacturasPendientesVerifactuJobsService() : this(null)
        {
        }

        internal AvisoFacturasPendientesVerifactuJobsService(IServicioFacturasPendientesVerifactu servicioPendientes,
            IServicioNotificacionesPush servicioNotificaciones = null, IServicioVerifactu servicioVerifactu = null,
            Func<List<string>> usuariosAdministracion = null, Func<string, string> leerParametro = null)
        {
            this.servicioPendientes = servicioPendientes ?? new ServicioFacturasPendientesVerifactu();
            this.servicioNotificaciones = servicioNotificaciones ?? new ServicioNotificacionesPush();
            this.servicioVerifactu = servicioVerifactu ?? ProveedorVerifactu.Actual;
            this.usuariosAdministracion = usuariosAdministracion
                ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ADMINISTRACION));
            this.leerParametro = leerParametro ?? (clave => ConfigurationManager.AppSettings[clave]);
        }

        /// <summary>Punto de entrada de Hangfire (patrón del resto de jobs).</summary>
        public static async Task Avisar()
        {
            _ = await new AvisoFacturasPendientesVerifactuJobsService().AvisarSiHayPendientes().ConfigureAwait(false);
        }

        /// <summary>Devuelve a cuántos usuarios se ha avisado (0 si no había nada o está apagado).</summary>
        public async Task<int> AvisarSiHayPendientes()
        {
            try
            {
                if (!EstaActivo() || !servicioVerifactu.EstaHabilitado)
                {
                    return 0;
                }
                List<FacturaPendienteVerifactuDTO> pendientes = (await servicioPendientes.Listar().ConfigureAwait(false))
                    .Where(f => f.Situacion != ServicioFacturasPendientesVerifactu.SITUACION_SIN_DATOS_FISCALES)
                    .ToList();
                if (!pendientes.Any())
                {
                    return 0;
                }
                List<string> usuarios = Destinatarios();
                if (!usuarios.Any())
                {
                    ElmahHelper.Log(new Exception($"[Verifactu aviso] Hay {pendientes.Count} facturas pendientes " +
                        "de Verifactu pero no se ha encontrado a quién avisar (grupo Administración vacío o sin acceso al dominio)."));
                    return 0;
                }
                NotificacionPushDTO notificacion = CrearNotificacion(pendientes);
                foreach (string usuario in usuarios)
                {
                    // El buzón ya avisa por SignalR al guardar: la campana se enciende al momento
                    await servicioNotificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion)
                        .ConfigureAwait(false);
                }
                return usuarios.Count;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Verifactu aviso] No se pudo avisar de las facturas pendientes: {ex.Message}", ex));
                return 0;
            }
        }

        private bool EstaActivo()
        {
            string valor = leerParametro(CLAVE_ACTIVO);
            return string.IsNullOrWhiteSpace(valor) || !bool.TryParse(valor.Trim(), out bool activo) || activo;
        }

        /// <summary>La lista del parámetro manda; si no hay, el grupo Administración. Siempre con el dominio.</summary>
        internal List<string> Destinatarios()
        {
            string configurados = leerParametro(CLAVE_USUARIOS);
            IEnumerable<string> usuarios = !string.IsNullOrWhiteSpace(configurados)
                ? configurados.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                : (IEnumerable<string>)(usuariosAdministracion() ?? new List<string>());
            return usuarios
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static NotificacionPushDTO CrearNotificacion(List<FacturaPendienteVerifactuDTO> pendientes)
        {
            int incorrectas = pendientes.Count(f => f.Situacion == ServicioFacturasPendientesVerifactu.SITUACION_INCORRECTA_AEAT);
            int sinRegistrar = pendientes.Count - incorrectas;
            var partes = new List<string>();
            if (sinRegistrar > 0)
            {
                partes.Add(sinRegistrar == 1 ? "1 sin registrar" : $"{sinRegistrar} sin registrar");
            }
            if (incorrectas > 0)
            {
                partes.Add(incorrectas == 1 ? "1 incorrecta en la AEAT" : $"{incorrectas} incorrectas en la AEAT");
            }
            string cuantas = pendientes.Count == 1 ? "Hay 1 factura" : $"Hay {pendientes.Count} facturas";
            return new NotificacionPushDTO
            {
                Titulo = "Facturas pendientes de Verifactu",
                Cuerpo = $"{cuantas} que Verifactu todavía no da por buenas ({string.Join(" y ", partes)}). " +
                    "Pulsa aquí para ver el motivo y reintentar el envío cuando estén corregidas.",
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["cantidad"] = pendientes.Count.ToString()
                }
            };
        }
    }

    /// <summary>
    /// NestoAPI#522: los usuarios (sAMAccountName, sin dominio) que son miembros DIRECTOS de un grupo del dominio.
    /// Sin los grupos anidados a propósito: Dirección está dentro de Administración y el aviso es para
    /// administración (además, al recorrer los anidados el dominio da un error con algún miembro, comprobado
    /// el 28/09/26). Nunca lanza: si falla a mitad, se queda con los leídos; si no hay acceso al dominio,
    /// devuelve la lista vacía. En ambos casos lo deja en ELMAH.
    /// </summary>
    internal static class MiembrosGrupoDominio
    {
        internal static List<string> Leer(string grupo)
        {
            try
            {
                using (var contexto = new System.DirectoryServices.AccountManagement.PrincipalContext(
                    System.DirectoryServices.AccountManagement.ContextType.Domain))
                using (var principal = System.DirectoryServices.AccountManagement.GroupPrincipal.FindByIdentity(contexto, grupo))
                {
                    var usuarios = new List<string>();
                    if (principal == null)
                    {
                        return usuarios;
                    }
                    using (var miembros = principal.GetMembers(false))
                    using (IEnumerator<System.DirectoryServices.AccountManagement.Principal> recorrido = miembros.GetEnumerator())
                    {
                        while (true)
                        {
                            try
                            {
                                if (!recorrido.MoveNext())
                                {
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                ElmahHelper.Log(new Exception($"[Grupo del dominio] Error al recorrer los miembros de '{grupo}' " +
                                    $"(se avisa a los {usuarios.Count} leídos): {ex.Message}", ex));
                                break;
                            }
                            if (recorrido.Current is System.DirectoryServices.AccountManagement.UserPrincipal usuario
                                && usuario.Enabled != false && !string.IsNullOrWhiteSpace(usuario.SamAccountName))
                            {
                                usuarios.Add(usuario.SamAccountName);
                            }
                        }
                    }
                    return usuarios;
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Grupo del dominio] No se pudieron leer los miembros de '{grupo}': {ex.Message}", ex));
                return new List<string>();
            }
        }
    }
}
