using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#556: la parte de servidor de Ariadna, la app de almacén (MAUI, Android y Windows).
    /// Preparación de pedidos con lector de códigos: cargar el picking y el packing en el móvil,
    /// guardar lo que se lee y la foto de cada bulto.
    ///
    /// <para>No cambia nada de lo que hay en uso: el picking lo sigue sacando Nesto. Aquí solo se lee
    /// ese picking y se guarda la evidencia en dos tablas propias (PreparacionEscaneos y
    /// EnviosAgenciaBultos).</para>
    ///
    /// <para>Las rutas van por dominio (api/Almacen), no con el nombre de la app. [Authorize] desde el
    /// primer día: los mozos entran como los vendedores de NestoApp (/oauth/token). Leer basta con estar
    /// identificado; escribir (todo lo que no es GET) pide Almacén o Dirección (EscrituraSoloAlmacen),
    /// para que el usuario de revisión de Google Play no toque datos reales.</para>
    /// </summary>
    [EscrituraSoloAlmacen]
    [RoutePrefix("api/Almacen")]
    public class AlmacenController : ApiController
    {
        private readonly IServicioPreparacionAlmacen servicio;
        private readonly IServicioUbicacionesAlmacen ubicaciones;
        private readonly IServicioRecepcionCompras compras;
        private readonly IServicioRecepcionReposiciones reposiciones;
        private readonly IFichasProductoAlmacen fichas;
        private readonly IServicioEtiquetasHueco etiquetas;
        private readonly IServicioCambioHuecoPicking cambioHueco;
        /// <summary>Ariadna#12: lo mismo en la salida de una reposición (REPO). Null: las reposiciones no lo admiten.</summary>
        private readonly IServicioCambioHuecoReposicion cambioHuecoReposicion;
        /// <summary>NestoAPI#646: avisar a las demás PDA del documento de que ha cambiado (SignalR). Null: no se avisa.</summary>
        private readonly Infraestructure.Notificaciones.IAvisosAlmacen avisosAlmacen;
        /// <summary>NestoAPI#595 (slice 5, Ariadna#13): la etiqueta de agencia desde el packing. Null: no está configurada.</summary>
        private readonly Infraestructure.Agencias.IServicioEtiquetaPacking etiquetaPacking;
        /// <summary>Sugerencia 824: la ficha del producto (botón «Producto» del inicio de Ariadna). Null: no está configurada.</summary>
        private readonly IServicioFichaProductoAlmacen fichaProducto;

        /// <param name="fichas">Familia, subgrupo, tamaño y unidad de cada producto que ve el mozo. Sin él, solo el nombre.</param>
        /// <param name="etiquetas">Imprimir etiquetas de hueco (Nesto y Ariadna).</param>
        /// <param name="cambioHueco">Ariadna#12: coger de otro hueco lo que no estaba en el de la parada.</param>
        public AlmacenController(IServicioPreparacionAlmacen servicio, IServicioUbicacionesAlmacen ubicaciones,
            IServicioRecepcionCompras compras, IServicioRecepcionReposiciones reposiciones, IFichasProductoAlmacen fichas = null,
            IServicioEtiquetasHueco etiquetas = null, IServicioCambioHuecoPicking cambioHueco = null,
            Infraestructure.Notificaciones.IAvisosAlmacen avisosAlmacen = null,
            Infraestructure.Agencias.IServicioEtiquetaPacking etiquetaPacking = null,
            IServicioCambioHuecoReposicion cambioHuecoReposicion = null,
            IServicioFichaProductoAlmacen fichaProducto = null)
        {
            this.fichaProducto = fichaProducto;
            this.cambioHuecoReposicion = cambioHuecoReposicion;
            this.etiquetaPacking = etiquetaPacking;
            this.avisosAlmacen = avisosAlmacen;
            this.cambioHueco = cambioHueco;
            this.servicio = servicio;
            this.ubicaciones = ubicaciones;
            this.compras = compras;
            this.reposiciones = reposiciones;
            this.fichas = fichas;
            this.etiquetas = etiquetas;
        }

        // GET api/Almacen/Ping
        /// <summary>
        /// Para el indicador de conexión de la app: responde sin tocar la base de datos. La hora del
        /// servidor sirve para detectar un móvil con el reloj mal (los escaneos llevan la hora del móvil).
        /// </summary>
        [HttpGet]
        [Route("Ping")]
        [ResponseType(typeof(PingAlmacenDTO))]
        public IHttpActionResult GetPing()
        {
            return Ok(new PingAlmacenDTO
            {
                HoraServidor = DateTime.Now,
                Usuario = Usuario(),
                FotosConfiguradas = servicio.FotosConfiguradas
            });
        }

        // GET api/Almacen/Picking/99633?empresa=1
        /// <summary>El recorrido del picking: un producto por hueco, ordenado para andar lo menos posible.</summary>
        [HttpGet]
        [Route("Picking/{picking:int}")]
        [ResponseType(typeof(PickingAlmacenDTO))]
        public async Task<IHttpActionResult> GetPicking(int picking, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            PickingAlmacenDTO resultado = await servicio.LeerPicking(Empresa(empresa), picking).ConfigureAwait(false);
            await Task.WhenAll(
                CompletarCodigos(Empresa(empresa), resultado?.Lineas),
                CompletarMismoHueco(Empresa(empresa), resultado?.Almacen, resultado?.Lineas)).ConfigureAwait(false);
            return resultado.Lineas.Count == 0 ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        // GET api/Almacen/Picking/EnCurso?almacen=ALG&empresa=1
        /// <summary>Los pickings sacados que todavía tienen líneas sin servir, el más reciente primero.</summary>
        [HttpGet]
        [Route("Picking/EnCurso")]
        [ResponseType(typeof(List<PickingEnCursoDTO>))]
        public async Task<IHttpActionResult> GetPickingsEnCurso(string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await servicio.LeerPickingsEnCurso(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false));
        }

        // GET api/Almacen/Picking/99633/Estado?empresa=1
        /// <summary>
        /// Cómo va el picking: lo que había que coger frente a lo leído y lo dado por falta. Sirve
        /// para retomarlo o para que dos mozos se lo repartan.
        /// </summary>
        [HttpGet]
        [Route("Picking/{picking:int}/Estado")]
        [ResponseType(typeof(EstadoPickingDTO))]
        public async Task<IHttpActionResult> GetEstadoDelPicking(int picking, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            EstadoPickingDTO estado = await servicio.LeerEstadoPicking(Empresa(empresa), picking).ConfigureAwait(false);
            return estado == null ? (IHttpActionResult)NotFound() : Ok(estado);
        }

        // GET api/Almacen/Recogidas?almacen=ALG&empresa=1
        /// <summary>
        /// NestoAPI#574: lo que hay por recoger, sea un picking de pedidos o una reposición a tienda.
        /// Para el mozo es el mismo trabajo y la misma pantalla.
        /// </summary>
        [HttpGet]
        [Route("Recogidas")]
        [ResponseType(typeof(List<RecogidaPendienteDTO>))]
        public async Task<IHttpActionResult> GetRecogidas(string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await servicio.LeerRecogidasPendientes(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false));
        }

        // GET api/Almacen/Recogidas/PICK/99633?empresa=1
        /// <summary>
        /// El recorrido de una recogida y cómo va, en una sola llamada: cada parada con lo que ya está
        /// hecho, y por cuál seguir. Así la app abre donde se dejó sin que el mozo toque nada.
        /// NestoAPI#640: cada parada dice quién la ha resuelto (Lectores). Con <paramref name="ligero"/>, lo mismo sin la
        /// ficha, la foto, los códigos ni los vecinos de hueco de cada producto: para sondear cada pocos segundos lo que
        /// coge otra PDA (la app ya tiene lo demás de cuando la abrió).
        /// </summary>
        [HttpGet]
        [Route("Recogidas/{tipo}/{numero:int}")]
        [ResponseType(typeof(RecogidaAlmacenDTO))]
        public async Task<IHttpActionResult> GetRecogida(string tipo, int numero, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
            bool ligero = false)
        {
            if (CasadorEscaneos.NormalizarTipoOrigen(tipo) == null)
            {
                return BadRequest("El tipo de recogida tiene que ser PICK o REPO.");
            }
            RecogidaAlmacenDTO recogida = await servicio.LeerRecogida(Empresa(empresa), tipo, numero).ConfigureAwait(false);
            if (recogida == null)
            {
                return NotFound();
            }
            MarcarLoPropio(recogida);
            if (!ligero)
            {
                await Task.WhenAll(
                    CompletarFichas(Empresa(empresa), recogida.Lineas),
                    CompletarMismoHueco(Empresa(empresa), recogida.Almacen, recogida.Lineas)).ConfigureAwait(false);
            }
            return Ok(recogida);
        }

        /// <summary>NestoAPI#640: lo que ha leído el mismo usuario que pregunta (con el mismo nombre que se guarda en los escaneos).</summary>
        private void MarcarLoPropio(RecogidaAlmacenDTO recogida)
        {
            string yo = UsuarioAuditoriaHelper.ParaAuditoria(Usuario());
            foreach (LectorLineaRecogidaDTO lector in recogida.Lineas.SelectMany(l => l.Lectores ?? new List<LectorLineaRecogidaDTO>()))
            {
                lector.Propio = string.Equals(lector.Usuario?.Trim(), yo, StringComparison.OrdinalIgnoreCase);
            }
        }

        // POST api/Almacen/Recogidas/PICK/99633/Terminar?empresa=1
        /// <summary>
        /// NestoAPI#556: el mozo da por terminada una salida (picking o reposición): todo cogido o dado por falta. Lo que
        /// se hace al terminar depende del tipo; si no queda resuelto, 409 con lo que falta. Sin permiso, 403 con el motivo.
        /// </summary>
        [HttpPost]
        [Route("Recogidas/{tipo}/{numero:int}/Terminar")]
        [ResponseType(typeof(ResultadoTerminarSalidaDTO))]
        public async Task<IHttpActionResult> PostTerminarRecogida(string tipo, int numero, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool ensayo = false)
        {
            ResultadoTerminarSalida resultado = await servicio.TerminarRecogida(Empresa(empresa), tipo, numero, User, ensayo).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoTerminarSalida.Terminada:
                    if (!ensayo)
                    {
                        Avisar(Infraestructure.Notificaciones.AvisosAlmacen.GrupoRecogida(tipo, numero)); // NestoAPI#646
                    }
                    return Ok(resultado.Salida);
                case EstadoTerminarSalida.SinPermiso:
                    return Content(HttpStatusCode.Forbidden, resultado.Mensaje);
                case EstadoTerminarSalida.SinTerminar:
                    return Content(HttpStatusCode.Conflict, resultado.Mensaje);
                case EstadoTerminarSalida.NoExiste:
                    return NotFound();
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }

        // GET api/Almacen/Picking/99633/Alternativas?producto=22624&hueco=001007002&empresa=1
        /// <summary>
        /// Ariadna#12: el mozo no encuentra el producto en el hueco de la parada. Los otros huecos del almacén donde hay
        /// libre de ese producto, en el orden del recorrido (lista vacía si no hay ninguno: entonces es una falta).
        /// </summary>
        [HttpGet]
        [Route("Picking/{picking:int}/Alternativas")]
        [ResponseType(typeof(List<HuecoAlternativoDTO>))]
        public Task<IHttpActionResult> GetAlternativasDelPicking(int picking, string producto, string hueco = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return GetAlternativasDeLaRecogida(CasadorEscaneos.ORIGEN_PICKING, picking, producto, hueco, empresa);
        }

        // GET api/Almacen/Recogidas/REPO/81143/Alternativas?producto=22624&hueco=001007002&empresa=1
        /// <summary>
        /// Ariadna#12: lo mismo que Picking/{n}/Alternativas para cualquier salida: PICK (el picking de siempre) y REPO (la
        /// salida de una reposición: lo libre del almacén de origen). Un tipo que no admite cambiar de hueco, lista vacía.
        /// </summary>
        [HttpGet]
        [Route("Recogidas/{tipo}/{numero:int}/Alternativas")]
        [ResponseType(typeof(List<HuecoAlternativoDTO>))]
        public async Task<IHttpActionResult> GetAlternativasDeLaRecogida(string tipo, int numero, string producto, string hueco = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            switch (CasadorEscaneos.NormalizarTipoOrigen(tipo))
            {
                case CasadorEscaneos.ORIGEN_PICKING when cambioHueco != null:
                    return Ok(await cambioHueco.LeerAlternativas(Empresa(empresa), numero, producto, hueco).ConfigureAwait(false));
                case CasadorEscaneos.ORIGEN_REPOSICION when cambioHuecoReposicion != null:
                    return Ok(await cambioHuecoReposicion.LeerAlternativas(Empresa(empresa), numero, producto, hueco).ConfigureAwait(false));
                default:
                    return Ok(new List<HuecoAlternativoDTO>());
            }
        }

        // POST api/Almacen/Picking/99633/CambiarHueco?empresa=1   { "Producto": "22624", "HuecoOrigen": "001007002", "HuecoDestino": "003001002", "Cantidad": 2 }
        /// <summary>
        /// Ariadna#12: lo que no estaba en el hueco de la parada se coge de otro: la reserva del picking pasa a ese hueco
        /// (y lo que no estaba queda «pendiente de ubicar», como en una falta). La línea del pedido no cambia. Si ya no
        /// hay nada que cambiar (otro mozo se ha adelantado), 409 con el motivo.
        /// </summary>
        [HttpPost]
        [Route("Picking/{picking:int}/CambiarHueco")]
        [ResponseType(typeof(ResultadoCambiarHuecoDTO))]
        public Task<IHttpActionResult> PostCambiarHueco(int picking, [FromBody] CambiarHuecoPickingDTO cambio,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool ensayo = false)
        {
            return PostCambiarHuecoDeLaRecogida(CasadorEscaneos.ORIGEN_PICKING, picking, cambio, empresa, ensayo);
        }

        // POST api/Almacen/Recogidas/REPO/81143/CambiarHueco?empresa=1   { "Producto": "22624", "HuecoOrigen": "001007002", "HuecoDestino": "003001002", "Cantidad": 2 }
        /// <summary>
        /// Ariadna#12: lo mismo que Picking/{n}/CambiarHueco para cualquier salida. PICK: la reserva del picking (lo que haya
        /// libre). REPO: la reserva de la reposición (estado 4) pasa al otro hueco, todo o nada, mientras la salida no esté
        /// terminada; lo que no estaba queda «pendiente de ubicar». 409 con el motivo si no se puede; 400 si el tipo no lo
        /// admite. Con <paramref name="ensayo"/> (Admin o Dirección; si no, 403), todo en una transacción que se deshace
        /// siempre: 200 con las filas de Ubicaciones antes y después.
        /// </summary>
        [HttpPost]
        [Route("Recogidas/{tipo}/{numero:int}/CambiarHueco")]
        [ResponseType(typeof(ResultadoCambiarHuecoDTO))]
        public async Task<IHttpActionResult> PostCambiarHuecoDeLaRecogida(string tipo, int numero, [FromBody] CambiarHuecoPickingDTO cambio,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool ensayo = false)
        {
            if (ensayo && !ServicioSalidas.PuedeEnsayar(User))
            {
                return Content(HttpStatusCode.Forbidden, ServicioSalidas.MENSAJE_ENSAYO_SIN_PERMISO);
            }
            string tipoSalida = CasadorEscaneos.NormalizarTipoOrigen(tipo);
            ResultadoCambioHueco resultado;
            if (tipoSalida == CasadorEscaneos.ORIGEN_PICKING && cambioHueco != null)
            {
                resultado = ensayo
                    ? await cambioHueco.Ensayar(Empresa(empresa), numero, cambio, Usuario()).ConfigureAwait(false)
                    : await cambioHueco.Cambiar(Empresa(empresa), numero, cambio, Usuario()).ConfigureAwait(false);
            }
            else if (tipoSalida == CasadorEscaneos.ORIGEN_REPOSICION && cambioHuecoReposicion != null)
            {
                resultado = await cambioHuecoReposicion.Cambiar(Empresa(empresa), numero, cambio, Usuario(), ensayo).ConfigureAwait(false);
            }
            else
            {
                return BadRequest(tipoSalida == null
                    ? $"El tipo de salida tiene que ser {CasadorEscaneos.ORIGEN_PICKING} o {CasadorEscaneos.ORIGEN_REPOSICION}."
                    : "Este servidor todavía no sabe cambiar de hueco en este tipo de salida.");
            }

            if (resultado.Ensayo && resultado.Estado != EstadoCambioHueco.NoValido)
            {
                return Ok(Dto(resultado));
            }
            switch (resultado.Estado)
            {
                case EstadoCambioHueco.Cambiado:
                    Avisar(Infraestructure.Notificaciones.AvisosAlmacen.GrupoRecogida(tipoSalida, numero)); // NestoAPI#646
                    return Ok(Dto(resultado));
                case EstadoCambioHueco.NoSePuede:
                    return Content(HttpStatusCode.Conflict, resultado.Mensaje);
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }

        private static ResultadoCambiarHuecoDTO Dto(ResultadoCambioHueco resultado)
        {
            var dto = new ResultadoCambiarHuecoDTO { Movidas = resultado.Movidas, Mensaje = resultado.Mensaje };
            if (resultado.Ensayo)
            {
                dto.Ensayo = true;
                dto.FilasAntes = resultado.FilasAntes;
                dto.FilasDespues = resultado.FilasDespues;
                dto.ErrorEnsayo = resultado.ErrorEnsayo;
            }
            return dto;
        }

        // POST api/Almacen/Recogidas/PICK/99739/AnularLecturas?empresa=1   { "Usuario": "Pedro" }
        /// <summary>
        /// Ariadna#6: descarta TODO lo que un mozo ha leído en una salida (una prueba olvidada en la cola de su PDA), como si
        /// lo hubiera deshecho. Solo Admin o Dirección (403); si la salida ya está terminada, 409 y no se toca nada.
        /// </summary>
        [HttpPost]
        [Route("Recogidas/{tipo}/{numero:int}/AnularLecturas")]
        [ResponseType(typeof(ResultadoAnularLecturasDTO))]
        public async Task<IHttpActionResult> PostAnularLecturas(string tipo, int numero, [FromBody] AnularLecturasDTO peticion,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(peticion?.Usuario))
            {
                return BadRequest("Falta de qué mozo son las lecturas.");
            }
            ResultadoAnularLecturas resultado = await servicio.AnularLecturasRecogida(Empresa(empresa), tipo, numero, peticion.Usuario.Trim(), User)
                .ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoAnularLecturas.Anuladas:
                    if (resultado.Filas > 0)
                    {
                        // NestoAPI#710: puede tocar muchas paradas de golpe: las demás PDA releen la salida
                        Avisar(Infraestructure.Notificaciones.AvisosAlmacen.GrupoRecogida(tipo, numero));
                    }
                    return Ok(new ResultadoAnularLecturasDTO { Filas = resultado.Filas, Mensaje = resultado.Mensaje });
                case EstadoAnularLecturas.SinPermiso:
                    return Content(HttpStatusCode.Forbidden, resultado.Mensaje);
                case EstadoAnularLecturas.YaTerminada:
                    return Content(HttpStatusCode.Conflict, resultado.Mensaje);
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }

        // GET api/Almacen/Picking/99633/Packing?empresa=1
        /// <summary>
        /// Lo que hay que meter en cajas de todo un picking, agrupado por entrega y por pedido. NestoAPI#646: cada entrega
        /// lleva sus bultos y, si otro usuario la está empaquetando (en los últimos 5 minutos), quién y desde cuándo
        /// (AvisoEmpaquetando). Con <paramref name="ligero"/>, sin la ficha ni la foto de cada producto: para sondear.
        /// </summary>
        [HttpGet]
        [Route("Picking/{picking:int}/Packing")]
        [ResponseType(typeof(PackingAlmacenDTO))]
        public async Task<IHttpActionResult> GetPackingDelPicking(int picking, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool ligero = false)
        {
            PackingAlmacenDTO resultado = await servicio.LeerPacking(Empresa(empresa), picking).ConfigureAwait(false);
            if (!ligero)
            {
                await CompletarFichas(Empresa(empresa), LineasDe(resultado)).ConfigureAwait(false);
            }
            ServicioPreparacionAlmacen.PonerQuienEmpaqueta(resultado, Usuario(), DateTime.Now);
            return resultado == null || resultado.Entregas.Count == 0 ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        // GET api/Almacen/Packing/Pendientes?almacen=ALG&empresa=1
        /// <summary>
        /// Ariadna («Empaquetar»): los pickings del almacén con alguna entrega (cliente + dirección) sin sus bultos con
        /// foto, el más reciente primero, se hayan recogido con Ariadna o en papel. El criterio está en <see cref="PackingPendienteDTO"/>.
        /// </summary>
        [HttpGet]
        [Route("Packing/Pendientes")]
        [ResponseType(typeof(List<PackingPendienteDTO>))]
        public async Task<IHttpActionResult> GetPackingsPendientes(string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            List<PackingPendienteDTO> pendientes = await servicio.LeerPackingsPendientes(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false);
            // NestoAPI#646: de cada picking, si otro usuario está empaquetando algo de él
            ServicioPreparacionAlmacen.PonerQuienEmpaqueta(pendientes, Usuario(), DateTime.Now);
            return Ok(pendientes);
        }

        // GET api/Almacen/Pedidos/926940/Packing?empresa=1
        /// <summary>
        /// El packing de un pedido (el que se abre al leer el código del pedido en la mesa). NestoAPI#646: con sus bultos y
        /// quién lo está empaquetando, como el del picking; con <paramref name="ligero"/>, sin fichas (Agencias de Nesto lo
        /// usa para avisar si alguien lo está empaquetando todavía).
        /// </summary>
        [HttpGet]
        [Route("Pedidos/{pedido:int}/Packing")]
        [ResponseType(typeof(PackingAlmacenDTO))]
        public async Task<IHttpActionResult> GetPackingDelPedido(int pedido, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool ligero = false)
        {
            PackingAlmacenDTO resultado = await servicio.LeerPackingDePedido(Empresa(empresa), pedido).ConfigureAwait(false);
            if (!ligero)
            {
                await CompletarFichas(Empresa(empresa), LineasDe(resultado)).ConfigureAwait(false);
            }
            ServicioPreparacionAlmacen.PonerQuienEmpaqueta(resultado, Usuario(), DateTime.Now);
            return resultado == null || resultado.Entregas.Count == 0 ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        // GET api/Almacen/Pedidos/926940/Preparacion?empresa=1&picking=99633
        /// <summary>Lo pedido frente a lo metido en las cajas, y los bultos con su foto.</summary>
        [HttpGet]
        [Route("Pedidos/{pedido:int}/Preparacion")]
        [ResponseType(typeof(EstadoPreparacionPedidoDTO))]
        public async Task<IHttpActionResult> GetPreparacionDelPedido(int pedido, int? picking = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            EstadoPreparacionPedidoDTO resultado = await servicio.LeerEstadoPedido(Empresa(empresa), pedido, picking).ConfigureAwait(false);
            return resultado == null ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        // POST api/Almacen/Escaneos?empresa=1
        /// <summary>
        /// Guarda un lote de escaneos de la cola del móvil. Se puede reenviar sin miedo: cada escaneo
        /// lleva su IdCliente y no se guarda dos veces.
        /// </summary>
        [HttpPost]
        [Route("Escaneos")]
        [ResponseType(typeof(ResultadoEscaneosAlmacenDTO))]
        public async Task<IHttpActionResult> PostEscaneos([FromBody] List<EscaneoAlmacenDTO> escaneos,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (escaneos == null || escaneos.Count == 0)
            {
                return BadRequest("No ha llegado ningún escaneo.");
            }
            ResultadoEscaneosAlmacenDTO guardados = await servicio.GuardarEscaneos(Empresa(empresa), escaneos, Usuario()).ConfigureAwait(false);
            if (guardados?.Guardados > 0)
            {
                // NestoAPI#710: a las demás PDA de cada documento tocado, lo grabado CON SUS DATOS (y el «haCambiado» de siempre,
                // según Almacen:AvisosConDatos). Lo rechazado no se manda; lo repetido sí (la PDA que lo recibe no lo cuenta dos veces)
                string dispositivo = escaneos.Select(e => e?.Dispositivo).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
                var rechazados = new HashSet<Guid>((guardados.Rechazados ?? new List<EscaneoRechazadoDTO>()).Select(r => r.IdCliente));
                string usuario = UsuarioAuditoriaHelper.ParaAuditoria(Usuario());
                foreach (KeyValuePair<string, List<LecturaAlmacenAvisoDTO>> grupo in LecturasPorGrupo(escaneos.Where(e => e != null && !rechazados.Contains(e.IdCliente)), usuario))
                {
                    AvisarLecturas(grupo.Key, grupo.Value, dispositivo);
                }
            }
            return Ok(guardados);
        }

        /// <summary>
        /// NestoAPI#710: las lecturas de un lote de escaneos, por el grupo de SignalR de su documento (recorrido o packing), en el
        /// orden en que llegaron y sin repetir IdCliente. <paramref name="usuario"/>: el que se guarda en PreparacionEscaneos.
        /// </summary>
        internal static Dictionary<string, List<LecturaAlmacenAvisoDTO>> LecturasPorGrupo(IEnumerable<EscaneoAlmacenDTO> escaneos, string usuario)
        {
            var porGrupo = new Dictionary<string, List<LecturaAlmacenAvisoDTO>>(StringComparer.OrdinalIgnoreCase);
            var vistos = new HashSet<Guid>();
            foreach (EscaneoAlmacenDTO e in escaneos ?? Enumerable.Empty<EscaneoAlmacenDTO>())
            {
                if (e == null || CasadorEscaneos.NumeroOrigenDe(e) <= 0 || !vistos.Add(e.IdCliente))
                {
                    continue;
                }
                string grupo = GrupoDe(e);
                if (!porGrupo.TryGetValue(grupo, out List<LecturaAlmacenAvisoDTO> lista))
                {
                    porGrupo[grupo] = lista = new List<LecturaAlmacenAvisoDTO>();
                }
                lista.Add(new LecturaAlmacenAvisoDTO
                {
                    IdCliente = e.IdCliente,
                    TipoOrigen = CasadorEscaneos.TipoOrigenDe(e),
                    NumeroOrigen = CasadorEscaneos.NumeroOrigenDe(e),
                    Pedido = e.Pedido,
                    LineaPedido = e.LineaPedido,
                    Producto = e.Producto?.Trim(),
                    Cantidad = e.Cantidad,
                    Metodo = e.Metodo?.Trim().ToUpperInvariant(),
                    Fase = e.Fase?.Trim().ToUpperInvariant(),
                    Bulto = e.Bulto,
                    Motivo = e.Motivo?.Trim(),
                    Usuario = usuario,
                    FechaEscaneo = e.FechaEscaneo
                });
            }
            return porGrupo;
        }

        /// <summary>NestoAPI#646: el grupo de SignalR del documento de un escaneo (recorrido o packing).</summary>
        private static string GrupoDe(EscaneoAlmacenDTO e)
        {
            return string.Equals(e.Fase?.Trim(), CasadorEscaneos.FASE_PACKING, StringComparison.OrdinalIgnoreCase)
                ? Infraestructure.Notificaciones.AvisosAlmacen.GrupoPacking(CasadorEscaneos.NumeroOrigenDe(e))
                : Infraestructure.Notificaciones.AvisosAlmacen.GrupoRecogida(CasadorEscaneos.TipoOrigenDe(e), CasadorEscaneos.NumeroOrigenDe(e));
        }

        /// <summary>
        /// NestoAPI#646: a las demás PDA del grupo, «ha cambiado». A la que hace la petición no: su conexión llega en la cabecera
        /// X-Conexion-SignalR y, si no, se excluyen las de su dispositivo (cabecera X-Dispositivo o el de la petición). El aviso
        /// nunca rompe la petición (lo que se ha guardado, guardado está).
        /// </summary>
        private void Avisar(string grupo, string dispositivoDeLaPeticion = null)
        {
            AvisarCon(grupo, dispositivoDeLaPeticion, (avisos, usuario, conexion, dispositivo) => avisos.HaCambiado(grupo, usuario, conexion, dispositivo));
        }

        /// <summary>NestoAPI#710: como <see cref="Avisar"/>, con las lecturas grabadas (sus datos).</summary>
        private void AvisarLecturas(string grupo, List<LecturaAlmacenAvisoDTO> lecturas, string dispositivoDeLaPeticion)
        {
            AvisarCon(grupo, dispositivoDeLaPeticion, (avisos, usuario, conexion, dispositivo) => avisos.Lecturas(grupo, usuario, conexion, dispositivo, lecturas));
        }

        /// <summary>Lo común de los avisos: quién lo ha hecho y desde qué conexión y dispositivo. Nunca rompe la petición.</summary>
        private void AvisarCon(string grupo, string dispositivoDeLaPeticion, Action<Infraestructure.Notificaciones.IAvisosAlmacen, string, string, string> avisar)
        {
            if (avisosAlmacen == null || string.IsNullOrWhiteSpace(grupo))
            {
                return;
            }
            try
            {
                string dispositivo = Cabecera(Infraestructure.Notificaciones.AvisosAlmacen.CABECERA_DISPOSITIVO) ?? dispositivoDeLaPeticion?.Trim();
                avisar(avisosAlmacen, UsuarioAuditoriaHelper.ParaAuditoria(Usuario()),
                    Cabecera(Infraestructure.Notificaciones.AvisosAlmacen.CABECERA_CONEXION), string.IsNullOrWhiteSpace(dispositivo) ? null : dispositivo);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex);
            }
        }

        // POST api/Almacen/Escaneos/Corregir?empresa=1   { "IdCliente": "…", "TipoOrigen": "PICK", "NumeroOrigen": 99739, "Producto": "22624",
        //   "Hueco": "001/007/002", "Orden": 3, "Tipo": "FALTA", "Unidades": 1, "EsperadoCogidas": 2, "EsperadoFaltas": 1 }
        /// <summary>
        /// NestoAPI#646 / Ariadna#41: quita una falta (Tipo FALTA) o lo cogido (LECTURA) de una parada concreta, la haya hecho la
        /// PDA que la haya hecho, de forma atómica: solo si la parada sigue con lo cogido y lo dado por falta que vio la app
        /// (EsperadoCogidas, EsperadoFaltas). 200 con la parada corregida (también un reenvío del mismo IdCliente, con
        /// Repetida). 409 con cómo está ahora si ha cambiado (otra PDA a la vez), si ya no está o si la salida ya está
        /// terminada: no se toca nada. 400 si la petición no vale.
        /// </summary>
        [HttpPost]
        [Route("Escaneos/Corregir")]
        [ResponseType(typeof(ResultadoCorregirEscaneoDTO))]
        public async Task<IHttpActionResult> PostCorregirEscaneo([FromBody] CorregirEscaneoDTO correccion,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ResultadoCorreccionLectura resultado = await servicio.CorregirLectura(Empresa(empresa), correccion, User).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoCorreccionLectura.Corregida:
                    AvisarCorreccion(correccion, resultado.Respuesta); // NestoAPI#646 / #710
                    return Ok(resultado.Respuesta);
                case EstadoCorreccionLectura.Repetida:
                    return Ok(resultado.Respuesta);
                case EstadoCorreccionLectura.HaCambiado:
                case EstadoCorreccionLectura.YaTerminada:
                    return Content(HttpStatusCode.Conflict, resultado.Respuesta);
                default:
                    return BadRequest(resultado.Respuesta?.Mensaje);
            }
        }

        /// <summary>NestoAPI#710: a las demás PDA de la salida, la parada corregida con lo necesario para deshacerla en local.</summary>
        private void AvisarCorreccion(CorregirEscaneoDTO correccion, ResultadoCorregirEscaneoDTO respuesta)
        {
            string grupo = Infraestructure.Notificaciones.AvisosAlmacen.GrupoRecogida(correccion.TipoOrigen, correccion.NumeroOrigen);
            AvisarCon(grupo, correccion.Dispositivo, (avisos, usuario, conexion, dispositivo) => avisos.Correccion(grupo, usuario, conexion, dispositivo,
                new CorreccionAlmacenAvisoDTO
                {
                    IdCliente = correccion.IdCliente,
                    TipoOrigen = CasadorEscaneos.NormalizarTipoOrigen(correccion.TipoOrigen) ?? correccion.TipoOrigen?.Trim().ToUpperInvariant(),
                    NumeroOrigen = correccion.NumeroOrigen,
                    Producto = correccion.Producto?.Trim(),
                    Hueco = correccion.Hueco?.Trim(),
                    Orden = correccion.Orden,
                    Tipo = correccion.Tipo?.Trim().ToUpperInvariant(),
                    Unidades = correccion.Unidades,
                    Cogidas = respuesta?.Cogidas ?? 0,
                    Faltas = respuesta?.Faltas ?? 0,
                    Usuario = usuario,
                    FechaEscaneo = correccion.FechaEscaneo
                }));
        }

        // POST api/Almacen/Bultos/Foto?idCliente=...&pedido=926940&picking=99633&bulto=1&otrosPedidos=926941
        /// <summary>
        /// La foto de un bulto antes de cerrarlo. El cuerpo de la petición es la imagen (image/jpeg),
        /// sin envoltorio. Repetir la foto de un bulto sustituye a la anterior. Si en la caja van
        /// también otros pedidos del mismo cliente, sus números van en otrosPedidos, separados por comas.
        /// </summary>
        [HttpPost]
        [Route("Bultos/Foto")]
        [ResponseType(typeof(BultoAlmacenDTO))]
        public async Task<IHttpActionResult> PostFotoBulto(Guid idCliente, int pedido, int picking, int bulto,
            DateTime? fechaFoto = null, string dispositivo = null, string otrosPedidos = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            var otros = new List<int>();
            foreach (string texto in (otrosPedidos ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(texto.Trim(), out int otroPedido))
                {
                    return BadRequest($"«{texto.Trim()}» no es un número de pedido.");
                }
                otros.Add(otroPedido);
            }

            long? tamano = Request.Content?.Headers?.ContentLength;
            if (tamano > ServicioPreparacionAlmacen.TAMANO_MAXIMO_FOTO)
            {
                return BadRequest($"La foto pesa demasiado (máximo {ServicioPreparacionAlmacen.TAMANO_MAXIMO_FOTO / 1024} KB).");
            }

            byte[] imagen = Request.Content == null
                ? null
                : await Request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            BultoAlmacenDTO guardado = await servicio.GuardarFotoBulto(new FotoBultoAlmacen
            {
                IdCliente = idCliente,
                Empresa = Empresa(empresa),
                Pedido = pedido,
                OtrosPedidos = otros,
                Picking = picking,
                Bulto = bulto,
                Imagen = imagen,
                FechaFoto = fechaFoto,
                Dispositivo = dispositivo
            }, Usuario()).ConfigureAwait(false);

            Avisar(Infraestructure.Notificaciones.AvisosAlmacen.GrupoPacking(picking), dispositivo); // NestoAPI#646
            return Ok(guardado);
        }

        // GET api/Almacen/Pedidos/926940/Bultos?empresa=1
        [HttpGet]
        [Route("Pedidos/{pedido:int}/Bultos")]
        [ResponseType(typeof(List<BultoAlmacenDTO>))]
        public async Task<IHttpActionResult> GetBultosDelPedido(int pedido, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await servicio.LeerBultos(Empresa(empresa), pedido).ConfigureAwait(false));
        }

        /// <summary>NestoAPI#771: «926941,926942» → la lista; null y el texto que no es un número si alguno no lo es.</summary>
        internal static List<int> LeerOtrosPedidos(string otrosPedidos, out string malo)
        {
            malo = null;
            var otros = new List<int>();
            foreach (string texto in (otrosPedidos ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(texto.Trim(), out int otroPedido))
                {
                    malo = texto.Trim();
                    return null;
                }
                otros.Add(otroPedido);
            }
            return otros;
        }

        // POST api/Almacen/Bultos/Cerrar?idCliente=...&pedido=926940&picking=99633&bulto=2&otrosPedidos=926941   (sin cuerpo)
        /// <summary>
        /// NestoAPI#771: cierra una caja SIN foto (las fotos del packing ya no van ligadas a los bultos: van por
        /// <c>Packing/Fotos</c>). Mismas reglas que <c>Bultos/Foto</c>: si otra PDA ya tiene ese número en la entrega, el siguiente
        /// libre (<c>Renumerado</c>); los otros pedidos de la caja, cada uno con su fila; repetir con el mismo idCliente no duplica.
        /// Cerrar sin foto nunca borra la foto de un bulto que ya la tenía.
        /// </summary>
        [HttpPost]
        [Route("Bultos/Cerrar")]
        [ResponseType(typeof(BultoAlmacenDTO))]
        public async Task<IHttpActionResult> PostCerrarBulto(Guid idCliente, int pedido, int picking, int bulto,
            DateTime? fechaFoto = null, string dispositivo = null, string otrosPedidos = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            List<int> otros = LeerOtrosPedidos(otrosPedidos, out string malo);
            if (otros == null)
            {
                return BadRequest($"«{malo}» no es un número de pedido.");
            }
            BultoAlmacenDTO guardado = await servicio.CerrarBultoSinFoto(new FotoBultoAlmacen
            {
                IdCliente = idCliente,
                Empresa = Empresa(empresa),
                Pedido = pedido,
                OtrosPedidos = otros,
                Picking = picking,
                Bulto = bulto,
                FechaFoto = fechaFoto,
                Dispositivo = dispositivo
            }, Usuario()).ConfigureAwait(false);

            Avisar(Infraestructure.Notificaciones.AvisosAlmacen.GrupoPacking(picking), dispositivo); // NestoAPI#646
            return Ok(guardado);
        }

        // POST api/Almacen/Packing/Fotos?idCliente=...&pedido=926940&picking=99633&otrosPedidos=926941   (cuerpo: image/jpeg)
        /// <summary>
        /// NestoAPI#771: una foto del packing SIN bulto (regla de Carlos 31/10/26: mínimo una por entrega, las que quieran, sin
        /// relación con el número de cajas). El cuerpo es la imagen, sin envoltorio. Repetir con el mismo idCliente (la cola de la
        /// PDA) no la duplica. Los otros pedidos de la entrega, separados por comas: cada uno con su fila y la misma foto.
        /// 400 si falta Scripts/Issue771_FotosPackingYSeguimiento.sql.
        /// </summary>
        [HttpPost]
        [Route("Packing/Fotos")]
        [ResponseType(typeof(FotoPackingAlmacenDTO))]
        public async Task<IHttpActionResult> PostFotoPacking(Guid idCliente, int pedido, int picking,
            DateTime? fechaFoto = null, string dispositivo = null, string otrosPedidos = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            List<int> otros = LeerOtrosPedidos(otrosPedidos, out string malo);
            if (otros == null)
            {
                return BadRequest($"«{malo}» no es un número de pedido.");
            }
            long? tamano = Request.Content?.Headers?.ContentLength;
            if (tamano > ServicioPreparacionAlmacen.TAMANO_MAXIMO_FOTO)
            {
                return BadRequest($"La foto pesa demasiado (máximo {ServicioPreparacionAlmacen.TAMANO_MAXIMO_FOTO / 1024} KB).");
            }
            byte[] imagen = Request.Content == null
                ? null
                : await Request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            FotoPackingAlmacenDTO guardada = await servicio.GuardarFotoPacking(new FotoBultoAlmacen
            {
                IdCliente = idCliente,
                Empresa = Empresa(empresa),
                Pedido = pedido,
                OtrosPedidos = otros,
                Picking = picking,
                Imagen = imagen,
                FechaFoto = fechaFoto,
                Dispositivo = dispositivo
            }, Usuario()).ConfigureAwait(false);

            Avisar(Infraestructure.Notificaciones.AvisosAlmacen.GrupoPacking(picking), dispositivo);
            return Ok(guardada);
        }

        // GET api/Almacen/Pedidos/926940/FotosPacking?empresa=1
        /// <summary>NestoAPI#771: las fotos del packing sin bulto del pedido (todas sus entregas), con su enlace público.</summary>
        [HttpGet]
        [Route("Pedidos/{pedido:int}/FotosPacking")]
        [ResponseType(typeof(List<FotoPackingAlmacenDTO>))]
        public async Task<IHttpActionResult> GetFotosPackingDelPedido(int pedido, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await servicio.LeerFotosPacking(Empresa(empresa), pedido).ConfigureAwait(false));
        }

        // GET api/Almacen/FotosPacking/17/Foto
        /// <summary>NestoAPI#771: enlace temporal a una foto del packing (como <c>Bultos/{id}/Foto</c>).</summary>
        [HttpGet]
        [Route("FotosPacking/{id:int}/Foto")]
        [ResponseType(typeof(EnlaceFotoBultoDTO))]
        public async Task<IHttpActionResult> GetFotoPacking(int id)
        {
            Uri enlace = await servicio.EnlaceFotoPacking(id).ConfigureAwait(false);
            if (enlace == null)
            {
                return NotFound();
            }
            return Ok(new EnlaceFotoBultoDTO
            {
                Url = enlace.AbsoluteUri,
                MinutosDeVigencia = (int)ServicioPreparacionAlmacen.VIGENCIA_ENLACE_FOTO.TotalMinutes
            });
        }

        // POST api/Almacen/Pedidos/926940/Etiqueta   { "Empresa": "1", "Peso": 3.5, "Impresora": null, "Confirmaciones": [],
        //   "DescontarPrepagos": null, "Dimensiones": null }
        /// <summary>
        /// NestoAPI#595 (slice 5, Ariadna#13): la etiqueta de agencia de la entrega al terminarla en el packing, en una llamada:
        /// graba el envío desde el pedido (DesdePedido) con las cajas de la entrega, lo registra en la agencia (Tramitar, CTT e
        /// Innovatrans) e imprime la etiqueta en el servidor (ImprimirEtiqueta); si el usuario tiene «FacturarAlImprimirEtiqueta»,
        /// factura el pedido como Facturar rutas (#592). Si el pedido ya tiene un envío en curso, sigue desde él (reintento o
        /// reimpresión): nunca graba ni registra dos veces.
        /// <list type="bullet">
        /// <item>200 <see cref="Models.Agencias.EtiquetaPackingDTO"/>: impresa, o para GLS y Canteras grabado con
        /// EtiquetaEnNesto y «La etiqueta de GLS hay que sacarla en Nesto».</item>
        /// <item>409 <see cref="Models.Agencias.ConflictoEnvioDesdePedidoDTO"/>: lo pendiente de DesdePedido; se repite con las respuestas.</item>
        /// <item>400 <see cref="Models.Agencias.ErrorEnvioDesdePedidoDTO"/> con su Codigo (los de DesdePedido y SinBultos).</item>
        /// <item>404: el pedido no existe.</item>
        /// <item>502 <see cref="Models.Agencias.EtiquetaPackingDTO"/>: el envío está grabado pero la agencia o la impresora han
        /// fallado (Codigo TramitarFallido o ImprimirFallido); repetir sigue desde ahí.</item>
        /// </list>
        /// </summary>
        [HttpPost]
        [Route("Pedidos/{pedido:int}/Etiqueta")]
        [ResponseType(typeof(Models.Agencias.EtiquetaPackingDTO))]
        public async Task<IHttpActionResult> PostEtiquetaDelPedido(int pedido, [FromBody] Models.Agencias.PeticionEtiquetaPackingDTO peticion)
        {
            if (etiquetaPacking == null)
            {
                return Content(HttpStatusCode.ServiceUnavailable, "La etiqueta de agencia desde el packing no está configurada en el servidor.");
            }
            // Sin ConfigureAwait(false): ELMAH y los controladores de agencias toman el usuario de la petición.
            Infraestructure.Agencias.ResultadoEtiquetaPacking resultado = await etiquetaPacking.Imprimir(pedido, peticion, User);
            switch (resultado.Tipo)
            {
                case Infraestructure.Agencias.TipoResultadoEtiquetaPacking.Hecha:
                    return Ok(resultado.Etiqueta);
                case Infraestructure.Agencias.TipoResultadoEtiquetaPacking.Conflicto:
                    return Content(HttpStatusCode.Conflict, resultado.Conflicto);
                case Infraestructure.Agencias.TipoResultadoEtiquetaPacking.NoEncontrado:
                    return NotFound();
                case Infraestructure.Agencias.TipoResultadoEtiquetaPacking.FalloAgencia:
                    return Content(HttpStatusCode.BadGateway, resultado.Etiqueta);
                default:
                    return Content(HttpStatusCode.BadRequest, resultado.Error);
            }
        }

        // GET api/Almacen/Bultos/17/Foto
        /// <summary>
        /// Un enlace temporal para ver la foto. Se devuelve la dirección en vez de redirigir porque una
        /// etiqueta de imagen no puede mandar el token de la API; el enlace, en cambio, no necesita nada más.
        /// </summary>
        [HttpGet]
        [Route("Bultos/{id:int}/Foto")]
        [ResponseType(typeof(EnlaceFotoBultoDTO))]
        public async Task<IHttpActionResult> GetFotoBulto(int id)
        {
            Uri enlace = await servicio.EnlaceFotoBulto(id).ConfigureAwait(false);
            if (enlace == null)
            {
                return NotFound();
            }
            return Ok(new EnlaceFotoBultoDTO
            {
                Url = enlace.AbsoluteUri,
                MinutosDeVigencia = (int)ServicioPreparacionAlmacen.VIGENCIA_ENLACE_FOTO.TotalMinutes
            });
        }

        // GET api/Almacen/Fotos/17-3f2a…   (sin usuario)
        /// <summary>
        /// La foto de un bulto para quien no tiene usuario (el cliente que reclama, la agencia): el
        /// enlace lleva una firma que no se puede adivinar y vale mientras exista la foto. Se
        /// comprueba la firma y se manda al navegador a la imagen con un acceso de unos minutos.
        /// Anónimo a propósito: lo único que protege la foto es la firma del enlace.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        [Route("Fotos/{token}")]
        public async Task<IHttpActionResult> GetFotoPublica(string token)
        {
            Uri enlace = await servicio.EnlaceFotoBultoPublico(token).ConfigureAwait(false);
            if (enlace == null)
            {
                // El mismo 404 para un enlace malo, un bulto que no existe o uno sin foto
                return NotFound();
            }
            return Redirect(enlace);
        }

        // GET api/Almacen/PendienteDeUbicar?almacen=ALG&empresa=1
        /// <summary>
        /// Lo recibido que todavía no tiene hueco, con la sugerencia de dónde ubicarlo (donde ya hay
        /// de ese producto), ordenado para ubicar andando lo menos posible.
        /// </summary>
        [HttpGet]
        [Route("PendienteDeUbicar")]
        [ResponseType(typeof(PendienteDeUbicarDTO))]
        public async Task<IHttpActionResult> GetPendienteDeUbicar(string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            PendienteDeUbicarDTO pendiente = await ubicaciones.LeerPendienteDeUbicar(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false);
            await Task.WhenAll(
                CompletarFichas(Empresa(empresa), pendiente?.Productos),
                CompletarMismoHueco(Empresa(empresa), pendiente?.Almacen, pendiente?.Productos)).ConfigureAwait(false);
            return Ok(pendiente);
        }

        // POST api/Almacen/Ubicar?empresa=1   { Producto, Almacen, Pasillo, Fila, Columna, Cantidad, AlbaranCompra? ... }
        /// <summary>
        /// Ubica en un hueco unidades recibidas que están pendientes de ubicar (lo que hoy se hace con
        /// Ariadna Vieja; usa el mismo prdUbicar). Devuelve cómo queda el producto.
        /// </summary>
        [HttpPost]
        [Route("Ubicar")]
        [ResponseType(typeof(ProductoAlmacenDTO))]
        public async Task<IHttpActionResult> PostUbicar([FromBody] UbicarProductoDTO ubicar,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ProductoAlmacenDTO producto = await ubicaciones.Ubicar(Empresa(empresa), ubicar, Usuario()).ConfigureAwait(false);
            await Task.WhenAll(
                CompletarFichas(Empresa(empresa), new[] { producto }),
                CompletarMismoHueco(Empresa(empresa), producto?.Almacen, new[] { producto })).ConfigureAwait(false);
            return Ok(producto);
        }

        // POST api/Almacen/EtiquetasHueco/Imprimir?empresa=1&almacen=ALG&ensayo=false
        /// <summary>
        /// Etiquetas de hueco (30×20 mm, el código PPPFFFCCC que lee Ariadna) en la impresora de etiquetas de producto del
        /// usuario (ImpresoraCodBarras). Huecos sueltos o un rango de un pasillo; SoloEnUso, solo los que tienen algo.
        /// Con ensayo=true no imprime: dice qué huecos y en qué impresora (vista previa). La impresión la hace el servidor.
        /// 400 si lo pedido no vale o el usuario no tiene impresora; 502 si Windows no deja imprimir.
        /// </summary>
        [HttpPost]
        [Route("EtiquetasHueco/Imprimir")]
        [ResponseType(typeof(ResultadoEtiquetasHuecoDTO))]
        public async Task<IHttpActionResult> PostImprimirEtiquetasHueco([FromBody] EtiquetasHuecoDTO peticion,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string almacen = Constantes.Almacenes.ALGETE, bool ensayo = false)
        {
            if (etiquetas == null)
            {
                return Content(HttpStatusCode.ServiceUnavailable, "La impresión de etiquetas de hueco no está configurada en el servidor.");
            }
            try
            {
                return Ok(await etiquetas.Imprimir(Empresa(empresa), Almacen(almacen), peticion, Usuario(), ensayo).ConfigureAwait(false));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (ImpresionEtiquetasException ex)
            {
                ElmahHelper.Log(ex);
                return Content(HttpStatusCode.BadGateway, ex.Message);
            }
        }

        // GET api/Almacen/Productos/Buscar?codigo=8436620930427&almacen=ALG&empresa=1
        /// <summary>
        /// Un producto por su código de barras o su número, con los huecos donde está. Devuelve una
        /// lista porque hay códigos de barras que comparten dos productos.
        /// </summary>
        [HttpGet]
        [Route("Productos/Buscar")]
        [ResponseType(typeof(List<ProductoAlmacenDTO>))]
        public async Task<IHttpActionResult> GetProductoPorCodigo(string codigo, string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(codigo))
            {
                return BadRequest("Falta el código de barras o el número de producto.");
            }
            List<ProductoAlmacenDTO> productos = await ubicaciones.BuscarProducto(Empresa(empresa), Almacen(almacen), codigo).ConfigureAwait(false);
            await Task.WhenAll(
                CompletarFichas(Empresa(empresa), productos),
                CompletarMismoHueco(Empresa(empresa), Almacen(almacen), productos)).ConfigureAwait(false);
            return Ok(productos);
        }

        // GET api/Almacen/Productos/8436620930427/Ficha?empresa=1   (o la referencia: Productos/18004/Ficha)
        /// <summary>
        /// Sugerencia 824 (Andre): la ficha del producto para Ariadna: foto, nombre, referencia del proveedor, familia, los códigos
        /// de barras con sus unidades y, en ALG, REI y ALC, el stock, los huecos con sus unidades y lo pendiente de entregar.
        /// Se busca por la referencia o por cualquiera de sus códigos. 404 si no hay ningún producto; 409 con los candidatos si
        /// el código lo llevan varios (la app pregunta cuál y vuelve con la referencia).
        /// </summary>
        [HttpGet]
        [Route("Productos/{codigo}/Ficha")]
        [ResponseType(typeof(FichaProductoAlmacenDTO))]
        public async Task<IHttpActionResult> GetFichaProducto(string codigo, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (fichaProducto == null)
            {
                return Content(HttpStatusCode.ServiceUnavailable, "La ficha de producto no está configurada en el servidor.");
            }
            string leido = codigo?.Trim();
            if (string.IsNullOrEmpty(leido))
            {
                return BadRequest("Falta el código de barras o la referencia del producto.");
            }
            ResultadoFichaProductoAlmacen resultado = await fichaProducto.Leer(Empresa(empresa), leido).ConfigureAwait(false);
            if (resultado.Ficha != null)
            {
                return Ok(resultado.Ficha);
            }
            if (resultado.Candidatos.Count > 0)
            {
                return Content(HttpStatusCode.Conflict, new FichaProductoAmbiguaDTO
                {
                    Message = $"El código {leido} lo llevan {resultado.Candidatos.Count} productos: elija cuál.",
                    Productos = resultado.Candidatos
                });
            }
            return Content(HttpStatusCode.NotFound, new MensajeFichaProductoDTO { Message = $"No hay ningún producto con el código o la referencia {leido}." });
        }

        // GET api/Almacen/Compras/Pendientes?almacen=ALG&empresa=1
        /// <summary>Los pedidos de compra con mercancía por recibir, el más urgente primero (NestoAPI#559).</summary>
        [HttpGet]
        [Route("Compras/Pendientes")]
        [ResponseType(typeof(List<PedidoCompraPendienteDTO>))]
        public async Task<IHttpActionResult> GetComprasPendientes(string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await compras.LeerPedidosPendientes(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false));
        }

        // GET api/Almacen/Compras/220438/Recepcion?empresa=1
        /// <summary>Lo que queda por recibir de un pedido de compra, con los códigos de barras para leerlo.</summary>
        [HttpGet]
        [Route("Compras/{pedido:int}/Recepcion")]
        [ResponseType(typeof(RecepcionCompraDTO))]
        public async Task<IHttpActionResult> GetRecepcionDeCompra(int pedido, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            RecepcionCompraDTO recepcion = await compras.LeerRecepcion(Empresa(empresa), pedido).ConfigureAwait(false);
            await CompletarCodigos(Empresa(empresa), recepcion?.Lineas).ConfigureAwait(false);
            return recepcion == null ? (IHttpActionResult)NotFound() : Ok(recepcion);
        }

        // POST api/Almacen/Compras/220438/Casar?empresa=1
        /// <summary>
        /// Compara lo contado al recibir con lo que queda por recibir del pedido: de más, de menos y
        /// productos que no se habían pedido. No guarda nada.
        /// </summary>
        [HttpPost]
        [Route("Compras/{pedido:int}/Casar")]
        [ResponseType(typeof(ResultadoRecepcionCompraDTO))]
        public async Task<IHttpActionResult> PostCasarCompra(int pedido, [FromBody] List<LecturaRecepcionDTO> lecturas,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ResultadoRecepcionCompraDTO resultado = await compras.Casar(Empresa(empresa), pedido, lecturas).ConfigureAwait(false);
            return resultado == null ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        // GET api/Almacen/Reposiciones/Pendientes?almacen=REI&empresa=1
        /// <summary>Las reposiciones que están de camino a un almacén, todavía sin dar entrada (NestoAPI#553).</summary>
        [HttpGet]
        [Route("Reposiciones/Pendientes")]
        [ResponseType(typeof(List<ReposicionPendienteDTO>))]
        public async Task<IHttpActionResult> GetReposicionesPendientes(string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await reposiciones.LeerPendientes(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false));
        }

        // GET api/Almacen/Reposiciones/80841?almacen=ALG&empresa=1
        /// <summary>Lo que trae una reposición, con los códigos de barras para leerlo al recibirla.</summary>
        [HttpGet]
        [Route("Reposiciones/{traspaso:int}")]
        [ResponseType(typeof(RecepcionReposicionDTO))]
        public async Task<IHttpActionResult> GetReposicion(int traspaso, string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            RecepcionReposicionDTO recepcion = await reposiciones
                .LeerRecepcion(Empresa(empresa), Almacen(almacen), traspaso).ConfigureAwait(false);
            await CompletarCodigos(Empresa(empresa), recepcion?.Lineas).ConfigureAwait(false);
            return recepcion == null ? (IHttpActionResult)NotFound() : Ok(recepcion);
        }

        // POST api/Almacen/Reposiciones/80841/Casar?almacen=ALG&empresa=1
        /// <summary>Compara lo contado al recibir la reposición con lo enviado. No guarda nada.</summary>
        [HttpPost]
        [Route("Reposiciones/{traspaso:int}/Casar")]
        [ResponseType(typeof(ResultadoRecepcionReposicionDTO))]
        public async Task<IHttpActionResult> PostCasarReposicion(int traspaso, [FromBody] List<LecturaRecepcionDTO> lecturas,
            string almacen = Constantes.Almacenes.ALGETE, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ResultadoRecepcionReposicionDTO resultado = await reposiciones
                .Casar(Empresa(empresa), Almacen(almacen), traspaso, lecturas).ConfigureAwait(false);
            return resultado == null ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        private static string Almacen(string almacen)
        {
            return string.IsNullOrWhiteSpace(almacen) ? Constantes.Almacenes.ALGETE : almacen.Trim().ToUpperInvariant();
        }

        private static string Empresa(string empresa)
        {
            return string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
        }

        /// <summary>Todas las líneas de un packing (de todas sus entregas y pedidos), para completar su ficha de una vez.</summary>
        private static List<LineaPackingAlmacenDTO> LineasDe(PackingAlmacenDTO packing)
        {
            return packing?.Entregas?
                .SelectMany(e => e.Pedidos ?? new List<PedidoPackingAlmacenDTO>())
                .SelectMany(p => p.Lineas ?? new List<LineaPackingAlmacenDTO>())
                .ToList();
        }

        private Task CompletarFichas(string empresa, IEnumerable<IConFichaProducto> productos)
        {
            return fichas == null || productos == null ? Task.CompletedTask : fichas.Completar(empresa, productos);
        }

        /// <summary>
        /// Ariadna#47: los demás productos con stock en el hueco de cada línea (una consulta para todos). Nunca falla: si no
        /// se puede leer, las líneas salen sin el campo.
        /// </summary>
        private Task CompletarMismoHueco(string empresa, string almacen, IEnumerable<IConProductosMismoHueco> lineas)
        {
            return fichas == null || lineas == null || string.IsNullOrWhiteSpace(almacen)
                ? Task.CompletedTask
                : fichas.CompletarMismoHueco(empresa, almacen, lineas);
        }

        /// <summary>NestoAPI#605: todos los códigos activos de cada producto, para las líneas que no llevan ficha.</summary>
        private Task CompletarCodigos(string empresa, IEnumerable<IConCodigosBarras> lineas)
        {
            return fichas == null || lineas == null ? Task.CompletedTask : fichas.CompletarCodigos(empresa, lineas);
        }

        /// <summary>El valor de una cabecera de la petición; null si no viene o está en blanco.</summary>
        private string Cabecera(string nombre)
        {
            if (Request?.Headers == null || !Request.Headers.TryGetValues(nombre, out IEnumerable<string> valores))
            {
                return null;
            }
            string valor = valores?.FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(valor) ? null : valor;
        }

        private string Usuario()
        {
            return UsuarioAuditoriaHelper.Resolver(User, null);
        }
    }

    public class PingAlmacenDTO
    {
        public DateTime HoraServidor { get; set; }
        /// <summary>El usuario del token, tal como quedará en los escaneos y las fotos.</summary>
        public string Usuario { get; set; }
        public bool FotosConfiguradas { get; set; }
    }

    public class EnlaceFotoBultoDTO
    {
        public string Url { get; set; }
        public int MinutosDeVigencia { get; set; }
    }
}
