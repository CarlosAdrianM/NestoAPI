using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// NestoAPI#764 (Carlos 30/10/26): en el picking, los pedidos que van por ruta interna y cuyo código postal de entrega tiene
    /// día de reparto (<see cref="DiasRepartoCodigosPostales.DiaRutaInterna"/>) se preparan EL MISMO DÍA en que se reparten:
    /// <list type="bullet">
    /// <item>Nunca entra una línea con fecha de mañana o después (p. ej. el picking de la tarde, cuyo horizonte es el laborable
    /// siguiente).</item>
    /// <item>En los picking de rutas, de cliente y de todo (el de las 9, «Solo nuestra ruta», el de las 11), solo salen el día
    /// que les toca: lo que se queda pendiente sin stock espera al siguiente día de su ruta. En el picking de UN pedido no se
    /// mira el día: almacén decide en última instancia.</item>
    /// <item>El día de entrega para los días de servir del cliente es el mismo día (<see cref="GestorDiasEnServir"/>).</item>
    /// </list>
    /// Los demás pedidos (agencia, o ruta interna sin día: la 16 de la zona de Algete), sin cambios.
    /// </summary>
    public static class GestorRutaInternaPicking
    {
        /// <summary>El día de reparto del pedido por nuestra ruta, o null si no tiene (y entonces nada cambia).</summary>
        public static DayOfWeek? DiaReparto(PedidoPicking pedido)
        {
            return pedido == null ? null : DiasRepartoCodigosPostales.DiaRutaInterna(pedido.Ruta, pedido.CodigoPostal);
        }

        /// <summary>
        /// Quita las líneas que no salen hoy de los pedidos con día de reparto. Devuelve los pedidos a los que hoy no les toca (para
        /// la traza); su <see cref="PedidoPicking.PrimeraEntregaFuturaQuitada"/> queda en el próximo día de su ruta.
        /// </summary>
        /// <param name="hoy">El día en que se saca el picking.</param>
        /// <param name="mirarDiaDeReparto">False en el picking de un solo pedido (lo pide almacén a propósito).</param>
        public static List<PedidoPicking> Aplicar(List<PedidoPicking> candidatos, DateTime hoy, Func<DateTime, bool> esFestivo, bool mirarDiaDeReparto)
        {
            var noLesToca = new List<PedidoPicking>();
            foreach (PedidoPicking pedido in candidatos ?? new List<PedidoPicking>())
            {
                DayOfWeek? dia = DiaReparto(pedido);
                if (!dia.HasValue || pedido.Lineas == null)
                {
                    continue;
                }
                List<LineaPedidoPicking> futuras = pedido.Lineas.Where(l => l.FechaEntrega.Date > hoy.Date).ToList();
                if (futuras.Count > 0)
                {
                    DateTime primera = futuras.Min(l => l.FechaEntrega);
                    pedido.PrimeraEntregaFuturaQuitada = pedido.PrimeraEntregaFuturaQuitada.HasValue && pedido.PrimeraEntregaFuturaQuitada < primera
                        ? pedido.PrimeraEntregaFuturaQuitada
                        : primera;
                    _ = pedido.Lineas.RemoveAll(l => l.FechaEntrega.Date > hoy.Date);
                }
                if (mirarDiaDeReparto && pedido.Lineas.Count > 0 && !CalendarioRutasDia.EsDiaDeReparto(dia.Value, hoy, esFestivo))
                {
                    pedido.PrimeraEntregaFuturaQuitada = CalendarioRutasDia.ProximoDiaReparto(dia.Value, hoy, esFestivo);
                    pedido.Lineas.Clear();
                    noLesToca.Add(pedido);
                }
            }
            return noLesToca;
        }
    }
}
