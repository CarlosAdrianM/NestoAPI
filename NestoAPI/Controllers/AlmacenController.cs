using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
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
    /// primer día: los mozos entran como los vendedores de NestoApp (/oauth/token).</para>
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Almacen")]
    public class AlmacenController : ApiController
    {
        private readonly IServicioPreparacionAlmacen servicio;
        private readonly IServicioUbicacionesAlmacen ubicaciones;
        private readonly IServicioRecepcionCompras compras;

        public AlmacenController(IServicioPreparacionAlmacen servicio, IServicioUbicacionesAlmacen ubicaciones,
            IServicioRecepcionCompras compras)
        {
            this.servicio = servicio;
            this.ubicaciones = ubicaciones;
            this.compras = compras;
        }

        // GET api/Almacen/Picking/99633?empresa=1
        /// <summary>El recorrido del picking: un producto por hueco, ordenado para andar lo menos posible.</summary>
        [HttpGet]
        [Route("Picking/{picking:int}")]
        [ResponseType(typeof(PickingAlmacenDTO))]
        public async Task<IHttpActionResult> GetPicking(int picking, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            PickingAlmacenDTO resultado = await servicio.LeerPicking(Empresa(empresa), picking).ConfigureAwait(false);
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

        // GET api/Almacen/Picking/99633/Packing?empresa=1
        /// <summary>Lo que hay que meter en cajas de todo un picking, agrupado por entrega y por pedido.</summary>
        [HttpGet]
        [Route("Picking/{picking:int}/Packing")]
        [ResponseType(typeof(PackingAlmacenDTO))]
        public async Task<IHttpActionResult> GetPackingDelPicking(int picking, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            PackingAlmacenDTO resultado = await servicio.LeerPacking(Empresa(empresa), picking).ConfigureAwait(false);
            return resultado.Entregas.Count == 0 ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        // GET api/Almacen/Pedidos/926940/Packing?empresa=1
        /// <summary>El packing de un pedido (el que se abre al leer el código del pedido en la mesa).</summary>
        [HttpGet]
        [Route("Pedidos/{pedido:int}/Packing")]
        [ResponseType(typeof(PackingAlmacenDTO))]
        public async Task<IHttpActionResult> GetPackingDelPedido(int pedido, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            PackingAlmacenDTO resultado = await servicio.LeerPackingDePedido(Empresa(empresa), pedido).ConfigureAwait(false);
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

        // POST api/Almacen/Bultos/Foto?idCliente=...&pedido=926940&picking=99633&bulto=1
        /// <summary>
        /// La foto de un bulto antes de cerrarlo. El cuerpo de la petición es la imagen (image/jpeg),
        /// sin envoltorio. Repetir la foto de un bulto sustituye a la anterior.
        /// </summary>
        [HttpPost]
        [Route("Bultos/Foto")]
        [ResponseType(typeof(BultoAlmacenDTO))]
        public async Task<IHttpActionResult> PostFotoBulto(Guid idCliente, int pedido, int picking, int bulto,
            DateTime? fechaFoto = null, string dispositivo = null, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
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

        // GET api/Almacen/PendienteDeUbicar?almacen=ALG&empresa=1
        /// <summary>
        /// Lo recibido que todavía no tiene hueco, con la sugerencia de dónde colocarlo (donde ya hay
        /// de ese producto), ordenado para colocar andando lo menos posible.
        /// </summary>
        [HttpGet]
        [Route("PendienteDeUbicar")]
        [ResponseType(typeof(PendienteDeUbicarDTO))]
        public async Task<IHttpActionResult> GetPendienteDeUbicar(string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await ubicaciones.LeerPendienteDeUbicar(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false));
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
            return Ok(await ubicaciones.BuscarProducto(Empresa(empresa), Almacen(almacen), codigo).ConfigureAwait(false));
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

        private static string Almacen(string almacen)
        {
            return string.IsNullOrWhiteSpace(almacen) ? Constantes.Almacenes.ALGETE : almacen.Trim().ToUpperInvariant();
        }

        private static string Empresa(string empresa)
        {
            return string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
        }

        private string Usuario()
        {
            return UsuarioAuditoriaHelper.Resolver(User, null);
        }
    }

    public class EnlaceFotoBultoDTO
    {
        public string Url { get; set; }
        public int MinutosDeVigencia { get; set; }
    }
}
