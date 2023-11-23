using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    
    public class ValidadorMuestrasYMaterialPromocional : IValidadorAceptacion
    {
        private const decimal PORCENTAJE_MAXIMO_MUESTRAS = 0.05M;
        private const int UNIDADES_MAXIMO_MUESTRAS = 10;

        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            // TODO: REFACTORIZAR PARA QUE SOLO SE EJECUTE UNA VEZ POR PEDIDO
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                ProductoId = numeroProducto,
                Motivo = "El producto " + numeroProducto
                        + " no puede ir a ese precio porque no es material promocional o se supera el importe autorizado"
            };

            Producto producto = GestorPrecios.servicio.BuscarProducto(numeroProducto);
            if (producto.SubGrupo == Constantes.Productos.SUBGRUPO_MUESTRAS && producto.Grupo == Constantes.Productos.GRUPO_COSMETICA)
            {
                decimal baseImponiblePedido = pedido.Lineas
                    .Where(l => l.GrupoProducto != Constantes.Productos.GRUPO_APARATOS)
                    .Sum(l => l.BaseImponible);
                int maximoUnidades = pedido.Lineas.Where(l => l.Producto == numeroProducto).Sum(l => l.Cantidad);

                var importeMuestras = GestorPrecios.servicio.CalcularImporteGrupo(pedido, Constantes.Productos.GRUPO_COSMETICA, Constantes.Productos.SUBGRUPO_MUESTRAS);

                if (importeMuestras <= baseImponiblePedido * PORCENTAJE_MAXIMO_MUESTRAS && maximoUnidades <= UNIDADES_MAXIMO_MUESTRAS)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = true,
                        ProductoId = numeroProducto,
                        Motivo = "El producto " + numeroProducto
                        + " puede ir a ese precio porque es material promocional y no se supera el importe autorizado"
                    };
                }
                
            }

            return respuesta;
        }
    }
}