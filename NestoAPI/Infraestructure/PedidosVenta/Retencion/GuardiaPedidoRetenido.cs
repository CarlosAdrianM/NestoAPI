using NestoAPI.Models;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.Retencion
{
    /// <summary>
    /// NestoAPI#698 (Carlos, 28/10/26): un pedido retenido (modo 5) no sale del almacén por ningún camino: ni envío nuevo,
    /// ni tramitar o imprimir la etiqueta de uno ya hecho, ni albarán. Devuelve el motivo para el usuario o null si el
    /// pedido no está retenido (o no existe: de eso se ocupa cada camino). prdCrearAlbaránVta hace la misma comprobación
    /// en la base de datos (Scripts/Issue698_prdCrearAlbaranVta_RechazaRetenidos.sql) para los caminos que no pasan por aquí.
    /// </summary>
    public static class GuardiaPedidoRetenido
    {
        public static async Task<string> Motivo(IRepositorioPedidosRetenidos repositorio, string empresa, int pedido)
        {
            if (repositorio == null || pedido <= 0)
            {
                return null;
            }
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            CabeceraPedidoRetencion cabecera = await repositorio.LeerCabecera(empresaLimpia, pedido).ConfigureAwait(false);
            if (cabecera == null)
            {
                return null;
            }
            if (Constantes.Pedidos.ModosServicio.EsRetenido(cabecera.ModoServicio))
            {
                RetencionPedidoDTO viva = await repositorio.LeerActiva(empresaLimpia, pedido).ConfigureAwait(false);
                return TextosRetencion.BloqueoSalida(pedido, viva);
            }
            // NestoAPI#722: aplazado = picking con fecha de entrega futura (solo se llega ahí aplazando desde el pedido)
            if (cabecera.FechaEntregaPicking.HasValue && cabecera.FechaEntregaPicking.Value.Date > hoy().Date)
            {
                return TextosRetencion.BloqueoAplazado(pedido, cabecera.FechaEntregaPicking.Value, cabecera.Pickings());
            }
            return null;
        }

        /// <summary>Para los tests: el día de hoy.</summary>
        internal static Func<DateTime> hoy = () => DateTime.Today;

        /// <summary>El de producción: su propio contexto, como el resto de la retención.</summary>
        public static Task<string> Motivo(string empresa, int pedido) => Motivo(new RepositorioPedidosRetenidos(), empresa, pedido);
    }
}
