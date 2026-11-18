using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Pedido 925901 (18/11/26, decisión de Carlos): de dónde sale una línea de producto para el correo del pedido, según el motor
    /// de la fecha de entrega a la agencia (NestoAPI#606). Ordenado de mejor a peor: una línea repartida en varias partidas toma la peor.
    /// </summary>
    public enum SituacionStockLineaCorreo
    {
        /// <summary>Sale de Algete: ya en el picking o con stock libre.</summary>
        EnAlgete = 0,
        /// <summary>Sale de Algete desmontando un kit o montando un pack con componentes que ya están allí.</summary>
        SeMontaEnAlgete = 1,
        /// <summary>Tiene que venir de una tienda (reposición, ya de camino, o el componente de un pack que llega de una tienda).</summary>
        TieneQueVenir = 2,
        /// <summary>Falta de verdad: espera un pedido a proveedor o no tiene fecha (también el pack cuyo componente falta así).</summary>
        FaltaStock = 3
    }

    /// <summary>
    /// Pedido 925901 (18/11/26, decisión de Carlos: «el cambio correcto»): el color de cada línea del correo del pedido sale del MISMO
    /// motor que el pie «Entrega a la agencia… Por qué:», para que el correo tenga una sola verdad. Antes el color era
    /// <see cref="GestorStocks.ColorStock(string, string, int)"/> (stock del almacén − TODOS los pendientes de entregar, sin mirar de qué
    /// pedido ni su fecha) y un pedido a 31/12/2099 ponía en rojo una línea que el pie, con razón, daba por servida de Algete.
    /// Pura. Sin motor para la línea (presupuesto, no es de Algete, ya servida, el motor falló o no la trae) devuelve null y el
    /// correo sigue con <see cref="GestorStocks.ColorStock(string, string, int)"/> como respaldo.
    /// </summary>
    public static class ReglaColorLineaCorreo
    {
        public const string VERDE = "green";
        /// <summary>El color que en el correo siempre ha significado «tiene que venir de otra tienda».</summary>
        public const string ROSA = "DeepPink";
        public const string ROJO = "red";

        /// <summary>La situación de una partida del motor. Null si el origen no se conoce (el correo usa el respaldo).</summary>
        public static SituacionStockLineaCorreo? DePartida(PartidaFechaEntregaAgenciaDTO partida)
        {
            switch (partida?.Origen)
            {
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PICKING:
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE:
                    return SituacionStockLineaCorreo.EnAlgete;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT:
                    return SituacionStockLineaCorreo.SeMontaEnAlgete;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE:
                    return string.IsNullOrWhiteSpace(partida.ComponenteQueFalta)
                        ? SituacionStockLineaCorreo.SeMontaEnAlgete
                        : DeOrigenQueTrae(partida.OrigenComponente);
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA:
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO:
                    return SituacionStockLineaCorreo.TieneQueVenir;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR:
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK:
                    return SituacionStockLineaCorreo.FaltaStock;
                default:
                    return null;
            }
        }

        /// <summary>El componente de un pack que falta en Algete: de una tienda = tiene que venir; si no, falta de verdad.</summary>
        private static SituacionStockLineaCorreo DeOrigenQueTrae(string origenComponente)
        {
            return origenComponente == PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA || origenComponente == PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO
                ? SituacionStockLineaCorreo.TieneQueVenir
                : SituacionStockLineaCorreo.FaltaStock;
        }

        /// <summary>La peor de las partidas de un producto. Null si no hay ninguna o alguna tiene un origen desconocido.</summary>
        public static SituacionStockLineaCorreo? DePartidas(IEnumerable<PartidaFechaEntregaAgenciaDTO> partidas)
        {
            List<SituacionStockLineaCorreo?> situaciones = (partidas ?? Enumerable.Empty<PartidaFechaEntregaAgenciaDTO>())
                .Where(p => p != null)
                .Select(DePartida)
                .ToList();
            if (!situaciones.Any() || situaciones.Any(s => !s.HasValue))
            {
                return null;
            }
            return situaciones.Max();
        }

        /// <summary>El color de la línea en el correo.</summary>
        public static string Color(SituacionStockLineaCorreo situacion)
        {
            switch (situacion)
            {
                case SituacionStockLineaCorreo.EnAlgete:
                    return VERDE;
                case SituacionStockLineaCorreo.FaltaStock:
                    return ROJO;
                default:
                    return ROSA; // tiene que venir de una tienda, o se monta/desmonta un kit en Algete
            }
        }

        /// <summary>
        /// ¿El motor calcula esta línea? Solo las de producto, pendientes (-1, 1), de Algete y con cantidad, de un pedido (no
        /// presupuesto): son las que <c>ServicioFechaEntregaAgencia</c> reparte. Las demás van con el respaldo.
        /// </summary>
        public static bool LaCalculaElMotor(LineaPedidoVentaDTO linea, bool esPresupuesto)
        {
            return !esPresupuesto
                && linea != null
                && linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                && linea.estado >= Constantes.EstadosLineaVenta.PENDIENTE
                && linea.estado <= Constantes.EstadosLineaVenta.EN_CURSO
                && linea.Cantidad > 0
                && !string.IsNullOrWhiteSpace(linea.Producto)
                && RepartidorStockFechaEntregaAgencia.EsDeAlgete(linea.almacen);
        }

        /// <summary>Las partidas del motor del producto de la línea (el desglose va por producto).</summary>
        public static List<PartidaFechaEntregaAgenciaDTO> PartidasDe(FechaEntregaAgenciaDTO fecha, string producto)
        {
            string codigo = producto?.Trim();
            if (fecha?.Desglose == null || string.IsNullOrEmpty(codigo))
            {
                return new List<PartidaFechaEntregaAgenciaDTO>();
            }
            return fecha.Desglose
                .Where(p => p != null && string.Equals(p.Producto?.Trim(), codigo, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// La situación de la línea según el motor, o null si no hay motor para ella (el correo usa
        /// <see cref="GestorStocks.ColorStock(string, string, int)"/>). Dos líneas del mismo producto llevan la misma situación (la peor
        /// del producto): el motor reparte por producto.
        /// </summary>
        public static SituacionStockLineaCorreo? DeLinea(LineaPedidoVentaDTO linea, FechaEntregaAgenciaDTO fecha, bool esPresupuesto)
        {
            return fecha == null || !LaCalculaElMotor(linea, esPresupuesto)
                ? null
                : DePartidas(PartidasDe(fecha, linea.Producto));
        }

        /// <summary>Lo que hay que reservar en cada tienda para el producto: las partidas «Reposición tienda → Algete», por tienda.</summary>
        public static List<KeyValuePair<string, int>> ReservasEnTiendas(IEnumerable<PartidaFechaEntregaAgenciaDTO> partidas)
        {
            return (partidas ?? Enumerable.Empty<PartidaFechaEntregaAgenciaDTO>())
                .Where(p => p != null && p.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA && p.Unidades > 0 && !string.IsNullOrWhiteSpace(p.Tienda))
                .GroupBy(p => p.Tienda.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(p => p.Unidades)))
                .OrderBy(r => Constantes.Sedes.ListaSedes.IndexOf(r.Key))
                .ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
