using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#665/#666 (Sugerencias 584 y 672 de Novedades): un comercial al que le toca un pedido.
    /// </summary>
    public class ComercialPedido
    {
        public string Vendedor { get; set; }
        public string Nombre { get; set; }
        /// <summary>Vendedores.Mail, ya recortado. Null si no tiene.</summary>
        public string Correo { get; set; }
        /// <summary>Usuarios (sin dominio) que tienen este vendedor en ParametrosUsuario (clave «Vendedor»).</summary>
        public List<string> Usuarios { get; set; } = new List<string>();
        /// <summary>True si es el vendedor de peluquería; false si es el de estética (el de la cabecera).</summary>
        public bool EsPeluqueria { get; set; }
    }

    /// <summary>
    /// NestoAPI#665/#666: quién tiene que enterarse de lo que pasa con un pedido. Los comerciales según
    /// sus líneas (estética y/o peluquería) y, como respaldo, el usuario que lo grabó.
    /// </summary>
    public class ComercialesPedido
    {
        private const string DOMINIO = "NUEVAVISION\\";

        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        /// <summary>Quien grabó el pedido (CabPedidoVta.Usuario, sin dominio).</summary>
        public string UsuarioPedido { get; set; }
        /// <summary>CorreoDefecto de quien grabó el pedido. Null si no lo tiene.</summary>
        public string CorreoUsuarioPedido { get; set; }
        /// <summary>
        /// Novedad 716 (Laura): el pedido es de la tienda online o de un canal externo (Amazon, Perfumes Club,
        /// Miravia), según la forma de venta de sus líneas (<see cref="Constantes.FormasVenta.EsCanalExterno"/>).
        /// Esos pedidos los llevan Laura y Enrique desde tiendaonline@.
        /// </summary>
        public bool EsCanalExterno { get; set; }
        public List<ComercialPedido> Comerciales { get; set; } = new List<ComercialPedido>();

        /// <summary>
        /// Los comerciales que se nombran (columna «Comercial» del correo de retenidos).
        /// </summary>
        public string NombresComerciales => string.Join(", ", Comerciales
            .Select(c => string.IsNullOrWhiteSpace(c.Nombre) ? c.Vendedor : c.Nombre)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Correos a los que va el aviso: el de cada comercial real (no el vendedor general) con correo
        /// válido; por cada comercial que no lo tenga (o si no hay comerciales), el de quien grabó el pedido
        /// (lo que se hacía antes de #665). Sin duplicados y en minúsculas.
        /// </summary>
        public List<string> Correos()
        {
            var correos = new List<string>();
            bool hayQueUsarRespaldo = !Comerciales.Any();
            foreach (ComercialPedido comercial in Comerciales)
            {
                if (EsComercialReal(comercial) && EsCorreoValido(comercial.Correo))
                {
                    Anadir(correos, comercial.Correo);
                }
                else
                {
                    hayQueUsarRespaldo = true;
                }
            }
            if (hayQueUsarRespaldo && EsCorreoValido(CorreoUsuarioPedido))
            {
                Anadir(correos, CorreoUsuarioPedido);
            }
            return correos;
        }

        /// <summary>
        /// Usuarios (con dominio, como los guarda el buzón de Nesto) que reciben el aviso en la campana:
        /// los de cada comercial real; por cada comercial sin usuario (o si no hay comerciales), quien
        /// grabó el pedido. Sin duplicados.
        /// </summary>
        public List<string> UsuariosBuzon()
        {
            var usuarios = new List<string>();
            bool hayQueUsarRespaldo = !Comerciales.Any();
            foreach (ComercialPedido comercial in Comerciales)
            {
                List<string> suyos = EsComercialReal(comercial)
                    ? (comercial.Usuarios ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).ToList()
                    : new List<string>();
                if (suyos.Any())
                {
                    suyos.ForEach(u => Anadir(usuarios, DOMINIO + SinDominio(u)));
                }
                else
                {
                    hayQueUsarRespaldo = true;
                }
            }
            if (hayQueUsarRespaldo && !string.IsNullOrWhiteSpace(UsuarioPedido))
            {
                Anadir(usuarios, DOMINIO + SinDominio(UsuarioPedido));
            }
            return usuarios;
        }

        /// <summary>
        /// LA regla (la misma que la factura, CargarVendedoresPedido): si el pedido tiene líneas de peluquería
        /// (grupo PEL) le toca el vendedor de peluquería del pedido; si tiene de cualquier otro grupo
        /// (estética), el vendedor de la cabecera. Con los dos tipos, los dos.
        /// </summary>
        /// <param name="gruposLineas">El grupo de cada línea (puede venir con espacios o null).</param>
        /// <param name="peluqueriaSinVendedorVaAlDeEstetica">Si hay líneas de peluquería pero el pedido no
        /// tiene vendedor de peluquería: true = le toca al de estética (avisos: alguien tiene que enterarse);
        /// false = a nadie (lo que hace la factura desde siempre).</param>
        public static List<string> CodigosVendedores(IEnumerable<string> gruposLineas, string vendedorEstetica,
            string vendedorPeluqueria, bool peluqueriaSinVendedorVaAlDeEstetica)
        {
            List<string> grupos = (gruposLineas ?? Enumerable.Empty<string>()).Select(g => g?.Trim()).ToList();
            bool hayPeluqueria = grupos.Any(g => g == Constantes.Productos.GRUPO_PELUQUERIA);
            bool hayNoPeluqueria = grupos.Any(g => g != Constantes.Productos.GRUPO_PELUQUERIA);
            bool tienePeluqueria = !string.IsNullOrWhiteSpace(vendedorPeluqueria);

            var codigos = new List<string>();
            if (hayPeluqueria && tienePeluqueria)
            {
                codigos.Add(vendedorPeluqueria.Trim());
            }
            bool esteticaPorPeluqueriaHuerfana = hayPeluqueria && !tienePeluqueria && peluqueriaSinVendedorVaAlDeEstetica;
            if ((hayNoPeluqueria || esteticaPorPeluqueriaHuerfana) && !string.IsNullOrWhiteSpace(vendedorEstetica)
                && !codigos.Contains(vendedorEstetica.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                codigos.Add(vendedorEstetica.Trim());
            }
            return codigos;
        }

        internal static bool EsComercialReal(ComercialPedido comercial) =>
            comercial != null && !string.IsNullOrWhiteSpace(comercial.Vendedor)
            && !string.Equals(comercial.Vendedor.Trim(), Constantes.Vendedores.VENDEDOR_GENERAL, StringComparison.OrdinalIgnoreCase);

        internal static bool EsCorreoValido(string correo)
        {
            if (string.IsNullOrWhiteSpace(correo))
            {
                return false;
            }
            try
            {
                var direccion = new MailAddress(correo.Trim());
                return direccion.Address == correo.Trim();
            }
            catch (FormatException)
            {
                return false;
            }
        }

        internal static string SinDominio(string usuario) =>
            usuario?.Substring(usuario.IndexOf('\\') + 1).Trim();

        private static void Anadir(List<string> lista, string valor)
        {
            string limpio = valor.Trim();
            if (limpio.Contains("@"))
            {
                limpio = limpio.ToLowerInvariant();
            }
            if (!lista.Contains(limpio, StringComparer.OrdinalIgnoreCase))
            {
                lista.Add(limpio);
            }
        }
    }

    public interface ILectorComercialesPedido
    {
        /// <summary>Null si el pedido no existe.</summary>
        ComercialesPedido Leer(string empresa, int pedido);

        /// <summary>
        /// NestoAPI#666: para lo que no tiene pedido (un envío suelto): el comercial que se indica y, como
        /// respaldo, el usuario dado.
        /// </summary>
        ComercialesPedido LeerDeVendedor(string empresa, string vendedor, string usuarioRespaldo);
    }

    /// <summary>
    /// NestoAPI#665/#666: lee de la BD los comerciales de un pedido. Los vendedores son los DEL PEDIDO
    /// (CabPedidoVta.Vendedor para estética y VendedoresPedidosGruposProductos con grupo PEL para
    /// peluquería), que se copian de la ficha del cliente (Clientes.Vendedor y VendedoresClienteGrupoProducto)
    /// al crear el pedido y son los que comisionan; la factura lee los mismos.
    /// </summary>
    public class LectorComercialesPedido : ILectorComercialesPedido
    {
        private const string CLAVE_VENDEDOR = "Vendedor";
        private const string USUARIO_DEFECTO = "(defecto)";

        private readonly NVEntities db;

        public LectorComercialesPedido(NVEntities db)
        {
            this.db = db;
        }

        public ComercialesPedido Leer(string empresa, int pedido)
        {
            CabPedidoVta cabecera = db.CabPedidoVtas.FirstOrDefault(p => p.Empresa == empresa && p.Número == pedido);
            if (cabecera == null)
            {
                return null;
            }

            // Solo las líneas de producto: las de portes o texto no tienen grupo y no deben meter al de estética
            // en un pedido que es todo de peluquería.
            var lineas = db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && l.Número == pedido && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                .Select(l => new { l.Grupo, l.Forma_Venta })
                .ToList();
            List<string> grupos = lineas.Select(l => l.Grupo).ToList();
            string vendedorPeluqueria = db.VendedoresPedidosGruposProductos
                .Where(v => v.Empresa == empresa && v.Pedido == pedido && v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA)
                .Select(v => v.Vendedor)
                .FirstOrDefault();

            List<string> codigos = ComercialesPedido.CodigosVendedores(grupos, cabecera.Vendedor, vendedorPeluqueria,
                peluqueriaSinVendedorVaAlDeEstetica: true);

            var resultado = new ComercialesPedido
            {
                Empresa = empresa?.Trim(),
                Pedido = pedido,
                Cliente = cabecera.Nº_Cliente?.Trim(),
                Contacto = cabecera.Contacto?.Trim(),
                EsCanalExterno = lineas.Any(l => Constantes.FormasVenta.EsCanalExterno(l.Forma_Venta)),
                NombreCliente = db.Clientes
                    .Where(c => c.Empresa == empresa && c.Nº_Cliente == cabecera.Nº_Cliente && c.Contacto == cabecera.Contacto)
                    .Select(c => c.Nombre).FirstOrDefault()?.Trim()
            };
            Completar(resultado, empresa, codigos, vendedorPeluqueria, cabecera.Usuario);
            return resultado;
        }

        public ComercialesPedido LeerDeVendedor(string empresa, string vendedor, string usuarioRespaldo)
        {
            var resultado = new ComercialesPedido { Empresa = empresa?.Trim() };
            List<string> codigos = string.IsNullOrWhiteSpace(vendedor) ? new List<string>() : new List<string> { vendedor.Trim() };
            Completar(resultado, empresa, codigos, null, usuarioRespaldo);
            return resultado;
        }

        // Ficha (nombre y correo) y usuarios de cada vendedor, y el respaldo (quien grabó el pedido).
        private void Completar(ComercialesPedido resultado, string empresa, List<string> codigos, string vendedorPeluqueria, string usuarioRespaldo)
        {
            // La ficha del vendedor, de la empresa del pedido o, si no está, de la empresa por defecto (de donde la
            // saca el resto de avisos del picking). ParametrosUsuario vive en la empresa por defecto.
            string empresaDefecto = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            List<Vendedor> fichas = codigos.Any()
                ? db.Vendedores.Where(v => (v.Empresa == empresa || v.Empresa == empresaDefecto) && codigos.Contains(v.Número)).ToList()
                : new List<Vendedor>();
            List<ParametroUsuario> usuariosVendedor = codigos.Any()
                ? db.ParametrosUsuario.Where(p => p.Empresa == empresaDefecto && p.Clave == CLAVE_VENDEDOR && p.Valor != null).ToList()
                : new List<ParametroUsuario>();

            resultado.UsuarioPedido = ComercialesPedido.SinDominio(usuarioRespaldo);
            resultado.CorreoUsuarioPedido = LeerCorreoUsuario(empresaDefecto, usuarioRespaldo);
            foreach (string codigo in codigos)
            {
                Vendedor ficha = fichas.FirstOrDefault(v => v.Número?.Trim() == codigo && v.Empresa?.Trim() == empresa?.Trim())
                    ?? fichas.FirstOrDefault(v => v.Número?.Trim() == codigo);
                resultado.Comerciales.Add(new ComercialPedido
                {
                    Vendedor = codigo,
                    Nombre = ficha?.Descripción?.Trim(),
                    Correo = string.IsNullOrWhiteSpace(ficha?.Mail) ? null : ficha.Mail.Trim(),
                    EsPeluqueria = codigo == vendedorPeluqueria?.Trim(),
                    Usuarios = usuariosVendedor
                        .Where(p => p.Valor.Trim() == codigo && !string.IsNullOrWhiteSpace(p.Usuario)
                            && p.Usuario.Trim() != USUARIO_DEFECTO)
                        .Select(p => p.Usuario.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                });
            }
        }

        // Mismo mecanismo que RellenadorPrepagosService.CorreoUsuario (lo que usaba el correo de retenidos).
        private string LeerCorreoUsuario(string empresa, string usuario)
        {
            string sinDominio = ComercialesPedido.SinDominio(usuario);
            if (string.IsNullOrWhiteSpace(sinDominio))
            {
                return null;
            }
            string correo = db.ParametrosUsuario
                .Where(p => p.Empresa == empresa && p.Usuario == sinDominio && p.Clave == Parametros.Claves.CorreoDefecto)
                .Select(p => p.Valor).FirstOrDefault();
            return string.IsNullOrWhiteSpace(correo) ? null : correo.Trim();
        }
    }
}
