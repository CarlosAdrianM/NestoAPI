using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Nesto#340 (slice A3): sirve el pedido que necesita el módulo de Agencias de Nesto, que
    /// hasta ahora lo leía con EF (4 métodos de AgenciaService con Include de Clientes y de sus
    /// personas de contacto).
    ///
    /// Los tres modos replican EXACTAMENTE la semántica que tenían esos métodos, rarezas
    /// incluidas, porque el objetivo del slice es quitar EF sin cambiar comportamiento:
    /// - Por empresa y número      -> CargarPedido
    /// - Por número (con o sin la empresa espejo) -> las dos sobrecargas de CargarPedidoPorNumero
    /// - Por factura               -> CargarPedidoPorFactura
    /// </summary>
    public class GestorPedidoParaAgencia
    {
        private readonly NVEntities db;

        public GestorPedidoParaAgencia(NVEntities db)
        {
            this.db = db;
        }

        /// <summary>Equivale a CargarPedido(empresa, numero): SingleOrDefault por clave.</summary>
        public async Task<PedidoParaAgenciaDTO> LeerPorEmpresaYNumero(string empresa, int numero)
        {
            CabPedidoVta pedido = await db.CabPedidoVtas
                .SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == numero)
                .ConfigureAwait(false);
            return await Montar(pedido).ConfigureAwait(false);
        }

        /// <summary>
        /// Equivale a CargarPedidoPorNumero. Con <paramref name="incluirEspejo"/> a false se
        /// excluye la empresa espejo, que es como lo llama Agencias en primera instancia; el
        /// fallback (sobrecarga de un argumento) la incluye, o sea "prefiero el pedido de la
        /// empresa normal, pero si el número solo existe en la espejo, me vale".
        ///
        /// Se conserva el FirstOrDefault SIN OrderBy del código original: si el mismo número
        /// existe en varias empresas, el elegido depende del plan de SQL Server. Cambiarlo aquí
        /// seria alterar comportamiento en un slice cuyo objetivo es justo lo contrario.
        /// </summary>
        public async Task<PedidoParaAgenciaDTO> LeerPorNumero(int numero, bool incluirEspejo)
        {
            IQueryable<CabPedidoVta> consulta = db.CabPedidoVtas.Where(p => p.Número == numero);
            if (!incluirEspejo)
            {
                consulta = consulta.Where(p => p.Empresa != Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO);
            }
            CabPedidoVta pedido = await consulta.FirstOrDefaultAsync().ConfigureAwait(false);
            return await Montar(pedido).ConfigureAwait(false);
        }

        /// <summary>
        /// Equivale a CargarPedidoPorFactura: primero se busca el número de pedido de una línea
        /// con esa factura y luego el pedido por número.
        ///
        /// Rareza conservada a propósito: el segundo paso NO filtra por empresa, así que puede
        /// traer el pedido homónimo de otra empresa. Se replica igual; si algún día se decide
        /// corregirlo, que sea una decisión consciente y con su propio test, no un efecto
        /// colateral de quitar EF. Lo que sí se corrige es el caso "sin línea": el original se
        /// quedaba con pedido = 0 y buscaba el pedido número 0.
        /// </summary>
        public async Task<PedidoParaAgenciaDTO> LeerPorFactura(string factura)
        {
            int? numeroPedido = await db.LinPedidoVtas
                .Where(l => l.Nº_Factura == factura)
                .Select(l => (int?)l.Número)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (numeroPedido == null)
            {
                return null;
            }
            CabPedidoVta pedido = await db.CabPedidoVtas
                .FirstOrDefaultAsync(p => p.Número == numeroPedido.Value)
                .ConfigureAwait(false);
            return await Montar(pedido).ConfigureAwait(false);
        }

        /// <summary>
        /// Equivale al fallback de CalcularPedidoTexto: cuando lo que se teclea en el hueco del
        /// pedido no es ni un número ni una factura, se busca un CLIENTE cuyo nombre, dirección
        /// o teléfono contengan ese texto, y se coge su pedido más reciente.
        ///
        /// En Nesto esto eran dos pasos: CargarClientePorUnDato (que abría un DbContext, lo
        /// cerraba con Using y devolvía la entidad) y después
        /// <c>clienteEncontrado.CabPedidoVta.OrderByDescending(...).FirstOrDefault()</c>. Esa
        /// segunda parte navegaba una propiedad con lazy loading sobre un contexto YA CERRADO,
        /// así que reventaba con ObjectDisposedException en cuanto la búsqueda encontraba
        /// cliente. Al traerlo aquí, los dos pasos son una sola consulta y el fallo desaparece.
        ///
        /// Se conserva del original el FirstOrDefault del cliente (sin OrderBy: si varios
        /// clientes casan con el texto, gana el que decida el plan de SQL Server) y el
        /// OrderByDescending por número para quedarse con el pedido más reciente.
        /// </summary>
        public async Task<PedidoParaAgenciaDTO> LeerPorTextoDeCliente(string empresa, string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }

            Cliente cliente = await db.Clientes
                .FirstOrDefaultAsync(c => c.Empresa == empresa
                    && (c.Nombre.Contains(texto) || c.Dirección.Contains(texto) || c.Teléfono.Contains(texto)))
                .ConfigureAwait(false);
            if (cliente == null)
            {
                return null;
            }

            CabPedidoVta pedido = await db.CabPedidoVtas
                .Where(p => p.Empresa == cliente.Empresa
                         && p.Nº_Cliente == cliente.Nº_Cliente
                         && p.Contacto == cliente.Contacto)
                .OrderByDescending(p => p.Número)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            return await Montar(pedido).ConfigureAwait(false);
        }

        /// <summary>
        /// Núcleo común: de la entidad al DTO. SIN Trim (ver PedidoParaAgenciaDTO) y con la
        /// ficha del cliente a null cuando no la hay, que para Agencias es una señal, no un
        /// descuido.
        /// </summary>
        private async Task<PedidoParaAgenciaDTO> Montar(CabPedidoVta pedido)
        {
            if (pedido == null)
            {
                return null;
            }

            var dto = new PedidoParaAgenciaDTO
            {
                Empresa = pedido.Empresa,
                Numero = pedido.Número,
                Cliente = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Fecha = pedido.Fecha,
                Vendedor = pedido.Vendedor,
                Comentarios = pedido.Comentarios,
                ComentarioPicking = pedido.ComentarioPicking
            };

            Cliente ficha = await db.Clientes.FirstOrDefaultAsync(c =>
                c.Empresa == pedido.Empresa
                && c.Nº_Cliente == pedido.Nº_Cliente
                && c.Contacto == pedido.Contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return dto;
            }

            dto.ClienteFicha = new ClienteParaAgenciaDTO
            {
                Nombre = ficha.Nombre,
                Direccion = ficha.Dirección,
                CodPostal = ficha.CodPostal,
                Poblacion = ficha.Población,
                Provincia = ficha.Provincia,
                Telefono = ficha.Teléfono,
                PersonasContacto = await db.PersonasContactoClientes
                    .Where(p => p.Empresa == ficha.Empresa
                             && p.NºCliente == ficha.Nº_Cliente
                             && p.Contacto == ficha.Contacto)
                    .Select(p => new PersonaContactoAgenciaDTO
                    {
                        Cargo = p.Cargo,
                        CorreoElectronico = p.CorreoElectrónico
                    })
                    .ToListAsync().ConfigureAwait(false)
            };

            return dto;
        }
    }
}
