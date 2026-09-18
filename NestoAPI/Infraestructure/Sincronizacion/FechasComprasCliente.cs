using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#498: las fechas de compras de un cliente (todo el Nº_Cliente, no por contacto) que
    /// viajan a Odoo en <see cref="Models.Sincronizacion.ClienteSyncMessage"/> para mover los leads
    /// del CRM con lo que pasa de verdad en Nesto. Un campo es null si no hay datos.
    /// </summary>
    public class FechasComprasCliente
    {
        public DateTime? FechaPrimerPresupuesto { get; set; }
        public DateTime? FechaPrimerPedido { get; set; }
        public DateTime? FechaUltimoPedido { get; set; }
    }

    /// <summary>
    /// NestoAPI#498: el ÚNICO cálculo de <see cref="FechasComprasCliente"/>. Lo usan la
    /// publicación de un cliente y la carga inicial (muchos clientes a la vez). El job que decide a
    /// quién republicar no calcula las fechas (sería caro, ver
    /// <see cref="FechasComprasClientesJobsService.ClientesARepublicar"/>), pero aplica los mismos
    /// criterios:
    /// <list type="bullet">
    /// <item>La fecha es la de la CABECERA (<c>CabPedidoVta.Fecha</c>); el estado, el de las líneas.</item>
    /// <item>Presupuesto: alguna línea en <see cref="Constantes.EstadosLineaVenta.PRESUPUESTO"/> (-3).</item>
    /// <item>Pedido: alguna línea en <see cref="Constantes.EstadosLineaVenta.PENDIENTE"/> (-1) o
    /// superior. La nota de entrega (-2) NO cuenta como pedido (decisión de Carlos, 18/09/26).</item>
    /// <item>Cuentan la empresa principal y la espejo: un pedido traspasado sigue siendo del cliente.</item>
    /// </list>
    /// Una sola consulta agrupada por lote de clientes, nunca tres por cliente.
    /// </summary>
    public static class CalculoFechasComprasCliente
    {
        /// <summary>Tamaño de <c>[Nº Cliente]</c> (char(10)) en CabPedidoVta/LinPedidoVta.</summary>
        private const int LONGITUD_NUMERO_CLIENTE = 10;

        /// <summary>
        /// EF6 traduce el Contains a un IN con constantes: con miles de clientes (carga inicial de
        /// un vendedor) la sentencia se hace enorme, así que va por lotes.
        /// </summary>
        private const int TAMANO_LOTE = 500;

        /// <summary>
        /// Las fechas de compras de los clientes indicados, por Nº_Cliente recortado. Los clientes
        /// sin presupuestos ni pedidos no aparecen en el diccionario.
        /// </summary>
        public static async Task<Dictionary<string, FechasComprasCliente>> Calcular(NVEntities db, IEnumerable<string> clientes)
        {
            // Se rellenan con espacios hasta el ancho de la columna: en SQL da igual (el = ignora
            // los espacios finales), pero en memoria "15191" != "15191     ", y así el filtro es
            // el mismo en los dos mundos sin recortar la columna (que mataría el índice).
            List<string> buscados = (clientes ?? Enumerable.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().PadRight(LONGITUD_NUMERO_CLIENTE))
                .Distinct()
                .ToList();

            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string empresaEspejo = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
            const short presupuesto = Constantes.EstadosLineaVenta.PRESUPUESTO;
            const short pendiente = Constantes.EstadosLineaVenta.PENDIENTE;

            var resultado = new Dictionary<string, FechasComprasCliente>();
            for (int inicio = 0; inicio < buscados.Count; inicio += TAMANO_LOTE)
            {
                List<string> lote = buscados.Skip(inicio).Take(TAMANO_LOTE).ToList();

                // Los criterios de presupuesto y pedido están también en
                // FechasComprasClientesJobsService.ClientesARepublicar: si cambian, en los dos.
                var filas = await db.LinPedidoVtas
                    .Where(l => (l.Empresa == empresa || l.Empresa == empresaEspejo)
                        && lote.Contains(l.Nº_Cliente)
                        && (l.Estado == presupuesto || l.Estado >= pendiente))
                    .GroupBy(l => l.Nº_Cliente)
                    .Select(g => new
                    {
                        Cliente = g.Key,
                        PrimerPresupuesto = g.Where(l => l.Estado == presupuesto).Min(l => l.CabPedidoVta.Fecha),
                        PrimerPedido = g.Where(l => l.Estado >= pendiente).Min(l => l.CabPedidoVta.Fecha),
                        UltimoPedido = g.Where(l => l.Estado >= pendiente).Max(l => l.CabPedidoVta.Fecha)
                    })
                    .ToListAsync().ConfigureAwait(false);

                foreach (var fila in filas)
                {
                    resultado[fila.Cliente.Trim()] = new FechasComprasCliente
                    {
                        FechaPrimerPresupuesto = fila.PrimerPresupuesto,
                        FechaPrimerPedido = fila.PrimerPedido,
                        FechaUltimoPedido = fila.UltimoPedido
                    };
                }
            }
            return resultado;
        }

        /// <summary>Las fechas de un solo cliente (vacías si no tiene presupuestos ni pedidos).</summary>
        public static async Task<FechasComprasCliente> Calcular(NVEntities db, string cliente)
        {
            Dictionary<string, FechasComprasCliente> fechas = await Calcular(db, new[] { cliente }).ConfigureAwait(false);
            return Buscar(fechas, cliente);
        }

        /// <summary>Busca un cliente en el resultado de <see cref="Calcular(NVEntities, IEnumerable{string})"/>.</summary>
        public static FechasComprasCliente Buscar(IDictionary<string, FechasComprasCliente> fechas, string cliente)
        {
            return cliente != null && fechas != null && fechas.TryGetValue(cliente.Trim(), out FechasComprasCliente encontradas)
                ? encontradas
                : new FechasComprasCliente();
        }
    }
}
