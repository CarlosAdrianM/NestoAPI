using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// TNV#66: los pedidos recientes de un cliente, ya resumidos para él.
    ///
    /// <para>Vive fuera del controller porque tiene DOS consumidores que no pueden discrepar: la
    /// pantalla «Mis pedidos» (<c>GET api/Pedidos/Cliente</c>) y el job que avisa por push de los
    /// cambios de estado. Si cada uno calculara el estado por su cuenta, la notificación diría una
    /// cosa y la pantalla otra.</para>
    /// </summary>
    public interface IServicioPedidosCliente
    {
        /// <summary>Ver <see cref="ServicioPedidosCliente.LeerPedidosRecientes"/>.</summary>
        Task<List<PedidoClienteResumenDTO>> LeerPedidosRecientes(string empresa, string cliente, int dias);
    }

    /// <inheritdoc cref="IServicioPedidosCliente"/>
    public class ServicioPedidosCliente : IServicioPedidosCliente
    {
        private readonly NVEntities db;

        public ServicioPedidosCliente(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Los pedidos del cliente de los últimos <paramref name="dias"/> días, del más reciente
        /// al más antiguo. Se devuelven también los ya servidos: el paquete de un pedido facturado
        /// ayer sigue de camino y es el que el cliente quiere seguir.
        /// </summary>
        public async Task<List<PedidoClienteResumenDTO>> LeerPedidosRecientes(string empresa, string cliente, int dias)
        {
            DateTime desde = DateTime.Today.AddDays(-dias);

            var pedidos = await db.CabPedidoVtas
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Fecha >= desde)
                .Select(c => new
                {
                    c.Número,
                    c.Fecha,
                    c.Forma_Pago,
                    c.PlazosPago,
                    Lineas = c.LinPedidoVtas.Select(l => new
                    {
                        l.Estado,
                        l.Picking,
                        l.TipoLinea,
                        l.Cantidad,
                        l.Total,
                        l.Texto
                    }),
                    // Solo los prepagos vivos del pedido: los que ya se llevó una factura no
                    // cuentan (es lo mismo que mira el picking para soltarlo).
                    ImportePrepagado = db.Prepagos
                        .Where(p => p.Pedido == c.Número && p.Factura == null)
                        .Select(p => (decimal?)p.Importe)
                        .Sum()
                })
                .OrderByDescending(c => c.Fecha)
                .ThenByDescending(c => c.Número)
                .ToListAsync()
                .ConfigureAwait(false);

            List<int> numeros = pedidos.Select(p => p.Número).ToList();
            Dictionary<int, UltimoEnvioClienteDTO> envios =
                await LeerEnviosDeLosPedidos(empresa, cliente, numeros).ConfigureAwait(false);

            return pedidos
                // Un presupuesto todavía no es un pedido: enseñárselo como tal sería prometerle
                // algo que nadie ha confirmado. Basta con una línea viva para que cuente.
                .Where(p => p.Lineas.Any(l => l.Estado > Constantes.EstadosLineaVenta.PRESUPUESTO))
                .Select(p => ResumidorPedidosCliente.Resumir(new DatosPedidoCliente
                {
                    Numero = p.Número,
                    Fecha = p.Fecha,
                    FormaPago = p.Forma_Pago,
                    PlazosPago = p.PlazosPago,
                    ImportePrepagado = p.ImportePrepagado ?? 0m,
                    Envio = envios.ContainsKey(p.Número) ? envios[p.Número] : null,
                    Lineas = p.Lineas.Select(l => new DatosLineaPedidoCliente
                    {
                        Estado = l.Estado,
                        Picking = l.Picking,
                        TipoLinea = l.TipoLinea,
                        Cantidad = l.Cantidad,
                        Total = l.Total,
                        Texto = l.Texto
                    }).ToList()
                }))
                .ToList();
        }

        /// <summary>
        /// El envío de cada pedido (el último, si hubo varios), con su seguimiento. Se filtra
        /// igual que <c>EnviosAgencias/UltimoEnvioCliente</c>: sin código de barras no hay nada
        /// que seguir, y los que aún no están en curso no han salido del almacén.
        /// </summary>
        private async Task<Dictionary<int, UltimoEnvioClienteDTO>> LeerEnviosDeLosPedidos(
            string empresa, string cliente, List<int> pedidos)
        {
            if (pedidos.Count == 0)
            {
                return new Dictionary<int, UltimoEnvioClienteDTO>();
            }

            List<UltimoEnvioClienteDTO> enviosDelCliente = await db.EnviosAgencias
                .Where(e => e.Empresa == empresa &&
                            e.Cliente == cliente &&
                            e.Pedido != null &&
                            pedidos.Contains(e.Pedido.Value) &&
                            e.CodigoBarras != null &&
                            e.Estado >= Constantes.Agencias.ESTADO_EN_CURSO)
                .OrderByDescending(e => e.Fecha)
                .ThenByDescending(e => e.Numero)
                .Select(e => new UltimoEnvioClienteDTO
                {
                    Pedido = e.Pedido ?? 0,
                    Fecha = e.Fecha,
                    FechaEntrega = e.FechaEntrega,
                    AgenciaId = e.Agencia,
                    AgenciaNombre = e.AgenciasTransporte.Nombre,
                    AgenciaIdentificador = e.AgenciasTransporte.Identificador,
                    NumeroSeguimiento = e.CodigoBarras,
                    CodigoPostal = e.CodPostal,
                    Cliente = e.Cliente,
                    Estado = e.Estado,
                    Bultos = e.Bultos,
                    Observaciones = e.Observaciones
                })
                .ToListAsync()
                .ConfigureAwait(false);

            return enviosDelCliente
                .GroupBy(e => e.Pedido)
                .ToDictionary(g => g.Key, g => g.First());
        }
    }
}
