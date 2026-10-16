namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>
    /// Sugerencia 644 (Manuel): qué pasa EXACTAMENTE al aplicar una propuesta, en una frase para la pantalla de Nesto (y para
    /// cualquier otro cliente: se calcula aquí, en un solo sitio, y los clientes solo la enseñan). Sale de lo que hace de verdad
    /// <see cref="ServicioDecisionesPropuestasStock"/> al «Aplicar» cada acción; si se cambia lo que escribe una acción, hay que
    /// cambiar también su frase (los tests de ConsecuenciasPropuestaTests lo recuerdan).
    /// <para>Los números son los de la fila: el máximo y el mínimo que había cuando se calculó la propuesta. Al aplicar, la API
    /// vuelve a leer los de ese momento (si alguien los ha tocado desde entonces, el cambio parte de los nuevos).</para>
    /// </summary>
    public static class ConsecuenciasPropuesta
    {
        internal const string SIN_CAMBIOS = "No cambia nada.";
        internal const string SOLO_ANOTADO = "Solo queda anotado: no se cambia nada automáticamente, hay que hacerlo a mano.";
        /// <summary>NestoAPI#628: con el canal Ganavisiones, «Liquidar» no cambia el estado.</summary>
        internal const string LIQUIDAR_GANAVISIONES = "Si elige el canal Ganavisiones, el estado no cambia: solo el máximo y el mínimo de este almacén " +
            "pasan a 0 y el paso a Ganavisiones lo hace usted a mano.";
        internal const string PROPIOS_NO_SE_LIQUIDAN = "Los productos propios (fabricación propia) nunca se liquidan: esta propuesta no se puede aplicar.";

        public static string Texto(PropuestaStockConDecision propuesta)
        {
            if (propuesta == null || string.IsNullOrWhiteSpace(propuesta.Accion))
            {
                return null;
            }
            string almacen = string.IsNullOrWhiteSpace(propuesta.Almacen) ? "el almacén" : propuesta.Almacen.Trim();
            string accion = propuesta.Accion.Trim();
            switch (accion)
            {
                case AccionesPropuesta.MANTENER:
                case AccionesPropuesta.NADA:
                    return SIN_CAMBIOS;
                case AccionesPropuesta.AJUSTAR:
                case AccionesPropuesta.AJUSTAR_LOTE:
                case AccionesPropuesta.CADUCA:
                    return Ajuste(propuesta, almacen);
                case AccionesPropuesta.POTENCIAR:
                    return Ajuste(propuesta, almacen) + " Si prefiere no tocar el stock, la decisión «Potenciar» fija un objetivo de venta " +
                        $"(unidades al mes) y una fecha de revisión (por defecto, dentro de {ServicioDecisionesPropuestasStock.MESES_POTENCIACION_POR_DEFECTO} meses): " +
                        "hasta entonces sale «En potenciación» y no como pendiente.";
                case AccionesPropuesta.ESTADO_1:
                    {
                        string ahora = TieneMaximos(propuesta) ? $" (en {almacen}, ahora {propuesta.StockMaximoActual ?? 0}/{propuesta.StockMinimoActual ?? 0})" : string.Empty;
                        return $"El producto pasa a estado {ServicioDecisionesPropuestasStock.ESTADO_SOBRE_PEDIDO} (sobre pedido) y el máximo y el mínimo " +
                            $"de todos los almacenes quedan a 0{ahora}.";
                    }
                case AccionesPropuesta.LIQUIDAR:
                    {
                        // NestoAPI#628: los propios nunca se liquidan (el servicio devuelve 400 si se intenta).
                        if (propuesta.FabricacionPropia)
                        {
                            return PROPIOS_NO_SE_LIQUIDAN;
                        }
                        string stock;
                        if (!propuesta.StockMaximoActual.HasValue && !propuesta.StockMinimoActual.HasValue)
                        {
                            stock = $"no hay control de stock en {almacen}, así que no cambia ningún máximo";
                        }
                        else if (!TieneMaximos(propuesta))
                        {
                            stock = $"el máximo y el mínimo de {almacen} ya están a 0";
                        }
                        else
                        {
                            stock = $"el máximo y el mínimo de {almacen} pasan de {propuesta.StockMaximoActual ?? 0}/{propuesta.StockMinimoActual ?? 0} a 0";
                        }
                        string estado = propuesta.EstadoProducto == ServicioDecisionesPropuestasStock.ESTADO_A_EXTINGUIR
                            ? $"Ya está en estado {ServicioDecisionesPropuestasStock.ESTADO_A_EXTINGUIR} y, si tiene ficha en la tienda, entra en el Outlet en la pasada nocturna siguiente"
                            : $"Pasa a estado {ServicioDecisionesPropuestasStock.ESTADO_A_EXTINGUIR} y, si tiene ficha en la tienda, entra en el Outlet en la pasada nocturna siguiente";
                        return $"{estado}; {stock} (y los de los demás almacenes, si los tiene, también quedan a 0). {LIQUIDAR_GANAVISIONES}";
                    }
                case AccionesPropuesta.PARAR_COMPRAS:
                    {
                        string pedido = propuesta.PedidoCompraAbierto > 0
                            ? $" Hay {propuesta.PedidoCompraAbierto} uds en pedidos de compra abiertos: hay que reducirlos o anularlos usted en el pedido."
                            : " Hay que reducir o anular usted el pedido de compra abierto.";
                        return SOLO_ANOTADO + pedido;
                    }
                default:
                    return $"«{accion}»: no se cambia nada automáticamente, hay que hacerlo a mano.";
            }
        }

        private static bool TieneMaximos(PropuestaStockConDecision p) => (p.StockMaximoActual ?? 0) != 0 || (p.StockMinimoActual ?? 0) != 0;

        /// <summary>Como el «Aplicar» de un «Ajustar máx/mín»: escribe el máximo y el mínimo propuestos en el almacén (o crea el control).</summary>
        private static string Ajuste(PropuestaStockConDecision p, string almacen)
        {
            if (!p.StockMaximoPropuesto.HasValue || !p.StockMinimoPropuesto.HasValue)
            {
                return "La propuesta no trae máximo y mínimo: tendrá que indicarlos usted al aplicarla.";
            }
            int maximo = p.StockMaximoPropuesto.Value;
            int minimo = p.StockMinimoPropuesto.Value;
            if (!p.StockMaximoActual.HasValue && !p.StockMinimoActual.HasValue)
            {
                return $"Se crea el control de stock de {almacen} con máximo {maximo} y mínimo {minimo}.";
            }
            int maximoActual = p.StockMaximoActual ?? 0;
            int minimoActual = p.StockMinimoActual ?? 0;
            bool cambiaMaximo = maximoActual != maximo;
            bool cambiaMinimo = minimoActual != minimo;
            if (cambiaMaximo && cambiaMinimo)
            {
                return $"El máximo de {almacen} pasa de {maximoActual} a {maximo} y el mínimo de {minimoActual} a {minimo}.";
            }
            if (cambiaMaximo)
            {
                return $"El máximo de {almacen} pasa de {maximoActual} a {maximo}; el mínimo se queda en {minimo}.";
            }
            if (cambiaMinimo)
            {
                return $"El mínimo de {almacen} pasa de {minimoActual} a {minimo}; el máximo se queda en {maximo}.";
            }
            return $"El máximo y el mínimo de {almacen} ya son {maximo}/{minimo}: no cambia nada.";
        }
    }
}
