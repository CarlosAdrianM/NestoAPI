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
            // NestoAPI#855: la nota de una recogida que recoge el vendedor no tiene parada: su NR leído en Algete la cierra.
            get => abonosRecogida ?? (abonosRecogida = new ServicioAbonosRecogida(new RepositorioAbonosRecogida(db))
            {
                EsRecogidaDelVendedor = (empresa, pedido) => new RepositorioRecogidasSolicitadas(db).EsRecogidaDelVendedor(empresa, pedido)
            });
            set => abonosRecogida = value;
        }

        /// <summary>
        /// NestoAPI#772 (red de seguridad de la furgoneta): al hacer el abono de una recogida, los bultos de esa nota que sigan en una
        /// furgoneta cuentan como descargados en Algete (Origen Almacen). (códigos, usuario). Nunca lanza.
        /// </summary>
        internal Func<IEnumerable<string>, string, Task> FurgonetaAlAbonar { get; set; } =
            (codigos, usuario) => Infraestructure.Agencias.RepartoPropio.RedSeguridadFurgoneta.LeidosConContextoPropio(codigos, usuario, true);

        /// <summary>
        /// NestoAPI#855 (remates): después del abono de una recogida (empresa, pedido de la nota, abono recién creado, usuario): si es una
        /// recogida solicitada, procesa la nota de una que recoge el vendedor y avisa a quien la pidió (RecogidaSolicitadaRecibida).
        /// Las recogidas sin solicitud no cambian. Nunca lanza.
        /// </summary>
        internal Func<string, int, bool, string, Task> RecogidaSolicitadaAlAbonar
        {
            get => recogidaSolicitadaAlAbonar ?? (db == null ? null : (Func<string, int, bool, string, Task>)RecogidaSolicitadaAlAbonarPorDefecto);
            set => recogidaSolicitadaAlAbonar = value;
        }

        private Func<string, int, bool, string, Task> recogidaSolicitadaAlAbonar;

        private Task RecogidaSolicitadaAlAbonarPorDefecto(string empresa, int pedidoNota, bool abonoNuevo, string usuario) =>
            new ServicioRecogidasSolicitadas(new RepositorioRecogidasSolicitadas(db), new AvisosRecogidasSolicitadas())
                .AlAbonar(empresa, pedidoNota, abonoNuevo, usuario);

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
            if (resultado.Tipo == TipoResultadoNotaEntrega.Ok && resultado.Contenido != null && RecogidaSolicitadaAlAbonar != null)
            {
                // NestoAPI#855: la recogida solicitada ha llegado a Algete (ya con el abono guardado; nunca lo rompe)
                try
                {
                    await RecogidaSolicitadaAlAbonar(resultado.Contenido.Empresa?.Trim(), resultado.Contenido.PedidoNota, resultado.Creado,
                        UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"));
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Recogidas solicitadas #855] Abono de la nota {resultado.Contenido.PedidoNota}: {ex.Message}", ex));
                }
            }
            if (resultado.Tipo == TipoResultadoNotaEntrega.Ok && resultado.Creado)
            {
                return Content(HttpStatusCode.Created, resultado.Contenido);
            }
            return Responder(resultado);
        }

        private ServicioNotasCobro notasCobro;

        /// <summary>NestoAPI#786: las notas de cobro (sustituible en los tests).</summary>
        internal ServicioNotasCobro NotasCobro
        {
            get => notasCobro ?? (notasCobro = new ServicioNotasCobro(new RepositorioNotasCobro(db),
                new ServicioRepartidores(new RepositorioRepartidores(db))));
            set => notasCobro = value;
        }

        /// <summary>NestoAPI#786: avisar a las PDA del reparto de que hay una parada nueva (sustituible en los tests). Nunca lanza.</summary>
        internal Action<string> AvisarReparto { get; set; } = usuario =>
            new Infraestructure.Notificaciones.AvisosAlmacenSignalR().RepartoCambiado(usuario, Models.Agencias.RepartoCambiadoDTO.MOTIVO_PARADA, DateTime.Now, null);

        // POST api/NotasEntrega/Cobro { Empresa?, Cliente, Contacto?, Efectos?: [NºOrden…], Importe?, Fecha?, Comentario?, Repartidor }
        /// <summary>
        /// NestoAPI#786: «Nueva nota de cobro» (Nesto, desde el detalle del pedido o la ficha del cliente). Crea la nota de solo COBRO
        /// (un pedido con NotaEntrega = 1 y SIN líneas: no pasa por picking, packing ni Facturar rutas) y, en la misma transacción, su
        /// parada del reparto propio: un envío de la agencia 14 ya tramitado con Bultos 0 y Reembolso = el importe, que sale en
        /// GET api/RepartoPropio/Envios como «NotaCobro» (NC{pedido}), con soloMios solo al Repartidor asignado (obligatorio, uno de
        /// GET api/RepartoPropio/Repartidores; 400 si no lo es). 201 con <see cref="NotaCobroDTO"/>. 400 (de usted) sin efectos
        /// ni importe, con un efecto de otro cliente o que ya no está pendiente o si falta el script (la ruta de la ficha ya no cuenta, Carlos 13/11/26);
        /// 404 si no existe el cliente/contacto; 409 si ese cliente/contacto ya tiene otra nota de cobro en el reparto.
        /// </summary>
        [HttpPost]
        [Route("Cobro")]
        [ResponseType(typeof(NotaCobroDTO))]
        public async Task<IHttpActionResult> PostNotaCobro([FromBody] CrearNotaCobroDTO peticion)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, "NestoAPI");
            ResultadoNotaCobro resultado = await NotasCobro.Crear(peticion, usuario);
            if (resultado.Tipo != TipoResultadoNotaEntrega.Ok)
            {
                return Responder<object>(resultado.Tipo, resultado.Mensaje, null);
            }
            try
            {
                AvisarReparto?.Invoke(usuario);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Nota de cobro #786] La nota {resultado.Contenido.Pedido} está creada, pero no se ha podido avisar a las PDA: {ex.Message}", ex));
            }
            return Content(HttpStatusCode.Created, resultado.Contenido);
        }

        // ================================================================ NestoAPI#855: recogidas solicitadas por el vendedor

        private ServicioRecogidasSolicitadas recogidasSolicitadas;

        /// <summary>NestoAPI#855: las recogidas solicitadas (sustituible en los tests).</summary>
        internal ServicioRecogidasSolicitadas RecogidasSolicitadas
        {
            get => recogidasSolicitadas ?? (recogidasSolicitadas = new ServicioRecogidasSolicitadas(new RepositorioRecogidasSolicitadas(db),
                new AvisosRecogidasSolicitadas()));
            set => recogidasSolicitadas = value;
        }

        /// <summary>NestoAPI#855: (empresa, usuario, vendedor, cliente) → el cliente es del vendedor (sustituible en los tests).</summary>
        internal Func<string, string, string, string, bool> EsClienteDelVendedor { get; set; } = Infraestructure.Seguridad.ClientesDelVendedor.EsDelVendedor;

        // POST api/NotasEntrega/Recogidas/Solicitudes { Empresa?, Cliente, Contacto, Texto, Bultos? (1), FacturaOriginal?, QuienRecoge? (Reparto) }
        /// <summary>
        /// NestoAPI#855: el vendedor (NestoApp, solo sus clientes) o un empleado pide una recogida. 201 con <see cref="RecogidaSolicitadaDTO"/>
        /// (Estado «Solicitada») y campana a Almacén. 400: falta el contacto o el texto, bultos fuera de 1-999, quién recoge no es
        /// Reparto/Vendedor, la factura no existe o es de otro cliente, o recoge el Reparto y el contacto no es de ruta propia; 403: el
        /// cliente no es suyo; 404: no existe el contacto; 409: el contacto ya tiene otra sin contestar; 503: falta el script.
        /// </summary>
        [HttpPost]
        [Route("Recogidas/Solicitudes")]
        [ResponseType(typeof(RecogidaSolicitadaDTO))]
        public async Task<IHttpActionResult> PostRecogidaSolicitada([FromBody] SolicitarRecogidaDTO peticion)
        {
            if (!PermisosRecogidasSolicitadas.PuedeCliente(User, peticion?.Empresa, peticion?.Cliente, EsClienteDelVendedor))
            {
                return ProhibidoRecogidas();
            }
            return Responder(await RecogidasSolicitadas.Solicitar(peticion, UsuarioRecogidas()));
        }

        // GET api/NotasEntrega/Recogidas/Solicitudes?empresa=1&cliente=15191&contacto=0
        /// <summary>NestoAPI#855: las del cliente (y contacto, si viene), de la más reciente a la más antigua, con su estado calculado.</summary>
        [HttpGet]
        [Route("Recogidas/Solicitudes")]
        [ResponseType(typeof(List<RecogidaSolicitadaDTO>))]
        public async Task<IHttpActionResult> GetRecogidasSolicitadas(string cliente, string empresa = null, string contacto = null)
        {
            if (!PermisosRecogidasSolicitadas.PuedeCliente(User, empresa, cliente, EsClienteDelVendedor))
            {
                return ProhibidoRecogidas();
            }
            return Responder(await RecogidasSolicitadas.LeerDelCliente(empresa, cliente, contacto));
        }

        // GET api/NotasEntrega/Recogidas/Solicitudes/Pendientes?empresa=1
        /// <summary>NestoAPI#855: para el almacén (los cuatro grupos de las notas): todas las «Solicitada», de la más antigua a la más reciente.</summary>
        [HttpGet]
        [Route("Recogidas/Solicitudes/Pendientes")]
        [ResponseType(typeof(List<RecogidaSolicitadaDTO>))]
        public async Task<IHttpActionResult> GetRecogidasSolicitadasPendientes(string empresa = null)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await RecogidasSolicitadas.LeerPendientes(empresa));
        }

        // GET api/NotasEntrega/Recogidas/Solicitudes/12
        /// <summary>NestoAPI#855: una solicitud (la campana la abre): el almacén, quien la pidió o el vendedor del cliente. 404 si no existe.</summary>
        [HttpGet]
        [Route("Recogidas/Solicitudes/{id:int}")]
        [ResponseType(typeof(RecogidaSolicitadaDTO))]
        public async Task<IHttpActionResult> GetRecogidaSolicitada(int id)
        {
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> resultado = await RecogidasSolicitadas.Leer(id);
            RecogidaSolicitadaDTO dto = resultado.Contenido;
            if (dto != null && !PermisosNotasEntrega.Puede(User)
                && !ServicioRecogidasSolicitadas.MismoUsuario(dto.UsuarioSolicita, UsuarioAuditoriaHelper.Resolver(User, null))
                && !PermisosRecogidasSolicitadas.PuedeCliente(User, dto.Empresa, dto.Cliente, EsClienteDelVendedor))
            {
                return ProhibidoRecogidas();
            }
            return Responder(resultado);
        }

        // POST api/NotasEntrega/Recogidas/Solicitudes/12/Confirmar { Bultos?, Texto? }
        /// <summary>
        /// NestoAPI#855: el almacén la confirma: crea la nota de recogida (NR{pedido}: pedido con NotaEntrega = 1, tipo Recogida, bultos,
        /// factura original y el texto en líneas de texto) y avisa a quien la pidió. Con QuienRecoge = Vendedor la nota no entra en el
        /// picking (fecha sin determinar): no hay parada. 200 con el DTO; 400 texto o bultos que no valen o, si recoge el Reparto, el
        /// contacto ya no es de ruta propia; 404 si no existe; 409 si ya no está «Solicitada»; 503 sin el script.
        /// </summary>
        [HttpPost]
        [Route("Recogidas/Solicitudes/{id:int}/Confirmar")]
        [ResponseType(typeof(RecogidaSolicitadaDTO))]
        public async Task<IHttpActionResult> PostConfirmarRecogidaSolicitada(int id, [FromBody] ConfirmarRecogidaSolicitadaDTO peticion)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await RecogidasSolicitadas.Confirmar(id, peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI")));
        }

        // POST api/NotasEntrega/Recogidas/Solicitudes/12/Rechazar { Motivo }
        /// <summary>NestoAPI#855: el almacén la rechaza (motivo obligatorio, 400 sin él). 404 si no existe; 409 si ya no está «Solicitada».</summary>
        [HttpPost]
        [Route("Recogidas/Solicitudes/{id:int}/Rechazar")]
        [ResponseType(typeof(RecogidaSolicitadaDTO))]
        public async Task<IHttpActionResult> PostRechazarRecogidaSolicitada(int id, [FromBody] RechazarRecogidaSolicitadaDTO peticion)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await RecogidasSolicitadas.Rechazar(id, peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI")));
        }

        // DELETE api/NotasEntrega/Recogidas/Solicitudes/12?motivo=...
        /// <summary>
        /// NestoAPI#855: anularla. Quien la pidió (o el almacén) mientras no esté en reparto: «Solicitada», o «Confirmada» con la nota
        /// sin empezar (sin picking, sin parada, sin abono), que se borra. Ya en reparto (parada o picking), solo Almacén o Administración:
        /// se marca anulada, la nota se queda y se avisa a Almacén (AvisoAnulacion dice qué hacer a mano). 403 si no le toca; 404 si no
        /// existe; 409 si ya está rechazada, anulada o recogida; 503 sin el script.
        /// </summary>
        [HttpDelete]
        [Route("Recogidas/Solicitudes/{id:int}")]
        [ResponseType(typeof(RecogidaSolicitadaDTO))]
        public async Task<IHttpActionResult> DeleteRecogidaSolicitada(int id, string motivo = null)
        {
            if (!PermisosRecogidasSolicitadas.PuedeSolicitar(User))
            {
                return ProhibidoRecogidas();
            }
            return Responder(await RecogidasSolicitadas.Anular(id, motivo, UsuarioRecogidas()));
        }

        private UsuarioRecogidas UsuarioRecogidas() => new UsuarioRecogidas
        {
            Nombre = UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"),
            Vendedor = (User?.Identity as System.Security.Claims.ClaimsIdentity)?.FindFirst("Vendedor")?.Value?.Trim(),
            PuedeGestionar = PermisosNotasEntrega.Puede(User),
            PuedeAnularEnReparto = PermisosRecogidasSolicitadas.PuedeAnularEnReparto(User)
        };

        private IHttpActionResult Responder<T>(ResultadoRecogidaSolicitada<T> resultado)
        {
            if (resultado.Codigo == HttpStatusCode.OK)
            {
                return Ok(resultado.Contenido);
            }
            if (resultado.Codigo == HttpStatusCode.Created)
            {
                return Content(HttpStatusCode.Created, resultado.Contenido);
            }
            return Content(resultado.Codigo, new { Message = resultado.Mensaje });
        }

        private IHttpActionResult ProhibidoRecogidas()
            => Content(HttpStatusCode.Forbidden, new { Message = "Su usuario no puede pedir ni ver recogidas de este cliente." });

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
