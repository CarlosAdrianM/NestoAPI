using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// Nesto#458: quién puede cambiar el estado y el vendedor de un cliente por
    /// PUT api/Clientes/ClienteComercial. Hasta ahora el endpoint no validaba nada: el filtro
    /// del combo de la ventana era una sugerencia, no una restricción.
    ///
    /// <para>La regla: un VENDEDOR solo puede tocar clientes cuyo vendedor actual esté en su
    /// equipo (EquiposVenta a fecha de hoy, él incluido) o sea el genérico NV, y solo puede
    /// asignar dentro de ese mismo conjunto. Un vendedor sin equipo queda restringido a sus
    /// propios clientes. Si el cliente lleva en la cabecera O en el grupo de producto a alguien
    /// de fuera, no se toca nada — tampoco el estado.</para>
    ///
    /// <para>La OFICINA no se restringe: los usuarios de los grupos de administración y los que
    /// no tienen vendedor asociado siguen como siempre. Se hace así a propósito: la ventana la
    /// usa administración a diario y un fallo en el matching de grupos de AD (acentos, dominio)
    /// no puede dejarles fuera. "Ser jefe de ventas" sale de los DATOS (tener equipo vigente),
    /// no de ninguna constante.</para>
    /// </summary>
    public static class ValidadorCambioClienteComercial
    {
        /// <summary>Grupos de AD que no se restringen (con y sin acento, que el NTAccount
        /// depende de cómo esté escrito el grupo en el dominio).</summary>
        private static readonly HashSet<string> GruposSinRestriccion = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Administración", "Administracion", "Dirección", "Direccion", "Informática", "Informatica"
        };

        public static async Task<ResultadoPermisoClienteComercial> EvaluarAsync(
            IPrincipal user, NVEntities db, Cliente clienteDB, ClienteDTO cambios,
            IServicioVendedores servicioVendedores, IServicioUsuarioVendedor servicioUsuarioVendedor)
        {
            DatosPermisoClienteComercial datos = new DatosPermisoClienteComercial
            {
                EsOficina = EsUsuarioDeOficina(user),
                VendedorDelUsuario = VendedorDelUsuario(user, servicioUsuarioVendedor),
                VendedorActual = clienteDB.Vendedor,
                VendedorDestino = cambios.vendedor,
                CambiaEstado = cambios.estado != null && clienteDB.Estado != cambios.estado,
                VendedorGrupoActual = db.VendedoresClientesGruposProductos
                    .Where(v => v.Empresa == clienteDB.Empresa && v.Cliente == clienteDB.Nº_Cliente && v.Contacto == clienteDB.Contacto)
                    .Select(v => v.Vendedor)
                    .FirstOrDefault(),
                VendedorGrupoDestino = cambios.VendedoresGrupoProducto?.FirstOrDefault()?.vendedor
            };

            if (!datos.EsOficina && datos.VendedorDelUsuario != null)
            {
                datos.EquipoDelUsuario = await servicioVendedores
                    .VendedoresEquipoString(clienteDB.Empresa.Trim(), datos.VendedorDelUsuario)
                    .ConfigureAwait(false);
            }

            return Evaluar(datos);
        }

        /// <summary>El núcleo puro de la regla, sin base de datos ni claims, para testearlo.</summary>
        internal static ResultadoPermisoClienteComercial Evaluar(DatosPermisoClienteComercial datos)
        {
            if (datos.EsOficina || datos.VendedorDelUsuario == null)
            {
                return ResultadoPermisoClienteComercial.Si();
            }

            string actual = datos.VendedorActual?.Trim();
            bool cambiaVendedor = SonDistintos(actual, datos.VendedorDestino);
            bool cambiaGrupo = datos.VendedorGrupoDestino != null
                && SonDistintos(datos.VendedorGrupoActual?.Trim(), datos.VendedorGrupoDestino);

            if (!cambiaVendedor && !cambiaGrupo && !datos.CambiaEstado)
            {
                return ResultadoPermisoClienteComercial.Si();
            }

            HashSet<string> conjunto = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Constantes.Vendedores.VENDEDOR_GENERAL,
                datos.VendedorDelUsuario.Trim()
            };
            foreach (string vendedor in datos.EquipoDelUsuario ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(vendedor))
                {
                    _ = conjunto.Add(vendedor.Trim());
                }
            }

            // Si el cliente lo lleva alguien de fuera —en la cabecera o en el grupo de
            // producto—, no se toca NADA, tampoco el estado.
            if (!string.IsNullOrWhiteSpace(actual) && !conjunto.Contains(actual))
            {
                return ResultadoPermisoClienteComercial.No(
                    $"El cliente lo lleva {actual}, que no es de tu equipo: no puedes cambiarlo");
            }
            string grupoActual = datos.VendedorGrupoActual?.Trim();
            if (!string.IsNullOrWhiteSpace(grupoActual) && !conjunto.Contains(grupoActual))
            {
                return ResultadoPermisoClienteComercial.No(
                    $"El cliente lo lleva {grupoActual} en el grupo de producto, que no es de tu equipo: no puedes cambiarlo");
            }

            if (cambiaVendedor && !conjunto.Contains(datos.VendedorDestino.Trim()))
            {
                return ResultadoPermisoClienteComercial.No(
                    $"No puedes asignar el cliente a {datos.VendedorDestino.Trim()}: no es de tu equipo");
            }
            if (cambiaGrupo && !conjunto.Contains(datos.VendedorGrupoDestino.Trim()))
            {
                return ResultadoPermisoClienteComercial.No(
                    $"No puedes asignar el grupo de producto a {datos.VendedorGrupoDestino.Trim()}: no es de tu equipo");
            }

            return ResultadoPermisoClienteComercial.Si();
        }

        /// <summary>Mismo criterio de "cambia" que AplicarCambiosClienteComercial: ambos con
        /// valor y distintos tras el trim (los char de la base de datos llegan con relleno).</summary>
        private static bool SonDistintos(string actual, string nuevo)
        {
            return actual != null && nuevo != null
                && !string.Equals(actual.Trim(), nuevo.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// El JWT de empleado (windows-token) lleva los grupos de AD como claims de rol, en
        /// formato NTAccount ("NUEVAVISION\Administración"): se compara lo que hay detrás de la
        /// última barra.
        /// </summary>
        internal static bool EsUsuarioDeOficina(IPrincipal user)
        {
            ClaimsIdentity identity = user?.Identity as ClaimsIdentity;
            if (identity == null)
            {
                return false;
            }
            return identity.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Substring(v.LastIndexOf('\\') + 1).Trim())
                .Any(grupo => GruposSinRestriccion.Contains(grupo));
        }

        /// <summary>
        /// El vendedor del usuario: el claim "Vendedor" si viene (NestoApp lo trae de serie) y,
        /// si no, UsuarioVendedor por el nombre SIN dominio (la tabla guarda "Antonio", no
        /// "NUEVAVISION\Antonio"). Null = usuario de oficina sin vendedor asociado.
        /// </summary>
        internal static string VendedorDelUsuario(IPrincipal user, IServicioUsuarioVendedor servicio)
        {
            ClaimsIdentity identity = user?.Identity as ClaimsIdentity;
            string delClaim = identity?.FindFirst("Vendedor")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(delClaim))
            {
                return delClaim;
            }

            string nombre = user?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }
            string sinDominio = nombre.Substring(nombre.LastIndexOf('\\') + 1).Trim();
            return servicio.ObtenerVendedorDeUsuario(sinDominio);
        }
    }

    public class ResultadoPermisoClienteComercial
    {
        public bool Permitido { get; private set; }
        public string Motivo { get; private set; }

        public static ResultadoPermisoClienteComercial Si()
        {
            return new ResultadoPermisoClienteComercial { Permitido = true };
        }

        public static ResultadoPermisoClienteComercial No(string motivo)
        {
            return new ResultadoPermisoClienteComercial { Permitido = false, Motivo = motivo };
        }
    }

    /// <summary>Los hechos sobre los que se evalúa el permiso, ya resueltos.</summary>
    internal class DatosPermisoClienteComercial
    {
        public bool EsOficina { get; set; }
        public string VendedorDelUsuario { get; set; }
        public List<string> EquipoDelUsuario { get; set; }
        public string VendedorActual { get; set; }
        public string VendedorDestino { get; set; }
        public string VendedorGrupoActual { get; set; }
        public string VendedorGrupoDestino { get; set; }
        public bool CambiaEstado { get; set; }
    }
}
