using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#672: el desplazamiento de cada impresora de etiquetas (EPL) y la etiqueta de prueba para ajustarlo. La de Reina
    /// imprime corrida a la izquierda con el mismo papel que Algete y Alcobendas: con esto se corrige en la API, sin tocar
    /// código, y vale para las etiquetas de producto, de hueco y de bulto. Solo Dirección e Informática; el resto, 403.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Etiquetas")]
    public class EtiquetasImpresorasController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "El ajuste de las impresoras de etiquetas solo lo pueden tocar Dirección e Informática.";

        private readonly IServicioAjustesImpresoras servicio;

        public EtiquetasImpresorasController(IServicioAjustesImpresoras servicio)
        {
            this.servicio = servicio;
        }

        private bool PuedeAjustar() => User != null
            && (User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // GET api/Etiquetas/Ajustes
        /// <summary>Los ajustes guardados (las impresoras que no salen van sin ajuste). Vacía si aún no está la tabla.</summary>
        [HttpGet]
        [Route("Ajustes")]
        [ResponseType(typeof(List<AjusteImpresoraEtiquetasDTO>))]
        public IHttpActionResult GetAjustes()
        {
            return !PuedeAjustar() ? Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO) : (IHttpActionResult)Ok(servicio.Listar());
        }

        // GET api/Etiquetas/Ajuste?impresora=\\RDS2016\etiquetasreina
        /// <summary>El ajuste de una impresora: 0 y 0 si no tiene ninguno guardado.</summary>
        [HttpGet]
        [Route("Ajuste")]
        [ResponseType(typeof(AjusteImpresoraEtiquetasDTO))]
        public IHttpActionResult GetAjuste(string impresora = null)
        {
            if (!PuedeAjustar())
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            try
            {
                return Ok(servicio.Leer(impresora));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // PUT api/Etiquetas/Ajuste   { "Impresora": "\\\\RDS2016\\etiquetasreina", "DesplazamientoX": 24, "DesplazamientoY": 0 }
        /// <summary>
        /// Guarda el ajuste de una impresora, en puntos (8 = 1 mm): X positivo corre la etiqueta a la derecha, Y positivo hacia
        /// abajo. 0 y 0 la deja como siempre. 400 si los valores no valen; 503 si aún no está la tabla (script de #672).
        /// </summary>
        [HttpPut]
        [Route("Ajuste")]
        [ResponseType(typeof(AjusteImpresoraEtiquetasDTO))]
        public IHttpActionResult PutAjuste([FromBody] AjusteImpresoraEtiquetasDTO ajuste)
        {
            if (!PuedeAjustar())
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            try
            {
                return Ok(servicio.Guardar(ajuste, UsuarioAuditoriaHelper.Resolver(User, null)));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Content(HttpStatusCode.ServiceUnavailable, ex.Message);
            }
        }

        // POST api/Etiquetas/Prueba?empresa=1&impresora=\\RDS2016\etiquetasreina&x=24&y=0
        /// <summary>
        /// Imprime la etiqueta de prueba (marco en el borde y reglas cada milímetro) en la impresora pedida o, sin ella, en la
        /// del usuario (ImpresoraCodBarras). Con x e y, prueba esos valores sin guardarlos; sin ellos, usa el ajuste guardado.
        /// 400 si los valores no valen o el usuario no tiene impresora; 502 si Windows no deja imprimir.
        /// </summary>
        [HttpPost]
        [Route("Prueba")]
        [ResponseType(typeof(ResultadoEtiquetaPruebaDTO))]
        public IHttpActionResult PostPrueba(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string impresora = null,
            int? x = null, int? y = null)
        {
            if (!PuedeAjustar())
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            try
            {
                string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                return Ok(servicio.ImprimirPrueba(empresaLimpia, impresora, x, y, UsuarioAuditoriaHelper.Resolver(User, null)));
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
