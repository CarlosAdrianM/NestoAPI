using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#654 (sugerencia 546): la etiqueta de código de barras de un producto (30×20 mm), en la impresora de etiquetas
    /// del usuario (ImpresoraCodBarras). La piden Nesto (ficha del producto y Recibir reposición) y, cuando haga falta, Ariadna.
    ///
    /// <para>Va en un controlador propio, SIN el filtro EscrituraSoloAlmacen del resto de api/Almacen (igual que Recepciones):
    /// la usan también las tiendas, que no son de Almacén. Imprimir no cambia datos, pero manda papel a una impresora: pueden
    /// los mismos que gestionan los códigos de barras (Compras, Almacén, Tiendas, Dirección e Informática); el resto, 403.</para>
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Almacen/EtiquetasProducto")]
    public class EtiquetasProductoController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO =
            "Para imprimir etiquetas de producto hay que ser de Compras, Almacén, Tiendas, Dirección o Informática.";

        private readonly IServicioEtiquetasProducto servicio;

        public EtiquetasProductoController(IServicioEtiquetasProducto servicio)
        {
            this.servicio = servicio;
        }

        // POST api/Almacen/EtiquetasProducto/Imprimir?empresa=1&ensayo=false   { "Producto": "17404", "Copias": 1 }
        /// <summary>
        /// Imprime las etiquetas del producto (nombre, subgrupo y tamaño; el código de barras con el número del producto y el
        /// número debajo). Con ensayo=true no imprime: dice qué llevaría y en qué impresora. La impresión la hace el servidor.
        /// 400 si el producto no existe, sobran copias o el usuario no tiene impresora; 403 sin permiso; 502 si Windows no deja
        /// imprimir.
        /// </summary>
        [HttpPost]
        [Route("Imprimir")]
        [ResponseType(typeof(ResultadoEtiquetasProductoDTO))]
        public async Task<IHttpActionResult> PostImprimir([FromBody] EtiquetasProductoDTO peticion,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool ensayo = false)
        {
            if (!ServicioCodigosBarras.PuedeEscribir(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            try
            {
                string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                return Ok(await servicio.Imprimir(empresaLimpia, peticion, UsuarioAuditoriaHelper.Resolver(User, null), ensayo).ConfigureAwait(false));
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
    }
}
