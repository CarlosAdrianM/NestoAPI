using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Reposiciones
{
    public interface IPermisoRellenarReposicionManual
    {
        /// <summary>¿Puede <paramref name="usuario"/> crear (rellenar) una reposición a mano con POST api/Reposiciones?</summary>
        bool Puede(IPrincipal usuario);
    }

    /// <summary>
    /// NestoAPI#577 (corte 3b). Decisión de Carlos (08/10/26): las reposiciones las rellena solo el proceso automático (job
    /// «reposiciones-automaticas», a la hora de cierre del calendario); a mano, solo quien esté en la lista del parámetro
    /// <see cref="CLAVE"/> de la fila «(defecto)» de la empresa por defecto (hoy «Manuel, Alfredo, Carlos»). El resto
    /// trabaja sobre las que ya están rellenas (preparar, cambiar cantidades, terminar).
    ///
    /// <para>Se lee SOLO la fila «(defecto)», a propósito: ParametrosUsuarioController.LeerParametro copiaría el valor a una
    /// fila del usuario, y cada uno podría cambiarse la suya. Lista separada por comas o punto y coma; se compara el nombre
    /// sin dominio y sin distinguir mayúsculas (Nesto llega como «NUEVAVISION\Alfredo»; Ariadna, sin dominio). Sin la
    /// fila, la lista por defecto <see cref="POR_DEFECTO"/>; con la fila vacía, nadie.</para>
    /// </summary>
    public class PermisoRellenarReposicionManual : IPermisoRellenarReposicionManual
    {
        public const string CLAVE = "UsuariosRellenarReposicionManual";
        public const string POR_DEFECTO = "Manuel, Alfredo, Carlos";
        public const string MENSAJE_SIN_PERMISO = "Solo el proceso automático y las personas autorizadas pueden rellenar reposiciones a mano.";
        private const string USUARIO_GENERAL = "(defecto)";

        private readonly Func<string> leerValor;

        public PermisoRellenarReposicionManual(NVEntities db) : this(() => LeerValor(db))
        {
        }

        /// <param name="leerValor">El valor del parámetro en la fila «(defecto)»; null si no hay fila.</param>
        internal PermisoRellenarReposicionManual(Func<string> leerValor)
        {
            this.leerValor = leerValor ?? throw new ArgumentNullException(nameof(leerValor));
        }

        public bool Puede(IPrincipal usuario)
        {
            string nombre = usuario?.Identity?.Name;
            if (usuario?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(nombre))
            {
                return false;
            }
            string sinDominio = nombre.Substring(nombre.LastIndexOf('\\') + 1).Trim();
            return Lista(leerValor()).Contains(sinDominio, StringComparer.OrdinalIgnoreCase);
        }

        internal static IEnumerable<string> Lista(string valor)
        {
            return (valor ?? POR_DEFECTO)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Select(u => u.Substring(u.LastIndexOf('\\') + 1))
                .Where(u => u.Length > 0);
        }

        private static string LeerValor(NVEntities db)
        {
            ParametroUsuario parametro = db.ParametrosUsuario.FirstOrDefault(p =>
                p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == USUARIO_GENERAL && p.Clave == CLAVE);
            return parametro?.Valor;
        }
    }
}
