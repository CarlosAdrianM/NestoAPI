using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#498: encola en Nesto_sync los clientes cuyas fechas de compras
    /// (<see cref="FechasComprasCliente"/>) acaban de cambiar, para que el job 'sincronizar-clientes'
    /// los republique y Odoo mueva sus leads (odoo-custom-addons#9). No publica nada: solo encola.
    ///
    /// <para><b>Por qué un job y no un trigger en LinPedidoVta</b> (lo que proponía la issue): es la
    /// tabla más cargada de la BD, ya tiene un trigger con ROLLBACK y sp_send_dbmail, detectar "el
    /// primero" obliga a leer los pedidos del cliente dentro de la transacción de quien escribe, y un
    /// fallo desharía una venta. Aquí el coste queda fuera del camino de escritura y no puede romper
    /// nada. El razonamiento completo está en la issue.</para>
    ///
    /// <para><b>Cómo detecta</b>: la ventana va por <c>CabPedidoVta.[Fecha Modificación]</c>, que
    /// tiene índice propio (la de las líneas no: cada pasada recorrería 2,75 M filas). Las altas
    /// llevan el DEFAULT getdate() y la PUT de pedidos la actualiza siempre, también al aceptar un
    /// presupuesto. Se encola el cliente solo si alguno de los pedidos tocados es JUSTO el que marca
    /// la fecha (no hay otro anterior, o posterior para el último pedido): el primer pedido de un
    /// cliente nuevo sí; el pedido número 200 de un cliente de siempre, no.</para>
    ///
    /// <para><b>Limitaciones aceptadas</b> (Carlos, 18/09/26): aceptar un presupuesto desde Nesto
    /// viejo es un UPDATE de líneas y, si no toca la cabecera, no se ve; y borrar el único pedido no
    /// deja rastro. En los dos casos el dato llega con la siguiente publicación del cliente.</para>
    /// </summary>
    public class FechasComprasClientesJobsService
    {
        /// <summary>
        /// Pasada cada 30 minutos con ventana de 90: regla de #410, ventana ≈ 2-3 veces el
        /// intervalo, para que una pasada fallida la cubra la siguiente. Encolar de más es gratis
        /// (se publica el estado ACTUAL y no se reencola lo pendiente).
        /// </summary>
        internal const int MINUTOS_VENTANA_FRECUENTE = 90;

        /// <summary>Pasada nocturna, con la misma regla que la de stocks (#410).</summary>
        internal const int HORAS_VENTANA_NOCTURNA = 48;

        internal const string USUARIO_ENCOLADO = "Fechas compras clientes";

        /// <summary>
        /// Punto de entrada para Hangfire, cada 30 minutos: primer presupuesto y primer pedido,
        /// que son los que mueven los leads y conviene que lleguen el mismo día.
        /// </summary>
        public static Task EncolarClientesConPrimerasCompras(int minutosVentana)
        {
            return Encolar(DateTime.Now.AddMinutes(-minutosVentana), incluirUltimoPedido: false,
                descripcion: $"primer presupuesto o primer pedido, ventana de {minutosVentana} minutos");
        }

        /// <summary>
        /// Punto de entrada para Hangfire, de noche: lo mismo y además el último pedido. Va aparte
        /// para no republicar cada cliente con cada pedido durante el día; para la segunda fase del
        /// marketing (clientes que llevan tiempo sin comprar) basta con que llegue al día siguiente.
        /// </summary>
        public static Task EncolarClientesConUltimoPedido(int horasVentana)
        {
            return Encolar(DateTime.Now.AddHours(-horasVentana), incluirUltimoPedido: true,
                descripcion: $"primeras compras y último pedido, ventana de {horasVentana} horas");
        }

        private static async Task Encolar(DateTime desde, bool incluirUltimoPedido, string descripcion)
        {
            Console.WriteLine($"🚀 [Hangfire] Encolando clientes con fechas de compras cambiadas ({descripcion})...");

            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;

                    List<string> clientes = await ClientesARepublicar(db, desde, incluirUltimoPedido).ConfigureAwait(false);
                    int encolados = await db.EncolarSync("Clientes", clientes, USUARIO_ENCOLADO).ConfigureAwait(false);

                    Console.WriteLine($"✅ [Hangfire] Fechas de compras: {clientes.Count} clientes a republicar, " +
                        $"{encolados} encolados en Nesto_sync (el resto ya estaba pendiente); " +
                        $"la sincronización de clientes los publicará en los próximos minutos");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Hangfire] Error encolando clientes con fechas de compras cambiadas: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }

        /// <summary>
        /// Los clientes (Nº_Cliente recortado) a los que un pedido tocado desde <paramref name="desde"/>
        /// les ha podido cambiar el primer presupuesto o el primer pedido (y el último, si
        /// <paramref name="incluirUltimoPedido"/>).
        /// </summary>
        internal static async Task<List<string>> ClientesARepublicar(NVEntities db, DateTime desde, bool incluirUltimoPedido)
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string empresaEspejo = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
            const short presupuesto = Constantes.EstadosLineaVenta.PRESUPUESTO;
            const short pendiente = Constantes.EstadosLineaVenta.PENDIENTE;

            // Mismos criterios de presupuesto y pedido que CalculoFechasComprasCliente (escritos
            // en línea porque EF no traduce la llamada a un método).
            //
            // "Es el primero" se pregunta con un NOT EXISTS de otro pedido ANTERIOR del cliente, y
            // no calculando sus fechas completas: el cliente 32624 (facturas simplificadas de
            // Amazon) tiene ~35.000 pedidos y sale en casi todas las ventanas. Con el cálculo
            // completo cada pasada costaba ~1 s y 350.000 lecturas; así, 20 ms y unas 400
            // (medido en producción el 18/09/26). El EXISTS para en cuanto encuentra uno.
            // Con la misma fecha no hay uno "anterior": es coherente con el MIN de las fechas.
            IQueryable<CabPedidoVta> delCliente = db.CabPedidoVtas
                .Where(o => o.Empresa == empresa || o.Empresa == empresaEspejo);

            List<string> clientes = await db.CabPedidoVtas
                .Where(c => (c.Empresa == empresa || c.Empresa == empresaEspejo)
                    && c.Fecha_Modificación >= desde
                    && c.Fecha != null)
                .Select(c => new
                {
                    c.Nº_Cliente,
                    c.Fecha,
                    EsPresupuesto = c.LinPedidoVtas.Any(l => l.Estado == presupuesto),
                    EsPedido = c.LinPedidoVtas.Any(l => l.Estado >= pendiente)
                })
                .Where(t =>
                    (t.EsPresupuesto && !delCliente.Any(o => o.Nº_Cliente == t.Nº_Cliente && o.Fecha < t.Fecha
                        && o.LinPedidoVtas.Any(l => l.Estado == presupuesto)))
                    || (t.EsPedido && !delCliente.Any(o => o.Nº_Cliente == t.Nº_Cliente && o.Fecha < t.Fecha
                        && o.LinPedidoVtas.Any(l => l.Estado >= pendiente)))
                    || (incluirUltimoPedido && t.EsPedido && !delCliente.Any(o => o.Nº_Cliente == t.Nº_Cliente && o.Fecha > t.Fecha
                        && o.LinPedidoVtas.Any(l => l.Estado >= pendiente))))
                .Select(t => t.Nº_Cliente)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);

            return clientes
                .Select(c => c.Trim())
                .Distinct()
                .OrderBy(c => c)
                .ToList();
        }
    }
}
