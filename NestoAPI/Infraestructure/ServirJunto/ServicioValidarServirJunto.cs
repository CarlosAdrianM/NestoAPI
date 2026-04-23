using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.ValidadoresServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ServirJunto
{
    public class ServicioValidarServirJunto : IServicioValidarServirJunto
    {
        private readonly NVEntities db;
        private readonly IProductoService productoService;

        // Constructor de producción: NVEntities y ProductoService no están registrados
        // en el contenedor DI, así que se instancian aquí (mismo patrón que InformesService).
        public ServicioValidarServirJunto()
        {
            db = new NVEntities();
            productoService = new ProductoService();
        }

        internal ServicioValidarServirJunto(NVEntities db, IProductoService productoService)
        {
            this.db = db;
            this.productoService = productoService;
        }

        public async Task<ValidarServirJuntoResponse> Validar(ValidarServirJuntoRequest request)
        {
            var productosUnificados = UnificarProductosBonificados(request);
            var lineasPedido = request.LineasPedido ?? new List<ProductoBonificadoConCantidadRequest>();

            // NestoAPI#175: el cliente sólo puede marcar candidatos a bonificado con
            // criterio local (BaseImponible==0, sin oferta). El servidor confirma
            // consultando la tabla Ganavision y desmarca los falsos positivos (MMP,
            // regalos por importe, descuentos 100% manuales…) que no son bonificados
            // del sistema Ganavisiones.
            lineasPedido = await ConfirmarBonificadosContraBdAsync(lineasPedido).ConfigureAwait(false);

            // Si no hay bonificados Y tampoco hay líneas del pedido que validar, se puede
            // desmarcar directamente. Si solo hay líneas del pedido (p. ej. solo MMP sin
            // regalos), sí hay que pasar por los validadores.
            if (!productosUnificados.Any() && !lineasPedido.Any())
            {
                return NuevaRespuestaOK();
            }

            // ValidadorMaterialPromocional va primero porque tiene un mensaje más
            // específico: los MMP no se resuelven trayendo stock de otro almacén; la
            // única solución es borrar ese producto del pedido.
            var validadores = new List<IValidadorServirJunto>
            {
                new ValidadorMaterialPromocional(db, productoService),
                new ValidadorDisponibilidadRegalos(db, productoService)
            };

            foreach (var validador in validadores)
            {
                var resultado = await validador.Validar(request.Almacen, productosUnificados, lineasPedido).ConfigureAwait(false);
                if (!resultado.PuedeDesmarcar)
                {
                    return resultado;
                }
            }

            return NuevaRespuestaOK();
        }

        private async Task<List<ProductoBonificadoConCantidadRequest>> ConfirmarBonificadosContraBdAsync(
            List<ProductoBonificadoConCantidadRequest> lineasPedido)
        {
            var candidatosIds = lineasPedido
                .Where(l => l.EsBonificadoGanavisiones)
                .Select(l => l.ProductoId?.Trim())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            if (!candidatosIds.Any())
            {
                return lineasPedido;
            }

            var idsEnBd = await db.Ganavisiones
                .Where(g => g.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                         && candidatosIds.Contains(g.ProductoId))
                .Select(g => g.ProductoId)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false);

            var confirmados = new HashSet<string>(idsEnBd.Select(id => id.Trim()));

            return lineasPedido.Select(l =>
            {
                if (!l.EsBonificadoGanavisiones) return l;
                if (confirmados.Contains(l.ProductoId?.Trim())) return l;

                // Candidato no confirmado: se desmarca para que no entre al validador
                // de regalos. Si es MMP lo cogerá su validador específico por subgrupo.
                return new ProductoBonificadoConCantidadRequest
                {
                    ProductoId = l.ProductoId,
                    Cantidad = l.Cantidad,
                    EsBonificadoGanavisiones = false
                };
            }).ToList();
        }

        internal static List<ProductoBonificadoConCantidadRequest> UnificarProductosBonificados(ValidarServirJuntoRequest request)
        {
            if (request.ProductosBonificadosConCantidad != null && request.ProductosBonificadosConCantidad.Any())
            {
                return request.ProductosBonificadosConCantidad;
            }

            if (request.ProductosBonificados != null)
            {
                return request.ProductosBonificados
                    .Select(id => new ProductoBonificadoConCantidadRequest { ProductoId = id, Cantidad = 1 })
                    .ToList();
            }

            return new List<ProductoBonificadoConCantidadRequest>();
        }

        private static ValidarServirJuntoResponse NuevaRespuestaOK() =>
            new ValidarServirJuntoResponse
            {
                PuedeDesmarcar = true,
                ProductosProblematicos = new List<ProductoSinStockDTO>(),
                Mensaje = null
            };
    }
}
