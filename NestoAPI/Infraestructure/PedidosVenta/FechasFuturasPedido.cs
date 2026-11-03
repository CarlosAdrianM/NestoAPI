using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Nesto#588 (NestoAPI#764): la regla de «tiene fechas futuras» de un pedido, la de la lista de pedidos y la del pedido completo
    /// (GET, POST y PUT de api/PedidosVenta), en un solo sitio. Un pedido tiene fechas futuras si alguna línea pendiente o en curso
    /// (-1 a 1) se entrega DESPUÉS de la fecha de entrega mínima de hoy para su ruta; en las rutas internas con día de reparto
    /// (AT/16 + CodigosPostalesDiaReparto, corte a las 9:00), el próximo día de su ruta.
    /// </summary>
    public static class FechasFuturasPedido
    {
        /// <summary>El código postal solo cuenta si el pedido va por ruta interna y ese código postal tiene día de reparto.</summary>
        internal static string CodigoPostalQueCuenta(string ruta, string codigoPostal)
        {
            return DiasRepartoCodigosPostales.DiaRutaInterna(ruta?.Trim(), codigoPostal).HasValue ? codigoPostal?.Trim() : null;
        }

        /// <summary>
        /// Lo que hace distinta la fecha mínima de un pedido a otro (la ruta y, en ruta interna, el código postal; NestoAPI#799: o
        /// el día de la excepción del contacto, si se conoce y la tiene).
        /// </summary>
        internal static string Clave(string ruta, string codigoPostal, string empresa = null, string cliente = null, string contacto = null)
        {
            string rutaLimpia = ruta?.Trim() ?? string.Empty;
            DayOfWeek? diaCliente = RutasPropiasConfiguradas.Contiene(rutaLimpia) ? DiasRepartoClientes.DiaDe(empresa, cliente, contacto) : null;
            if (diaCliente.HasValue)
            {
                return $"{rutaLimpia}|@{(int)diaCliente.Value}";
            }
            string cp = CodigoPostalQueCuenta(rutaLimpia, codigoPostal);
            return cp == null ? rutaLimpia : $"{rutaLimpia}|{cp}";
        }

        /// <summary>
        /// La fecha de entrega mínima de hoy para la ruta (y el código postal de entrega; NestoAPI#799: o la excepción del contacto,
        /// que manda): lo que llegue después es futuro.
        /// </summary>
        internal static DateTime FechaEntregaFutura(string ruta, string codigoPostal, string empresa = null, string cliente = null, string contacto = null)
        {
            string rutaLimpia = ruta?.Trim() ?? string.Empty;
            return GestorPedidosVenta.FechaEntregaAjustadaConCodigoPostal(DateTime.Now, rutaLimpia, string.Empty,
                CodigoPostalQueCuenta(rutaLimpia, codigoPostal), empresa, cliente, contacto);
        }

        internal static bool EstaPendiente(short estado)
        {
            return estado >= Constantes.EstadosLineaVenta.PENDIENTE && estado <= Constantes.EstadosLineaVenta.EN_CURSO;
        }

        /// <summary>La última fecha de entrega de las líneas pendientes o en curso; null si no hay ninguna.</summary>
        internal static DateTime? UltimaEntregaPendiente(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            return lineas?.Where(l => l != null && EstaPendiente(l.estado)).Max(l => (DateTime?)l.fechaEntrega);
        }

        /// <summary>LA REGLA: la última entrega pendiente es posterior a la fecha mínima.</summary>
        internal static bool TieneFechasFuturas(DateTime? ultimaEntregaPendiente, DateTime fechaEntregaFutura)
        {
            return ultimaEntregaPendiente.HasValue && ultimaEntregaPendiente.Value > fechaEntregaFutura;
        }

        /// <summary>Con las líneas del pedido (sin líneas pendientes, false; entonces ni se calcula la fecha mínima).</summary>
        internal static bool TieneFechasFuturas(IEnumerable<LineaPedidoVentaDTO> lineas, string ruta, string codigoPostal,
            string empresa = null, string cliente = null, string contacto = null)
        {
            DateTime? ultima = UltimaEntregaPendiente(lineas);
            return ultima.HasValue && TieneFechasFuturas(ultima, FechaEntregaFutura(ruta, codigoPostal, empresa, cliente, contacto));
        }
    }
}
