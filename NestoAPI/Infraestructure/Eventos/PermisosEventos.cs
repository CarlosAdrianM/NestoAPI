using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#611 (Carlos, 14/10/26): los eventos no los mantiene Tienda online sino cada tienda. Un evento es en Reina
    /// (REI) o en Alcobendas (ALC); crearlo, modificarlo, marcar pagos y pasar lista lo puede hacer el grupo Tiendas, pero
    /// solo en los eventos de SU tienda (su AlmacénPedidoVta en ParametrosUsuario). Dirección (e Informática) puede con todo.
    /// Inscribir puede cualquiera de la casa (Nesto, vendedores en NestoApp) y el cliente de la tienda online, solo a sí mismo.
    /// </summary>
    public static class PermisosEventos
    {
        /// <summary>La clave de ParametrosUsuario con el almacén donde trabaja el usuario (la misma que reposiciones y Ariadna).</summary>
        public const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";

        /// <summary>Las tiendas donde se hacen eventos.</summary>
        public static readonly string[] ALMACENES_EVENTOS = { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        public const string MENSAJE_SIN_PERMISO_EVENTOS =
            "Los eventos los mantiene cada tienda (los de Reina, Reina; los de Alcobendas, Alcobendas) y Dirección.";

        /// <summary>Dirección puede con todo; Informática también (como en #591).</summary>
        public static bool EsDireccion(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        /// <summary>
        /// ¿Puede crear/modificar el evento, marcar pagos y pasar lista? Dirección siempre; el grupo Tiendas solo si el
        /// evento es de su tienda (<paramref name="almacenUsuario"/> = su AlmacénPedidoVta).
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
            string almacen = almacenUsuario?.Trim();
            return usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS)
                && !string.IsNullOrEmpty(almacen)
                && string.Equals(almacen, almacenEvento?.Trim(), StringComparison.OrdinalIgnoreCase);
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
