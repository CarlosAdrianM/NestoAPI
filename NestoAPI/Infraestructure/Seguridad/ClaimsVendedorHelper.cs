using System.Security.Claims;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// Helper para añadir claims de vendedor a una identidad.
    /// </summary>
    public static class ClaimsVendedorHelper
    {
        /// <summary>
        /// Añade claims de vendedor si el usuario tiene uno asociado.
        /// Claims añadidos:
        /// - "IsVendedor" = "true"
        /// - "Vendedor" = código del vendedor (ej: "NV")
        /// </summary>
        /// <param name="identity">Identidad a la que añadir los claims</param>
        /// <param name="userName">Nombre de usuario</param>
        /// <param name="servicio">Servicio para obtener el vendedor (opcional, usa implementación por defecto)</param>
        /// <returns>true si se añadieron claims, false en caso contrario</returns>
        public static bool AñadirClaimsVendedor(ClaimsIdentity identity, string userName, IServicioUsuarioVendedor servicio = null)
        {
            if (identity == null || string.IsNullOrWhiteSpace(userName))
            {
                return false;
            }

            servicio = servicio ?? new ServicioUsuarioVendedor();
            var codigoVendedor = servicio.ObtenerVendedorDeUsuario(userName);

            if (!string.IsNullOrWhiteSpace(codigoVendedor))
            {
                identity.AddClaim(new Claim("IsVendedor", "true"));
                identity.AddClaim(new Claim("Vendedor", codigoVendedor));
                return true;
            }

            return false;
        }
    }
}
