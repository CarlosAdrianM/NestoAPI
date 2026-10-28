using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>A quién se avisa de un pedido (usuarios de Nesto, con dominio, y correos).</summary>
    public class DestinatariosPromesaRiesgo
    {
        public List<string> Usuarios { get; set; } = new List<string>();
        public List<string> Correos { get; set; } = new List<string>();
        /// <summary>De qué usuarios (sin dominio) es cada correo; un correo sin usuarios conocidos no se filtra por preferencia.</summary>
        public Dictionary<string, List<string>> UsuariosDeCorreo { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    public interface IAvisadorPromesaRiesgo
    {
        /// <summary>El riesgo con su cliente y comerciales, listo para avisar.</summary>
        AvisoPromesaRiesgo Preparar(RiesgoPromesa riesgo, bool empeora);
        /// <summary>Campana de Nesto (buzón + SignalR) al vendedor, a su jefe de equipo y a Dirección. Nunca lanza.</summary>
        Task<int> AvisarCampana(AvisoPromesaRiesgo aviso);
        /// <summary>
        /// El correo resumen: uno por destinatario con sus pedidos (el vendedor los suyos, el jefe los de su equipo, Dirección
        /// todos). En Sombra, uno solo a Informática con todo. Devuelve cuántos correos han salido. Nunca lanza.
        /// </summary>
        int EnviarCorreos(IEnumerable<AvisoPromesaRiesgo> avisos, bool sombra);
    }

    /// <summary>
    /// NestoAPI#693: los avisos. Destinatarios (decisión de Carlos en la issue): el vendedor del pedido (los comerciales reales
    /// de <see cref="LectorComercialesPedido"/>: estética y, si lleva peluquería, el de peluquería; sin comercial real, quien
    /// grabó el pedido), su jefe de equipo (<see cref="IServicioVendedores.JefeEquipo"/>, EquiposVenta) y Dirección (Manuel y
    /// Carlos, <see cref="InterruptorAvisoPromesaRiesgo.UsuariosDireccion"/>). Sin push: campana de Nesto y correo.
    /// </summary>
    public class AvisadorPromesaRiesgo : IAvisadorPromesaRiesgo
    {
        private const string DOMINIO = "NUEVAVISION\\";
        internal const string CORREO_REMITENTE = "nesto@nuevavision.es";

        private readonly ILectorComercialesPedido lector;
        private readonly IServicioVendedores vendedores;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly IServicioCorreoElectronico correo;
        private readonly Func<List<string>> usuariosDireccion;
        private readonly Func<string, string> correoDeUsuario;
        private readonly Action<Exception> registrar;
        private readonly Dictionary<string, ComercialesPedido> jefes = new Dictionary<string, ComercialesPedido>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, DestinatariosPromesaRiesgo> destinatariosPedido = new Dictionary<int, DestinatariosPromesaRiesgo>();

        /// <param name="preferenciaDeUsuario">Carlos (28/10/26): (usuario) → Todo | SoloCorreo | SoloCampana | Nada
        /// (<see cref="PreferenciaAvisosPromesaRiesgo"/>). Null = todos reciben todo.</param>
        public AvisadorPromesaRiesgo(ILectorComercialesPedido lector, IServicioVendedores vendedores, IServicioNotificacionesPush notificaciones,
            IServicioCorreoElectronico correo, Func<List<string>> usuariosDireccion, Func<string, string> correoDeUsuario, Action<Exception> registrar = null,
            Func<string, string> preferenciaDeUsuario = null)
        {
            this.preferenciaDeUsuario = preferenciaDeUsuario;
            this.lector = lector;
            this.vendedores = vendedores;
            this.notificaciones = notificaciones;
            this.correo = correo;
            this.usuariosDireccion = usuariosDireccion ?? InterruptorAvisoPromesaRiesgo.UsuariosDireccion;
            this.correoDeUsuario = correoDeUsuario ?? (_ => null);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, "Sistema (promesa en riesgo)"));
        }

        public AvisoPromesaRiesgo Preparar(RiesgoPromesa riesgo, bool empeora)
        {
            ComercialesPedido comerciales = LeerComerciales(riesgo);
            return new AvisoPromesaRiesgo
            {
                Riesgo = riesgo,
                Empeora = empeora,
                Cliente = comerciales?.Cliente,
                NombreCliente = comerciales?.NombreCliente,
                Comerciales = comerciales?.NombresComerciales
            };
        }

        public async Task<int> AvisarCampana(AvisoPromesaRiesgo aviso)
        {
            int avisados = 0;
            try
            {
                DestinatariosPromesaRiesgo destinatarios = Destinatarios(aviso.Riesgo);
                NotificacionPushDTO notificacion = TextosPromesaRiesgo.Notificacion(aviso);
                foreach (string usuario in destinatarios.Usuarios)
                {
                    try
                    {
                        await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                        avisados++;
                    }
                    catch (Exception ex)
                    {
                        Registrar($"no se ha podido avisar a {usuario} del pedido {aviso.Riesgo.Pedido}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Registrar($"no se ha podido avisar del pedido {aviso.Riesgo?.Pedido}", ex);
            }
            return avisados;
        }

        public int EnviarCorreos(IEnumerable<AvisoPromesaRiesgo> avisos, bool sombra)
        {
            List<AvisoPromesaRiesgo> lista = (avisos ?? Enumerable.Empty<AvisoPromesaRiesgo>()).Where(a => a?.Riesgo != null).ToList();
            if (!lista.Any())
            {
                return 0;
            }
            Dictionary<string, List<AvisoPromesaRiesgo>> porCorreo = sombra
                ? new Dictionary<string, List<AvisoPromesaRiesgo>> { [Constantes.Correos.INFORMATICA] = lista }
                : AgruparPorCorreo(lista, a => SafeDestinatarios(a.Riesgo).Correos);
            int enviados = 0;
            foreach (KeyValuePair<string, List<AvisoPromesaRiesgo>> destino in porCorreo)
            {
                try
                {
                    var mail = new MailMessage
                    {
                        From = new MailAddress(CORREO_REMITENTE),
                        Subject = TextosPromesaRiesgo.AsuntoCorreo(destino.Value.Count, sombra),
                        Body = TextosPromesaRiesgo.CuerpoCorreo(destino.Value, sombra),
                        IsBodyHtml = true
                    };
                    mail.To.Add(destino.Key);
                    if (correo.EnviarCorreoSMTP(mail))
                    {
                        enviados++;
                    }
                }
                catch (Exception ex)
                {
                    Registrar($"no se ha podido mandar el correo resumen a {destino.Key}", ex);
                }
            }
            return enviados;
        }

        /// <summary>Invierte «pedido → correos» en «correo → pedidos», sin repetir pedidos y en el orden de llegada.</summary>
        internal static Dictionary<string, List<AvisoPromesaRiesgo>> AgruparPorCorreo(IEnumerable<AvisoPromesaRiesgo> avisos,
            Func<AvisoPromesaRiesgo, IEnumerable<string>> correosDe)
        {
            var resultado = new Dictionary<string, List<AvisoPromesaRiesgo>>(StringComparer.OrdinalIgnoreCase);
            foreach (AvisoPromesaRiesgo aviso in avisos)
            {
                foreach (string direccion in (correosDe(aviso) ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)))
                {
                    if (!resultado.TryGetValue(direccion.Trim(), out List<AvisoPromesaRiesgo> suyos))
                    {
                        suyos = new List<AvisoPromesaRiesgo>();
                        resultado[direccion.Trim()] = suyos;
                    }
                    if (!suyos.Contains(aviso))
                    {
                        suyos.Add(aviso);
                    }
                }
            }
            return resultado;
        }

        private DestinatariosPromesaRiesgo SafeDestinatarios(RiesgoPromesa riesgo)
        {
            try
            {
                return Destinatarios(riesgo);
            }
            catch (Exception ex)
            {
                Registrar($"no se han podido leer los destinatarios del pedido {riesgo.Pedido}", ex);
                return new DestinatariosPromesaRiesgo();
            }
        }

        private readonly Func<string, string> preferenciaDeUsuario;
        private readonly Dictionary<string, string> preferencias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>La preferencia del usuario (sin dominio), leída una vez por pasada. Sin función o si falla, Todo.</summary>
        private string Preferencia(string usuario)
        {
            string sinDominio = ComercialesPedido.SinDominio(usuario) ?? string.Empty;
            if (preferencias.TryGetValue(sinDominio, out string guardada))
            {
                return guardada;
            }
            string valor = PreferenciaAvisosPromesaRiesgo.TODO;
            if (preferenciaDeUsuario != null)
            {
                try
                {
                    valor = PreferenciaAvisosPromesaRiesgo.Normalizar(preferenciaDeUsuario(sinDominio));
                }
                catch (Exception ex)
                {
                    Registrar($"no se ha podido leer la preferencia de avisos de {sinDominio}", ex);
                }
            }
            preferencias[sinDominio] = valor;
            return valor;
        }

        /// <summary>
        /// Pura: quita de la campana a quien no la quiere y del correo a quien no lo quiere. Un correo se queda mientras alguno de
        /// sus usuarios lo quiera (o no se sepa de quién es).
        /// </summary>
        internal static DestinatariosPromesaRiesgo AplicarPreferencias(DestinatariosPromesaRiesgo destinatarios, Func<string, string> preferencia)
        {
            if (destinatarios == null || preferencia == null)
            {
                return destinatarios;
            }
            var filtrados = new DestinatariosPromesaRiesgo { UsuariosDeCorreo = destinatarios.UsuariosDeCorreo };
            filtrados.Usuarios.AddRange(destinatarios.Usuarios.Where(u => PreferenciaAvisosPromesaRiesgo.Campana(preferencia(ComercialesPedido.SinDominio(u)))));
            foreach (string correo in destinatarios.Correos)
            {
                List<string> usuarios = destinatarios.UsuariosDeCorreo != null && destinatarios.UsuariosDeCorreo.TryGetValue(correo, out List<string> lista) ? lista : null;
                if (usuarios == null || usuarios.Count == 0 || usuarios.Any(u => PreferenciaAvisosPromesaRiesgo.Correo(preferencia(u))))
                {
                    filtrados.Correos.Add(correo);
                }
            }
            return filtrados;
        }

        /// <summary>Los del pedido (vendedores y sus jefes) más Dirección, ya con la preferencia de cada uno. Cacheados por pedido durante la pasada.</summary>
        public DestinatariosPromesaRiesgo Destinatarios(RiesgoPromesa riesgo)
        {
            if (destinatariosPedido.TryGetValue(riesgo.Pedido, out DestinatariosPromesaRiesgo yaLeidos))
            {
                return yaLeidos;
            }
            ComercialesPedido comerciales = LeerComerciales(riesgo);
            var jefesPedido = new List<ComercialesPedido>();
            foreach (ComercialPedido comercial in (comerciales?.Comerciales ?? new List<ComercialPedido>()).Where(ComercialesPedido.EsComercialReal))
            {
                ComercialesPedido jefe = LeerJefe(riesgo.Empresa, comercial.Vendedor);
                if (jefe != null)
                {
                    jefesPedido.Add(jefe);
                }
            }
            List<string> direccion = usuariosDireccion() ?? new List<string>();
            DestinatariosPromesaRiesgo destinatarios = Componer(comerciales, jefesPedido, direccion,
                direccion.Select(u => new KeyValuePair<string, string>(u, correoDeUsuario(u))).Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToList());
            destinatarios = AplicarPreferencias(destinatarios, Preferencia);
            destinatariosPedido[riesgo.Pedido] = destinatarios;
            return destinatarios;
        }

        /// <summary>
        /// Pura: usuarios con dominio (los del buzón de Nesto) y correos válidos, sin repetir. Jefes: sus usuarios y su correo
        /// (Vendedores.Mail); los de un jefe sin usuario ni correo no se pierden: Dirección siempre está.
        /// </summary>
        internal static DestinatariosPromesaRiesgo Componer(ComercialesPedido comerciales, IEnumerable<ComercialesPedido> jefesEquipo,
            IEnumerable<string> usuariosDireccion, IEnumerable<string> correosDireccion)
            => Componer(comerciales, jefesEquipo, usuariosDireccion,
                (correosDireccion ?? Enumerable.Empty<string>()).Select(c => new KeyValuePair<string, string>(null, c)));

        /// <summary>Con los correos de Dirección por usuario (usuario, correo), para que cuente la preferencia de cada uno.</summary>
        internal static DestinatariosPromesaRiesgo Componer(ComercialesPedido comerciales, IEnumerable<ComercialesPedido> jefesEquipo,
            IEnumerable<string> usuariosDireccion, IEnumerable<KeyValuePair<string, string>> correosDireccionPorUsuario)
        {
            var d = new DestinatariosPromesaRiesgo();
            void Dueños(string correo, IEnumerable<string> usuarios)
            {
                string limpio = correo?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(limpio))
                {
                    return;
                }
                if (!d.UsuariosDeCorreo.TryGetValue(limpio, out List<string> lista))
                {
                    lista = new List<string>();
                    d.UsuariosDeCorreo[limpio] = lista;
                }
                foreach (string u in (usuarios ?? Enumerable.Empty<string>()).Select(ComercialesPedido.SinDominio).Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    if (!lista.Contains(u, StringComparer.OrdinalIgnoreCase))
                    {
                        lista.Add(u);
                    }
                }
            }
            void Usuario(string usuario)
            {
                string sinDominio = ComercialesPedido.SinDominio(usuario);
                if (!string.IsNullOrWhiteSpace(sinDominio) && !d.Usuarios.Contains(DOMINIO + sinDominio, StringComparer.OrdinalIgnoreCase))
                {
                    d.Usuarios.Add(DOMINIO + sinDominio);
                }
            }
            void Correo(string direccion)
            {
                string limpio = direccion?.Trim().ToLowerInvariant();
                if (ComercialesPedido.EsCorreoValido(limpio) && !d.Correos.Contains(limpio))
                {
                    d.Correos.Add(limpio);
                }
            }
            if (comerciales != null)
            {
                comerciales.UsuariosBuzon().ForEach(Usuario);
                foreach (KeyValuePair<string, List<string>> correoConUsuarios in comerciales.CorreosConUsuarios())
                {
                    Correo(correoConUsuarios.Key);
                    Dueños(correoConUsuarios.Key, correoConUsuarios.Value);
                }
            }
            foreach (ComercialesPedido jefe in jefesEquipo ?? Enumerable.Empty<ComercialesPedido>())
            {
                foreach (ComercialPedido comercial in jefe.Comerciales ?? new List<ComercialPedido>())
                {
                    (comercial.Usuarios ?? new List<string>()).ForEach(Usuario);
                    Correo(comercial.Correo);
                    Dueños(comercial.Correo, comercial.Usuarios);
                }
            }
            foreach (string usuario in usuariosDireccion ?? Enumerable.Empty<string>())
            {
                Usuario(usuario);
            }
            foreach (KeyValuePair<string, string> direccion in correosDireccionPorUsuario ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                Correo(direccion.Value);
                if (!string.IsNullOrWhiteSpace(direccion.Key))
                {
                    Dueños(direccion.Value, new[] { direccion.Key });
                }
            }
            return d;
        }

        private readonly Dictionary<int, ComercialesPedido> comercialesPedido = new Dictionary<int, ComercialesPedido>();

        private ComercialesPedido LeerComerciales(RiesgoPromesa riesgo)
        {
            if (!comercialesPedido.TryGetValue(riesgo.Pedido, out ComercialesPedido comerciales))
            {
                try
                {
                    comerciales = lector.Leer(riesgo.Empresa?.Trim(), riesgo.Pedido);
                }
                catch (Exception ex)
                {
                    Registrar($"no se han podido leer los comerciales del pedido {riesgo.Pedido}", ex);
                    comerciales = null;
                }
                comercialesPedido[riesgo.Pedido] = comerciales;
            }
            return comerciales;
        }

        private ComercialesPedido LeerJefe(string empresa, string vendedor)
        {
            string clave = $"{empresa?.Trim()}|{vendedor?.Trim()}";
            if (jefes.TryGetValue(clave, out ComercialesPedido yaLeido))
            {
                return yaLeido;
            }
            ComercialesPedido jefe = null;
            try
            {
                VendedorDTO superior = vendedores.JefeEquipo(empresa?.Trim(), vendedor?.Trim()).GetAwaiter().GetResult();
                if (superior != null && !string.IsNullOrWhiteSpace(superior.vendedor)
                    && !string.Equals(superior.vendedor.Trim(), vendedor?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    jefe = lector.LeerDeVendedor(empresa?.Trim(), superior.vendedor.Trim(), null);
                }
            }
            catch (Exception ex)
            {
                Registrar($"no se ha podido leer el jefe de equipo del vendedor {vendedor}", ex);
            }
            jefes[clave] = jefe;
            return jefe;
        }

        private void Registrar(string que, Exception ex)
        {
            try
            {
                registrar(new Exception($"[Promesa en riesgo #693] {que}: {ex.Message}", ex));
            }
            catch
            {
                // nunca romper la pasada
            }
        }
    }
}
