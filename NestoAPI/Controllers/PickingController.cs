using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class PickingController : ApiController
    {
        private GestorPicking gestorPicking;
        // GET: api/Picking/1/654321
        [HttpGet]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPicking(string empresa, int numeroPedido)
        {
            crearModulos();
            await Task.Run(() => gestorPicking.SacarPicking(empresa, numeroPedido));

            return Ok(gestorPicking.PedidosEnPicking());
        }

        // GET: api/Picking
        [HttpGet]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPicking()
        {
            crearModulos();
            await Task.Run(() => gestorPicking.SacarPicking());

            return Ok(gestorPicking.PedidosEnPicking());
        }

        /// <summary>
        /// NestoAPI#361: disparo MANUAL del picking de CIERRE del día (el de las 11h).
        ///
        /// Su ejecución normal ya no pasa por aquí: desde la migración a Hangfire la lanza el job
        /// recurrente <c>picking-cierre-diario</c>. Este endpoint se mantiene para poder relanzarlo
        /// a mano si hiciera falta, y delega en EL MISMO método que el job, para que no haya dos
        /// implementaciones del picking de cierre que puedan divergir.
        ///
        /// A diferencia de <see cref="SacarPicking()"/>, declara su horizonte de entrega (hoy) en
        /// vez de deducirlo del reloj, y avisa por correo al almacén del resultado.
        /// </summary>
        [HttpGet]
        [Route("api/Picking/Automatico")]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPickingAutomatico()
        {
            List<PedidoPicking> pedidos = await Task.Run(
                () => Infraestructure.Picking.PickingJobsService.SacarPickingDeCierre());

            return Ok(pedidos);
        }

        private void crearModulos()
        {
            ModulosPicking modulos = new ModulosPicking();
            modulos.rellenadorPicking = new RellenadorPickingService();
            modulos.rellenadorStocks = new RellenadorStocksService();
            modulos.rellenadorUbicaciones = new RellenadorUbicacionesService();
            modulos.finalizador = new FinalizadorPicking();

            gestorPicking = new GestorPicking(modulos);
        }
    }

}