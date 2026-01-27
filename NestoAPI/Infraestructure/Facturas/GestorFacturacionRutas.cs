using Elmah;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// Gestor para facturación masiva de pedidos por rutas.
    /// Procesa pedidos de rutas (propia o agencias), crea albaranes/facturas y genera PDFs para impresión.
    /// </summary>
    public class GestorFacturacionRutas : IGestorFacturacionRutas
    {
        private readonly NVEntities db;
        private readonly IServicioAlbaranesVenta servicioAlbaranes;
        private readonly IServicioFacturas servicioFacturas;
        private readonly IGestorFacturas gestorFacturas;
        private readonly IServicioTraspasoEmpresa servicioTraspaso;
        private readonly IServicioNotasEntrega servicioNotasEntrega;
        private readonly IServicioExtractoRuta servicioExtractoRuta;

        public GestorFacturacionRutas(
            NVEntities db,
            IServicioAlbaranesVenta servicioAlbaranes,
            IServicioFacturas servicioFacturas,
            IGestorFacturas gestorFacturas,
            IServicioTraspasoEmpresa servicioTraspaso,
            IServicioNotasEntrega servicioNotasEntrega,
            IServicioExtractoRuta servicioExtractoRuta)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.servicioAlbaranes = servicioAlbaranes ?? throw new ArgumentNullException(nameof(servicioAlbaranes));
            this.servicioFacturas = servicioFacturas ?? throw new ArgumentNullException(nameof(servicioFacturas));
            this.gestorFacturas = gestorFacturas ?? throw new ArgumentNullException(nameof(gestorFacturas));
            this.servicioTraspaso = servicioTraspaso ?? throw new ArgumentNullException(nameof(servicioTraspaso));
            this.servicioNotasEntrega = servicioNotasEntrega ?? throw new ArgumentNullException(nameof(servicioNotasEntrega));
            this.servicioExtractoRuta = servicioExtractoRuta ?? throw new ArgumentNullException(nameof(servicioExtractoRuta));
        }

        /// <summary>
        /// Determina si se debe imprimir un documento (factura o albarán) físicamente.
        /// Busca en los comentarios del pedido (case insensitive, sin tildes):
        /// - "Factura física"
        /// - "Factura en papel"
        /// - "Albarán físico"
        /// </summary>
        public bool DebeImprimirDocumento(string comentarios)
        {
            if (string.IsNullOrWhiteSpace(comentarios))
            {
                return false;
            }

            // Normalizar: quitar tildes y convertir a minúsculas
            string comentariosNormalizados = RemoverTildes(comentarios.ToLower());

            // Lista de frases que indican que se debe imprimir
            var frasesImpresion = new[]
            {
                "factura fisica",
                "factura en papel",
                "albaran fisico"
            };

            return frasesImpresion.Any(frase => comentariosNormalizados.Contains(frase));
        }

        /// <summary>
        /// Remueve tildes de un texto para normalización.
        /// </summary>
        private string RemoverTildes(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return texto;
            }

            var normalized = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Verifica si el pedido puede ser facturado.
        /// Reproduce la lógica del procedimiento prdCrearFacturaVta:
        /// Si MantenerJunto = 1 y existen líneas con Estado < 2 (sin albarán), no se puede facturar.
        /// </summary>
        public bool PuedeFacturarPedido(CabPedidoVta pedido)
        {
            if (pedido == null)
            {
                return false;
            }

            // Si no tiene MantenerJunto, siempre se puede facturar
            if (!pedido.MantenerJunto)
            {
                return true;
            }

            // Si no tiene líneas, se puede facturar
            if (pedido.LinPedidoVtas == null || !pedido.LinPedidoVtas.Any())
            {
                return true;
            }

            // Si MantenerJunto = 1, verificar si todas las líneas tienen albarán (Estado >= 2)
            bool tieneLineasSinAlbaran = pedido.LinPedidoVtas
                .Any(l => l.Estado < Constantes.EstadosLineaVenta.ALBARAN);

            return !tieneLineasSinAlbaran;
        }

        /// <summary>
        /// Verifica si el pedido tiene todas las líneas con visto bueno (VtoBueno = true).
        /// Si alguna línea no tiene VtoBueno = true, el pedido no se puede procesar.
        /// </summary>
        public bool TieneTodasLasLineasConVistoBueno(CabPedidoVta pedido)
        {
            if (pedido == null)
            {
                return false;
            }

            // Si no tiene líneas, se considera válido
            if (pedido.LinPedidoVtas == null || !pedido.LinPedidoVtas.Any())
            {
                return true;
            }

            // Verificar que TODAS las líneas tengan VtoBueno = true
            return pedido.LinPedidoVtas.All(l => l.VtoBueno == true);
        }

        /// <summary>
        /// Obtiene las líneas del pedido que son procesables según criterios de facturación:
        /// - Tienen picking asignado (Picking > 0)
        /// - Fecha de entrega <= fechaEntregaDesde
        /// </summary>
        internal List<LinPedidoVta> ObtenerLineasProcesables(CabPedidoVta pedido, DateTime fechaEntregaDesde)
        {
            if (pedido?.LinPedidoVtas == null)
            {
                return new List<LinPedidoVta>();
            }

            return pedido.LinPedidoVtas
                .Where(l => l.Picking != null &&
                            l.Picking > 0 &&
                            l.Fecha_Entrega <= fechaEntregaDesde)
                .ToList();
        }

        /// <summary>
        /// Obtiene el número de albarán existente si TODAS las líneas procesables ya tienen albarán.
        /// Devuelve null si alguna línea procesable aún no tiene albarán.
        /// </summary>
        internal int? ObtenerNumeroAlbaranExistente(CabPedidoVta pedido, DateTime fechaEntregaDesde)
        {
            var lineasProcesables = ObtenerLineasProcesables(pedido, fechaEntregaDesde);

            // Si no hay líneas procesables, no hay albarán
            if (!lineasProcesables.Any())
            {
                return null;
            }

            // Verificar si TODAS las líneas procesables ya tienen albarán (Estado >= ALBARAN)
            bool todasTienenAlbaran = lineasProcesables.All(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN);

            if (!todasTienenAlbaran)
            {
                return null; // Hay líneas sin albarán
            }

            // Obtener el número de albarán (todas deberían tener el mismo)
            var numeroAlbaran = lineasProcesables
                .Where(l => l.Nº_Albarán.HasValue)
                .Select(l => l.Nº_Albarán.Value)
                .FirstOrDefault();

            return numeroAlbaran > 0 ? numeroAlbaran : (int?)null;
        }

        /// <summary>
        /// Procesa facturación masiva de pedidos por rutas.
        /// </summary>
        /// <param name="pedidos">Lista de pedidos a facturar</param>
        /// <param name="usuario">Usuario que realiza la facturación</param>
        /// <param name="fechaEntregaDesde">Fecha desde para filtrar líneas procesables</param>
        public async Task<FacturarRutasResponseDTO> FacturarRutas(System.Collections.Generic.List<CabPedidoVta> pedidos, string usuario, DateTime fechaEntregaDesde)
        {
            var response = new FacturarRutasResponseDTO
            {
                PedidosConErrores = new System.Collections.Generic.List<PedidoConErrorDTO>()
            };

            System.Diagnostics.Debug.WriteLine($"===== INICIO FACTURACIÓN RUTAS - {pedidos.Count} pedidos =====");

            var stopwatch = Stopwatch.StartNew();

            foreach (var pedido in pedidos)
            {
                System.Diagnostics.Debug.WriteLine($"Procesando pedido {pedido.Número} - Cliente: {pedido.Nº_Cliente} - NotaEntrega: {pedido.NotaEntrega} - Periodo: {pedido.Periodo_Facturacion}");
                try
                {
                    await ProcesarPedido(pedido, response, usuario, fechaEntregaDesde);
                    response.PedidosProcesados++;
                    System.Diagnostics.Debug.WriteLine($"  ✓ Pedido {pedido.Número} procesado correctamente");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ EXCEPCIÓN en pedido {pedido.Número}: {ex.Message}");
                    RegistrarError(pedido, "Proceso general", ex, response);

                    // CRÍTICO: Limpiar el contexto después de un error para evitar que afecte a los siguientes pedidos
                    // Si no limpiamos, las entidades modificadas por SPs pueden tener RowVersion desactualizado
                    LimpiarContextoDespuesDeError(pedido);
                }
            }

            stopwatch.Stop();
            response.TiempoTotal = stopwatch.Elapsed;

            // RESUMEN FINAL
            System.Diagnostics.Debug.WriteLine($"===== FIN FACTURACIÓN RUTAS =====");
            System.Diagnostics.Debug.WriteLine($"Pedidos procesados: {response.PedidosProcesados}");
            System.Diagnostics.Debug.WriteLine($"Albaranes creados: {response.AlbaranesCreados}");
            System.Diagnostics.Debug.WriteLine($"Facturas creadas: {response.FacturasCreadas}");
            System.Diagnostics.Debug.WriteLine($"Notas de entrega: {response.NotasEntregaCreadas}");
            System.Diagnostics.Debug.WriteLine($"ERRORES: {response.PedidosConErrores.Count}");
            if (response.PedidosConErrores.Any())
            {
                foreach (var error in response.PedidosConErrores)
                {
                    System.Diagnostics.Debug.WriteLine($"  - Pedido {error.NumeroPedido}: [{error.TipoError}] {error.MensajeError}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"Tiempo total: {response.TiempoTotal.TotalSeconds:F2} segundos");

            return response;
        }

        /// <summary>
        /// Procesa un pedido individual: crea albarán (si no existe), traspasa si necesario, crea factura y genera PDFs si corresponde.
        /// Si es nota de entrega, solo procesa la nota de entrega (no crea albarán ni factura).
        /// Si las líneas procesables ya tienen albarán, reutiliza el existente y continúa con facturación si es NRM.
        /// </summary>
        private async Task ProcesarPedido(
            CabPedidoVta pedido,
            FacturarRutasResponseDTO response,
            string usuario,
            DateTime fechaEntregaDesde)
        {
            // 0. Validar visto bueno ANTES de cualquier procesamiento
            if (!TieneTodasLasLineasConVistoBueno(pedido))
            {
                RegistrarError(pedido, "Visto Bueno",
                    "El pedido tiene líneas sin visto bueno y no puede ser procesado",
                    response);
                return; // No procesar nada
            }

            // 1. Si es nota de entrega, procesarla y RETORNAR (no crear albarán ni factura)
            if (pedido.NotaEntrega == true)
            {
                System.Diagnostics.Debug.WriteLine($"  → Procesando NOTA DE ENTREGA para pedido {pedido.Número}");
                try
                {
                    var notaEntrega = await servicioNotasEntrega.ProcesarNotaEntrega(pedido, usuario);
                    System.Diagnostics.Debug.WriteLine($"  → Nota de entrega procesada: {notaEntrega?.NumeroLineas ?? 0} líneas");

                    if (notaEntrega != null)
                    {
                        // Determinar tipo de ruta y si debe generar PDF
                        var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
                        bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

                        // Obtener número de copias según el tipo de ruta
                        int numeroCopias = tipoRuta != null
                            ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                            : 0;

                        // Si debe imprimir (numeroCopias > 0), generar PDF
                        if (numeroCopias > 0)
                        {
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"  → Generando PDF de nota de entrega ({numeroCopias} copias)");
                                notaEntrega.DatosImpresion = GenerarDatosImpresionNotaEntrega(pedido, pedido.Empresa, pedido.Número, usuario);
                            }
                            catch (Exception exPdf)
                            {
                                System.Diagnostics.Debug.WriteLine($"  ✗ ERROR al generar PDF de nota de entrega: {exPdf.Message}");
                                RegistrarError(pedido, "Generación PDF Nota de Entrega", exPdf, response);
                            }
                        }

                        response.NotasEntrega.Add(notaEntrega);
                        System.Diagnostics.Debug.WriteLine($"  → Nota de entrega agregada al response correctamente");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  ⚠ ADVERTENCIA: ProcesarNotaEntrega retornó null");
                        RegistrarError(pedido, "Nota de Entrega", "El servicio retornó null", response);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ ERROR en nota de entrega: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"     InnerException: {ex.InnerException.Message}");
                    }
                    RegistrarError(pedido, "Nota de Entrega", ex, response);
                }
                return; // IMPORTANTE: No continuar con la creación de albarán/factura
            }

            // 2. Verificar si ya existe albarán o crearlo
            int numeroAlbaran;
            bool albaranYaExistia = false;
            var numeroAlbaranExistente = ObtenerNumeroAlbaranExistente(pedido, fechaEntregaDesde);

            if (numeroAlbaranExistente.HasValue)
            {
                // Ya tiene albarán - reutilizarlo
                numeroAlbaran = numeroAlbaranExistente.Value;
                albaranYaExistia = true;
                System.Diagnostics.Debug.WriteLine($"  → Albarán {numeroAlbaran} ya existe (todas las líneas procesables están albaranadas)");

                // CRÍTICO: Recargar líneas para asegurar estados actualizados
                if (pedido.LinPedidoVtas != null && pedido.LinPedidoVtas.Any())
                {
                    foreach (var linea in pedido.LinPedidoVtas)
                    {
                        await db.Entry(linea).ReloadAsync();
                    }
                }
            }
            else
            {
                // Crear nuevo albarán
                try
                {
                    System.Diagnostics.Debug.WriteLine($"  → Creando ALBARÁN para pedido {pedido.Número}");
                    numeroAlbaran = await servicioAlbaranes.CrearAlbaran(
                        pedido.Empresa,
                        pedido.Número,
                        usuario);

                    System.Diagnostics.Debug.WriteLine($"  → Albarán {numeroAlbaran} creado correctamente");

                    // CRÍTICO: Recargar las líneas del pedido desde la BD
                    // El procedimiento prdCrearAlbaránVta actualiza el Estado de las líneas en la BD,
                    // pero el objeto pedido en memoria NO se actualiza automáticamente.
                    // LoadAsync() NO refresca entidades ya tracked, por lo que usamos Reload() en cada línea.
                    System.Diagnostics.Debug.WriteLine($"  → Recargando líneas del pedido desde BD para obtener estados actualizados");

                    if (pedido.LinPedidoVtas != null && pedido.LinPedidoVtas.Any())
                    {
                        // IMPORTANTE: Reload() fuerza a EF a descartar los valores en memoria y recargar desde BD
                        foreach (var linea in pedido.LinPedidoVtas)
                        {
                            await db.Entry(linea).ReloadAsync();
                        }
                        System.Diagnostics.Debug.WriteLine($"  → Líneas recargadas. Estados actuales: {string.Join(", ", pedido.LinPedidoVtas.Select(l => $"Línea {l.Nº_Orden}={l.Estado}"))}");
                    }

                    // Agregar albarán creado al response (sin datos de impresión por ahora)
                    var albaranCreado = CrearAlbaranCreadoDTO(pedido, numeroAlbaran);
                    response.Albaranes.Add(albaranCreado);

                    // Insertar en ExtractoRuta desde albarán SOLO si:
                    // 1. El tipo de ruta lo requiere (Ruta Propia SÍ, Agencia NO)
                    // 2. NO es NRM (porque NRM insertará desde factura)
                    var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
                    bool esNRM = pedido.Periodo_Facturacion?.Trim() == Constantes.Pedidos.PERIODO_FACTURACION_NORMAL;

                    if (tipoRuta?.DebeInsertarEnExtractoRuta() == true && !esNRM)
                    {
                        System.Diagnostics.Debug.WriteLine($"  → Insertando en ExtractoRuta desde albarán (FDM)");
                        await servicioExtractoRuta.InsertarDesdeAlbaran(pedido, numeroAlbaran, usuario, autoSave: false);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ ERROR al crear albarán: {ex.Message}");
                    RegistrarError(pedido, "Albarán", ex, response);
                    return; // No continuar si falla el albarán
                }
            }

            // 3. Verificar si hay que traspasar a empresa destino
            if (servicioTraspaso.HayQueTraspasar(pedido))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"  → Traspasando pedido {pedido.Número} a empresa espejo");

                    // El traspaso maneja su propia transacción y hace SaveChanges internamente
                    // El ExtractoRuta del albarán se guarda dentro de la transacción del traspaso
                    await servicioTraspaso.TraspasarPedidoAEmpresa(
                        pedido,
                        Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO,
                        usuario);

                    System.Diagnostics.Debug.WriteLine($"  ✓ Traspaso completado exitosamente");

                    // IMPORTANTE: Después del traspaso, el objeto 'pedido' quedó Detached
                    // Necesitamos recargarlo desde BD porque cambió de empresa (PK modificada)
                    System.Diagnostics.Debug.WriteLine($"  → Recargando pedido desde BD después del traspaso");

                    var pedidoRecargado = await db.CabPedidoVtas
                        .Include(p => p.LinPedidoVtas)
                        .FirstOrDefaultAsync(p =>
                            p.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO &&
                            p.Número == pedido.Número);

                    if (pedidoRecargado == null)
                    {
                        throw new InvalidOperationException(
                            $"No se pudo recargar el pedido {pedido.Número} después del traspaso");
                    }

                    // Reemplazar la referencia al pedido para seguir trabajando con el recargado
                    pedido = pedidoRecargado;
                    System.Diagnostics.Debug.WriteLine($"  ✓ Pedido recargado con empresa {pedido.Empresa}");
                }
                catch (Exception ex)
                {
                    RegistrarError(pedido, "Traspaso", ex, response);
                    return; // Si falla el traspaso, no continuar
                }
            }
            else
            {
                // Si NO hay traspaso, guardar los cambios pendientes del ExtractoRuta
                await db.SaveChangesAsync();
            }

            // 4. Si es NRM, crear factura (si es posible)
            if (pedido.Periodo_Facturacion?.Trim() == Constantes.Pedidos.PERIODO_FACTURACION_NORMAL)
            {
                await ProcesarFacturaNRM(pedido, numeroAlbaran, response, usuario);
            }
            // 5. Si es FDM, generar PDF del albarán si tiene comentario de impresión
            else if (pedido.Periodo_Facturacion?.Trim() == Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES)
            {
                AgregarDatosImpresionAlbaranSiCorresponde(pedido, numeroAlbaran, response, usuario);
            }
        }

        /// <summary>
        /// Procesa facturación para pedidos NRM (Normal).
        /// </summary>
        private async Task ProcesarFacturaNRM(
            CabPedidoVta pedido,
            int numeroAlbaran,
            FacturarRutasResponseDTO response,
            string usuario)
        {
            // IMPORTANTE: Después de crear el albarán, el estado de las líneas puede haber cambiado.
            // Necesitamos validar de nuevo si ahora SÍ se puede facturar.
            // Esto es crucial para pedidos con MantenerJunto=1 donde el albarán completó todas las líneas.

            if (!PuedeFacturarPedido(pedido))
            {
                // No se puede facturar porque MantenerJunto = 1 y hay líneas sin albarán
                // Es un WARNING porque el albarán SÍ se creó, solo la factura queda pendiente
                var lineasSinAlbaran = pedido.LinPedidoVtas?
                    .Count(l => l.Estado < Constantes.EstadosLineaVenta.ALBARAN) ?? 0;

                RegistrarError(pedido, "Factura",
                    $"No se puede facturar porque tiene MantenerJunto=1 y hay {lineasSinAlbaran} línea(s) sin albarán. " +
                    $"Se ha creado el albarán {numeroAlbaran} pero la factura queda pendiente hasta que todas las líneas tengan albarán.",
                    response,
                    NivelSeveridad.Warning);

                // En este caso, si tiene comentario de impresión, generar PDF del ALBARÁN
                if (DebeImprimirDocumento(pedido.Comentarios))
                {
                    try
                    {
                        AgregarDatosImpresionAlbaranSiCorresponde(pedido, numeroAlbaran, response, usuario);
                    }
                    catch (Exception ex)
                    {
                        RegistrarError(pedido, "Generación PDF Albarán", ex, response);
                    }
                }
                return; // No intentar crear factura
            }

            // Si llegamos aquí, se puede facturar (ya sea porque no tiene MantenerJunto,
            // o porque después de crear el albarán todas las líneas tienen Estado >= 2)

            try
            {
                // Crear factura (el auto-fix de descuadre se maneja internamente en ServicioFacturas)
                System.Diagnostics.Debug.WriteLine($"  → Creando FACTURA para pedido {pedido.Número}");
                var resultadoFactura = await servicioFacturas.CrearFactura(
                    pedido.Empresa,
                    pedido.Número,
                    usuario);

                System.Diagnostics.Debug.WriteLine($"  → Factura {resultadoFactura.NumeroFactura} creada correctamente");

                // Agregar factura creada al response
                var facturaCreada = CrearFacturaCreadaDTO(pedido, resultadoFactura.NumeroFactura);
                response.Facturas.Add(facturaCreada);

                // Insertar en ExtractoRuta desde factura SOLO si el tipo de ruta lo requiere (Ruta Propia SÍ, Agencia NO)
                // Para NRM, el ExtractoRuta se inserta desde la factura (no desde el albarán)
                var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
                if (tipoRuta?.DebeInsertarEnExtractoRuta() == true)
                {
                    System.Diagnostics.Debug.WriteLine($"  → Insertando en ExtractoRuta desde factura (NRM)");
                    // IMPORTANTE: Pasar resultadoFactura.Empresa porque puede ser diferente a pedido.Empresa
                    // cuando hay traspaso a empresa espejo (ej: factura GB en empresa 3, pedido en empresa 1)
                    await servicioExtractoRuta.InsertarDesdeFactura(pedido, resultadoFactura.NumeroFactura, usuario, resultadoFactura.Empresa, autoSave: true);
                }

                // Determinar si debe generar PDF según el tipo de ruta
                bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);
                int numeroCopias = tipoRuta != null
                    ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                    : 0;

                // Si debe imprimirse (numeroCopias > 0), generar bytes del PDF
                if (numeroCopias > 0)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"  → Generando PDF de factura ({numeroCopias} copias)");
                        // IMPORTANTE: Usar resultadoFactura.Empresa porque puede ser diferente a pedido.Empresa
                        // cuando hay traspaso a empresa espejo (ej: factura GB en empresa 3, pedido en empresa 1)
                        facturaCreada.DatosImpresion = GenerarDatosImpresionFactura(pedido, resultadoFactura.Empresa, resultadoFactura.NumeroFactura, usuario);
                    }
                    catch (Exception exPdf)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ✗ ERROR al generar PDF de factura: {exPdf.Message}");
                        RegistrarError(pedido, "Generación PDF Factura", exPdf, response);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"  ✗ ERROR al crear factura: {ex.Message}");
                RegistrarError(pedido, "Factura", ex, response);

                // Si falla la factura pero tiene comentario, generar PDF del albarán
                if (DebeImprimirDocumento(pedido.Comentarios))
                {
                    try
                    {
                        AgregarDatosImpresionAlbaranSiCorresponde(pedido, numeroAlbaran, response, usuario);
                    }
                    catch (Exception exImpresion)
                    {
                        RegistrarError(pedido, "Generación PDF Albarán", exImpresion, response);
                    }
                }
            }
        }

        /// <summary>
        /// Agrega datos de impresión al albarán si corresponde según el tipo de ruta.
        /// Usado para pedidos FDM (Fin de Mes) y cuando falla la factura en NRM.
        /// </summary>
        private void AgregarDatosImpresionAlbaranSiCorresponde(
            CabPedidoVta pedido,
            int numeroAlbaran,
            FacturarRutasResponseDTO response,
            string usuario)
        {
            // Determinar si debe generar PDF según el tipo de ruta
            var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);
            int numeroCopias = tipoRuta != null
                ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : 0;

            // Solo generar PDF si numeroCopias > 0
            if (numeroCopias == 0)
            {
                return;
            }

            // Buscar el albarán en la lista y agregarle los datos de impresión
            var albaran = response.Albaranes.FirstOrDefault(a =>
                a.Empresa == pedido.Empresa &&
                a.NumeroAlbaran == numeroAlbaran);

            if (albaran != null)
            {
                System.Diagnostics.Debug.WriteLine($"  → Generando PDF de albarán ({numeroCopias} copias)");
                albaran.DatosImpresion = GenerarDatosImpresionAlbaran(pedido, pedido.Empresa, numeroAlbaran, usuario);
            }
        }

        /// <summary>
        /// Genera los datos de impresión para un albarán (bytes del PDF, copias, bandeja).
        /// </summary>
        private DocumentoParaImprimir GenerarDatosImpresionAlbaran(CabPedidoVta pedido, string empresa, int numeroAlbaran, string usuario)
        {
            var lookup = new FacturaLookup { Empresa = empresa, Factura = numeroAlbaran.ToString() };
            var lista = new List<FacturaLookup> { lookup };
            var albaranes = gestorFacturas.LeerAlbaranes(lista);

            var bytesPdf = gestorFacturas.FacturasEnPDF(albaranes, papelConMembrete: true, usuario: usuario);

            // Determinar tipo de ruta y obtener configuración de impresión
            var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

            // Si la ruta no está manejada por ningún tipo, no imprimir
            int numeroCopias = tipoRuta != null
                ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : 0;

            TipoBandejaImpresion tipoBandeja = tipoRuta != null
                ? tipoRuta.ObtenerBandeja(pedido, esFactura: false, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : TipoBandejaImpresion.Middle;

            return new DocumentoParaImprimir
            {
                BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
                NumeroCopias = numeroCopias,
                TipoBandeja = tipoBandeja
            };
        }

        /// <summary>
        /// Genera los datos de impresión para una factura (bytes del PDF, copias, bandeja).
        /// </summary>
        private DocumentoParaImprimir GenerarDatosImpresionFactura(CabPedidoVta pedido, string empresa, string numeroFactura, string usuario)
        {
            var factura = gestorFacturas.LeerFactura(empresa, numeroFactura);
            var facturas = new List<Factura> { factura };

            var bytesPdf = gestorFacturas.FacturasEnPDF(facturas, papelConMembrete: true, usuario: usuario);

            // Determinar tipo de ruta y obtener configuración de impresión
            var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

            // Si la ruta no está manejada por ningún tipo, no imprimir
            int numeroCopias = tipoRuta != null
                ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : 0;

            TipoBandejaImpresion tipoBandeja = tipoRuta != null
                ? tipoRuta.ObtenerBandeja(pedido, esFactura: true, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : TipoBandejaImpresion.Middle;

            return new DocumentoParaImprimir
            {
                BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
                NumeroCopias = numeroCopias,
                TipoBandeja = tipoBandeja
            };
        }

        /// <summary>
        /// Genera los datos de impresión para una nota de entrega (bytes del PDF, copias, bandeja).
        /// Usa el formato de pedido para generar el PDF de la nota de entrega.
        /// </summary>
        private DocumentoParaImprimir GenerarDatosImpresionNotaEntrega(CabPedidoVta pedido, string empresa, int numeroPedido, string usuario)
        {
            var pedidoFactura = gestorFacturas.LeerPedido(empresa, numeroPedido);
            var pedidos = new List<Factura> { pedidoFactura };

            var bytesPdf = gestorFacturas.FacturasEnPDF(pedidos, papelConMembrete: true, usuario: usuario);

            // Determinar tipo de ruta y obtener configuración de impresión
            var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

            // Si la ruta no está manejada por ningún tipo, no imprimir
            int numeroCopias = tipoRuta != null
                ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : 0;

            TipoBandejaImpresion tipoBandeja = tipoRuta != null
                ? tipoRuta.ObtenerBandeja(pedido, esFactura: false, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : TipoBandejaImpresion.Middle;

            return new DocumentoParaImprimir
            {
                BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
                NumeroCopias = numeroCopias,
                TipoBandeja = tipoBandeja
            };
        }

        /// <summary>
        /// Crea un DTO de albarán creado con datos del pedido.
        /// </summary>
        private AlbaranCreadoDTO CrearAlbaranCreadoDTO(CabPedidoVta pedido, int numeroAlbaran)
        {
            var cliente = db.Clientes.FirstOrDefault(c =>
                c.Empresa == pedido.Empresa &&
                c.Nº_Cliente == pedido.Nº_Cliente &&
                c.Contacto == pedido.Contacto);

            return new AlbaranCreadoDTO
            {
                Empresa = pedido.Empresa,
                NumeroAlbaran = numeroAlbaran,
                NumeroPedido = pedido.Número,
                Cliente = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                NombreCliente = cliente?.Nombre?.Trim() ?? "Desconocido",
                DatosImpresion = null // Se rellena después si corresponde
            };
        }

        /// <summary>
        /// Crea un DTO de factura creada con datos del pedido.
        /// </summary>
        private FacturaCreadaDTO CrearFacturaCreadaDTO(CabPedidoVta pedido, string numeroFactura)
        {
            var cliente = db.Clientes.FirstOrDefault(c =>
                c.Empresa == pedido.Empresa &&
                c.Nº_Cliente == pedido.Nº_Cliente &&
                c.Contacto == pedido.Contacto);

            // Obtener serie de la factura (típicamente extraída del número de factura)
            var serie = ObtenerSerieDeFactura(numeroFactura);

            return new FacturaCreadaDTO
            {
                Empresa = pedido.Empresa,
                NumeroFactura = numeroFactura,
                Serie = serie,
                NumeroPedido = pedido.Número,
                Cliente = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                NombreCliente = cliente?.Nombre?.Trim() ?? "Desconocido",
                DatosImpresion = null // Se rellena después si corresponde
            };
        }

        /// <summary>
        /// Extrae la serie del número de factura.
        /// Ejemplo: "A25/123" -> "A25"
        /// </summary>
        private string ObtenerSerieDeFactura(string numeroFactura)
        {
            if (string.IsNullOrEmpty(numeroFactura))
            {
                return string.Empty;
            }

            var partes = numeroFactura.Split('/');
            return partes.Length > 0 ? partes[0] : string.Empty;
        }

        /// <summary>
        /// Genera un preview (simulación) de facturación de rutas SIN crear nada en la BD.
        /// Calcula qué albaranes, facturas y notas de entrega se crearían y sus importes.
        /// Solo cuenta las líneas que realmente se procesarán según los filtros de facturación.
        /// Solo incluye pedidos que tengan todas las líneas con visto bueno.
        /// </summary>
        public PreviewFacturacionRutasResponseDTO PreviewFacturarRutas(List<CabPedidoVta> pedidos, DateTime fechaEntregaDesde)
        {
            var preview = new PreviewFacturacionRutasResponseDTO();

            if (pedidos == null || !pedidos.Any())
            {
                return preview;
            }

            foreach (var pedido in pedidos)
            {
                // Validar visto bueno - si no pasa, no procesar este pedido
                if (!TieneTodasLasLineasConVistoBueno(pedido))
                {
                    continue; // Saltar este pedido
                }

                // Incrementar contador de pedidos procesados
                preview.NumeroPedidos++;

                // Calcular base imponible SOLO de las líneas que se van a procesar
                // Incluimos EN_CURSO (sin albarán) y ALBARAN (para re-facturación NRM)
                decimal baseImponible = pedido.LinPedidoVtas?
                    .Where(l => (l.Estado == Constantes.EstadosLineaVenta.EN_CURSO ||
                                 l.Estado == Constantes.EstadosLineaVenta.ALBARAN) &&
                                l.Picking != null &&
                                l.Picking > 0 &&
                                l.Fecha_Entrega <= fechaEntregaDesde)
                    .Sum(l => l.Base_Imponible) ?? 0;

                // Determinar qué se crearía para este pedido
                bool esNotaEntrega = pedido.NotaEntrega == true;
                bool esNRM = pedido.Periodo_Facturacion?.Trim() == Constantes.Pedidos.PERIODO_FACTURACION_NORMAL;
                bool puedeFacturar = PuedeFacturarPedido(pedido);

                bool creaAlbaran = !esNotaEntrega;  // Si NO es nota de entrega, se crea albarán
                bool creaFactura = !esNotaEntrega && esNRM && puedeFacturar;
                bool creaNotaEntrega = esNotaEntrega;

                // Acumular contadores y bases imponibles
                // IMPORTANTE: Si se crea factura, NO contar en albaranes (evitar doble contabilización)
                if (creaFactura)
                {
                    // Si se crea factura, solo contar en facturas
                    preview.NumeroFacturas++;
                    preview.BaseImponibleFacturas += baseImponible;
                }
                else if (creaAlbaran)
                {
                    // Si solo se crea albarán (sin factura), contar en albaranes
                    preview.NumeroAlbaranes++;
                    preview.BaseImponibleAlbaranes += baseImponible;
                }
                else if (creaNotaEntrega)
                {
                    // Si es nota de entrega, contar en notas de entrega
                    preview.NumeroNotasEntrega++;
                    preview.BaseImponibleNotasEntrega += baseImponible;
                }

                // Agregar a muestra (primeros 20 pedidos)
                if (preview.PedidosMuestra.Count < 20)
                {
                    var cliente = db.Clientes.FirstOrDefault(c =>
                        c.Empresa == pedido.Empresa &&
                        c.Nº_Cliente == pedido.Nº_Cliente &&
                        c.Contacto == pedido.Contacto);

                    preview.PedidosMuestra.Add(new PedidoPreviewDTO
                    {
                        NumeroPedido = pedido.Número,
                        Cliente = pedido.Nº_Cliente,
                        Contacto = pedido.Contacto,
                        NombreCliente = cliente?.Nombre?.Trim() ?? "Desconocido",
                        PeriodoFacturacion = pedido.Periodo_Facturacion?.Trim(),
                        BaseImponible = baseImponible,
                        CreaAlbaran = creaAlbaran && !creaFactura, // Solo mostrar albarán si NO se crea factura
                        CreaFactura = creaFactura,
                        CreaNotaEntrega = creaNotaEntrega,
                        Comentarios = pedido.Comentarios?.Trim()
                    });
                }
            }

            return preview;
        }

        /// <summary>
        /// Registra un error en el response.
        /// ROBUSTO: No falla aunque la BD esté en estado de error.
        /// </summary>
        private void RegistrarError(
            CabPedidoVta pedido,
            string tipoError,
            string mensajeError,
            FacturarRutasResponseDTO response,
            NivelSeveridad severidad = NivelSeveridad.Error)
        {
            string nombreCliente = "Desconocido";
            try
            {
                // Intentar obtener el nombre del cliente, pero no fallar si la BD está en mal estado
                var cliente = db.Clientes.AsNoTracking().FirstOrDefault(c =>
                    c.Empresa == pedido.Empresa &&
                    c.Nº_Cliente == pedido.Nº_Cliente &&
                    c.Contacto == pedido.Contacto);
                nombreCliente = cliente?.Nombre?.Trim() ?? "Desconocido";
            }
            catch
            {
                // Si falla la query del cliente, usar valor por defecto
                nombreCliente = "Desconocido (error al consultar)";
            }

            // Obtener la fecha de entrega mínima de las líneas del pedido
            DateTime? fechaEntrega = null;
            decimal total = 0;
            try
            {
                fechaEntrega = pedido.LinPedidoVtas?.Min(l => l.Fecha_Entrega);
                total = pedido.LinPedidoVtas?.Sum(l => l.Total) ?? 0;
            }
            catch
            {
                // Si falla el cálculo, usar valores por defecto
                fechaEntrega = DateTime.Today;
                total = 0;
            }

            try
            {
                // Agregar el error al response
                response.PedidosConErrores.Add(new PedidoConErrorDTO
                {
                    Empresa = pedido.Empresa,
                    NumeroPedido = pedido.Número,
                    Cliente = pedido.Nº_Cliente,
                    Contacto = pedido.Contacto,
                    NombreCliente = nombreCliente,
                    Ruta = pedido.Ruta,
                    PeriodoFacturacion = pedido.Periodo_Facturacion,
                    TipoError = tipoError,
                    MensajeError = mensajeError,
                    Severidad = severidad,
                    FechaEntrega = fechaEntrega ?? DateTime.Today,
                    Total = total
                });

                // Escribir en Debug con el nivel de severidad
                var prefijo = severidad == NivelSeveridad.Warning ? "WARNING" : severidad == NivelSeveridad.Info ? "INFO" : "ERROR";
                System.Diagnostics.Debug.WriteLine($"{prefijo} REGISTRADO - Pedido {pedido.Número}: [{tipoError}] {mensajeError}");
            }
            catch (Exception ex)
            {
                // Último recurso: Si incluso agregar el error falla, escribir en Debug
                System.Diagnostics.Debug.WriteLine($"FALLO AL REGISTRAR ERROR - Pedido {pedido?.Número}: {ex.Message}");
            }
        }

        /// <summary>
        /// Registra un error en el response con detalles completos de la excepción.
        /// Si el error es de descuadre, añade información del ValidadorDescuentoPP.
        /// Detecta la severidad por:
        /// 1. Prefijo [WARNING] en el mensaje
        /// 2. State de SqlException (state=2 → Warning)
        /// 3. Severity de SqlException (menor a 11 → Warning)
        /// </summary>
        private void RegistrarError(
            CabPedidoVta pedido,
            string tipoError,
            Exception ex,
            FacturarRutasResponseDTO response)
        {
            // Construir mensaje completo con InnerException
            var mensajeCompleto = ex.Message;
            if (ex.InnerException != null)
            {
                mensajeCompleto += " | Inner: " + ex.InnerException.Message;
                if (ex.InnerException.InnerException != null)
                {
                    mensajeCompleto += " | Inner2: " + ex.InnerException.InnerException.Message;
                }
            }

            // Detectar severidad (combina prefijo, state y severity de SQL)
            var severidad = DetectarSeveridad(ex, ref mensajeCompleto);

            RegistrarError(pedido, tipoError, mensajeCompleto, response, severidad);

            // Si es un error de descuadre, añadir información del validador de descuento PP
            if (EsErrorDescuadre(mensajeCompleto))
            {
                AgregarInfoDescuentoPP(pedido, response);
            }
        }

        /// <summary>
        /// Detecta la severidad combinando múltiples métodos:
        /// 1. Prefijo [WARNING] o [INFO] en el mensaje
        /// 2. State de SqlException (state=2 → Warning, state=3 → Info)
        /// 3. Severity/Class de SqlException (menor a 11 → Warning)
        /// </summary>
        /// <param name="ex">Excepción a analizar</param>
        /// <param name="mensaje">Mensaje a analizar (se modifica para quitar el prefijo si existe)</param>
        /// <returns>NivelSeveridad detectado</returns>
        internal static NivelSeveridad DetectarSeveridad(Exception ex, ref string mensaje)
        {
            // 1. Primero verificar prefijo en el mensaje (tiene prioridad)
            var severidadPorPrefijo = DetectarSeveridadPorPrefijo(ref mensaje);
            if (severidadPorPrefijo != NivelSeveridad.Error)
            {
                return severidadPorPrefijo;
            }

            // 2. Verificar SqlException (buscar en toda la cadena de excepciones)
            var sqlEx = EncontrarSqlException(ex);
            if (sqlEx != null)
            {
                foreach (System.Data.SqlClient.SqlError error in sqlEx.Errors)
                {
                    // State = 2 → Warning (convención personalizada)
                    if (error.State == 2)
                    {
                        return NivelSeveridad.Warning;
                    }
                    // State = 3 → Info (convención personalizada)
                    if (error.State == 3)
                    {
                        return NivelSeveridad.Info;
                    }
                    // Severity/Class < 11 → Warning (mensajes informativos de SQL Server)
                    if (error.Class < 11)
                    {
                        return NivelSeveridad.Warning;
                    }
                }
            }

            // 3. Por defecto es Error
            return NivelSeveridad.Error;
        }

        /// <summary>
        /// Busca una SqlException en la cadena de excepciones.
        /// </summary>
        private static System.Data.SqlClient.SqlException EncontrarSqlException(Exception ex)
        {
            var current = ex;
            while (current != null)
            {
                if (current is System.Data.SqlClient.SqlException sqlEx)
                {
                    return sqlEx;
                }
                current = current.InnerException;
            }
            return null;
        }

        /// <summary>
        /// Detecta la severidad por prefijo en el mensaje ([WARNING], [INFO]).
        /// Si encuentra un prefijo, lo elimina del mensaje y devuelve la severidad correspondiente.
        /// Por defecto devuelve Error si no hay prefijo.
        /// </summary>
        /// <param name="mensaje">Mensaje a analizar (se modifica para quitar el prefijo)</param>
        /// <returns>NivelSeveridad detectado</returns>
        internal static NivelSeveridad DetectarSeveridadPorPrefijo(ref string mensaje)
        {
            if (string.IsNullOrEmpty(mensaje))
            {
                return NivelSeveridad.Error;
            }

            // Detectar [WARNING]
            if (mensaje.StartsWith("[WARNING]", StringComparison.OrdinalIgnoreCase))
            {
                mensaje = mensaje.Substring("[WARNING]".Length).TrimStart();
                return NivelSeveridad.Warning;
            }

            // Detectar [INFO]
            if (mensaje.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase))
            {
                mensaje = mensaje.Substring("[INFO]".Length).TrimStart();
                return NivelSeveridad.Info;
            }

            // Por defecto es Error
            return NivelSeveridad.Error;
        }

        /// <summary>
        /// Detecta si el mensaje de error indica un problema de descuadre.
        /// </summary>
        private bool EsErrorDescuadre(string mensajeError)
        {
            if (string.IsNullOrEmpty(mensajeError))
            {
                return false;
            }

            var mensajeLower = mensajeError.ToLower();
            return mensajeLower.Contains("descuadre") ||
                   mensajeLower.Contains("cuadre") ||
                   mensajeLower.Contains("diferencia") && mensajeLower.Contains("total");
        }

        /// <summary>
        /// Agrega información detallada del pedido para diagnosticar descuadres.
        /// Captura los valores de BD vs los calculados para identificar el origen del problema.
        /// </summary>
        private void AgregarInfoDescuentoPP(CabPedidoVta pedido, FacturarRutasResponseDTO response)
        {
            try
            {
                var infoBuilder = new StringBuilder();
                infoBuilder.AppendLine($"=== DIAGNÓSTICO DESCUADRE (Issues #242/#243) ===");
                infoBuilder.AppendLine($"Fecha/Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                infoBuilder.AppendLine($"Modo redondeo NestoAPI: {(RoundingHelper.UsarAwayFromZero ? "AwayFromZero" : "ToEven (VB6)")}");
                infoBuilder.AppendLine();

                // Datos del pedido
                infoBuilder.AppendLine($"--- DATOS DEL PEDIDO ---");
                infoBuilder.AppendLine($"Pedido: {pedido.Empresa}/{pedido.Número}");
                infoBuilder.AppendLine($"Cliente: {pedido.Nº_Cliente} - {pedido.Contacto}");
                infoBuilder.AppendLine($"Plazo pago: {pedido.PlazosPago}");
                infoBuilder.AppendLine($"Origen: {pedido.Origen ?? "NULL"}");
                infoBuilder.AppendLine($"Fecha pedido: {pedido.Fecha:yyyy-MM-dd}");
                infoBuilder.AppendLine();

                // Totales de las líneas en BD (lo que usa el procedimiento almacenado)
                infoBuilder.AppendLine($"--- LÍNEAS EN BASE DE DATOS (LinPedidoVta) ---");
                decimal sumaBrutoBD = 0;
                decimal sumaBaseImponibleBD = 0;
                decimal sumaTotalBD = 0;
                decimal sumaImporteIvaBD = 0;
                decimal sumaImporteReBD = 0;

                if (pedido.LinPedidoVtas != null && pedido.LinPedidoVtas.Any())
                {
                    int numLinea = 0;
                    foreach (var linea in pedido.LinPedidoVtas.OrderBy(l => l.Nº_Orden))
                    {
                        numLinea++;
                        sumaBrutoBD += linea.Bruto;
                        sumaBaseImponibleBD += linea.Base_Imponible;
                        sumaTotalBD += linea.Total;
                        sumaImporteIvaBD += linea.ImporteIVA;
                        sumaImporteReBD += linea.ImporteRE;

                        // Mostrar detalle de cada línea
                        infoBuilder.AppendLine($"  Línea {numLinea}: Prod={linea.Producto?.Trim()}, Cant={linea.Cantidad}, " +
                            $"Precio={linea.Precio:F4}, Bruto={linea.Bruto:F2}, " +
                            $"DtoCli={linea.DescuentoCliente:P2}, DtoProd={linea.DescuentoProducto:P2}, " +
                            $"DtoLin={linea.Descuento:P2}, DtoPP={linea.DescuentoPP:P2}, " +
                            $"Base={linea.Base_Imponible:F2}, IVA%={linea.PorcentajeIVA}, " +
                            $"ImpIVA={linea.ImporteIVA:F2}, Total={linea.Total:F2}");
                    }
                    infoBuilder.AppendLine();
                    infoBuilder.AppendLine($"  SUMA BD -> Bruto={sumaBrutoBD:F2}, Base={sumaBaseImponibleBD:F2}, " +
                        $"IVA={sumaImporteIvaBD:F2}, RE={sumaImporteReBD:F2}, Total={sumaTotalBD:F2}");
                }
                else
                {
                    infoBuilder.AppendLine($"  (Sin líneas cargadas)");
                }
                infoBuilder.AppendLine();

                // Comparación con cálculo desde NestoAPI
                infoBuilder.AppendLine($"--- CÁLCULO DESDE NESTOAPI (recalculado) ---");
                var pedidoDTO = ConvertirADTO(pedido);
                if (pedidoDTO != null && pedidoDTO.Lineas.Any())
                {
                    decimal sumaBaseDTO = pedidoDTO.Lineas.Sum(l => l.BaseImponible);
                    decimal sumaTotalDTO = pedidoDTO.Lineas.Sum(l => l.Total);
                    infoBuilder.AppendLine($"  Base (suma líneas): {sumaBaseDTO:F2}");
                    infoBuilder.AppendLine($"  Total (suma líneas): {sumaTotalDTO:F2}");
                    infoBuilder.AppendLine($"  Base (PedidoBase.BaseImponible): {pedidoDTO.BaseImponible:F2}");
                    infoBuilder.AppendLine($"  Total (PedidoBase.Total): {pedidoDTO.Total:F2}");
                    infoBuilder.AppendLine();

                    // Diferencias
                    infoBuilder.AppendLine($"--- DIFERENCIAS ---");
                    infoBuilder.AppendLine($"  Base BD vs DTO: {sumaBaseImponibleBD:F2} vs {sumaBaseDTO:F2} = {(sumaBaseImponibleBD - sumaBaseDTO):F4}");
                    infoBuilder.AppendLine($"  Total BD vs DTO: {sumaTotalBD:F2} vs {sumaTotalDTO:F2} = {(sumaTotalBD - sumaTotalDTO):F4}");
                }
                infoBuilder.AppendLine();

                // Agrupación por IVA (como hace la vista vstContabilizarFacturaVta)
                infoBuilder.AppendLine($"--- AGRUPACIÓN POR IVA (como vstContabilizarFacturaVta) ---");
                if (pedido.LinPedidoVtas != null && pedido.LinPedidoVtas.Any())
                {
                    var gruposIva = pedido.LinPedidoVtas
                        .GroupBy(l => new { l.PorcentajeIVA, l.PorcentajeRE })
                        .Select(g => new
                        {
                            PorcentajeIVA = g.Key.PorcentajeIVA,
                            PorcentajeRE = g.Key.PorcentajeRE,
                            SumaTotal = g.Sum(l => l.Total),
                            SumaTotalRedondeado = RoundingHelper.DosDecimalesRound(g.Sum(l => l.Total))
                        });

                    foreach (var grupo in gruposIva)
                    {
                        infoBuilder.AppendLine($"  IVA {grupo.PorcentajeIVA}% RE {grupo.PorcentajeRE:P2}: " +
                            $"Total={grupo.SumaTotal:F4}, Redondeado={grupo.SumaTotalRedondeado:F2}");
                    }

                    var totalCuadre = gruposIva.Sum(g => g.SumaTotalRedondeado);
                    infoBuilder.AppendLine($"  TOTAL CUADRE (sum de grupos redondeados): {totalCuadre:F2}");
                }

                var infoDiagnostico = infoBuilder.ToString();

                // Actualizar el último error con la info
                var ultimoError = response.PedidosConErrores.LastOrDefault();
                if (ultimoError != null && ultimoError.NumeroPedido == pedido.Número)
                {
                    ultimoError.InfoDescuentoPP = infoDiagnostico;
                }

                // Escribir en Debug para los logs de Visual Studio
                System.Diagnostics.Debug.WriteLine(infoDiagnostico);

                // Registrar en ELMAH para trazabilidad
                RegistrarDescuadreEnElmah(pedido, ultimoError?.MensajeError, infoDiagnostico);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al calcular diagnóstico de descuadre: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Registra un error de descuadre en ELMAH con información detallada del análisis.
        /// </summary>
        private void RegistrarDescuadreEnElmah(
            CabPedidoVta pedido,
            string mensajeErrorOriginal,
            string infoDiagnostico)
        {
            try
            {
                // Crear una excepción con toda la información para ELMAH
                var mensajeCompleto = new StringBuilder();
                mensajeCompleto.AppendLine($"ERROR DE DESCUADRE EN FACTURACIÓN - Pedido {pedido.Número}");
                mensajeCompleto.AppendLine();
                mensajeCompleto.AppendLine("--- ERROR ORIGINAL ---");
                mensajeCompleto.AppendLine(mensajeErrorOriginal ?? "Sin mensaje");
                mensajeCompleto.AppendLine();
                mensajeCompleto.AppendLine(infoDiagnostico);

                var excepcionDescuadre = new System.ApplicationException(mensajeCompleto.ToString());

                // Añadir datos adicionales a la excepción para búsqueda rápida
                excepcionDescuadre.Data["Pedido"] = pedido.Número;
                excepcionDescuadre.Data["Empresa"] = pedido.Empresa;
                excepcionDescuadre.Data["Cliente"] = pedido.Nº_Cliente;
                excepcionDescuadre.Data["Contacto"] = pedido.Contacto;
                excepcionDescuadre.Data["Ruta"] = pedido.Ruta;
                excepcionDescuadre.Data["PeriodoFacturacion"] = pedido.Periodo_Facturacion;
                excepcionDescuadre.Data["PlazoPago"] = pedido.PlazosPago;
                excepcionDescuadre.Data["Origen"] = pedido.Origen;
                excepcionDescuadre.Data["ModoRedondeo"] = RoundingHelper.UsarAwayFromZero ? "AwayFromZero" : "ToEven";
                excepcionDescuadre.Data["TipoError"] = "DESCUADRE_FACTURACION";
                excepcionDescuadre.Data["NumeroLineas"] = pedido.LinPedidoVtas?.Count ?? 0;

                // Totales de BD para referencia rápida
                if (pedido.LinPedidoVtas != null && pedido.LinPedidoVtas.Any())
                {
                    excepcionDescuadre.Data["TotalBD"] = pedido.LinPedidoVtas.Sum(l => l.Total);
                    excepcionDescuadre.Data["BaseImponibleBD"] = pedido.LinPedidoVtas.Sum(l => l.Base_Imponible);
                }

                // Registrar en ELMAH
                var httpContext = HttpContext.Current;
                if (httpContext != null)
                {
                    ErrorSignal.FromContext(httpContext).Raise(excepcionDescuadre, httpContext);
                }
                else
                {
                    // Si no hay contexto HTTP, usar ErrorLog directamente
                    ErrorLog.GetDefault(null)?.Log(new Error(excepcionDescuadre));
                }

                System.Diagnostics.Debug.WriteLine($"[ELMAH] Registrado error de descuadre para pedido {pedido.Número}");
            }
            catch (Exception ex)
            {
                // Si falla el registro en ELMAH, no interrumpir el flujo
                System.Diagnostics.Debug.WriteLine($"[ELMAH] Error al registrar en ELMAH: {ex.Message}");
            }
        }

        /// <summary>
        /// Limpia el contexto de EF después de un error para evitar que entidades "sucias"
        /// afecten al procesamiento de los siguientes pedidos.
        /// Cuando un SP modifica datos y luego falla, las entidades en memoria pueden tener
        /// RowVersion desactualizado, causando errores de concurrencia en operaciones posteriores.
        /// </summary>
        internal void LimpiarContextoDespuesDeError(CabPedidoVta pedido)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"  → Limpiando contexto después del error para pedido {pedido.Número}");

                // Detach el pedido y sus líneas para que EF "olvide" el estado corrupto
                if (pedido != null)
                {
                    // Detach las líneas primero
                    if (pedido.LinPedidoVtas != null)
                    {
                        foreach (var linea in pedido.LinPedidoVtas.ToList())
                        {
                            var entry = db.Entry(linea);
                            if (entry.State != EntityState.Detached)
                            {
                                entry.State = EntityState.Detached;
                            }
                        }
                    }

                    // Detach la cabecera
                    var cabEntry = db.Entry(pedido);
                    if (cabEntry.State != EntityState.Detached)
                    {
                        cabEntry.State = EntityState.Detached;
                    }
                }

                // Limpiar cualquier cambio pendiente en el contexto
                // Esto evita que SaveChanges intente guardar cambios parciales de la operación fallida
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged && e.State != EntityState.Detached).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                System.Diagnostics.Debug.WriteLine($"  ✓ Contexto limpiado correctamente");
            }
            catch (Exception ex)
            {
                // Si falla la limpieza, solo loggear - no interrumpir el flujo
                System.Diagnostics.Debug.WriteLine($"  ⚠ Error al limpiar contexto: {ex.Message}");
            }
        }

        /// <summary>
        /// Convierte un CabPedidoVta a PedidoVentaDTO para poder usar el validador.
        /// </summary>
        private PedidoVentaDTO ConvertirADTO(CabPedidoVta pedido)
        {
            if (pedido == null)
            {
                return null;
            }

            var dto = new PedidoVentaDTO
            {
                empresa = pedido.Empresa,
                numero = pedido.Número,
                cliente = pedido.Nº_Cliente,
                contacto = pedido.Contacto
            };

            // Obtener el descuento PP del plazo de pago
            if (!string.IsNullOrWhiteSpace(pedido.PlazosPago))
            {
                var plazoPago = db.PlazosPago.FirstOrDefault(p =>
                    p.Empresa == pedido.Empresa &&
                    p.Número == pedido.PlazosPago);
                if (plazoPago != null)
                {
                    dto.DescuentoPP = plazoPago.DtoProntoPago;
                }
            }

            // Convertir líneas
            if (pedido.LinPedidoVtas != null)
            {
                foreach (var linea in pedido.LinPedidoVtas)
                {
                    var lineaDTO = new LineaPedidoVentaDTO
                    {
                        Pedido = dto,
                        Producto = linea.Producto,
                        Cantidad = (int)(linea.Cantidad ?? 0),
                        PrecioUnitario = linea.Precio ?? 0m,
                        DescuentoLinea = linea.Descuento,
                        DescuentoProducto = linea.DescuentoProducto,
                        DescuentoEntidad = linea.DescuentoCliente,
                        AplicarDescuento = linea.Aplicar_Dto,
                        PorcentajeIva = linea.PorcentajeIVA / 100m,
                        PorcentajeRecargoEquivalencia = linea.PorcentajeRE
                    };
                    dto.Lineas.Add(lineaDTO);
                }
            }

            return dto;
        }

        /// <summary>
        /// Obtiene los documentos de impresión para un pedido ya facturado.
        /// Genera PDFs con las copias y bandeja apropiadas según el tipo de ruta.
        /// </summary>
        /// <param name="empresa">Empresa del pedido</param>
        /// <param name="numeroPedido">Número del pedido</param>
        /// <param name="numeroFactura">Número de factura si se generó (null o "FDM" si es fin de mes)</param>
        /// <param name="numeroAlbaran">Número de albarán si se generó</param>
        /// <returns>DTO con los documentos listos para imprimir</returns>
        public async Task<DocumentosImpresionPedidoDTO> ObtenerDocumentosImpresion(
            string empresa,
            int numeroPedido,
            string numeroFactura = null,
            int? numeroAlbaran = null,
            string usuario = null)
        {
            var response = new DocumentosImpresionPedidoDTO();

            try
            {
                // 1. Cargar el pedido de la base de datos
                var pedido = await db.CabPedidoVtas
                    .Where(p => p.Empresa == empresa && p.Número == numeroPedido)
                    .FirstOrDefaultAsync();

                if (pedido == null)
                {
                    throw new ArgumentException($"No se encontró el pedido {numeroPedido} de la empresa {empresa}");
                }

                System.Diagnostics.Debug.WriteLine($"=== ObtenerDocumentosImpresion ===");
                System.Diagnostics.Debug.WriteLine($"Pedido: {numeroPedido}, Factura: {numeroFactura ?? "null"}, Albarán: {numeroAlbaran?.ToString() ?? "null"}");

                // 2. Determinar qué documento generar según lo que se pasó

                // Si hay factura y no es FIN_DE_MES → generar factura
                bool debeGenerarFactura = !string.IsNullOrWhiteSpace(numeroFactura) &&
                                         numeroFactura != Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES;

                // Si no hay factura o es FIN_DE_MES y hay albarán → generar albarán
                bool debeGenerarAlbaran = !debeGenerarFactura && numeroAlbaran.HasValue && numeroAlbaran.Value > 0;

                // Si no hay ninguno de los anteriores → generar nota de entrega
                bool debeGenerarNotaEntrega = !debeGenerarFactura && !debeGenerarAlbaran;

                System.Diagnostics.Debug.WriteLine($"Generar: Factura={debeGenerarFactura}, Albarán={debeGenerarAlbaran}, Nota={debeGenerarNotaEntrega}");

                // 3. Generar los documentos correspondientes

                if (debeGenerarFactura)
                {
                    var facturaCreada = new FacturaCreadaDTO
                    {
                        NumeroFactura = numeroFactura,
                        Empresa = empresa,
                        NumeroPedido = numeroPedido
                    };

                    try
                    {
                        facturaCreada.DatosImpresion = GenerarDatosImpresionFactura(pedido, empresa, numeroFactura, usuario);
                        response.Facturas.Add(facturaCreada);
                        response.TipoDocumentoPrincipal = "Factura";
                        response.Mensaje = $"Factura {numeroFactura} lista para imprimir";
                        System.Diagnostics.Debug.WriteLine($"✓ Factura {numeroFactura} generada para impresión");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error generando factura: {ex.Message}");
                        throw new Exception($"Error al generar PDF de factura {numeroFactura}: {ex.Message}", ex);
                    }
                }
                else if (debeGenerarAlbaran)
                {
                    var albaranCreado = new AlbaranCreadoDTO
                    {
                        NumeroAlbaran = numeroAlbaran.Value,
                        Empresa = empresa,
                        NumeroPedido = numeroPedido
                    };

                    try
                    {
                        albaranCreado.DatosImpresion = GenerarDatosImpresionAlbaran(pedido, empresa, numeroAlbaran.Value, usuario);
                        response.Albaranes.Add(albaranCreado);
                        response.TipoDocumentoPrincipal = "Albarán";
                        response.Mensaje = $"Albarán {numeroAlbaran} listo para imprimir";
                        System.Diagnostics.Debug.WriteLine($"✓ Albarán {numeroAlbaran} generado para impresión");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error generando albarán: {ex.Message}");
                        throw new Exception($"Error al generar PDF de albarán {numeroAlbaran}: {ex.Message}", ex);
                    }
                }
                else if (debeGenerarNotaEntrega)
                {
                    var notaEntrega = new NotaEntregaCreadaDTO
                    {
                        NumeroPedido = numeroPedido,
                        Empresa = empresa
                    };

                    try
                    {
                        notaEntrega.DatosImpresion = GenerarDatosImpresionNotaEntrega(pedido, empresa, numeroPedido, usuario);
                        response.NotasEntrega.Add(notaEntrega);
                        response.TipoDocumentoPrincipal = "Nota de Entrega";
                        response.Mensaje = $"Nota de entrega del pedido {numeroPedido} lista para imprimir";
                        System.Diagnostics.Debug.WriteLine($"✓ Nota de entrega del pedido {numeroPedido} generada para impresión");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error generando nota de entrega: {ex.Message}");
                        throw new Exception($"Error al generar PDF de nota de entrega del pedido {numeroPedido}: {ex.Message}", ex);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"✓✓✓ Documentos generados correctamente. Total para imprimir: {response.TotalDocumentosParaImprimir}");
                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌❌❌ Error en ObtenerDocumentosImpresion: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                throw;
            }
        }
    }
}
