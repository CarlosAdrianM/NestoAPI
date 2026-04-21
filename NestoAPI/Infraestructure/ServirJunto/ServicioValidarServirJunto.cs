using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.ValidadoresServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta.ServirJunto;
using System.Collections.Generic;
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
