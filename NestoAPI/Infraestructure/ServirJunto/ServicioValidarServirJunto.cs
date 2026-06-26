using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure;
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
        private readonly ILogService logService;

        // Constructor de producción: NVEntities y ProductoService no están registrados
        // en el contenedor DI, así que se instancian aquí (mismo patrón que InformesService).
        public ServicioValidarServirJunto()
        {
            db = new NVEntities();
            productoService = new ProductoService();
            logService = new ElmahLogService();
        }

        internal ServicioValidarServirJunto(NVEntities db, IProductoService productoService, ILogService logService = null)
        {
            this.db = db;
            this.productoService = productoService;
            this.logService = logService ?? new ElmahLogService();
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
                var resultado = await validador.Validar(request.Almacen, productosUnificados, lineasPedido, request.Pedido).ConfigureAwait(false);
                if (!resultado.PuedeDesmarcar)
                {
                    // NestoAPI#220: dejamos constancia en ELMAH del porqué se denegó desmarcar "servir
                    // junto" (antes no quedaba en ningún sitio y no podíamos diagnosticar las quejas).
                    // El usuario lo captura ELMAH del contexto HTTP. No bloquea: si el log falla, se ignora.
                    LogDenegacion(request, resultado);
                    return resultado;
                }
            }

            var respuesta = ConAvisoSiProcede(NuevaRespuestaOK(), request);

            // NestoAPI#211 / Nesto#365: si el cliente manda las líneas, devolvemos la base de portes
            // que quedaría al desmarcar (excluyendo las sobre pedido) para que avise si aparecen portes.
            if (request.LineasParaPortes != null && request.LineasParaPortes.Any())
            {
                respuesta.BaseImponibleSinServirJunto =
                    CalcularBaseImponibleSinServirJunto(request.LineasParaPortes, new GestorStocks());
            }

            return respuesta;
        }

        /// <summary>
        /// Base imponible de portes sin "servir junto": suma las líneas que NO son sobre pedido
        /// (estado 0 siempre cuenta; estado != 0 solo si hay stock en el almacén). Reutiliza la misma
        /// regla que NestoAPI#211 (GestorPortes.EsSobrePedidoParaPortes). El stock solo se consulta para
        /// líneas estado != 0.
        /// </summary>
        internal static decimal CalcularBaseImponibleSinServirJunto(
            List<LineaPortesServirJuntoDTO> lineas, IGestorStocks gestorStocks)
        {
            if (lineas == null)
            {
                return 0;
            }

            return lineas
                .Where(l => l.Estado == Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO
                         || !GestorPortes.EsSobrePedidoParaPortes(
                                l.Estado, l.Cantidad,
                                gestorStocks.Stock(l.ProductoId?.Trim(), l.Almacen?.Trim()),
                                stockDisponibleTodosAlmacenes: 0, servirJunto: false))
                .Sum(l => l.BaseImponible);
        }

        // NestoAPI#187: añade Aviso al response si el pedido aplica comisión contra
        // reembolso. Solo informativo — no cambia PuedeDesmarcar. El cliente mostrará
        // una confirmación al usuario para que no haya sorpresas con la comisión por
        // envío que introduce NestoAPI#174.
        internal static ValidarServirJuntoResponse ConAvisoSiProcede(
            ValidarServirJuntoResponse response, ValidarServirJuntoRequest request)
        {
            if (response == null || !response.PuedeDesmarcar) return response;

            if (GestorPortes.EsContraReembolso(
                    request.FormaPago,
                    request.PlazosPago,
                    request.CCC,
                    request.PeriodoFacturacion,
                    request.NotaEntrega.GetValueOrDefault(false)))
            {
                response.Aviso = Constantes.Portes.AVISO_COMISION_REEMBOLSO_SPLIT;
            }

            return response;
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

        /// <summary>
        /// NestoAPI#220: registra en ELMAH (vía ILogService) el motivo por el que se denegó desmarcar
        /// "servir junto", con el almacén, los productos problemáticos y lo que mandó el cliente, para
        /// poder diagnosticar después. El usuario lo añade ELMAH del contexto HTTP. Nunca lanza.
        /// </summary>
        private void LogDenegacion(ValidarServirJuntoRequest request, ValidarServirJuntoResponse resultado)
        {
            try
            {
                string problematicos = resultado.ProductosProblematicos != null
                    ? string.Join(", ", resultado.ProductosProblematicos.Select(p => p.ProductoId?.Trim()))
                    : "";
                string bonificados = string.Join(", ",
                    UnificarProductosBonificados(request).Select(p => p.ProductoId?.Trim()));
                string lineas = request.LineasPedido != null
                    ? string.Join(", ", request.LineasPedido.Select(l => l.ProductoId?.Trim()))
                    : "";

                logService.LogError(
                    $"[ServirJunto NestoAPI#220] Denegado desmarcar 'servir junto' en almacén {request.Almacen?.Trim()}. " +
                    $"Motivo: {resultado.Mensaje}. Productos problemáticos: [{problematicos}]. " +
                    $"Bonificados enviados: [{bonificados}]. Líneas enviadas: [{lineas}].");
            }
            catch
            {
                // El logging de diagnóstico nunca debe afectar a la respuesta de validación.
            }
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
