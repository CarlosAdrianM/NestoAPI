using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#559/#553: recibir mercancía con un solo contrato para todos los tipos: COMP (lo que llega de un
    /// proveedor, documento = proveedor) y REPO (una reposición entre almacenes, documento = traspaso). Igual que
    /// Recogidas/{tipo}/{numero}. Lo usan Ariadna y Nesto (por ejemplo, las tiendas para recibir sus reposiciones).
    ///
    /// <para>Va en un controlador propio, SIN el filtro EscrituraSoloAlmacen del resto de api/Almacen: aquí quién
    /// puede terminar lo decide el tipo de recepción (compras: Almacén, Compras o Dirección; reposiciones: quien tiene el
    /// almacén de destino en AlmacénPedidoVta, como hoy). Leer y comparar basta con estar identificado.</para>
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Almacen/Recepciones")]
    public class RecepcionesController : ApiController
    {
        private readonly IServicioRecepciones servicio;

        public RecepcionesController(IServicioRecepciones servicio)
        {
            this.servicio = servicio;
        }

        // GET api/Almacen/Recepciones?almacen=ALG&empresa=1
        /// <summary>Todo lo que está por recibir en el almacén, de todos los tipos, lo más antiguo primero.</summary>
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<RecepcionPendienteDTO>))]
        public async Task<IHttpActionResult> GetPendientes(string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await servicio.LeerPendientes(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false));
        }

        // GET api/Almacen/Recepciones/Buscar?codigo=8436620930427&almacen=ALG&empresa=1
        /// <summary>Lo pendiente de recibir que contiene un producto leído (hoy cada producto pendiente es de un solo proveedor).</summary>
        [HttpGet]
        [Route("Buscar")]
        [ResponseType(typeof(List<RecepcionPendienteDTO>))]
        public async Task<IHttpActionResult> GetBuscar(string codigo, string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(codigo))
            {
                return BadRequest("Falta el código de barras o el número de producto.");
            }
            return Ok(await servicio.Buscar(Empresa(empresa), Almacen(almacen), codigo.Trim()).ConfigureAwait(false));
        }

        // GET api/Almacen/Recepciones/COMP/65?almacen=ALG&empresa=1
        /// <summary>Lo que se espera recibir, con los códigos de barras para leerlo y si quien pregunta puede terminarla.</summary>
        [HttpGet]
        [Route("{tipo}/{documento}")]
        [ResponseType(typeof(RecepcionDTO))]
        public async Task<IHttpActionResult> GetRecepcion(string tipo, string documento, string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            RecepcionDTO recepcion = await servicio.LeerEsperado(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), User).ConfigureAwait(false);
            return recepcion == null ? (IHttpActionResult)NotFound() : Ok(recepcion);
        }

        // POST api/Almacen/Recepciones/COMP/65/Casar?almacen=ALG&empresa=1   [{ Producto, Cantidad }]
        /// <summary>Compara lo contado con lo esperado: de más, de menos y lo que no se esperaba. No guarda nada.</summary>
        [HttpPost]
        [Route("{tipo}/{documento}/Casar")]
        [ResponseType(typeof(ResultadoCasarRecepcionDTO))]
        public async Task<IHttpActionResult> PostCasar(string tipo, string documento, [FromBody] List<LecturaRecepcionDTO> lecturas,
            string almacen = Constantes.Almacenes.ALGETE, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ResultadoCasarRecepcionDTO resultado = await servicio
                .Casar(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), lecturas).ConfigureAwait(false);
            return resultado == null ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        // POST api/Almacen/Recepciones/COMP/65/Terminar?almacen=ALG&empresa=1   { IdRecepcion, Lecturas: [{ Producto, Cantidad }], Dispositivo }
        /// <summary>
        /// Da por recibido lo contado (todo o nada). Compras: reparte entre los pedidos del proveedor, del más
        /// antiguo al más reciente, y crea los albaranes. Reenviar el mismo IdRecepcion no recibe dos veces.
        /// 403 si quien llama no puede terminar ese tipo de recepción.
        /// </summary>
        [HttpPost]
        [Route("{tipo}/{documento}/Terminar")]
        [ResponseType(typeof(ResultadoTerminarRecepcionDTO))]
        public async Task<IHttpActionResult> PostTerminar(string tipo, string documento, [FromBody] TerminarRecepcionDTO terminar,
            string almacen = Constantes.Almacenes.ALGETE, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                return Ok(await servicio.Terminar(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), terminar, User).ConfigureAwait(false));
            }
            catch (UnauthorizedAccessException ex)
            {
                // Identificado pero sin permiso para este tipo: 403, no 401 (con 401 la app intentaría refrescar el token)
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Forbidden, ex.Message));
            }
        }

        private static string Almacen(string almacen)
        {
            return string.IsNullOrWhiteSpace(almacen) ? Constantes.Almacenes.ALGETE : almacen.Trim().ToUpperInvariant();
        }

        private static string Empresa(string empresa)
        {
            return string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
        }
    }
}
