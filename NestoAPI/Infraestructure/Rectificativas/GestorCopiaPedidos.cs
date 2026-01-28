using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rectificativas
{
    /// <summary>
    /// Gestor para copiar facturas y crear rectificativas.
    /// Issue #85 - Preparado para integración con Issue #38 (LinFacturaVtaRectificacion)
    /// </summary>
    public class GestorCopiaPedidos : IGestorCopiaPedidos
    {
        private readonly NVEntities _db;
        private readonly IServicioPedidosVenta _servicioPedidos;
        private readonly IServicioAlbaranesVenta _servicioAlbaranes;
        private readonly IServicioFacturas _servicioFacturas;

        public GestorCopiaPedidos(
            NVEntities db,
            IServicioPedidosVenta servicioPedidos,
            IServicioAlbaranesVenta servicioAlbaranes,
            IServicioFacturas servicioFacturas)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _servicioPedidos = servicioPedidos ?? throw new ArgumentNullException(nameof(servicioPedidos));
            _servicioAlbaranes = servicioAlbaranes ?? throw new ArgumentNullException(nameof(servicioAlbaranes));
            _servicioFacturas = servicioFacturas ?? throw new ArgumentNullException(nameof(servicioFacturas));
        }

        public async Task<CopiarFacturaResponse> CopiarFactura(CopiarFacturaRequest request, string usuario)
        {
            var response = new CopiarFacturaResponse();

            try
            {
                // 1. Validar request
                ValidarRequest(request);

                // 2. Obtener líneas de la factura origen
                var lineasOrigen = await ObtenerLineasFactura(request.Empresa, request.NumeroFactura);
                if (!lineasOrigen.Any())
                {
                    response.Mensaje = $"No se encontraron líneas para la factura {request.NumeroFactura}";
                    return response;
                }

                // 3. Obtener o crear el pedido destino
                int numeroPedido;
                CabPedidoVta pedidoDestino;

                if (request.AnadirAPedidoOriginal)
                {
                    // Añadir al pedido original
                    numeroPedido = lineasOrigen.First().Número;
                    pedidoDestino = await _db.CabPedidoVtas
                        .FirstOrDefaultAsync(p => p.Empresa == request.Empresa && p.Número == numeroPedido);

                    if (pedidoDestino == null)
                    {
                        response.Mensaje = $"No se encontró el pedido original {numeroPedido}";
                        return response;
                    }
                }
                else
                {
                    // Crear pedido nuevo
                    pedidoDestino = await CrearPedidoNuevo(request, lineasOrigen.First(), usuario);
                    numeroPedido = pedidoDestino.Número;
                }

                response.NumeroPedido = numeroPedido;

                // 4. Copiar las líneas
                var lineasCopiadas = await CopiarLineas(
                    request,
                    lineasOrigen,
                    pedidoDestino,
                    usuario);

                response.LineasCopiadas = lineasCopiadas;

                // 5. Añadir comentario de trazabilidad
                string comentarioOrigen = $"[Copia de factura {request.NumeroFactura}]";
                if (request.InvertirCantidades)
                {
                    comentarioOrigen = $"[Rectificativa de factura {request.NumeroFactura}]";
                }
                if (request.EsCambioCliente)
                {
                    comentarioOrigen += $" [Cliente origen: {request.Cliente}]";
                }
                pedidoDestino.Comentarios = string.IsNullOrEmpty(pedidoDestino.Comentarios)
                    ? comentarioOrigen
                    : pedidoDestino.Comentarios + " " + comentarioOrigen;

                await _db.SaveChangesAsync();

                // 6. Crear albarán y factura si se solicitó
                if (request.CrearAlbaranYFactura)
                {
                    await CrearAlbaranYFactura(request, response, numeroPedido, usuario, lineasCopiadas);
                }

                response.Exitoso = true;
                response.Mensaje = GenerarMensajeExito(response, request);
            }
            catch (Exception ex)
            {
                response.Exitoso = false;
                response.Mensaje = $"Error al copiar factura: {ex.Message}";
                // Registrar en ELMAH para diagnóstico
                Elmah.ErrorSignal.FromCurrentContext().Raise(ex);
            }

            return response;
        }

        private void ValidarRequest(CopiarFacturaRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Empresa))
                throw new ArgumentException("La empresa es requerida");

            if (string.IsNullOrWhiteSpace(request.Cliente))
                throw new ArgumentException("El cliente es requerido");

            if (string.IsNullOrWhiteSpace(request.NumeroFactura))
                throw new ArgumentException("El número de factura es requerido");

            if (request.EsCambioCliente && string.IsNullOrWhiteSpace(request.ContactoDestino))
                throw new ArgumentException("El contacto destino es requerido cuando se cambia de cliente");
        }

        private async Task<List<LinPedidoVta>> ObtenerLineasFactura(string empresa, string numeroFactura)
        {
            return await _db.LinPedidoVtas
                .Where(l => l.Empresa == empresa
                    && l.Nº_Factura.Trim() == numeroFactura.Trim()
                    && l.Estado == Constantes.EstadosLineaVenta.FACTURA)
                .OrderBy(l => l.Nº_Orden)
                .ToListAsync();
        }

        private async Task<CabPedidoVta> CrearPedidoNuevo(
            CopiarFacturaRequest request,
            LinPedidoVta lineaReferencia,
            string usuario)
        {
            // Determinar cliente destino
            string clienteDestino = request.EsCambioCliente ? request.ClienteDestino : request.Cliente;
            string contactoDestino = request.EsCambioCliente ? request.ContactoDestino : lineaReferencia.Contacto;

            // Obtener datos del cliente destino (con condiciones de pago)
            var clienteDb = await _db.Clientes
                .Include(c => c.CondPagoClientes)
                .FirstOrDefaultAsync(c => c.Empresa == request.Empresa
                    && c.Nº_Cliente == clienteDestino
                    && c.Contacto == contactoDestino);

            if (clienteDb == null)
            {
                throw new Exception($"No se encontró el cliente {clienteDestino}/{contactoDestino}");
            }

            // Obtener condiciones de pago del cliente (la de importe mínimo más bajo)
            var condPagoCliente = clienteDb.CondPagoClientes
                .OrderBy(c => c.ImporteMínimo)
                .FirstOrDefault();

            string formaPagoCliente = condPagoCliente?.FormaPago?.Trim() ?? "EFC";
            string plazosPagoCliente = condPagoCliente?.PlazosPago?.Trim() ?? "PRE";

            // Obtener pedido original para copiar datos de cabecera
            var pedidoOriginal = await _db.CabPedidoVtas
                .FirstOrDefaultAsync(p => p.Empresa == request.Empresa && p.Número == lineaReferencia.Número);

            // Obtener siguiente número de pedido
            var contador = await _db.ContadoresGlobales.FirstOrDefaultAsync();
            contador.Pedidos++;
            int numeroPedido = contador.Pedidos;

            // Calcular primer vencimiento
            var plazoPago = await _db.PlazosPago
                .FirstOrDefaultAsync(p => p.Empresa == request.Empresa && p.Número == plazosPagoCliente);

            DateTime primerVencimiento = DateTime.Today;
            if (plazoPago != null)
            {
                primerVencimiento = DateTime.Today.AddDays(plazoPago.DíasPrimerPlazo).AddMonths(plazoPago.MesesPrimerPlazo);
            }

            // Crear cabecera del pedido
            var pedidoNuevo = new CabPedidoVta
            {
                Empresa = request.Empresa,
                Número = numeroPedido,
                Nº_Cliente = clienteDestino,
                Contacto = contactoDestino,
                Fecha = DateTime.Today,
                Forma_Pago = request.MantenerCondicionesOriginales && !request.EsCambioCliente
                    ? pedidoOriginal?.Forma_Pago ?? formaPagoCliente
                    : formaPagoCliente,
                PlazosPago = request.MantenerCondicionesOriginales && !request.EsCambioCliente
                    ? pedidoOriginal?.PlazosPago ?? plazosPagoCliente
                    : plazosPagoCliente,
                Primer_Vencimiento = primerVencimiento,
                IVA = clienteDb.IVA,
                Vendedor = clienteDb.Vendedor,
                Periodo_Facturacion = clienteDb.PeriodoFacturación,
                Ruta = clienteDb.Ruta,
                Serie = pedidoOriginal?.Serie ?? "NV", // TODO: Usar serie rectificativa cuando exista
                CCC = clienteDb.CCC,
                Origen = request.Empresa,
                ContactoCobro = clienteDb.ContactoCobro,
                NoComisiona = pedidoOriginal?.NoComisiona ?? 0,
                MantenerJunto = true, // Para que se facture todo junto
                ServirJunto = true,
                Usuario = usuario
            };

            _db.CabPedidoVtas.Add(pedidoNuevo);
            await _db.SaveChangesAsync();

            return pedidoNuevo;
        }

        private async Task<List<LineaCopiadaDTO>> CopiarLineas(
            CopiarFacturaRequest request,
            List<LinPedidoVta> lineasOrigen,
            CabPedidoVta pedidoDestino,
            string usuario)
        {
            var lineasCopiadas = new List<LineaCopiadaDTO>();

            // Obtener siguiente número de orden
            int siguienteOrden = await _db.LinPedidoVtas
                .Where(l => l.Empresa == pedidoDestino.Empresa && l.Número == pedidoDestino.Número)
                .Select(l => l.Nº_Orden)
                .DefaultIfEmpty(0)
                .MaxAsync() + 1;

            // Obtener datos para recálculo si es necesario
            DescuentosCliente descuentoClienteDestino = null;
            PlazoPago plazoPagoDestino = null;
            if (!request.MantenerCondicionesOriginales && request.EsCambioCliente)
            {
                descuentoClienteDestino = _servicioPedidos.LeerDescuentoCliente(
                    request.Empresa,
                    request.ClienteDestino,
                    request.ContactoDestino);

                plazoPagoDestino = await _db.PlazosPago
                    .FirstOrDefaultAsync(p => p.Empresa == request.Empresa && p.Número == pedidoDestino.PlazosPago);
            }

            foreach (var lineaOrigen in lineasOrigen)
            {
                // Calcular cantidad
                short cantidadNueva = (short)(request.InvertirCantidades
                    ? -lineaOrigen.Cantidad
                    : lineaOrigen.Cantidad);

                // Crear nueva línea
                var lineaNueva = new LinPedidoVta
                {
                    Empresa = pedidoDestino.Empresa,
                    Número = pedidoDestino.Número,
                    Nº_Orden = siguienteOrden++,
                    Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                    TipoLinea = lineaOrigen.TipoLinea,
                    Producto = lineaOrigen.Producto,
                    Texto = lineaOrigen.Texto,
                    Cantidad = cantidadNueva,
                    Fecha_Entrega = DateTime.Today,
                    Almacén = lineaOrigen.Almacén,
                    IVA = lineaOrigen.IVA,
                    Grupo = lineaOrigen.Grupo,
                    SubGrupo = lineaOrigen.SubGrupo,
                    Familia = lineaOrigen.Familia,
                    Nº_Cliente = pedidoDestino.Nº_Cliente,
                    Contacto = pedidoDestino.Contacto,
                    Delegación = lineaOrigen.Delegación,
                    Forma_Venta = lineaOrigen.Forma_Venta,
                    TipoExclusiva = lineaOrigen.TipoExclusiva,
                    Picking = 0,
                    VtoBueno = true,
                    Usuario = usuario,
                    BlancoParaBorrar = "NestoAPI",
                    LineaParcial = true // No es sobre pedido
                };

                // Copiar o recalcular condiciones
                if (request.MantenerCondicionesOriginales || !request.EsCambioCliente)
                {
                    // Mantener condiciones originales
                    lineaNueva.Precio = lineaOrigen.Precio;
                    lineaNueva.PrecioTarifa = lineaOrigen.PrecioTarifa;
                    lineaNueva.Coste = lineaOrigen.Coste;
                    lineaNueva.Descuento = lineaOrigen.Descuento;
                    lineaNueva.DescuentoProducto = lineaOrigen.DescuentoProducto;
                    lineaNueva.DescuentoCliente = lineaOrigen.DescuentoCliente;
                    lineaNueva.DescuentoPP = lineaOrigen.DescuentoPP;
                    lineaNueva.Aplicar_Dto = lineaOrigen.Aplicar_Dto;
                }
                else
                {
                    // Recalcular para cliente destino
                    lineaNueva.Precio = lineaOrigen.Precio; // Precio base igual
                    lineaNueva.PrecioTarifa = lineaOrigen.PrecioTarifa;
                    lineaNueva.Coste = lineaOrigen.Coste;
                    lineaNueva.Descuento = lineaOrigen.Descuento; // Descuento de línea se mantiene
                    lineaNueva.DescuentoProducto = lineaOrigen.DescuentoProducto;
                    lineaNueva.DescuentoCliente = descuentoClienteDestino?.Descuento ?? 0;
                    lineaNueva.DescuentoPP = plazoPagoDestino?.DtoProntoPago ?? 0;
                    lineaNueva.Aplicar_Dto = lineaOrigen.Aplicar_Dto;
                }

                // Calcular importes
                var gestorPedidos = new GestorPedidosVenta(_servicioPedidos);
                gestorPedidos.CalcularImportesLinea(lineaNueva, pedidoDestino.IVA);

                _db.LinPedidoVtas.Add(lineaNueva);

                // Registrar línea copiada para trazabilidad (preparación para #38)
                lineasCopiadas.Add(new LineaCopiadaDTO
                {
                    NumeroLineaNueva = lineaNueva.Nº_Orden,
                    Producto = lineaNueva.Producto?.Trim(),
                    Descripcion = lineaNueva.Texto?.Trim(),
                    FacturaOrigen = request.NumeroFactura,
                    LineaOrigen = lineaOrigen.Nº_Orden,
                    CantidadOriginal = lineaOrigen.Cantidad ?? 0,
                    CantidadCopiada = cantidadNueva,
                    PrecioUnitario = lineaNueva.Precio ?? 0,
                    BaseImponible = lineaNueva.Base_Imponible
                });
            }

            await _db.SaveChangesAsync();
            return lineasCopiadas;
        }

        private async Task CrearAlbaranYFactura(
            CopiarFacturaRequest request,
            CopiarFacturaResponse response,
            int numeroPedido,
            string usuario,
            List<LineaCopiadaDTO> lineasCopiadas)
        {
            bool esMismoCliente = !request.EsCambioCliente;
            bool esRectificativa = request.InvertirCantidades;

            // Lógica especial: mismo cliente
            // - Rectificativa (negativo): albaranear y facturar
            // - Cargo (positivo): NO albaranear ni facturar (dejar para modificaciones del usuario)
            if (esMismoCliente && !esRectificativa)
            {
                response.Mensaje = "Líneas copiadas sin albaranear (mismo cliente, cantidades positivas). " +
                    "Puede modificarlas antes de albaranear.";
                return;
            }

            // Validar que no hay líneas pendientes en el pedido (evitar facturar líneas no deseadas)
            var lineasPendientes = await _db.LinPedidoVtas
                .Where(l => l.Empresa == request.Empresa
                    && l.Número == numeroPedido
                    && l.Estado == Constantes.EstadosLineaVenta.PENDIENTE
                    && !lineasCopiadas.Select(lc => lc.NumeroLineaNueva).Contains(l.Nº_Orden))
                .AnyAsync();

            if (lineasPendientes && request.AnadirAPedidoOriginal)
            {
                response.Mensaje = "ADVERTENCIA: El pedido tiene otras líneas pendientes. " +
                    "Las líneas copiadas se han dejado sin albaranear para evitar facturar líneas no deseadas.";
                return;
            }

            // Crear albarán
            int numeroAlbaran = await _servicioAlbaranes.CrearAlbaran(request.Empresa, numeroPedido, usuario);
            response.NumeroAlbaran = numeroAlbaran;

            // Crear factura
            var resultadoFactura = await _servicioFacturas.CrearFactura(request.Empresa, numeroPedido, usuario);
            response.NumeroFactura = resultadoFactura.NumeroFactura;

            // Issue #38: Guardar vinculaciones en LinFacturaVtaRectificacion
            if (esRectificativa && !string.IsNullOrEmpty(response.NumeroFactura))
            {
                await GuardarVinculacionesRectificativa(
                    request.Empresa,
                    response.NumeroFactura,
                    lineasCopiadas);
            }
        }

        /// <summary>
        /// Guarda las vinculaciones entre la factura rectificativa y las facturas originales.
        /// Issue #38 - LinFacturaVtaRectificacion
        /// </summary>
        private async Task GuardarVinculacionesRectificativa(
            string empresa,
            string numeroFacturaRectificativa,
            List<LineaCopiadaDTO> lineasCopiadas)
        {
            // Obtener las líneas de la factura rectificativa recién creada
            var lineasFacturadas = await _db.LinPedidoVtas
                .Where(l => l.Empresa == empresa
                    && l.Nº_Factura.Trim() == numeroFacturaRectificativa.Trim())
                .ToListAsync();

            foreach (var lineaCopiada in lineasCopiadas.Where(l => l.CantidadCopiada < 0))
            {
                // Buscar la línea facturada correspondiente
                var lineaFacturada = lineasFacturadas
                    .FirstOrDefault(l => l.Producto?.Trim() == lineaCopiada.Producto?.Trim()
                        && l.Cantidad == lineaCopiada.CantidadCopiada);

                if (lineaFacturada != null)
                {
                    _db.LinFacturaVtaRectificaciones.Add(new LinFacturaVtaRectificacion
                    {
                        Empresa = empresa,
                        NumeroFactura = numeroFacturaRectificativa,
                        NumeroLinea = lineaFacturada.Nº_Orden,
                        FacturaOriginalNumero = lineaCopiada.FacturaOrigen,
                        FacturaOriginalLinea = lineaCopiada.LineaOrigen,
                        CantidadRectificada = Math.Abs(lineaCopiada.CantidadCopiada)
                    });
                }
            }

            await _db.SaveChangesAsync();
        }

        private string GenerarMensajeExito(CopiarFacturaResponse response, CopiarFacturaRequest request)
        {
            var partes = new List<string>();

            partes.Add($"Pedido {response.NumeroPedido}");

            if (response.NumeroAlbaran.HasValue)
            {
                partes.Add($"Albarán {response.NumeroAlbaran}");
            }

            if (!string.IsNullOrEmpty(response.NumeroFactura))
            {
                partes.Add($"Factura {response.NumeroFactura}");
            }

            string accion = request.InvertirCantidades ? "Rectificativa creada" : "Copia creada";
            return $"{accion}: {string.Join(", ", partes)}. {response.LineasCopiadas.Count} líneas procesadas.";
        }
    }
}
