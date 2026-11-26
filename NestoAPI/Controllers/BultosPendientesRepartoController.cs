using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Seguridad;
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
    /// NestoAPI#880 (corte 2): los bultos que faltaron en una entrega incompleta del reparto propio, con su cierre a mano. Ver
    /// <see cref="ServicioBultosPendientesReparto"/>. Ver: empleados y vendedores (403 a un cliente de la tienda); cerrar: Almacén,
    /// Dirección e Informática (decisión 9 de Carlos, 19/11/26). Sin Scripts/Issue880_RepartoBultosPendientes.sql, 503.
    /// </summary>
    [Authorize]
    public class BultosPendientesRepartoController : ApiController
    {
        private NVEntities db;

        internal Func<ServicioBultosPendientesReparto> CrearServicio { get; set; }

        public BultosPendientesRepartoController()
        {
            CrearServicio = () => new ServicioBultosPendientesReparto(new RepositorioBultosPendientesSql(), new RepositorioFurgoneta(db ?? (db = new NVEntities())));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }

        // GET api/RepartoPropio/BultosPendientes?soloPendientes=true&empresa=1&pedido=930942&envio=251099&dias=30
        [HttpGet]
        [Route("api/RepartoPropio/BultosPendientes")]
        [ResponseType(typeof(List<BultoPendienteRepartoDTO>))]
        public async Task<IHttpActionResult> GetBultosPendientes(bool soloPendientes = true, string empresa = null, int? pedido = null, int? envio = null, int dias = 30)
        {
            if (ValidadorAccesoCliente.EsClienteTienda(User))
            {
                return Error(HttpStatusCode.Forbidden, ValidadorAccesoCliente.MENSAJE_SOLO_INTERNO);
            }
            ResultadoBultosPendientes<List<BultoPendienteRepartoDTO>> resultado = await CrearServicio().Listar(new FiltroBultosPendientes
            {
                SoloPendientes = soloPendientes,
                Empresa = empresa,
                Pedido = pedido,
                Envio = envio,
                Dias = dias
            }).ConfigureAwait(true);
            return resultado.Estado == HttpStatusCode.OK ? Ok(resultado.Contenido) : Error(resultado.Estado, resultado.Mensaje);
        }

        // POST api/RepartoPropio/BultosPendientes/17/Cerrar { "Cierre": "EntregadoEnMano", "Motivo": "Se lo llevó Alfredo el jueves" }
        [HttpPost]
        [Route("api/RepartoPropio/BultosPendientes/{id:int}/Cerrar")]
        [ResponseType(typeof(BultoPendienteRepartoDTO))]
        public async Task<IHttpActionResult> PostCerrar(int id, CerrarBultoPendienteDTO peticion)
        {
            if (ValidadorAccesoCliente.EsClienteTienda(User))
            {
                return Error(HttpStatusCode.Forbidden, ValidadorAccesoCliente.MENSAJE_SOLO_INTERNO);
            }
            ResultadoBultosPendientes<BultoPendienteRepartoDTO> resultado = await CrearServicio()
                .Cerrar(id, peticion, User?.Identity?.Name, PermisosRepartoPropio.PuedeCerrarBultoPendiente(User)).ConfigureAwait(true);
            return resultado.Estado == HttpStatusCode.OK ? Ok(resultado.Contenido) : Error(resultado.Estado, resultado.Mensaje);
        }

        private IHttpActionResult Error(HttpStatusCode codigo, string mensaje)
            => Request != null ? ResponseMessage(Request.CreateErrorResponse(codigo, mensaje)) : (IHttpActionResult)Content(codigo, mensaje);
    }
}
