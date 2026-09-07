using Hangfire;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// TNV#68: la red que recoge los cobros de carrito que nunca llegaron a ser pedido.
    ///
    /// <para>Cobrar antes de crear el pedido quita el pedido fantasma, pero abre el hueco
    /// contrario: el cliente paga y cierra la app antes de que la app pida crear el pedido. Ahí
    /// hay dinero cobrado y ningún pedido, y eso no puede quedarse esperando a que alguien lo vea
    /// en el cuadre de fin de mes.</para>
    ///
    /// <para>Lo que hace el job es distinto según lo que se encuentre, y a propósito:</para>
    /// <list type="bullet">
    /// <item><description><b>Cobro sin tocar</b> (nadie llegó a pedir el pedido): se devuelve. No
    /// hay pedido ninguno, así que el dinero sobra.</description></item>
    /// <item><description><b>Cobro reservado</b> (el pedido se estaba creando y algo se torció a
    /// mitad): NO se toca. Puede que el pedido exista y esté retenido esperando su prepago, y
    /// devolver el dinero dejaría un pedido creado y sin pagar. Se avisa para mirarlo a mano, que
    /// es lo que hay que hacer con dos casos al año.</description></item>
    /// </list>
    /// </summary>
    public class CobrosCarritoJobsService
    {
        /// <summary>
        /// Margen antes de dar por perdido un cobro. Entre que el banco autoriza y la app pide
        /// crear el pedido pasa un segundo; media hora es de sobra para no pisar a nadie que esté
        /// en mitad del proceso.
        /// </summary>
        internal const int MINUTOS_DE_GRACIA = ServicioPagos.MINUTOS_VALIDEZ_COBRO_CARRITO;

        /// <summary>
        /// Cuántos días atrás se mira. Los devueltos ya no vuelven a salir (quedan en "Devuelto"),
        /// así que la ventana solo evita arrastrar para siempre uno cuya devolución rechace el
        /// banco: pasada la ventana, ese hay que resolverlo a mano de todas formas.
        /// </summary>
        internal const int DIAS_A_REVISAR = 30;

        // Sin reintentos: si falla, dentro de una hora vuelve a pasar por aquí.
        // Y nunca dos a la vez, que devolverían el mismo cobro dos veces.
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task RevisarCobrosHuerfanos()
        {
            IServicioPagos servicioPagos = new ServicioPagos(
                new RedsysService(), new ContabilidadService(), new LectorParametrosUsuario());

            await RevisarCobrosHuerfanos(servicioPagos).ConfigureAwait(false);
        }

        /// <summary>Internal para poder probarlo sin tocar Redsys.</summary>
        internal static async Task RevisarCobrosHuerfanos(IServicioPagos servicioPagos)
        {
            DateTime limite = DateTime.Now.AddMinutes(-MINUTOS_DE_GRACIA);
            DateTime desde = DateTime.Now.AddDays(-DIAS_A_REVISAR);

            List<PagoTPV> huerfanos;
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;

                huerfanos = await db.PagosTPV
                    .AsNoTracking()
                    .Where(p => p.Tipo == Constantes.TiposPagoTPV.CARRITO_APP
                             && p.Estado == Constantes.EstadosPagoTPV.AUTORIZADO
                             && p.FechaCreacion < limite
                             && p.FechaCreacion > desde)
                    .OrderBy(p => p.FechaCreacion)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }

            foreach (PagoTPV pago in huerfanos)
            {
                string documento = pago.Documento?.Trim();

                if (string.Equals(documento, ServicioPagos.MARCA_COBRO_RESERVADO, StringComparison.OrdinalIgnoreCase))
                {
                    // El pedido se estaba creando cuando algo se rompió. Puede existir (retenido,
                    // sin su prepago) o no existir: hay que mirarlo, no adivinarlo.
                    ElmahHelper.Log(new Exception(
                        $"[Cobros carrito] El cobro {pago.NumeroOrden} ({pago.Importe:N2} EUR, cliente " +
                        $"{pago.Cliente?.Trim()}, {pago.FechaCreacion:dd/MM/yyyy HH:mm}) se quedó a medio aplicar. " +
                        "Comprobar si el pedido llegó a crearse: si existe, apuntarle el prepago; si no, " +
                        "devolver el cobro desde el panel de Redsys."));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(documento))
                {
                    // Tiene pedido: no es huérfano (la consulta no puede filtrarlo en SQL sin
                    // complicarla, y son cuatro filas al día).
                    continue;
                }

                // Cobrado y sin pedido: el cliente pagó y no llegó a pedir nada. Se devuelve.
                bool devuelto = await servicioPagos.DevolverCobro(pago.Id,
                    "cobro de carrito que no llegó a ser pedido (TNV#68)").ConfigureAwait(false);

                ElmahHelper.Log(new Exception(
                    $"[Cobros carrito] Cobro {pago.NumeroOrden} ({pago.Importe:N2} EUR, cliente " +
                    $"{pago.Cliente?.Trim()}, {pago.FechaCreacion:dd/MM/yyyy HH:mm}) sin pedido: " +
                    (devuelto
                        ? "DEVUELTO automáticamente."
                        : "NO se ha podido devolver. Hay que devolverlo desde el panel de Redsys.")));
            }
        }
    }
}
