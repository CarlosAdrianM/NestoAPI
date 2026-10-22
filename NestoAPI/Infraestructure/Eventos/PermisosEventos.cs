using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#611 (Carlos, 14/10/26): un evento con tienda (Reina REI o Alcobendas ALC) lo mantiene esa tienda (grupo Tiendas
    /// con ese AlmacénPedidoVta en ParametrosUsuario) y Administración; uno sin tienda (los de #591 y los que crea el Nesto de
    /// hoy), los mismos que en #591 (Tienda online). Dirección (e Informática) puede con todo. Marcar pagada: además,
    /// Administración en cualquier evento.
    /// Inscribir puede cualquiera de la casa (Nesto, vendedores en NestoApp) y el cliente de la tienda online, solo a sí mismo.
    /// </summary>
    public static class PermisosEventos
    {
        /// <summary>La clave de ParametrosUsuario con el almacén donde trabaja el usuario (la misma que reposiciones y Ariadna).</summary>
        public const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";

        /// <summary>Las tiendas donde se hacen eventos.</summary>
        public static readonly string[] ALMACENES_EVENTOS = { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        public const string MENSAJE_SIN_PERMISO_EVENTOS =
            "Los eventos los mantiene cada tienda (los de Reina, Reina; los de Alcobendas, Alcobendas), Administración y Dirección; " +
            "los que no tienen tienda, Tienda online y Dirección.";

        /// <summary>Dirección puede con todo; Informática también (como en #591).</summary>
        public static bool EsDireccion(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        /// <summary>
        /// ¿Puede crear/modificar el evento y pasar lista? Dirección (e Informática) siempre.
        /// <list type="bullet">
        /// <item>Evento SIN tienda (los de #591 y los que sigue creando el Nesto de hoy): los mismos de #591, Tienda online.</item>
        /// <item>Evento CON tienda: Administración, y el grupo Tiendas solo si es su tienda (<paramref name="almacenUsuario"/> =
        /// su AlmacénPedidoVta).</item>
        /// </list>
        /// </summary>
        public static bool PuedeMantener(IPrincipal usuario, string almacenUsuario, string almacenEvento)
        {
            if (usuario == null || ClienteDelToken(usuario) != null)
            {
                return false;
            }
            if (EsDireccion(usuario))
            {
                return true;
            }
            string tiendaEvento = almacenEvento?.Trim();
            if (string.IsNullOrEmpty(tiendaEvento))
            {
                return usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDA_ON_LINE);
            }
            if (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION))
            {
                return true;
            }
            string almacen = almacenUsuario?.Trim();
            return usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS)
                && !string.IsNullOrEmpty(almacen)
                && string.Equals(almacen, tiendaEvento, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// ¿Puede marcar una inscripción como pagada? Quien mantiene el evento y, además, Administración en cualquier evento,
        /// sea de la tienda que sea (Carlos, 14/10/26).
        /// </summary>
        public static bool PuedeMarcarPagada(IPrincipal usuario, string almacenUsuario, string almacenEvento) =>
            PuedeMantener(usuario, almacenUsuario, almacenEvento)
            || (usuario != null && ClienteDelToken(usuario) == null && usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION));

        public const string MENSAJE_SIN_PERMISO_IMAGEN =
            "La imagen del evento la pone Tienda online.";

        /// <summary>
        /// NestoAPI#690 (Carlos, 22/10/26): la imagen del evento, para las campañas, la pone solo Tienda online (y Dirección
        /// e Informática, que pueden con todo). Ni la tienda del evento ni Administración.
        /// </summary>
        public static bool PuedeSubirImagen(IPrincipal usuario)
        {
            if (usuario == null || ClienteDelToken(usuario) != null)
            {
                return false;
            }
            return EsDireccion(usuario) || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDA_ON_LINE);
        }

        /// <summary>El cliente del token de la tienda online (claim «cliente»), o null si es un usuario de la casa.</summary>
        public static string ClienteDelToken(IPrincipal usuario)
        {
            string cliente = (usuario?.Identity as ClaimsIdentity)?.FindFirst("cliente")?.Value?.Trim();
            return string.IsNullOrEmpty(cliente) ? null : cliente;
        }

        public static bool EsAlmacenDeEventos(string almacen) =>
            ALMACENES_EVENTOS.Contains(almacen?.Trim()?.ToUpperInvariant());

        public static string NombreAlmacen(string almacen)
        {
            switch (almacen?.Trim()?.ToUpperInvariant())
            {
                case Constantes.Almacenes.REINA: return "Reina";
                case Constantes.Almacenes.ALCOBENDAS: return "Alcobendas";
                default: return almacen?.Trim();
            }
        }

        /// <summary>«NUEVAVISION\Reina» → «Reina» (como se guarda en ParametrosUsuario).</summary>
        public static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            int barra = limpio.IndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1).Trim() : limpio;
        }
    }
}
