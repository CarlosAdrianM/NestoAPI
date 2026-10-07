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

        /// <param name="fichas">Familia, subgrupo, tamaño y unidad de cada producto que ve el mozo. Sin él, solo el nombre.</param>
        /// <param name="etiquetas">Imprimir etiquetas de hueco (Nesto y Ariadna).</param>
        /// <param name="cambioHueco">Ariadna#12: coger de otro hueco lo que no estaba en el de la parada.</param>
        public AlmacenController(IServicioPreparacionAlmacen servicio, IServicioUbicacionesAlmacen ubicaciones,
            IServicioRecepcionCompras compras, IServicioRecepcionReposiciones reposiciones, IFichasProductoAlmacen fichas = null,
            IServicioEtiquetasHueco etiquetas = null, IServicioCambioHuecoPicking cambioHueco = null)
        {
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
            await CompletarCodigos(Empresa(empresa), resultado?.Lineas).ConfigureAwait(false);
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
        /// </summary>
        [HttpGet]
        [Route("Recogidas/{tipo}/{numero:int}")]
        [ResponseType(typeof(RecogidaAlmacenDTO))]
        public async Task<IHttpActionResult> GetRecogida(string tipo, int numero, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
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
            await CompletarFichas(Empresa(empresa), recogida.Lineas).ConfigureAwait(false);
            return Ok(recogida);
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
        public async Task<IHttpActionResult> GetAlternativasDelPicking(int picking, string producto, string hueco = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return cambioHueco == null
                ? Ok(new List<HuecoAlternativoDTO>())
                : Ok(await cambioHueco.LeerAlternativas(Empresa(empresa), picking, producto, hueco).ConfigureAwait(false));
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
        public async Task<IHttpActionResult> PostCambiarHueco(int picking, [FromBody] CambiarHuecoPickingDTO cambio,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (cambioHueco == null)
            {
                return BadRequest("Este servidor todavía no sabe cambiar de hueco.");
            }
            ResultadoCambioHueco resultado = await cambioHueco.Cambiar(Empresa(empresa), picking, cambio, Usuario()).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoCambioHueco.Cambiado:
                    return Ok(new ResultadoCambiarHuecoDTO { Movidas = resultado.Movidas, Mensaje = resultado.Mensaje });
                case EstadoCambioHueco.NoSePuede:
                    return Content(HttpStatusCode.Conflict, resultado.Mensaje);
                default:
                    return BadRequest(resultado.Mensaje);
            }
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
        /// <summary>Lo que hay que meter en cajas de todo un picking, agrupado por entrega y por pedido.</summary>
        [HttpGet]
        [Route("Picking/{picking:int}/Packing")]
        [ResponseType(typeof(PackingAlmacenDTO))]
        public async Task<IHttpActionResult> GetPackingDelPicking(int picking, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            PackingAlmacenDTO resultado = await servicio.LeerPacking(Empresa(empresa), picking).ConfigureAwait(false);
            await CompletarFichas(Empresa(empresa), LineasDe(resultado)).ConfigureAwait(false);
            return resultado.Entregas.Count == 0 ? (IHttpActionResult)NotFound() : Ok(resultado);
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
            return Ok(await servicio.LeerPackingsPendientes(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false));
        }

        // GET api/Almacen/Pedidos/926940/Packing?empresa=1
        /// <summary>El packing de un pedido (el que se abre al leer el código del pedido en la mesa).</summary>
        [HttpGet]
        [Route("Pedidos/{pedido:int}/Packing")]
        [ResponseType(typeof(PackingAlmacenDTO))]
        public async Task<IHttpActionResult> GetPackingDelPedido(int pedido, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            PackingAlmacenDTO resultado = await servicio.LeerPackingDePedido(Empresa(empresa), pedido).ConfigureAwait(false);
            await CompletarFichas(Empresa(empresa), LineasDe(resultado)).ConfigureAwait(false);
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
            return Ok(await servicio.GuardarEscaneos(Empresa(empresa), escaneos, Usuario()).ConfigureAwait(false));
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
            await CompletarFichas(Empresa(empresa), pendiente?.Productos).ConfigureAwait(false);
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
            await CompletarFichas(Empresa(empresa), new[] { producto }).ConfigureAwait(false);
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
            await CompletarFichas(Empresa(empresa), productos).ConfigureAwait(false);
            return Ok(productos);
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

        /// <summary>NestoAPI#605: todos los códigos activos de cada producto, para las líneas que no llevan ficha.</summary>
        private Task CompletarCodigos(string empresa, IEnumerable<IConCodigosBarras> lineas)
        {
            return fichas == null || lineas == null ? Task.CompletedTask : fichas.CompletarCodigos(empresa, lineas);
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
