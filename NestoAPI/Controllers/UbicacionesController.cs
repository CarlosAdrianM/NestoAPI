using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Ubicaciones;
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
    /// NestoAPI#594 (corte 2): consultas sobre Ubicaciones (la puerta única). Solo para Almacén y Dirección (403 al resto),
    /// los mismos grupos que escriben en el almacén (EscrituraSoloAlmacen).
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Ubicaciones")]
    public class UbicacionesController : ApiController
    {
        internal const string MENSAJE_SIN_PERMISO = "El cuadre de ubicaciones es para Almacén y Dirección.";

        private readonly IServicioCuadreUbicaciones cuadre;
        private readonly NVEntities dbPropio;

        public UbicacionesController() : this(new NVEntities())
        {
        }

        private UbicacionesController(NVEntities db) : this(new ServicioCuadreUbicaciones(new RepositorioCuadreUbicacionesSql(db)))
        {
            dbPropio = db;
        }

        internal UbicacionesController(IServicioCuadreUbicaciones cuadre)
        {
            this.cuadre = cuadre ?? throw new ArgumentNullException(nameof(cuadre));
        }

        // GET api/Ubicaciones/Cuadre?almacen=ALG&producto=23101&empresa=1
        /// <summary>
        /// Cuadre de Ubicaciones contra el stock de ExtractoProducto en un almacén con control de ubicaciones (empresa y su
        /// espejo juntas). Cuenta como mercancía en el almacén lo que tiene Estado &gt;= 0: en hueco (0), pendiente de ubicar
        /// (2), reservado para picking (3), para reposición (4) y para kit (5); las negativas son histórico.
        /// <para>Con <paramref name="producto"/>: ese producto, cuadre o no (rápida). SIN producto: solo los descuadrados de
        /// TODO el almacén. Esa consulta es PESADA (recorre Ubicaciones y el extracto del almacén enteros): para fuera de
        /// horario, nunca con la gente trabajando.</para>
        /// <para>Diferencia = Ubicaciones − Stock: positiva, sobra en los huecos lo que el extracto ya no tiene; negativa, falta.</para>
        /// </summary>
        [HttpGet]
        [Route("Cuadre")]
        [ResponseType(typeof(List<CuadreUbicacionProducto>))]
        public async Task<IHttpActionResult> GetCuadre(string almacen = Constantes.Almacenes.ALGETE, string producto = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (User == null || !(User.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN) || User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            try
            {
                return Ok(await cuadre.Cuadrar(empresa, almacen, producto).ConfigureAwait(false));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dbPropio?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
