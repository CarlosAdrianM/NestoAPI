using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Sugerencia 1478 (Manuel, 26/11/26): la referencia anterior y la siguiente de un producto, para las flechas de la ficha de
    /// Nesto. Solo lectura; aparte de GET api/Productos, como la próxima entrada (#869).
    /// </summary>
    public class VecinosProductoController : ApiController
    {
        public const string MENSAJE_SIN_PRODUCTO = "Falta el producto.";
        public const string MENSAJE_SOLO_EMPLEADOS = "Esta consulta es solo para empleados.";

        private readonly Func<ILectorVecinosProducto> crearLector;

        public VecinosProductoController()
            : this(() => LectorVecinosProductoSql.DesdeConfiguracion())
        {
        }

        internal VecinosProductoController(Func<ILectorVecinosProducto> crearLector)
        {
            this.crearLector = crearLector ?? throw new ArgumentNullException(nameof(crearLector));
        }

        // GET api/Productos/Vecinos?producto=45957&empresa=1
        /// <summary>200 {Producto, Anterior, Siguiente} (null = no hay); 400 sin producto; 404 si no existe.</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Productos/Vecinos")]
        [ResponseType(typeof(VecinosProductoDTO))]
        public IHttpActionResult GetVecinos(string producto, string empresa = null)
        {
            if (ValidadorAccesoCliente.EsClienteTienda(User))
            {
                return Content(System.Net.HttpStatusCode.Forbidden, MENSAJE_SOLO_EMPLEADOS);
            }
            if (string.IsNullOrWhiteSpace(producto))
            {
                return BadRequest(MENSAJE_SIN_PRODUCTO);
            }
            string empresaConsulta = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            VecinosProductoDTO vecinos = crearLector().Leer(empresaConsulta, producto);
            if (vecinos == null)
            {
                return NotFound();
            }
            return Ok(vecinos);
        }
    }
}
