using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Infraestructure.Prestashop
{
    /// <summary>
    /// NestoAPI#614 (Carlos, 22/10/26): el ÚNICO casamiento entre un cliente de Nesto y la tienda PrestaShop, el mismo que usa
    /// CanalesExternos al importar los pedidos de la tienda (Nesto, CanalExternoPedidosPrestashop.BuscarClienteAsync):
    /// <list type="number">
    /// <item>el DNI de la dirección del pedido (address.dni) se limpia: solo letras y cifras, sin ceros a la izquierda
    /// (<see cref="LimpiarDni"/>);</item>
    /// <item>con 5 caracteres o más, se buscan los clientes principales activos de la empresa 1 con ese NIF: primero exacto y,
    /// si no hay, que lo contenga (GET api/Clientes/PorNif, <see cref="ClientesDelNif"/>);</item>
    /// <item>el primero (por cliente y contacto) es el cliente; si no hay ninguno, el pedido entra con el genérico de la tienda
    /// online (31517).</item>
    /// </list>
    /// Hasta ahora la limpieza del DNI vivía solo en Nesto. Nesto debería pasar a llamar a GET api/Clientes/PorDniTiendaOnline,
    /// que hace los tres pasos aquí, en vez de limpiar el DNI él y llamar a PorNif.
    /// </summary>
    public static class CasamientoClientePrestashop
    {
        /// <summary>Nesto#340: un resto más corto barrería el fichero entero con el Contains.</summary>
        public const int LONGITUD_MINIMA_NIF = 5;
        /// <summary>El cliente genérico con el que entran los pedidos de la tienda sin cliente reconocido (en Nesto).</summary>
        public const string CLIENTE_TIENDA_ONLINE = "31517";

        /// <summary>Calcado de CanalExternoPedidosPrestashop.LimpiarDni de Nesto: solo letras y cifras, sin ceros a la izquierda.</summary>
        public static string LimpiarDni(string dni)
        {
            if (dni == null)
            {
                return string.Empty;
            }
            return Regex.Replace(dni.Trim(), "[^0-9A-Za-z]", string.Empty).TrimStart('0');
        }

        /// <summary>
        /// GET api/Clientes/PorNif (Nesto#340): principales activos por NIF, exacto y, si no hay, Contains. Con menos de
        /// <see cref="LONGITUD_MINIMA_NIF"/> caracteres, ninguno.
        /// </summary>
        public static async Task<List<ClienteDTO>> ClientesDelNif(IServicioGestorClientes servicio, string nif)
        {
            nif = nif?.Trim();
            return string.IsNullOrEmpty(nif) || nif.Length < LONGITUD_MINIMA_NIF
                ? new List<ClienteDTO>()
                : await servicio.BuscarClientesPorNif(nif).ConfigureAwait(false) ?? new List<ClienteDTO>();
        }

        /// <summary>El camino entero de CanalesExternos: DNI de la dirección → limpiar → PorNif. Los candidatos, el primero es el cliente.</summary>
        public static Task<List<ClienteDTO>> ClientesDelDni(IServicioGestorClientes servicio, string dni) =>
            ClientesDelNif(servicio, LimpiarDni(dni));

        /// <summary>El cliente de un DNI de la tienda, o null si entraría con el genérico.</summary>
        public static async Task<ClienteDTO> ClienteDelDni(IServicioGestorClientes servicio, string dni) =>
            (await ClientesDelDni(servicio, dni).ConfigureAwait(false)).FirstOrDefault();

        /// <summary>
        /// Lo que se busca en las direcciones de la tienda para un NIF de Nesto: la tira de cifras más larga del NIF limpio (así
        /// «B-12345678» y «B12345678» salen igual); si no tiene 5 cifras seguidas, el NIF limpio entero. Es solo para
        /// preseleccionar: cada dirección se vuelve a casar con <see cref="ClienteDelDni"/>.
        /// </summary>
        public static string TextoBusqueda(string nif)
        {
            string limpio = LimpiarDni(nif);
            string cifras = Regex.Matches(limpio, "[0-9]+").Cast<Match>().Select(m => m.Value).OrderByDescending(v => v.Length).FirstOrDefault();
            return cifras != null && cifras.Length >= LONGITUD_MINIMA_NIF ? cifras : limpio;
        }
    }

    /// <summary>Una dirección de PrestaShop tal como la devuelve <c>addresses?display=[id,id_customer,dni,deleted]</c>.</summary>
    public class DireccionPrestashop
    {
        public int Id { get; set; }
        public int IdCustomer { get; set; }
        public string Dni { get; set; }
        public bool Borrada { get; set; }

        public static List<DireccionPrestashop> Leer(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return new List<DireccionPrestashop>();
            }
            int Entero(XElement a, string campo) =>
                int.TryParse((a.Element(campo)?.Value ?? (campo == "id" ? a.Attribute("id")?.Value : null))?.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int valor) ? valor : 0;
            return XDocument.Parse(xml.Trim()).Descendants("address")
                .Select(a => new DireccionPrestashop
                {
                    Id = Entero(a, "id"),
                    IdCustomer = Entero(a, "id_customer"),
                    Dni = a.Element("dni")?.Value?.Trim(),
                    Borrada = a.Element("deleted")?.Value?.Trim() == "1"
                })
                .Where(a => a.IdCustomer > 0)
                .ToList();
        }
    }

    /// <summary>Un customer de PrestaShop tal como lo devuelve <c>customers?display=[id,active,is_guest,deleted]</c>.</summary>
    public class CuentaPrestashop
    {
        public int Id { get; set; }
        public bool Activa { get; set; }
        public bool Invitado { get; set; }
        public bool Borrada { get; set; }

        /// <summary>Una cuenta con la que se puede entrar en la tienda (el cupón va a su nombre).</summary>
        public bool Valida => Id > 0 && Activa && !Invitado && !Borrada;

        public static List<CuentaPrestashop> Leer(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return new List<CuentaPrestashop>();
            }
            bool Uno(XElement c, string campo) => c.Element(campo)?.Value?.Trim() == "1";
            return XDocument.Parse(xml.Trim()).Descendants("customer")
                .Select(c => new CuentaPrestashop
                {
                    Id = int.TryParse((c.Element("id")?.Value ?? c.Attribute("id")?.Value)?.Trim(), out int id) ? id : 0,
                    // Sin el campo (display corto), se da por buena
                    Activa = c.Element("active") == null || Uno(c, "active"),
                    Invitado = Uno(c, "is_guest"),
                    Borrada = Uno(c, "deleted")
                })
                .Where(c => c.Id > 0)
                .ToList();
        }
    }

    /// <summary>La cuenta de la tienda de un cliente de Nesto (el sentido contrario del casamiento de CanalesExternos).</summary>
    public interface IBuscadorCuentaPrestashop
    {
        /// <summary>El id_customer de la cuenta más reciente del cliente, o null si no tiene ninguna (o ninguna con la que pueda entrar).</summary>
        Task<int?> CuentaDelCliente(string cliente, string nif);
    }

    /// <summary>
    /// Las cuentas de la tienda de un cliente son las de las direcciones cuyo DNI, casado como lo casa CanalesExternos
    /// (<see cref="CasamientoClientePrestashop.ClienteDelDni"/>), da ese cliente: exactamente las cuentas cuyos pedidos entrarían
    /// en Nesto a su nombre. Con varias, la más reciente (id mayor) de las que pueden entrar en la tienda (activas, no de
    /// invitado, no borradas).
    /// </summary>
    public class BuscadorCuentaPrestashop : IBuscadorCuentaPrestashop
    {
        private readonly IClienteWebservicePrestashop tienda;
        private readonly Func<string, Task<ClienteDTO>> clienteDelDni;

        /// <param name="clienteDelDni">El casamiento de CanalesExternos. Null: <see cref="CasamientoClientePrestashop.ClienteDelDni"/> contra la BD.</param>
        public BuscadorCuentaPrestashop(IClienteWebservicePrestashop tienda, Func<string, Task<ClienteDTO>> clienteDelDni = null)
        {
            this.tienda = tienda ?? throw new ArgumentNullException(nameof(tienda));
            this.clienteDelDni = clienteDelDni ?? (dni => CasamientoClientePrestashop.ClienteDelDni(new ServicioGestorClientes(), dni));
        }

        public async Task<int?> CuentaDelCliente(string cliente, string nif)
        {
            string buscado = CasamientoClientePrestashop.TextoBusqueda(nif);
            string clienteLimpio = cliente?.Trim();
            if (string.IsNullOrEmpty(clienteLimpio) || buscado.Length < CasamientoClientePrestashop.LONGITUD_MINIMA_NIF)
            {
                return null;
            }
            // Contiene (%[…]%): las direcciones guardan el DNI como lo escribió el cliente (con guiones, ceros…)
            string xml = await tienda.Leer($"addresses?filter[dni]=%25[{Uri.EscapeDataString(buscado)}]%25&display=[id,id_customer,dni,deleted]")
                .ConfigureAwait(false);
            List<DireccionPrestashop> direcciones = DireccionPrestashop.Leer(xml).Where(d => !d.Borrada && !string.IsNullOrWhiteSpace(d.Dni)).ToList();
            var cuentas = new HashSet<int>();
            foreach (IGrouping<string, DireccionPrestashop> porDni in direcciones.GroupBy(d => d.Dni.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                ClienteDTO casado = await clienteDelDni(porDni.Key).ConfigureAwait(false);
                if (string.Equals(casado?.cliente?.Trim(), clienteLimpio, StringComparison.OrdinalIgnoreCase))
                {
                    cuentas.UnionWith(porDni.Select(d => d.IdCustomer));
                }
            }
            if (!cuentas.Any())
            {
                return null;
            }
            string ids = string.Join("|", cuentas.OrderBy(i => i));
            List<CuentaPrestashop> leidas = CuentaPrestashop.Leer(await tienda.Leer($"customers?filter[id]=[{ids}]&display=[id,active,is_guest,deleted]")
                .ConfigureAwait(false));
            return leidas.Where(c => c.Valida && cuentas.Contains(c.Id)).OrderByDescending(c => c.Id).Select(c => (int?)c.Id).FirstOrDefault();
        }
    }
}
