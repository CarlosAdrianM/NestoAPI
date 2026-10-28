using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.Retencion
{
    /// <summary>
    /// NestoAPI#698 (Carlos, 28/10/26): cuando se retiene un pedido que ya tiene picking, el almacén tiene que enterarse
    /// para no entregarlo a la agencia. Campana de Nesto (con dominio) y buzón de Ariadna (sin dominio) a todo el grupo
    /// Almacén, como el aviso de las diferencias de reposición (<see cref="PreparacionAlmacen.AvisadorReposiciones"/>).
    /// </summary>
    public class AvisadorRetencionAlmacen
    {
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<List<string>> usuariosDeAlmacen;

        public AvisadorRetencionAlmacen() : this(null, null)
        {
        }

        internal AvisadorRetencionAlmacen(IServicioNotificacionesPush notificaciones, Func<List<string>> usuariosDeAlmacen)
        {
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.usuariosDeAlmacen = usuariosDeAlmacen ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ALMACEN));
        }

        /// <summary>A quién se avisa: el grupo Almacén, limpio y sin repetidos (con dominio). Vacía si no hay nadie.</summary>
        internal List<string> Destinatarios()
        {
            return (usuariosDeAlmacen() ?? new List<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public Task Avisar(RetencionPedidoDTO retencion, CabeceraPedidoRetencion cabecera, List<int> pickings)
        {
            return retencion == null ? Task.CompletedTask : Avisar(TextosRetencion.NotificacionPickingEnCurso(retencion, cabecera, pickings));
        }

        /// <summary>NestoAPI#722: la misma campana para cualquier aviso al grupo Almacén (aplazamiento con picking).</summary>
        public async Task Avisar(NotificacionPushDTO notificacion)
        {
            if (notificacion == null)
            {
                return;
            }
            List<string> usuarios = Destinatarios();
            if (usuarios.Count == 0)
            {
                throw new InvalidOperationException("No hay nadie en el grupo Almacén a quien avisar.");
            }
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                await notificaciones.GuardarEnBuzonDeUsuario(usuario.Substring(usuario.LastIndexOf('\\') + 1), Constantes.Aplicaciones.ARIADNA, notificacion)
                    .ConfigureAwait(false);
            }
        }
    }
}
