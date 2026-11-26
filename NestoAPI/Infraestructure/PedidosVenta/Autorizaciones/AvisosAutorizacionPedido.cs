using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.Autorizaciones
{
    /// <summary>Novedades 1258: los avisos de las autorizaciones de pedidos. Nunca lanzan (sustituible en los tests).</summary>
    public interface IAvisosAutorizacionPedido
    {
        /// <summary>Campana de Nesto (NO urgente) a los que pueden autorizar.</summary>
        Task Pedida(IReadOnlyCollection<string> destinatarios, NotificacionPushDTO aviso);
        /// <summary>
        /// Campana de Nesto a quien la pidió y push de NestoApp al vendedor del pedido (si lo tiene) y, Carlos 26/11/26, también a quien la
        /// pidió si es otra persona y tiene NestoApp (<see cref="ReglaPushAutorizacionResuelta"/>).
        /// </summary>
        Task Resuelta(string solicitante, string empresa, string vendedor, NotificacionPushDTO aviso);
        /// <summary>«notificacionResuelta» a los demás que podían autorizarla.</summary>
        void QuitarDeLosDemas(IReadOnlyCollection<string> destinatarios, NotificacionResueltaDTO resuelta);
    }

    public class AvisosAutorizacionPedido : IAvisosAutorizacionPedido
    {
        private readonly Lazy<IServicioNotificacionesPush> notificaciones;
        private readonly Lazy<IAvisosTiempoReal> tiempoReal;

        public AvisosAutorizacionPedido(IServicioNotificacionesPush notificaciones = null, IAvisosTiempoReal tiempoReal = null)
        {
            this.notificaciones = new Lazy<IServicioNotificacionesPush>(() => notificaciones ?? new ServicioNotificacionesPush());
            this.tiempoReal = new Lazy<IAvisosTiempoReal>(() => tiempoReal ?? new AvisosTiempoReal());
        }

        public async Task Pedida(IReadOnlyCollection<string> destinatarios, NotificacionPushDTO aviso)
        {
            if (aviso == null)
            {
                return;
            }
            foreach (string usuario in destinatarios ?? new List<string>())
            {
                try
                {
                    await notificaciones.Value.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, aviso).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] No se ha podido avisar a {usuario}: {ex.Message}", ex));
                }
            }
        }

        public async Task Resuelta(string solicitante, string empresa, string vendedor, NotificacionPushDTO aviso)
        {
            if (aviso == null)
            {
                return;
            }
            string limpio = solicitante?.Trim();
            string empresaPush = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            try
            {
                if (!string.IsNullOrEmpty(limpio) && limpio.Contains("\\"))
                {
                    await notificaciones.Value.GuardarEnBuzonDeUsuario(limpio, Constantes.Aplicaciones.NESTO, aviso).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] No se ha podido avisar a {solicitante} de la respuesta: {ex.Message}", ex));
            }
            List<DispositivoNotificacion> delVendedor = new List<DispositivoNotificacion>();
            try
            {
                if (!string.IsNullOrWhiteSpace(vendedor))
                {
                    delVendedor = await notificaciones.Value.ObtenerDispositivosVendedor(empresaPush, vendedor.Trim(), Constantes.Aplicaciones.NESTO_APP)
                        .ConfigureAwait(false) ?? new List<DispositivoNotificacion>();
                    _ = await notificaciones.Value.EnviarAVendedor(empresaPush, vendedor.Trim(), aviso).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] No se ha podido avisar al vendedor {vendedor} de la respuesta: {ex.Message}", ex));
            }
            // Carlos 26/11/26: el push de NestoApp también a quien la pidió, si es otra persona y tiene NestoApp (sin duplicar).
            try
            {
                if (!string.IsNullOrEmpty(limpio))
                {
                    List<DispositivoNotificacion> delSolicitante = await notificaciones.Value.ObtenerDispositivosUsuario(limpio, Constantes.Aplicaciones.NESTO_APP)
                        .ConfigureAwait(false) ?? new List<DispositivoNotificacion>();
                    if (ReglaPushAutorizacionResuelta.PushAlSolicitante(delSolicitante.Select(d => d.Id), delVendedor.Select(d => d.Id)))
                    {
                        _ = await notificaciones.Value.EnviarAUsuario(limpio, Constantes.Aplicaciones.NESTO_APP, aviso).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] No se ha podido mandar el push a {solicitante}: {ex.Message}", ex));
            }
        }

        public void QuitarDeLosDemas(IReadOnlyCollection<string> destinatarios, NotificacionResueltaDTO resuelta)
        {
            try
            {
                List<string> usuarios = (destinatarios ?? new List<string>()).ToList();
                if (usuarios.Count > 0 && resuelta != null)
                {
                    tiempoReal.Value.NotificacionResuelta(usuarios, resuelta);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] No se ha podido quitar la solicitud {resuelta?.Referencia} " +
                    $"de los demás Nesto: {ex.Message}", ex));
            }
        }
    }

    /// <summary>
    /// Carlos 26/11/26: LA REGLA del push de NestoApp a quien pidió la autorización cuando se resuelve. Va si tiene algún dispositivo de
    /// NestoApp y ninguno es de los del vendedor del pedido (si alguno lo es, es la misma persona: ya lo recibe como vendedor).
    /// </summary>
    public static class ReglaPushAutorizacionResuelta
    {
        public static bool PushAlSolicitante(IEnumerable<int> dispositivosSolicitante, IEnumerable<int> dispositivosVendedor)
        {
            List<int> suyos = (dispositivosSolicitante ?? Enumerable.Empty<int>()).ToList();
            var delVendedor = new HashSet<int>(dispositivosVendedor ?? Enumerable.Empty<int>());
            return suyos.Count > 0 && !suyos.Any(delVendedor.Contains);
        }
    }
}
