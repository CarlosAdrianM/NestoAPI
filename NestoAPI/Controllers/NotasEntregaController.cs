using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#757 (corte 1 de #755): la cabecera de una nota de entrega (tipo Entrega / Recogida, bultos y factura original).
    /// La usan Almacén, Administración, Dirección e Informática (los mismos que api/RepartoPropio); el resto, 403.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/NotasEntrega")]
    public class NotasEntregaController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioNotasEntregaCabecera servicio;

        public NotasEntregaController() : this(new NVEntities())
        {
        }

        internal NotasEntregaController(NVEntities db) : this(db, new ServicioNotasEntregaCabecera(new RepositorioNotasEntregaCabecera(db)))
        {
        }

        internal NotasEntregaController(NVEntities db, IServicioNotasEntregaCabecera servicio)
        {
            this.db = db;
            this.servicio = servicio;
        }

        private IServicioDocumentoNotaEntrega documentos;
        private IServicioEtiquetasRecogida etiquetasRecogida;

        /// <summary>NestoAPI#758: el PDF de la nota (sustituible en los tests; se crea al usarlo, con el db del controlador).</summary>
        internal IServicioDocumentoNotaEntrega Documentos
        {
            get
            {
                if (documentos == null)
                {
                    var servicioFacturas = new ServicioFacturas(db);
                    documentos = new ServicioDocumentoNotaEntrega(db, new GestorFacturas(servicioFacturas));
                }
                return documentos;
            }
            set => documentos = value;
        }

        private IServicioAbonosRecogida abonosRecogida;

        /// <summary>NestoAPI#760: el abono por recogida (sustituible en los tests).</summary>
        internal IServicioAbonosRecogida AbonosRecogida
        {
            get => abonosRecogida ?? (abonosRecogida = new ServicioAbonosRecogida(new RepositorioAbonosRecogida(db)));
            set => abonosRecogida = value;
        }

        /// <summary>
        /// NestoAPI#772 (red de seguridad de la furgoneta): al hacer el abono de una recogida, los bultos de esa nota que sigan en una
        /// furgoneta cuentan como descargados en Algete (Origen Almacen). (códigos, usuario). Nunca lanza.
        /// </summary>
        internal Func<IEnumerable<string>, string, Task> FurgonetaAlAbonar { get; set; } =
            (codigos, usuario) => Infraestructure.Agencias.RepartoPropio.RedSeguridadFurgoneta.LeidosConContextoPropio(codigos, usuario, true);

        /// <summary>NestoAPI#758: las etiquetas de recogida (sustituible en los tests).</summary>
        internal IServicioEtiquetasRecogida EtiquetasRecogida
        {
            get => etiquetasRecogida ?? (etiquetasRecogida = new ServicioEtiquetasRecogida(db));
            set => etiquetasRecogida = value;
        }

        // GET api/NotasEntrega/1/930123
        /// <summary>La cabecera de la nota. 404 si el pedido no es una nota de entrega. Sin fila: Entrega, 1 bulto.</summary>
        [HttpGet]
        [Route("{empresa}/{pedido:int}")]
        [ResponseType(typeof(NotaEntregaCabeceraDTO))]
        public async Task<IHttpActionResult> GetNotaEntrega(string empresa, int pedido)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Leer(empresa, pedido));
        }

        // PUT api/NotasEntrega/1/930123/Cabecera { Tipo, Bultos, FacturaOriginal }
        /// <summary>
        /// Cambia tipo, bultos y factura original y deja fila en Modificaciones. 400 (de usted) si el pedido no es nota, el tipo
        /// no es Entrega/Recogida, una Recogida no lleva bultos (≥ 1) o la factura no existe o es de otro cliente; 404 si el
        /// pedido no existe; 409 si su envío ya está Entregado. En Entrega, Bultos no se valida (se guarda 1) y FacturaOriginal
        /// se ignora (se guarda NULL).
        /// </summary>
        [HttpPut]
        [Route("{empresa}/{pedido:int}/Cabecera")]
        [ResponseType(typeof(NotaEntregaCabeceraDTO))]
        public async Task<IHttpActionResult> PutCabecera(string empresa, int pedido, [FromBody] ActualizarNotaEntregaCabeceraDTO peticion)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Actualizar(empresa, pedido, peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI")));
        }

        // GET api/NotasEntrega/1/930123/Documento?papelConMembrete=false
        /// <summary>
        /// NestoAPI#758: el PDF de la nota (application/pdf): «NOTA DE ENTREGA» / «NOTA DE RECOGIDA», nº de nota, bultos, teléfonos,
        /// el texto en grande, «Recibí» y el Code128 NE{pedido} / NR{pedido}. Para «Imprimir nota» de Nesto: vale también antes
        /// de facturar la ruta y para las notas de Nesto viejo, y NO crea la parada. 404 si el pedido no existe o no es nota.
        /// </summary>
        [HttpGet]
        [Route("{empresa}/{pedido:int}/Documento")]
        public async Task<IHttpActionResult> GetDocumento(string empresa, int pedido, bool papelConMembrete = false)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            ResultadoDocumentoNotaEntrega resultado = await Documentos.Generar(empresa, pedido, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"), papelConMembrete);
            if (resultado.Tipo != TipoResultadoNotaEntrega.Ok)
            {
                return Responder<object>(resultado.Tipo, resultado.Mensaje, null);
            }
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(resultado.Pdf)
            };
            respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            respuesta.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
            {
                FileName = (resultado.CodigoDocumento ?? pedido.ToString()) + ".pdf"
            };
            return ResponseMessage(respuesta);
        }

        // POST api/NotasEntrega/1/929554/EtiquetasRecogida { Impresora? }
        /// <summary>
        /// NestoAPI#758: imprime en la Zebra una etiqueta por bulto de una nota de RECOGIDA (Code128 NR{pedido}-{N}) y devuelve
        /// { Impresas, Codigos, Impresora }. 400 si no es de recogida (las de entrega llevan la etiqueta del packing) o el usuario no
        /// tiene impresora; 404 si el pedido no existe; 502 si falla la impresora.
        /// </summary>
        [HttpPost]
        [Route("{empresa}/{pedido:int}/EtiquetasRecogida")]
        [ResponseType(typeof(EtiquetasRecogidaResultadoDTO))]
        public async Task<IHttpActionResult> PostEtiquetasRecogida(string empresa, int pedido, [FromBody] EtiquetasRecogidaPeticionDTO peticion)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            ResultadoEtiquetasRecogida resultado = await EtiquetasRecogida.Imprimir(empresa, pedido, peticion?.Impresora, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"));
            return Responder(resultado.Tipo, resultado.Mensaje, resultado.Contenido);
        }

        // GET api/NotasEntrega/Recogidas/NR929554-1
        /// <summary>
        /// NestoAPI#760: la nota de recogida de una etiqueta (NR{pedido}-{N} o NR{pedido}), para enseñarla antes de crear el abono:
        /// cliente, texto, bultos, factura original, estado de la parada, cuándo y a quién se recogió (Receptor solo para Almacén,
        /// Administración y Dirección) y el abono si ya existe. 404 si el código no es de una nota de recogida.
        /// </summary>
        [HttpGet]
        [Route("Recogidas/{codigo}")]
        [ResponseType(typeof(RecogidaDTO))]
        public async Task<IHttpActionResult> GetRecogida(string codigo)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            ResultadoAbonoRecogida<RecogidaDTO> resultado = await AbonosRecogida.Leer(codigo, PermisosRepartoPropio.PuedeVerDatosPersonales(User));
            return Responder(resultado);
        }

        // POST api/NotasEntrega/Recogidas/Abono { Codigo, Forzar }
        /// <summary>
        /// NestoAPI#760: crea el pedido de abono (serie rectificativa, sin líneas) del cliente de la nota de recogida y lo apunta en
        /// NotasEntregaCabecera.PedidoAbono: 201 con { Empresa, Pedido, Cliente, Contacto, Nombre, Serie, PedidoNota, FacturaOriginal,
        /// TextoNota, YaExistia }. Idempotente: si ya tiene abono, 200 con ese pedido y YaExistia = true. 409 RECOGIDA_NO_RECOGIDA
        /// (details.estado) si el repartidor no la ha marcado como recogida, salvo con Forzar = true. 404 si no es una recogida.
        /// </summary>
        [HttpPost]
        [Route("Recogidas/Abono")]
        [ResponseType(typeof(AbonoRecogidaDTO))]
        public async Task<IHttpActionResult> PostAbonoRecogida([FromBody] CrearAbonoRecogidaDTO peticion)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            ResultadoAbonoRecogida<AbonoRecogidaDTO> resultado = await AbonosRecogida.CrearAbono(peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"));
            if (resultado.Tipo == TipoResultadoNotaEntrega.Ok && FurgonetaAlAbonar != null
                && CodigoNotaEntrega.TryLeer(peticion?.Codigo, out string prefijo, out int pedidoNota, out _) && prefijo == CodigoNotaEntrega.PREFIJO_RECOGIDA)
            {
                // NestoAPI#772: la nota entera (todas sus NR) ya está en el almacén
                await FurgonetaAlAbonar(new[] { CodigoNotaEntrega.Documento(true, pedidoNota) }, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"));
            }
            if (resultado.Tipo == TipoResultadoNotaEntrega.Ok && resultado.Creado)
            {
                return Content(HttpStatusCode.Created, resultado.Contenido);
            }
            return Responder(resultado);
        }

        private IHttpActionResult Responder<T>(ResultadoAbonoRecogida<T> resultado)
        {
            if (resultado.Tipo == TipoResultadoNotaEntrega.Conflicto && resultado.Codigo != null)
            {
                // El formato de GlobalExceptionFilter ({ error: { code, message, details } }) y Message como siempre (como api/RepartoPropio).
                return Content(HttpStatusCode.Conflict, new
                {
                    error = new { code = resultado.Codigo, message = resultado.Mensaje, details = resultado.Detalles },
                    Message = resultado.Mensaje
                });
            }
            return Responder(resultado.Tipo, resultado.Mensaje, resultado.Contenido);
        }

        private IHttpActionResult Responder(ResultadoNotaEntregaCabecera resultado)
            => Responder(resultado.Tipo, resultado.Mensaje, resultado.Contenido);

        private IHttpActionResult Responder<T>(TipoResultadoNotaEntrega tipo, string mensaje, T contenido)
        {
            switch (tipo)
            {
                case TipoResultadoNotaEntrega.Ok:
                    return Ok(contenido);
                case TipoResultadoNotaEntrega.NoEncontrado:
                    return Content(HttpStatusCode.NotFound, new { Message = mensaje });
                case TipoResultadoNotaEntrega.Conflicto:
                    return Content(HttpStatusCode.Conflict, new { Message = mensaje });
                case TipoResultadoNotaEntrega.FalloImpresora:
                    return Content(HttpStatusCode.BadGateway, new { Message = mensaje });
                default:
                    return Content(HttpStatusCode.BadRequest, new { Message = mensaje });
            }
        }

        private IHttpActionResult Prohibido()
            => Content(HttpStatusCode.Forbidden, new { Message = "Su usuario no tiene permiso para las notas de entrega." });

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
