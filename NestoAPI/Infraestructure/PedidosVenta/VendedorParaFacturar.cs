using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Data.SqlClient;
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
        public string VendedorCliente { get; set; }
    }

    /// <summary>
    /// NestoAPI#813 (Paloma 03/11/26 ×6, Reina 04/11/26): prdCrearAlbaránVta (vía prdActualizarComisionesPorGrupoProducto,
    /// VendedorLinPedidoVta) y prdCrearFacturaVta (CabFacturaVta.Vendedor) toman el vendedor de cada grupo de
    /// VendedoresPedidoGrupoProducto y, si no hay, el de la CABECERA del pedido. Con la cabecera sin vendedor (pedido grabado
    /// cuando la ficha del cliente aún no lo tenía) revientan con «No se puede insertar el valor NULL en la columna
    /// 'Vendedor'» y el recuento de transacciones descuadrado. Antes de llamarlos: si la cabecera no tiene vendedor se le
    /// pone el de la ficha del cliente; si tampoco hay, 400 con lo que tiene que hacer el usuario.
    /// </summary>
    public class VendedorParaFacturar
    {
        private readonly Func<string, int, Task<VendedoresPedidoYCliente>> leer;
        private readonly Func<string, int, string, Task> fijarEnPedido;

        public VendedorParaFacturar(Func<string, int, Task<VendedoresPedidoYCliente>> leer, Func<string, int, string, Task> fijarEnPedido)
        {
            this.leer = leer ?? throw new ArgumentNullException(nameof(leer));
            this.fijarEnPedido = fijarEnPedido ?? throw new ArgumentNullException(nameof(fijarEnPedido));
        }

        /// <summary>En producción: lee y escribe en la BD con el mismo contexto (y la misma conexión) que luego llama al SP.</summary>
        public static VendedorParaFacturar ConContexto(NVEntities db)
        {
            return new VendedorParaFacturar(
                (empresa, pedido) => LeerDeLaBaseDeDatos(db, empresa, pedido),
                (empresa, pedido, vendedor) => db.Database.ExecuteSqlCommandAsync(
                    "UPDATE CabPedidoVta SET Vendedor = @Vendedor WHERE Empresa = @Empresa AND Número = @Pedido " +
                    "AND (Vendedor IS NULL OR LTRIM(RTRIM(Vendedor)) = '')",
                    new SqlParameter("@Vendedor", vendedor),
                    new SqlParameter("@Empresa", empresa),
                    new SqlParameter("@Pedido", pedido)));
        }

        internal static string Mensaje(string cliente)
            => $"El cliente {cliente?.Trim()} no tiene vendedor asignado: póngaselo en la ficha antes de facturar";

        /// <summary>El de la cabecera; si no tiene, el de la ficha del cliente; si tampoco, null.</summary>
        internal static string Resolver(string vendedorPedido, string vendedorCliente)
        {
            if (!string.IsNullOrWhiteSpace(vendedorPedido))
            {
                return vendedorPedido.Trim();
            }
            return string.IsNullOrWhiteSpace(vendedorCliente) ? null : vendedorCliente.Trim();
        }

        /// <summary>
        /// Devuelve el vendedor con el que va a trabajar el SP (y, si la cabecera no lo tenía, se lo pone). Pedido inexistente:
        /// null (de eso se ocupa cada camino). Sin vendedor en ningún sitio: <see cref="NestoBusinessException"/> 400.
        /// </summary>
        public async Task<string> Asegurar(string empresa, int pedido)
        {
            VendedoresPedidoYCliente datos = await leer(empresa, pedido).ConfigureAwait(false);
            if (datos == null)
            {
                return null;
            }
            string vendedor = Resolver(datos.VendedorPedido, datos.VendedorCliente);
            if (vendedor == null)
            {
                throw new NestoBusinessException(Mensaje(datos.Cliente))
                {
                    StatusCode = HttpStatusCode.BadRequest
                };
            }
            if (string.IsNullOrWhiteSpace(datos.VendedorPedido))
            {
                await fijarEnPedido(empresa, pedido, vendedor).ConfigureAwait(false);
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
            string vendedorCliente = null;
            if (string.IsNullOrWhiteSpace(cabecera.Vendedor))
            {
                vendedorCliente = await db.Clientes.AsNoTracking()
                    .Where(c => c.Empresa == empresa && c.Nº_Cliente == cabecera.Nº_Cliente && c.Contacto == cabecera.Contacto)
                    .Select(c => c.Vendedor)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
            return new VendedoresPedidoYCliente
            {
                Cliente = cabecera.Nº_Cliente?.Trim(),
                Contacto = cabecera.Contacto?.Trim(),
                VendedorPedido = cabecera.Vendedor,
                VendedorCliente = vendedorCliente
            };
        }
    }
}
