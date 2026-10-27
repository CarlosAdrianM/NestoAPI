using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#712: aviso (no bloqueo) al grabar un pedido con alguna línea cuya fecha de entrega está a más de
    /// <see cref="Constantes.Pedidos.DIAS_AVISO_FECHA_ENTREGA_LEJANA"/> días. Caso real: el pedido 929057 (21/10/26) se grabó
    /// con todas las líneas para el 27/10/2027 (se quería 2026) y no entró en ningún picking hasta que alguien lo vio.
    ///
    /// <para>Mismo patrón que <see cref="Clientes.CambioDiasEnServirConPicking"/> (NestoAPI#541): la API rechaza el POST/PUT
    /// con <see cref="FechaEntregaLejanaException"/> (código FECHA_ENTREGA_LEJANA, 400) y el mensaje ya trae la pregunta;
    /// el cliente pregunta y, si el usuario dice que sí, repite la llamada con <c>ConfirmarFechaEntregaLejana = true</c>.</para>
    ///
    /// <para>Compatibilidad: el flag es <c>bool?</c>. Solo se avisa si el cliente manda <c>false</c> (sabe preguntar).
    /// Un cliente que no lo manda (null: NestoApp hasta su issue gemela, TiendasNuevaVision, llamadas internas como
    /// UnirPedidos) graba como siempre: si se le avisara, recibiría un 400 que no sabe resolver y no podría grabar.</para>
    ///
    /// <para>En una modificación solo cuentan las líneas nuevas y las que cambian de fecha: si ya estaba así y no se
    /// toca, no se vuelve a preguntar. No cuentan las líneas en albarán o factura, la fecha sin informar ni los
    /// cursos (EventoId: van a la fecha del evento, que puede estar lejos).</para>
    /// </summary>
    public static class AvisoFechaEntregaLejana
    {
        public const string CODIGO = "FECHA_ENTREGA_LEJANA";

        private static readonly CultureInfo ES = new CultureInfo("es-ES");

        /// <summary>
        /// El núcleo, puro. Lanza <see cref="FechaEntregaLejanaException"/> si hay que preguntar: el pedido trae el flag a
        /// false y alguna línea (nueva o con la fecha cambiada respecto a <paramref name="lineasAntes"/>) va a más de 60 días.
        /// </summary>
        /// <param name="lineasAntes">Las líneas grabadas (PUT) o null (POST).</param>
        public static void Comprobar(PedidoVentaDTO pedido, IEnumerable<LinPedidoVta> lineasAntes, DateTime hoy)
        {
            if (pedido?.ConfirmarFechaEntregaLejana != false)
            {
                return;
            }
            List<LineaPedidoVentaDTO> lejanas = LineasLejanas(pedido.Lineas, lineasAntes, hoy);
            if (!lejanas.Any())
            {
                return;
            }
            DateTime fecha = lejanas.Max(l => l.fechaEntrega).Date;
            int dias = (fecha - hoy.Date).Days;
            throw new FechaEntregaLejanaException(Mensaje(pedido.Lineas, lejanas, hoy), pedido.empresa, pedido.numero, fecha, dias, lejanas.Count);
        }

        public static bool EsLejana(DateTime fechaEntrega, DateTime hoy)
        {
            return (fechaEntrega.Date - hoy.Date).Days > Constantes.Pedidos.DIAS_AVISO_FECHA_ENTREGA_LEJANA;
        }

        /// <summary>Las líneas por las que hay que preguntar.</summary>
        internal static List<LineaPedidoVentaDTO> LineasLejanas(IEnumerable<LineaPedidoVentaDTO> lineas, IEnumerable<LinPedidoVta> lineasAntes, DateTime hoy)
        {
            Dictionary<int, DateTime> fechasAntes = (lineasAntes ?? Enumerable.Empty<LinPedidoVta>())
                .GroupBy(l => l.Nº_Orden)
                .ToDictionary(g => g.Key, g => g.First().Fecha_Entrega.Date);
            return (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => CuentaLaLinea(l) && EsLejana(l.fechaEntrega, hoy))
                .Where(l => l.id == 0 || !fechasAntes.TryGetValue(l.id, out DateTime antes) || antes != l.fechaEntrega.Date)
                .ToList();
        }

        /// <summary>
        /// «La fecha de entrega de las líneas es el miércoles 27/10/2027 (dentro de 365 días). ¿Es correcta?»
        /// Con una sola línea lejana entre varias, dice el producto; con varias fechas lejanas distintas, la más lejana.
        /// </summary>
        internal static string Mensaje(IEnumerable<LineaPedidoVentaDTO> todas, List<LineaPedidoVentaDTO> lejanas, DateTime hoy)
        {
            DateTime fecha = lejanas.Max(l => l.fechaEntrega).Date;
            int dias = (fecha - hoy.Date).Days;
            string cuando = $"el {ES.DateTimeFormat.GetDayName(fecha.DayOfWeek)} {fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} " +
                $"(dentro de {dias} {(dias == 1 ? "día" : "días")}). ¿Es correcta?";
            int cuantasCuentan = (todas ?? Enumerable.Empty<LineaPedidoVentaDTO>()).Count(CuentaLaLinea);
            bool mismaFecha = lejanas.Select(l => l.fechaEntrega.Date).Distinct().Count() == 1;

            if (lejanas.Count == 1)
            {
                string producto = lejanas[0].Producto?.Trim();
                return cuantasCuentan > 1 && !string.IsNullOrWhiteSpace(producto)
                    ? $"La fecha de entrega del producto {producto} es {cuando}"
                    : $"La fecha de entrega de la línea es {cuando}";
            }
            if (!mismaFecha)
            {
                return $"La fecha de entrega de {lejanas.Count} líneas llega hasta {cuando}";
            }
            return lejanas.Count == cuantasCuentan
                ? $"La fecha de entrega de las líneas es {cuando}"
                : $"La fecha de entrega de {lejanas.Count} líneas es {cuando}";
        }

        private static bool CuentaLaLinea(LineaPedidoVentaDTO linea)
        {
            return linea != null
                && linea.estado < Constantes.EstadosLineaVenta.ALBARAN
                && linea.fechaEntrega != default
                && linea.EventoId == null;
        }
    }

    /// <summary>
    /// NestoAPI#712. JSON (GlobalExceptionFilter), 400: error.code = "FECHA_ENTREGA_LEJANA", error.message con la pregunta
    /// y en details empresa, pedido (0 al crear), fechaEntrega (yyyy-MM-dd, la más lejana), dias y lineas (cuántas).
    /// Sí → repetir el POST/PUT con ConfirmarFechaEntregaLejana = true; No → volver a editar.
    /// </summary>
    public class FechaEntregaLejanaException : NestoBusinessException
    {
        public FechaEntregaLejanaException(string mensaje, string empresa, int pedido, DateTime fechaEntrega, int dias, int lineas)
            : base(mensaje, new ErrorContext { ErrorCode = AvisoFechaEntregaLejana.CODIGO, Empresa = empresa?.Trim(), Pedido = pedido })
        {
            Context.AdditionalData["fechaEntrega"] = fechaEntrega.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Context.AdditionalData["dias"] = dias;
            Context.AdditionalData["lineas"] = lineas;
        }
    }
}
