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
        /// <summary>Campana de Nesto a quien la pidió y push de NestoApp a su vendedor (si lo tiene).</summary>
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
            try
            {
                string limpio = solicitante?.Trim();
                if (!string.IsNullOrEmpty(limpio) && limpio.Contains("\\"))
                {
                    await notificaciones.Value.GuardarEnBuzonDeUsuario(limpio, Constantes.Aplicaciones.NESTO, aviso).ConfigureAwait(false);
                }
                if (!string.IsNullOrWhiteSpace(vendedor))
                {
                    _ = await notificaciones.Value.EnviarAVendedor(string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(),
                        vendedor.Trim(), aviso).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] No se ha podido avisar a {solicitante} de la respuesta: {ex.Message}", ex));
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
}
