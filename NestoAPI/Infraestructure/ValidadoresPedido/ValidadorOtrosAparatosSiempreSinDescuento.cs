using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorOtrosAparatosSiempreSinDescuento : IValidadorDenegacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion {
                ValidacionSuperada = true,
                Motivo = "El pedido " + pedido.numero + " no tiene ninguna línea de productos"
            };
            bool esValidoDeMomento = true;
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas.Where(l => l.tipoLinea == 1)) //1=Producto
            {
                Producto producto = servicio.BuscarProducto(linea.Producto);

                if (producto == null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        AutorizadaDenegadaExpresamente = false,
                        Motivo = "No existe el producto " + linea.Producto + " en la línea " + linea.id.ToString()
                    };
                }

                PrecioDescuentoProducto datos = new PrecioDescuentoProducto
                {
                    precioCalculado = linea.PrecioUnitario,
                    descuentoCalculado = linea.DescuentoLinea,
                    producto = producto,
                    cantidad = (short)linea.Cantidad,
                    aplicarDescuento = linea.AplicarDescuento
                };

                esValidoDeMomento = new OtrosAparatosNoPuedeLlevarDescuento().precioAceptado(datos);
                
                // Una vez que una línea no es válida, todo el pedido deja de ser válido
                if (!esValidoDeMomento) 
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        Motivo = "El producto " + linea.Producto + " no puede llevar ningún descuento ni oferta porque es Otros Aparatos" ,
                        ProductoId = linea.Producto,
                        AutorizadaDenegadaExpresamente = true
                    };
                }
            }
            

            return respuesta;
        }
    }
}