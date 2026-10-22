using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#693: lo que el motor de #606 mira de una línea de pedido a proveedor.</summary>
    public class LineaCompraPromesaRiesgo
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        /// <summary>LinPedidoCmp.Estado: solo cuentan las pendientes (-1 y 1).</summary>
        public short Estado { get; set; }
        public bool Enviado { get; set; }
        public int Cantidad { get; set; }
        public DateTime? FechaRecepcion { get; set; }

        /// <summary>Lo que el motor cuenta como «llega del proveedor» (SQL_COMPRAS: Estado -1 o 1 y Enviado).</summary>
        public bool CuentaParaElMotor => Enviado && (Estado == -1 || Estado == 1);

        public static LineaCompraPromesaRiesgo De(LinPedidoCmp linea) => new LineaCompraPromesaRiesgo
        {
            Id = linea.NºOrden,
            Producto = linea.Producto?.Trim(),
            Estado = linea.Estado,
            Enviado = linea.Enviado,
            Cantidad = linea.Cantidad ?? 0,
            FechaRecepcion = linea.FechaRecepción
        };
    }

    /// <summary>
    /// NestoAPI#693 (Carlos, 22/10): un pedido a proveedor que cambia desde la API puede retrasar pedidos de venta que contaban
    /// con él. Pura: compara las líneas antes y después y devuelve, por producto, por qué empeora: se retrasa la fecha de
    /// recepción, baja la cantidad pendiente o la línea desaparece (borrada o ya no pendiente/enviada). Lo que mejora no se
    /// anota (ya lo recoge el siguiente evento o la pasada nocturna).
    /// </summary>
    public static class CambiosCompraPromesaRiesgo
    {
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        public static Dictionary<string, CausaPromesaRiesgo> QueEmpeora(int pedidoCompra, IEnumerable<LineaCompraPromesaRiesgo> antes,
            IEnumerable<LineaCompraPromesaRiesgo> despues)
        {
            var causas = new Dictionary<string, CausaPromesaRiesgo>(StringComparer.OrdinalIgnoreCase);
            List<LineaCompraPromesaRiesgo> ahora = (despues ?? Enumerable.Empty<LineaCompraPromesaRiesgo>()).Where(l => l != null).ToList();
            foreach (LineaCompraPromesaRiesgo vieja in (antes ?? Enumerable.Empty<LineaCompraPromesaRiesgo>())
                .Where(l => l != null && l.CuentaParaElMotor && !string.IsNullOrWhiteSpace(l.Producto)))
            {
                LineaCompraPromesaRiesgo nueva = ahora.FirstOrDefault(l => l.Id == vieja.Id);
                string producto = vieja.Producto.Trim();
                string texto = null;
                if (nueva == null || !nueva.CuentaParaElMotor
                    || !string.Equals(nueva.Producto?.Trim(), producto, StringComparison.OrdinalIgnoreCase))
                {
                    texto = $"se ha quitado del pedido a proveedor {pedidoCompra}";
                }
                else if (nueva.Cantidad < vieja.Cantidad)
                {
                    texto = $"el pedido a proveedor {pedidoCompra} baja de {vieja.Cantidad} a {nueva.Cantidad}";
                }
                else if (Retrasa(vieja.FechaRecepcion, nueva.FechaRecepcion))
                {
                    texto = nueva.FechaRecepcion.HasValue
                        ? $"el pedido a proveedor {pedidoCompra} retrasa la fecha prevista del {Dia(vieja.FechaRecepcion)} al {Dia(nueva.FechaRecepcion)}"
                        : $"el pedido a proveedor {pedidoCompra} se queda sin fecha prevista (antes, el {Dia(vieja.FechaRecepcion)})";
                }
                if (texto != null && !causas.ContainsKey(producto))
                {
                    causas[producto] = new CausaPromesaRiesgo { Texto = texto };
                }
            }
            return causas;
        }

        private static bool Retrasa(DateTime? antes, DateTime? despues) =>
            antes.HasValue && (!despues.HasValue || despues.Value.Date > antes.Value.Date);

        private static string Dia(DateTime? fecha) => fecha?.ToString("dd/MM/yyyy", castellano);
    }
}
