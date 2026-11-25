using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Inmovilizados;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Etiquetas de inmovilizado (Carlos 25/11/26): tras el inventario se inmovilizan máquinas (demostración, uso propio). Se les pega
    /// una etiqueta «INMOVILIZADO · NO ES STOCK» con su código «I/1823» (y su Code128), para que nadie la cuente ni la prepare y,
    /// si se vende, se sepa qué inmovilizado poner. La consulta es de cualquier empleado; imprimir, de Administración, Dirección,
    /// Almacén, Tiendas e Informática. Un cliente de la tienda, 403.
    /// </summary>
    [Authorize]
    public class InmovilizadosController : ApiController
    {
        private readonly IRepositorioInmovilizados repositorio;
        private readonly IServicioEtiquetasInmovilizado etiquetas;

        public InmovilizadosController(IRepositorioInmovilizados repositorio, IServicioEtiquetasInmovilizado etiquetas)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.etiquetas = etiquetas ?? throw new ArgumentNullException(nameof(etiquetas));
        }

        // GET api/Inmovilizados?empresa=1&filtro=depiblend&delegacion=ALG&soloVivos=true
        /// <summary>
        /// Los inmovilizados de la empresa, los más nuevos primero (500 como mucho). <paramref name="filtro"/> busca en el número
        /// (también «I/1823»: entonces ese número exacto) y en la descripción; <paramref name="soloVivos"/> deja fuera los anulados.
        /// </summary>
        [HttpGet]
        [Route("api/Inmovilizados")]
        [ResponseType(typeof(List<InmovilizadoDTO>))]
        public async Task<IHttpActionResult> GetInmovilizados(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string filtro = null,
            string delegacion = null, bool soloVivos = true)
        {
            if (ValidadorAccesoCliente.EsClienteTienda(User))
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(ValidadorAccesoCliente.MENSAJE_SOLO_INTERNO));
            }
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            return Ok(await repositorio.Buscar(empresaLimpia, filtro, delegacion, soloVivos, ReglasInmovilizados.MAXIMO_BUSQUEDA).ConfigureAwait(false));
        }

        // POST api/Inmovilizados/Etiquetas/Imprimir   { "Empresa": "1", "Numeros": ["1823", "1822"], "Copias": 1, "Impresora": null }
        /// <summary>
        /// Imprime la etiqueta 3×2 de cada inmovilizado en la impresora pedida o en la de su delegación (ParametrosUsuario «(defecto)»
        /// ImpresoraEtiquetasInmovilizado.{ALG|REI|ALC} o la general ImpresoraEtiquetas.{…}; otra delegación, la ImpresoraCodBarras de quien imprime). 200 con Impresas y
        /// una fila por inmovilizado (Ok, Motivo); 400 sin números, más de 50 o copias fuera de 1..10; 403 sin permiso; 404 si no
        /// existe ninguno. Uno que no existe, anulado o sin impresora no para a los demás.
        /// </summary>
        [HttpPost]
        [Route("api/Inmovilizados/Etiquetas/Imprimir")]
        [ResponseType(typeof(ResultadoEtiquetasInmovilizadoDTO))]
        public async Task<IHttpActionResult> PostImprimirEtiquetas([FromBody] EtiquetasInmovilizadoDTO peticion)
        {
            if (!ReglasInmovilizados.PuedeImprimirEtiquetas(User) || ValidadorAccesoCliente.EsClienteTienda(User))
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(ReglasInmovilizados.MENSAJE_SIN_PERMISO_ETIQUETAS));
            }
            try
            {
                ResultadoEtiquetasInmovilizadoDTO resultado = await etiquetas.Imprimir(peticion, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false);
                if (resultado == null)
                {
                    return Content(HttpStatusCode.NotFound, new HttpError("No existe ninguno de los inmovilizados pedidos."));
                }
                return Ok(resultado);
            }
            catch (ArgumentException ex)
            {
                return Content(HttpStatusCode.BadRequest, new HttpError(ex.Message));
            }
        }
    }
}
