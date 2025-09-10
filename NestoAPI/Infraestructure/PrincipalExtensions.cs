using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;

namespace NestoAPI.Infrastructure
{
    public static class PrincipalExtensions
    {
        public static bool IsInRoleSinDominio(this ClaimsPrincipal user, string roleName)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            // Recorremos todos los claims de tipo Role
            foreach (var claim in user.Claims.Where(c => c.Type == ClaimTypes.Role))
            {
                var role = claim.Value;
                // role puede venir como "NUEVAVISION\\Dirección"
                var soloNombre = role.Contains("\\") ? role.Split('\\')[1] : role;

                if (string.Equals(soloNombre, roleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsInRoleSinDominio(this IPrincipal user, string roleName)
        {
            if (!(user is ClaimsPrincipal claimsPrincipal))
            {
                return false;
            }

            return claimsPrincipal.IsInRoleSinDominio(roleName); // reutiliza el otro método
        }
    }
}
