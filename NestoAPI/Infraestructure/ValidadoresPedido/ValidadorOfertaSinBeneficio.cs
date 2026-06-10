using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// Nesto#375 (backstop servidor): rechaza ofertas "falsas" en las que todas las unidades de un
    /// mismo producto, marcadas con el mismo nº de oferta, van al MISMO precio (la oferta no aplica
    /// ningún descuento). Caso típico: un 6+6 con los 12 a precio completo creado al "personalizar
    /// oferta" en PlantillaVenta. Si todas las unidades van al mismo precio, deben meterse sin oferta.
    ///
    /// Es un rechazo DURO (AutorizadaDenegadaExpresamente): ningún validador de aceptación lo anula.
    ///
    /// SALVAGUARDAS (ver auditoría de escritores del campo oferta, Nesto#375):
    ///  - Solo ofertas de UN producto (varios productos = oferta combinada, fuera de alcance).
    ///  - Solo grupos con &gt;= 2 líneas: una línea suelta con oferta (p. ej. un precio especial de
    ///    DescuentosProducto que coja nº de oferta) NUNCA se marca.
    ///  - Solo si el precio común es &gt; 0: una oferta con regalo real (alguna unidad a 0) tiene
    ///    precios distintos y no se marca.
    /// </summary>
    public class ValidadorOfertaSinBeneficio : IValidadorDenegacion
    {
        private const decimal EPSILON = 0.01m;

        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = true
            };

            if (pedido?.Lineas == null)
            {
                return respuesta;
            }

            var gruposOferta = pedido.Lineas
                .Where(l => l.tipoLinea == 1 && l.oferta.HasValue && l.oferta.Value != 0
                            && l.Producto != null && l.Cantidad > 0)
                .GroupBy(l => l.oferta.Value);

            foreach (var grupo in gruposOferta)
            {
                List<LineaPedidoVentaDTO> lineas = grupo.ToList();

                // Solo ofertas de un único producto y con al menos dos líneas (pago + oferta).
                bool unSoloProducto = lineas.Select(l => l.Producto.Trim()).Distinct().Count() == 1;
                if (!unSoloProducto || lineas.Count < 2)
                {
                    continue;
                }

                // Precio neto por unidad de cada línea (la base imponible ya incluye los descuentos).
                List<decimal> preciosNetos = lineas.Select(l => l.BaseImponible / l.Cantidad).ToList();
                decimal maximo = preciosNetos.Max();
                decimal minimo = preciosNetos.Min();

                // Todas las unidades al mismo precio (sin variación) y ese precio es > 0 => sin beneficio.
                if (maximo > EPSILON && (maximo - minimo) < EPSILON)
                {
                    respuesta.ValidacionSuperada = false;
                    respuesta.AutorizadaDenegadaExpresamente = true;
                    respuesta.ProductoId = lineas.First().Producto.Trim();
                    respuesta.Motivo = "La oferta del producto " + respuesta.ProductoId
                        + " no aplica ningún descuento (todas las unidades van al mismo precio). "
                        + "Si quieres todas las unidades a ese precio, mételas sin oferta.";
                    return respuesta;
                }
            }

            return respuesta;
        }
    }
}
