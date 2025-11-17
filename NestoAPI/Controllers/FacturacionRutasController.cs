using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
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

        /// <summary>
        /// Constructor por defecto (usado en producción)
        /// </summary>
        public FacturacionRutasController()
        {
            db = new NVEntities();
            servicioPedidos = new ServicioPedidosParaFacturacion(db);
        }

        /// <summary>
        /// Constructor para testing (permite inyección de dependencias)
        /// </summary>
        internal FacturacionRutasController(
            NVEntities db,
            IServicioPedidosParaFacturacion servicioPedidos)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.servicioPedidos = servicioPedidos ?? throw new ArgumentNullException(nameof(servicioPedidos));
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

                // 1. Obtener pedidos
                var fechaDesde = request.FechaEntregaDesde ?? DateTime.Today;
                var pedidos = await servicioPedidos.ObtenerPedidosParaFacturar(
                    request.TipoRuta,
                    fechaDesde);

                // Si no hay pedidos, retornar response vacío
                if (!pedidos.Any())
                {
                    return Ok(new FacturarRutasResponseDTO
                    {
                        PedidosConErrores = new System.Collections.Generic.List<PedidoConErrorDTO>()
                    });
                }

                // 2. Procesar facturación
                var servicioAlbaranes = new ServicioAlbaranesVenta();
                var servicioFacturas = new ServicioFacturas();
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

                var response = await gestor.FacturarRutas(pedidos, usuario);

                return Ok(response);
            }
            catch (Exception ex)
            {
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
                var servicioAlbaranes = new ServicioAlbaranesVenta();
                var servicioFacturas = new ServicioFacturas();
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
                return InternalServerError(ex);
            }
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
                // Devolver el nombre completo CON dominio (ej: NUEVAVISION\Carlos)
                return User.Identity.Name;
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
