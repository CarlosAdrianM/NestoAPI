using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#501: hay familias que no se pueden vender a un cliente que haya comprado de otras.
    /// El primer caso es Kinetics, que no se vende a quien haya comprado Faby o Greenik en los
    /// últimos 24 meses, pero el mecanismo es genérico: las parejas y la ventana viven en la tabla
    /// <c>FamiliasIncompatibles</c>, así que ampliarlo no toca código.
    ///
    /// <para>Denegar no es la última palabra: quien tenga el parámetro
    /// <c>PermitirCrearPedidoConErroresValidacion</c> (Manuel y Carlos hoy) recibe el motivo,
    /// confirma el "¿desea crearlo de todos modos?" que ya existe en Nesto y en NestoApp, y el
    /// pedido se crea con <c>CreadoSinPasarValidacion</c>.</para>
    /// </summary>
    public class ValidadorFamiliasIncompatibles : IValidadorDenegacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = true
            };

            if (pedido?.Lineas == null || !pedido.Lineas.Any() || string.IsNullOrWhiteSpace(pedido.cliente))
            {
                return respuesta;
            }

            // La familia de cada producto una sola vez: el pedido puede traer varias líneas de la
            // misma familia y cada BuscarProducto es una consulta.
            Dictionary<string, string> familiaPorProducto = pedido.Lineas
                .Where(EsLineaDeProducto)
                .Select(l => l.Producto?.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p, p => servicio.BuscarProducto(p)?.Familia?.Trim(), StringComparer.OrdinalIgnoreCase);

            List<string> familiasDelPedido = familiaPorProducto.Values
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string familia in familiasDelPedido)
            {
                List<FamiliaIncompatibilidad> incompatibilidades = servicio.BuscarIncompatibilidadesFamilia(familia);
                if (incompatibilidades == null || !incompatibilidades.Any())
                {
                    continue;
                }

                foreach (FamiliaIncompatibilidad incompatibilidad in incompatibilidades)
                {
                    string familiaIncompatible = incompatibilidad.FamiliaIncompatible?.Trim();
                    DateTime? ultimaCompra = servicio.UltimaCompraDeFamilia(
                        pedido.cliente, familiaIncompatible, incompatibilidad.Meses);

                    if (ultimaCompra == null)
                    {
                        continue;
                    }

                    string motivo = MotivoDenegacion(familia, familiaIncompatible, ultimaCompra.Value);
                    List<string> productosDeLaFamilia = familiaPorProducto
                        .Where(p => string.Equals(p.Value, familia, StringComparison.OrdinalIgnoreCase))
                        .Select(p => p.Key)
                        .ToList();

                    respuesta.ValidacionSuperada = false;
                    respuesta.Motivo = motivo;
                    respuesta.ProductoId = productosDeLaFamilia.FirstOrDefault();
                    respuesta.Errores = productosDeLaFamilia.Select(p => new ErrorValidacion
                    {
                        Motivo = motivo,
                        ProductoId = p,
                        AutorizadaDenegadaExpresamente = false
                    }).ToList();
                    return respuesta;
                }
            }

            return respuesta;
        }

        /// <summary>
        /// Las líneas de producto con cantidad. Un tipoLinea nulo se trata como producto, igual que
        /// en <c>GestorSugerenciasOfertas</c>.
        /// </summary>
        private static bool EsLineaDeProducto(LineaPedidoVentaDTO linea)
        {
            return linea != null
                && (linea.tipoLinea == null || linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                && linea.Cantidad > 0;
        }

        internal static string MotivoDenegacion(string familia, string familiaIncompatible, DateTime ultimaCompra)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "No se puede vender {0} a este cliente porque compró {1} el {2:dd/MM/yyyy}",
                familia,
                familiaIncompatible,
                ultimaCompra);
        }

    }
}
