using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Sugerencia 1340: lo que necesitan el selector de modos (ofrecer el 6 solo si hay algo con lo que juntarse) y el POST/PUT (rechazar
    /// el 6 sin nada pendiente o sin el script). La regla es <see cref="ReglaJuntoConPendiente"/>; aquí solo se lee y se aplica.
    /// </summary>
    public class ServicioJuntoConPendiente
    {
        private readonly IRepositorioJuntoConPendiente repositorio;
        private readonly Action<Exception> registrar;

        public ServicioJuntoConPendiente() : this(new RepositorioJuntoConPendiente(), null)
        {
        }

        internal ServicioJuntoConPendiente(IRepositorioJuntoConPendiente repositorio, Action<Exception> registrar)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public bool ModoAdmitido() => repositorio.ModoAdmitido();

        /// <summary>Con lo que se juntaría ese pedido (o el que se está montando: <paramref name="pedido"/> 0). Lanza si falla la lectura.</summary>
        public List<PendienteDelCliente> ConLoQueSeJunta(string empresa, int pedido, string cliente, string contacto)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return new List<PendienteDelCliente>();
            }
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            List<PendienteDelCliente> pendientes = repositorio.LeerPendientes(empresaLimpia,
                new[] { new ClaveClienteContacto { Cliente = cliente.Trim(), Contacto = contacto?.Trim() } });
            return ReglaJuntoConPendiente.ConLoQueSeJunta(pendientes, empresaLimpia, pedido, cliente, contacto);
        }

        /// <summary>
        /// Decisión 2: la sugerencia con el modo 6 añadido si el contacto tiene algo con lo que juntarse (o el pedido ya está en 6). Sin el
        /// script, o si falla la lectura, la sugerencia tal cual (nunca lanza).
        /// </summary>
        /// <param name="yaEstaEnModo6">El pedido (guardado o el que manda el cliente) ya está en modo 6.</param>
        public SugeridorModoServicio.Sugerencia Ofrecer(SugeridorModoServicio.Sugerencia sugerencia, PedidoVentaDTO pedido, bool yaEstaEnModo6)
        {
            if (sugerencia == null || pedido == null)
            {
                return sugerencia;
            }
            try
            {
                if (!repositorio.ModoAdmitido())
                {
                    return sugerencia;
                }
                List<PendienteDelCliente> conLoQueSeJunta = ConLoQueSeJunta(pedido.empresa, pedido.numero, pedido.cliente, pedido.contacto);
                ModoServicioPermitidoDTO opcion = ReglaJuntoConPendiente.Opcion(conLoQueSeJunta, yaEstaEnModo6, TipoAlmacen(pedido));
                return ReglaJuntoConPendiente.ConModoJunto(sugerencia, opcion);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Junto con lo pendiente, sugerencia 1340] No se ha podido mirar lo pendiente del cliente {pedido.cliente?.Trim()}/" +
                    $"{pedido.contacto?.Trim()} para ofrecer el modo 6 (se ofrecen los de siempre): {ex.Message}", ex));
                return sugerencia;
            }
        }

        /// <summary>
        /// Decisión 2, al guardar: por qué no puede ENTRAR el pedido en el modo 6 (null si puede, o si no entra en él). Solo al entrar: el
        /// cliente que devuelve el 6 que leyó no se rechaza. Si falla la lectura de lo pendiente, se deja pasar (sale como «Todo junto»).
        /// </summary>
        /// <param name="modoEfectivoNuevo">El modo efectivo con el que va a quedar el pedido.</param>
        /// <param name="modoEfectivoAnterior">El que tenía (null en un alta).</param>
        public string MotivoAlEntrar(PedidoVentaDTO pedido, byte modoEfectivoNuevo, byte? modoEfectivoAnterior)
        {
            if (pedido == null || modoEfectivoNuevo != Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE
                || modoEfectivoAnterior == Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE)
            {
                return null;
            }
            bool admitido = repositorio.ModoAdmitido();
            if (!admitido)
            {
                return ReglaJuntoConPendiente.MOTIVO_SIN_SCRIPT;
            }
            int pendientes;
            try
            {
                pendientes = ConLoQueSeJunta(pedido.empresa, pedido.numero, pedido.cliente, pedido.contacto).Count;
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Junto con lo pendiente, sugerencia 1340] No se ha podido mirar lo pendiente del cliente {pedido.cliente?.Trim()}/" +
                    $"{pedido.contacto?.Trim()} al guardar el pedido {pedido.numero} en modo 6 (se deja pasar): {ex.Message}", ex));
                return null;
            }
            return ReglaJuntoConPendiente.MotivoNoSePuedeElegir(admitido, pendientes);
        }

        /// <summary>
        /// Decisión 7 (portes al grabar): la base imponible de lo pendiente que viaja junto y si ya lleva portes. Sin modo 6, o si falla
        /// la lectura, (0, false): los portes como siempre.
        /// </summary>
        public (decimal basePendiente, bool yaLlevaPortes) PortesDeLoPendiente(PedidoVentaDTO pedido)
        {
            if (pedido == null || Constantes.Pedidos.ModosServicio.Efectivo(pedido.modoServicio, pedido.servirJunto) != Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE)
            {
                return (0, false);
            }
            try
            {
                List<PendienteDelCliente> conLoQueSeJunta = ConLoQueSeJunta(pedido.empresa, pedido.numero, pedido.cliente, pedido.contacto);
                return (ReglaJuntoConPendiente.BaseDeLoPendiente(conLoQueSeJunta), ReglaJuntoConPendiente.LoPendienteYaLlevaPortes(conLoQueSeJunta));
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Junto con lo pendiente, sugerencia 1340] No se han podido sumar los portes de lo pendiente del pedido {pedido.numero} " +
                    $"(se calculan solo con el pedido): {ex.Message}", ex));
                return (0, false);
            }
        }

        private static ModosServicioPermitidos.TipoAlmacen TipoAlmacen(PedidoVentaDTO pedido)
            => ModosServicioPermitidos.Clasificar((pedido.Lineas ?? new List<LineaPedidoVentaDTO>())
                .Where(l => l != null && (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                            && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0));
    }
}
