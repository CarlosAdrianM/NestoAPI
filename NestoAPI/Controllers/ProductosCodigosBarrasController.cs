using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
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
    /// NestoAPI#605: los códigos de barras de cada producto (varios por producto, con cantidad, proveedor y principal).
    /// Leer, cualquiera identificado; escribir, Compras, Almacén, Tiendas, Dirección e Informática. Lo usan Nesto (ficha
    /// del producto) y Ariadna (añadir desde el almacén el código del envase que tiene delante, con Origen Almacen).
    /// </summary>
    [Authorize]
    public class ProductosCodigosBarrasController : ApiController
    {
        private readonly IServicioCodigosBarras servicio;

        private readonly InformeRepuestosSinCodigo repuestosSinCodigo;

        /// <param name="repuestosSinCodigo">NestoAPI#762: el informe de lo repuesto sin código. Sin él, …/RepuestosSinCodigoBarras da 404.</param>
        public ProductosCodigosBarrasController(IServicioCodigosBarras servicio, InformeRepuestosSinCodigo repuestosSinCodigo = null)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
            this.repuestosSinCodigo = repuestosSinCodigo;
        }

        /// <summary>Para los tests: la fecha de hoy.</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        // GET api/Productos/RepuestosSinCodigoBarras?meses=3&empresa=1
        /// <summary>
        /// NestoAPI#762 (limpieza inicial): los productos repuestos en los últimos <paramref name="meses"/> (3 por defecto, 1-24)
        /// que no tienen ningún código de barras válido, los de más unidades primero: a esos hay que darles su código interno
        /// e imprimir sus etiquetas (POST api/Almacen/EtiquetasProducto/Imprimir con Formato Ean13).
        /// </summary>
        [HttpGet]
        [Route("api/Productos/RepuestosSinCodigoBarras")]
        [ResponseType(typeof(List<ProductoRepuestoSinCodigoDTO>))]
        public async Task<IHttpActionResult> GetRepuestosSinCodigoBarras(int meses = InformeRepuestosSinCodigo.MESES_POR_DEFECTO,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (repuestosSinCodigo == null)
            {
                return NotFound();
            }
            try
            {
                return Ok(await repuestosSinCodigo.Leer(Empresa(empresa), meses, Hoy()).ConfigureAwait(false));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // GET api/Productos/32565/CodigosBarras?empresa=1
        /// <summary>Los códigos del producto: los activos primero y, de ellos, el principal el primero.</summary>
        [HttpGet]
        [Route("api/Productos/{producto}/CodigosBarras")]
        [ResponseType(typeof(List<CodigoBarrasProductoDTO>))]
        public async Task<IHttpActionResult> GetCodigosBarras(string producto, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await servicio.Listar(Empresa(empresa), producto).ConfigureAwait(false));
        }

        // POST api/Productos/32565/CodigosBarras
        /// <summary>
        /// Añade un código al producto (201). Si ya lo tenía, 200 con la fila (reactivada si estaba de baja). Si está activo
        /// en otro producto y no viene PermitirCompartido, 409 con { Message, Productos: [ { Producto, Nombre } ] }. Si se parece
        /// mucho a uno del producto (sugerencia 822) y no viene PermitirParecido, 422 con { Message, CodigoExistente, Diferencias }.
        /// </summary>
        [HttpPost]
        [Route("api/Productos/{producto}/CodigosBarras")]
        [ResponseType(typeof(CodigoBarrasProductoDTO))]
        public async Task<IHttpActionResult> PostCodigoBarras(string producto, [FromBody] NuevoCodigoBarrasDTO nuevo)
        {
            if (!ServicioCodigosBarras.PuedeEscribir(User))
            {
                return SinPermiso();
            }
            if (nuevo == null)
            {
                return BadRequest("Falta el código de barras.");
            }
            nuevo.Empresa = Empresa(nuevo.Empresa);
            ResultadoCodigoBarras resultado = await servicio.Anadir(producto, nuevo, Usuario()).ConfigureAwait(false);
            if (resultado.Estado == EstadoOperacionCodigoBarras.Creado)
            {
                return Content(HttpStatusCode.Created, resultado.Codigo);
            }
            return Respuesta(resultado);
        }

        // POST api/Productos/32565/CodigosBarras/Interno?empresa=1
        /// <summary>
        /// NestoAPI#762: le da al producto su código de barras interno (EAN-13 que empieza por 29; ver CodigoBarrasInterno): 201
        /// con la fila si se acaba de crear, 200 si ya lo tenía. Nunca es el principal. 404 si no existe el producto.
        /// </summary>
        [HttpPost]
        [Route("api/Productos/{producto}/CodigosBarras/Interno")]
        [ResponseType(typeof(CodigoBarrasProductoDTO))]
        public async Task<IHttpActionResult> PostInterno(string producto, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!ServicioCodigosBarras.PuedeEscribir(User))
            {
                return SinPermiso();
            }
            ResultadoCodigoBarras resultado = await servicio.AsignarInterno(Empresa(empresa), producto, Usuario()).ConfigureAwait(false);
            if (resultado.Estado == EstadoOperacionCodigoBarras.Creado)
            {
                return Content(HttpStatusCode.Created, resultado.Codigo);
            }
            return Respuesta(resultado);
        }

        // PUT api/Productos/32565/CodigosBarras/12/Principal?empresa=1
        /// <summary>Lo marca como principal y lo pone en la ficha (Productos.CodBarras). El principal anterior sigue activo.</summary>
        [HttpPut]
        [Route("api/Productos/{producto}/CodigosBarras/{id:int}/Principal")]
        [ResponseType(typeof(CodigoBarrasProductoDTO))]
        public async Task<IHttpActionResult> PutPrincipal(string producto, int id, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!ServicioCodigosBarras.PuedeEscribir(User))
            {
                return SinPermiso();
            }
            return Respuesta(await servicio.MarcarPrincipal(Empresa(empresa), producto, id, Usuario()).ConfigureAwait(false));
        }

        // DELETE api/Productos/32565/CodigosBarras/12?empresa=1
        /// <summary>Lo da de baja (Activo = 0; no se borra). El principal no se puede dar de baja (400).</summary>
        [HttpDelete]
        [Route("api/Productos/{producto}/CodigosBarras/{id:int}")]
        [ResponseType(typeof(CodigoBarrasProductoDTO))]
        public async Task<IHttpActionResult> DeleteCodigoBarras(string producto, int id, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!ServicioCodigosBarras.PuedeEscribir(User))
            {
                return SinPermiso();
            }
            return Respuesta(await servicio.DarDeBaja(Empresa(empresa), producto, id, Usuario()).ConfigureAwait(false));
        }

        // GET api/Productos/PorCodigoBarras?codigo=8437017506362&empresa=1
        /// <summary>Los productos activos que tienen ese código activo (el que lo tiene de principal, el primero). Vacía si ninguno.</summary>
        [HttpGet]
        [Route("api/Productos/PorCodigoBarras")]
        [ResponseType(typeof(List<ProductoPorCodigoBarrasDTO>))]
        public async Task<IHttpActionResult> GetPorCodigoBarras(string codigo, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await servicio.BuscarPorCodigo(Empresa(empresa), codigo).ConfigureAwait(false));
        }

        private IHttpActionResult Respuesta(ResultadoCodigoBarras resultado)
        {
            switch (resultado.Estado)
            {
                case EstadoOperacionCodigoBarras.Ok:
                case EstadoOperacionCodigoBarras.Creado:
                    return Ok(resultado.Codigo);
                case EstadoOperacionCodigoBarras.NoEncontrado:
                    return Content(HttpStatusCode.NotFound, new { Message = resultado.Mensaje });
                case EstadoOperacionCodigoBarras.Compartido:
                    return Content(HttpStatusCode.Conflict, resultado.Compartido);
                case EstadoOperacionCodigoBarras.Parecido:
                    // Sugerencia 822: 422 con { Message, CodigoExistente, Diferencias }; con PermitirParecido se añade (201).
                    return Content((HttpStatusCode)422, resultado.Parecido);
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }

        private IHttpActionResult SinPermiso()
        {
            // 403 y no 401: con 401 las apps intentarían refrescar el token
            return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Forbidden, ServicioCodigosBarras.MENSAJE_SIN_PERMISO));
        }

        private string Usuario()
        {
            return UsuarioAuditoriaHelper.Resolver(User, null);
        }

        private static string Empresa(string empresa)
        {
            return string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
        }
    }
}
