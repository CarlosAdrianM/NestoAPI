using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Description;
using System.Web.Http;
using System;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#593: el cheque regalo de cara a los clientes.
    /// <para>(c4) GET Cliente: el cheque de un cliente, para que Nesto y NestoApp avisen en la plantilla y sepan qué línea
    /// mandar; el canje en sí va en POST y PUT de api/PedidosVenta.</para>
    /// <para>(aviso) Ver y probar el correo del cheque antes de lanzar la campaña (aunque esté inactiva). Solo Dirección e
    /// Informática.</para>
    /// </summary>
    [RoutePrefix("api/ChequesRegalo")]
    public class ChequesRegaloController : ApiController
    {
        private readonly NVEntities db;
        private readonly IAvisadorChequesRegalo avisador;

        public ChequesRegaloController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            avisador = AvisadorChequesRegalo.Crear(db);
        }

        public ChequesRegaloController(NVEntities db)
        {
            this.db = db;
        }

        internal ChequesRegaloController(IAvisadorChequesRegalo avisador)
        {
            this.avisador = avisador;
        }

        private IRepositorioAvisoChequesRegalo campanasCorreo;
        /// <summary>NestoAPI#660 / #593: de donde sale la imagen de la campaña (la misma lectura que el correo del cheque).</summary>
        internal IRepositorioAvisoChequesRegalo CampanasCorreo
        {
            get => campanasCorreo ?? (campanasCorreo = new RepositorioAvisoChequesRegalo(db));
            set => campanasCorreo = value;
        }

        private IServicioCanjeChequesRegalo servicioCanje;
        internal IServicioCanjeChequesRegalo ServicioCanje
        {
            get => servicioCanje ?? (servicioCanje = new ServicioCanjeChequesRegalo(new RepositorioCanjeChequesRegalo(db)));
            set => servicioCanje = value;
        }

        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        private ServicioEstadisticaChequesRegalo servicioEstadistica;
        internal ServicioEstadisticaChequesRegalo ServicioEstadistica
        {
            get => servicioEstadistica ?? (servicioEstadistica = new ServicioEstadisticaChequesRegalo(new RepositorioEstadisticaChequesRegalo(db)));
            set => servicioEstadistica = value;
        }

        private IServicioVendedoresVisibles vendedoresVisibles;
        internal IServicioVendedoresVisibles VendedoresVisibles
        {
            get => vendedoresVisibles ?? (vendedoresVisibles = new ServicioVendedoresVisibles(db));
            set => vendedoresVisibles = value;
        }

        public const string MENSAJE_SIN_PERMISO_ESTADISTICA = "Solo puedes ver los cheques regalo de tu vendedor o de los vendedores de tu equipo.";

        /// <summary>
        /// NestoAPI#593 (estadística): GET api/ChequesRegalo/Resumen?campana=CHEQUE50_OCT_2026&amp;vendedor=JGP — por
        /// vendedor (el de estética de la ficha del cliente), cuántos cheques se han generado, usado (en pedido o ya
        /// servidos), quedan pendientes y han caducado, con el total. Sin campaña, la última en curso (404 si no hay). Sin
        /// vendedor: Dirección ve todos; un jefe de ventas, el suyo y los de su equipo; el resto, el suyo (las mismas reglas
        /// que «clientes para contactar», <see cref="IServicioVendedoresVisibles"/>). Con un vendedor que no puede ver, 403.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("Resumen")]
        [ResponseType(typeof(ResumenChequesRegaloDTO))]
        public async Task<IHttpActionResult> GetResumen(string campana = null, string vendedor = null, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            empresa = EmpresaOPorDefecto(empresa);
            AlcanceEstadistica alcance = await LeerAlcance(empresa, vendedor).ConfigureAwait(false);
            if (alcance == null)
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_ESTADISTICA));
            }
            ResumenChequesRegaloDTO resumen = await ServicioEstadistica.LeerResumen(empresa, campana, alcance.Visibles, Ahora()).ConfigureAwait(false);
            if (resumen == null)
            {
                return NotFound();
            }
            return Ok(resumen);
        }

        /// <summary>
        /// NestoAPI#593 (estadística): GET api/ChequesRegalo/Pendientes?campana=&amp;vendedor=JGP&amp;todos=false — los
        /// cheques sin usar (Disponible o PendienteDeActivar), para la barrida telefónica: cliente, contacto, teléfonos,
        /// dirección, días que quedan, la factura que lo generó (con su importe) y el último contacto. Con todos=true, todos
        /// los cheques con su estado. Mismas reglas de visibilidad que <see cref="GetResumen"/>.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("Pendientes")]
        [ResponseType(typeof(List<ChequeRegaloPendienteDTO>))]
        public async Task<IHttpActionResult> GetPendientes(string campana = null, string vendedor = null, bool todos = false,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            empresa = EmpresaOPorDefecto(empresa);
            AlcanceEstadistica alcance = await LeerAlcance(empresa, vendedor).ConfigureAwait(false);
            if (alcance == null)
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_ESTADISTICA));
            }
            ISet<string> vendedores = alcance.Visibles == null
                ? null
                : new HashSet<string>(alcance.Visibles.Select(v => ReglasEstadisticaChequesRegalo.Vendedor(v.Vendedor)), StringComparer.OrdinalIgnoreCase);
            List<ChequeRegaloPendienteDTO> pendientes = await ServicioEstadistica.LeerPendientes(empresa, campana, vendedores, todos, Ahora()).ConfigureAwait(false);
            if (pendientes == null)
            {
                return NotFound();
            }
            return Ok(pendientes);
        }

        private class AlcanceEstadistica
        {
            /// <summary>Null = todos (Dirección sin pedir vendedor).</summary>
            public List<VendedorVisibleDTO> Visibles { get; set; }
        }

        /// <summary>Null si no puede ver lo que pide (o es un cliente de la tienda).</summary>
        private async Task<AlcanceEstadistica> LeerAlcance(string empresa, string vendedor)
        {
            if ((User?.Identity as ClaimsIdentity)?.FindFirst("cliente") != null)
            {
                return null;
            }
            if (!string.IsNullOrWhiteSpace(vendedor))
            {
                if (!await VendedoresVisibles.PuedeVer(User, empresa, vendedor).ConfigureAwait(false))
                {
                    return null;
                }
                return new AlcanceEstadistica
                {
                    Visibles = new List<VendedorVisibleDTO> { new VendedorVisibleDTO { Vendedor = ReglasEstadisticaChequesRegalo.Vendedor(vendedor) } }
                };
            }
            if (ServicioVendedoresVisibles.EsDireccion(User))
            {
                return new AlcanceEstadistica { Visibles = null };
            }
            return new AlcanceEstadistica
            {
                Visibles = await VendedoresVisibles.LeerVisibles(User, empresa).ConfigureAwait(false) ?? new List<VendedorVisibleDTO>()
            };
        }

        private static string EmpresaOPorDefecto(string empresa)
            => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        /// <summary>
        /// GET api/ChequesRegalo/Cliente?empresa=1&amp;cliente=15191 → el cheque del cliente en una campaña activa (el que
        /// se puede usar o ya está en un pedido, si hay varios), o 404 si no tiene ninguno. Un cliente de la tienda (JWT
        /// con claim «cliente») solo puede consultar el suyo.
        /// <para>NestoAPI#652: también con la X-API-KEY de PrestaShop (de servidor a servidor, sin claim «cliente»: se fía
        /// del parámetro). Sin ninguna de las dos credenciales, 401 siempre (ExigirSiempre, sin modo sombra).</para>
        /// </summary>
        [HttpGet]
        [AutorizadoOApiKey("ApiKeyPrestashop", "X-API-KEY", ExigirSiempre = true)]
        [Route("Cliente")]
        [ResponseType(typeof(ChequeRegaloClienteDTO))]
        public async Task<IHttpActionResult> GetChequeCliente(string cliente, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return BadRequest("Falta el cliente");
            }
            if (!EsSuCliente(cliente))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            ChequeRegaloClienteDTO cheque = await ServicioCanje
                .LeerChequeVigente(string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(), cliente.Trim(), Ahora())
                .ConfigureAwait(false);
            if (cheque == null)
            {
                return NotFound();
            }
            return Ok(cheque);
        }

        /// <summary>
        /// NestoAPI#652: POST api/ChequesRegalo/ComprobarCarrito — ¿puede el cliente usar su cheque con este carrito de la
        /// tienda PrestaShop? Body <c>{ empresa, cliente, lineas: [{ producto, cantidad, baseImponible }] }</c>; responde
        /// 200 con <see cref="ComprobacionCarritoChequeRegaloDTO"/> (Aplicable, Importe, Codigo, Mensaje de usted, Falta).
        /// Las reglas son las del canje en POST api/PedidosVenta (<see cref="CarritoChequesRegalo"/>); no reserva nada.
        /// Mismas credenciales que GET Cliente; un JWT de cliente solo puede preguntar por el suyo (403).
        /// </summary>
        [HttpPost]
        [AutorizadoOApiKey("ApiKeyPrestashop", "X-API-KEY", ExigirSiempre = true)]
        [Route("ComprobarCarrito")]
        [ResponseType(typeof(ComprobacionCarritoChequeRegaloDTO))]
        public async Task<IHttpActionResult> ComprobarCarrito([FromBody] ComprobarCarritoChequeRegaloDTO carrito)
        {
            if (carrito == null || string.IsNullOrWhiteSpace(carrito.Cliente))
            {
                return BadRequest("Falta el cliente");
            }
            if (!EsSuCliente(carrito.Cliente))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            ComprobacionCarritoChequeRegaloDTO resultado = await new CarritoChequesRegalo(ServicioCanje)
                .ComprobarCarritoTienda(carrito, Ahora()).ConfigureAwait(false);
            return Ok(resultado);
        }

        /// <summary>Un JWT de cliente de la tienda (claim «cliente») solo puede preguntar por el suyo. Empleados,
        /// vendedores y la API key de servidor a servidor no llevan el claim.</summary>
        private bool EsSuCliente(string cliente)
        {
            string clienteDelToken = (User?.Identity as ClaimsIdentity)?.FindFirst("cliente")?.Value;
            return clienteDelToken == null || clienteDelToken.Trim() == cliente.Trim();
        }


        /// <summary>
        /// GET api/ChequesRegalo/PrevisualizarCorreo?campana=CHEQUE50_OCT_2026&amp;cliente=15000 — el HTML del correo, con
        /// la imagen dentro (data URI) para verlo en el navegador. Sin cliente, con «Nombre del cliente». No manda nada.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("PrevisualizarCorreo")]
        public async Task<IHttpActionResult> PrevisualizarCorreo(string campana, string cliente = null)
        {
            if (!EsDireccionOInformatica())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (string.IsNullOrWhiteSpace(campana))
            {
                return BadRequest("Falta la campaña (campana=CHEQUE50_OCT_2026)");
            }
            CorreoChequeRegalo correo = await avisador.Previsualizar(campana, cliente);
            if (correo == null)
            {
                return NotFound();
            }
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(correo.Html, Encoding.UTF8, "text/html")
            };
            return ResponseMessage(respuesta);
        }

        /// <summary>
        /// NestoAPI#660 / #593: GET api/ChequesRegalo/CHEQUE50_OCT_2026/Miniatura — la imagen del cheque de la campaña
        /// (ImagenCorreo, la del correo del cheque) reducida a 128 px de ancho, en JPEG. Es la foto de la línea del cheque en
        /// el correo «Gracias por su pedido» (el producto ficticio no está en la tienda). Anónimo, porque la piden los
        /// clientes de correo, y cacheable: Cache-Control público de 7 días y ETag (con If-None-Match que coincide, 304).
        /// 404 si la campaña no existe o no tiene imagen.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        [Route("{campana}/Miniatura")]
        public async Task<IHttpActionResult> GetMiniatura(string campana)
        {
            if (string.IsNullOrWhiteSpace(campana))
            {
                return NotFound();
            }
            CampanaCorreoChequeRegalo leida = await CampanasCorreo.LeerCampana(campana.Trim()).ConfigureAwait(false);
            MiniaturaChequeRegalo miniatura = leida == null ? null : MiniaturasChequeRegalo.DeImagen(leida.ImagenCorreo);
            if (miniatura == null)
            {
                return NotFound();
            }
            var etag = new EntityTagHeaderValue(miniatura.ETag);
            bool sinCambios = Request?.Headers.IfNoneMatch?.Any(e => e.Tag == "*" || e.Tag == etag.Tag) == true;
            HttpResponseMessage respuesta;
            if (sinCambios)
            {
                respuesta = new HttpResponseMessage(HttpStatusCode.NotModified);
            }
            else
            {
                respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(miniatura.Jpeg) };
                respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                respuesta.Content.Headers.ContentLength = miniatura.Jpeg.Length;
            }
            respuesta.Headers.ETag = etag;
            respuesta.Headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = MiniaturasChequeRegalo.VIGENCIA_CACHE };
            return ResponseMessage(respuesta);
        }

        /// <summary>
        /// POST api/ChequesRegalo/EnviarCorreoPrueba?campana=CHEQUE50_OCT_2026&amp;correo=alguien@nuevavision.es&amp;cliente=15000
        /// — manda el correo (asunto con «[PRUEBA]») a esa dirección. No marca ningún cheque.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("EnviarCorreoPrueba")]
        public async Task<IHttpActionResult> EnviarCorreoPrueba(string campana, string correo, string cliente = null)
        {
            if (!EsDireccionOInformatica())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (string.IsNullOrWhiteSpace(campana))
            {
                return BadRequest("Falta la campaña (campana=CHEQUE50_OCT_2026)");
            }
            if (!EsCorreoValido(correo))
            {
                return BadRequest("Falta un correo válido (correo=alguien@nuevavision.es)");
            }
            bool? enviado = await avisador.EnviarPrueba(campana, correo.Trim(), cliente);
            if (enviado == null)
            {
                return NotFound();
            }
            if (enviado == false)
            {
                return Content(HttpStatusCode.BadGateway, "No se ha podido mandar el correo (SMTP)");
            }
            return Ok(new { Enviado = true, Correo = correo.Trim() });
        }

        private static bool EsCorreoValido(string correo)
        {
            if (string.IsNullOrWhiteSpace(correo))
            {
                return false;
            }
            try
            {
                return string.Equals(new MailAddress(correo.Trim()).Address, correo.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private bool EsDireccionOInformatica()
        {
            return User != null && (User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) ||
                User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA));
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
