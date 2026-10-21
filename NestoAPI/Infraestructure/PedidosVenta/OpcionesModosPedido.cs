using NestoAPI.Models;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#688: lo que Nesto y NestoApp tienen que enseñar de los modos del pedido. Respuesta de
    /// <c>POST api/PedidosVenta/OpcionesModos</c>. PascalCase, como las sugerencias de las que sale.
    /// </summary>
    public class OpcionesModosPedidoDTO
    {
        /// <summary>El modo de servicio: la MISMA sugerencia que <c>POST api/PedidosVenta/ModoServicioSugerido</c>.</summary>
        public SugeridorModoServicio.Sugerencia Servicio { get; set; }
        /// <summary>El modo de facturación: la MISMA sugerencia que <c>POST api/PedidosVenta/ModoFacturacionSugerido</c>, más si se enseña.</summary>
        public OpcionesFacturacionPedidoDTO Facturacion { get; set; }
        /// <summary>La casilla «¿Servir lo pendiente en cuanto haya stock?».</summary>
        public OpcionServirPendienteAlHaberStockDTO ServirPendienteAlHaberStock { get; set; }
    }

    /// <summary>NestoAPI#688: la sugerencia de facturación de siempre y si el selector se enseña.</summary>
    public class OpcionesFacturacionPedidoDTO : ModosFacturacionPermitidos.Sugerencia
    {
        /// <summary>False = no se enseña el selector (y el cliente no manda modoFacturacion: se queda lo que pongan los triggers).</summary>
        public bool Visible { get; set; }
        /// <summary>Por qué no se enseña. Null si se enseña.</summary>
        public string MotivoOculta { get; set; }
    }

    /// <summary>NestoAPI#688: la casilla «¿Servir lo pendiente en cuanto haya stock?».</summary>
    public class OpcionServirPendienteAlHaberStockDTO
    {
        public bool Visible { get; set; }
        /// <summary>El valor que hay que pintar (y mandar al grabar). Siempre con valor.</summary>
        public bool Valor { get; set; }
        /// <summary>Por qué no se enseña. Null si se enseña.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#688 (decisiones de Carlos, 21/10/26): LA regla que cruza modo de servicio × modo de facturación × casilla
    /// «¿Servir lo pendiente en cuanto haya stock?». Núcleo puro: lo que no tiene sentido no se enseña.
    /// <list type="bullet">
    /// <item>Nota de entrega: no se factura (sin selector de facturación); la casilla se enseña con lo que tenga la nota
    /// (se cambia con <c>POST api/PedidosVenta/{empresa}/{nota}/ServirPendienteAlHaberStock</c>, no con el PUT).</item>
    /// <item>«Todo junto»: el selector de facturación se OCULTA y se deja lo que pongan los triggers (decisión 6), salvo
    /// que el pedido ya esté en «Todo ahora»: esa combinación se permite con el aviso rojo de siempre (decisión 7) y el
    /// usuario tiene que verla para poder cambiarla.</item>
    /// <item>La casilla, solo con «Todo ahora» (decisión 10). Con «Todo junto» no se enseña: el pedido sale entero de una
    /// vez y no queda nada pendiente que servir después.</item>
    /// <item>Valor: el informado; si no, el guardado (solo si el pedido ya estaba en «Todo ahora»); si no, MARCADA
    /// (decisión 12: al elegir «Todo ahora» viene marcada). Sin «Todo ahora», false.</item>
    /// </list>
    /// </summary>
    public static class OpcionesModosPedido
    {
        internal const string MOTIVO_FACTURACION_TODO_JUNTO = "Con «Todo junto» el pedido sale de una vez: la facturación se queda como la pongan las condiciones del cliente.";
        internal const string MOTIVO_FACTURACION_NOTA = ModosFacturacionPermitidos.MOTIVO_NOTA_ENTREGA;
        internal const string MOTIVO_CASILLA_SOLO_TODO_AHORA = "Solo con la facturación «Todo ahora, lo pendiente se entrega después».";
        internal const string MOTIVO_CASILLA_TODO_JUNTO = "Con «Todo junto» el pedido sale entero de una vez: no queda nada pendiente.";

        /// <param name="servicio">La sugerencia de modo de servicio (la de ModoServicioSugerido).</param>
        /// <param name="facturacion">La sugerencia de modo de facturación (la de ModoFacturacionSugerido).</param>
        /// <param name="modoServicioElegido">El modo de servicio que tiene (o va a tener) el pedido.</param>
        /// <param name="modoFacturacionElegido">El modo de facturación efectivo que tiene (o va a tener) el pedido.</param>
        /// <param name="servirPendienteInformado">Lo que manda el cliente en el DTO (null = no lo dice).</param>
        /// <param name="servirPendienteAlmacenado">Lo guardado en el pedido si YA estaba en «Todo ahora» (o en la nota); null si no.</param>
        /// <param name="esNotaEntrega">El pedido es una nota de entrega.</param>
        public static OpcionesModosPedidoDTO Calcular(SugeridorModoServicio.Sugerencia servicio, ModosFacturacionPermitidos.Sugerencia facturacion,
            byte modoServicioElegido, byte modoFacturacionElegido, bool? servirPendienteInformado, bool? servirPendienteAlmacenado, bool esNotaEntrega)
        {
            bool todoJunto = Constantes.Pedidos.ModosServicio.EsTodoJunto(modoServicioElegido);
            bool todoAhora = Constantes.Pedidos.ModosFacturacion.EsTodoAhora(modoFacturacionElegido);

            string motivoOculta = esNotaEntrega ? MOTIVO_FACTURACION_NOTA
                : todoJunto && !todoAhora ? MOTIVO_FACTURACION_TODO_JUNTO
                : null;
            var opcionesFacturacion = new OpcionesFacturacionPedidoDTO
            {
                Visible = motivoOculta == null,
                MotivoOculta = motivoOculta
            };
            if (facturacion != null)
            {
                opcionesFacturacion.Modo = facturacion.Modo;
                opcionesFacturacion.Nombre = facturacion.Nombre;
                opcionesFacturacion.Motivo = facturacion.Motivo;
                opcionesFacturacion.ModosPermitidos = facturacion.ModosPermitidos;
                opcionesFacturacion.Modos = facturacion.Modos;
            }

            return new OpcionesModosPedidoDTO
            {
                Servicio = servicio,
                Facturacion = opcionesFacturacion,
                ServirPendienteAlHaberStock = Casilla(todoJunto, todoAhora, servirPendienteInformado, servirPendienteAlmacenado, esNotaEntrega)
            };
        }

        private static OpcionServirPendienteAlHaberStockDTO Casilla(bool todoJunto, bool todoAhora, bool? informado, bool? almacenado, bool esNotaEntrega)
        {
            if (esNotaEntrega)
            {
                return new OpcionServirPendienteAlHaberStockDTO { Visible = true, Valor = informado ?? almacenado ?? false };
            }
            if (!todoAhora)
            {
                return new OpcionServirPendienteAlHaberStockDTO { Visible = false, Valor = false, Motivo = MOTIVO_CASILLA_SOLO_TODO_AHORA };
            }
            bool valor = ValorConTodoAhora(informado, almacenado);
            return todoJunto
                ? new OpcionServirPendienteAlHaberStockDTO { Visible = false, Valor = valor, Motivo = MOTIVO_CASILLA_TODO_JUNTO }
                : new OpcionServirPendienteAlHaberStockDTO { Visible = true, Valor = valor };
        }

        /// <summary>Decisión 12: con «Todo ahora», lo informado; si no, lo guardado; si no, marcada.</summary>
        internal static bool ValorConTodoAhora(bool? informado, bool? almacenado) => informado ?? almacenado ?? true;

        /// <summary>
        /// El valor efectivo de la casilla de un pedido guardado (lo que se devuelve al leerlo): en una nota, lo que tenga;
        /// en un pedido, solo cuenta con «Todo ahora» (NULL = como hasta ahora, sin marcar: los pedidos existentes no se
        /// tocan, decisión 13).
        /// </summary>
        public static bool Efectivo(bool? almacenado, byte modoFacturacionEfectivo, bool esNotaEntrega)
        {
            if (esNotaEntrega)
            {
                return almacenado == true;
            }
            return Constantes.Pedidos.ModosFacturacion.EsTodoAhora(modoFacturacionEfectivo) && almacenado == true;
        }

        /// <summary>
        /// Lo que lleva el DTO al LEER el pedido: el efectivo cuando la casilla aplica (nota o «Todo ahora») y null cuando no
        /// aplica. Así el cliente que reenvía lo que leyó (el detalle de Nesto) manda null al pasar a «Todo ahora» y la
        /// casilla viene marcada por defecto (decisión 12), y null al guardar es «no tocar».
        /// </summary>
        public static bool? ParaLeer(bool? almacenado, byte modoFacturacionEfectivo, bool esNotaEntrega)
        {
            if (!esNotaEntrega && !Constantes.Pedidos.ModosFacturacion.EsTodoAhora(modoFacturacionEfectivo))
            {
                return null;
            }
            return Efectivo(almacenado, modoFacturacionEfectivo, esNotaEntrega);
        }

        internal const string MOTIVO_RECHAZO_CASILLA = "La opción «¿Servir lo pendiente en cuanto haya stock?» solo se puede marcar con la facturación " +
            "«Todo ahora, lo pendiente se entrega después». Desmárquela o cambie la facturación.";

        /// <summary>Resultado de <see cref="ResolverAlGuardar"/>.</summary>
        public class ResolucionCasilla
        {
            /// <summary>El motivo del rechazo (400), o null.</summary>
            public string Error { get; set; }
            /// <summary>Lo que hay que escribir en la columna; null = no se toca.</summary>
            public bool? AGuardar { get; set; }
        }

        /// <summary>
        /// POST/PUT: qué hacer con la casilla. Como el resto de validaciones de modos, solo se rechaza al ENTRAR en una
        /// combinación que no vale: marcarla en un pedido que no está ni estaba en «Todo ahora». Un cliente que sigue
        /// mandando el valor que leyó después de cambiar la facturación (salía de «Todo ahora») no se rechaza: la casilla
        /// deja de contar, como cualquier pedido sin «Todo ahora».
        /// <list type="bullet">
        /// <item>Con «Todo ahora»: lo informado; sin informar, al ENTRAR en «Todo ahora» (alta o cambio de facturación)
        /// marcada (decisión 12); si ya estaba, no se toca.</item>
        /// <item>Sin «Todo ahora»: no se toca nunca (los pedidos existentes no se tocan).</item>
        /// </list>
        /// </summary>
        /// <param name="informado">Lo que manda el cliente (null = no lo dice).</param>
        /// <param name="modoFacturacionNuevo">El modo de facturación efectivo YA normalizado.</param>
        /// <param name="modoFacturacionAnterior">El efectivo que tenía el pedido; null en un alta.</param>
        public static ResolucionCasilla ResolverAlGuardar(bool? informado, byte modoFacturacionNuevo, byte? modoFacturacionAnterior)
        {
            bool nuevoTodoAhora = Constantes.Pedidos.ModosFacturacion.EsTodoAhora(modoFacturacionNuevo);
            bool anteriorTodoAhora = modoFacturacionAnterior.HasValue && Constantes.Pedidos.ModosFacturacion.EsTodoAhora(modoFacturacionAnterior.Value);
            if (nuevoTodoAhora)
            {
                if (informado.HasValue)
                {
                    return new ResolucionCasilla { AGuardar = informado };
                }
                return new ResolucionCasilla { AGuardar = anteriorTodoAhora ? (bool?)null : true };
            }
            if (informado == true && !anteriorTodoAhora)
            {
                return new ResolucionCasilla { Error = MOTIVO_RECHAZO_CASILLA };
            }
            return new ResolucionCasilla();
        }
    }
}
