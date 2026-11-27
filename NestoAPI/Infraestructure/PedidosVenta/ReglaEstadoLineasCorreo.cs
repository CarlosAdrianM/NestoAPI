using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Pedido 929421 (27/11/26): cómo está una línea en la BD tras guardar (lo copia el PUT para el correo).</summary>
    public sealed class SituacionGuardadaLinea
    {
        public short Estado { get; set; }
        public int? Albaran { get; set; }
        public DateTime? FechaAlbaran { get; set; }
        public string Factura { get; set; }
        public DateTime? FechaFactura { get; set; }
        public bool YaFacturado { get; set; }
    }

    /// <summary>En qué está una línea para el correo del pedido.</summary>
    public enum SituacionLineaCorreo
    {
        /// <summary>Por servir: pendiente, en curso, presupuesto o cualquier estado que no sea de servida (también el 0 de un DTO sin estado).</summary>
        Pendiente = 0,
        /// <summary>Entregada: en albarán (2) o en una nota de entrega ya procesada (−2).</summary>
        Entregada = 1,
        /// <summary>Facturada (4).</summary>
        Facturada = 2
    }

    /// <summary>
    /// Pedido 929421 (Carlos, 27/11/26): el correo «Modificación Pedido» de un pedido ya servido pintaba las líneas como las de un
    /// pedido cualquiera y llevaba el bloque de entrega (modo de entrega y de facturación, fecha de entrega y «⚠ Cambia la fecha de
    /// entrega a la agencia… Ahora: sin fecha») cuando ya no quedaba nada por entregar. LA REGLA (pura), en el generador común
    /// (<see cref="GestorPresupuestos"/>, el mismo del pedido nuevo):
    /// - Cada línea servida lleva su marca («Entregada: albarán … del …», «Facturada: factura … del …»). Las pendientes, sin marca
    ///   (como siempre), salvo en un pedido a medias, donde dicen «Pendiente de entregar» para que se vea lo que falta.
    /// - Sin nada por entregar, fuera el bloque de entrega y una frase arriba («Este pedido ya está entregado y facturado; el cambio no
    ///   afecta a ninguna entrega.»).
    /// - A medias, el bloque sigue y se refiere solo a lo pendiente (la fecha de entrega común y la columna F. Entrega salen de lo pendiente).
    /// Un pedido sin nada servido (el nuevo, el presupuesto, el de siempre) sale idéntico.
    /// </summary>
    public static class ReglaEstadoLineasCorreo
    {
        public const string PENDIENTE_DE_ENTREGAR = "Pendiente de entregar";

        private static short Estado(LineaPedidoVentaDTO linea) => linea.SituacionGuardada?.Estado ?? linea.estado;

        public static SituacionLineaCorreo Situacion(LineaPedidoVentaDTO linea)
        {
            if (linea == null)
            {
                return SituacionLineaCorreo.Pendiente;
            }
            short estado = Estado(linea);
            if (estado >= Constantes.EstadosLineaVenta.FACTURA)
            {
                return SituacionLineaCorreo.Facturada;
            }
            return estado >= Constantes.EstadosLineaVenta.ALBARAN || estado == Constantes.EstadosLineaVenta.NOTA_ENTREGA
                ? SituacionLineaCorreo.Entregada
                : SituacionLineaCorreo.Pendiente;
        }

        public static bool EstaServida(LineaPedidoVentaDTO linea) => Situacion(linea) != SituacionLineaCorreo.Pendiente;

        /// <summary>
        /// Las líneas que dicen si queda algo por entregar: las de producto con cantidad (los portes o una cuenta contable no son
        /// mercancía); si el pedido no tiene ninguna de producto, todas las que tienen cantidad.
        /// </summary>
        public static List<LineaPedidoVentaDTO> LineasQueCuentan(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            List<LineaPedidoVentaDTO> conCantidad = (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && l.Cantidad != 0).ToList();
            List<LineaPedidoVentaDTO> deProducto = conCantidad.Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO).ToList();
            return deProducto.Any() ? deProducto : conCantidad;
        }

        /// <summary>Si queda algo por entregar. Sin líneas que cuenten, true: el correo sale como siempre.</summary>
        public static bool QuedaAlgoPorEntregar(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            List<LineaPedidoVentaDTO> cuentan = LineasQueCuentan(lineas);
            return !cuentan.Any() || cuentan.Any(l => !EstaServida(l));
        }

        /// <summary>Si alguna línea del pedido (de cualquier tipo) ya está servida. Si no, el correo es el de siempre.</summary>
        public static bool HayAlgoServido(IEnumerable<LineaPedidoVentaDTO> lineas)
            => (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>()).Any(l => l != null && EstaServida(l));

        /// <summary>Pedido a medias: queda algo por entregar y algo ya está servido.</summary>
        public static bool EsPedidoAMedias(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            List<LineaPedidoVentaDTO> lista = (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>()).ToList();
            return HayAlgoServido(lista) && QuedaAlgoPorEntregar(lista);
        }

        /// <summary>
        /// Las líneas con las que se calcula la fecha de entrega del pie y la columna F. Entrega: con algo servido, solo lo que queda
        /// por servir (la fecha de lo ya entregado no es una entrega). Sin nada servido, todas (como siempre).
        /// </summary>
        public static List<LineaPedidoVentaDTO> LineasParaFechas(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            List<LineaPedidoVentaDTO> lista = (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>()).Where(l => l != null).ToList();
            return HayAlgoServido(lista) ? lista.Where(l => !EstaServida(l)).ToList() : lista;
        }

        /// <summary>
        /// La marca corta de la línea: «Entregada: albarán 732939 del 28/10/2026», «Entregada (nota de entrega)», «Facturada: factura
        /// NV26/012345 del 28/10/2026». Pendiente: null, salvo en un pedido a medias («Pendiente de entregar», «… (ya facturada)»).
        /// </summary>
        public static string Marca(LineaPedidoVentaDTO linea, bool pedidoAMedias)
        {
            if (linea == null)
            {
                return null;
            }
            SituacionGuardadaLinea guardada = linea.SituacionGuardada;
            switch (Situacion(linea))
            {
                case SituacionLineaCorreo.Facturada:
                    {
                        string factura = (guardada != null ? guardada.Factura : linea.Factura)?.Trim();
                        DateTime? fecha = guardada?.FechaFactura;
                        return "Facturada" + (string.IsNullOrEmpty(factura) ? string.Empty : ": factura " + factura) + Del(fecha);
                    }
                case SituacionLineaCorreo.Entregada:
                    {
                        int? albaran = guardada != null ? guardada.Albaran : linea.Albaran;
                        DateTime? fecha = guardada?.FechaAlbaran;
                        if (albaran.HasValue && albaran.Value != 0)
                        {
                            return $"Entregada: albarán {albaran.Value}" + Del(fecha);
                        }
                        return Estado(linea) == Constantes.EstadosLineaVenta.NOTA_ENTREGA ? "Entregada (nota de entrega)" : "Entregada";
                    }
                default:
                    if (!pedidoAMedias)
                    {
                        return null;
                    }
                    bool yaFacturada = guardada != null ? guardada.YaFacturado : linea.yaFacturado;
                    return yaFacturada ? PENDIENTE_DE_ENTREGAR + " (ya facturada)" : PENDIENTE_DE_ENTREGAR;
            }
        }

        private static string Del(DateTime? fecha) => fecha.HasValue ? " del " + fecha.Value.ToString("dd/MM/yyyy") : string.Empty;

        /// <summary>
        /// La frase de arriba cuando no queda nada por entregar: «Este pedido ya está entregado y facturado; el cambio no afecta a
        /// ninguna entrega.» (o «ya está entregado», o «entregado y facturado en parte», según lo que haya). Null si queda algo.
        /// </summary>
        public static string FraseTodoServido(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            List<LineaPedidoVentaDTO> cuentan = LineasQueCuentan(lineas);
            if (!cuentan.Any() || cuentan.Any(l => !EstaServida(l)))
            {
                return null;
            }
            int facturadas = cuentan.Count(l => Situacion(l) == SituacionLineaCorreo.Facturada);
            string que = facturadas == cuentan.Count ? "entregado y facturado"
                : facturadas == 0 ? "entregado"
                : "entregado y facturado en parte";
            return $"Este pedido ya está {que}; el cambio no afecta a ninguna entrega.";
        }

        /// <summary>
        /// Pedido a medias: lo que se dice antes del bloque de entrega para que quede claro a qué se refiere. Null si no es a medias.
        /// </summary>
        public static string FraseLoQueFalta(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            List<LineaPedidoVentaDTO> lista = (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>()).ToList();
            if (!EsPedidoAMedias(lista))
            {
                return null;
            }
            int pendientes = LineasQueCuentan(lista).Count(l => !EstaServida(l));
            string lineasTexto = pendientes == 1 ? "1 línea marcada" : $"{pendientes} líneas marcadas";
            return $"Parte del pedido ya está entregada. Lo que sigue (modo de entrega y fechas) es solo para lo que queda por entregar: " +
                $"{lineasTexto} «{PENDIENTE_DE_ENTREGAR}».";
        }

        /// <summary>El PUT: copia a cada línea del correo (por Nº de orden) cómo ha quedado en la BD. Las nuevas (id 0) no se tocan.</summary>
        public static void CopiarSituacionGuardada(IEnumerable<LineaPedidoVentaDTO> lineasCorreo, IEnumerable<LinPedidoVta> lineasGuardadas)
        {
            Dictionary<int, LinPedidoVta> porOrden = (lineasGuardadas ?? Enumerable.Empty<LinPedidoVta>())
                .Where(l => l != null)
                .GroupBy(l => l.Nº_Orden)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (LineaPedidoVentaDTO linea in (lineasCorreo ?? Enumerable.Empty<LineaPedidoVentaDTO>()).Where(l => l != null && l.id != 0))
            {
                if (porOrden.TryGetValue(linea.id, out LinPedidoVta guardada))
                {
                    linea.SituacionGuardada = new SituacionGuardadaLinea
                    {
                        Estado = guardada.Estado,
                        Albaran = guardada.Nº_Albarán,
                        FechaAlbaran = guardada.Fecha_Albarán,
                        Factura = guardada.Nº_Factura?.Trim(),
                        FechaFactura = guardada.Fecha_Factura,
                        YaFacturado = guardada.YaFacturado
                    };
                }
            }
        }
    }
}
