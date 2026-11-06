using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// Vendedores de NestoApp (06/11/26): la regla única de «el cliente es de este vendedor», para
    /// <see cref="ValidadorAccesoCliente"/> con comprobador. Vale cualquier cliente si el usuario tiene
    /// <see cref="PARAMETRO_VER_TODOS"/> = "1" (empresa 1). Si no, en la empresa del documento (o la por defecto), el cliente es
    /// suyo si alguna fila de Clientes de ese Nº Cliente (cualquier contacto) o de VendedoresClientesGruposProductos de ese
    /// cliente (cualquier contacto y grupo: el vendedor de peluquería) tiene de Vendedor a alguien de su EQUIPO
    /// (<see cref="IServicioVendedores.VendedoresEquipoString"/>: él mismo y, si es jefe, los suyos).
    /// </summary>
    public static class ClientesDelVendedor
    {
        /// <summary>El 403 de un vendedor con un documento o un cliente que no es suyo (de usted).</summary>
        public const string MOTIVO_SOLO_SUS_CLIENTES = "Solo puede enviar documentos de sus clientes.";

        /// <summary>Parámetro de usuario (ParametrosUsuario, empresa 1) que deja ver todos los clientes («1»).</summary>
        public const string PARAMETRO_VER_TODOS = "PermitirVerTodosLosPedidos";

        /// <summary>
        /// La regla entera con sus dependencias inyectadas (la BD o listas en los tests).
        /// </summary>
        /// <param name="usuario">El usuario del token (identity.Name), para <paramref name="puedeVerTodos"/>.</param>
        /// <param name="equipoDe">vendedor → los vendedores de su equipo (el propio se añade siempre).</param>
        /// <param name="puedeVerTodos">usuario → si tiene <see cref="PARAMETRO_VER_TODOS"/> = "1".</param>
        public static bool EsDelVendedor(string empresa, string usuario, string vendedor, string cliente,
            IQueryable<Cliente> clientes, IQueryable<VendedorClienteGrupoProducto> vendedoresGrupo,
            Func<string, IEnumerable<string>> equipoDe, Func<string, bool> puedeVerTodos)
        {
            string codigoVendedor = vendedor?.Trim();
            string codigoCliente = cliente?.Trim();
            if (string.IsNullOrEmpty(codigoVendedor) || string.IsNullOrEmpty(codigoCliente))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(usuario) && puedeVerTodos != null && puedeVerTodos(usuario.Trim()))
            {
                return true;
            }
            List<string> equipo = (equipoDe?.Invoke(codigoVendedor) ?? Enumerable.Empty<string>())
                .Select(v => v?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .Concat(new[] { codigoVendedor })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return EsDeAlgunoDe(clientes, vendedoresGrupo, empresa, equipo, codigoCliente);
        }

        /// <summary>
        /// El cliente tiene de vendedor (en Clientes o en VendedoresClientesGruposProductos) a alguno de <paramref name="vendedores"/>.
        /// En SQL Server la igualdad de char ya ignora los espacios de relleno, así que Empresa y cliente se comparan sin Trim
        /// (usa la clave); el Vendedor, con Trim (IN de la lista).
        /// </summary>
        internal static bool EsDeAlgunoDe(IQueryable<Cliente> clientes, IQueryable<VendedorClienteGrupoProducto> vendedoresGrupo,
            string empresa, List<string> vendedores, string cliente)
        {
            string empresaCliente = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            if (vendedores == null || vendedores.Count == 0 || string.IsNullOrEmpty(cliente))
            {
                return false;
            }
            if (clientes != null
                && clientes.Any(c => c.Empresa == empresaCliente && c.Nº_Cliente == cliente && vendedores.Contains(c.Vendedor.Trim())))
            {
                return true;
            }
            return vendedoresGrupo != null
                && vendedoresGrupo.Any(v => v.Empresa == empresaCliente && v.Cliente == cliente && vendedores.Contains(v.Vendedor.Trim()));
        }

        /// <summary>
        /// La regla contra la BD: abre su NVEntities, el equipo de <see cref="ServicioVendedores"/> (empresa 1, como en
        /// ClientesController y PedidosVentaController) y el parámetro con <see cref="LectorParametrosUsuario"/>.
        /// </summary>
        public static bool EsDelVendedor(string empresa, string usuario, string vendedor, string cliente)
        {
            IServicioVendedores servicioVendedores = new ServicioVendedores();
            ILectorParametrosUsuario lector = new LectorParametrosUsuario();
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return EsDelVendedor(empresa, usuario, vendedor, cliente, db.Clientes, db.VendedoresClientesGruposProductos,
                    // Síncrono a propósito (el comprobador del validador lo es); ServicioVendedores usa ConfigureAwait(false)
                    v => servicioVendedores.VendedoresEquipoString(Constantes.Empresas.EMPRESA_POR_DEFECTO, v).GetAwaiter().GetResult(),
                    u => lector.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO, u, PARAMETRO_VER_TODOS)?.Trim() == "1");
            }
        }

        /// <summary>
        /// ¿Es un vendedor de NestoApp (IsVendedor = "true" con su código en el claim "Vendedor") y no un empleado ni un cliente
        /// de la tienda? Los controladores que antes eran solo de empleados lo usan para abrir a los vendedores únicamente sus
        /// clientes, sin abrir nada a TNV.
        /// </summary>
        public static bool EsVendedorDeNestoApp(ClaimsIdentity identity)
            => identity != null
               && identity.FindFirst("IsVendedor")?.Value == "true"
               && !string.IsNullOrWhiteSpace(identity.FindFirst("Vendedor")?.Value)
               && identity.FindFirst("IsEmployee")?.Value != "true"
               && string.IsNullOrWhiteSpace(identity.FindFirst("cliente")?.Value);

        /// <summary>
        /// Para un vendedor de NestoApp: ¿puede trabajar con ese cliente? Pasa por <see cref="ValidadorAccesoCliente"/> con el
        /// comprobador dado (empresa, usuario del token, vendedor, cliente). False para cualquiera que no sea vendedor.
        /// </summary>
        public static bool PuedeElVendedor(ClaimsIdentity identity, string empresa, string cliente, Func<string, string, string, string, bool> esDelVendedor)
        {
            if (!EsVendedorDeNestoApp(identity) || esDelVendedor == null)
            {
                return false;
            }
            return ValidadorAccesoCliente.ValidarAcceso(identity, cliente,
                (vendedor, c) => esDelVendedor(empresa, identity.Name, vendedor, c)).Autorizado;
        }
    }
}
