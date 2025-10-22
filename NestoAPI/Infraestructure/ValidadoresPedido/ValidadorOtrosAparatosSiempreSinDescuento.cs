using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorOtrosAparatosSiempreSinDescuento : IValidadorDenegacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = true
            };

            List<string> erroresLineas = new List<string>();

            foreach (LineaPedidoVentaDTO linea in pedido.Lineas.Where(l => l.tipoLinea == 1)) //1=Producto
            {
                Producto producto = servicio.BuscarProducto(linea.Producto);

                if (producto == null)
                {
                    erroresLineas.Add("No existe el producto " + linea.Producto + " en la línea " + linea.id.ToString());
                    continue; // Seguimos validando las demás líneas
                }

                PrecioDescuentoProducto datos = new PrecioDescuentoProducto
                {
                    precioCalculado = linea.PrecioUnitario,
                    descuentoCalculado = linea.DescuentoLinea,
                    producto = producto,
                    cantidad = (short)linea.Cantidad,
                    aplicarDescuento = linea.AplicarDescuento
                };

                bool esValidoLinea = new OtrosAparatosNoPuedeLlevarDescuento().precioAceptado(datos);

                // En lugar de return, acumulamos el error
                if (!esValidoLinea)
                {
                    erroresLineas.Add("El producto " + linea.Producto + " no puede llevar ningún descuento ni oferta porque es Otros Aparatos");
                }
            }

            // Consolidamos todos los errores
            if (erroresLineas.Any())
            {
                respuesta.ValidacionSuperada = false;
                respuesta.Motivos = erroresLineas;
                respuesta.AutorizadaDenegadaExpresamente = true;
            }
            else if (pedido.Lineas.Where(l => l.tipoLinea == 1).Count() == 0)
            {
                respuesta.Motivo = "El pedido " + pedido.numero + " no tiene ninguna línea de productos";
            }

            return respuesta;
        }
    }
}