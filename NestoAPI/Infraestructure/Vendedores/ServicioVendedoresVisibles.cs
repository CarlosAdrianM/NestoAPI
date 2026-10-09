using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Vendedores
{
    /// <summary>Nesto#521: un vendedor que el usuario puede elegir en «clientes para contactar».</summary>
    public class VendedorVisibleDTO
    {
        public string Vendedor { get; set; }
        public string Nombre { get; set; }
    }

    /// <summary>
    /// Nesto#521 / NestoApp#219: de qué vendedores puede ver un usuario las sugerencias de contacto (y la lista antigua de
    /// GetClientesProbabilidadVenta). Una sola regla para el desplegable y para el 403:
    /// <list type="bullet">
    /// <item>Dirección (grupo de AD o rol «Dirección»): cualquier vendedor; el desplegable ofrece los activos.</item>
    /// <item>Jefe de ventas: el suyo y los de su equipo (EquiposVenta, Superior → Vendedor, en fechas), vía
    /// <see cref="IServicioVendedores.VendedoresEquipo"/>.</item>
    /// <item>Resto: solo el suyo. Sin vendedor asociado y sin ser Dirección: ninguno.</item>
    /// </list>
    /// <para>«El suyo» se busca en los tres sitios donde lo tienen los clientes, para no romper a ninguno: el parámetro de
    /// usuario «Vendedor» (el que usan Nesto y NestoApp para decidir qué vendedor piden), el claim «Vendedor» del JWT de
    /// NestoApp y la tabla UsuarioVendedor (de donde sale ese claim). Hay usuarios que solo están en uno de los sitios
    /// (en oct/26: Eva, Kelma y Noelia solo en ParametrosUsuario; Mamen con dos filas en UsuarioVendedor), así que valen todos.</para>
    /// </summary>
    public interface IServicioVendedoresVisibles
    {
        /// <summary>Los vendedores que se le ofrecen al usuario: primero el suyo, luego el resto por nombre.</summary>
        Task<List<VendedorVisibleDTO>> LeerVisibles(IPrincipal usuario, string empresa);

        /// <summary>True si el usuario puede ver las sugerencias de <paramref name="vendedor"/>.</summary>
        Task<bool> PuedeVer(IPrincipal usuario, string empresa, string vendedor);
    }

    public class ServicioVendedoresVisibles : IServicioVendedoresVisibles
    {
        public const string MENSAJE_SIN_PERMISO = "Solo puedes ver los clientes para contactar de tu vendedor o de los vendedores de tu equipo.";
        public const string CLAVE_PARAMETRO_VENDEDOR = "Vendedor";
        public const string CLAIM_VENDEDOR = "Vendedor";

        private readonly NVEntities db;
        private readonly IServicioVendedores servicioVendedores;

        public ServicioVendedoresVisibles(NVEntities db, IServicioVendedores servicioVendedores = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.servicioVendedores = servicioVendedores ?? new ServicioVendedores();
        }

        internal static bool EsDireccion(IPrincipal usuario)
        {
            return usuario != null && usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION);
        }

        public async Task<bool> PuedeVer(IPrincipal usuario, string empresa, string vendedor)
        {
            string pedido = Normalizar(vendedor);
            if (pedido == null || usuario?.Identity?.IsAuthenticated != true)
            {
                return false;
            }
            if (EsDireccion(usuario))
            {
                return true;
            }
            empresa = EmpresaOPorDefecto(empresa);
            List<string> propios = await VendedoresDelUsuario(usuario, empresa).ConfigureAwait(false);
            if (propios.Contains(pedido))
            {
                return true;
            }
            foreach (string propio in propios)
            {
                List<VendedorDTO> equipo = await servicioVendedores.VendedoresEquipo(empresa, propio).ConfigureAwait(false) ?? new List<VendedorDTO>();
                if (equipo.Any(v => Normalizar(v.vendedor) == pedido))
                {
                    return true;
                }
            }
            return false;
        }

        public async Task<List<VendedorVisibleDTO>> LeerVisibles(IPrincipal usuario, string empresa)
        {
            if (usuario?.Identity?.IsAuthenticated != true)
            {
                return new List<VendedorVisibleDTO>();
            }
            empresa = EmpresaOPorDefecto(empresa);
            List<string> propios = await VendedoresDelUsuario(usuario, empresa).ConfigureAwait(false);
            var nombres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (EsDireccion(usuario))
            {
                var activos = await db.Vendedores
                    .Where(v => v.Empresa == empresa && v.Estado >= 0)
                    .Select(v => new { v.Número, v.Descripción })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var v in activos)
                {
                    AnadirNombre(nombres, v.Número, v.Descripción);
                }
            }
            else
            {
                foreach (string propio in propios)
                {
                    List<VendedorDTO> equipo = await servicioVendedores.VendedoresEquipo(empresa, propio).ConfigureAwait(false) ?? new List<VendedorDTO>();
                    foreach (VendedorDTO v in equipo)
                    {
                        AnadirNombre(nombres, v.vendedor, v.nombre);
                    }
                }
            }

            List<string> propiosSinNombre = propios.Where(p => !nombres.ContainsKey(p)).ToList();
            if (propiosSinNombre.Any())
            {
                // El propio va siempre, aunque esté de baja (a Dirección solo se le cargan los activos).
                var sinNombre = await db.Vendedores
                    .Where(v => v.Empresa == empresa && propiosSinNombre.Contains(v.Número))
                    .Select(v => new { v.Número, v.Descripción })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var v in sinNombre)
                {
                    AnadirNombre(nombres, v.Número, v.Descripción);
                }
            }

            var resultado = propios
                .Select(p => new VendedorVisibleDTO { Vendedor = p, Nombre = nombres.TryGetValue(p, out string nombre) ? nombre : p })
                .ToList();
            resultado.AddRange(nombres
                .Where(n => !propios.Contains(n.Key))
                .Select(n => new VendedorVisibleDTO { Vendedor = n.Key, Nombre = n.Value })
                .OrderBy(v => v.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(v => v.Vendedor, StringComparer.OrdinalIgnoreCase));
            return resultado;
        }

        /// <summary>
        /// El vendedor (o vendedores) del usuario, normalizados (sin espacios y en mayúsculas) y sin repetir. Primero el del
        /// parámetro de usuario, que es el que Nesto y NestoApp eligen por defecto.
        /// </summary>
        internal async Task<List<string>> VendedoresDelUsuario(IPrincipal usuario, string empresa)
        {
            var propios = new List<string>();
            void Anadir(string vendedor)
            {
                string limpio = Normalizar(vendedor);
                if (limpio != null && !propios.Contains(limpio))
                {
                    propios.Add(limpio);
                }
            }

            string nombre = UsuarioSinDominio(usuario?.Identity?.Name);
            if (nombre != null)
            {
                List<string> delParametro = await db.ParametrosUsuario
                    .Where(p => p.Empresa == empresa && p.Usuario == nombre && p.Clave == CLAVE_PARAMETRO_VENDEDOR)
                    .Select(p => p.Valor)
                    .ToListAsync().ConfigureAwait(false);
                delParametro.ForEach(Anadir);
            }

            Anadir((usuario?.Identity as ClaimsIdentity)?.FindFirst(CLAIM_VENDEDOR)?.Value);

            if (nombre != null)
            {
                List<string> deLaTabla = await db.UsuarioVendedores
                    .Where(uv => uv.Usuario == nombre)
                    .Select(uv => uv.Vendedor)
                    .ToListAsync().ConfigureAwait(false);
                deLaTabla.ForEach(Anadir);
            }
            return propios;
        }

        internal static string UsuarioSinDominio(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }
            string sinDominio = usuario.Substring(usuario.LastIndexOf('\\') + 1).Trim();
            return sinDominio.Length == 0 ? null : sinDominio;
        }

        private static string Normalizar(string vendedor)
        {
            string limpio = vendedor?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }

        private static string EmpresaOPorDefecto(string empresa)
        {
            return string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
        }

        private static void AnadirNombre(Dictionary<string, string> nombres, string vendedor, string nombre)
        {
            string limpio = Normalizar(vendedor);
            if (limpio != null && !nombres.ContainsKey(limpio))
            {
                nombres[limpio] = string.IsNullOrWhiteSpace(nombre) ? limpio : nombre.Trim();
            }
        }
    }
}
