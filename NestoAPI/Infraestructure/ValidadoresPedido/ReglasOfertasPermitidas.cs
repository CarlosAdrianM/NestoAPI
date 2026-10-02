using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#564 (Carlos, 29/09/26): cómo se combinan las reglas de OfertasPermitidas de un producto.
    /// <list type="bullet">
    /// <item>Por defecto ninguna oferta se puede hacer: hace falta una regla de AUTORIZACIÓN (Denegar = false).</item>
    /// <item>Una regla de DENEGACIÓN N+M quita, para los productos a los que aplica, la autorización N+M: se rechaza
    /// el N+M y sus múltiplos. Otra oferta autorizada (p. ej. 10+1) sigue valiendo salvo que también se deniegue.</item>
    /// <item>El subgrupo ya lo filtra <see cref="ServicioPrecios.BuscarOfertasPermitidas"/> (único punto de lectura).</item>
    /// </list>
    /// Caso: Genéricos 6+1 (autorización) y Genéricos + DES 6+1 (denegación): el 6+1 vale en toda la familia menos en
    /// los desechables. Antes la denegación no se miraba y hasta autorizaba (fila 720, producto 44731).
    /// </summary>
    public static class ReglasOfertasPermitidas
    {
        /// <summary>La regla aplica a este producto por su filtro de nombre (sin filtro, aplica).</summary>
        public static bool CumpleFiltro(OfertaPermitida regla, Producto producto)
            => regla != null && (string.IsNullOrWhiteSpace(regla.FiltroProducto)
                || (producto?.Nombre != null && producto.Nombre.StartsWith(regla.FiltroProducto.Trim(), StringComparison.OrdinalIgnoreCase)));

        /// <summary>Mismo N+M (6+1 y 12+2 son la misma proporción).</summary>
        public static bool MismaProporcion(OfertaPermitida a, OfertaPermitida b)
            => a != null && b != null && a.CantidadRegalo > 0 && b.CantidadRegalo > 0
               && a.CantidadConPrecio * b.CantidadRegalo == b.CantidadConPrecio * a.CantidadRegalo;

        /// <summary>La oferta del pedido (cantidad cobrada + cantidad regalada) es el N+M de la regla o un múltiplo.</summary>
        public static bool EsMultiplo(OfertaPermitida regla, int cantidadCobrada, int cantidadRegalada)
            => regla != null && regla.CantidadConPrecio > 0 && regla.CantidadRegalo > 0 && cantidadRegalada > 0
               && cantidadCobrada * regla.CantidadRegalo == cantidadRegalada * regla.CantidadConPrecio
               && cantidadRegalada % regla.CantidadRegalo == 0;

        /// <summary>Las denegaciones que aplican al producto (filtro de nombre; cliente y subgrupo ya vienen filtrados).</summary>
        public static List<OfertaPermitida> Denegaciones(IEnumerable<OfertaPermitida> reglas, Producto producto)
            => (reglas ?? Enumerable.Empty<OfertaPermitida>()).Where(r => r.Denegar && CumpleFiltro(r, producto)).ToList();

        /// <summary>
        /// Las autorizaciones que siguen en pie: las no denegadas, quitando las que tienen el mismo N+M que alguna
        /// denegación que aplica al producto.
        /// </summary>
        public static List<OfertaPermitida> AutorizacionesEfectivas(IEnumerable<OfertaPermitida> reglas, Producto producto)
        {
            List<OfertaPermitida> lista = (reglas ?? Enumerable.Empty<OfertaPermitida>()).ToList();
            List<OfertaPermitida> denegaciones = Denegaciones(lista, producto);
            return lista
                .Where(r => !r.Denegar)
                .Where(r => !denegaciones.Any(d => MismaProporcion(d, r)))
                .ToList();
        }

        /// <summary>La denegación que prohíbe esta oferta (N+M o múltiplo), o null.</summary>
        public static OfertaPermitida DenegacionQueLaProhibe(IEnumerable<OfertaPermitida> reglas, Producto producto, int cantidadCobrada, int cantidadRegalada)
            => Denegaciones(reglas, producto).FirstOrDefault(d => EsMultiplo(d, cantidadCobrada, cantidadRegalada));

        /// <summary>
        /// Un N+M (unidades regaladas del mismo producto) exige cobrar las demás a precio de tarifa y sin descuento: o
        /// precio especial o descuento, o la oferta, nunca las dos. Devuelve el motivo si no se cumple, o null. Lo usan
        /// la validación y la sugerencia (NestoAPI#589, pedido 927595: no se sugería un 6+1 que no se aceptaría).
        /// </summary>
        public static string MotivoCobradoFueraDeTarifa(PrecioDescuentoProducto oferta, Producto producto)
        {
            if (oferta == null || producto == null || oferta.cantidadOferta == 0)
            {
                return null;
            }
            if (oferta.cantidad > 0 && oferta.precioCalculado < producto.PVP)
            {
                return "Oferta a precio inferior al de ficha en el producto " + producto.Número.Trim();
            }
            if (oferta.descuentoCalculado > 0)
            {
                return "Oferta no puede llevar descuento en el producto " + producto.Número.Trim();
            }
            return null;
        }
    }
}
