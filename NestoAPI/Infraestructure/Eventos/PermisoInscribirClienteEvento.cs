using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#741: los datos que hacen falta para saber si un usuario de la casa puede inscribir a un cliente en un evento.
    /// </summary>
    public interface ILectorVendedoresInscripcion
    {
        /// <summary>El vendedor del usuario: el claim «Vendedor» (NestoApp) o su parámetro de usuario «Vendedor». Null si no tiene.</summary>
        string VendedorDelUsuario(IPrincipal usuario);

        /// <summary>Los vendedores del equipo de <paramref name="vendedor"/> si es jefe de equipo (EquiposVenta vigente), él incluido.</summary>
        Task<List<string>> VendedoresDelEquipo(string empresa, string vendedor);

        /// <summary>
        /// Los vendedores del cliente: el de estética del contacto (Clientes.Vendedor) y el de peluquería
        /// (VendedoresClienteGrupoProducto, PEL) del contacto y del contacto principal.
        /// </summary>
        Task<List<string>> VendedoresDelCliente(string empresa, string cliente, string contacto);
    }

    /// <summary>NestoAPI#741: el lector de producción (ParametrosUsuario, EquiposVenta y la ficha del cliente).</summary>
    public class LectorVendedoresInscripcion : ILectorVendedoresInscripcion
    {
        private const string CLAVE_VENDEDOR = "Vendedor";
        private readonly NVEntities db;
        private readonly IServicioVendedores servicioVendedores;

        public LectorVendedoresInscripcion(NVEntities db, IServicioVendedores servicioVendedores = null)
        {
            this.db = db;
            this.servicioVendedores = servicioVendedores ?? new ServicioVendedores();
        }

        public string VendedorDelUsuario(IPrincipal usuario)
        {
            string delClaim = (usuario?.Identity as ClaimsIdentity)?.FindFirst(CLAVE_VENDEDOR)?.Value?.Trim();
            if (!string.IsNullOrEmpty(delClaim))
            {
                return delClaim;
            }
            string sinDominio = PermisosEventos.SinDominio(usuario?.Identity?.Name);
            if (sinDominio == null)
            {
                return null;
            }
            // Solo el parámetro del propio usuario (como LectorComercialesPedido): el de «(defecto)» no hace a nadie comercial.
            string empresaDefecto = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string valor = db.ParametrosUsuario
                .Where(p => p.Empresa == empresaDefecto && p.Clave == CLAVE_VENDEDOR && p.Usuario == sinDominio)
                .Select(p => p.Valor)
                .FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(valor) ? null : valor;
        }

        public async Task<List<string>> VendedoresDelEquipo(string empresa, string vendedor) =>
            await servicioVendedores.VendedoresEquipoString(empresa, vendedor).ConfigureAwait(false) ?? new List<string>();

        public async Task<List<string>> VendedoresDelCliente(string empresa, string cliente, string contacto)
        {
            string contactoLimpio = contacto?.Trim();
            var fichas = await db.Clientes
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente)
                .Select(c => new { c.Contacto, c.ClientePrincipal, c.Vendedor })
                .ToListAsync().ConfigureAwait(false);
            string grupoPeluqueria = Constantes.Productos.GRUPO_PELUQUERIA;
            var peluqueria = await db.VendedoresClientesGruposProductos
                .Where(v => v.Empresa == empresa && v.Cliente == cliente && v.GrupoProducto == grupoPeluqueria)
                .Select(v => new { v.Contacto, v.Vendedor })
                .ToListAsync().ConfigureAwait(false);

            var ficha = fichas.FirstOrDefault(f => f.Contacto?.Trim() == contactoLimpio)
                ?? fichas.FirstOrDefault(f => f.ClientePrincipal);
            string principal = fichas.FirstOrDefault(f => f.ClientePrincipal)?.Contacto?.Trim();
            string delContacto = ficha?.Contacto?.Trim();

            var vendedores = new List<string> { ficha?.Vendedor };
            vendedores.AddRange(peluqueria
                .Where(p => p.Contacto?.Trim() == delContacto || (principal != null && p.Contacto?.Trim() == principal))
                .Select(p => p.Vendedor));
            return vendedores
                .Select(v => v?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// NestoAPI#741 (sugerencia 863, Carlos 29/10/26): ¿puede este usuario inscribir a este cliente en un evento? Junta los
    /// datos (<see cref="ILectorVendedoresInscripcion"/>) y aplica la regla de <see cref="PermisosEventos.PuedeInscribir"/>;
    /// si no puede, 403 con <see cref="PermisosEventos.MENSAJE_SIN_PERMISO_INSCRIBIR"/>. Solo para «Inscribir» a petición de
    /// un usuario: las inscripciones automáticas (señal, plantilla, PrestaShop) no pasan por aquí.
    /// </summary>
    public class AutorizadorInscripcionEventos
    {
        private readonly ILectorVendedoresInscripcion lector;

        public AutorizadorInscripcionEventos(ILectorVendedoresInscripcion lector)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
        }

        public async Task<bool> PuedeInscribir(IPrincipal usuario, string empresa, string cliente, string contacto)
        {
            if (usuario == null)
            {
                return false;
            }
            if (PermisosEventos.ClienteDelToken(usuario) != null || PermisosEventos.PuedeInscribirACualquiera(usuario))
            {
                return PermisosEventos.PuedeInscribir(usuario, cliente, null, null, null);
            }
            string vendedor = lector.VendedorDelUsuario(usuario);
            if (vendedor == null)
            {
                return false;
            }
            List<string> equipo = await lector.VendedoresDelEquipo(empresa, vendedor).ConfigureAwait(false);
            List<string> delCliente = await lector.VendedoresDelCliente(empresa, cliente, contacto).ConfigureAwait(false);
            return PermisosEventos.PuedeInscribir(usuario, cliente, vendedor, equipo, delCliente);
        }

        /// <summary>Como <see cref="PuedeInscribir"/>, pero si no puede lanza el 403 de usted.</summary>
        public async Task Comprobar(IPrincipal usuario, string empresa, string cliente, string contacto)
        {
            if (!await PuedeInscribir(usuario, empresa, cliente, contacto).ConfigureAwait(false))
            {
                throw new NestoBusinessException(PermisosEventos.MENSAJE_SIN_PERMISO_INSCRIBIR) { StatusCode = HttpStatusCode.Forbidden };
            }
        }
    }
}
