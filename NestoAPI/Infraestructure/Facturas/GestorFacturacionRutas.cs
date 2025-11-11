using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        /// Busca en los comentarios del pedido:
        /// - "Factura física" (case insensitive, sin tildes)
        /// - "Albarán físico" (case insensitive, sin tildes)
        /// </summary>
        public bool DebeImprimirDocumento(string comentarios)
        {
            if (string.IsNullOrWhiteSpace(comentarios))
            {
                return false;
            }

            // Normalizar: quitar tildes y convertir a minúsculas
            string comentariosNormalizados = RemoverTildes(comentarios.ToLower());

            // Buscar "factura fisica" o "albaran fisico"
            string facturaFisica = "factura fisica";
            string albaranFisico = "albaran fisico";

            return comentariosNormalizados.Contains(facturaFisica) ||
                   comentariosNormalizados.Contains(albaranFisico);
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
        /// Procesa facturación masiva de pedidos por rutas.
        /// </summary>
        /// <param name="pedidos">Lista de pedidos a facturar</param>
        /// <param name="usuario">Usuario que realiza la facturación</param>
        public async Task<FacturarRutasResponseDTO> FacturarRutas(System.Collections.Generic.List<CabPedidoVta> pedidos, string usuario)
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
                    await ProcesarPedido(pedido, response, usuario);
                    response.PedidosProcesados++;
                    System.Diagnostics.Debug.WriteLine($"  ✓ Pedido {pedido.Número} procesado correctamente");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ EXCEPCIÓN en pedido {pedido.Número}: {ex.Message}");
                    RegistrarError(pedido, "Proceso general", ex, response);
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
        /// Procesa un pedido individual: crea albarán, traspasa si necesario, crea factura y genera PDFs si corresponde.
        /// Si es nota de entrega, solo procesa la nota de entrega (no crea albarán ni factura).
        /// </summary>
        private async Task ProcesarPedido(
            CabPedidoVta pedido,
            FacturarRutasResponseDTO response,
            string usuario)
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

            // 2. Crear albarán
            int numeroAlbaran;
            try
            {
                System.Diagnostics.Debug.WriteLine($"  → Creando ALBARÁN para pedido {pedido.Número}");
                numeroAlbaran = await servicioAlbaranes.CrearAlbaran(
                    pedido.Empresa,
                    pedido.Número,
                    usuario);

                System.Diagnostics.Debug.WriteLine($"  → Albarán {numeroAlbaran} creado correctamente");

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

            // 3. Verificar si hay que traspasar a empresa destino
            if (servicioTraspaso.HayQueTraspasar(pedido))
            {
                try
                {
                    // IMPORTANTE: Guardar ExtractoRuta del albarán ANTES del traspaso
                    // El traspaso usa BeginTransaction() y no puede tener cambios pendientes
                    await db.SaveChangesAsync();

                    await servicioTraspaso.TraspasarPedidoAEmpresa(
                        pedido,
                        Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO);
                }
                catch (Exception ex)
                {
                    RegistrarError(pedido, "Traspaso", ex, response);
                    return; // Si falla el traspaso, no continuar
                }
            }
            else
            {
                // Si no hay traspaso, guardar los cambios pendientes del ExtractoRuta
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
                AgregarDatosImpresionAlbaranSiCorresponde(pedido, numeroAlbaran, response);
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
            // Validar ANTES si se puede facturar (MantenerJunto)
            if (!PuedeFacturarPedido(pedido))
            {
                // No se puede facturar porque MantenerJunto = 1 y hay líneas sin albarán
                // IMPORTANTE: Registrar esto como un "error" para que el usuario lo sepa
                var lineasSinAlbaran = pedido.LinPedidoVtas?
                    .Count(l => l.Estado < Constantes.EstadosLineaVenta.ALBARAN) ?? 0;

                RegistrarError(pedido, "Factura",
                    $"No se puede facturar porque tiene MantenerJunto=1 y hay {lineasSinAlbaran} línea(s) sin albarán. " +
                    $"Se ha creado el albarán {numeroAlbaran} pero la factura queda pendiente hasta que todas las líneas tengan albarán.",
                    response);

                // En este caso, si tiene comentario de impresión, generar PDF del ALBARÁN
                if (DebeImprimirDocumento(pedido.Comentarios))
                {
                    try
                    {
                        AgregarDatosImpresionAlbaranSiCorresponde(pedido, numeroAlbaran, response);
                    }
                    catch (Exception ex)
                    {
                        RegistrarError(pedido, "Generación PDF Albarán", ex, response);
                    }
                }
                return; // No intentar crear factura
            }

            try
            {
                // Crear factura
                System.Diagnostics.Debug.WriteLine($"  → Creando FACTURA para pedido {pedido.Número}");
                string numeroFactura = await servicioFacturas.CrearFactura(
        pedido.Empresa,
        pedido.Número,
        usuario);

                System.Diagnostics.Debug.WriteLine($"  → Factura {numeroFactura} creada correctamente");

                // Agregar factura creada al response
                var facturaCreada = CrearFacturaCreadaDTO(pedido, numeroFactura);
                response.Facturas.Add(facturaCreada);

                // Insertar en ExtractoRuta desde factura SOLO si el tipo de ruta lo requiere (Ruta Propia SÍ, Agencia NO)
                // Para NRM, el ExtractoRuta se inserta desde la factura (no desde el albarán)
                var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
                if (tipoRuta?.DebeInsertarEnExtractoRuta() == true)
                {
                    System.Diagnostics.Debug.WriteLine($"  → Insertando en ExtractoRuta desde factura (NRM)");
                    await servicioExtractoRuta.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: true);
                }

                // Si debe imprimirse, generar bytes del PDF
                if (DebeImprimirDocumento(pedido.Comentarios))
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"  → Generando PDF de factura");
                        facturaCreada.DatosImpresion = GenerarDatosImpresionFactura(pedido, pedido.Empresa, numeroFactura);
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
                        AgregarDatosImpresionAlbaranSiCorresponde(pedido, numeroAlbaran, response);
                    }
                    catch (Exception exImpresion)
                    {
                        RegistrarError(pedido, "Generación PDF Albarán", exImpresion, response);
                    }
                }
                return;
            }
        }

        /// <summary>
        /// Agrega datos de impresión al albarán si el pedido tiene comentario de impresión.
        /// Usado para pedidos FDM (Fin de Mes).
        /// </summary>
        private void AgregarDatosImpresionAlbaranSiCorresponde(
            CabPedidoVta pedido,
            int numeroAlbaran,
            FacturarRutasResponseDTO response)
        {
            // Solo generar PDF si tiene comentario de impresión
            if (!DebeImprimirDocumento(pedido.Comentarios))
            {
                return;
            }

            // Buscar el albarán en la lista y agregarle los datos de impresión
            var albaran = response.Albaranes.FirstOrDefault(a =>
                a.Empresa == pedido.Empresa &&
                a.NumeroAlbaran == numeroAlbaran);

            if (albaran != null)
            {
                albaran.DatosImpresion = GenerarDatosImpresionAlbaran(pedido, pedido.Empresa, numeroAlbaran);
            }
        }

        /// <summary>
        /// Genera los datos de impresión para un albarán (bytes del PDF, copias, bandeja).
        /// </summary>
        private DocumentoParaImprimir GenerarDatosImpresionAlbaran(CabPedidoVta pedido, string empresa, int numeroAlbaran)
        {
            var lookup = new FacturaLookup { Empresa = empresa, Factura = numeroAlbaran.ToString() };
            var lista = new List<FacturaLookup> { lookup };
            var albaranes = gestorFacturas.LeerAlbaranes(lista);

            var bytesPdf = gestorFacturas.FacturasEnPDF(albaranes, papelConMembrete: false);

            // Determinar tipo de ruta y obtener configuración de impresión
            var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

            // Si la ruta no está manejada por ningún tipo, no imprimir
            int numeroCopias = tipoRuta != null
                ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : 0;

            string bandeja = tipoRuta != null ? tipoRuta.ObtenerBandeja() : "Default";

            return new DocumentoParaImprimir
            {
                BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
                NumeroCopias = numeroCopias,
                Bandeja = bandeja
            };
        }

        /// <summary>
        /// Genera los datos de impresión para una factura (bytes del PDF, copias, bandeja).
        /// </summary>
        private DocumentoParaImprimir GenerarDatosImpresionFactura(CabPedidoVta pedido, string empresa, string numeroFactura)
        {
            var factura = gestorFacturas.LeerFactura(empresa, numeroFactura);
            var facturas = new List<Factura> { factura };

            var bytesPdf = gestorFacturas.FacturasEnPDF(facturas, papelConMembrete: false);

            // Determinar tipo de ruta y obtener configuración de impresión
            var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

            // Si la ruta no está manejada por ningún tipo, no imprimir
            int numeroCopias = tipoRuta != null
                ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : 0;

            string bandeja = tipoRuta != null ? tipoRuta.ObtenerBandeja() : "Default";

            return new DocumentoParaImprimir
            {
                BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
                NumeroCopias = numeroCopias,
                Bandeja = bandeja
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
                // Filtros: Estado = EN_CURSO, Picking > 0, Fecha_Entrega <= fechaEntregaDesde
                decimal baseImponible = pedido.LinPedidoVtas?
                    .Where(l => l.Estado == Constantes.EstadosLineaVenta.EN_CURSO &&
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
            FacturarRutasResponseDTO response)
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
                    FechaEntrega = fechaEntrega ?? DateTime.Today,
                    Total = total
                });

                // CRÍTICO: Escribir en Debug para que aparezca en la ventana de Salida
                System.Diagnostics.Debug.WriteLine($"ERROR REGISTRADO - Pedido {pedido.Número}: [{tipoError}] {mensajeError}");
            }
            catch (Exception ex)
            {
                // Último recurso: Si incluso agregar el error falla, escribir en Debug
                System.Diagnostics.Debug.WriteLine($"FALLO AL REGISTRAR ERROR - Pedido {pedido?.Número}: {ex.Message}");
            }
        }

        /// <summary>
        /// Registra un error en el response con detalles completos de la excepción.
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

            RegistrarError(pedido, tipoError, mensajeCompleto, response);
        }
    }
}
