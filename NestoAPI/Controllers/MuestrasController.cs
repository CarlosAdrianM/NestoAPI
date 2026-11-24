using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#884 (Carlos 24/11/26): las muestras (subgrupo MMP) sin código de fabricante no se etiquetan unidad a unidad: una
    /// etiqueta por referencia en el cajón del mostrador (REI, ALC) o en la gaveta / el hueco (ALG) con el EAN-13 interno del 29
    /// (#762). Aquí: (a) dar el código interno en bloque, (b) imprimir las etiquetas de cajón por lotes y (d) la hoja de muestras
    /// por marca en PDF. Lo usa Nesto. Va en un controlador propio, sin el filtro EscrituraSoloAlmacen de api/Almacen (como
    /// EtiquetasProducto: lo usan también las tiendas).
    /// </summary>
    [Authorize]
    public class MuestrasController : ApiController
    {
        private readonly IServicioCodigoInternoMuestras codigoInterno;
        private readonly IServicioEtiquetasMuestras etiquetas;
        private readonly IServicioHojaMuestras hoja;

        public MuestrasController(IServicioCodigoInternoMuestras codigoInterno, IServicioEtiquetasMuestras etiquetas, IServicioHojaMuestras hoja)
        {
            this.codigoInterno = codigoInterno;
            this.etiquetas = etiquetas;
            this.hoja = hoja;
        }

        // POST api/Productos/Muestras/CodigoInterno?empresa=1&ensayo=true
        /// <summary>
        /// (a) Da el código interno del 29 a las muestras activas (MMP, Estado ≥ 0) que no tienen NINGÚN código de barras (ni en la
        /// ficha ni activo en ProductosCodigosBarras). Con ensayo=true (por defecto) no escribe: dice cuántas y cuáles (y el código
        /// que le tocaría a cada una). Con ensayo=false las da, una a una por el camino de siempre (con su bloqueo): idempotente, la
        /// que ya lo tiene no entra. Rastro: una fila en Modificaciones (Tabla CodigoInternoMuestras). Almacén, Compras, Dirección e
        /// Informática; el resto, 403.
        /// </summary>
        [HttpPost]
        [Route("api/Productos/Muestras/CodigoInterno")]
        [ResponseType(typeof(ResultadoCodigoInternoMuestrasDTO))]
        public async Task<IHttpActionResult> PostCodigoInterno(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool ensayo = true)
        {
            if (!PermisosMuestras.PuedeDarCodigoEnBloque(User))
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(PermisosMuestras.MENSAJE_SIN_PERMISO_BLOQUE));
            }
            if (codigoInterno == null)
            {
                return NotFound();
            }
            return Ok(await codigoInterno.Dar(Empresa(empresa), Usuario(), ensayo).ConfigureAwait(false));
        }

        // POST api/Almacen/EtiquetasProducto/Muestras?empresa=1&ensayo=true   { "Almacen": "REI", "Familia": "Lisap", "SoloConStock": true, "Productos": ["17404"] }
        /// <summary>
        /// (b) Las etiquetas de cajón o gaveta de las muestras de un almacén, en el orden en que se pegan: en ALG una por hueco con
        /// existencias, por el recorrido (y detrás las que no tienen hueco); en REI y ALC por familia y nombre. Filtros: familia
        /// (código o nombre), solo con stock y una lista de referencias (para reimprimir unas pocas). La etiqueta es la 3×2 de
        /// producto con otro texto: «MUESTRA · Ref. 17404» y el nombre en grande, y el EAN-13 del fabricante o el interno. Con
        /// ensayo=true (por defecto) solo dice cuántas y cuáles saldrían. De verdad, a la que no tiene código se le da el interno y se
        /// manda todo en UN trabajo a la impresora de etiquetas del usuario. 400 almacén que no vale, más de 500 o sin impresora;
        /// 403 sin permiso (los de las etiquetas de producto); 502 si Windows no deja imprimir.
        /// </summary>
        [HttpPost]
        [Route("api/Almacen/EtiquetasProducto/Muestras")]
        [ResponseType(typeof(ResultadoEtiquetasMuestrasDTO))]
        public async Task<IHttpActionResult> PostEtiquetas([FromBody] PeticionEtiquetasMuestrasDTO peticion,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool ensayo = true)
        {
            if (!PermisosMuestras.PuedeImprimir(User))
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(PermisosMuestras.MENSAJE_SIN_PERMISO));
            }
            if (etiquetas == null)
            {
                return NotFound();
            }
            try
            {
                return Ok(await etiquetas.Imprimir(Empresa(empresa), peticion ?? new PeticionEtiquetasMuestrasDTO(), Usuario(), ensayo).ConfigureAwait(false));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (ImpresionEtiquetasException ex)
            {
                ElmahHelper.Log(ex);
                return Content(HttpStatusCode.BadGateway, new HttpError(ex.Message));
            }
        }

        // GET api/Productos/Muestras/Hoja?almacen=REI&familia=Lisap&soloConStock=false&conFotos=true&empresa=1
        /// <summary>
        /// (d) La hoja de muestras (plan B del mostrador) en PDF: por familia, tres por fila, con la foto de la tienda online (si la
        /// hay y llega a tiempo), el nombre, la referencia y su EAN-13 dibujado (o «sin código»). Almacén opcional (ALG, REI, ALC),
        /// obligatorio con soloConStock. 400 si no vale; 404 con Message si no hay ninguna muestra con esos filtros; 403 sin permiso.
        /// </summary>
        [HttpGet]
        [Route("api/Productos/Muestras/Hoja")]
        public async Task<IHttpActionResult> GetHoja(string almacen = null, string familia = null, bool soloConStock = false, bool conFotos = true,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!PermisosMuestras.PuedeImprimir(User))
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(PermisosMuestras.MENSAJE_SIN_PERMISO));
            }
            if (hoja == null)
            {
                return NotFound();
            }
            HojaMuestras datos;
            try
            {
                datos = await hoja.Preparar(Empresa(empresa), almacen, familia, soloConStock, conFotos).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            if (datos.Muestras.Count == 0)
            {
                return Content(HttpStatusCode.NotFound, new HttpError("No hay ninguna muestra activa con esos filtros."));
            }
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(hoja.GenerarPdf(datos)) };
            respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            respuesta.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
            {
                FileName = NombreFichero(datos)
            };
            return ResponseMessage(respuesta);
        }

        /// <summary>«Muestras Lisap REI.pdf», sin caracteres que no valen en un nombre de fichero.</summary>
        internal static string NombreFichero(HojaMuestras datos)
        {
            string nombre = string.Join(" ", new[] { "Muestras", datos?.Familia, datos?.Almacen }.Where(t => !string.IsNullOrWhiteSpace(t)));
            char[] prohibidos = System.IO.Path.GetInvalidFileNameChars().Concat(new[] { '"' }).ToArray();
            return new string(nombre.Where(c => !prohibidos.Contains(c) && c < 128).ToArray()).Trim() + ".pdf";
        }

        private string Usuario() => UsuarioAuditoriaHelper.Resolver(User, null);

        private static string Empresa(string empresa)
            => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
