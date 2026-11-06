using NestoAPI.Models;
using System;
using System.Linq;
using System.Security.Claims;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// Vendedores de NestoApp (06/11/26): la regla única de «el cliente es de este vendedor». En esa empresa (o en la por defecto
    /// si no se dice), lo es si alguna fila de Clientes de ese Nº Cliente (cualquier contacto) tiene ese Vendedor, o si lo tiene
    /// alguna fila de VendedoresClientesGruposProductos de ese cliente (cualquier contacto y grupo: el vendedor de peluquería).
    /// Para <see cref="ValidadorAccesoCliente"/> con comprobador.
    /// </summary>
    public static class ClientesDelVendedor
    {
        /// <summary>El 403 de un vendedor con un documento o un cliente que no es suyo (de usted).</summary>
        public const string MOTIVO_SOLO_SUS_CLIENTES = "Solo puede enviar documentos de sus clientes.";

        /// <summary>
        /// La regla sobre cualquier origen de clientes (la BD o listas en los tests). En SQL Server la igualdad de char ya
        /// ignora los espacios de relleno, así que Empresa y cliente se comparan sin Trim (usa la clave); el Vendedor, con Trim.
        /// </summary>
        public static bool EsDelVendedor(IQueryable<Cliente> clientes, IQueryable<VendedorClienteGrupoProducto> vendedoresGrupo,
            string empresa, string vendedor, string cliente)
        {
            string empresaCliente = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string codigoVendedor = vendedor?.Trim();
            string codigoCliente = cliente?.Trim();
            if (string.IsNullOrEmpty(codigoVendedor) || string.IsNullOrEmpty(codigoCliente))
            {
                return false;
            }
            if (clientes != null
                && clientes.Any(c => c.Empresa == empresaCliente && c.Nº_Cliente == codigoCliente && c.Vendedor.Trim() == codigoVendedor))
            {
                return true;
            }
            return vendedoresGrupo != null
                && vendedoresGrupo.Any(v => v.Empresa == empresaCliente && v.Cliente == codigoCliente && v.Vendedor.Trim() == codigoVendedor);
        }

        /// <summary>La regla contra la BD (abre su propio NVEntities).</summary>
        public static bool EsDelVendedor(string empresa, string vendedor, string cliente)
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return EsDelVendedor(db.Clientes, db.VendedoresClientesGruposProductos, empresa, vendedor, cliente);
            }
        }

        /// <summary>
        /// ¿Es un vendedor de NestoApp (IsVendedor = "true" con su código en el claim "Vendedor") y no un empleado ni un cliente
        /// de la tienda? Los controladores que
        /// antes eran solo de empleados lo usan para abrir a los vendedores únicamente sus clientes, sin abrir nada a TNV.
        /// </summary>
        public static bool EsVendedorDeNestoApp(ClaimsIdentity identity)
            => identity != null
               && identity.FindFirst("IsVendedor")?.Value == "true"
               && !string.IsNullOrWhiteSpace(identity.FindFirst("Vendedor")?.Value)
               && identity.FindFirst("IsEmployee")?.Value != "true"
               && string.IsNullOrWhiteSpace(identity.FindFirst("cliente")?.Value);

        /// <summary>
        /// Para un vendedor de NestoApp: ¿puede trabajar con ese cliente? Pasa por <see cref="ValidadorAccesoCliente"/> con el
        /// comprobador dado (empresa, vendedor, cliente). False para cualquiera que no sea vendedor.
        /// </summary>
        public static bool PuedeElVendedor(ClaimsIdentity identity, string empresa, string cliente, Func<string, string, string, bool> esDelVendedor)
        {
            if (!EsVendedorDeNestoApp(identity) || esDelVendedor == null)
            {
                return false;
            }
            return ValidadorAccesoCliente.ValidarAcceso(identity, cliente, (vendedor, c) => esDelVendedor(empresa, vendedor, c)).Autorizado;
        }
    }
}
