using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#773: los avisos por SignalR (AlmacenHub) a la PDA que espera: «pagoCompletado» al grupo «PagoTPV:{id}» y
    /// «autorizacionRespondida» al grupo «AutorizacionReembolso:{id}». Sin exclusiones (lo ha hecho otro: Redsys o Administración).
    /// Nunca lanza.
    /// </summary>
    public interface IAvisosCobroReparto
    {
        void PagoCompletado(PagoCompletadoRepartoDTO pago);
        void AutorizacionRespondida(AutorizacionReembolsoDTO autorizacion);
    }

    /// <summary>
    /// NestoAPI#773: lo que hace la notificación de Redsys (ServicioPagos.ProcesarNotificacion, pago autorizado) con un enlace del
    /// reparto: avisar a la PDA que lo generó. El dinero ya lo ha apuntado ServicioPagos (ContabilizarCobro); aquí no se contabiliza nada.
    /// </summary>
    public class AvisoPagoReparto
    {
        private readonly Func<int, Task<EnlaceTarjetaReparto>> leer;
        private readonly IAvisosCobroReparto avisos;
        private readonly Func<DateTime> ahora;

        public AvisoPagoReparto(Func<int, Task<EnlaceTarjetaReparto>> leer, IAvisosCobroReparto avisos, Func<DateTime> ahora = null)
        {
            this.leer = leer ?? throw new ArgumentNullException(nameof(leer));
            this.avisos = avisos ?? throw new ArgumentNullException(nameof(avisos));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>El de producción: lee RepartoCobrosTarjeta (si existe) y avisa por el AlmacenHub. Nunca lanza.</summary>
        public static Task AvisarAsync(PagoTPV pago)
            => new AvisoPagoReparto(LeerDeBd, new Notificaciones.AvisosAlmacenSignalR()).Avisar(pago);

        /// <returns>True si el pago era de un enlace del reparto (y se ha avisado).</returns>
        public async Task<bool> Avisar(PagoTPV pago)
        {
            if (pago == null)
            {
                return false;
            }
            try
            {
                int raiz = pago.PagoOriginalId ?? pago.Id;
                EnlaceTarjetaReparto enlace = await leer(raiz).ConfigureAwait(false);
                if (enlace == null)
                {
                    return false;
                }
                avisos.PagoCompletado(new PagoCompletadoRepartoDTO
                {
                    PagoTPVId = enlace.PagoTPVId,
                    EnvioId = enlace.EnvioId,
                    Cliente = enlace.Cliente?.Trim(),
                    Importe = pago.Importe,
                    Tipo = enlace.Tipo?.Trim(),
                    FechaHora = ahora()
                });
                return true;
            }
            catch (Exception ex)
            {
                // El aviso es un extra: la PDA pregunta el estado al volver a la pantalla o al reconectar
                ElmahHelper.Log(new Exception($"[Cobro con tarjeta #773] No se pudo avisar a la PDA del pago {pago.NumeroOrden}: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>El enlace del reparto de ese PagoTPV raíz, o null (no es del reparto o no está el script).</summary>
        internal static async Task<EnlaceTarjetaReparto> LeerDeBd(int pagoRaiz)
        {
            using (var db = new NVEntities())
            {
                var repositorio = new RepositorioCobrosTarjetaReparto(db);
                return repositorio.ScriptAplicado() ? await repositorio.LeerPorPago(pagoRaiz).ConfigureAwait(false) : null;
            }
        }

        /// <summary>
        /// NestoAPI#773: ¿es el enlace (raíz o regenerado) de un cobro del reparto? Para no mandar el «URGENTE: pagado por enlace con el
        /// envío en reparto contra reembolso» (#679) de algo que el repartidor ya tiene resuelto en la mano.
        /// </summary>
        internal static async Task<bool> EsDelReparto(int idPago)
        {
            using (var db = new NVEntities())
            {
                var repositorio = new RepositorioCobrosTarjetaReparto(db);
                if (!repositorio.ScriptAplicado())
                {
                    return false;
                }
                int? original = await db.PagosTPV.Where(p => p.Id == idPago).Select(p => p.PagoOriginalId).FirstOrDefaultAsync().ConfigureAwait(false);
                return await repositorio.LeerPorPago(original ?? idPago).ConfigureAwait(false) != null;
            }
        }
    }
}
