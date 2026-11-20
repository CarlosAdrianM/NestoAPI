using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Origenes
{
    /// <summary>
    /// NestoAPI#623 (Carlos 09/11/26): a quién se avisa de un lead. «En todo lo que entre de Madrid, Toledo o Guadalajara lo
    /// principal es que vayan Alberto y Dirección en copia»: en esas provincias (por el código postal del cliente), además del
    /// vendedor (y, en el recordatorio de las 24 h, su jefe), siempre Alberto Sancho (los usuarios con «Vendedor» = ASH en
    /// ParametrosUsuario) y Dirección. El resto de provincias, como siempre.
    /// </summary>
    public static class ReglasAvisosLeads
    {
        /// <summary>Madrid, Toledo y Guadalajara (las dos primeras cifras del código postal).</summary>
        public static readonly IReadOnlyList<string> ProvinciasConJefeYDireccion = new[] { "28", "45", "19" };
        /// <summary>Alberto Sancho, jefe de la zona centro.</summary>
        public const string VENDEDOR_JEFE_ZONA_CENTRO = "ASH";
        /// <summary>Su nombre, para decirle al vendedor del cliente quién gestiona el lead.</summary>
        public const string NOMBRE_JEFE_ZONA_CENTRO = "Alberto Sancho";
        internal const string DOMINIO = "NUEVAVISION\\";

        /// <summary>Lo que se añade al aviso de quien NO gestiona el lead.</summary>
        public const string TEXTO_LO_GESTIONA = "Lo gestiona " + NOMBRE_JEFE_ZONA_CENTRO + ": no hace falta que usted le llame.";

        /// <summary>
        /// Carlos 20/11/26 (lead 42157, vendedor JE): los leads cuyo vendedor es del equipo de Alberto Sancho (EquiposVenta vigente
        /// con Superior ASH, o el propio ASH) los GESTIONA Alberto: a él se le pide llamar y al vendedor del cliente (y a Dirección)
        /// se le dice que lo gestiona él. La misma regla que el responsable del embudo (ReglasResponsableEmbudo).
        /// </summary>
        public static bool LoGestionaElJefeDeZonaCentro(string vendedor, IEnumerable<string> jefesDelVendedor) =>
            string.Equals(vendedor?.Trim(), VENDEDOR_JEFE_ZONA_CENTRO, StringComparison.OrdinalIgnoreCase)
            || (jefesDelVendedor ?? Enumerable.Empty<string>()).Any(j => string.Equals(j?.Trim(), VENDEDOR_JEFE_ZONA_CENTRO, StringComparison.OrdinalIgnoreCase));

        private static List<string> JefesVigentes(IDestinatariosLeadsSinLlamar destinatarios, string empresa, string vendedor)
        {
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                return new List<string>();
            }
            try
            {
                return destinatarios.JefesDe(empresa, vendedor) ?? new List<string>();
            }
            catch (Exception)
            {
                // Sin poder leer el equipo, el aviso sale como siempre (no se pierde un lead por esto)
                return new List<string>();
            }
        }

        /// <summary>Código postal de 5 cifras de Madrid, Toledo o Guadalajara.</summary>
        public static bool EsZonaConJefeYDireccion(string codigoPostal)
        {
            string cp = codigoPostal?.Trim();
            return cp != null && cp.Length == 5 && cp.All(char.IsDigit) && ProvinciasConJefeYDireccion.Contains(cp.Substring(0, 2));
        }

        /// <summary>
        /// Los vendedores (push de NestoApp) y los usuarios (buzón de Nesto, con dominio, sin repetir) a los que se avisa. Con
        /// <paramref name="conJefes"/> (recordatorio de las 24 h), también los jefes del vendedor (EquiposVentas) y, si no tiene,
        /// Dirección. En la zona centro, siempre Alberto y Dirección.
        /// </summary>
        public static DestinatariosAvisoLead Destinatarios(IDestinatariosLeadsSinLlamar destinatarios, string empresa, string vendedor,
            string codigoPostal, bool conJefes)
        {
            vendedor = vendedor?.Trim();
            var vendedores = new List<string>();
            if (!string.IsNullOrWhiteSpace(vendedor))
            {
                vendedores.Add(vendedor);
            }
            List<string> jefesVigentes = JefesVigentes(destinatarios, empresa, vendedor);
            List<string> jefes = conJefes ? jefesVigentes : new List<string>();
            Anadir(vendedores, jefes);
            bool zonaCentro = EsZonaConJefeYDireccion(codigoPostal);
            bool loGestionaAlberto = LoGestionaElJefeDeZonaCentro(vendedor, jefesVigentes);
            if (zonaCentro || loGestionaAlberto)
            {
                Anadir(vendedores, new[] { VENDEDOR_JEFE_ZONA_CENTRO });
            }

            List<string> usuarios = destinatarios.UsuariosDeVendedores(vendedores) ?? new List<string>();
            if (zonaCentro || (conJefes && !jefes.Any()))
            {
                // Zona centro: Dirección en copia siempre. Fuera, solo si el vendedor no tiene jefe de ventas modelado.
                usuarios.AddRange(destinatarios.UsuariosDireccion() ?? new List<string>());
            }
            return new DestinatariosAvisoLead
            {
                Vendedores = vendedores,
                Usuarios = ConDominio(usuarios),
                Gestor = loGestionaAlberto ? VENDEDOR_JEFE_ZONA_CENTRO : null,
                UsuariosGestor = loGestionaAlberto
                    ? ConDominio(destinatarios.UsuariosDeVendedores(new[] { VENDEDOR_JEFE_ZONA_CENTRO }) ?? new List<string>())
                    : new List<string>()
            };
        }

        private static List<string> ConDominio(IEnumerable<string> usuarios) => usuarios
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim().Contains("\\") ? u.Trim() : DOMINIO + u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        private static void Anadir(List<string> vendedores, IEnumerable<string> nuevos)
        {
            foreach (string v in nuevos.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()))
            {
                if (!vendedores.Contains(v, StringComparer.OrdinalIgnoreCase))
                {
                    vendedores.Add(v);
                }
            }
        }

        /// <summary>Buzón de Nesto a cada usuario y push de NestoApp a cada vendedor (los vendedores casi no usan Nesto).</summary>
        /// <param name="siNoGestiona">Con gestor (<see cref="DestinatariosAvisoLead.Gestor"/>), lo que reciben los demás: «Lo gestiona Alberto Sancho…».</param>
        public static async Task Enviar(IServicioNotificacionesPush notificaciones, string empresa, DestinatariosAvisoLead a, NotificacionPushDTO notificacion,
            NotificacionPushDTO siNoGestiona = null)
        {
            bool hayGestor = !string.IsNullOrWhiteSpace(a.Gestor) && siNoGestiona != null;
            foreach (string usuario in a.Usuarios)
            {
                bool gestiona = !hayGestor || (a.UsuariosGestor ?? new List<string>()).Contains(usuario, StringComparer.OrdinalIgnoreCase);
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, gestiona ? notificacion : siNoGestiona).ConfigureAwait(false);
            }
            foreach (string codigo in a.Vendedores)
            {
                bool gestiona = !hayGestor || string.Equals(codigo?.Trim(), a.Gestor.Trim(), StringComparison.OrdinalIgnoreCase);
                _ = await notificaciones.EnviarAVendedor(empresa, codigo, gestiona ? notificacion : siNoGestiona).ConfigureAwait(false);
            }
        }
    }

    public class DestinatariosAvisoLead
    {
        /// <summary>Códigos de vendedor (push de NestoApp).</summary>
        public List<string> Vendedores { get; set; } = new List<string>();
        /// <summary>Usuarios con dominio, sin repetir (buzón de Nesto).</summary>
        public List<string> Usuarios { get; set; } = new List<string>();
        /// <summary>El vendedor que GESTIONA el lead cuando no es el del cliente (hoy, Alberto Sancho para su equipo); null = el del cliente.</summary>
        public string Gestor { get; set; }
        /// <summary>Los usuarios de Nesto de ese gestor (con dominio): a ellos se les pide llamar; a los demás se les informa.</summary>
        public List<string> UsuariosGestor { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#623: el lead que acaba de entrar (cliente creado o cliente que ya existía y se queda con el origen).</summary>
    public class LeadNuevoAviso
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Poblacion { get; set; }
        public string CodigoPostal { get; set; }
        public string Vendedor { get; set; }
        public string Origen { get; set; }
    }

    /// <summary>NestoAPI#623: avisa de un lead nuevo (sustituible en los tests).</summary>
    public interface IAvisadorLeadsNuevos
    {
        Task Avisar(LeadNuevoAviso lead);
    }

    /// <summary>
    /// NestoAPI#623 (Carlos 09/11/26): aviso en la campana de Nesto y push de NestoApp en cuanto el importador da de alta un lead
    /// (o le pone el origen de la campaña a un cliente que ya existía): «Nuevo lead de Harmonia 09/26: CENTRO X (MADRID), cliente
    /// 12345. Llámele hoy.» Sin teléfono ni correo en el texto. Destinatarios: <see cref="ReglasAvisosLeads.Destinatarios"/>.
    /// </summary>
    public class AvisadorLeadsNuevos : IAvisadorLeadsNuevos
    {
        public const string TIPO_NOTIFICACION = "LeadNuevo";

        private readonly IDestinatariosLeadsSinLlamar destinatarios;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly IRepositorioOrigenesCliente origenes;

        public AvisadorLeadsNuevos(IDestinatariosLeadsSinLlamar destinatarios, IServicioNotificacionesPush notificaciones, IRepositorioOrigenesCliente origenes)
        {
            this.destinatarios = destinatarios;
            this.notificaciones = notificaciones;
            this.origenes = origenes;
        }

        public static AvisadorLeadsNuevos DeProduccion() =>
            new AvisadorLeadsNuevos(new DestinatariosLeadsSinLlamar(), new ServicioNotificacionesPush(), new RepositorioOrigenesClienteSql());

        public async Task Avisar(LeadNuevoAviso lead)
        {
            if (lead == null || string.IsNullOrWhiteSpace(lead.Cliente))
            {
                return;
            }
            string empresa = string.IsNullOrWhiteSpace(lead.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : lead.Empresa.Trim();
            string campana = await Campana(lead.Origen).ConfigureAwait(false);
            DestinatariosAvisoLead a = ReglasAvisosLeads.Destinatarios(destinatarios, empresa, lead.Vendedor, lead.CodigoPostal, conJefes: false);
            NotificacionPushDTO informar = CrearNotificacion(lead, empresa, campana);
            informar.Cuerpo = TextoSiNoGestiona(lead, campana);
            await ReglasAvisosLeads.Enviar(notificaciones, empresa, a, CrearNotificacion(lead, empresa, campana), informar).ConfigureAwait(false);
        }

        /// <summary>El nombre de la campaña del catálogo (Campana; si no, Descripcion; si no, el código).</summary>
        private async Task<string> Campana(string origen)
        {
            string codigo = origen?.Trim();
            List<OrigenCliente> catalogo = origenes == null ? null : await origenes.LeerCatalogo().ConfigureAwait(false);
            OrigenCliente o = catalogo?.FirstOrDefault(x => string.Equals(x?.Codigo?.Trim(), codigo, StringComparison.OrdinalIgnoreCase));
            return !string.IsNullOrWhiteSpace(o?.Campana) ? o.Campana.Trim()
                : !string.IsNullOrWhiteSpace(o?.Descripcion) ? o.Descripcion.Trim()
                : codigo;
        }

        /// <summary>«Nuevo lead de Harmonia 09/26: CENTRO X (MADRID), cliente 12345. Llámele hoy.»</summary>
        public static string Texto(LeadNuevoAviso lead, string campana)
        {
            string poblacion = string.IsNullOrWhiteSpace(lead.Poblacion) ? "" : $" ({lead.Poblacion.Trim()})";
            return $"Nuevo lead de {campana}: {lead.Nombre?.Trim()}{poblacion}, cliente {lead.Cliente?.Trim()}. Llámele hoy.";
        }

        /// <summary>«Nuevo lead de Harmonia 09/26: CENTRO X (MADRID), cliente 12345. Lo gestiona Alberto Sancho: no hace falta que usted le llame.»</summary>
        public static string TextoSiNoGestiona(LeadNuevoAviso lead, string campana)
        {
            string poblacion = string.IsNullOrWhiteSpace(lead.Poblacion) ? "" : $" ({lead.Poblacion.Trim()})";
            return $"Nuevo lead de {campana}: {lead.Nombre?.Trim()}{poblacion}, cliente {lead.Cliente?.Trim()}. {ReglasAvisosLeads.TEXTO_LO_GESTIONA}";
        }

        /// <summary>Los mismos Datos que el recordatorio de las 24 h (LeadSinLlamar), para que al pulsarlo se abra el cliente.</summary>
        internal static NotificacionPushDTO CrearNotificacion(LeadNuevoAviso lead, string empresa, string campana) => new NotificacionPushDTO
        {
            Titulo = $"Nuevo lead de {campana}",
            Cuerpo = Texto(lead, campana),
            Tipo = TIPO_NOTIFICACION,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["empresa"] = empresa,
                ["cliente"] = lead.Cliente?.Trim() ?? string.Empty,
                ["contacto"] = lead.Contacto?.Trim() ?? string.Empty,
                ["origen"] = lead.Origen?.Trim() ?? string.Empty
            }
        };
    }

    /// <summary>Sin avisos (los tests y quien no pase avisador).</summary>
    public class SinAvisosLeadsNuevos : IAvisadorLeadsNuevos
    {
        public static readonly SinAvisosLeadsNuevos Instancia = new SinAvisosLeadsNuevos();
        public Task Avisar(LeadNuevoAviso lead) => Task.CompletedTask;
    }
}
