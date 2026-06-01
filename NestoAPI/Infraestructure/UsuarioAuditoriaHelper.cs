using System;
using System.Security.Principal;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Resuelve el usuario que se graba en los campos de auditoría (Usuario) de las tablas.
    /// </summary>
    /// <remarks>
    /// El usuario debe tomarse SIEMPRE del Identity autenticado, no de lo que mande el cliente
    /// en la petición. El JWT de empleado (endpoint /api/auth/windows-token) lleva el usuario
    /// Windows real (DOMINIO\Usuario), así que <c>User.Identity.Name</c> es la fuente fiable.
    ///
    /// Los clientes (Nesto) venían pasando el usuario en un parámetro de query construido con
    /// <c>Environment.UserDomainName + "\" + Environment.UserName</c>. Cuando Nesto se ejecuta en
    /// contexto de máquina/sistema en el servidor RDS, <c>Environment.UserName</c> devuelve el
    /// machine account del proceso (p. ej. NUEVAVISION\RDS2016$), que acababa grabándose como
    /// autor de la oferta. Además, un parámetro de query es spoofeable. Por eso se ignora salvo
    /// como último recurso cuando no hay Identity (tests o llamadas no autenticadas).
    /// </remarks>
    public static class UsuarioAuditoriaHelper
    {
        public static string Resolver(IPrincipal user, string usuarioFallback)
        {
            if (user?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(user.Identity.Name))
            {
                return user.Identity.Name;
            }

            return usuarioFallback;
        }
    }
}
