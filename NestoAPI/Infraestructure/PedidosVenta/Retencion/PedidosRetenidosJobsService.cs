using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.Retencion
{
    /// <summary>NestoAPI#698: lo que ha hecho la pasada nocturna.</summary>
    public class ResultadoPasadaRetenidos
    {
        public int Liberados { get; set; }
        public int Recordados { get; set; }
        /// <summary>Avisos dejados en la campana (liberaciones y recordatorios).</summary>
        public int Avisos { get; set; }
        public List<int> PedidosLiberados { get; set; } = new List<int>();
        public List<int> PedidosRecordados { get; set; } = new List<int>();
    }

    /// <summary>
    /// NestoAPI#698: la pasada nocturna de los pedidos retenidos (L-S a las 5:35, fuera de horario y antes de la de promesa
    /// en riesgo de las 5:40, que así ya ve el pedido liberado como candidato):
    /// <list type="bullet">
    /// <item>Los retenidos con fecha de revisión ya llegada se liberan solos (vuelven a su modo y entran en el picking) y se
    /// avisa por la campana a quien lo retuvo y al vendedor del pedido.</item>
    /// <item>Los retenidos SIN fecha se recuerdan por la campana como mucho cada N días (Pedidos:Retenidos:DiasRecordatorio,
    /// 7 por defecto: Carlos, 23/10/26, «como mucho una vez a la semana»). 0 = sin recordatorios.</item>
    /// </list>
    /// Sin la tabla no hace nada. Nunca rompe: lo que falle va a ELMAH y no se reintenta.
    /// </summary>
    public static class PedidosRetenidosJobsService
    {
        public const string ID_JOB = "pedidos-retenidos-nocturna";
        public const string CRON = "35 5 * * 1-6";

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task PasadaNocturna()
        {
            try
            {
                var repositorio = new RepositorioPedidosRetenidos();
                if (!repositorio.TablaExiste())
                {
                    return;
                }
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    var lector = new LectorComercialesPedido(db);
                    _ = await Procesar(repositorio, new ServicioRetencionPedidos(repositorio, new RepositorioPromesaRiesgo()),
                        new ServicioNotificacionesPush(), (empresa, pedido) => lector.Leer(empresa, pedido)?.UsuariosBuzon(),
                        DateTime.Now, AjustesRetencion.DiasRecordatorio).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Retenidos #698] Error en la pasada nocturna: {ex.Message}", ex), "Sistema (retenidos)");
            }
        }

        /// <summary>El núcleo, con todo inyectado para los tests.</summary>
        /// <param name="usuariosVendedor">Los usuarios del buzón (con dominio) de los comerciales del pedido; null o vacío si no se saben.</param>
        internal static async Task<ResultadoPasadaRetenidos> Procesar(IRepositorioPedidosRetenidos repositorio, ServicioRetencionPedidos servicio,
            IServicioNotificacionesPush notificaciones, Func<string, int, List<string>> usuariosVendedor, DateTime ahora, int diasRecordatorio,
            Action<Exception> registrar = null)
        {
            registrar = registrar ?? (ex => ElmahHelper.Log(ex, "Sistema (retenidos)"));
            var resultado = new ResultadoPasadaRetenidos();
            if (!repositorio.TablaExiste())
            {
                return resultado;
            }

            foreach (RetencionPedidoDTO vencida in await repositorio.LeerVencidas(ahora.Date).ConfigureAwait(false) ?? new List<RetencionPedidoDTO>())
            {
                try
                {
                    CabeceraPedidoRetencion cabecera = await repositorio.LeerCabecera(vencida.Empresa, vencida.Pedido).ConfigureAwait(false);
                    ResultadoRetencion liberacion = await servicio.Liberar(vencida.Empresa, vencida.Pedido, ServicioRetencionPedidos.USUARIO_SISTEMA,
                        TextosRetencion.LIBERADO_POR_FECHA).ConfigureAwait(false);
                    if (liberacion.Error != null || liberacion.NoEncontrado)
                    {
                        // El pedido ya no existe o la fila se cerró entre medias: se cierra la fila para que no vuelva a salir.
                        _ = await repositorio.Liberar(vencida.Id, ahora, ServicioRetencionPedidos.USUARIO_SISTEMA, liberacion.Error ?? "Pedido inexistente").ConfigureAwait(false);
                        continue;
                    }
                    resultado.Liberados++;
                    resultado.PedidosLiberados.Add(vencida.Pedido);
                    resultado.Avisos += await Avisar(notificaciones, Destinatarios(vencida, usuariosVendedor),
                        TextosRetencion.NotificacionLiberado(vencida, cabecera), registrar).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Retenidos #698] No se ha podido liberar el pedido {vencida.Pedido} al llegar su fecha: {ex.Message}", ex));
                }
            }

            if (diasRecordatorio <= 0)
            {
                return resultado;
            }
            DateTime limite = ahora.Date.AddDays(-diasRecordatorio);
            foreach (RetencionPedidoDTO sinFecha in await repositorio.LeerSinFecha().ConfigureAwait(false) ?? new List<RetencionPedidoDTO>())
            {
                try
                {
                    DateTime ultimoAviso = (sinFecha.UltimoRecordatorio ?? sinFecha.Fecha).Date;
                    if (ultimoAviso > limite)
                    {
                        continue;
                    }
                    CabeceraPedidoRetencion cabecera = await repositorio.LeerCabecera(sinFecha.Empresa, sinFecha.Pedido).ConfigureAwait(false);
                    if (cabecera != null && !cabecera.QuedaPendiente)
                    {
                        // Se sirvió o se borró por otro camino: la fila se cierra sin avisar a nadie.
                        _ = await repositorio.Liberar(sinFecha.Id, ahora, ServicioRetencionPedidos.USUARIO_SISTEMA, "Sin nada pendiente").ConfigureAwait(false);
                        continue;
                    }
                    resultado.Avisos += await Avisar(notificaciones, Destinatarios(sinFecha, usuariosVendedor),
                        TextosRetencion.NotificacionRecordatorio(sinFecha, cabecera, ahora), registrar).ConfigureAwait(false);
                    await repositorio.MarcarRecordatorio(sinFecha.Id, ahora).ConfigureAwait(false);
                    resultado.Recordados++;
                    resultado.PedidosRecordados.Add(sinFecha.Pedido);
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Retenidos #698] No se ha podido recordar el pedido {sinFecha.Pedido}: {ex.Message}", ex));
                }
            }
            return resultado;
        }

        /// <summary>Quien lo retuvo y los comerciales del pedido, con dominio (como los guarda el buzón de Nesto), sin repetir.</summary>
        internal static List<string> Destinatarios(RetencionPedidoDTO retencion, Func<string, int, List<string>> usuariosVendedor)
        {
            var usuarios = new List<string>();
            void Anadir(string usuario)
            {
                string sinDominio = ComercialesPedido.SinDominio(usuario);
                if (string.IsNullOrWhiteSpace(sinDominio) || sinDominio.Equals(ComercialesPedido.SinDominio(ServicioRetencionPedidos.USUARIO_SISTEMA), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                string conDominio = "NUEVAVISION\\" + sinDominio;
                if (!usuarios.Contains(conDominio, StringComparer.OrdinalIgnoreCase))
                {
                    usuarios.Add(conDominio);
                }
            }
            Anadir(retencion?.Usuario);
            List<string> vendedores = null;
            try
            {
                vendedores = usuariosVendedor?.Invoke(retencion?.Empresa, retencion?.Pedido ?? 0);
            }
            catch
            {
                // Sin los del vendedor: al menos avisa a quien lo retuvo.
            }
            (vendedores ?? new List<string>()).ForEach(Anadir);
            return usuarios;
        }

        private static async Task<int> Avisar(IServicioNotificacionesPush notificaciones, List<string> usuarios, NotificacionPushDTO notificacion, Action<Exception> registrar)
        {
            int avisados = 0;
            if (notificaciones == null)
            {
                return avisados;
            }
            foreach (string usuario in usuarios)
            {
                try
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                    avisados++;
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Retenidos #698] No se ha podido avisar a {usuario}: {ex.Message}", ex));
                }
            }
            return avisados;
        }
    }
}
