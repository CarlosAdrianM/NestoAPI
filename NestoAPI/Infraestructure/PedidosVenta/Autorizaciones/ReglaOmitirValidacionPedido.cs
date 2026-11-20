using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.PedidosVenta.Autorizaciones
{
    /// <summary>
    /// LA REGLA de quién puede guardar un pedido que NO pasa la validación de ofertas y descuentos («¿Desea crearlo de todos modos?»),
    /// sacada tal cual del POST de PedidosVenta (Carlos 12/01/25, NestoAPI#501). La usan el POST y las autorizaciones de pedidos
    /// (Novedades 1258): quien puede crearlo de todos modos es quien puede autorizar el de otro.
    /// <list type="bullet">
    /// <item>Dirección o Almacén, o el parámetro de usuario <see cref="Constantes.ParametrosUsuario.PERMITIR_CREAR_PEDIDO_CON_ERRORES"/>
    /// activado (sea cual sea el almacén);</item>
    /// <item>Tiendas, solo si TODAS las líneas son de su almacén (parámetro «AlmacénPedidoVta»);</item>
    /// <item>y, si la denegación pide un permiso propio (<see cref="RespuestaValidacion.PermisoNecesario"/>, familias restringidas), tenerlo además.</item>
    /// </list>
    /// </summary>
    public static class ReglaOmitirValidacionPedido
    {
        public const string PARAMETRO_ALMACEN = "AlmacénPedidoVta";

        /// <summary>
        /// Si el grupo o el parámetro le dejan omitir la validación. Los parámetros se leen solo si hace falta (leer uno con fila
        /// «(defecto)» crea la del usuario): primero los grupos, como siempre. Pura salvo lo que hagan los lectores.
        /// </summary>
        public static bool GrupoPermite(bool esDireccion, bool esAlmacen, Func<bool> tieneParametro, bool esTiendas,
            Func<string> almacenDeLaTienda, IEnumerable<string> almacenesLineas)
        {
            if (esDireccion || esAlmacen || (tieneParametro?.Invoke() ?? false))
            {
                return true;
            }
            if (!esTiendas)
            {
                return false;
            }
            string almacen = almacenDeLaTienda?.Invoke()?.Trim();
            List<string> almacenes = (almacenesLineas ?? Enumerable.Empty<string>()).ToList();
            return !string.IsNullOrEmpty(almacen) && almacenes.Any() && almacenes.All(a => a?.Trim() == almacen);
        }

        /// <summary>El grupo le deja y, si esta denegación pide un permiso propio, también lo tiene.</summary>
        public static bool PuedeOmitir(bool grupoPermite, string permisoNecesario, Func<bool> tienePermisoNecesario)
            => grupoPermite && (string.IsNullOrWhiteSpace(permisoNecesario) || (tienePermisoNecesario?.Invoke() ?? false));

        /// <summary>El valor de un parámetro de usuario que cuenta como activado: «1», «true», «sí» (sin mayúsculas ni espacios).</summary>
        public static bool ValorActivado(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                return false;
            }
            string v = valor.Trim().ToUpperInvariant();
            return v == "1" || v == "TRUE" || v == "SI" || v == "SÍ";
        }

        /// <summary>
        /// La regla completa para un usuario y un pedido. <paramref name="leerParametro"/>(empresa, usuario, clave) es el lector de
        /// ParametrosUsuario (en producción, <c>ParametrosUsuarioController.LeerParametro</c>). Un token de cliente de la tienda nunca.
        /// Nunca lanza: si algo falla, false (como el POST).
        /// </summary>
        public static bool PuedeOmitir(IPrincipal principal, string usuario, string empresa, IEnumerable<string> almacenesLineas,
            string permisoNecesario, Func<string, string, string, string> leerParametro)
        {
            if (principal == null || (principal.Identity as ClaimsIdentity)?.FindFirst("cliente") != null)
            {
                return false;
            }
            try
            {
                string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                string usu = usuario?.Trim() ?? string.Empty;
                bool grupo = GrupoPermite(
                    principal.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION),
                    principal.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN),
                    () => !string.IsNullOrEmpty(usu) && ValorActivado(leerParametro(emp, usu, Constantes.ParametrosUsuario.PERMITIR_CREAR_PEDIDO_CON_ERRORES)),
                    principal.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS),
                    () => string.IsNullOrEmpty(usu) ? null : leerParametro(emp, SinDominio(usu), PARAMETRO_ALMACEN),
                    almacenesLineas);
                return PuedeOmitir(grupo, permisoNecesario,
                    () => !string.IsNullOrEmpty(usu) && ValorActivado(leerParametro(emp, usu, permisoNecesario)));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Si en general puede autorizar pedidos de otros (Dirección, Almacén, Tiendas o el parámetro): para ver todas las solicitudes.
        /// La de un pedido concreto es <see cref="PuedeOmitir(IPrincipal, string, string, IEnumerable{string}, string, Func{string, string, string, string})"/>.
        /// </summary>
        public static bool PuedeAutorizarAlguno(IPrincipal principal, string usuario, string empresa, Func<string, string, string, string> leerParametro)
        {
            if (principal == null || (principal.Identity as ClaimsIdentity)?.FindFirst("cliente") != null)
            {
                return false;
            }
            try
            {
                if (principal.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || principal.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN)
                    || principal.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS))
                {
                    return true;
                }
                string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                return !string.IsNullOrWhiteSpace(usuario)
                    && ValorActivado(leerParametro(emp, usuario.Trim(), Constantes.ParametrosUsuario.PERMITIR_CREAR_PEDIDO_CON_ERRORES));
            }
            catch
            {
                return false;
            }
        }

        public static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return limpio;
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }
    }
}
