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
    ///
    /// <para>NestoAPI#663: lo mismo para los cobros de Aplázame, con su plazo (2 horas desde que se confirmaron, no 30
    /// minutos desde que se crearon) y deshaciéndolos a su manera: <see cref="IServicioPagos.DevolverCobro"/> los cancela
    /// en Aplázame en vez de devolverlos por Redsys.</para>
    /// </summary>
    public class CobrosCarritoJobsService
    {
        /// <summary>
        /// Margen antes de dar por perdido un cobro de Redsys. Entre que el banco autoriza y la app pide
        /// crear el pedido pasa un segundo; media hora es de sobra para no pisar a nadie que esté
        /// en mitad del proceso. (El de Aplázame está en <see cref="ProveedorCobroCarrito"/>.)
        /// </summary>
        internal const int MINUTOS_DE_GRACIA = ServicioPagos.MINUTOS_VALIDEZ_COBRO_CARRITO;

        /// <summary>
        /// Cuántos días atrás se mira. Los devueltos ya no vuelven a salir (quedan en "Devuelto"),
        /// así que la ventana solo evita arrastrar para siempre uno cuya devolución rechace el
        /// banco: pasada la ventana, ese hay que resolverlo a mano de todas formas.
        /// </summary>
        internal const int DIAS_A_REVISAR = 30;

        /// <summary>Qué hacer con un cobro del carrito autorizado.</summary>
        internal enum Decision
        {
            /// <summary>Todavía está a tiempo, o ya tiene pedido.</summary>
            Nada,

            /// <summary>Se quedó a medio aplicar (reservado): mirarlo a mano.</summary>
            Avisar,

            /// <summary>Caducado y sin pedido: devolver (Redsys) o cancelar (Aplázame).</summary>
            Deshacer
        }

        // Sin reintentos: si falla, dentro de una hora vuelve a pasar por aquí.
        // Y nunca dos a la vez, que devolverían el mismo cobro dos veces.
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task RevisarCobrosHuerfanos()
        {
            IServicioPagos servicioPagos = new ServicioPagos(
                new RedsysService(), new ContabilidadService(), new LectorParametrosUsuario());

            await RevisarCobrosHuerfanos(servicioPagos, LeerCandidatos, DateTime.Now).ConfigureAwait(false);
        }

        /// <summary>Los cobros del carrito (Redsys y Aplázame) autorizados de los últimos días.</summary>
        private static async Task<List<PagoTPV>> LeerCandidatos(DateTime ahora)
        {
            DateTime desde = ahora.AddDays(-DIAS_A_REVISAR);
            string[] tipos = { Constantes.TiposPagoTPV.CARRITO_APP, Constantes.TiposPagoTPV.CARRITO_APLAZAME };
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;

                return await db.PagosTPV
                    .AsNoTracking()
                    .Where(p => tipos.Contains(p.Tipo)
                             && p.Estado == Constantes.EstadosPagoTPV.AUTORIZADO
                             && p.FechaCreacion > desde)
                    .OrderBy(p => p.FechaCreacion)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Lo que se hace con cada cobro, separado de la base de datos para poder probarlo. Internal para los tests.
        /// </summary>
        internal static Decision Decidir(PagoTPV pago, DateTime ahora)
        {
            ProveedorCobroCarrito proveedor = ProveedorCobroCarrito.De(pago);
            if (proveedor == null
                || !string.Equals(pago.Estado?.Trim(), Constantes.EstadosPagoTPV.AUTORIZADO, StringComparison.OrdinalIgnoreCase)
                || proveedor.CaducaEl(pago) >= ahora)
            {
                return Decision.Nada;
            }

            string documento = pago.Documento?.Trim();
            if (string.Equals(documento, ServicioPagos.MARCA_COBRO_RESERVADO, StringComparison.OrdinalIgnoreCase))
            {
                return Decision.Avisar;
            }

            // Con pedido no es huérfano (la consulta no puede filtrarlo en SQL sin complicarla, y son cuatro filas al día)
            return string.IsNullOrWhiteSpace(documento) ? Decision.Deshacer : Decision.Nada;
        }

        /// <summary>Internal para poder probarlo sin tocar Redsys, Aplázame ni la base de datos.</summary>
        internal static async Task RevisarCobrosHuerfanos(IServicioPagos servicioPagos,
            Func<DateTime, Task<List<PagoTPV>>> leerCandidatos, DateTime ahora)
        {
            List<PagoTPV> candidatos = await leerCandidatos(ahora).ConfigureAwait(false);

            foreach (PagoTPV pago in candidatos)
            {
                ProveedorCobroCarrito proveedor = ProveedorCobroCarrito.De(pago);
                switch (Decidir(pago, ahora))
                {
                    case Decision.Avisar:
                        // El pedido se estaba creando cuando algo se rompió. Puede existir (retenido,
                        // sin su prepago) o no existir: hay que mirarlo, no adivinarlo.
                        ElmahHelper.Log(new Exception(
                            $"[Cobros carrito] El cobro {pago.NumeroOrden} ({pago.Importe:N2} EUR, cliente " +
                            $"{pago.Cliente?.Trim()}, {pago.FechaCreacion:dd/MM/yyyy HH:mm}) se quedó a medio aplicar. " +
                            "Comprobar si el pedido llegó a crearse: si existe, apuntarle el prepago; si no, " +
                            $"deshacer el cobro desde el panel de {proveedor.Nombre}."));
                        break;

                    case Decision.Deshacer:
                        // Cobrado y sin pedido: el cliente pagó y no llegó a pedir nada. Se devuelve (o se cancela).
                        bool deshecho = await servicioPagos.DevolverCobro(pago.Id,
                            "cobro de carrito que no llegó a ser pedido (TNV#68)").ConfigureAwait(false);

                        ElmahHelper.Log(new Exception(
                            $"[Cobros carrito] Cobro {pago.NumeroOrden} ({pago.Importe:N2} EUR, cliente " +
                            $"{pago.Cliente?.Trim()}, {pago.FechaCreacion:dd/MM/yyyy HH:mm}) sin pedido: " +
                            (deshecho
                                ? (proveedor == ProveedorCobroCarrito.Aplazame ? "CANCELADO en Aplázame automáticamente." : "DEVUELTO automáticamente.")
                                : $"NO se ha podido deshacer. Hay que hacerlo desde el panel de {proveedor.Nombre}.")));
                        break;
                }
            }
        }
    }
}
