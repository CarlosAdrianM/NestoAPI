using NestoAPI.Infraestructure.PedidosVenta.Anomalias;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#715: el informe de las señales de anomalía registradas al grabar pedidos (PedidosAnomalias) y de su precisión
    /// según el etiquetado nocturno. Controlador propio para no engordar PedidosVentaController.
    /// </summary>
    [Authorize]
    public class PedidosVentaAnomaliasController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Dirección e Informática pueden ver el informe de anomalías.";

        private readonly IRepositorioAnomaliasPedido repositorio;

        public PedidosVentaAnomaliasController() : this(RepositorioAnomaliasPedidoSql.DesdeConfiguracion())
        {
        }

        internal PedidosVentaAnomaliasController(IRepositorioAnomaliasPedido repositorio)
        {
            this.repositorio = repositorio;
        }

        // GET api/PedidosVenta/Anomalias/Informe?desde=2026-10-01&hasta=2026-11-01
        /// <summary>
        /// Recuentos y precisión por regla de las filas grabadas entre <paramref name="desde"/> (incluido) y <paramref name="hasta"/>
        /// (excluido). Por defecto, los últimos 30 días. Solo Dirección e Informática (403 para el resto). Sin la tabla
        /// (Scripts/Issue715_PedidosAnomalias.sql), un informe vacío con TablaExiste = false.
        /// </summary>
        [HttpGet]
        [Route("api/PedidosVenta/Anomalias/Informe")]
        [ResponseType(typeof(InformeAnomaliasDTO))]
        public IHttpActionResult GetInforme(DateTime? desde = null, DateTime? hasta = null)
        {
            if (!PuedeVer(User))
            {
                return ResponseMessage(Request != null
                    ? Request.CreateErrorResponse(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO)
                    : new HttpResponseMessage(HttpStatusCode.Forbidden));
            }
            DateTime fin = (hasta ?? DateTime.Today.AddDays(1)).Date;
            DateTime inicio = (desde ?? fin.AddDays(-InformeAnomalias.DIAS_POR_DEFECTO)).Date;
            if (inicio >= fin)
            {
                return BadRequest("La fecha «desde» debe ser anterior a «hasta».");
            }
            InformeAnomaliasDTO informe;
            if (repositorio == null || !repositorio.TablaExiste())
            {
                informe = InformeAnomalias.Calcular(new List<FilaAnomaliaPedido>(), inicio, fin);
                informe.TablaExiste = false;
            }
            else
            {
                informe = InformeAnomalias.Calcular(repositorio.LeerEntre(inicio, fin), inicio, fin);
            }
            informe.Modo = InterruptorAnomalias.Modo.ToString();
            return Ok(informe);
        }

        internal static bool PuedeVer(IPrincipal usuario)
        {
            try
            {
                return usuario != null
                    && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));
            }
            catch
            {
                return false;
            }
        }
    }
}
