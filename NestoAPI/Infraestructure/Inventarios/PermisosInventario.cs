using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>
    /// NestoAPI#790 (Carlos, 03/11/26): quién consulta, contabiliza y a qué precio ve las diferencias del inventario.
    /// <list type="bullet">
    /// <item>Administración, Dirección e Informática: cualquier almacén; consultan y contabilizan.</item>
    /// <item>Almacén y Compras: cualquier almacén, consultan (comprobaciones y diferencias). Almacén además contabiliza ALG (Carlos 04/11/26);
    /// Compras no contabiliza.</item>
    /// <item>Tiendas: solo su almacén (AlmacenInventario o, si no lo tiene, AlmacénPedidoVta); consultan y contabilizan.</item>
    /// </list>
    /// Valoración: a coste (PrecioMedio) por defecto para Compras, Almacén, Administración, Dirección e Informática, que pueden pedir
    /// PVP; a PVP (precio profesional) para quien solo es de Tiendas, que nunca ve el coste.
    /// </summary>
    public static class PermisosInventario
    {
        public const string CLAVE_ALMACEN_INVENTARIO = "AlmacenInventario";
        public const string CLAVE_ALMACEN_PEDIDO_VTA = "AlmacénPedidoVta";

        public const string VALORACION_PVP = "PVP";
        public const string VALORACION_COSTE = "Coste";

        public const string MENSAJE_SIN_PERMISO =
            "El inventario lo consultan las tiendas (el suyo), Almacén, Compras, Administración y Dirección.";
        public const string MENSAJE_SIN_PERMISO_CONTABILIZAR =
            "Contabilizar el inventario lo hacen las tiendas (el suyo), Almacén (Algete), Administración y Dirección.";
        public const string MENSAJE_SIN_PERMISO_CONTAR =
            "El inventario lo cuentan las tiendas (el suyo), Almacén, Administración y Dirección.";
        public const string MENSAJE_SOLO_SU_TIENDA = "Solo puede trabajar con el inventario de su tienda ({0}).";
        public const string MENSAJE_SIN_TIENDA = "No tiene tienda asignada (parámetro AlmacénPedidoVta): pídasela a Informática.";
        public const string MENSAJE_COSTE_TIENDAS = "Las diferencias del inventario de las tiendas se ven a PVP, no a coste.";

        /// <summary>Administración, Dirección e Informática: cualquier almacén, también contabilizar.</summary>
        public static bool EsGlobal(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        /// <summary>Los de <see cref="EsGlobal"/> más Almacén y Compras: consultan cualquier almacén y ven el coste.</summary>
        public static bool EsLectorGlobal(IPrincipal usuario) =>
            EsGlobal(usuario) || (usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.COMPRAS)));

        public static bool EsTienda(IPrincipal usuario) =>
            usuario != null && usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS);

        /// <summary>null si puede consultar el inventario de <paramref name="almacen"/>; si no, el motivo (403).</summary>
        public static string MotivoNoPuedeConsultar(IPrincipal usuario, string almacenUsuario, string almacen)
        {
            if (EsLectorGlobal(usuario))
            {
                return null;
            }
            return MotivoTienda(usuario, almacenUsuario, almacen, MENSAJE_SIN_PERMISO);
        }

        /// <summary>
        /// null si puede contabilizar el inventario de <paramref name="almacen"/>; si no, el motivo (403).
        /// NestoAPI#784 (Carlos, 04/11/26): el inventario de Algete lo contabilizan Santiago, Andre o Alfredo (grupo Almacén) desde
        /// Nesto; Almacén solo puede contabilizar ALG (las tiendas siguen siendo de cada tienda, Administración o Dirección).
        /// </summary>
        public static string MotivoNoPuedeContabilizar(IPrincipal usuario, string almacenUsuario, string almacen)
        {
            if (EsGlobal(usuario))
            {
                return null;
            }
            if (EsAlmacen(usuario))
            {
                return string.Equals(almacen?.Trim(), Constantes.Almacenes.ALGETE, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : MENSAJE_ALMACEN_SOLO_ALGETE;
            }
            return MotivoTienda(usuario, almacenUsuario, almacen, MENSAJE_SIN_PERMISO_CONTABILIZAR);
        }

        public const string MENSAJE_ALMACEN_SOLO_ALGETE = "Almacén solo puede contabilizar el inventario de Algete (ALG); el de las tiendas lo contabiliza cada tienda, Administración o Dirección.";

        public static bool EsAlmacen(IPrincipal usuario) =>
            usuario != null && usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN);

        /// <summary>
        /// NestoAPI#795: null si puede contar (rellenar y meter lo contado) en el inventario de <paramref name="almacen"/>: Almacén y los de
        /// <see cref="EsGlobal"/> cualquiera; Tiendas, el suyo. Compras no cuenta.
        /// </summary>
        public static string MotivoNoPuedeContar(IPrincipal usuario, string almacenUsuario, string almacen)
        {
            if (EsGlobal(usuario) || (usuario != null && usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN)))
            {
                return null;
            }
            return MotivoTienda(usuario, almacenUsuario, almacen, MENSAJE_SIN_PERMISO_CONTAR);
        }

        /// <summary>
        /// La valoración con la que ve las diferencias: «PVP» o «Coste». <paramref name="pedida"/>: null/vacía (la suya), «pvp» o
        /// «coste». <paramref name="motivo"/> lleva el porqué cuando devuelve null (403; 400 si no es ni pvp ni coste).
        /// </summary>
        public static string Valoracion(IPrincipal usuario, string pedida, out string motivo, out bool peticionIncorrecta)
        {
            motivo = null;
            peticionIncorrecta = false;
            string valor = pedida?.Trim();
            bool lectorGlobal = EsLectorGlobal(usuario);
            bool tienda = EsTienda(usuario);
            if (!lectorGlobal && !tienda)
            {
                motivo = MENSAJE_SIN_PERMISO;
                return null;
            }
            if (string.IsNullOrEmpty(valor))
            {
                return lectorGlobal ? VALORACION_COSTE : VALORACION_PVP;
            }
            if (string.Equals(valor, VALORACION_PVP, StringComparison.OrdinalIgnoreCase))
            {
                return VALORACION_PVP;
            }
            if (string.Equals(valor, VALORACION_COSTE, StringComparison.OrdinalIgnoreCase))
            {
                if (lectorGlobal)
                {
                    return VALORACION_COSTE;
                }
                motivo = MENSAJE_COSTE_TIENDAS;
                return null;
            }
            motivo = "La valoración tiene que ser «pvp» o «coste».";
            peticionIncorrecta = true;
            return null;
        }

        private static string MotivoTienda(IPrincipal usuario, string almacenUsuario, string almacen, string sinPermiso)
        {
            if (!EsTienda(usuario))
            {
                return sinPermiso;
            }
            string suyo = almacenUsuario?.Trim();
            if (string.IsNullOrEmpty(suyo))
            {
                return MENSAJE_SIN_TIENDA;
            }
            return string.Equals(suyo, almacen?.Trim(), StringComparison.OrdinalIgnoreCase)
                ? null
                : string.Format(MENSAJE_SOLO_SU_TIENDA, suyo);
        }
    }
}
