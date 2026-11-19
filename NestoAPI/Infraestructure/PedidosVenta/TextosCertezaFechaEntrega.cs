using NestoAPI.Models.PedidosVenta;
using System;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#853: el grado de certeza de la fecha de una partida y sus textos, UNA pieza pura para los tres compositores: el motivo del
    /// motor (<see cref="CalculadoraFechaEntregaAgencia"/>, que pintan Nesto y NestoApp), el correo del pedido
    /// (<c>GestorPresupuestos.GenerarHtmlFechaEntregaAgencia</c>) y la promesa en riesgo (<see cref="TextosPromesaRiesgo"/>). Cada uno
    /// formatea los días a su manera (<paramref name="dia"/> en cada método).
    /// <list type="bullet">
    /// <item>Segura: sale de Algete (picking, libre, kit, pack que se monta con sus componentes en Algete). Un pack que espera un
    /// componente tiene la certeza de ese componente (<see cref="MontajeKits.ComoComponente"/>).</item>
    /// <item>Prácticamente segura: en una tienda o de camino desde ella; pedido a proveedor ya enviado con seguimiento de la agencia.</item>
    /// <item>Aproximada: pedido a proveedor con fecha prevista y sin seguimiento (también con la prevista vencida).</item>
    /// <item>Orientativa: sin fecha, con la estimación de <see cref="PartidaFechaEntregaAgenciaDTO.FechaOrientativa"/> (no es un compromiso).</item>
    /// </list>
    /// </summary>
    public static class TextosCertezaFechaEntrega
    {
        public const string ETIQUETA_CASI_SEGURA = "Prácticamente segura";
        public const string ETIQUETA_APROXIMADA = "Aproximada";
        public const string ETIQUETA_ORIENTATIVA = "Orientativa";

        public const string TEXTO_CASI_SEGURA = "fecha prácticamente segura";
        public const string TEXTO_EN_TIENDAS = "ya está en nuestras tiendas";
        public const string TEXTO_APROXIMADA = "fecha aproximada, aún sin seguimiento de la agencia";
        public const string TEXTO_ORIENTATIVA = "orientativo, no es un compromiso";

        /// <summary>
        /// Pedido 931105 (19/11/26): la fecha que se da es posterior a la que daría la mercancía PORQUE el pedido tiene una fecha de
        /// entrega posterior: «No sale antes del jueves 26/11/2026, que es la fecha de entrega del pedido.» La misma frase para el motivo
        /// del motor, el pie del correo y «Dónde está lo que falta» (cada uno con su formato de día).
        /// </summary>
        public static string FechaEntregaDelPedido(DateTime fecha, Func<DateTime, string> dia)
            => $"No sale antes del {(dia ?? (d => d.ToString("dd/MM/yyyy")))(fecha)}, que es la fecha de entrega del pedido.";

        /// <summary>El grado de certeza de la partida (<see cref="PartidaFechaEntregaAgenciaDTO"/>.CERTEZA_*), o null si no hay nada que decir.</summary>
        public static string Certeza(PartidaFechaEntregaAgenciaDTO p)
        {
            if (p == null)
            {
                return null;
            }
            switch (p.Origen)
            {
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PICKING:
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE:
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT:
                    return PartidaFechaEntregaAgenciaDTO.CERTEZA_SEGURA;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE:
                    // NestoAPI#606 (packs montables): con los componentes en Algete, segura; si espera uno, la certeza de ese componente
                    PartidaFechaEntregaAgenciaDTO componente = MontajeKits.ComoComponente(p);
                    return componente == null ? PartidaFechaEntregaAgenciaDTO.CERTEZA_SEGURA : Certeza(componente);
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA:
                    return p.PuedeSalirEl.HasValue ? PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA : Orientativa(p);
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO:
                    return PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR:
                    if (!p.FechaPrevistaProveedor.HasValue || !p.PuedeSalirEl.HasValue)
                    {
                        return Orientativa(p);
                    }
                    return p.ConSeguimientoAgencia && !p.LlegadaSupuesta.HasValue
                        ? PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA
                        : PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA;
                default:
                    return Orientativa(p);
            }
        }

        private static string Orientativa(PartidaFechaEntregaAgenciaDTO p) =>
            p.FechaOrientativa.HasValue ? PartidaFechaEntregaAgenciaDTO.CERTEZA_ORIENTATIVA : null;

        /// <summary>La etiqueta corta para el correo («Prácticamente segura», «Aproximada», «Orientativa»); null en lo seguro y sin certeza.</summary>
        public static string Etiqueta(string certeza)
        {
            switch (certeza)
            {
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA: return ETIQUETA_CASI_SEGURA;
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA: return ETIQUETA_APROXIMADA;
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_ORIENTATIVA: return ETIQUETA_ORIENTATIVA;
                default: return null;
            }
        }

        public const string CORTO_SEGURO = "seguro";
        public const string CORTO_APROXIMADO = "aproximado";
        public const string CORTO_ORIENTATIVO = "orientativo";

        /// <summary>
        /// La certeza en UNA palabra, para la promesa en riesgo (Carlos, 18/11/26): Segura y CasiSegura → «seguro» (sale de Algete, de una
        /// tienda, de camino o con seguimiento de la agencia: para el vendedor es lo mismo), Aproximada → «aproximado», Orientativa →
        /// «orientativo». Null sin certeza. No cambia nada de lo de arriba (motivo del motor y correo de fechas).
        /// </summary>
        public static string CalificativoCorto(string certeza)
        {
            switch (certeza)
            {
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_SEGURA:
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA:
                    return CORTO_SEGURO;
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA:
                    return CORTO_APROXIMADO;
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_ORIENTATIVA:
                    return CORTO_ORIENTATIVO;
                default:
                    return null;
            }
        }

        /// <summary>Para quedarse con la peor de varias certezas (Segura 0 … Orientativa 3; sin certeza, -1).</summary>
        public static int Gravedad(string certeza)
        {
            switch (certeza)
            {
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_SEGURA: return 0;
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA: return 1;
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA: return 2;
                case PartidaFechaEntregaAgenciaDTO.CERTEZA_ORIENTATIVA: return 3;
                default: return -1;
            }
        }

        /// <summary>
        /// Lo que se dice de la fecha de una partida CON fecha, sin el día: «fecha prácticamente segura: ya está en nuestras tiendas»,
        /// «fecha prácticamente segura», «fecha aproximada, aún sin seguimiento de la agencia». Null en lo seguro, en lo vencido (tiene su
        /// propio aviso) y sin fecha.
        /// </summary>
        public static string Calificativo(PartidaFechaEntregaAgenciaDTO p)
        {
            if (p == null || !p.PuedeSalirEl.HasValue)
            {
                return null;
            }
            switch (p.Origen)
            {
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA:
                    return TEXTO_CASI_SEGURA + ": " + TEXTO_EN_TIENDAS;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO:
                    return TEXTO_CASI_SEGURA;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR:
                    if (!p.FechaPrevistaProveedor.HasValue || p.LlegadaSupuesta.HasValue)
                    {
                        return null;
                    }
                    return p.ConSeguimientoAgencia ? TEXTO_CASI_SEGURA : TEXTO_APROXIMADA;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE:
                    PartidaFechaEntregaAgenciaDTO componente = MontajeKits.ComoComponente(p);
                    return componente == null ? null : Calificativo(componente);
                default:
                    return null;
            }
        }

        /// <summary>«ya enviado por UPS» / «ya enviado por el proveedor, con seguimiento de la agencia».</summary>
        public static string Envio(string agencia) => string.IsNullOrWhiteSpace(agencia)
            ? "ya enviado por el proveedor, con seguimiento de la agencia"
            : $"ya enviado por {agencia.Trim()}";

        /// <summary>
        /// La llegada del proveedor, sin calificativo ni salida: «ya enviado por UPS, que prevé entregarlo el lunes 16/11» (con seguimiento) o
        /// «llegada estimada el lunes 16/11» (sin él). Null sin prevista.
        /// </summary>
        public static string LlegadaProveedor(PartidaFechaEntregaAgenciaDTO p, Func<DateTime, string> dia)
        {
            if (p?.FechaPrevistaProveedor == null)
            {
                return null;
            }
            return p.ConSeguimientoAgencia
                ? $"{Envio(p.AgenciaSeguimiento)}, que prevé entregarlo el {dia(p.FechaPrevistaProveedor.Value)}"
                : $"llegada estimada el {dia(p.FechaPrevistaProveedor.Value)}";
        }

        /// <summary>
        /// Lo que se dice de una partida SIN fecha con estimación: «aún no hay pedido al proveedor que lo cubra; estimamos que podría salir
        /// sobre el martes 24/11 (orientativo, no es un compromiso)» o, con un pedido lejano, «el pedido a proveedor 220900 está previsto para
        /// el lunes 18/01/2027; estimamos que…». Null sin <see cref="PartidaFechaEntregaAgenciaDTO.FechaOrientativa"/>.
        /// </summary>
        public static string SinFechaOrientativa(PartidaFechaEntregaAgenciaDTO p, Func<DateTime, string> dia)
        {
            if (p?.FechaOrientativa == null)
            {
                return null;
            }
            string estimacion = $"estimamos que podría salir sobre el {dia(p.FechaOrientativa.Value)} ({TEXTO_ORIENTATIVA})";
            if (!string.IsNullOrWhiteSpace(p.ComponenteQueFalta))
            {
                // NestoAPI#606 (packs montables): el pack espera un componente sin fecha
                return $"para montarlo en Algete falta {MontajeKits.Componente(p)}, aún sin fecha de llegada; {estimacion}";
            }
            if (p.PedidoProveedorOrientativo.HasValue || p.FechaPrevistaProveedorOrientativo.HasValue)
            {
                string pedido = p.PedidoProveedorOrientativo.HasValue ? $"el pedido a proveedor {p.PedidoProveedorOrientativo.Value}" : "el pedido al proveedor";
                string prevista = p.FechaPrevistaProveedorOrientativo.HasValue ? $" está previsto para el {dia(p.FechaPrevistaProveedorOrientativo.Value)}" : " ya está hecho";
                return $"{pedido}{prevista}; {estimacion}";
            }
            return $"aún no hay pedido al proveedor que lo cubra; {estimacion}";
        }

        /// <summary>
        /// El motivo «sin fecha» de lo que cubre un pedido a proveedor previsto a más de 15 días: «hay un pedido a proveedor (220900)
        /// previsto para el lunes 18/01/2027, a más de 15 días» (antes decía «no hay stock ni fecha de llegada», que era falso).
        /// </summary>
        public static string MotivoPedidoLejano(int? pedido, DateTime prevista, Func<DateTime, string> dia) =>
            $"hay un pedido a proveedor{(pedido.HasValue ? $" ({pedido.Value})" : string.Empty)} previsto para el {dia(prevista)}, " +
            $"a más de {ClasificadorCausasModoServicio.DIAS_MAXIMOS_EN_CAMINO} días";
    }
}
