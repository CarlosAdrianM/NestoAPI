using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// Valida que los productos de regalo (bonificación) no excedan
    /// el porcentaje máximo permitido del importe del pedido.
    /// </summary>
    public class ValidadorLimiteRegalos : IValidadorDenegacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = true
            };

            if (pedido?.Lineas == null || !pedido.Lineas.Any())
            {
                return respuesta;
            }

            // Identificar productos de regalo (bonificación)
            var lineasRegalo = new List<LineaPedidoVentaDTO>();
            var lineasNormales = new List<LineaPedidoVentaDTO>();

            foreach (var linea in pedido.Lineas.Where(l => l.tipoLinea == 1)) // 1 = Producto
            {
                if (string.IsNullOrEmpty(linea.Producto))
                {
                    continue;
                }

                Producto producto = servicio.BuscarProducto(linea.Producto);
                if (producto == null)
                {
                    continue;
                }

                bool esProductoRegalo = producto.Ficticio
                    && producto.Familia != null
                    && producto.Familia.Trim() == Constantes.Productos.FAMILIA_BONIFICACION;

                if (esProductoRegalo)
                {
                    lineasRegalo.Add(linea);
                }
                else
                {
                    lineasNormales.Add(linea);
                }
            }

            // Si no hay regalos, no hay nada que validar
            if (!lineasRegalo.Any())
            {
                return respuesta;
            }

            // Calcular importes
            // Para los regalos usamos el precio de tarifa (valor real del producto)
            decimal importeRegalos = lineasRegalo.Sum(l => l.precioTarifa * l.Cantidad);
            decimal importePedido = lineasNormales.Sum(l => l.BaseImponible);

            // Si el pedido no tiene productos normales, no se permiten regalos
            if (importePedido <= 0)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "No se pueden añadir productos de regalo a un pedido sin productos",
                    Errores = lineasRegalo.Select(l => new ErrorValidacion
                    {
                        Motivo = $"El producto {l.Producto?.Trim()} es un regalo pero el pedido no tiene productos",
                        ProductoId = l.Producto?.Trim(),
                        AutorizadaDenegadaExpresamente = false
                    }).ToList()
                };
            }

            // Calcular límite permitido
            decimal limiteRegalos = importePedido * Constantes.Productos.PORCENTAJE_MAXIMO_REGALOS;

            if (importeRegalos > limiteRegalos)
            {
                decimal porcentajeActual = importeRegalos / importePedido;
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = $"Los productos de regalo ({importeRegalos:C}) exceden el límite permitido del {Constantes.Productos.PORCENTAJE_MAXIMO_REGALOS:P0} ({limiteRegalos:C}) sobre el importe del pedido ({importePedido:C})",
                    Errores = lineasRegalo.Select(l => new ErrorValidacion
                    {
                        Motivo = $"El producto {l.Producto?.Trim()} forma parte de los regalos que exceden el {Constantes.Productos.PORCENTAJE_MAXIMO_REGALOS:P0} del pedido (actual: {porcentajeActual:P1})",
                        ProductoId = l.Producto?.Trim(),
                        AutorizadaDenegadaExpresamente = false
                    }).ToList()
                };
            }

            return respuesta;
        }
    }
}
