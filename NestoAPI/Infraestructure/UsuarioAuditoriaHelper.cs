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
        /// <summary>Longitud de las columnas Usuario de auditoría.</summary>
        public const int LONGITUD_MAXIMA = 30;

        /// <summary>Cuando no hay forma de saber quién fue. Mejor esto que una cadena vacía.</summary>
        public const string DESCONOCIDO = "DESCONOCIDO";

        public static string Resolver(IPrincipal user, string usuarioFallback)
        {
            if (user?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(user.Identity.Name))
            {
                return user.Identity.Name;
            }

            return usuarioFallback;
        }

        /// <summary>
        /// NestoAPI#456: deja el usuario listo para grabarlo en una columna de auditoría:
        /// recortado, de 30 caracteres como mucho y nunca vacío.
        ///
        /// <para>Hace falta porque estas columnas son NOT NULL con un valor por defecto
        /// <c>suser_sname()</c>. Mientras el EDMX marcaba la columna como <c>Computed</c>, EF ni
        /// siquiera la mandaba y el valor por defecto tapaba el problema: los 43.286 prepagos de
        /// la tabla salían a nombre de la cuenta de máquina. Al dejar de ser <c>Computed</c>, un
        /// usuario vacío ya no se tapa: rompe contra el NOT NULL. De eso protege esto.</para>
        /// </summary>
        public static string ParaAuditoria(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return DESCONOCIDO;
            }

            return limpio.Length > LONGITUD_MAXIMA
                ? limpio.Substring(0, LONGITUD_MAXIMA)
                : limpio;
        }
    }
}
