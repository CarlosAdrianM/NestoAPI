using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Origenes
{
    /// <summary>NestoAPI#623: a quién se avisa de un lead sin llamar (sustituible en los tests).</summary>
    public interface IDestinatariosLeadsSinLlamar
    {
        /// <summary>Los jefes de ventas vigentes del vendedor (EquiposVentas.Superior). Vacío si no tiene.</summary>
        List<string> JefesDe(string empresa, string vendedor);
        /// <summary>Usuarios (sin dominio) con esos vendedores en ParametrosUsuario (clave «Vendedor»), sin «(defecto)».</summary>
        List<string> UsuariosDeVendedores(IEnumerable<string> vendedores);
        /// <summary>Los usuarios del grupo Dirección (el respaldo si el vendedor no tiene jefe).</summary>
        List<string> UsuariosDireccion();
    }

    public class DestinatariosLeadsSinLlamar : IDestinatariosLeadsSinLlamar
    {
        private const string CLAVE_VENDEDOR = "Vendedor";
        private const string USUARIO_DEFECTO = "(defecto)";

        public List<string> JefesDe(string empresa, string vendedor)
        {
            DateTime hoy = DateTime.Today;
            using (var db = new NVEntities())
            {
                return db.EquiposVentas
                    .Where(e => e.Empresa == empresa && e.Vendedor == vendedor
                        && (e.FechaDesde == null || e.FechaDesde <= hoy) && (e.FechaHasta == null || e.FechaHasta >= hoy))
                    .Select(e => e.Superior)
                    .ToList()
                    .Where(j => !string.IsNullOrWhiteSpace(j))
                    .Select(j => j.Trim())
                    .Where(j => !j.Equals(vendedor?.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public List<string> UsuariosDeVendedores(IEnumerable<string> vendedores)
        {
            List<string> codigos = (vendedores ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
            if (!codigos.Any())
            {
                return new List<string>();
            }
            using (var db = new NVEntities())
            {
                string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
                return db.ParametrosUsuario
                    .Where(p => p.Empresa == empresa && p.Clave == CLAVE_VENDEDOR && p.Valor != null)
                    .Select(p => new { p.Usuario, p.Valor })
                    .ToList()
                    .Where(p => codigos.Contains(p.Valor.Trim(), StringComparer.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(p.Usuario) && p.Usuario.Trim() != USUARIO_DEFECTO)
                    .Select(p => p.Usuario.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public List<string> UsuariosDireccion() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.DIRECCION);
    }

    /// <summary>
    /// NestoAPI#623 (corte 5): job <c>leads-sin-llamar</c>, cada hora de 8 a 20 de lunes a viernes (los festivos no hace nada). Para
    /// cada lead de campaña con el plazo vencido (<see cref="ReglasLeadsCampana.PlazoLlamadaLead"/>), sin rapport desde su origen
    /// y sin aviso todavía (FechaAvisoRetraso null): aviso en la campana al vendedor del cliente y a su jefe de ventas
    /// (EquiposVentas; si no tiene, a Dirección) —buzón de Nesto de sus usuarios y push de NestoApp a los vendedores— y marca
    /// FechaAvisoRetraso para no repetir.
    /// </summary>
    public class LeadsSinLlamarJobsService
    {
        public const string JOB_ID = "leads-sin-llamar";
        /// <summary>A en punto, de 8 a 20, de lunes a viernes.</summary>
        public const string CRON = "0 8-20 * * 1-5";
        public const string TIPO_NOTIFICACION = "LeadSinLlamar";
        internal const string DOMINIO = "NUEVAVISION\\";

        private readonly IRepositorioOrigenesCliente repositorio;
        private readonly IDestinatariosLeadsSinLlamar destinatarios;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<DateTime, bool> esLaborable;

        public LeadsSinLlamarJobsService() : this(null)
        {
        }

        internal LeadsSinLlamarJobsService(IRepositorioOrigenesCliente repositorio, IDestinatariosLeadsSinLlamar destinatarios = null,
            IServicioNotificacionesPush notificaciones = null, Func<DateTime, bool> esLaborable = null)
        {
            this.repositorio = repositorio ?? new RepositorioOrigenesClienteSql();
            this.destinatarios = destinatarios ?? new DestinatariosLeadsSinLlamar();
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.esLaborable = esLaborable ?? ReglasLeadsCampana.Laborable(GestorFestivos.EsFestivo, Constantes.Almacenes.ALGETE);
        }

        /// <summary>Punto de entrada de Hangfire.</summary>
        public static async Task Avisar()
        {
            _ = await new LeadsSinLlamarJobsService().AvisarAsync(DateTime.Now).ConfigureAwait(false);
        }

        /// <summary>De cuántos leads se ha avisado. Nunca lanza (lo que falla, a ELMAH; ese lead se reintenta la hora siguiente).</summary>
        public async Task<int> AvisarAsync(DateTime ahora)
        {
            if (!esLaborable(ahora.Date))
            {
                return 0;
            }
            List<LeadCampana> leads;
            try
            {
                leads = await repositorio.LeerLeads(null).ConfigureAwait(false) ?? new List<LeadCampana>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Leads #623] No se han podido leer los leads de campaña: {ex.Message}", ex));
                return 0;
            }
            int avisados = 0;
            foreach (LeadCampana lead in leads.Where(l => HayQueAvisar(l, ahora, esLaborable)))
            {
                try
                {
                    await AvisarDe(lead).ConfigureAwait(false);
                    await repositorio.MarcarAvisoRetraso(lead.Empresa?.Trim(), lead.Cliente?.Trim(), ahora).ConfigureAwait(false);
                    avisados++;
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Leads #623] No se ha podido avisar del lead {lead.Cliente?.Trim()} sin llamar: {ex.Message}", ex));
                }
            }
            return avisados;
        }

        /// <summary>Plazo vencido, sin rapport desde su origen y sin aviso todavía.</summary>
        internal static bool HayQueAvisar(LeadCampana lead, DateTime ahora, Func<DateTime, bool> esLaborable) =>
            lead != null && !lead.FechaAvisoRetraso.HasValue && ReglasLeadsCampana.PendienteDeLlamar(lead, ahora)
            && ahora > ReglasLeadsCampana.PlazoLlamadaLead(lead.FechaOrigen, lead.HorasParaLlamar, esLaborable);

        private async Task AvisarDe(LeadCampana lead)
        {
            string empresa = string.IsNullOrWhiteSpace(lead.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : lead.Empresa.Trim();
            string vendedor = lead.Vendedor?.Trim();
            var vendedores = new List<string>();
            if (!string.IsNullOrWhiteSpace(vendedor))
            {
                vendedores.Add(vendedor);
            }
            List<string> jefes = string.IsNullOrWhiteSpace(vendedor) ? new List<string>() : destinatarios.JefesDe(empresa, vendedor) ?? new List<string>();
            vendedores.AddRange(jefes.Where(j => !vendedores.Contains(j, StringComparer.OrdinalIgnoreCase)));

            List<string> usuarios = destinatarios.UsuariosDeVendedores(vendedores) ?? new List<string>();
            if (!jefes.Any())
            {
                // Sin jefe de ventas modelado: a Dirección.
                usuarios.AddRange(destinatarios.UsuariosDireccion() ?? new List<string>());
            }
            NotificacionPushDTO notificacion = CrearNotificacion(lead);
            foreach (string usuario in usuarios
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim().Contains("\\") ? u.Trim() : DOMINIO + u.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            // Los vendedores casi no usan Nesto: la push les llega a NestoApp.
            foreach (string codigo in vendedores)
            {
                _ = await notificaciones.EnviarAVendedor(empresa, codigo, notificacion).ConfigureAwait(false);
            }
        }

        internal static NotificacionPushDTO CrearNotificacion(LeadCampana lead) => new NotificacionPushDTO
        {
            Titulo = $"{ReglasLeadsCampana.Etiqueta(lead.Origen)} sin llamar",
            Cuerpo = ReglasLeadsCampana.TextoAvisoRetraso(lead),
            Tipo = TIPO_NOTIFICACION,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["empresa"] = lead.Empresa?.Trim() ?? string.Empty,
                ["cliente"] = lead.Cliente?.Trim() ?? string.Empty,
                ["contacto"] = lead.Contacto?.Trim() ?? string.Empty,
                ["origen"] = lead.Origen?.Trim() ?? string.Empty
            }
        };
    }
}
