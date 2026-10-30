using Hangfire.Dashboard;
using Microsoft.Owin;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Security.Principal;

namespace NestoAPI
{
    /// <summary>
    /// NestoAPI#749 (30/10/26): el panel de Hangfire (/hangfire) estaba abierto a todo el mundo (Authorize devolvía true) y se
    /// han lanzado jobs con POST /hangfire/recurring/trigger sin credenciales. Solo entran Dirección e Informática.
    ///
    /// <para>Cómo: autenticación integrada de Windows (Negotiate/NTLM), la misma que usa Nesto en api/auth/windows-token. Un
    /// navegador que abre /hangfire no manda Bearer, pero sí sabe hacer Negotiate: IIS ya tiene activadas la autenticación
    /// anónima y la de Windows (Web.config del repo), así que la petición llega anónima, este filtro la rechaza con 401 +
    /// WWW-Authenticate: Negotiate/NTLM, el navegador repite con las credenciales del usuario del dominio (en la red de la
    /// empresa, sin preguntar; fuera, pide usuario y contraseña) y la petición llega con su WindowsPrincipal. Sin cookies ni
    /// secretos nuevos que guardar, y sin tocar la autenticación JWT.</para>
    ///
    /// <para>Solo vale una identidad de Windows: un JWT con el rol «Dirección» no abre el panel (el panel no es API y el
    /// JWT de empleado dura días; si se filtra, no debe poder lanzar jobs). Anónimo → 401 (con el reto); autenticado sin
    /// permiso → 403 (lo pone Hangfire según si hay usuario). Vale igual para los GET del panel y para los POST.</para>
    /// </summary>
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        internal static readonly IReadOnlyList<string> GRUPOS_PERMITIDOS = new[]
        {
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA
        };

        private readonly Func<IPrincipal, string, bool> estaEnGrupo;

        public HangfireAuthorizationFilter() : this(null)
        {
        }

        /// <param name="estaEnGrupo">Para los tests; por defecto, el grupo del dominio de una identidad de Windows.</param>
        internal HangfireAuthorizationFilter(Func<IPrincipal, string, bool> estaEnGrupo)
        {
            this.estaEnGrupo = estaEnGrupo ?? EstaEnGrupoWindows;
        }

        public bool Authorize(DashboardContext context)
        {
            if (context == null)
            {
                return false;
            }
            var owin = new OwinContext(context.GetOwinEnvironment());
            IPrincipal usuario = owin.Request.User;
            if (PuedeEntrar(usuario))
            {
                return true;
            }
            if (usuario?.Identity?.IsAuthenticated != true)
            {
                // Hangfire pone el 401; el reto es lo que hace que el navegador mande las credenciales de Windows
                owin.Response.Headers.AppendValues("WWW-Authenticate", "Negotiate", "NTLM");
            }
            return false;
        }

        internal bool PuedeEntrar(IPrincipal usuario)
        {
            if (usuario?.Identity == null || !usuario.Identity.IsAuthenticated)
            {
                return false;
            }
            foreach (string grupo in GRUPOS_PERMITIDOS)
            {
                try
                {
                    if (estaEnGrupo(usuario, grupo))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Si no se puede resolver el grupo, no se entra (falla en cerrado)
                }
            }
            return false;
        }

        /// <summary>
        /// Solo identidades de Windows (las que pone IIS tras Negotiate/NTLM). El grupo se busca en el dominio del propio
        /// usuario («NUEVAVISION\carlos» → «NUEVAVISION\Dirección»); IsInRole compara SIDs del token, sin recorrer los grupos.
        /// </summary>
        internal static bool EstaEnGrupoWindows(IPrincipal usuario, string grupo)
        {
            if (!(usuario?.Identity is WindowsIdentity identidad) || !identidad.IsAuthenticated || identidad.IsAnonymous || identidad.IsGuest)
            {
                return false;
            }
            string nombre = identidad.Name ?? string.Empty;
            int barra = nombre.IndexOf('\\');
            if (barra <= 0)
            {
                return false;
            }
            var principal = usuario as WindowsPrincipal ?? new WindowsPrincipal(identidad);
            return principal.IsInRole(nombre.Substring(0, barra) + "\\" + grupo);
        }
    }
}
