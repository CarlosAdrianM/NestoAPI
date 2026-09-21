using NestoAPI.Infraestructure;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>
    /// NestoAPI#501: familias que **solo** pueden ofrecer los vendedores presenciales
    /// (<c>Vendedores.Estado = 0</c>). La primera es Kinetics.
    ///
    /// <para>El filtro vive en el servidor a propósito: Nesto, NestoApp y la tienda de clientes
    /// llaman a los mismos endpoints, así que la regla se escribe una vez. Quien no se identifica
    /// (la tienda de PrestaShop llama al buscador sin token) NO las ve: el criterio por defecto es
    /// ocultarlas.</para>
    ///
    /// <para>La lista es diminuta y cambia como mucho una vez al año, así que se cachea cinco
    /// minutos, igual que las agencias.</para>
    /// </summary>
    public static class FamiliasRestringidas
    {
        private static readonly TimeSpan DuracionCache = TimeSpan.FromMinutes(5);
        private static readonly object candado = new object();
        private static HashSet<string> cache;
        private static DateTime cacheHasta = DateTime.MinValue;

        /// <summary>Códigos de familia restringidos, en mayúsculas y sin relleno.</summary>
        public static HashSet<string> Codigos(NVEntities db)
        {
            lock (candado)
            {
                if (cache != null && DateTime.Now < cacheHasta)
                {
                    return cache;
                }
            }

            List<string> familias = db.Familias
                .Where(f => f.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && f.SoloVentaPresencial)
                .Select(f => f.Número)
                .ToList();

            HashSet<string> nuevo = new HashSet<string>(
                familias.Select(f => f.Trim()), StringComparer.OrdinalIgnoreCase);

            lock (candado)
            {
                cache = nuevo;
                cacheHasta = DateTime.Now.Add(DuracionCache);
                return cache;
            }
        }

        /// <summary>Para los tests y para cuando se marca una familia desde Nesto.</summary>
        public static void VaciarCache()
        {
            lock (candado)
            {
                cache = null;
                cacheHasta = DateTime.MinValue;
            }
        }

        public static bool EsRestringida(NVEntities db, string familia)
        {
            return !string.IsNullOrWhiteSpace(familia) && Codigos(db).Contains(familia.Trim());
        }

        /// <summary>
        /// ¿Puede este usuario ver las familias restringidas? Cuatro puertas, y basta una:
        ///
        /// <list type="bullet">
        /// <item>ser <b>vendedor presencial</b> (<c>Vendedores.Estado = 0</c>), que es el equipo de calle;</item>
        /// <item>estar en <b>Dirección</b>;</item>
        /// <item>estar en <b>Almacén</b> — inventarios, abonos y devoluciones necesitan ver el producto
        /// aunque quien esté en el almacén no lo venda (Carlos, 21/09/26);</item>
        /// <item>tener el permiso <c>PermitirVenderFamiliasRestringidas</c>: quien puede saltarse la
        /// denegación tiene que poder ver la familia, o no podría crear el pedido de la excepción.
        /// Carlos y Manuel son vendedores «mini» (Estado 2) y sin esto no veían lo que sí podían
        /// vender.</item>
        /// </list>
        ///
        /// <para>Sin usuario identificado, no: el buscador es anónimo y lo llama la tienda.</para>
        /// </summary>
        public static bool PuedeVerlas(IPrincipal user, NVEntities db,
            IServicioUsuarioVendedor servicioUsuarioVendedor = null, ILectorParametrosUsuario lectorParametros = null)
        {
            if (user?.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            if (user.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || user.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN))
            {
                return true;
            }

            if (TienePermisoDeVenta(user, lectorParametros ?? new LectorParametrosUsuario()))
            {
                return true;
            }

            string vendedor = VendedorDelUsuario(user, servicioUsuarioVendedor ?? new ServicioUsuarioVendedor());
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                return false;
            }

            string codigo = vendedor.Trim();
            return db.Vendedores.Any(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                                       && v.Número == codigo
                                       && v.Estado == (short)Constantes.Vendedores.ESTADO_VENDEDOR_PRESENCIAL);
        }

        /// <summary>
        /// El mismo parámetro que levanta la denegación al vender (#501), leído con el mismo criterio
        /// que el servidor usa en <c>PedidosVentaController</c>: "1", "TRUE", "SI" o "SÍ".
        /// </summary>
        internal static bool TienePermisoDeVenta(IPrincipal user, ILectorParametrosUsuario lectorParametros)
        {
            string usuario = UsuarioSinDominio(user?.Identity?.Name);
            if (string.IsNullOrWhiteSpace(usuario) || lectorParametros == null)
            {
                return false;
            }

            string valor;
            try
            {
                valor = lectorParametros.LeerParametro(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, usuario,
                    Constantes.ParametrosUsuario.PERMITIR_VENDER_FAMILIAS_RESTRINGIDAS);
            }
            catch (System.Exception)
            {
                // Ante la duda, no se ensenan: es el criterio de toda esta clase.
                return false;
            }

            if (string.IsNullOrWhiteSpace(valor))
            {
                return false;
            }

            valor = valor.Trim().ToUpperInvariant();
            return valor == "1" || valor == "TRUE" || valor == "SI" || valor == "SÍ";
        }

        internal static string UsuarioSinDominio(string usuario)
        {
            return string.IsNullOrWhiteSpace(usuario)
                ? null
                : usuario.Substring(usuario.LastIndexOf('\\') + 1).Trim();
        }

        /// <summary>
        /// El vendedor del usuario: el claim "Vendedor" si viene (NestoApp lo trae de serie) y, si
        /// no, <c>UsuarioVendedor</c> por el nombre sin dominio. Mismo criterio que
        /// <c>ValidadorCambioClienteComercial</c>.
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
}
