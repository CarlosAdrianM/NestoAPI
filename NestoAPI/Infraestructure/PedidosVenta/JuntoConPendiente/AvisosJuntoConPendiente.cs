using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Sugerencia 1340 (decisión 5 de Carlos, 18/11/26): a los <see cref="ReglaJuntoConPendiente.DIAS_AVISO_ESPERA"/> días de estar un pedido en
    /// modo 6 sin salir, campana a quien lo hizo (Nesto, si lo hizo con usuario de dominio; si no, push de NestoApp) y push de NestoApp a su
    /// vendedor (como los avisos de #855), para que decida si sigue esperando o lo sirve ya (cambiando el modo). Una sola vez por pedido:
    /// el rastro es la tabla PedidosJuntoConPendienteAvisos. Nunca lanza (ELMAH).
    /// </summary>
    public class AvisosJuntoConPendiente
    {
        public const string TIPO = "PedidoJuntoConPendienteEsperando";

        private readonly IRepositorioJuntoConPendiente repositorio;
        private readonly Func<IServicioNotificacionesPush> crearNotificaciones;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrar;

        public AvisosJuntoConPendiente()
            : this(new RepositorioJuntoConPendiente(), () => new ServicioNotificacionesPush(), () => DateTime.Now, null)
        {
        }

        internal AvisosJuntoConPendiente(IRepositorioJuntoConPendiente repositorio, Func<IServicioNotificacionesPush> crearNotificaciones,
            Func<DateTime> ahora, Action<Exception> registrar)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.crearNotificaciones = crearNotificaciones;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>Avisa de los que llevan 15 días o más esperando. Devuelve cuántos ha avisado. Nunca lanza.</summary>
        public async Task<int> AvisarLosQueLlevanDemasiado()
        {
            int avisados = 0;
            try
            {
                DateTime momento = ahora();
                List<PedidoEsperandoJunto> pedidos = repositorio.LeerEsperandoSinAvisar(momento.Date.AddDays(-ReglaJuntoConPendiente.DIAS_AVISO_ESPERA + 1));
                foreach (PedidoEsperandoJunto pedido in pedidos ?? new List<PedidoEsperandoJunto>())
                {
                    try
                    {
                        List<PendienteDelCliente> conLoQueSeJunta = ReglaJuntoConPendiente.ConLoQueSeJunta(
                            repositorio.LeerPendientes(pedido.Empresa, new[] { new ClaveClienteContacto { Cliente = pedido.Cliente, Contacto = pedido.Contacto } }),
                            pedido.Empresa, pedido.Pedido, pedido.Cliente, pedido.Contacto);
                        if (!conLoQueSeJunta.Any())
                        {
                            continue; // ya no espera a nada: sale en el siguiente picking como «Todo junto»
                        }
                        // Primero el rastro: si la campana falla, no se repite cada día (va a ELMAH).
                        if (!repositorio.MarcarAvisado(pedido.Empresa, pedido.Pedido, momento))
                        {
                            continue;
                        }
                        await Avisar(pedido, Componer(pedido, conLoQueSeJunta, momento)).ConfigureAwait(false);
                        avisados++;
                    }
                    catch (Exception ex)
                    {
                        registrar(new Exception($"[Junto con lo pendiente, sugerencia 1340] No se ha podido avisar del pedido {pedido?.Pedido} en modo 6: {ex.Message}", ex));
                    }
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Junto con lo pendiente, sugerencia 1340] Aviso de los pedidos en modo 6 que llevan días esperando: {ex.Message}", ex));
            }
            return avisados;
        }

        /// <summary>La notificación, de usted. Pura.</summary>
        internal static NotificacionPushDTO Componer(PedidoEsperandoJunto pedido, List<PendienteDelCliente> conLoQueSeJunta, DateTime ahora)
        {
            int dias = Math.Max(0, (ahora.Date - pedido.Desde.Date).Days);
            string cliente = $"{pedido.Cliente?.Trim()}/{pedido.Contacto?.Trim()}";
            return new NotificacionPushDTO
            {
                Tipo = TIPO,
                Titulo = $"Pedido {pedido.Pedido} esperando a lo pendiente del cliente",
                Cuerpo = $"El pedido {pedido.Pedido} (cliente {cliente}) lleva {dias} días en «{Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE)}» " +
                    $"esperando a {ReglaJuntoConPendiente.Documentos(conLoQueSeJunta)}. Decida si sigue esperando o si se sirve ya: para servirlo, " +
                    "cambie el modo de servicio del pedido.",
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO,
                    ["empresa"] = pedido.Empresa?.Trim(),
                    ["pedido"] = pedido.Pedido.ToString(),
                    ["cliente"] = pedido.Cliente?.Trim(),
                    ["contacto"] = pedido.Contacto?.Trim(),
                    ["juntoCon"] = string.Join(",", conLoQueSeJunta.Select(p => p.Pedido))
                }
            };
        }

        /// <summary>A quien hizo el pedido (campana de Nesto o push de NestoApp) y push de NestoApp a su vendedor.</summary>
        private async Task Avisar(PedidoEsperandoJunto pedido, NotificacionPushDTO notificacion)
        {
            IServicioNotificacionesPush notificaciones = crearNotificaciones?.Invoke();
            if (notificaciones == null)
            {
                return;
            }
            string usuario = pedido.Usuario?.Trim();
            if (!string.IsNullOrEmpty(usuario))
            {
                if (usuario.Contains("\\"))
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
                else
                {
                    _ = await notificaciones.EnviarAUsuario(usuario, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
                }
            }
            if (!string.IsNullOrWhiteSpace(pedido.Vendedor))
            {
                _ = await notificaciones.EnviarAVendedor(pedido.Empresa?.Trim(), pedido.Vendedor.Trim(), notificacion).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Sugerencia 1340: job «pedidos-junto-con-pendiente-esperando», L-V a las 9:30. Sin el script, nada.</summary>
    public static class JuntoConPendienteJobsService
    {
        public const string ID_JOB = "pedidos-junto-con-pendiente-esperando";
        /// <summary>De lunes a viernes, a las 9:30.</summary>
        public const string CRON = "30 9 * * 1-5";

        /// <summary>Punto de entrada de Hangfire. Nunca lanza.</summary>
        public static async Task AvisarEsperando()
        {
            try
            {
                _ = await new AvisosJuntoConPendiente().AvisarLosQueLlevanDemasiado().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Junto con lo pendiente, sugerencia 1340] Job {ID_JOB}: {ex.Message}", ex));
            }
        }
    }
}
