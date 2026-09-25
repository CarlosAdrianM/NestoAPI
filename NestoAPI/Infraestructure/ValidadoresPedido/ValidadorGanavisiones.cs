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
    /// Los Ganavisiones se generan a partir de líneas de pedido de grupos específicos (COS y ACC;
    /// la peluquería quedó fuera en NestoAPI#466).
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

            // NestoAPI#228: las líneas bonificadas que YA estaban guardadas en el pedido (id != 0)
            // se respetan aunque el Ganavisión se haya retirado, caducado o quedado sin saldo
            // después de crearse el pedido: fueron válidas en su día y ampliar/unir el pedido no
            // debe revalidarlas contra la disponibilidad actual. Solo se valida lo NUEVO: si la
            // ampliación añade líneas bonificadas (id == 0) de este producto, sigue la validación
            // normal completa. Mismo criterio que el check de stock de Nesto#346 (más abajo).
            var lineasBonificadasProducto = pedido.Lineas
                .Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                    && l.Producto == numeroProducto
                    && l.BaseImponible == 0
                    && (l.oferta == null || l.oferta == 0))
                .ToList();
            // NestoAPI#528: "ya estaba guardada" no basta: si se cambia el producto, se sube la cantidad
            // o viene de un presupuesto, hay unidades nuevas y se valida todo (puntos y stock).
            if (lineasBonificadasProducto.Any() && lineasBonificadasProducto.All(l => ValidadorRegaloSinStock.UnidadesNuevas(l) == 0))
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    ProductoId = numeroProducto,
                    Motivo = $"El producto {numeroProducto} bonificado ya estaba guardado en el pedido y se respeta " +
                             "aunque el Ganavisión ya no esté disponible (solo se validan las líneas nuevas)"
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
            // Se generan a partir de las líneas de grupos bonificables (COS y ACC; PEL no, #466)
            // Fix #118: GrupoProducto puede ser null en líneas de ampliación (NestoApp/Nesto no lo envían),
            // así que lo resolvemos vía servicio cuando no está presente en el DTO
            decimal baseImponibleBonificable = pedido.Lineas
                .Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
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
            foreach (var linea in pedido.Lineas.Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO))
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
                // Issue #117: Validar stock disponible del producto bonificado.
                // Nesto#346: solo se valida el stock de lo NUEVO. Las líneas ya persistidas ya
                // pasaron este check al crearse y su stock ya quedó reservado; re-validarlas
                // devolvería 0 disponible (porque cuenta la propia reserva como stock consumido).
                // NestoAPI#528: mismo núcleo que ValidadorRegaloSinStock: "nuevo" incluye cambiar el
                // producto, subir la cantidad o aceptar un presupuesto, y el stock es el del almacén
                // de la línea si el pedido sale según vaya entrando.
                FaltaDeStockRegalo falta = ValidadorRegaloSinStock.PrimeraFaltaDeStock(
                    pedido, servicio, l => l.Producto?.Trim() == numeroProducto?.Trim());
                if (falta != null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        ProductoId = numeroProducto,
                        Motivo = $"El producto {numeroProducto} no puede bonificarse porque no hay stock disponible suficiente" +
                                 $"{(falta.Almacen == null ? string.Empty : " en " + falta.Almacen)} " +
                                 $"(disponible: {falta.Disponible}, solicitado: {falta.UnidadesNuevas})"
                    };
                }

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
