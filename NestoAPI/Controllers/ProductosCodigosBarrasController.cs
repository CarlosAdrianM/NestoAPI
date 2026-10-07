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

        public ProductosCodigosBarrasController(IServicioCodigosBarras servicio)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
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
        /// en otro producto y no viene PermitirCompartido, 409 con { Message, Productos: [ { Producto, Nombre } ] }.
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
