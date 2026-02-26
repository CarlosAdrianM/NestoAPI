using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// Validador de aceptación para productos bonificados mediante el sistema Ganavisiones.
    /// Issue #94: Sistema Ganavisiones
    ///
    /// 1 Ganavisión = 10 EUR de importe bonificable.
    /// Los Ganavisiones se generan a partir de líneas de pedido de grupos específicos (COS, ACC, PEL).
    /// Los productos con Ganavisiones configurados pueden bonificarse (100% descuento) si hay suficientes Ganavisiones disponibles.
    /// </summary>
    public class ValidadorGanavisiones : IValidadorAceptacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            RespuestaValidacion respuestaNoValida = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                ProductoId = numeroProducto,
                Motivo = $"El producto {numeroProducto} no puede bonificarse porque no hay suficientes Ganavisiones disponibles en el pedido"
            };

            if (servicio == null)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    ProductoId = numeroProducto,
                    Motivo = "No se ha pasado el servicio para validar Ganavisiones"
                };
            }

            // Verificar si el producto tiene Ganavisiones configurados
            int? ganavisionesProducto = servicio.BuscarGanavisionesProducto(numeroProducto);
            if (!ganavisionesProducto.HasValue)
            {
                // Este producto no está en el sistema Ganavisiones, no aplicamos esta validación
                return respuestaNoValida;
            }

            // Calcular los Ganavisiones disponibles en el pedido
            // Se generan a partir de las líneas de grupos bonificables (COS, ACC, PEL)
            // Fix #118: GrupoProducto puede ser null en líneas de ampliación (NestoApp/Nesto no lo envían),
            // así que lo resolvemos vía servicio cuando no está presente en el DTO
            decimal baseImponibleBonificable = pedido.Lineas
                .Where(l =>
                {
                    string grupo = l.GrupoProducto;
                    if (grupo == null)
                    {
                        grupo = servicio.BuscarProducto(l.Producto)?.Grupo;
                    }
                    return Constantes.Productos.GRUPOS_BONIFICABLES_CON_GANAVISIONES.Contains(grupo);
                })
                .Sum(l => l.BaseImponible);

            int ganavisionesDisponibles = (int)(baseImponibleBonificable / Constantes.Productos.VALOR_GANAVISION_EN_EUROS);

            // Calcular los Ganavisiones consumidos en el pedido
            // Son las líneas con productos que tienen Ganavisiones configurados y están bonificadas (BaseImponible = 0)
            // Se excluyen líneas con oferta asignada (ej: 5+5), ya que esas bonificaciones no son Ganavisiones
            int ganavisionesConsumidos = 0;
            foreach (var linea in pedido.Lineas)
            {
                if (linea.BaseImponible != 0)
                {
                    continue;
                }

                if (linea.oferta != null && linea.oferta != 0)
                {
                    continue;
                }

                int? ganavisionesLinea = servicio.BuscarGanavisionesProducto(linea.Producto);
                if (ganavisionesLinea.HasValue)
                {
                    ganavisionesConsumidos += ganavisionesLinea.Value * linea.Cantidad;
                }
            }

            // Validar que hay suficientes Ganavisiones
            if (ganavisionesConsumidos <= ganavisionesDisponibles)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    ProductoId = numeroProducto,
                    Motivo = $"El producto {numeroProducto} se permite bonificar porque hay {ganavisionesDisponibles} Ganavisiones disponibles " +
                             $"y se consumen {ganavisionesConsumidos} Ganavisiones en total"
                };
            }

            // No hay suficientes Ganavisiones
            respuestaNoValida.Motivo = $"El producto {numeroProducto} no puede bonificarse. " +
                                       $"Se necesitan {ganavisionesConsumidos} Ganavisiones pero solo hay {ganavisionesDisponibles} disponibles " +
                                       $"(base bonificable: {baseImponibleBonificable:N2} EUR / {Constantes.Productos.VALOR_GANAVISION_EN_EUROS} EUR)";
            return respuestaNoValida;
        }
    }
}
