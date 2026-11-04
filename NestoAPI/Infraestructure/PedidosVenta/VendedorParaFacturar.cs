using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Lo que hace falta para saber con qué vendedor va a albaranear y facturar el SP.</summary>
    public class VendedoresPedidoYCliente
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string VendedorPedido { get; set; }
    }

    /// <summary>
    /// NestoAPI#813 (Paloma 03/11/26 ×6, Reina 04/11/26): prdCrearAlbaránVta (vía prdActualizarComisionesPorGrupoProducto,
    /// VendedorLinPedidoVta) y prdCrearFacturaVta (CabFacturaVta.Vendedor) toman el vendedor de cada grupo de
    /// VendedoresPedidoGrupoProducto y, si no hay, el de la CABECERA del pedido. Con la cabecera sin vendedor (pedido grabado
    /// cuando la ficha del cliente aún no lo tenía) revientan con «No se puede insertar el valor NULL en la columna
    /// 'Vendedor'» y el recuento de transacciones descuadrado. Antes de llamarlos: si la cabecera no tiene vendedor, 400 con
    /// lo que tiene que hacer el usuario (Carlos 04/11/26: solo avisar; no se toma el de la ficha ni se escribe en el pedido).
    /// </summary>
    public class VendedorParaFacturar
    {
        private readonly Func<string, int, Task<VendedoresPedidoYCliente>> leer;

        public VendedorParaFacturar(Func<string, int, Task<VendedoresPedidoYCliente>> leer)
        {
            this.leer = leer ?? throw new ArgumentNullException(nameof(leer));
        }

        /// <summary>En producción: lee la cabecera con el mismo contexto que luego llama al SP.</summary>
        public static VendedorParaFacturar ConContexto(NVEntities db)
            => new VendedorParaFacturar((empresa, pedido) => LeerDeLaBaseDeDatos(db, empresa, pedido));

        internal static string Mensaje(int pedido, string cliente)
            => $"El pedido {pedido} del cliente {cliente?.Trim()} no tiene vendedor: póngaselo en el pedido antes de albaranar o facturar";

        /// <summary>El de la cabecera, recortado; null si no tiene.</summary>
        internal static string Resolver(string vendedorPedido)
            => string.IsNullOrWhiteSpace(vendedorPedido) ? null : vendedorPedido.Trim();

        /// <summary>
        /// Devuelve el vendedor de la cabecera con el que va a trabajar el SP. Pedido inexistente: null (de eso se ocupa cada
        /// camino). Sin vendedor en la cabecera: <see cref="NestoBusinessException"/> 400 (no se toca el pedido).
        /// </summary>
        public async Task<string> Asegurar(string empresa, int pedido)
        {
            VendedoresPedidoYCliente datos = await leer(empresa, pedido).ConfigureAwait(false);
            if (datos == null)
            {
                return null;
            }
            string vendedor = Resolver(datos.VendedorPedido);
            if (vendedor == null)
            {
                throw new NestoBusinessException(Mensaje(pedido, datos.Cliente))
                {
                    StatusCode = HttpStatusCode.BadRequest
                };
            }
            return vendedor;
        }

        private static async Task<VendedoresPedidoYCliente> LeerDeLaBaseDeDatos(NVEntities db, string empresa, int pedido)
        {
            var cabecera = await db.CabPedidoVtas.AsNoTracking()
                .Where(c => c.Empresa == empresa && c.Número == pedido)
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.Vendedor })
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (cabecera == null)
            {
                return null;
            }
            return new VendedoresPedidoYCliente
            {
                Cliente = cabecera.Nº_Cliente?.Trim(),
                Contacto = cabecera.Contacto?.Trim(),
                VendedorPedido = cabecera.Vendedor
            };
        }
    }
}
