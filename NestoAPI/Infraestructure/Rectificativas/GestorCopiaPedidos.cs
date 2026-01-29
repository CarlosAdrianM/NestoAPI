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
            try
            {
                // Validar request
                ValidarRequest(request);

                // Si es operación AbonoYCargo, ejecutar flujo especial
                if (request.CrearAbonoYCargo)
                {
                    return await EjecutarAbonoYCargo(request, usuario);
                }

                // Si hay múltiples facturas, procesar según la opción de agrupar
                if (request.NumerosFactura != null && request.NumerosFactura.Count > 1)
                {
                    return await CopiarMultiplesFacturas(request, usuario);
                }

                // Ejecutar copia estándar (una sola factura)
                return await CopiarFacturaInterno(request, usuario);
            }
            catch (Exception ex)
            {
                // Registrar en ELMAH para diagnóstico
                Elmah.ErrorSignal.FromCurrentContext().Raise(ex);

                return new CopiarFacturaResponse
                {
                    Exitoso = false,
                    Mensaje = $"Error al copiar factura: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Procesa múltiples facturas según la opción de agrupar.
        /// Issue #279 - SelectorFacturas
        /// </summary>
        private async Task<CopiarFacturaResponse> CopiarMultiplesFacturas(CopiarFacturaRequest request, string usuario)
        {
            if (request.AgruparEnUnaRectificativa)
            {
                // Agrupar todas las facturas en una sola rectificativa
                return await CopiarFacturasAgrupadas(request, usuario);
            }
            else
            {
                // Crear una rectificativa por cada factura
                return await CopiarFacturasSeparadas(request, usuario);
            }
        }

        /// <summary>
        /// Copia múltiples facturas agrupándolas en un solo pedido/rectificativa.
        /// </summary>
        private async Task<CopiarFacturaResponse> CopiarFacturasAgrupadas(CopiarFacturaRequest request, string usuario)
        {
            var response = new CopiarFacturaResponse();
            var todasLasLineas = new List<LinPedidoVta>();
            var facturasEncontradas = new List<string>();

            // Obtener líneas de todas las facturas
            foreach (var numeroFactura in request.NumerosFactura)
            {
                var (lineas, facturaReal, error) = await ObtenerLineasFacturaOPedido(request.Empresa, numeroFactura);
                if (!string.IsNullOrEmpty(error))
                {
                    response.Mensaje = $"Error en factura {numeroFactura}: {error}";
                    return response;
                }
                if (lineas.Any())
                {
                    todasLasLineas.AddRange(lineas);
                    facturasEncontradas.Add(facturaReal ?? numeroFactura);
                }
            }

            if (!todasLasLineas.Any())
            {
                response.Mensaje = "No se encontraron líneas en las facturas seleccionadas";
                return response;
            }

            // Usar el request original pero con NumeroFactura como la primera factura
            // (para trazabilidad en comentarios)
            request.NumeroFactura = string.Join(", ", facturasEncontradas);

            // Crear pedido nuevo con todas las líneas
            var pedidoDestino = await CrearPedidoNuevo(request, todasLasLineas.First(), usuario);
            response.NumeroPedido = pedidoDestino.Número;

            // Copiar líneas de todas las facturas
            var lineasCopiadas = await CopiarLineasMultiples(request, todasLasLineas, pedidoDestino, usuario, facturasEncontradas);
            response.LineasCopiadas = lineasCopiadas;

            // Añadir comentarios
            string comentarioTrazabilidad = request.InvertirCantidades
                ? $"[Rectificativa de facturas: {string.Join(", ", facturasEncontradas)}]"
                : $"[Copia de facturas: {string.Join(", ", facturasEncontradas)}]";

            var partesComentario = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Comentarios))
            {
                partesComentario.Add(request.Comentarios.Trim());
            }
            partesComentario.Add(comentarioTrazabilidad);

            string comentarioFinal = string.Join(" ", partesComentario);
            pedidoDestino.Comentarios = string.IsNullOrEmpty(pedidoDestino.Comentarios)
                ? comentarioFinal
                : pedidoDestino.Comentarios + " " + comentarioFinal;

            await _db.SaveChangesAsync();

            // Crear albarán y factura si se solicitó
            if (request.CrearAlbaranYFactura)
            {
                await CrearAlbaranYFactura(request, response, pedidoDestino.Número, usuario, lineasCopiadas);
            }

            response.Exitoso = true;
            response.Mensaje = $"{(request.InvertirCantidades ? "Rectificativa" : "Copia")} creada: Pedido {response.NumeroPedido}" +
                (response.NumeroFactura != null ? $", Factura {response.NumeroFactura}" : "") +
                $". {lineasCopiadas.Count} líneas de {facturasEncontradas.Count} facturas.";

            return response;
        }

        /// <summary>
        /// Copia múltiples facturas creando una rectificativa separada por cada una.
        /// </summary>
        private async Task<CopiarFacturaResponse> CopiarFacturasSeparadas(CopiarFacturaRequest request, string usuario)
        {
            var response = new CopiarFacturaResponse
            {
                ResultadosIndividuales = new List<CopiarFacturaResponse>()
            };

            var exitosos = 0;
            var errores = 0;

            foreach (var numeroFactura in request.NumerosFactura)
            {
                // Crear un request individual para cada factura
                var requestIndividual = new CopiarFacturaRequest
                {
                    Empresa = request.Empresa,
                    Cliente = request.Cliente,
                    NumeroFactura = numeroFactura,
                    InvertirCantidades = request.InvertirCantidades,
                    AnadirAPedidoOriginal = false, // Siempre crear pedidos nuevos
                    MantenerCondicionesOriginales = request.MantenerCondicionesOriginales,
                    CrearAlbaranYFactura = request.CrearAlbaranYFactura,
                    ClienteDestino = request.ClienteDestino,
                    ContactoDestino = request.ContactoDestino,
                    Comentarios = request.Comentarios
                };

                var resultadoIndividual = await CopiarFacturaInterno(requestIndividual, usuario);
                response.ResultadosIndividuales.Add(resultadoIndividual);

                if (resultadoIndividual.Exitoso)
                {
                    exitosos++;
                }
                else
                {
                    errores++;
                }
            }

            // Rellenar campos principales con el primer resultado exitoso (para compatibilidad)
            var primerExitoso = response.ResultadosIndividuales.FirstOrDefault(r => r.Exitoso);
            if (primerExitoso != null)
            {
                response.NumeroPedido = primerExitoso.NumeroPedido;
                response.NumeroAlbaran = primerExitoso.NumeroAlbaran;
                response.NumeroFactura = primerExitoso.NumeroFactura;
                response.LineasCopiadas = primerExitoso.LineasCopiadas;
            }

            response.Exitoso = exitosos > 0;
            response.Mensaje = errores == 0
                ? $"{exitosos} rectificativa(s) creada(s) correctamente"
                : $"{exitosos} rectificativa(s) creada(s), {errores} error(es)";

            return response;
        }

        /// <summary>
        /// Copia líneas de múltiples facturas a un mismo pedido.
        /// </summary>
        private async Task<List<LineaCopiadaDTO>> CopiarLineasMultiples(
            CopiarFacturaRequest request,
            List<LinPedidoVta> todasLasLineas,
            CabPedidoVta pedidoDestino,
            string usuario,
            List<string> facturasOrigen)
        {
            var lineasCopiadas = new List<LineaCopiadaDTO>();
            int siguienteOrden = await _db.LinPedidoVtas
                .Where(l => l.Empresa == pedidoDestino.Empresa && l.Número == pedidoDestino.Número)
                .Select(l => l.Nº_Orden)
                .DefaultIfEmpty(0)
                .MaxAsync() + 1;

            foreach (var lineaOrigen in todasLasLineas)
            {
                short cantidadNueva = (short)(request.InvertirCantidades
                    ? -lineaOrigen.Cantidad
                    : lineaOrigen.Cantidad);

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
                    LineaParcial = true,
                    Precio = lineaOrigen.Precio,
                    PrecioTarifa = lineaOrigen.PrecioTarifa,
                    Coste = lineaOrigen.Coste,
                    Descuento = lineaOrigen.Descuento,
                    DescuentoProducto = lineaOrigen.DescuentoProducto,
                    DescuentoCliente = lineaOrigen.DescuentoCliente,
                    DescuentoPP = lineaOrigen.DescuentoPP,
                    Aplicar_Dto = lineaOrigen.Aplicar_Dto
                };

                // Calcular importes
                var gestorPedidos = new GestorPedidosVenta(_servicioPedidos);
                gestorPedidos.CalcularImportesLinea(lineaNueva, pedidoDestino.IVA);

                _db.LinPedidoVtas.Add(lineaNueva);

                lineasCopiadas.Add(new LineaCopiadaDTO
                {
                    NumeroLineaNueva = lineaNueva.Nº_Orden,
                    Producto = lineaNueva.Producto?.Trim(),
                    Descripcion = lineaNueva.Texto?.Trim(),
                    FacturaOrigen = lineaOrigen.Nº_Factura?.Trim(),
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

        private void ValidarRequest(CopiarFacturaRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Empresa))
                throw new ArgumentException("La empresa es requerida");

            if (string.IsNullOrWhiteSpace(request.Cliente))
                throw new ArgumentException("El cliente es requerido");

            // Debe tener NumeroFactura O NumerosFactura con al menos un elemento
            bool tieneFacturaSimple = !string.IsNullOrWhiteSpace(request.NumeroFactura);
            bool tieneFacturasMultiples = request.NumerosFactura != null && request.NumerosFactura.Any();

            if (!tieneFacturaSimple && !tieneFacturasMultiples)
                throw new ArgumentException("El número de factura es requerido");

            // Si hay NumerosFactura con un solo elemento, usar NumeroFactura para simplificar
            if (tieneFacturasMultiples && request.NumerosFactura.Count == 1)
            {
                request.NumeroFactura = request.NumerosFactura[0];
            }

            if (request.EsCambioCliente && string.IsNullOrWhiteSpace(request.ContactoDestino))
                throw new ArgumentException("El contacto destino es requerido cuando se cambia de cliente");
        }

        /// <summary>
        /// Obtiene las líneas de una factura. Si no encuentra la factura, intenta buscar por número de pedido.
        /// Si el pedido tiene más de una factura, devuelve error.
        /// </summary>
        /// <returns>Tupla con las líneas y el número de factura encontrado (puede ser diferente si se buscó por pedido)</returns>
        private async Task<(List<LinPedidoVta> Lineas, string NumeroFactura, string Error)> ObtenerLineasFacturaOPedido(string empresa, string numeroFacturaOPedido)
        {
            // Primero intentar buscar como factura
            var lineasFactura = await _db.LinPedidoVtas
                .Where(l => l.Empresa == empresa
                    && l.Nº_Factura.Trim() == numeroFacturaOPedido.Trim()
                    && l.Estado == Constantes.EstadosLineaVenta.FACTURA)
                .OrderBy(l => l.Nº_Orden)
                .ToListAsync();

            if (lineasFactura.Any())
            {
                return (lineasFactura, numeroFacturaOPedido.Trim(), null);
            }

            // Si no encuentra como factura, intentar como número de pedido
            if (int.TryParse(numeroFacturaOPedido.Trim(), out int numeroPedido))
            {
                // Buscar líneas facturadas de ese pedido
                var lineasPedido = await _db.LinPedidoVtas
                    .Where(l => l.Empresa == empresa
                        && l.Número == numeroPedido
                        && l.Estado == Constantes.EstadosLineaVenta.FACTURA)
                    .ToListAsync();

                if (!lineasPedido.Any())
                {
                    return (new List<LinPedidoVta>(), null,
                        $"No se encontró la factura '{numeroFacturaOPedido}' ni el pedido {numeroPedido} con líneas facturadas");
                }

                // Verificar cuántas facturas distintas tiene el pedido
                var facturasDistintas = lineasPedido
                    .Select(l => l.Nº_Factura?.Trim())
                    .Where(f => !string.IsNullOrEmpty(f))
                    .Distinct()
                    .ToList();

                if (facturasDistintas.Count > 1)
                {
                    return (new List<LinPedidoVta>(), null,
                        $"El pedido {numeroPedido} tiene {facturasDistintas.Count} facturas: {string.Join(", ", facturasDistintas)}. " +
                        "Debe especificar el número de factura.");
                }

                // El pedido tiene una sola factura
                string facturaEncontrada = facturasDistintas.FirstOrDefault();
                var lineasOrdenadas = lineasPedido.OrderBy(l => l.Nº_Orden).ToList();
                return (lineasOrdenadas, facturaEncontrada, null);
            }

            // No es ni factura ni número de pedido válido
            return (new List<LinPedidoVta>(), null,
                $"No se encontró la factura '{numeroFacturaOPedido}'");
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

            // Rectificativas siempre con plazos CONTADO (requisito legal)
            bool esRectificativa = request.InvertirCantidades;
            string plazosPagoFinal = esRectificativa
                ? Constantes.PlazosPago.CONTADO
                : (request.MantenerCondicionesOriginales && !request.EsCambioCliente
                    ? pedidoOriginal?.PlazosPago ?? plazosPagoCliente
                    : plazosPagoCliente);

            // Si es rectificativa con CONTADO, el vencimiento es hoy
            DateTime vencimientoFinal = esRectificativa ? DateTime.Today : primerVencimiento;

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
                PlazosPago = plazosPagoFinal,
                Primer_Vencimiento = vencimientoFinal,
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
            // Extraer IDs a lista de primitivos para que EF pueda traducirlo a SQL
            var idsLineasCopiadas = lineasCopiadas.Select(lc => lc.NumeroLineaNueva).ToList();
            var lineasPendientes = await _db.LinPedidoVtas
                .Where(l => l.Empresa == request.Empresa
                    && l.Número == numeroPedido
                    && l.Estado == Constantes.EstadosLineaVenta.PENDIENTE
                    && !idsLineasCopiadas.Contains(l.Nº_Orden))
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

        /// <summary>
        /// Ejecuta la operación de Abono + Cargo en un solo paso:
        /// 1. Crea rectificativa (abono) al cliente ORIGEN de la factura
        /// 2. Crea factura nueva (cargo) al cliente DESTINO seleccionado
        ///
        /// Útil para:
        /// - Corregir errores de dirección: abono a dirección incorrecta + cargo a dirección correcta
        /// - Traspasar facturas entre clientes: abono al cliente erróneo + cargo al correcto
        /// </summary>
        private async Task<CopiarFacturaResponse> EjecutarAbonoYCargo(CopiarFacturaRequest request, string usuario)
        {
            var response = new CopiarFacturaResponse();

            try
            {
                // Validar que hay cliente destino (requerido para esta operación)
                if (string.IsNullOrWhiteSpace(request.ClienteDestino) || string.IsNullOrWhiteSpace(request.ContactoDestino))
                {
                    throw new ArgumentException("Para la operación Abono+Cargo debe especificar ClienteDestino y ContactoDestino");
                }

                // Obtener datos del cliente origen desde la factura (o buscar por pedido)
                var (lineasOrigen, numeroFacturaReal, error) = await ObtenerLineasFacturaOPedido(request.Empresa, request.NumeroFactura);
                if (!string.IsNullOrEmpty(error))
                {
                    response.Mensaje = error;
                    return response;
                }
                if (!lineasOrigen.Any())
                {
                    response.Mensaje = $"No se encontraron líneas para '{request.NumeroFactura}'";
                    return response;
                }

                // Usar el número de factura real (puede ser diferente si se buscó por pedido)
                string facturaAUsar = numeroFacturaReal ?? request.NumeroFactura;

                var primeraLinea = lineasOrigen.First();
                string clienteOrigen = primeraLinea.Nº_Cliente?.Trim();
                string contactoOrigen = primeraLinea.Contacto?.Trim();

                // ========== PASO 1: Crear ABONO al cliente origen ==========
                var requestAbono = new CopiarFacturaRequest
                {
                    Empresa = request.Empresa,
                    Cliente = clienteOrigen,
                    NumeroFactura = facturaAUsar,
                    InvertirCantidades = true, // Abono = cantidades negativas
                    AnadirAPedidoOriginal = false, // Pedido nuevo
                    MantenerCondicionesOriginales = true, // Mismas condiciones que la factura original
                    CrearAlbaranYFactura = true, // Facturar automáticamente
                    // No cambiar de cliente para el abono
                    ClienteDestino = null,
                    ContactoDestino = null
                };

                var responseAbono = await CopiarFacturaInterno(requestAbono, usuario);
                if (!responseAbono.Exitoso)
                {
                    response.Mensaje = $"Error al crear abono: {responseAbono.Mensaje}";
                    return response;
                }

                response.Abono = new OperacionFacturaDTO
                {
                    Cliente = clienteOrigen,
                    Contacto = contactoOrigen,
                    NumeroPedido = responseAbono.NumeroPedido,
                    NumeroAlbaran = responseAbono.NumeroAlbaran,
                    NumeroFactura = responseAbono.NumeroFactura,
                    Lineas = responseAbono.LineasCopiadas
                };

                // ========== PASO 2: Crear CARGO al cliente destino ==========
                var requestCargo = new CopiarFacturaRequest
                {
                    Empresa = request.Empresa,
                    Cliente = clienteOrigen, // Cliente de la factura original
                    NumeroFactura = facturaAUsar,
                    InvertirCantidades = false, // Cargo = cantidades positivas
                    AnadirAPedidoOriginal = false, // Pedido nuevo
                    MantenerCondicionesOriginales = request.MantenerCondicionesOriginales,
                    CrearAlbaranYFactura = true, // Facturar automáticamente
                    ClienteDestino = request.ClienteDestino,
                    ContactoDestino = request.ContactoDestino
                };

                var responseCargo = await CopiarFacturaInterno(requestCargo, usuario);
                if (!responseCargo.Exitoso)
                {
                    response.Mensaje = $"ATENCIÓN: Abono creado ({response.Abono.NumeroFactura}) pero error al crear cargo: {responseCargo.Mensaje}";
                    response.Exitoso = false;
                    return response;
                }

                response.Cargo = new OperacionFacturaDTO
                {
                    Cliente = request.ClienteDestino,
                    Contacto = request.ContactoDestino,
                    NumeroPedido = responseCargo.NumeroPedido,
                    NumeroAlbaran = responseCargo.NumeroAlbaran,
                    NumeroFactura = responseCargo.NumeroFactura,
                    Lineas = responseCargo.LineasCopiadas
                };

                // Rellenar campos principales del response con datos del cargo (para compatibilidad)
                response.NumeroPedido = responseCargo.NumeroPedido;
                response.NumeroAlbaran = responseCargo.NumeroAlbaran;
                response.NumeroFactura = responseCargo.NumeroFactura;
                response.LineasCopiadas = responseCargo.LineasCopiadas;

                response.Exitoso = true;
                response.Mensaje = $"Abono+Cargo completado. " +
                    $"ABONO: Factura {response.Abono.NumeroFactura} (cliente {clienteOrigen}/{contactoOrigen}). " +
                    $"CARGO: Factura {response.Cargo.NumeroFactura} (cliente {request.ClienteDestino}/{request.ContactoDestino}). " +
                    $"{lineasOrigen.Count} líneas procesadas.";
            }
            catch (Exception ex)
            {
                response.Exitoso = false;
                response.Mensaje = $"Error en operación Abono+Cargo: {ex.Message}";
                Elmah.ErrorSignal.FromCurrentContext().Raise(ex);
            }

            return response;
        }

        /// <summary>
        /// Método interno que ejecuta la copia sin la validación de CrearAbonoYCargo
        /// (para evitar recursión infinita).
        /// </summary>
        private async Task<CopiarFacturaResponse> CopiarFacturaInterno(CopiarFacturaRequest request, string usuario)
        {
            var response = new CopiarFacturaResponse();

            // Obtener líneas de la factura origen (o buscar por pedido si no encuentra factura)
            var (lineasOrigen, numeroFacturaReal, error) = await ObtenerLineasFacturaOPedido(request.Empresa, request.NumeroFactura);
            if (!string.IsNullOrEmpty(error))
            {
                response.Mensaje = error;
                return response;
            }
            if (!lineasOrigen.Any())
            {
                response.Mensaje = $"No se encontraron líneas para '{request.NumeroFactura}'";
                return response;
            }

            // Actualizar el número de factura en el request si se encontró por pedido
            if (numeroFacturaReal != request.NumeroFactura?.Trim())
            {
                request.NumeroFactura = numeroFacturaReal;
            }

            // Obtener o crear el pedido destino
            int numeroPedido;
            CabPedidoVta pedidoDestino;

            if (request.AnadirAPedidoOriginal)
            {
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
                pedidoDestino = await CrearPedidoNuevo(request, lineasOrigen.First(), usuario);
                numeroPedido = pedidoDestino.Número;
            }

            response.NumeroPedido = numeroPedido;

            // Copiar las líneas
            var lineasCopiadas = await CopiarLineas(
                request,
                lineasOrigen,
                pedidoDestino,
                usuario);

            response.LineasCopiadas = lineasCopiadas;

            // Añadir comentario del usuario (si existe) + comentario de trazabilidad + comentarios originales
            string comentarioTrazabilidad = $"[Copia de factura {request.NumeroFactura}]";
            if (request.InvertirCantidades)
            {
                comentarioTrazabilidad = $"[Rectificativa de factura {request.NumeroFactura}]";
            }
            if (request.EsCambioCliente)
            {
                comentarioTrazabilidad += $" [Cliente origen: {request.Cliente}]";
            }

            // Construir comentario final: comentario usuario + trazabilidad + comentarios originales
            var partesComentario = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Comentarios))
            {
                partesComentario.Add(request.Comentarios.Trim());
            }
            partesComentario.Add(comentarioTrazabilidad);

            // Si mantiene condiciones originales, copiar también los comentarios del pedido original
            if (request.MantenerCondicionesOriginales)
            {
                int numeroPedidoOriginal = lineasOrigen.First().Número;
                var pedidoOriginal = await _db.CabPedidoVtas
                    .FirstOrDefaultAsync(p => p.Empresa == request.Empresa && p.Número == numeroPedidoOriginal);

                if (pedidoOriginal != null && !string.IsNullOrWhiteSpace(pedidoOriginal.Comentarios))
                {
                    partesComentario.Add(pedidoOriginal.Comentarios.Trim());
                }
            }

            string comentarioFinal = string.Join(" ", partesComentario);
            pedidoDestino.Comentarios = string.IsNullOrEmpty(pedidoDestino.Comentarios)
                ? comentarioFinal
                : pedidoDestino.Comentarios + " " + comentarioFinal;

            await _db.SaveChangesAsync();

            // Crear albarán y factura si se solicitó
            if (request.CrearAlbaranYFactura)
            {
                await CrearAlbaranYFactura(request, response, numeroPedido, usuario, lineasCopiadas);
            }

            response.Exitoso = true;
            response.Mensaje = GenerarMensajeExito(response, request);

            return response;
        }
    }
}
