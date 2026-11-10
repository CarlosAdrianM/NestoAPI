using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Controller para facturación masiva de pedidos por rutas.
    /// Permite facturar pedidos de rutas propias (16, AT) o rutas de agencias (FW, 00).
    /// Requiere permisos de Almacén o Dirección.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/FacturacionRutas")]
    public class FacturacionRutasController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioPedidosParaFacturacion servicioPedidos;
        private readonly IServicioAgruparPorPO servicioAgruparPorPO;
        private readonly IServicioFacturarFinDeMes servicioFinDeMes;

        /// <summary>
        /// Constructor por defecto (usado en producción)
        /// </summary>
        public FacturacionRutasController()
        {
            db = new NVEntities();
            servicioPedidos = new ServicioPedidosParaFacturacion(db);
            servicioAgruparPorPO = CrearServicioAgruparPorPO(db);
            servicioFinDeMes = CrearServicioFinDeMes(db);
            // NestoAPI#828: solo en producción (en los tests no se toca la BD si no se ponen)
            MarcasPapel = new RepositorioDocumentosPapelPacking();
            EnviosDePedidos = LeerEnviosDePedidos;
        }

        /// <summary>
        /// Constructor para testing (permite inyección de dependencias)
        /// </summary>
        internal FacturacionRutasController(
            NVEntities db,
            IServicioPedidosParaFacturacion servicioPedidos,
            IServicioAgruparPorPO servicioAgruparPorPO = null,
            Func<IGestorFacturacionRutas> fabricaGestor = null,
            IServicioFacturarFinDeMes servicioFinDeMes = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.servicioPedidos = servicioPedidos ?? throw new ArgumentNullException(nameof(servicioPedidos));
            this.servicioAgruparPorPO = servicioAgruparPorPO ?? CrearServicioAgruparPorPO(db);
            this.fabricaGestor = fabricaGestor;
            this.servicioFinDeMes = servicioFinDeMes ?? CrearServicioFinDeMes(db);
        }

        /// <summary>NestoAPI#727: interruptor Facturacion:FinDeMesPorApi (se fakea en los tests).</summary>
        internal Func<bool> FinDeMesPorApi { get; set; } = InterruptorFinDeMesPorApi.EstaActivo;

        /// <summary>NestoAPI#727: hoy, para la fecha de corte por defecto (se fakea en los tests).</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        /// <summary>
        /// NestoAPI#765: la nota de entrega que no ha pasado por el packing no se procesa sin confirmación. Por defecto lee del db del
        /// controller; se sustituye en los tests.
        /// </summary>
        internal IGuardiaNotaSinPacking GuardiaNotas
        {
            get => guardiaNotas ?? (guardiaNotas = new GuardiaNotaSinPacking(new RepositorioEstadoNotaPacking(db)));
            set => guardiaNotas = value;
        }
        private IGuardiaNotaSinPacking guardiaNotas;

        /// <summary>NestoAPI#810: los reembolsos del reparto propio desfasados tras facturar. Lee del db del controller; se sustituye en los tests.</summary>
        internal Infraestructure.Agencias.RepartoPropio.IDetectorReembolsosDesfasados DetectorReembolsos
        {
            get => detectorReembolsos ?? (detectorReembolsos = new Infraestructure.Agencias.RepartoPropio.DetectorReembolsosDesfasados(db));
            set => detectorReembolsos = value;
        }
        private Infraestructure.Agencias.RepartoPropio.IDetectorReembolsosDesfasados detectorReembolsos;

        /// <summary>
        /// NestoAPI#810: tras facturar, los envíos del reparto propio sin entregar de los pedidos facturados cuyo reembolso no es lo
        /// facturado van en <see cref="FacturarRutasResponseDTO.ReembolsosDesfasados"/>. No cambia nada (pregunta el cliente) y nunca
        /// hace fallar la facturación: si algo falla, la lista sale vacía y a ELMAH.
        /// </summary>
        internal async Task AnotarReembolsosDesfasados(FacturarRutasResponseDTO response)
        {
            if (response?.Facturas == null || !response.Facturas.Any())
            {
                return;
            }
            try
            {
                var desfasados = await DetectorReembolsos.Detectar(response.Facturas.Select(f => f.NumeroPedido)).ConfigureAwait(true);
                if (desfasados != null && desfasados.Any())
                {
                    if (response.ReembolsosDesfasados == null)
                    {
                        response.ReembolsosDesfasados = new System.Collections.Generic.List<ReembolsoDesfasadoDTO>();
                    }
                    response.ReembolsosDesfasados.AddRange(desfasados);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"NestoAPI#810: no se han podido comprobar los reembolsos del reparto propio de los pedidos " +
                    $"{string.Join(", ", response.Facturas.Select(f => f.NumeroPedido).Distinct())}: {ex.Message}", ex));
            }
        }

        /// <summary>NestoAPI#828: las marcas del papel del packing (null = no se miran; en los tests, si no se ponen).</summary>
        internal IRepositorioDocumentosPapelPacking MarcasPapel { get; set; }

        /// <summary>NestoAPI#828: pedidos → su último envío (null = no se miran; en los tests, si no se pone).</summary>
        internal Func<System.Collections.Generic.List<int>, Task<System.Collections.Generic.Dictionary<int, int>>> EnviosDePedidos { get; set; }

        private async Task<System.Collections.Generic.Dictionary<int, int>> LeerEnviosDePedidos(System.Collections.Generic.List<int> pedidos)
        {
            var envios = await System.Data.Entity.QueryableExtensions.ToListAsync(db.EnviosAgencias
                .Where(e => e.Pedido.HasValue && pedidos.Contains(e.Pedido.Value))
                .Select(e => new { Pedido = e.Pedido.Value, e.Numero })).ConfigureAwait(true);
            return envios.GroupBy(e => e.Pedido).ToDictionary(g => g.Key, g => g.Max(e => e.Numero));
        }

        /// <summary>
        /// NestoAPI#828 (S4): en Facturar rutas de agencias, lo del packing de hoy (lo ya impreso y lo pendiente) y el envío de cada
        /// documento que sale en papel en esta tanda (para localizar la caja), con su resumen. Nunca hace fallar la facturación.
        /// </summary>
        internal async Task AnotarPapelDelPacking(FacturarRutasResponseDTO response, string tipoRuta)
        {
            if (response == null)
            {
                return;
            }
            try
            {
                ITipoRuta tipo = TipoRutaFactory.ObtenerTodosLosTipos()
                    .FirstOrDefault(t => string.Equals(t.Id, tipoRuta?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (tipo?.CuentaPapelDelPackingEnFacturarRutas == true && MarcasPapel != null)
                {
                    System.Collections.Generic.List<DocumentoEnPapelDTO> marcas = await MarcasPapel.LeerDesde(Hoy().Date).ConfigureAwait(true);
                    // Las cartas del cheque no son documentos de la facturación: no cuentan en «N facturas ya impresas»
                    response.ImpresosEnPacking.AddRange(marcas.Where(m => m.Estado == EstadosDocumentoEnPapel.IMPRESO && m.Tipo != TiposDocumentoEnPapel.CARTA_CHEQUE));
                    response.PendientesEnPacking.AddRange(marcas.Where(m => m.Estado == EstadosDocumentoEnPapel.PENDIENTE));
                }
                System.Collections.Generic.List<DocumentoImprimibleDTO> enPapel = DocumentosEnPapelDeLaTanda(response);
                if (enPapel.Any() && EnviosDePedidos != null)
                {
                    System.Collections.Generic.Dictionary<int, int> envios = await EnviosDePedidos(enPapel.Select(d => d.NumeroPedido).Distinct().ToList()).ConfigureAwait(true);
                    foreach (DocumentoImprimibleDTO documento in enPapel)
                    {
                        if (envios != null && envios.TryGetValue(documento.NumeroPedido, out int envio))
                        {
                            documento.Envio = envio;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"NestoAPI#828: no se ha podido anotar el papel del packing en Facturar rutas: {ex.Message}", ex));
            }
            response.ResumenPapelPacking = ResumenPapelPacking(response);
        }

        /// <summary>Los documentos de la tanda que salen en papel (con copias).</summary>
        internal static System.Collections.Generic.List<DocumentoImprimibleDTO> DocumentosEnPapelDeLaTanda(FacturarRutasResponseDTO response)
            => (response.Facturas ?? new System.Collections.Generic.List<FacturaCreadaDTO>()).Cast<DocumentoImprimibleDTO>()
                .Concat(response.Albaranes ?? new System.Collections.Generic.List<AlbaranCreadoDTO>())
                .Concat(response.NotasEntrega ?? new System.Collections.Generic.List<NotaEntregaCreadaDTO>())
                .Where(d => d?.DatosImpresion != null && d.DatosImpresion.NumeroCopias > 0)
                .ToList();

        /// <summary>
        /// NestoAPI#828 (decisión 3): «3 facturas en papel ya impresas en el packing.», lo pendiente del packing y lo que sale en esta
        /// tanda, cada uno con su pedido y su envío. Null si no hay nada que decir.
        /// </summary>
        internal static string ResumenPapelPacking(FacturarRutasResponseDTO response)
        {
            var lineas = new System.Collections.Generic.List<string>();
            System.Collections.Generic.List<DocumentoEnPapelDTO> impresos = response.ImpresosEnPacking ?? new System.Collections.Generic.List<DocumentoEnPapelDTO>();
            if (impresos.Any())
            {
                bool facturas = impresos.All(d => d.Tipo == TiposDocumentoEnPapel.FACTURA);
                lineas.Add(impresos.Count == 1
                    ? (facturas ? "1 factura en papel ya impresa en el packing." : "1 documento en papel ya impreso en el packing.")
                    : (facturas ? $"{impresos.Count} facturas en papel ya impresas en el packing." : $"{impresos.Count} documentos en papel ya impresos en el packing."));
            }
            System.Collections.Generic.List<DocumentoEnPapelDTO> pendientes = response.PendientesEnPacking ?? new System.Collections.Generic.List<DocumentoEnPapelDTO>();
            if (pendientes.Any())
            {
                lineas.Add($"Sin imprimir en el packing ({pendientes.Count}): " +
                    string.Join("; ", pendientes.Select(d => $"{d.Descripcion} ({DondeEsta(d.Pedido, d.Envio)})")) +
                    ". Repita la etiqueta del envío o «Reintentar impresión» en Ariadna, o imprímalas desde Nesto.");
            }
            System.Collections.Generic.List<DocumentoImprimibleDTO> tanda = DocumentosEnPapelDeLaTanda(response);
            if (tanda.Any())
            {
                lineas.Add($"En papel en esta facturación ({tanda.Count}): " + string.Join("; ", tanda.Select(d =>
                    $"{DescripcionDe(d)} ({DondeEsta(d.NumeroPedido, d.Envio)})")) + ".");
            }
            return lineas.Any() ? string.Join(Environment.NewLine, lineas) : null;
        }

        private static string DondeEsta(int pedido, int? envio)
            => envio.HasValue && envio.Value > 0 ? $"pedido {pedido}, envío {envio.Value}" : $"pedido {pedido}, sin envío";

        private static string DescripcionDe(DocumentoImprimibleDTO documento)
        {
            switch (documento)
            {
                case FacturaCreadaDTO f:
                    return TiposDocumentoEnPapel.Descripcion(TiposDocumentoEnPapel.FACTURA, f.NumeroFactura);
                case AlbaranCreadoDTO a:
                    return TiposDocumentoEnPapel.Descripcion(TiposDocumentoEnPapel.ALBARAN, a.NumeroAlbaran.ToString(CultureInfo.InvariantCulture));
                default:
                    return TiposDocumentoEnPapel.Descripcion(TiposDocumentoEnPapel.NOTA_ENTREGA, documento.NumeroPedido.ToString(CultureInfo.InvariantCulture));
            }
        }

        // Para tests: sustituye el gestor real (con sus servicios) por uno falso.
        private readonly Func<IGestorFacturacionRutas> fabricaGestor;

        /// <summary>
        /// El gestor de facturación de rutas con TODOS sus servicios sobre el MISMO db (si cada servicio
        /// tuviera su contexto, los SPs y los cambios de EF chocarían). Lo comparten «Facturar» (rutas) y
        /// «FacturarPedido» (Agencias): el mismo núcleo para los dos caminos.
        /// </summary>
        private IGestorFacturacionRutas CrearGestor()
        {
            if (fabricaGestor != null)
            {
                return fabricaGestor();
            }
            var servicioFacturas = new ServicioFacturas(db);
            return new GestorFacturacionRutas(
                db,
                new ServicioAlbaranesVenta(db),
                servicioFacturas,
                new GestorFacturas(servicioFacturas),
                new ServicioTraspasoEmpresa(db),
                new ServicioNotasEntrega(db),
                new ServicioExtractoRuta(db));
        }

        // NestoAPI#195 (Fase 3): construye el orquestador de agrupación por PO con el mismo
        // db compartido (igual patrón que FacturarRutas, para evitar conflictos de concurrencia).
        private static IServicioAgruparPorPO CrearServicioAgruparPorPO(NVEntities db)
        {
            return new ServicioAgruparPorPO(
                db,
                new EstrategiaAgrupacionPO(db),
                new MotorAgrupacionPedidos(db),
                new ServicioFacturas(db));
        }

        // NestoAPI#727: el fin de mes, con el mismo db compartido y el mismo motor que la agrupación por P.O.
        private static IServicioFacturarFinDeMes CrearServicioFinDeMes(NVEntities db)
        {
            return new ServicioFacturarFinDeMes(
                db,
                new EstrategiaAgrupacionFinDeMes(db),
                new MotorAgrupacionPedidos(db),
                new ServicioFacturas(db));
        }

        // NestoAPI#195 (Fase 3): vuelca el resultado de la agrupación por PO en la respuesta de
        // facturación de rutas (números de factura creados y errores por grupo).
        private static void VolcarResultadoPO(ResultadoAgrupacionPO resultadoPO, FacturarRutasResponseDTO response)
        {
            if (resultadoPO == null)
            {
                return;
            }
            foreach (var factura in resultadoPO.Facturas)
            {
                response.FacturasPorPO.Add(factura.NumeroFactura);
            }
            foreach (var error in resultadoPO.Errores)
            {
                response.ErroresPorPO.Add($"PO {error.SuPedido} (cliente {error.Cliente}): {error.Mensaje}");
            }
        }

        /// <summary>
        /// Obtiene la lista de tipos de ruta disponibles para facturación.
        /// Retorna información dinámica desde TipoRutaFactory para que la UI
        /// pueda generar los controles dinámicamente sin hardcodear rutas.
        /// </summary>
        /// <returns>Lista de tipos de ruta con sus propiedades (Id, Nombre, Descripción, Rutas)</returns>
        /// <remarks>
        /// PERMISOS REQUERIDOS: Almacén o Dirección
        ///
        /// USO: El frontend llama a este endpoint al abrir el diálogo de facturación
        /// para generar dinámicamente los RadioButtons con los tipos disponibles.
        ///
        /// EXTENSIBILIDAD: Si se agrega un nuevo tipo de ruta a TipoRutaFactory,
        /// automáticamente aparecerá en la UI sin modificar código frontend.
        /// </remarks>
        [HttpGet]
        [Route("TiposRuta")]
        public IHttpActionResult ObtenerTiposRuta()
        {
            // Validar autorización (Almacén o Dirección)
            if (!TienePermisosFacturacion())
                return StatusCode(HttpStatusCode.Forbidden);

            try
            {
                // Obtener todos los tipos de ruta registrados en el factory
                var tipos = TipoRutaFactory.ObtenerTodosLosTipos()
                    .Select(t => new TipoRutaInfoDTO
                    {
                        Id = t.Id,
                        NombreParaMostrar = t.NombreParaMostrar,
                        Descripcion = t.Descripcion,
                        RutasContenidas = t.RutasContenidas.ToList()
                    })
                    .ToList();

                return Ok(tipos);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex); // 28/09/26: InternalServerError se salta el GlobalExceptionFilter
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Factura pedidos por rutas (propia o agencias) listos para facturar.
        /// Procesa pedidos con líneas en curso, picking realizado y visto bueno.
        /// </summary>
        /// <param name="request">Parámetros de facturación (tipo ruta, fecha desde)</param>
        /// <returns>Resultado con contadores y lista de errores</returns>
        /// <remarks>
        /// PERMISOS REQUERIDOS: Almacén o Dirección
        ///
        /// PROCESO:
        /// 1. Obtiene pedidos que cumplen criterios (ruta, estado, fecha, visto bueno)
        /// 2. Para cada pedido:
        ///    - Crea albarán
        ///    - Traspasa a empresa 3 si corresponde (actualmente desactivado)
        ///    - Si NRM: crea factura y genera PDF si tiene "factura física" en comentarios
        ///    - Si FDM: genera PDF del albarán si tiene "factura física" o "albarán físico" en comentarios
        /// 3. Retorna lista de albaranes/facturas creados (algunos con bytes de PDF para imprimir)
        ///
        /// VALIDACIONES:
        /// - MantenerJunto: Si el pedido tiene MantenerJunto=true y hay líneas sin albarán (Estado<2),
        ///   NO se crea factura y se genera PDF del albarán si corresponde
        ///
        /// NOTA: El servidor NO imprime directamente. Genera bytes del PDF que el cliente WPF
        /// usa con PdfiumViewer para enviar a la impresora.
        /// </remarks>
        [HttpPost]
        [Route("Facturar")]
        public async Task<IHttpActionResult> FacturarRutas([FromBody] FacturarRutasRequestDTO request)
        {
            // Validar request
            if (request == null)
                return BadRequest("Request no puede ser null");

            // Validar autorización (Almacén o Dirección)
            if (!TienePermisosFacturacion())
                return StatusCode(HttpStatusCode.Forbidden);

            try
            {
                // Obtener el usuario autenticado
                string usuario = ObtenerUsuarioActual();
                if (string.IsNullOrEmpty(usuario))
                    return Unauthorized();

                var fechaDesde = request.FechaEntregaDesde ?? DateTime.Today;

                // NestoAPI#195 (Fase 3): ANTES de la facturación normal, agrupamos y facturamos
                // los grupos de PO completos (mismo SuPedido, con o sin MantenerJunto, todos en albarán; NestoAPI#727).
                // Tiene que ir primero para que la facturación normal no facture esos pedidos por
                // separado: tras agrupar, sus líneas viven en el pedido destino (ya facturado) y
                // ObtenerPedidosParaFacturar ya no los devuelve.
                ResultadoAgrupacionPO resultadoPO = await servicioAgruparPorPO.EvaluarYProcesar(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, usuario);

                // 1. Obtener pedidos (después de agrupar PO, para no incluir los ya facturados)
                var pedidos = await servicioPedidos.ObtenerPedidosParaFacturar(
                    request.TipoRuta,
                    fechaDesde);

                // Si no hay pedidos de ruta, devolvemos el resultado de la agrupación por PO igualmente
                if (!pedidos.Any())
                {
                    var responseVacio = new FacturarRutasResponseDTO();
                    VolcarResultadoPO(resultadoPO, responseVacio);
                    await AnotarPapelDelPacking(responseVacio, request.TipoRuta);
                    return Ok(responseVacio);
                }

                // 2. Procesar facturación
                var response = await CrearGestor().FacturarRutas(pedidos, usuario, fechaDesde);
                VolcarResultadoPO(resultadoPO, response);
                await AnotarReembolsosDesfasados(response);
                await AnotarPapelDelPacking(response, request.TipoRuta);

                return Ok(response);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex); // 28/09/26: InternalServerError se salta el GlobalExceptionFilter
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Factura UN pedido exactamente igual que la facturación de rutas (mismo núcleo,
        /// GestorFacturacionRutas.ProcesarPedido): visto bueno, nota de entrega (estado -2 y baja de stock
        /// de lo «de carpeta»), albarán, traspaso, factura según el periodo, extracto de ruta y documentos
        /// para imprimir. Lo usa «Facturar al imprimir etiqueta» de Agencias en Nesto: si un pedido se
        /// factura al imprimir la etiqueta ya no lo coge la facturación de rutas, y al revés.
        /// </summary>
        /// <remarks>
        /// PERMISOS REQUERIDOS: Almacén, Tiendas, Administración o Dirección. Son los grupos que Nesto deja
        /// facturar desde el detalle del pedido (EsGrupoQuePuedeFacturar). Antes la etiqueta llamaba a
        /// CrearAlbaran/CrearFactura, que no miran el grupo, y Aida (Administración) factura desde ahí.
        /// Si el pedido no tiene nada que facturar (líneas en curso o en albarán con picking y fecha de
        /// entrega hasta hoy) se devuelve con un error en PedidosConErrores, no un 500.
        /// </remarks>
        [HttpPost]
        [Route("FacturarPedido")]
        public async Task<IHttpActionResult> FacturarPedido([FromBody] FacturarPedidoRequestDTO request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Empresa) || request.Pedido <= 0)
                return BadRequest("Hay que indicar la empresa y el pedido");

            if (!TienePermisosFacturarPedido())
                return StatusCode(HttpStatusCode.Forbidden);

            try
            {
                string usuario = ObtenerUsuarioActual();
                if (string.IsNullOrEmpty(usuario))
                    return Unauthorized();

                var fechaDesde = request.FechaEntregaDesde ?? DateTime.Today;
                var pedido = await servicioPedidos.ObtenerPedidoParaFacturar(request.Empresa, request.Pedido, fechaDesde);

                // NestoAPI#765: una nota de ENTREGA sin picking o sin envío no se procesa sin confirmar (se saltaría el packing). Solo
                // se mira si es nota o si no hay nada con picking (puede ser una nota que aún no ha entrado en el picking).
                if (!request.SaltarPacking && (pedido == null || pedido.NotaEntrega))
                {
                    string motivo = await GuardiaNotas.Motivo(request.Empresa.Trim(), request.Pedido);
                    if (!string.IsNullOrWhiteSpace(motivo))
                    {
                        var sinPacking = new FacturarRutasResponseDTO();
                        sinPacking.PedidosConErrores.Add(new PedidoConErrorDTO
                        {
                            Empresa = request.Empresa.Trim(),
                            NumeroPedido = request.Pedido,
                            Ruta = pedido?.Ruta?.Trim(),
                            TipoError = GuardiaNotaSinPacking.TIPO_ERROR,
                            Codigo = GuardiaNotaSinPacking.CODIGO,
                            MensajeError = motivo,
                            FechaEntrega = fechaDesde
                        });
                        return Ok(sinPacking);
                    }
                }
                if (pedido == null && request.SaltarPacking)
                {
                    pedido = await servicioPedidos.ObtenerNotaEntregaSinPicking(request.Empresa, request.Pedido, fechaDesde);
                }

                if (pedido == null)
                {
                    var sinNada = new FacturarRutasResponseDTO();
                    sinNada.PedidosConErrores.Add(new PedidoConErrorDTO
                    {
                        Empresa = request.Empresa.Trim(),
                        NumeroPedido = request.Pedido,
                        TipoError = "Sin líneas para facturar",
                        MensajeError = $"El pedido {request.Pedido} no tiene líneas para facturar: tienen que estar en curso o en albarán, con picking y con fecha de entrega hasta el {fechaDesde:dd/MM/yy}.",
                        FechaEntrega = fechaDesde
                    });
                    return Ok(sinNada);
                }

                var response = await CrearGestor().FacturarRutas(
                    new System.Collections.Generic.List<CabPedidoVta> { pedido }, usuario, fechaDesde);
                await AnotarReembolsosDesfasados(response);
                return Ok(response);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex);
                return InternalServerError(ex);
            }
        }

        /// <summary>NestoAPI#828: los pasos del papel del packing (usuario, saltar el packing). Se sustituyen en los tests.</summary>
        internal Func<System.Security.Principal.IPrincipal, bool, IPasosDocumentosEnPapel> CrearPasosPapel { get; set; }
            = (usuario, saltarPacking) => new PasosDocumentosEnPapelApi(usuario, saltarPacking);

        /// <summary>
        /// NestoAPI#828 (S5): «Facturar al imprimir etiqueta» de Agencias de Nesto, con EL MISMO núcleo que F8 en Ariadna
        /// (<see cref="DocumentosEnPapelDelEnvio"/>, estrategia «Nesto imprime en local»): factura el pedido del envío como
        /// FacturarPedido (también la ruta propia, como antes del #828: Carlos 10/11/26), deja cada documento en papel marcado «Pendiente» y lo
        /// devuelve con su PDF para que Nesto lo imprima y lo confirme (POST DocumentosEnPapel/Impresion). Si ya estaba facturado y su
        /// papel pendiente (Nesto no llegó a imprimirlo), lo devuelve otra vez sin volver a facturar.
        /// </summary>
        /// <remarks>Mismos permisos que FacturarPedido. 200 <see cref="FacturarEtiquetaResponseDTO"/> (la respuesta de FacturarPedido
        /// con DocumentosEnPapel y AvisosPapel).</remarks>
        [HttpPost]
        [Route("FacturarEtiqueta")]
        public async Task<IHttpActionResult> FacturarEtiqueta([FromBody] FacturarEtiquetaRequestDTO request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Empresa) || request.Pedido <= 0)
                return BadRequest("Hay que indicar la empresa y el pedido");

            if (!TienePermisosFacturarPedido())
                return StatusCode(HttpStatusCode.Forbidden);

            string usuario = ObtenerUsuarioActual();
            if (string.IsNullOrEmpty(usuario))
                return Unauthorized();

            IPasosDocumentosEnPapel pasos = CrearPasosPapel(User, request.SaltarPacking);
            try
            {
                ResultadoDocumentosEnPapel papel = await new DocumentosEnPapelDelEnvio(pasos, new ImpresoraPapelPackingLocal()).Procesar(new PeticionDocumentosEnPapel
                {
                    Empresa = request.Empresa.Trim(),
                    Pedidos = new System.Collections.Generic.List<int> { request.Pedido },
                    Envio = request.Envio,
                    Usuario = usuario
                });
                return Ok(FacturarEtiquetaResponseDTO.De(papel));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex);
                return InternalServerError(ex);
            }
            finally
            {
                (pasos as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// NestoAPI#828 (S5): Nesto dice si ha impreso en local un documento en papel de la etiqueta (Impreso true) o no (false, con el
        /// motivo): la marca queda «Impreso» (Facturar rutas lo cuenta) o «Pendiente» (repetir la etiqueta lo devuelve otra vez).
        /// 204; 400 sin pedido o documento, o si el documento no es del papel del packing de ese pedido (no tiene su marca). Mismos
        /// permisos que FacturarPedido.
        /// </summary>
        [HttpPost]
        [Route("DocumentosEnPapel/Impresion")]
        public async Task<IHttpActionResult> ImpresionDocumentoEnPapel([FromBody] ImpresionDocumentoEnPapelDTO impresion)
        {
            if (impresion == null || impresion.Pedido <= 0 || string.IsNullOrWhiteSpace(impresion.Documento))
                return BadRequest("Hay que indicar el pedido y el documento");

            if (!TienePermisosFacturarPedido())
                return StatusCode(HttpStatusCode.Forbidden);

            IPasosDocumentosEnPapel pasos = CrearPasosPapel(User, false);
            try
            {
                if (!await new DocumentosEnPapelDelEnvio(pasos, new ImpresoraPapelPackingLocal()).MarcarImpresionLocal(impresion))
                {
                    return BadRequest($"El documento {impresion.Documento.Trim()} no es del papel del packing del pedido {impresion.Pedido}.");
                }
                return StatusCode(HttpStatusCode.NoContent);
            }
            finally
            {
                (pasos as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// NestoAPI#195 (Fase 3): agrupa y factura los pedidos que comparten P.O. (Purchase Order)
        /// con MantenerJunto cuyos grupos están completos (todas las líneas en albarán), para la
        /// empresa por defecto. Cada grupo se convierte en UNA factura con el deudor del cliente
        /// principal, conservando el P.O. y las direcciones de entrega por línea.
        /// </summary>
        /// <returns>Facturas creadas y grupos que fallaron (cada grupo se procesa aislado)</returns>
        /// <remarks>
        /// PERMISOS REQUERIDOS: Almacén o Dirección
        ///
        /// Endpoint dedicado y a demanda, separado del batch de FacturarRutas: permite verificar
        /// la agrupación por PO de forma controlada antes de automatizarla. El gate de
        /// PuedeFacturarPedido ya impide que FacturarRutas facture pedidos con hermanos de PO
        /// incompletos; este endpoint procesa los grupos que ya están listos.
        /// </remarks>
        [HttpPost]
        [Route("AgruparPorPO")]
        public async Task<IHttpActionResult> AgruparPorPO()
        {
            // Validar autorización (Almacén o Dirección)
            if (!TienePermisosFacturacion())
                return StatusCode(HttpStatusCode.Forbidden);

            try
            {
                string usuario = ObtenerUsuarioActual();
                if (string.IsNullOrEmpty(usuario))
                    return Unauthorized();

                ResultadoAgrupacionPO resultado = await servicioAgruparPorPO.EvaluarYProcesar(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, usuario);

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex); // 28/09/26: InternalServerError se salta el GlobalExceptionFilter
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// NestoAPI#727: facturación de FIN DE MES (lo que hacían prdAgruparAlbaranesVta y el Nesto viejo). Para cada cliente
        /// con pedidos de periodo FDM sin P.O., reúne en un pedido (el del cliente principal) los albaranes hasta la fecha de
        /// corte de todos sus contactos y lo factura. Los pedidos con P.O. no entran (van por AgruparPorPO). Un error en un
        /// cliente no para a los demás.
        /// </summary>
        /// <remarks>
        /// PERMISOS REQUERIDOS: Administración, Dirección o Informática. Detrás de Facturacion:FinDeMesPorApi (apagado: 400).
        /// Body: { Empresa, FechaCorte (yyyy-MM-dd; por defecto HOY: entran todos los albaranes, no puede haberlos con fecha futura), Cliente?, Contacto? }.
        /// </remarks>
        [HttpPost]
        [Route("FinDeMes")]
        public async Task<IHttpActionResult> FacturarFinDeMes([FromBody] FacturarFinDeMesRequestDTO request)
        {
            if (!TienePermisosFinDeMes())
                return StatusCode(HttpStatusCode.Forbidden);

            if (!FinDeMesPorApi())
                return BadRequest(InterruptorFinDeMesPorApi.MOTIVO_APAGADO);

            request = request ?? new FacturarFinDeMesRequestDTO();
            string empresa = string.IsNullOrWhiteSpace(request.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : request.Empresa.Trim();
            string cliente = string.IsNullOrWhiteSpace(request.Cliente) ? null : request.Cliente.Trim();
            string contacto = string.IsNullOrWhiteSpace(request.Contacto) ? null : request.Contacto.Trim();
            if (contacto != null && cliente == null)
                return BadRequest("Para facturar un solo contacto indique también el cliente.");

            DateTime hoy = Hoy().Date;
            DateTime fechaCorte;
            if (string.IsNullOrWhiteSpace(request.FechaCorte))
            {
                // Carlos 30/10/26: el fin de mes se hace el último día laborable y entran TODOS los albaranes (no puede
                // haberlos con fecha futura). Antes era el último día del mes anterior y había que cambiarlo a mano.
                fechaCorte = hoy;
            }
            else if (!DateTime.TryParseExact(request.FechaCorte.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fechaCorte))
            {
                return BadRequest($"La fecha de corte «{request.FechaCorte}» no es válida: use el formato yyyy-MM-dd.");
            }
            if (fechaCorte.Date > hoy)
                return BadRequest($"La fecha de corte ({fechaCorte:dd/MM/yyyy}) no puede ser posterior a hoy.");

            try
            {
                string usuario = ObtenerUsuarioActual();
                if (string.IsNullOrEmpty(usuario))
                    return Unauthorized();

                ResultadoFacturacionFinDeMes resultado = await servicioFinDeMes.Facturar(empresa, fechaCorte.Date, cliente, contacto, usuario);
                return Ok(resultado);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex);
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Genera un preview (simulación) de facturación de rutas SIN crear nada en la BD.
        /// Muestra qué albaranes, facturas y notas de entrega se crearían, con sus importes.
        /// Útil para validar contra el sistema legacy antes de facturar.
        /// </summary>
        /// <param name="request">Parámetros de facturación (tipo ruta, fecha desde)</param>
        /// <returns>Preview con contadores, bases imponibles y muestra de pedidos</returns>
        /// <remarks>
        /// PERMISOS REQUERIDOS: Almacén o Dirección
        ///
        /// PROCESO:
        /// 1. Obtiene pedidos que cumplen criterios (MISMA lógica que Facturar)
        /// 2. Para cada pedido:
        ///    - Calcula si se crearía albarán, factura o nota de entrega
        ///    - Acumula base imponible de cada tipo
        /// 3. Retorna resumen SIN tocar la BD
        ///
        /// USO: Comparar contra sistema legacy para validar antes de facturar.
        /// </remarks>
        [HttpPost]
        [Route("Preview")]
        public async Task<IHttpActionResult> PreviewFacturarRutas([FromBody] FacturarRutasRequestDTO request)
        {
            // Validar request
            if (request == null)
                return BadRequest("Request no puede ser null");

            // Validar autorización (Almacén o Dirección)
            if (!TienePermisosFacturacion())
                return StatusCode(HttpStatusCode.Forbidden);

            try
            {
                // 1. Obtener pedidos (misma lógica que Facturar)
                var fechaDesde = request.FechaEntregaDesde ?? DateTime.Today;
                var pedidos = await servicioPedidos.ObtenerPedidosParaFacturar(
                    request.TipoRuta,
                    fechaDesde);

                // Si no hay pedidos, retornar preview vacío
                if (!pedidos.Any())
                {
                    return Ok(new PreviewFacturacionRutasResponseDTO());
                }

                // 2. Generar preview (NO crea nada)
                // IMPORTANTE: Pasar el db a TODOS los servicios para consistencia con FacturarRutas
                var servicioAlbaranes = new ServicioAlbaranesVenta(db);
                var servicioFacturas = new ServicioFacturas(db);
                var gestorFacturas = new GestorFacturas(servicioFacturas);
                var servicioTraspaso = new ServicioTraspasoEmpresa(db);
                var servicioNotasEntrega = new ServicioNotasEntrega(db);
                var servicioExtractoRuta = new ServicioExtractoRuta(db);

                var gestor = new GestorFacturacionRutas(
                    db,
                    servicioAlbaranes,
                    servicioFacturas,
                    gestorFacturas,
                    servicioTraspaso,
                    servicioNotasEntrega,
                    servicioExtractoRuta
                );

                var preview = gestor.PreviewFacturarRutas(pedidos, fechaDesde);

                return Ok(preview);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex); // 28/09/26: InternalServerError se salta el GlobalExceptionFilter
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Facturar UN pedido (FacturarPedido): los grupos que pueden facturar desde el detalle del pedido
        /// en Nesto, más Dirección.
        /// </summary>
        private bool TienePermisosFacturarPedido()
        {
            var user = User as ClaimsPrincipal;
            if (user == null)
                return false;

            return TienePermisosFacturacion() ||
                   user.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS) ||
                   user.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION);
        }

        /// <summary>
        /// NestoAPI#727: el fin de mes es cosa de Administración (más Dirección e Informática).
        /// </summary>
        private bool TienePermisosFinDeMes()
        {
            var user = User as ClaimsPrincipal;
            if (user == null)
                return false;

            return user.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION) ||
                   user.IsInRoleSinDominio(Constantes.GruposSeguridad.FACTURACION) || // Carlos 30/10/26: Alfredo (Facturación) lanza el fin de mes
                   user.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) ||
                   user.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA);
        }

        /// <summary>
        /// Verifica si el usuario tiene permisos para facturar rutas.
        /// Requiere estar en grupo Almacén o Dirección.
        /// </summary>
        private bool TienePermisosFacturacion()
        {
            var user = User as ClaimsPrincipal;
            if (user == null)
                return false;

            // Usar IsInRoleSinDominio para comparar sin el prefijo del dominio (ej: NUEVAVISION\)
            return user.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN) ||
                   user.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION);
        }

        /// <summary>
        /// Obtiene el nombre del usuario autenticado desde los Claims CON DOMINIO (ej: NUEVAVISION\Carlos).
        /// Este método se usa para INSERT en tablas de auditoría (ExtractoRuta, PreExtrProducto, etc.)
        /// </summary>
        private string ObtenerUsuarioActual()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return null;

            // Intentar obtener el nombre de usuario desde User.Identity.Name
            if (!string.IsNullOrEmpty(User.Identity.Name))
            {
                // Devolver el nombre completo CON dominio (ej: NUEVAVISION\Carlos). NestoAPI#823: y si viene sin dominio
                // (Ariadna/NestoApp) con gemelo, el de dominio: va al SP de facturar, que mira FechasPermitidas.
                return UsuarioAuditoriaHelper.Resolver(User, User.Identity.Name);
            }

            // Si no está en Identity.Name, buscar en claims
            var user = User as ClaimsPrincipal;
            if (user != null)
            {
                var nombreClaim = user.FindFirst(ClaimTypes.Name) ?? user.FindFirst("sub");
                if (nombreClaim != null)
                {
                    // Devolver el nombre completo CON dominio (ej: NUEVAVISION\Carlos)
                    return nombreClaim.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Extrae el usuario SIN dominio de un nombre completo (ej: "NUEVAVISION\Carlos" -> "Carlos").
        /// Este método se usa para SELECT en ParametrosUsuario y otras búsquedas.
        /// </summary>
        /// <param name="usuarioConDominio">Usuario con formato DOMINIO\Usuario</param>
        /// <returns>Usuario sin el prefijo del dominio</returns>
        private string ExtraerUsuarioSinDominio(string usuarioConDominio)
        {
            if (string.IsNullOrEmpty(usuarioConDominio))
                return usuarioConDominio;

            var index = usuarioConDominio.LastIndexOf('\\');
            return index >= 0 ? usuarioConDominio.Substring(index + 1) : usuarioConDominio;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
