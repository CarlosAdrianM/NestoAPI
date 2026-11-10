using NestoAPI.Infraestructure.Clientes.Origenes;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Embudo
{
    /// <summary>A quién responde en Odoo el correo del embudo de un contacto (odoo-custom-addons#45).</summary>
    public class ResponsableEmbudo
    {
        public string Email { get; set; }
        /// <summary><see cref="ReglasResponsableEmbudo.TIPO_EQUIPO_ALBERTO"/>, <see cref="ReglasResponsableEmbudo.TIPO_VENDEDOR"/> o
        /// <see cref="ReglasResponsableEmbudo.TIPO_TIENDA_ONLINE"/>.</summary>
        public string Tipo { get; set; }
    }

    /// <summary>
    /// Embudo de Odoo con los clientes del vendedor general (odoo-custom-addons#45): el responsable de cada contacto sale del
    /// vendedor de SU código postal (el mismo que propone el alta de clientes y el importador de leads: CodigosPostales.Vendedor).
    /// Del equipo de Alberto Sancho (EquiposVenta vigente con Superior ASH, o el propio ASH) → Alberto; otro vendedor con correo →
    /// él; sin vendedor, el general o sin correo → la tienda online. Pura: los datos se los da <see cref="CalculadorResponsableEmbudo"/>.
    /// </summary>
    public static class ReglasResponsableEmbudo
    {
        public const string TIPO_EQUIPO_ALBERTO = "EquipoAlberto";
        public const string TIPO_VENDEDOR = "Vendedor";
        public const string TIPO_TIENDA_ONLINE = "TiendaOnline";

        /// <param name="vendedorCodigoPostal">El vendedor del código postal del contacto (null si no tiene CP o no existe).</param>
        /// <param name="correoVendedor">Vendedores.Mail de ese vendedor.</param>
        /// <param name="jefesDelVendedor">Sus jefes de ventas vigentes (EquiposVenta.Superior).</param>
        /// <param name="correoAlberto">Vendedores.Mail de ASH.</param>
        public static ResponsableEmbudo Decidir(string vendedorCodigoPostal, string correoVendedor, IEnumerable<string> jefesDelVendedor, string correoAlberto)
        {
            string vendedor = vendedorCodigoPostal?.Trim();
            if (string.IsNullOrEmpty(vendedor) || string.Equals(vendedor, Constantes.Vendedores.VENDEDOR_GENERAL, StringComparison.OrdinalIgnoreCase))
            {
                return TiendaOnline();
            }
            bool equipoAlberto = string.Equals(vendedor, ReglasAvisosLeads.VENDEDOR_JEFE_ZONA_CENTRO, StringComparison.OrdinalIgnoreCase)
                || (jefesDelVendedor ?? Enumerable.Empty<string>()).Any(j => string.Equals(j?.Trim(), ReglasAvisosLeads.VENDEDOR_JEFE_ZONA_CENTRO, StringComparison.OrdinalIgnoreCase));
            if (equipoAlberto)
            {
                // Sin el correo de Alberto en Vendedores (no debería pasar), mejor la tienda que un vendedor que no es
                return string.IsNullOrWhiteSpace(correoAlberto)
                    ? TiendaOnline()
                    : new ResponsableEmbudo { Email = correoAlberto.Trim(), Tipo = TIPO_EQUIPO_ALBERTO };
            }
            return string.IsNullOrWhiteSpace(correoVendedor)
                ? TiendaOnline()
                : new ResponsableEmbudo { Email = correoVendedor.Trim(), Tipo = TIPO_VENDEDOR };
        }

        private static ResponsableEmbudo TiendaOnline() =>
            new ResponsableEmbudo { Email = Constantes.Correos.TIENDA_ONLINE, Tipo = TIPO_TIENDA_ONLINE };
    }

    /// <summary>Los datos que necesita <see cref="ReglasResponsableEmbudo.Decidir"/> (sustituible en los tests).</summary>
    public interface IDatosResponsableEmbudo
    {
        /// <summary>El vendedor del código postal (null si no existe el CP).</summary>
        Task<string> VendedorDeCodigoPostal(string codigoPostal);
        Task<string> CorreoVendedor(string vendedor);
        List<string> JefesDe(string vendedor);
    }

    /// <summary>
    /// Lo de siempre: el vendedor del CP con <see cref="IServicioGestorClientes.CogerDatosCodigoPostal"/> (el del alta y el
    /// importador de leads), el correo con <see cref="IServicioGestorClientes.ObtenerEmailVendedor"/> y los jefes con
    /// <see cref="IDestinatariosLeadsSinLlamar.JefesDe"/> (los del aviso de leads).
    /// </summary>
    public class DatosResponsableEmbudo : IDatosResponsableEmbudo
    {
        private readonly IServicioGestorClientes servicio;
        private readonly IDestinatariosLeadsSinLlamar equipos;

        public DatosResponsableEmbudo(IServicioGestorClientes servicio, IDestinatariosLeadsSinLlamar equipos)
        {
            this.servicio = servicio;
            this.equipos = equipos;
        }

        public async Task<string> VendedorDeCodigoPostal(string codigoPostal)
        {
            try
            {
                return (await servicio.CogerDatosCodigoPostal(codigoPostal).ConfigureAwait(false))?.VendedorEstetica?.Trim();
            }
            catch (CodigoPostalNoExisteException)
            {
                return null;
            }
        }

        public Task<string> CorreoVendedor(string vendedor) => servicio.ObtenerEmailVendedor(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor);

        public List<string> JefesDe(string vendedor) => equipos.JefesDe(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor);
    }

    /// <summary>
    /// El responsable de cada contacto, con caché por código postal y por vendedor: los contactos de un bloque se repiten
    /// mucho de CP y de vendedor, y así una pasada del job no consulta lo mismo dos veces.
    /// </summary>
    public class CalculadorResponsableEmbudo
    {
        private readonly IDatosResponsableEmbudo datos;
        private readonly Dictionary<string, ResponsableEmbudo> porCodigoPostal = new Dictionary<string, ResponsableEmbudo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> correos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public CalculadorResponsableEmbudo(IDatosResponsableEmbudo datos)
        {
            this.datos = datos;
        }

        public async Task<ResponsableEmbudo> Calcular(string codigoPostal)
        {
            string cp = codigoPostal?.Trim() ?? string.Empty;
            if (porCodigoPostal.TryGetValue(cp, out ResponsableEmbudo conocido))
            {
                return conocido;
            }
            string vendedor = cp.Length == 0 ? null : await datos.VendedorDeCodigoPostal(cp).ConfigureAwait(false);
            ResponsableEmbudo responsable;
            if (string.IsNullOrWhiteSpace(vendedor) || string.Equals(vendedor.Trim(), Constantes.Vendedores.VENDEDOR_GENERAL, StringComparison.OrdinalIgnoreCase))
            {
                responsable = ReglasResponsableEmbudo.Decidir(vendedor, null, null, null);
            }
            else
            {
                vendedor = vendedor.Trim();
                List<string> jefes = datos.JefesDe(vendedor) ?? new List<string>();
                responsable = ReglasResponsableEmbudo.Decidir(vendedor, await Correo(vendedor).ConfigureAwait(false), jefes,
                    await Correo(ReglasAvisosLeads.VENDEDOR_JEFE_ZONA_CENTRO).ConfigureAwait(false));
            }
            porCodigoPostal[cp] = responsable;
            return responsable;
        }

        private async Task<string> Correo(string vendedor)
        {
            if (!correos.TryGetValue(vendedor, out string correo))
            {
                correo = (await datos.CorreoVendedor(vendedor).ConfigureAwait(false))?.Trim();
                correos[vendedor] = correo;
            }
            return correo;
        }
    }
}
