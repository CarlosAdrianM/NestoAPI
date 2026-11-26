using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Linq;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.ParametrosUsuario
{
    /// <summary>
    /// Auditoría de seguridad (26/11/26): quién puede cambiar una fila de ParametrosUsuario por PUT api/ParametrosUsuario.
    /// LA REGLA (pura):
    /// - Dirección e Informática: cualquier fila.
    /// - Las claves de PERMISO (<see cref="EsClaveDePermiso"/>) y la fila «(defecto)»: solo Dirección e Informática.
    /// - Un usuario identificado: solo SUS filas.
    /// - Sin identificar (Nesto guarda sus preferencias con un HttpClient SIN token, Configuracion.GuardarParametro): se deja
    ///   como hasta ahora para no romper Nesto, pero nunca una clave de permiso ni la fila «(defecto)».
    /// </summary>
    internal static class ReglaCambioParametroUsuario
    {
        internal const string USUARIO_DEFECTO = "(defecto)";

        /// <summary>Claves exactas que dan permisos y no empiezan por un prefijo protegido.</summary>
        internal static readonly string[] CLAVES_PERMISO =
        {
            Constantes.ParametrosUsuario.PERMITIR_CREAR_PEDIDO_CON_ERRORES,
            "InventarioBorrarFilas"
        };

        /// <summary>Prefijos de claves de permiso o de interruptores de negocio («Permitir…», «Cupones.», «NestoViejo_», «Validacion.»).</summary>
        internal static readonly string[] PREFIJOS_PERMISO =
        {
            "Permitir",
            "Cupones.",
            "NestoViejo_",
            "Validacion."
        };

        internal const string MOTIVO_CLAVE_PERMISO =
            "Solo Dirección o Informática pueden cambiar el parámetro «{0}»: es un permiso.";
        internal const string MOTIVO_FILA_DEFECTO =
            "Solo Dirección o Informática pueden cambiar el valor «(defecto)» de un parámetro.";
        internal const string MOTIVO_OTRO_USUARIO =
            "Solo puede cambiar sus propios parámetros (este es de {0}).";

        internal static bool EsClaveDePermiso(string clave)
        {
            string c = (clave ?? string.Empty).Trim();
            if (c.Length == 0)
            {
                return false;
            }
            return CLAVES_PERMISO.Any(k => string.Equals(k, c, StringComparison.OrdinalIgnoreCase))
                || PREFIJOS_PERMISO.Any(p => c.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool EsDireccionOInformatica(IPrincipal usuario)
        {
            return usuario?.Identity != null && usuario.Identity.IsAuthenticated
                && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                    || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA)
                    // Novedades lo escribe sin tilde (NovedadesController.GRUPO_INFORMATICA): valen las dos grafías.
                    || usuario.IsInRoleSinDominio("Informatica"));
        }

        internal static string SinDominio(string usuario)
        {
            string u = (usuario ?? string.Empty).Trim();
            int barra = u.LastIndexOf('\\');
            return barra >= 0 ? u.Substring(barra + 1).Trim() : u;
        }

        /// <summary>null = se puede; si no, el motivo (de usted) para el 403.</summary>
        internal static string MotivoNoPermitido(IPrincipal quien, string usuarioFila, string clave)
        {
            if (EsDireccionOInformatica(quien))
            {
                return null;
            }
            if (EsClaveDePermiso(clave))
            {
                return string.Format(MOTIVO_CLAVE_PERMISO, (clave ?? string.Empty).Trim());
            }
            string fila = SinDominio(usuarioFila);
            if (string.Equals(fila, USUARIO_DEFECTO, StringComparison.OrdinalIgnoreCase))
            {
                return MOTIVO_FILA_DEFECTO;
            }
            bool identificado = quien?.Identity != null && quien.Identity.IsAuthenticated;
            if (identificado && !string.Equals(fila, SinDominio(quien.Identity.Name), StringComparison.OrdinalIgnoreCase))
            {
                return string.Format(MOTIVO_OTRO_USUARIO, fila);
            }
            return null;
        }
    }
}
